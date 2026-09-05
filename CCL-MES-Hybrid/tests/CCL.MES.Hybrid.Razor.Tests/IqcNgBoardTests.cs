using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Shared.Iqc;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P13 bước 6 — tab NG/claim. Bám data-testid: list, KPI Open, tạo Modal,
/// Operator không thấy nút ghi.
/// </summary>
public sealed class IqcNgBoardTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    private void Wire(string role = "QC")
    {
        _session.SetUser(role.ToLowerInvariant() + "-user", role);
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(role.ToLowerInvariant() + "-user");
        auth.SetRoles(role);
    }

    [Fact]
    public void Lists_rows_and_open_kpi()
    {
        Wire();
        _api.ListIqcNgImpl = (_, _, _) => Task.FromResult(new IqcNgListResponse
        {
            Items = new[]
            {
                new IqcNgListItem
                {
                    Id = 11, DetectedAt = new DateTime(2026, 3, 1), DetectedStage = "Production",
                    Status = "Open", PartNo = "30030146", DefectName = "Xước", NgAreaM2 = 12.5,
                    CreatedBy = "qc-user",
                },
                new IqcNgListItem
                {
                    Id = 12, DetectedAt = new DateTime(2026, 3, 2), DetectedStage = "Unknown",
                    Status = "Claimed", PartNo = "30030147", DefectName = "Bong", NgRolls = 1,
                    CreatedBy = "qc-user",
                },
            },
        });

        var cut = RenderComponent<IqcNgBoard>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-ng]"));
        Assert.Contains("1", cut.Find("[data-testid=iqc-ng-kpi-open]").TextContent);
        Assert.Contains("1", cut.Find("[data-testid=iqc-ng-kpi-claimed]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=iqc-ng-row-11]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-ng-new]"));
    }

    [Fact]
    public void Operator_cannot_see_create_or_kebab()
    {
        Wire("Operator");
        _api.ListIqcNgImpl = (_, _, _) => Task.FromResult(new IqcNgListResponse
        {
            Items = new[]
            {
                new IqcNgListItem
                {
                    Id = 11, DetectedAt = new DateTime(2026, 3, 1), DetectedStage = "Unknown",
                    Status = "Open", PartNo = "30030146", DefectName = "Xước", NgAreaM2 = 1,
                    CreatedBy = "qc-user",
                },
            },
        });

        var cut = RenderComponent<IqcNgBoard>(p => p.Add(x => x.DebounceMs, 0));
        Assert.Empty(cut.FindAll("[data-testid=iqc-ng-new]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-ng-kebab-11]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-ng-row-11]"));
    }

    [Fact]
    public void Create_saves_unknown_stage_without_iqc_ticket()
    {
        Wire();
        var cut = RenderComponent<IqcNgBoard>(p => p.Add(x => x.DebounceMs, 0));

        cut.Find("[data-testid=iqc-ng-new]").Click();
        Assert.NotNull(cut.Find("[data-testid=iqc-ng-create]"));

        cut.Find("[data-testid=iqc-ng-part]").Input("30030146");
        cut.Find("[data-testid=iqc-ng-defect]").Input("Xước");
        cut.Find("[data-testid=iqc-ng-m2]").Input("12.5");
        cut.Find("[data-testid=iqc-ng-save]").Click();

        var body = Assert.Single(_api.CreateIqcNgCalls);
        Assert.Equal("30030146", body.PartNo);
        Assert.Equal("Xước", body.DefectName);
        Assert.Equal(12.5, body.NgAreaM2);
        Assert.Equal("Unknown", body.DetectedStage);
        Assert.Null(body.IqcInspectionId);
    }
}
