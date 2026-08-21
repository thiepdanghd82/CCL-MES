using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Realtime;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// bUnit tests for the real-time frozen-snapshot Traceability list + detail
/// body. P2 showcard-migration — the list is a registered WINDOW and a row
/// double-click no longer self-hosts a FloatingWindow: it calls
/// WM.Open("trace:{WoNo}", …, typeof(TraceabilityDetail)) so the WindowManager
/// host owns the chrome/rect/focus/dedupe/soft-cap. The list tests assert against
/// the WindowManager; the detail-body tests render TraceabilityDetailDialog
/// (Chrome=false) directly — the 4-tab body + renderers are unchanged.
/// </summary>
public sealed class QualityTraceabilityTests : TestContext
{
    private readonly RecordingApi _api;
    private readonly StubShopfloorLive _live;
    private readonly WindowManager _wm = new();

    public QualityTraceabilityTests()
    {
        _api = new RecordingApi();
        _live = new StubShopfloorLive();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IShopfloorLiveService>(_live);
        Services.AddSingleton<IBarcodeScannerService>(new StubScannerService());
        Services.AddSingleton<IWindowManager>(_wm);
        Services.AddSingleton(Options.Create(new HardwareOptions { ScanEnabled = false }));
        // i18n Phase-2 — window title + FloatingWindow tooltips.
        Services.AddSingleton<CCL.MES.Hybrid.Client.Localization.ILanguageService, CCL.MES.Hybrid.Client.Localization.InMemoryLanguageService>();
        Services.AddSingleton<CCL.MES.Hybrid.Client.Localization.ITranslationCatalog, CCL.MES.Hybrid.Client.Localization.TranslationCatalog>();
        Services.AddSingleton<CCL.MES.Hybrid.Client.Localization.ITranslator, CCL.MES.Hybrid.Client.Localization.Translator>();
        this.AddTestAuthorization().SetAuthorized("qc-user");
        // The floating-window chrome calls cclMesFloat.* JS interop in
        // OnAfterRender / Dispose; the test host has no JS engine, so run the
        // interop loose (unknown calls return default instead of throwing).
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static NpiPagedRaw<TraceListRow> OnePage(TraceListRow row)
        => new() { Items = new[] { row }, Total = 1, Page = 1, PageSize = 50 };

    private static NpiPagedRaw<TraceListRow> Page(params TraceListRow[] rows)
        => new() { Items = rows, Total = rows.Length, Page = 1, PageSize = 50 };

    private static TraceListRow Row(string woNo = "WO-TR-1") => new()
    {
        WoId = 7, WoNo = woNo, ProductName = "Widget", ProductCode = "PC-1", Customer = "Acme",
        CurrentMesPhase = "RUNNING", LastScannedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        LatestFrozenAtUtc = null, FrozenPhases = new() { TracePhase.Product },
    };

    private static TraceabilityDetailDto Detail() => new()
    {
        WoNo = "WO-TR-1", ProductName = "Widget",
        Product = new TracePhaseDto
        {
            Version = 1, SchemaVersion = 1, FrozenAtUtc = DateTime.UtcNow, FrozenBy = "op",
            Payload = new TracePayload
            {
                Phase = TracePhase.Product, WoNo = "WO-TR-1", Variant = "PC-1",
                Header = new() { new TraceKv { Label = "Product code", Value = "80640004" } },
                // NOTE: deliberately NO "no" key — proves the No. column comes
                // from the loop index (works for old snapshots that lack it).
                Items = new()
                {
                    // Clean OK row with a persisted scan + BOM-resolved description.
                    new TraceItem { Key = "mat-0", Label = "30030532", Status = "Ok", Extra = new()
                        { ["partNo"] = "30030532", ["description"] = "BOPP GLOSS",
                          ["qpaM2"] = "0.000004", ["qtyRequired"] = "0.003875", ["uom"] = "kg",
                          ["partScan"] = "30030532-0145", ["partDescription"] = "BOPP GLOSS", ["lotNo"] = "L1" } },
                    // Special-accept row: Status Ok WITH a retained NG reason.
                    new TraceItem { Key = "mat-1", Label = "30031145", Status = "Ok", NgReason = "SC-MAT-DAMAGE", NgNote = "edge nick", Extra = new()
                        { ["partNo"] = "30031145", ["description"] = "BW488",
                          ["qpaM2"] = "0.003000", ["qtyRequired"] = "3", ["uom"] = "m2",
                          ["partScan"] = "30031145", ["partDescription"] = "BW488", ["lotNo"] = "L2" } },
                },
                Tools = new()
                {
                    new TraceTool { Type = "Plate", NumberOrCode = "PL-77", Status = "Ok", CheckedBy = "op", CheckedAt = "2026-07-21 09:00" },
                    new TraceTool { Type = "Cutter", NumberOrCode = "CT-12", Status = "Ng", NgReason = "SC-CUTTER-WORN", CheckedBy = "op", CheckedAt = "2026-07-21 09:05" },
                },
            },
        },
        Ipqc = new TracePhaseDto
        {
            Version = 1, SchemaVersion = 1, FrozenAtUtc = DateTime.UtcNow, FrozenBy = "qc",
            Payload = new TracePayload
            {
                Phase = TracePhase.Ipqc, WoNo = "WO-TR-1",
                Header = new() { new TraceKv { Label = "Judgment", Value = "GoRun" } },
                Items = new()
                {
                    new TraceItem { Key = "i0", Label = "Material", Status = "Ok", Extra = new() { ["processLine"] = "LABEL" } },
                },
            },
        },
        Fqc = null, Oqc = null,
    };

    [Fact]
    public void List_renders_and_double_click_opens_a_window()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));

        var cut = RenderComponent<QualityTraceability>();
        Assert.Single(cut.FindAll("tr.trace-row"));
        Assert.Contains("WO-TR-1", cut.Markup);
        Assert.Contains("RUNNING", cut.Markup);
        Assert.Empty(_wm.Windows);   // no detail window yet

        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        // Row-open delegates to the WindowManager under the per-WO key.
        var win = Assert.Single(_wm.Windows);
        Assert.Equal("trace:WO-TR-1", win.Key);
        Assert.Equal(typeof(CCL.MES.Hybrid.Razor.Shared.TraceabilityDetail), win.ContentType);
        Assert.Equal("WO-TR-1", win.Parameters!["WoNo"]);
    }

    [Fact]
    public void Multiple_rows_open_independent_windows()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(Page(Row("WO-A"), Row("WO-B")));

        var cut = RenderComponent<QualityTraceability>();
        var rows = cut.FindAll("tr.trace-row");
        Assert.Equal(2, rows.Count);

        rows[0].TriggerEvent("ondblclick", new MouseEventArgs());
        cut.FindAll("tr.trace-row")[1].TriggerEvent("ondblclick", new MouseEventArgs());

        // Two distinct per-WO windows in the manager.
        Assert.Equal(2, _wm.Windows.Count);
        Assert.Contains(_wm.Windows, w => w.Key == "trace:WO-A");
        Assert.Contains(_wm.Windows, w => w.Key == "trace:WO-B");
    }

    [Fact]
    public void Reopening_same_wo_dedupes_not_duplicates()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row("WO-A")));

        var cut = RenderComponent<QualityTraceability>();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        Assert.Single(_wm.Windows);   // dedupe by "trace:WO-A" — still one
    }

    [Fact]
    public void Reopening_same_wo_keeps_the_window_alive_same_instance()
    {
        // Keep-alive: dedupe re-focuses the SAME OpenWindow (same Id), it is not
        // torn down + recreated — the host's @key stability preserves body state.
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row("WO-A")));

        var cut = RenderComponent<QualityTraceability>();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());
        var firstId = _wm.Windows[0].Id;

        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());
        Assert.Equal(firstId, Assert.Single(_wm.Windows).Id);
    }

    [Fact]
    public void Opening_beyond_the_soft_cap_is_blocked_with_a_notice()
    {
        // Fill the manager to its SoftCap with unrelated windows, then a row-open
        // is blocked (WM.Open → null) and the page surfaces the "max" notice.
        for (var i = 0; i < _wm.SoftCap; i++)
        {
            _wm.Open($"other:{i}", $"Other {i}", null, typeof(QualityTraceability));
        }
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row("WO-A")));

        var cut = RenderComponent<QualityTraceability>();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        // No trace window was opened (cap block) + the notice shows the SoftCap.
        Assert.DoesNotContain(_wm.Windows, w => w.Key.StartsWith("trace:"));
        Assert.Contains($"tối đa {_wm.SoftCap}", cut.Markup);
    }

    [Fact]
    public void Live_signal_repulls_the_list()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));
        var cut = RenderComponent<QualityTraceability>();
        var initial = _api.TraceabilityCalls.Count;   // 1 from OnInitialized

        _live.RaiseChanged("trace_updated:WO-TR-1");   // hub push → debounced re-pull

        cut.WaitForAssertion(() => Assert.True(_api.TraceabilityCalls.Count > initial));
    }

    [Fact]
    public void Live_offline_badge_reflects_connection()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));
        var cut = RenderComponent<QualityTraceability>();

        // Stub starts disconnected → Offline.
        Assert.Contains("Ngoại tuyến", cut.Find(".trace-live").TextContent);

        _live.SetConnected(true);
        cut.WaitForAssertion(() => Assert.Contains("Trực tuyến", cut.Find(".trace-live").TextContent));
    }

    [Fact]
    public void Product_tab_uses_fixed_layout_no_scrap_columns()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        Assert.Equal(4, cut.FindAll(".trace-tab").Count);
        var m = cut.Markup;
        // Header key-value still present (label localized VI-default; the baked
        // English label "Product code" is mapped to the translation at render).
        Assert.Contains("Mã sản phẩm", m);
        Assert.Contains("80640004", m);

        // Fixed Product columns, in order — No. + Part No are SEPARATE columns.
        var heads = cut.FindAll(".trace-prod thead th").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "STT", "Mã linh kiện", "Mô tả", "QPA (m²)", "SL yêu cầu",
            "ĐVT", "Quét linh kiện", "Mô tả linh kiện", "Lô", "Trạng thái", "NG — lý do · ghi chú" }, heads);

        // Dropped columns are gone.
        Assert.DoesNotContain("Scrap Factor", m);
        Assert.DoesNotContain("Scrap %", m);
        Assert.DoesNotContain("scrapFactor", m);

        // Persisted scan + resolved description render.
        Assert.Contains("30030532-0145", m);
        Assert.Contains("BOPP GLOSS", m);
        Assert.Equal(2, cut.FindAll(".trace-prod tbody tr").Count);
    }

    [Fact]
    public void Product_special_accept_row_shows_distinct_status_and_ng()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        var m = cut.Markup;
        Assert.Contains("OK · Chấp nhận đặc biệt", m);      // Ok + retained NG reason (VI-default)
        Assert.Contains("SC-MAT-DAMAGE · edge nick", m);    // NG reason · note combined
    }

    [Fact]
    public void Product_no_column_is_1_based_from_loop_index_even_without_no_key()
    {
        // Detail() items carry NO "no" key (old-snapshot shape) → the column must
        // still read 1, 2 from the loop index.
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        var nos = cut.FindAll(".trace-prod tbody tr td.trace-prod-no").Select(td => td.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "1", "2" }, nos);
    }

    [Fact]
    public void Product_has_two_sections_and_tools_table_renders_variable_rows()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        // Bold section headings.
        var sections = cut.FindAll(".trace-prod-section").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "1. Vật tư đã xác nhận", "2. Công cụ đã xác nhận" }, sections);

        // Tools table = flexible list from payload.Tools (2 rows here, N in general).
        Assert.Single(cut.FindAll(".trace-tools"));
        Assert.Equal(2, cut.FindAll(".trace-tools tbody tr").Count);
        var m = cut.Markup;
        Assert.Contains("Plate", m);
        Assert.Contains("PL-77", m);
        Assert.Contains("Cutter", m);
        Assert.Contains("SC-CUTTER-WORN", m);
        // Plate/Cutter moved OUT of the meta header.
        Assert.DoesNotContain("Plate check", m);
        Assert.DoesNotContain("Cutter check", m);
    }

    [Fact]
    public void Ipqc_tab_still_uses_the_generic_renderer()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        cut.FindAll(".trace-tab").First(b => b.TextContent.Contains("IPQC")).Click();

        // Generic renderer: Item/Status/NG columns + a column derived from Extra.
        var heads = cut.FindAll(".trace-items:not(.trace-prod) thead th").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Contains("Hạng mục", heads);
        Assert.Contains("processLine", heads);   // Extra key becomes a column
        Assert.Empty(cut.FindAll(".trace-prod"));
    }

    [Fact]
    public void Not_frozen_tab_shows_empty_state_not_error()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        Assert.Empty(cut.FindAll(".trace-empty"));
        var fqcTab = cut.FindAll(".trace-tab").First(b => b.TextContent.Contains("FQC"));
        fqcTab.Click();
        Assert.Single(cut.FindAll(".trace-empty"));
        Assert.Contains("Chưa chốt dữ liệu FQC — dữ liệu chưa được đóng băng.", cut.Markup);
    }

    [Fact]
    public void Chrome_false_renders_body_only_no_floatingwindow_double_wrap()
    {
        // The no-double-wrap contract: in WM-hosted mode (Chrome=false) the
        // component renders ONLY the tab strip + body — NO FloatingWindow chrome
        // (.trace-win) — so the host's own FloatingWindow is the single wrapper.
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.Chrome, false));

        Assert.Empty(cut.FindAll(".trace-win"));           // no chrome
        Assert.Empty(cut.FindComponents<FloatingWindow>());
        Assert.Equal(4, cut.FindAll(".trace-tab").Count);  // body (tabs) still there
    }

    [Fact]
    public void Chrome_true_self_hosts_a_floatingwindow_for_standalone_callers()
    {
        // The legacy standalone path still works: Chrome=true (default) wraps the
        // body in one FloatingWindow (single wrapper) for any hand-hosting caller.
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

        Assert.Single(cut.FindComponents<FloatingWindow>());
        Assert.Single(cut.FindAll(".trace-win"));
        Assert.Equal(4, cut.FindAll(".trace-tab").Count);
    }
}
