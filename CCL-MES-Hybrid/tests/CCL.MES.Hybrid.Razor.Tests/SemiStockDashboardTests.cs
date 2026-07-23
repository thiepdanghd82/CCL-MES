using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P11.5-3 — bUnit render tests cho <see cref="SemiStockDashboard"/> (kho
/// bán thành phẩm). Rule 4: plain button/select/input. Wire-mirror:
/// SemiStockControllerTests khớp từng probe server-side.
/// </summary>
public sealed class SemiStockDashboardTests : TestContext
{
    private readonly RecordingApi _api = new();

    public SemiStockDashboardTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("op");
        // RowContextMenu invokes cclMesMenu.place via JS on open — loose so the
        // menu-open fixtures don't need an explicit JS setup (place is caught).
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static SemiLotRow Lot(long id, string lotNo, int avail, int reserved = 0,
        string status = "AVAILABLE", bool expiring = false, DateTime? expiryAt = null,
        string semiKind = "PRINTED_SEMI") => new()
    {
        Id = id, LotNo = lotNo, SemiKind = semiKind, SourceWorkOrderId = 1,
        QtyProduced = avail + reserved, QtyAvailable = avail, QtyReserved = reserved,
        Status = status, Expiring = expiring, ExpiryAt = expiryAt,
    };

    private static SemiStockView View(params SemiLotRow[] lots) => new()
    {
        Lots = lots.ToList(),
        TotalAvailable = lots.Sum(l => l.QtyAvailable),
        TotalReserved = lots.Sum(l => l.QtyReserved),
    };

    [Fact]
    public void Renders_lots_and_totals()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400), Lot(2, "SEMI-B", 400, 100)));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.Equal(2, cut.FindAll("[data-testid^='semi-row-']").Count);
        var totals = cut.Find("[data-testid='semi-totals']").TextContent;
        Assert.Contains("800", totals);   // available
        Assert.Contains("100", totals);   // reserved
    }

    [Fact]
    public void Expiring_lot_raises_fefo_banner_and_tag()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-EARLY", 400, expiring: true)));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.NotNull(cut.Find("[data-testid='semi-expiry-banner']"));
        Assert.NotNull(cut.Find("[data-testid='semi-expiring-1']"));
    }

    [Fact]
    public void Empty_stock_shows_empty_state()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View());
        var cut = RenderComponent<SemiStockDashboard>();
        Assert.NotNull(cut.Find("[data-testid='semi-empty']"));
    }

    // ── Lọc = segmented CHIP-BUTTONS (thay <select> native — WKWebView native
    //    picker kẹt sau re-render). Click chip = @onclick set field client-side,
    //    KHÔNG picker → không kẹt. GET chỉ initial/Tải lại/sau nhập lô. ──

    private SemiStockView MixedView() => View(
        Lot(1, "SEMI-IN-A", 400, semiKind: "PRINTED_SEMI", status: "AVAILABLE"),
        Lot(2, "SEMI-TP-B", 300, semiKind: "TAPE_SEMI", status: "AVAILABLE"),
        Lot(3, "SEMI-IN-C", 0, semiKind: "PRINTED_SEMI", status: "DEPLETED"));

    [Fact]
    public void Click_kind_chip_narrows_rows_client_side_without_reload()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(MixedView());
        var cut = RenderComponent<SemiStockDashboard>();
        Assert.Equal(3, cut.FindAll("[data-testid^='semi-row-']").Count);

        cut.Find("[data-testid='semi-kind-TAPE_SEMI']").Click();

        Assert.Single(cut.FindAll("[data-testid^='semi-row-']"));   // chỉ lô TAPE
        Assert.NotNull(cut.Find("[data-testid='semi-row-2']"));
        Assert.Single(_api.SemiLotsCalls);                          // KHÔNG GET mới
    }

    [Fact]
    public void Click_status_chip_narrows_rows_client_side_without_reload()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(MixedView());
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-status-DEPLETED']").Click();

        Assert.Single(cut.FindAll("[data-testid^='semi-row-']"));
        Assert.NotNull(cut.Find("[data-testid='semi-row-3']"));
        Assert.Single(_api.SemiLotsCalls);
    }

    [Fact]
    public void Consecutive_chip_clicks_narrow_each_time_without_intermediate_action()
    {
        // Bug cũ: đổi filter lần 2 bị đóng băng (native select). Chip-button: click
        // Loại → Trạng thái → Loại liên tiếp, rows hẹp đúng mỗi lần, KHÔNG thao
        // tác trung gian, KHÔNG GET thêm, KHÔNG picker native để kẹt.
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(MixedView());
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-kind-PRINTED_SEMI']").Click();
        Assert.Equal(2, cut.FindAll("[data-testid^='semi-row-']").Count);   // IN-A + IN-C

        cut.Find("[data-testid='semi-status-AVAILABLE']").Click();
        Assert.Single(cut.FindAll("[data-testid^='semi-row-']"));           // IN-A
        Assert.NotNull(cut.Find("[data-testid='semi-row-1']"));

        cut.Find("[data-testid='semi-kind-TAPE_SEMI']").Click();
        Assert.Single(cut.FindAll("[data-testid^='semi-row-']"));           // TP-B (TAPE+AVAILABLE)
        Assert.NotNull(cut.Find("[data-testid='semi-row-2']"));

        Assert.Single(_api.SemiLotsCalls);                                  // không GET thêm lần nào
    }

    [Fact]
    public void Active_chip_reflects_selection_and_group_has_no_native_select()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(MixedView());
        var cut = RenderComponent<SemiStockDashboard>();

        // Mặc định "Tất cả" active.
        Assert.Contains("is-on", cut.Find("[data-testid='semi-kind-all']").GetAttribute("class"));

        cut.Find("[data-testid='semi-kind-TAPE_SEMI']").Click();
        Assert.Contains("is-on", cut.Find("[data-testid='semi-kind-TAPE_SEMI']").GetAttribute("class"));
        Assert.DoesNotContain("is-on", cut.Find("[data-testid='semi-kind-all']").GetAttribute("class"));

        // KHÔNG còn <select> native trong 2 group filter (nguồn bug WKWebView).
        Assert.Empty(cut.FindAll("[data-testid='semi-filter-kind'] select"));
        Assert.Empty(cut.FindAll("[data-testid='semi-filter-status'] select"));
    }

    [Fact]
    public void Post_lot_submits_with_entered_fields_then_reloads()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400)));
        PostSemiLotRequest? sent = null;
        _api.PostSemiLotImpl = (req, _) => { sent = req; return Task.FromResult(new SemiSetResponse { Ok = true, SemiLotId = 9 }); };
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-add-toggle']").Click();
        cut.Find("[data-testid='semi-add-lotno']").Input("SEMI-NEW-1");
        cut.Find("[data-testid='semi-add-qty']").Change("250");
        cut.Find("[data-testid='semi-add-submit']").Click();

        Assert.NotNull(sent);
        Assert.Equal("SEMI-NEW-1", sent!.LotNo);
        Assert.Equal(250, sent.Qty);
        Assert.Equal("PRINTED_SEMI", sent.SemiKind);
        // reloaded after post (initial + reload).
        Assert.Equal(2, _api.SemiLotsCalls.Count);
    }

    [Fact]
    public void Post_lot_duplicate_shows_localised_banner()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400)));
        _api.PostSemiLotImpl = (_, _) => Task.FromResult(new SemiSetResponse { Ok = false, ErrorCode = "semi.lot_exists" });
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-add-toggle']").Click();
        cut.Find("[data-testid='semi-add-lotno']").Input("SEMI-A");
        cut.Find("[data-testid='semi-add-qty']").Change("10");
        cut.Find("[data-testid='semi-add-submit']").Click();

        Assert.Contains("đã tồn tại", cut.Find("[data-testid='semi-banner']").TextContent);
    }

    [Fact]
    public void Initial_load_error_shows_banner()
    {
        _api.SemiLotsImpl = (_, _, _, _) => throw new InvalidOperationException("boom");
        var cut = RenderComponent<SemiStockDashboard>();
        Assert.NotNull(cut.Find("[data-testid='semi-initial-error']"));
    }

    // ── REDESIGN fixtures: KPI / search / pill / countdown / modal / menu ──

    [Fact]
    public void Kpi_cards_show_available_reserved_expiring_count()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(
            Lot(1, "SEMI-A", 400, expiring: true), Lot(2, "SEMI-B", 400, 100)));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.Equal("800", cut.Find("[data-testid='semi-kpi-available']").TextContent.Trim());
        Assert.Equal("100", cut.Find("[data-testid='semi-kpi-reserved']").TextContent.Trim());
        Assert.Equal("1", cut.Find("[data-testid='semi-kpi-expiring']").TextContent.Trim());
        Assert.Equal("2", cut.Find("[data-testid='semi-kpi-count']").TextContent.Trim());
    }

    [Fact]
    public void Search_filters_rows_client_side_without_reload()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(
            Lot(1, "SEMI-ALPHA", 400), Lot(2, "SEMI-BETA", 400)));
        var cut = RenderComponent<SemiStockDashboard>();
        Assert.Equal(2, cut.FindAll("[data-testid^='semi-row-']").Count);

        cut.Find("[data-testid='semi-search']").Input("ALPHA");

        Assert.Single(cut.FindAll("[data-testid^='semi-row-']"));
        Assert.NotNull(cut.Find("[data-testid='semi-row-1']"));
        // client-side only — no extra API call beyond the initial load.
        Assert.Single(_api.SemiLotsCalls);
    }

    [Fact]
    public void Status_pill_class_reflects_status()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(
            Lot(1, "SEMI-A", 400),
            Lot(2, "SEMI-B", 0, status: "DEPLETED"),
            Lot(3, "SEMI-C", 0, status: "EXPIRED")));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.NotNull(cut.Find("[data-testid='semi-row-1'] .semi-status-available"));
        Assert.NotNull(cut.Find("[data-testid='semi-row-2'] .semi-status-depleted"));
        Assert.NotNull(cut.Find("[data-testid='semi-row-3'] .semi-status-expired"));
    }

    [Fact]
    public void Expiry_countdown_renders_relative_text()
    {
        var today = DateTime.UtcNow.Date;
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(
            Lot(1, "SEMI-FUT", 400, expiryAt: today.AddDays(3)),
            Lot(2, "SEMI-PAST", 400, expiryAt: today.AddDays(-2))));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.Contains("còn 3 ngày", cut.Find("[data-testid='semi-row-1']").TextContent);
        Assert.Contains("quá hạn 2 ngày", cut.Find("[data-testid='semi-row-2']").TextContent);
    }

    [Fact]
    public void Add_is_modal_with_prominent_barcode_field()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400)));
        var cut = RenderComponent<SemiStockDashboard>();

        // form not present until modal opened.
        Assert.Empty(cut.FindAll("[data-testid='semi-add-form']"));
        cut.Find("[data-testid='semi-add-toggle']").Click();
        Assert.NotNull(cut.Find("[data-testid='semi-add-form']"));
        // barcode field is the monospace scan input.
        Assert.Contains("semi-input-scan", cut.Find("[data-testid='semi-add-lotno']").GetAttribute("class"));
        // submit disabled until a lot no + qty entered.
        Assert.True(cut.Find("[data-testid='semi-add-submit']").HasAttribute("disabled"));
    }

    [Fact]
    public void Kebab_opens_menu_and_view_source_shows_genealogy()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(7, "SEMI-G", 400)));
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-kebab-7']").Click();               // open row menu
        var item = cut.FindAll(".row-ctx-item").Single(b => b.TextContent.Contains("Xem WO nguồn"));
        item.Click();                                                   // select action

        Assert.NotNull(cut.Find("[data-testid='semi-genealogy']"));
        Assert.Contains("SEMI-G", cut.Find("[data-testid='semi-genealogy']").TextContent);
    }

    [Fact]
    public void Empty_state_has_add_cta()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View());
        var cut = RenderComponent<SemiStockDashboard>();
        var empty = cut.Find("[data-testid='semi-empty']");
        Assert.Contains("Kho trống", empty.TextContent);
        Assert.NotNull(empty.QuerySelector("button"));
    }

    [Fact]
    public void Icons_carry_explicit_pixel_size_not_unbounded()
    {
        // Regression: <svg> sinh từ MarkupString KHÔNG nhận scope attribute của
        // scoped .razor.css → phải nhúng size inline, nếu không SVG nở kín màn
        // hình (giant-icon bug). Khoá: mọi icon có width + inline style px.
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View());  // empty → box icon 44
        var cut = RenderComponent<SemiStockDashboard>();

        var svgs = cut.FindAll("svg");
        Assert.NotEmpty(svgs);
        foreach (var svg in svgs)
        {
            Assert.True(svg.HasAttribute("width"), "svg thiếu width → sẽ nở vô hạn");
            Assert.Contains("px", svg.GetAttribute("style") ?? "");
        }
        // empty-state box icon phải là 44px tường minh.
        var emptyIco = cut.Find("[data-testid='semi-empty'] svg");
        Assert.Equal("44", emptyIco.GetAttribute("width"));
    }
}
