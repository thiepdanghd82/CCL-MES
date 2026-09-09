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
            // KPI đọc từ ĐÂY, không cộng từ Items. Cố ý cho số khác hẳn số
            // dòng: Items chỉ là 200 dòng đầu và đã lọc theo chip, nên nếu ai
            // đó quay lại cộng từ Items thì hai assert dưới đỏ ngay.
            Summary = new IqcNgSummary
            {
                Total = 139, Open = 7, Claimed = 8, Settled = 123, ClosedNoClaim = 2,
                DetectedIqc = 70, DetectedProduction = 64,
                SettlementReplacement = 84, SettlementCreditNote = 39,
                TotalAreaM2 = 1234.5, TrendYear = 2026,
                Monthly = Enumerable.Range(1, 12)
                    .Select(m => new IqcNgMonthPoint { Month = m, Count = m }).ToList(),
                TopSuppliers = new List<IqcNgNameCount>
                {
                    new() { Name = "Vietnam Paper Tube Co.", Count = 19 },
                    new() { Name = "P.T.S International", Count = 6 },
                },
                TopDefects = new List<IqcNgNameCount>
                {
                    new() { Name = "Xước", Count = 14 },
                },
            },
        });

        var cut = RenderComponent<IqcNgBoard>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-ng]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-ng-row-11]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-ng-new]"));

        // Số của CẢ SỔ (server), KHÔNG phải số dòng đang hiện. Items có 1 Open
        // và 1 Claimed; nếu KPI cộng từ Items thì ra "1" và hai dòng này đỏ.
        Assert.Contains("7", cut.Find("[data-testid=iqc-ng-kpi-open]").TextContent);
        Assert.Contains("8", cut.Find("[data-testid=iqc-ng-kpi-claimed]").TextContent);
        Assert.Contains("139", cut.Find("[data-testid=iqc-ng-kpi-total]").TextContent);
        Assert.Contains("123", cut.Find("[data-testid=iqc-ng-kpi-settled]").TextContent);
    }

    [Fact]
    public void Dashboard_nho_hien_du_bon_khoi()
    {
        Wire();
        _api.ListIqcNgImpl = (_, _, _) => Task.FromResult(new IqcNgListResponse
        {
            Items = Array.Empty<IqcNgListItem>(),
            Summary = new IqcNgSummary
            {
                Total = 139, Open = 0, Claimed = 8, Settled = 123, ClosedNoClaim = 2,
                DetectedIqc = 70, DetectedProduction = 64, TrendYear = 2026,
                Monthly = Enumerable.Range(1, 12)
                    .Select(m => new IqcNgMonthPoint { Month = m, Count = m == 3 ? 9 : 0 }).ToList(),
                TopSuppliers = new List<IqcNgNameCount> { new() { Name = "NCC A", Count = 19 } },
                TopDefects = new List<IqcNgNameCount> { new() { Name = "Xước", Count = 14 } },
            },
        });

        var cut = RenderComponent<IqcNgBoard>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-ng-dash]"));
        // Phát hiện ở đâu: hai đoạn thanh, IQC + SX.
        Assert.Equal(2, cut.FindAll("[data-testid=iqc-ng-stage] .iqc-ng-seg").Count);
        // Hai bảng xếp hạng.
        Assert.Single(cut.FindAll("[data-testid=iqc-ng-top-suppliers] .iqc-ng-rank-row"));
        Assert.Single(cut.FindAll("[data-testid=iqc-ng-top-defects] .iqc-ng-rank-row"));
        // Xu hướng LUÔN đủ 12 cột, kể cả tháng 0 vụ — thiếu cột thì trục co
        // lại và tháng im lặng trông như không tồn tại.
        Assert.Equal(12, cut.FindAll("[data-testid=iqc-ng-trend] .iqc-ng-tslot").Count);
        // Không còn vụ nào chưa claim ⇒ ô "Mở" KHÔNG được tô báo động.
        Assert.DoesNotContain("is-alarm",
            cut.Find("[data-testid=iqc-ng-kpi-open]").GetAttribute("class") ?? "");
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
