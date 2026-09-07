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

/// <summary>Tab History — chip sheet Excel + list Pass/Fail.</summary>
public sealed class IqcHistoryBoardTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    private void Wire()
    {
        _session.SetUser("qc-user", "QC");
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("qc-user");
        auth.SetRoles("QC");
    }

    [Fact]
    public void Lists_approved_rows_and_sheet_chips()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) => Task.FromResult(new IqcHistoryListResponse
        {
            Page = 1, PageSize = 50, Total = 1,
            Items = new List<IqcHistoryListItem>
            {
                new()
                {
                    Id = 9, ReceiptNo = "IQC-R9", Sheet = "Roll", Group = "Materials",
                    MaterialCategory = "Roll", Result = "Pass",
                    ReceivedDate = new DateTime(2026, 3, 1),
                    ApprovedAt = new DateTime(2026, 3, 2), ApprovedBy = "qc-user",
                    Quantity = 2, Uom = "rolls",
                },
            },
        });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-history]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-history-sheet-roll]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-history-row-9]"));
        Assert.Single(_api.ListIqcHistoryCalls);
    }

    [Fact]
    public void Sheet_chip_reloads_with_filter()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) =>
            Task.FromResult(new IqcHistoryListResponse { Page = 1, PageSize = 50, Total = 0 });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-history-sheet-chem]").Click();

        Assert.Equal(2, _api.ListIqcHistoryCalls.Count);
        Assert.Equal("Chem", _api.ListIqcHistoryCalls[1].Sheet);
    }

    [Fact]
    public void Nut_x_xoa_o_tim_kiem_va_nap_lai_khong_loc()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) =>
            Task.FromResult(new IqcHistoryListResponse { Page = 1, PageSize = 50, Total = 0 });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        Assert.Empty(cut.FindAll("[data-testid=iqc-history-search-clear]"));   // rỗng thì không có nút

        cut.Find("[data-testid=iqc-history-search]").Input("XLS-ROLL");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=iqc-history-search-clear]")));

        cut.Find("[data-testid=iqc-history-search-clear]").Click();

        Assert.Equal("", cut.Find("[data-testid=iqc-history-search]").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-history-search-clear]"));
        Assert.True(string.IsNullOrEmpty(_api.ListIqcHistoryCalls[^1].Search));
    }

    [Theory]
    [InlineData(15, "iqc-exp-crit")]     // < 30 ngày → đỏ
    [InlineData(45, "iqc-exp-warn")]     // 30–60 ngày → vàng
    [InlineData(200, null)]              // còn dài → không tô
    [InlineData(-5, "iqc-exp-crit")]     // quá hạn → đỏ
    public void To_mau_o_han_dung_theo_so_ngay_con_lai(int daysLeft, string? expected)
    {
        Wire();
        var expiry = DateTime.Today.AddDays(daysLeft);
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) => Task.FromResult(HistoryWith(
            warehouseIn: expiry.AddDays(-365), expiry: expiry, received: expiry.AddDays(-360)));

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        var cls = cut.Find("[data-testid=iqc-history-exp-7]").GetAttribute("class") ?? "";

        Assert.Contains("iqc-exp", cls);
        if (expected is null)
        {
            Assert.DoesNotContain("iqc-exp-crit", cls);
            Assert.DoesNotContain("iqc-exp-warn", cls);
        }
        else
        {
            Assert.Contains(expected, cls);
        }
    }

    [Fact]
    public void Ngay_nhap_kho_trung_ngay_ve_thi_hien_mo_mot_du_lieu()
    {
        Wire();
        var day = new DateTime(2026, 1, 5);
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) => Task.FromResult(HistoryWith(
            warehouseIn: day, expiry: day.AddDays(365), received: day));

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        var cell = cut.Find("[data-testid=iqc-history-wh-7]");

        Assert.Equal("05/01/2026", cell.TextContent.Trim());
        Assert.Contains("iqc-wh-same", cell.GetAttribute("class") ?? "");
    }

    [Fact]
    public void Ledger_khong_co_ngay_nhap_kho_thi_khong_doan_han_dung()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) => Task.FromResult(HistoryWith(
            warehouseIn: null, expiry: null, received: new DateTime(2026, 1, 5)));

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));

        Assert.Equal("—", cut.Find("[data-testid=iqc-history-wh-7]").TextContent.Trim());
        Assert.Equal("—", cut.Find("[data-testid=iqc-history-exp-7]").TextContent.Trim());
    }

    private static IqcHistoryListResponse HistoryWith(
        DateTime? warehouseIn, DateTime? expiry, DateTime received) => new()
    {
        Page = 1, PageSize = 50, Total = 1,
        Items = new List<IqcHistoryListItem>
        {
            new()
            {
                Id = 7, ReceiptNo = "XLS-ROLL-03722", Sheet = "Roll", Group = "Materials",
                MaterialCategory = "Roll", Result = "Pass",
                ReceivedDate = received, WarehouseInDate = warehouseIn, ExpiryDate = expiry,
                Quantity = 1, Uom = "rolls",
            },
        },
    };

    [Fact]
    public void Date_inputs_reload_with_from_to()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) =>
            Task.FromResult(new IqcHistoryListResponse { Page = 1, PageSize = 50, Total = 0 });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-history-from]").Change("2026-01-01");
        cut.Find("[data-testid=iqc-history-to]").Change("2026-01-31");

        Assert.True(_api.ListIqcHistoryCalls.Count >= 3);
        var last = _api.ListIqcHistoryCalls[^1];
        Assert.Equal(new DateTime(2026, 1, 1), last.From);
        Assert.Equal(new DateTime(2026, 2, 1), last.To);
    }
}
