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
/// dialog: list renders + double-click opens the dialog, a hub "change"
/// signal re-pulls the list (debounced), the Live/Offline badge reflects the
/// connection, the generic renderer shows header + variant columns, and a
/// not-frozen phase shows the empty-state.
/// </summary>
public sealed class QualityTraceabilityTests : TestContext
{
    private readonly RecordingApi _api;
    private readonly StubShopfloorLive _live;
    private readonly InMemoryFloatingWindowStore _winStore;

    public QualityTraceabilityTests()
    {
        _api = new RecordingApi();
        _live = new StubShopfloorLive();
        _winStore = new InMemoryFloatingWindowStore();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IShopfloorLiveService>(_live);
        Services.AddSingleton<IBarcodeScannerService>(new StubScannerService());
        Services.AddSingleton<IFloatingWindowStore>(_winStore);
        Services.AddSingleton(Options.Create(new HardwareOptions { ScanEnabled = false }));
        // i18n Phase-2 — FloatingWindow (wrapped by TraceabilityDetailDialog) tooltips.
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
    public void List_renders_and_double_click_opens_dialog()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        Assert.Single(cut.FindAll("tr.trace-row"));
        Assert.Contains("WO-TR-1", cut.Markup);
        Assert.Contains("RUNNING", cut.Markup);
        Assert.Empty(cut.FindAll(".trace-win"));

        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        Assert.Single(cut.FindAll(".trace-win"));
        Assert.Equal("WO-TR-1", cut.Find(".trace-win-title").TextContent.Trim());
        // Non-modal: no blocking scrim over the list.
        Assert.Empty(cut.FindAll(".trace-modal-scrim"));
        // role/aria for the floating dialog.
        Assert.Equal("dialog", cut.Find(".trace-win").GetAttribute("role"));
        Assert.Contains("Truy xuất WO-TR-1", cut.Find(".trace-win").GetAttribute("aria-label"));
    }

    [Fact]
    public void Multiple_showcards_open_independently()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(Page(Row("WO-A"), Row("WO-B")));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        var rows = cut.FindAll("tr.trace-row");
        Assert.Equal(2, rows.Count);

        rows[0].TriggerEvent("ondblclick", new MouseEventArgs());
        cut.FindAll("tr.trace-row")[1].TriggerEvent("ondblclick", new MouseEventArgs());

        // Two independent floating windows, each with its own resize handles.
        Assert.Equal(2, cut.FindAll(".trace-win").Count);
        Assert.Equal(16, cut.FindAll(".fw-handle").Count);   // 8 per window
    }

    [Fact]
    public void Reopening_same_wo_focuses_not_duplicates()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row("WO-A")));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        Assert.Single(cut.FindAll(".trace-win"));   // still one, not two
    }

    [Fact]
    public void Opening_beyond_the_cap_is_blocked_with_a_notice()
    {
        var rows = Enumerable.Range(1, 7).Select(i => Row($"WO-{i}")).ToArray();
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(Page(rows));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        foreach (var _ in Enumerable.Range(0, 7))
        {
            // Re-query each time — opening re-renders the list.
            var domRows = cut.FindAll("tr.trace-row");
            var idx = cut.FindAll(".trace-win").Count;   // open the next unopened WO
            domRows[Math.Min(idx, domRows.Count - 1)].TriggerEvent("ondblclick", new MouseEventArgs());
        }

        Assert.Equal(6, cut.FindAll(".trace-win").Count);
        Assert.Contains("tối đa 6", cut.Markup);
    }

    [Fact]
    public void Closing_a_showcard_removes_it()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row("WO-A")));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());
        Assert.Single(cut.FindAll(".trace-win"));

        cut.Find(".fw-light-close").Click();   // the ✕ close button
        Assert.Empty(cut.FindAll(".trace-win"));
    }

    [Fact]
    public async Task Rect_callback_persists_to_the_store_and_restores_on_reopen()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row("WO-A")));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        // Simulate floating-window.js reporting the final rect after a drag —
        // the JSInvokable now lives on the shared FloatingWindow chrome.
        var win = cut.FindComponent<FloatingWindow>();
        var rect = new WindowRect { X = 120, Y = 80, W = 900, H = 600, Maximized = false };
        await cut.InvokeAsync(() => win.Instance.OnRectChanged_JS(rect));

        // Parent persisted it under the WoNo…
        Assert.Equal(rect, _winStore.Get("WO-A"));

        // …and a re-opened card receives the stored rect as its Rect param.
        cut.Find(".fw-light-close").Click();
        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());
        var reopened = cut.FindComponent<TraceabilityDetailDialog>();
        Assert.Equal(rect, reopened.Instance.Rect);
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
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

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
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

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
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

        var nos = cut.FindAll(".trace-prod tbody tr td.trace-prod-no").Select(td => td.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "1", "2" }, nos);
    }

    [Fact]
    public void Product_has_two_sections_and_tools_table_renders_variable_rows()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

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
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

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
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

        Assert.Empty(cut.FindAll(".trace-empty"));
        var fqcTab = cut.FindAll(".trace-tab").First(b => b.TextContent.Contains("FQC"));
        fqcTab.Click();
        Assert.Single(cut.FindAll(".trace-empty"));
        Assert.Contains("Chưa chốt dữ liệu FQC — dữ liệu chưa được đóng băng.", cut.Markup);
    }
}
