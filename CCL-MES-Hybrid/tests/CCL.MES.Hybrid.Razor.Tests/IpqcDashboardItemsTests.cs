using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Phương án C — Bước 4 UI. IpqcDashboard ở mode DATA-DRIVEN: khi view có
/// Items (auto-sync materialize), render danh sách hạng mục theo nhóm thay vì
/// 4 slot cứng; nút OK item gọi PutIpqcItemAsync. Khi Items rỗng → vẫn 4 slot
/// legacy (đã có IpqcDashboardTests khóa).
/// </summary>
public sealed class IpqcDashboardItemsTests : TestContext
{
    public IpqcDashboardItemsTests()
    {
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
        var session = new StubAuthSession();
        session.SetUser("qc-user", "QC");
        Services.AddSingleton<CCL.MES.Hybrid.Client.Auth.IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    private static IpqcView ViewWithItems() => new()
    {
        WoId = 39, WoNo = "WO-PAC-DEMO-LABEL", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL,PRESS_CNC",
        IsReadyForJudgment = false, AllOk = false, AnyNg = false,
        Items = new[]
        {
            new IpqcViewItem { ItemKey = "LBL-A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan",
                Label = "Đúng nội dung in", AcceptanceCriteria = "Khớp file", Status = "Pending", DefectCode = "CONTENT" },
            new IpqcViewItem { ItemKey = "PCC-B1", ProcessLine = "PRESS_CNC", GroupLabel = "B·Cắt",
                Label = "Đường cắt", AcceptanceCriteria = "Đúng dao", Status = "Pending", DefectCode = "CUTLINE" },
        },
    };

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "CONTENT", LabelEn = "Content", LabelVi = "Nội dung", Kind = "Scrap", Sort = 1 },
        new() { Code = "CUTLINE", LabelEn = "Cut", LabelVi = "Đường cắt", Kind = "Scrap", Sort = 2 },
    };

    [Fact]
    public void Items_mode_renders_item_cards_and_resolved_lines_not_legacy_slots()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithItems());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 39L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            // Resolved-lines banner present.
            Assert.Contains("LABEL,PRESS_CNC", cut.Find("[data-testid='ipqc-resolved-lines']").TextContent);
            // Two-axis: LABEL → PRINT process (default active) shows LBL-A1;
            // PRESS_CNC → CUT process, so PCC-B1 is NOT visible until the CUT
            // chip is selected.
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-A1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-PCC-B1']"));
            // Counter shows /2 (item-aware, WO-wide), not /4.
            Assert.Contains("/2", cut.Find("[data-testid='ipqc-counter']").TextContent);
            // Legacy 4-slot cards NOT rendered in items mode.
            Assert.Empty(cut.FindAll("[data-testid='ipqc-slot-material']"));
        });

        // Switch to the CUT process → PCC-B1 appears, LBL-A1 gone.
        cut.Find("[data-testid='ipqc-process-cut']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-PCC-B1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-LBL-A1']"));
        });
    }

    [Fact]
    public void Item_ok_calls_put_item_endpoint_with_key()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithItems());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 39L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-A1-ok']")));
        cut.Find("[data-testid='ipqc-item-LBL-A1-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(api.PutIpqcItemCalls);
            Assert.Equal(39L, call.Id);
            Assert.Equal("LBL-A1", call.ItemKey);
            Assert.Equal("Ok", call.Req.Status);
        });
    }
}
