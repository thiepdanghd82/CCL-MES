using System.Net;
using System.Net.Http.Json;
using CCL.MES.Hybrid.Client.Tests._Support;
using CCL.MES.Shared.WorkOrders;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7a-1.3 — verify the CLIENT side of the
/// <c>If-Match + Idempotency-Key + ETag</c> contract directly against
/// <see cref="CclApiClient.AdvanceWorkOrderAsync"/>. These tests
/// REPLACE the Safari-DevTools-driven items 2-4 of the original
/// PR #101 Catalyst checkpoint: they assert what bytes the client
/// puts on the wire using the same production code path the MAUI
/// app calls.
///
/// Strategy: stub HttpMessageHandler, snapshot request headers per
/// call, assert what the real <see cref="CclApiClient"/> sent.
/// </summary>
public sealed class CclApiClientAdvanceContractTests
{
    // ── Item 2 (was DevTools) — If-Match header sent ─────────────────

    [Fact]
    public async Task Advance_sends_If_Match_header_from_caller_supplied_etag()
    {
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.OK, new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "OpSetting", ETag = "NEW_ETAG_X="
            }));

        await client.AdvanceWorkOrderAsync(workOrderId: 42, ifMatchETag: "OLD_ETAG_A=");

        var req = stub.Requests[0];
        Assert.True(req.Headers.Contains("If-Match"), "If-Match header missing — server would 428.");
        var ifMatch = string.Join(",", req.Headers.GetValues("If-Match"));
        // Canonical RFC 7232 form: ETag value wrapped in double quotes.
        Assert.Equal("\"OLD_ETAG_A=\"", ifMatch);
    }

    // ── Item 4 (was DevTools) — Idempotency-Key sent + fresh per call ─

    [Fact]
    public async Task Advance_sends_fresh_Idempotency_Key_per_call()
    {
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.OK, new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "OpSetting", ETag = "RV1"
            }));

        await client.AdvanceWorkOrderAsync(1, "RV0");
        await client.AdvanceWorkOrderAsync(1, "RV0");
        await client.AdvanceWorkOrderAsync(1, "RV0");

        Assert.Equal(3, stub.Requests.Count);
        var keys = stub.Requests
            .Select(r => string.Join(",", r.Headers.GetValues("Idempotency-Key")))
            .ToList();

        // Every key MUST be present + parseable as a v4 UUID.
        Assert.All(keys, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k), "Idempotency-Key missing — server would 400.");
            Assert.True(Guid.TryParse(k, out var parsed), $"Key '{k}' is not a UUID.");
            // UUID v4 has high-nibble of byte 6 = 4 (per RFC 4122).
            var bytes = parsed.ToByteArray();
            Assert.Equal(0x40, bytes[7] & 0xF0); // .NET endian quirk: byte 6 lives at [7]
        });

        // Every key MUST be distinct — fresh UUID per intent.
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ── Item 3 (was DevTools) — new ETag captured from 200 response ─

    [Fact]
    public async Task Advance_captures_new_ETag_from_200_response_body()
    {
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.OK, new AdvanceWorkOrderResponse
            {
                Ok = true,
                CurrentStep = "Running",
                ETag = "BUMPED_AFTER_TRIGGER=",
            }));

        var result = await client.AdvanceWorkOrderAsync(99, "OLD_ETAG=");

        Assert.True(result.Ok);
        Assert.Equal("Running", result.CurrentStep);
        Assert.Equal("BUMPED_AFTER_TRIGGER=", result.ETag);
        Assert.NotEqual("OLD_ETAG=", result.ETag);
    }

    // ── Item 5 (was app conflict observation) — 409 flow ──────────────

    [Fact]
    public async Task Advance_returns_409_body_without_throwing()
    {
        // Mirrors the production server behavior: 409 Conflict carries an
        // AdvanceWorkOrderResponse with ErrorCode=wo.state_conflict + the
        // server's CURRENT ETag so the caller can adopt + retry.
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.Conflict, new AdvanceWorkOrderResponse
            {
                Ok = false,
                CurrentStep = "PrePressCheck",
                ErrorCode = "wo.state_conflict",
                ETag = "SERVER_CURRENT_ETAG=",
            }));

        // Should NOT throw — must return the body so the Razor caller
        // can adopt the new ETag for retry without a separate Summary GET.
        var result = await client.AdvanceWorkOrderAsync(7, "STALE_ETAG=");

        Assert.False(result.Ok);
        Assert.Equal("wo.state_conflict", result.ErrorCode);
        Assert.Equal("SERVER_CURRENT_ETAG=", result.ETag);
    }

    // ── 428 / 422 / 400 — throw ApiException for the central VN mapper ─

    [Theory]
    [InlineData((int)HttpStatusCode.PreconditionRequired, "wo.if_match_required")]
    [InlineData((int)HttpStatusCode.BadRequest, "wo.idempotency_key_required")]
    [InlineData((int)HttpStatusCode.UnprocessableEntity, "idempotency.replay_mismatch")]
    public async Task Advance_throws_ApiException_for_non_OK_non_409_so_VN_mapper_takes_over(
        int statusCode, string errorCode)
    {
        var (client, stub) = Build();
        stub.Responder = (_, _) =>
        {
            var body = new { code = errorCode, messageEn = $"server says {errorCode}" };
            return Task.FromResult(StubHttpHandler.Json((HttpStatusCode)statusCode, body));
        };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            client.AdvanceWorkOrderAsync(1, "any"));

        Assert.Equal(statusCode, ex.StatusCode);
        Assert.Equal(errorCode, ex.ApiError.Code);
    }

    // ── X-Device-Id pre-existing header still flows through ──────────

    [Fact]
    public async Task Advance_still_sends_X_Device_Id_when_configured()
    {
        var (client, stub) = Build(deviceId: "01-cafe-cafe-cafe");
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.OK, new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "Running", ETag = "RV1"
            }));

        await client.AdvanceWorkOrderAsync(1, "RV0");

        var req = stub.Requests[0];
        Assert.True(req.Headers.Contains("X-Device-Id"));
        Assert.Equal("01-cafe-cafe-cafe",
            string.Join(",", req.Headers.GetValues("X-Device-Id")));
        // All three headers coexist — no regression of P10.3 W4 plumbing.
        Assert.True(req.Headers.Contains("If-Match"));
        Assert.True(req.Headers.Contains("Idempotency-Key"));
    }

    // ── helper ───────────────────────────────────────────────────────

    private static (CclApiClient client, StubHttpHandler stub) Build(string? deviceId = null)
    {
        var stub = new StubHttpHandler();
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://localhost:5100") };
        var opts = Options.Create(new ApiClientOptions
        {
            BaseUrl = "http://localhost:5100",
            DeviceId = deviceId,
        });
        var client = new CclApiClient(http, opts);
        return (client, stub);
    }
}
