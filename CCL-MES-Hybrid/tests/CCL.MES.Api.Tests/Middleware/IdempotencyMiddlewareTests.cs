using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests.Middleware;

/// <summary>
/// P10.7a-1.2 — end-to-end coverage of
/// <see cref="CCL.MES.Api.Middleware.IdempotencyMiddleware"/>
/// per contract §6.2. Tests exercise the live HTTP pipeline against
/// <c>POST /api/v2/work-orders/{id}/advance</c> (the only mutating
/// endpoint currently shipped — PR 7a-1.3 will retrofit it to
/// REQUIRE the header; until then the header is opt-in and the
/// middleware passes through when absent).
///
/// 12 scenarios — names map 1:1 to breakdown §4.4 + the §6.2
/// behaviour table.
/// </summary>
public sealed class IdempotencyMiddlewareTests : IClassFixture<MesApiFactory>, IAsyncLifetime
{
    private readonly MesApiFactory _factory;
    private HttpClient _client = null!;
    private long _woId;
    private string _woNo = "";
    private long _actorId;
    private string _username = null!;

    public IdempotencyMiddlewareTests(MesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        // Per-test unique username so the factory's shared SQLite file
        // doesn't trip the Users.Username unique index across tests.
        // The IdempotencyKey natural key is (KeyValue, ActorId), so this
        // also gives every test its own actor namespace for assertions.
        _username = $"idem-{Guid.NewGuid():N}".Substring(0, 16);
        _client = _factory.CreateClient();
        var admin = await _factory.SeedUserAsync(_username, "pass1234", "Admin");
        _actorId = admin.Id;
        await _factory.LoginAndAuthenticateAsync(_client, _username, "pass1234");

        // Seed Customer + Product + ProductRevision + WO. Foreign keys
        // from WorkOrder require all three master rows to exist.
        // PrePressCheck → OpSetting needs ProductRevisionId not null +
        // MaterialsReady = true (per legacy CanAdvance guard).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-IDEM-FIX", Name = "idem-fixture-customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = "P-IDEM-FIX", Name = "idem-fixture-product", CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var rev = new ProductRevision
        {
            ProductId = product.Id,
            RevisionCode = "A",
            SpecCode = $"SP-IDEM-{Guid.NewGuid():N}".Substring(0, 14),
            Title = "idem-fixture-rev",
            Status = ProductRevisionStatus.Approved,
        };
        db.ProductRevisions.Add(rev);
        await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = $"WO-IDEM-{Guid.NewGuid():N}".Substring(0, 16),
            ProductRevisionId = rev.Id,
            MaterialsReady = true,
            CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = "PREPRESS",
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        _woId = wo.Id;
        _woNo = wo.WoNo;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── 1. Passthrough — no header → middleware does nothing ─────────

    [Fact]
    public async Task Mutating_post_without_header_passes_through_to_controller()
    {
        // P10.7a-1.3 — /advance also requires If-Match. The middleware
        // passes through when there's no Idempotency-Key — the
        // controller-level Idempotency-Key requirement (400) lands
        // AFTER the middleware decision; either way no ledger row gets
        // inserted, which is what this test asserts.
        var etag = await CurrentEtagAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{_woId}/advance");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{etag}\"");
        var rsp = await _client.SendAsync(req);

        // Controller rejects with 400 because Idempotency-Key is required
        // on /advance per the 7a-1.3 retrofit. The status code is not the
        // assertion target — what matters is no ledger row was inserted.
        Assert.False(rsp.Headers.Contains("Idempotency-Replayed"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(0, await db.IdempotencyKeys.CountAsync(k => k.ActorId == _actorId));
    }

    // ── 2. Passthrough — GET with header → middleware skips ──────────

    [Fact]
    public async Task Get_with_header_skipped_because_GET_is_not_mutating()
    {
        // Use the DTO-returning summary endpoint — the entity-returning
        // /work-orders/{id} endpoint has a serializer cycle bug
        // (Customer.Products.Customer...) unrelated to this middleware.
        using var scope0 = _factory.Services.CreateScope();
        var db0 = scope0.ServiceProvider.GetRequiredService<MesDbContext>();
        var woNo = (await db0.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == _woId)).WoNo;

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/work-orders/by-no/{woNo}/summary");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var rsp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(0, await db.IdempotencyKeys.CountAsync(k => k.ActorId == _actorId));
    }

    // ── 3. First time — header inserts ledger row ────────────────────

    [Fact]
    public async Task First_request_with_key_inserts_ledger_row_and_executes_downstream()
    {
        var key = Guid.NewGuid().ToString();
        var rsp = await PostAdvanceAsync(key);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body1 = await rsp.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":true", body1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.IdempotencyKeys.SingleAsync(k => k.KeyValue == key);
        Assert.NotNull(row.CompletedAtUtc);
        Assert.Equal(200, row.ResponseStatus);
        Assert.Contains("\"ok\":true", row.ResponseBody);
    }

    // ── 4. Replay same key + same body → identical response, no re-exec ─

    [Fact]
    public async Task Replay_same_key_same_body_returns_stored_response_without_re_executing()
    {
        var key = Guid.NewGuid().ToString();

        var rsp1 = await PostAdvanceAsync(key);
        var body1 = await rsp1.Content.ReadAsStringAsync();

        // Read CurrentStep right after first call.
        var stepAfterFirst = await ReadCurrentStepAsync();

        var rsp2 = await PostAdvanceAsync(key);
        var body2 = await rsp2.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, rsp2.StatusCode);
        Assert.Equal(body1, body2);
        // Replay marker present on the second response.
        Assert.True(rsp2.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal("true", rsp2.Headers.GetValues("Idempotency-Replayed").Single());

        // Crucially: CurrentStep did NOT advance twice. State machine
        // fired exactly once.
        var stepAfterSecond = await ReadCurrentStepAsync();
        Assert.Equal(stepAfterFirst, stepAfterSecond);
    }

    // ── 5. Replay same key + DIFFERENT body → 422 + audit ────────────

    [Fact]
    public async Task Replay_same_key_different_body_returns_422_and_emits_audit()
    {
        var key = Guid.NewGuid().ToString();
        await PostAdvanceWithBodyAsync(key, "{}");

        var rsp = await PostAdvanceWithBodyAsync(key, "{\"diff\":1}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadAsStringAsync();
        Assert.Contains("idempotency.replay_mismatch", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var audit = await db.AuditLogs
            .Where(a => a.Action == "IDEMPOTENCY_REPLAY")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains(key, audit!.Detail);
    }

    // ── 6. Stored response retains its content-type ──────────────────

    [Fact]
    public async Task Stored_content_type_round_trips_to_replay()
    {
        var key = Guid.NewGuid().ToString();
        var rsp1 = await PostAdvanceAsync(key);
        var ct1 = rsp1.Content.Headers.ContentType?.MediaType;

        var rsp2 = await PostAdvanceAsync(key);
        var ct2 = rsp2.Content.Headers.ContentType?.MediaType;

        Assert.NotNull(ct1);
        Assert.Equal(ct1, ct2);
    }

    // ── 7. Two different actors can reuse the same key ───────────────

    [Fact]
    public async Task Same_key_different_actor_is_allowed()
    {
        var key = Guid.NewGuid().ToString();
        await PostAdvanceAsync(key);

        // Spin up a second client + different user.
        var client2 = _factory.CreateClient();
        var otherUsername = $"oth-{Guid.NewGuid():N}".Substring(0, 16);
        await _factory.SeedUserAsync(otherUsername, "pass1234", "Admin");
        await _factory.LoginAndAuthenticateAsync(client2, otherUsername, "pass1234");

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{_woId}/advance");
        req.Headers.Add("Idempotency-Key", key);
        var rsp = await client2.SendAsync(req);

        // No 409 — the (key, actor) tuple is the natural-key. Different
        // actor → fresh row. (May 200 or domain-guard 200 depending on
        // current WO state; what matters is no replay collision.)
        Assert.False(rsp.Headers.Contains("Idempotency-Replayed"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rows = await db.IdempotencyKeys.Where(k => k.KeyValue == key).ToListAsync();
        Assert.Equal(2, rows.Count); // one per actor
    }

    // ── 8. Header > 64 chars → 400 ───────────────────────────────────

    [Fact]
    public async Task Key_longer_than_64_chars_rejected_400()
    {
        var key = new string('x', 65);
        var rsp = await PostAdvanceAsync(key);

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var body = await rsp.Content.ReadAsStringAsync();
        Assert.Contains("idempotency.key_too_long", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Empty(await db.IdempotencyKeys.Where(k => k.ActorId == _actorId).ToListAsync());
    }

    // ── 9. Response body byte-equal across replays (envelope intact) ─

    [Fact]
    public async Task Replay_response_body_is_byte_equal_to_original()
    {
        var key = Guid.NewGuid().ToString();
        var bytes1 = await (await PostAdvanceAsync(key)).Content.ReadAsByteArrayAsync();
        var bytes2 = await (await PostAdvanceAsync(key)).Content.ReadAsByteArrayAsync();

        Assert.Equal(bytes1, bytes2);
    }

    // ── 10. ExpiresAtUtc = CreatedAtUtc + TtlHours ───────────────────

    [Fact]
    public async Task Ledger_row_expires_at_created_plus_ttl_hours()
    {
        var key = Guid.NewGuid().ToString();
        await PostAdvanceAsync(key);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.IdempotencyKeys.SingleAsync(k => k.KeyValue == key);
        var delta = row.ExpiresAtUtc - row.CreatedAtUtc;

        // Default TTL = 24h. Tolerance ±1s for any clock skew during insert.
        Assert.InRange(delta.TotalHours, 23.99, 24.01);
    }

    // ── 11. ResponseStatus stored matches actual HTTP status ─────────

    [Fact]
    public async Task Stored_status_matches_actual_response_status()
    {
        var key = Guid.NewGuid().ToString();
        var rsp = await PostAdvanceAsync(key);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.IdempotencyKeys.SingleAsync(k => k.KeyValue == key);
        Assert.Equal((int)rsp.StatusCode, row.ResponseStatus);
    }

    // ── 12. Replay does NOT touch downstream services ────────────────

    [Fact]
    public async Task Replay_does_not_emit_a_second_WO_ADVANCE_audit_row()
    {
        var key = Guid.NewGuid().ToString();
        await PostAdvanceAsync(key);

        // Count WO_ADVANCE audit rows after first call.
        int firstCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            firstCount = await db.AuditLogs.CountAsync(a =>
                a.Action == "WO_ADVANCE" &&
                a.TargetId == _woId.ToString());
        }

        // Replay — downstream should NOT execute, so no new WO_ADVANCE.
        await PostAdvanceAsync(key);

        int secondCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            secondCount = await db.AuditLogs.CountAsync(a =>
                a.Action == "WO_ADVANCE" &&
                a.TargetId == _woId.ToString());
        }

        Assert.Equal(firstCount, secondCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostAdvanceAsync(string key)
    {
        // P10.7a-1.3 — /advance now REQUIRES If-Match. Read current
        // ETag from summary on every call so replay tests still hit
        // the same key (server's stored response from the first call
        // is what we're verifying gets replayed).
        var etag = await CurrentEtagAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{_woId}/advance");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{etag}\"");
        req.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PostAdvanceWithBodyAsync(string key, string body)
    {
        var etag = await CurrentEtagAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{_woId}/advance");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{etag}\"");
        req.Headers.Add("Idempotency-Key", key);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _client.SendAsync(req);
    }

    private async Task<string> CurrentEtagAsync()
    {
        var summary = await _client.GetFromJsonAsync<WorkOrderSummary>(
            $"/api/v2/work-orders/by-no/{Uri.EscapeDataString(_woNo)}/summary");
        return summary?.ETag ?? "";
    }

    private async Task<string> ReadCurrentStepAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var wo = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == _woId);
        return wo.CurrentStep.ToString();
    }
}
