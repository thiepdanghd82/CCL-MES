using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7b-3 — wire-mirror coverage for GET /api/v2/reason-codes used
/// by the PREPRESS dashboard NG picker. Closes LESSONS-LEARNED.md L17
/// (seed early-exit guards skip kind-specific data) by asserting that
/// even on a DB pre-populated with Recovery codes the Scrap codes are
/// still listed.
/// </summary>
public sealed class ReasonCodesControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public ReasonCodesControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> OperatorClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task SeedScrapReasonAsync(string code, string labelVi, int sort = 10)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
        {
            db.ReasonCodes.Add(new ReasonCode
            {
                Code = code, LabelEn = code, LabelVi = labelVi,
                Kind = ReasonCodeKind.Scrap, Sort = sort,
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedRecoveryReasonAsync(string code)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
        {
            db.ReasonCodes.Add(new ReasonCode
            {
                Code = code, LabelEn = code, LabelVi = code,
                Kind = ReasonCodeKind.Recovery, Sort = 10,
            });
            await db.SaveChangesAsync();
        }
    }

    // ── Auth gate ───────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.GetAsync("/api/v2/reason-codes?kind=Scrap");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Filter by kind ──────────────────────────────────────────────

    [Fact]
    public async Task Kind_Scrap_returns_only_Scrap_codes()
    {
        await SeedScrapReasonAsync("SC-FOO", "Foo");
        await SeedRecoveryReasonAsync("REC-IGNORED");

        var client = await OperatorClientAsync("op-rc-scrap");
        var resp = await client.GetAsync("/api/v2/reason-codes?kind=Scrap");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await resp.Content.ReadFromJsonAsync<List<ReasonCodeOption>>();
        Assert.NotNull(rows);
        Assert.NotEmpty(rows!);
        Assert.All(rows!, r => Assert.Equal("Scrap", r.Kind));
        Assert.Contains(rows!, r => r.Code == "SC-FOO" && r.LabelVi == "Foo");
        Assert.DoesNotContain(rows!, r => r.Code == "REC-IGNORED");
    }

    [Fact]
    public async Task Kind_Recovery_returns_only_Recovery_codes()
    {
        await SeedScrapReasonAsync("SC-NOT-ME", "Should not show");
        await SeedRecoveryReasonAsync("REC-WANTED");

        var client = await OperatorClientAsync("op-rc-rec");
        var resp = await client.GetAsync("/api/v2/reason-codes?kind=Recovery");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await resp.Content.ReadFromJsonAsync<List<ReasonCodeOption>>();
        Assert.NotNull(rows);
        Assert.All(rows!, r => Assert.Equal("Recovery", r.Kind));
        Assert.Contains(rows!, r => r.Code == "REC-WANTED");
        Assert.DoesNotContain(rows!, r => r.Code == "SC-NOT-ME");
    }

    [Fact]
    public async Task Kind_filter_is_case_insensitive()
    {
        await SeedScrapReasonAsync("SC-CASE", "case test");

        var client = await OperatorClientAsync("op-rc-case");
        var resp = await client.GetAsync("/api/v2/reason-codes?kind=SCRAP");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await resp.Content.ReadFromJsonAsync<List<ReasonCodeOption>>();
        Assert.Contains(rows!, r => r.Code == "SC-CASE");
    }

    [Fact]
    public async Task Omit_kind_returns_codes_from_every_kind()
    {
        await SeedScrapReasonAsync("SC-MIX", "mix");
        await SeedRecoveryReasonAsync("REC-MIX");

        var client = await OperatorClientAsync("op-rc-mix");
        var resp = await client.GetAsync("/api/v2/reason-codes");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await resp.Content.ReadFromJsonAsync<List<ReasonCodeOption>>();
        Assert.Contains(rows!, r => r.Code == "SC-MIX");
        Assert.Contains(rows!, r => r.Code == "REC-MIX");
    }

    // ── Invalid filter ─────────────────────────────────────────────

    [Fact]
    public async Task Unknown_kind_returns_422_invalid_kind()
    {
        var client = await OperatorClientAsync("op-rc-bad");
        var resp = await client.GetAsync("/api/v2/reason-codes?kind=NotAKind");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.NotNull(err);
        Assert.Equal("reason_codes.invalid_kind", err!.Code);
    }

    // ── L17 regression guard ────────────────────────────────────────
    // Seed early-exit guards (db.X.AnyAsync()) skipped kind-specific
    // data when a different kind was already present. After the fix
    // (per-kind idempotency in SeedReasonCodesAsync) a DB pre-populated
    // with Recovery codes still surfaces seeded Scrap codes via the
    // wire endpoint that the PREPRESS picker calls.

    [Fact]
    public async Task L17_regression_Recovery_present_does_not_block_Scrap_listing()
    {
        await SeedRecoveryReasonAsync("REC-PRE-EXIST");

        // Re-invoke seed — must still add Scrap rows despite Recovery present.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            await DbSeeder.SeedReasonCodesAsync(db);
        }

        var client = await OperatorClientAsync("op-rc-l17");
        var resp = await client.GetAsync("/api/v2/reason-codes?kind=Scrap");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await resp.Content.ReadFromJsonAsync<List<ReasonCodeOption>>();
        Assert.NotNull(rows);
        Assert.NotEmpty(rows!);
        // Per SeedReasonCodesAsync the Scrap list MUST include the 4 SC-*
        // codes + the 4 PREPRESS-specific SC-MAT-* / SC-PLATE-WORN /
        // SC-CUTTER-WORN codes added in 7b-3.
        var codes = rows!.Select(r => r.Code).ToHashSet();
        Assert.Contains("SC-COLOR", codes);
        Assert.Contains("SC-MAT-DAMAGE", codes);
        Assert.Contains("SC-PLATE-WORN", codes);
        Assert.Contains("SC-CUTTER-WORN", codes);
    }

    // ── Ordering ───────────────────────────────────────────────────

    [Fact]
    public async Task Codes_returned_in_Sort_then_Code_order()
    {
        await SeedScrapReasonAsync("SC-Z", "z", sort: 100);
        await SeedScrapReasonAsync("SC-A", "a", sort: 50);
        await SeedScrapReasonAsync("SC-M", "m", sort: 50);

        var client = await OperatorClientAsync("op-rc-order");
        var resp = await client.GetAsync("/api/v2/reason-codes?kind=Scrap");
        var rows = await resp.Content.ReadFromJsonAsync<List<ReasonCodeOption>>();

        var seededIndexes = rows!
            .Select((r, i) => (r.Code, i))
            .Where(t => t.Code is "SC-A" or "SC-M" or "SC-Z")
            .ToList();
        Assert.True(seededIndexes.First(t => t.Code == "SC-A").i
            < seededIndexes.First(t => t.Code == "SC-M").i,
            "Lower Sort first.");
        Assert.True(seededIndexes.First(t => t.Code == "SC-A").i
            < seededIndexes.First(t => t.Code == "SC-Z").i,
            "Sort=50 before Sort=100.");
        Assert.True(seededIndexes.First(t => t.Code == "SC-M").i
            < seededIndexes.First(t => t.Code == "SC-Z").i,
            "Sort=50/M before Sort=100/Z.");
    }
}
