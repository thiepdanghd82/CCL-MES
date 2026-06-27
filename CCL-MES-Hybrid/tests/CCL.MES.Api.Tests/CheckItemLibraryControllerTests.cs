using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.CheckLibrary;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Phương án C — Bước 5 + 6. Wire coverage cho CheckItemLibraryController:
/// list, lines, và (B5/GATE B9) scoped reason-codes theo process line.
/// </summary>
public sealed class CheckItemLibraryControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public CheckItemLibraryControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        async Task LibAsync(string id, string line, string defect)
        {
            if (!await db.CheckItemLibraries.AnyAsync(c => c.ItemId == id))
                db.CheckItemLibraries.Add(new CheckItemLibrary
                {
                    ItemId = id, ProcessLine = line, QcStage = "IPQC", GroupLabel = "G", Code = id,
                    ItemVi = "vi " + id, ItemEn = "en", AcceptanceVi = "acc", AcceptanceEn = "acc",
                    DefectCode = defect, Active = true, Sort = 10,
                });
        }
        async Task ScrapAsync(string code)
        {
            if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
                db.ReasonCodes.Add(new ReasonCode { Code = code, LabelEn = code, LabelVi = code, Kind = ReasonCodeKind.Scrap, Sort = 10 });
        }
        await LibAsync("TST-LBL-1", "LABEL", "TST_CONTENT");
        await LibAsync("TST-PCC-1", "PRESS_CNC", "TST_CUTLINE");
        await LibAsync("TST-SLK-1", "SILK", "TST_DIRTY");
        await ScrapAsync("TST_CONTENT");
        await ScrapAsync("TST_CUTLINE");
        await ScrapAsync("TST_DIRTY");
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_filters_by_line()
    {
        await SeedAsync();
        var client = await ClientAsync("lib-list");
        var rows = await client.GetFromJsonAsync<List<CheckLibraryItemDto>>(
            "/api/v2/check-item-library?line=LABEL");
        Assert.NotNull(rows);
        Assert.Contains(rows!, r => r.ItemId == "TST-LBL-1");
        Assert.DoesNotContain(rows!, r => r.ProcessLine == "PRESS_CNC");
    }

    [Fact]
    public async Task Lines_returns_counts_per_process_line()
    {
        await SeedAsync();
        var client = await ClientAsync("lib-lines");
        var rows = await client.GetFromJsonAsync<List<CheckLibraryLineDto>>(
            "/api/v2/check-item-library/lines");
        Assert.NotNull(rows);
        Assert.Contains(rows!, r => r.ProcessLine == "LABEL" && r.Count >= 1);
        Assert.Contains(rows!, r => r.ProcessLine == "SILK" && r.Count >= 1);
    }

    [Fact]
    public async Task ScopedReasonCodes_returns_only_codes_for_requested_lines()
    {
        await SeedAsync();
        var client = await ClientAsync("lib-scope");
        var rows = await client.GetFromJsonAsync<List<ReasonCodeOption>>(
            "/api/v2/check-item-library/reason-codes?lines=LABEL,PRESS_CNC");
        Assert.NotNull(rows);
        var codes = rows!.Select(r => r.Code).ToList();
        Assert.Contains("TST_CONTENT", codes);   // LABEL
        Assert.Contains("TST_CUTLINE", codes);    // PRESS_CNC
        Assert.DoesNotContain("TST_DIRTY", codes); // SILK — scoped out
    }

    [Fact]
    public async Task ScopedReasonCodes_no_lines_returns_full_scrap_catalog()
    {
        await SeedAsync();
        var client = await ClientAsync("lib-scope-all");
        var rows = await client.GetFromJsonAsync<List<ReasonCodeOption>>(
            "/api/v2/check-item-library/reason-codes");
        Assert.NotNull(rows);
        var codes = rows!.Select(r => r.Code).ToList();
        Assert.Contains("TST_DIRTY", codes); // SILK present when unscoped
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/check-item-library/lines");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
