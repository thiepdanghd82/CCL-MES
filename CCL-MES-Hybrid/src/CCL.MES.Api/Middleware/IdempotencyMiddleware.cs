using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCL.MES.Api.Audit;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCL.MES.Api.Middleware;

/// <summary>
/// P10.7a-1.2 — HTTP middleware enforcing the
/// <c>Idempotency-Key</c> contract per
/// <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §6.2.
///
/// Activates ONLY when:
///   - HTTP verb is mutating (POST / PUT / PATCH / DELETE), AND
///   - the request carries an <c>Idempotency-Key</c> header.
/// All other requests pass straight through.
///
/// On the first request with a given <c>(KeyValue, ActorId)</c>:
///   1. Insert an in-flight ledger row.
///   2. Call the rest of the pipeline; buffer the response stream.
///   3. Persist <c>(ResponseStatus, ResponseBody, ContentType)</c>
///      + set <c>CompletedAtUtc</c>.
///   4. Flush the buffered response back to the client.
///
/// On replay (same <c>KeyValue + ActorId</c>):
///   - If the original is still in-flight → 409 Conflict.
///   - If the stored <c>BodySha256</c> matches → return the stored
///     response verbatim. No downstream execution. No audit row.
///   - If the stored <c>BodySha256</c> differs → 422 Unprocessable
///     Entity + emit <c>IDEMPOTENCY_REPLAY</c> audit row (operator
///     UI bug — same key sent with different request body).
///
/// Race condition (two concurrent first-time requests with the
/// same key): the second hits the UNIQUE index on
/// <c>(KeyValue, ActorId)</c>, the middleware catches the
/// <see cref="DbUpdateException"/> and returns 409 — same outcome
/// as the in-flight collision path.
///
/// PR 7a-1.3 will retrofit <c>POST /work-orders/{id}/advance</c>
/// to mark <c>Idempotency-Key</c> as REQUIRED. Until then, this
/// middleware is opt-in (header presence triggers it).
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _log;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(
        HttpContext ctx,
        IMesDbContext db,
        IAuditWriter audit,
        IOptions<IdempotencyOptions> opts)
    {
        if (!IsMutatingVerb(ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        var keyValue = ctx.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(keyValue))
        {
            await _next(ctx);
            return;
        }

        if (keyValue.Length > 64)
        {
            await WriteProblemAsync(ctx, StatusCodes.Status400BadRequest,
                "idempotency.key_too_long",
                "Idempotency-Key header must be ≤ 64 characters.");
            return;
        }

        var actorId = ExtractActorId(ctx.User);
        var endpoint = ctx.Request.Path.Value ?? "";
        var bodyBytes = await ReadRequestBodyAsync(ctx.Request, opts.Value.MaxRequestBodyBytes);
        if (bodyBytes is null)
        {
            await WriteProblemAsync(ctx, StatusCodes.Status413PayloadTooLarge,
                "idempotency.body_too_large",
                $"Request body exceeded {opts.Value.MaxRequestBodyBytes} bytes.");
            return;
        }
        var bodySha = ComputeSha256Hex(bodyBytes);

        var existing = await db.IdempotencyKeys
            .FirstOrDefaultAsync(k => k.KeyValue == keyValue && k.ActorId == actorId);

        if (existing is not null)
        {
            if (existing.CompletedAtUtc is null)
            {
                await WriteProblemAsync(ctx, StatusCodes.Status409Conflict,
                    "idempotency.in_flight",
                    "An earlier request with this Idempotency-Key is still in progress. Retry shortly.");
                return;
            }

            if (!string.Equals(existing.BodySha256, bodySha, StringComparison.Ordinal))
            {
                // Replay-with-different-body: UI bug. Audit + 422.
                var actor = ctx.User?.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
                var role  = ctx.User?.FindFirstValue(ClaimTypes.Role) ?? "";
                var detail = JsonSerializer.Serialize(new
                {
                    key = keyValue,
                    endpoint_path = endpoint,
                    expected_body_sha = existing.BodySha256,
                    received_body_sha = bodySha,
                    actor_id = actorId,
                });
                await audit.EmitAsync(
                    AuditAction.IdempotencyReplay, actor, role,
                    targetType: "IdempotencyKey",
                    targetId: existing.Id.ToString(),
                    detail: detail);

                await WriteProblemAsync(ctx, StatusCodes.Status422UnprocessableEntity,
                    "idempotency.replay_mismatch",
                    "Replay attempted with a different request body.");
                return;
            }

            await ReplayStoredResponseAsync(ctx, existing);
            return;
        }

        // First time — insert in-flight row.
        var now = DateTime.UtcNow;
        var row = new IdempotencyKey
        {
            KeyValue = keyValue,
            ActorId = actorId,
            EndpointPath = endpoint,
            BodySha256 = bodySha,
            ResponseStatus = 0,
            ResponseBody = "",
            ResponseContentType = "",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(opts.Value.TtlHours),
        };
        db.IdempotencyKeys.Add(row);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // UNIQUE index race — another concurrent request just inserted
            // the same (KeyValue, ActorId). Treat as in-flight collision.
            _log.LogDebug(ex, "Idempotency race: concurrent insert for key {Key}", keyValue);
            await WriteProblemAsync(ctx, StatusCodes.Status409Conflict,
                "idempotency.in_flight",
                "Concurrent request with the same Idempotency-Key already in progress.");
            return;
        }

        // Buffer the downstream response.
        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await _next(ctx);
        }
        catch
        {
            // Restore body + don't store partial response. TTL will
            // sweep the in-flight row.
            ctx.Response.Body = originalBody;
            throw;
        }

        // Persist response (truncate over the cap).
        buffer.Position = 0;
        var maxBytes = opts.Value.MaxStoredResponseBytes;
        var responseBytes = buffer.ToArray();
        var bodyToStore = responseBytes.Length > maxBytes
            ? Encoding.UTF8.GetString(responseBytes, 0, maxBytes)
            : Encoding.UTF8.GetString(responseBytes);

        row.ResponseStatus = ctx.Response.StatusCode;
        row.ResponseBody = bodyToStore;
        row.ResponseContentType = ctx.Response.ContentType ?? "";
        row.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Flush real body.
        ctx.Response.Body = originalBody;
        await ctx.Response.Body.WriteAsync(responseBytes);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static bool IsMutatingVerb(string method) =>
        method.Equals("POST",   StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PUT",    StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PATCH",  StringComparison.OrdinalIgnoreCase) ||
        method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);

    private static long ExtractActorId(ClaimsPrincipal? user)
    {
        // Auth layer puts the user's PK in NameIdentifier; if anon, fall
        // back to 0 so the (KeyValue, ActorId) unique index doesn't
        // wedge on a NULL. Anon requests on mutating endpoints are
        // already rejected by FallbackPolicy in Program.cs — this is
        // defence in depth.
        var sub = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(sub, out var id) ? id : 0L;
    }

    private static async Task<byte[]?> ReadRequestBodyAsync(HttpRequest req, int maxBytes)
    {
        req.EnableBuffering();
        // The body length may be unknown if Content-Length is missing
        // (chunked transfer); read into a bounded MemoryStream so a
        // hostile client can't OOM us with a huge body.
        var ms = new MemoryStream();
        var buffer = new byte[8 * 1024];
        int read;
        while ((read = await req.Body.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            if (ms.Length + read > maxBytes)
                return null;
            ms.Write(buffer, 0, read);
        }
        req.Body.Position = 0;
        return ms.ToArray();
    }

    private static string ComputeSha256Hex(byte[] payload)
    {
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteProblemAsync(HttpContext ctx, int status, string code, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "about:blank",
            title = code,
            status,
            detail,
        };
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }

    private static async Task ReplayStoredResponseAsync(HttpContext ctx, IdempotencyKey row)
    {
        ctx.Response.StatusCode = row.ResponseStatus;
        if (!string.IsNullOrEmpty(row.ResponseContentType))
            ctx.Response.ContentType = row.ResponseContentType;
        // Signal replay so clients debugging double-tap can see it.
        ctx.Response.Headers["Idempotency-Replayed"] = "true";
        if (!string.IsNullOrEmpty(row.ResponseBody))
            await ctx.Response.WriteAsync(row.ResponseBody);
    }
}
