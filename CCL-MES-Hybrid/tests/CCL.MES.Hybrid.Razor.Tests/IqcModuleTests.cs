using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Qms;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Shared.Iqc;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// feat/iqc-ticket + W5 showcard-migration — bUnit for the IQC module.
///
/// The Materials inspection SHOWCARD is now a real WindowManager window (mirror
/// Traceability): New Ticket → Materials + row right-click/dbl-click no longer
/// self-host a FloatingWindow under IqcModule; they call WM.Open(key, …,
/// typeof(MaterialsInspectionWindow)) so the WM host owns chrome/rect/focus/
/// dedupe/soft-cap. The module tests assert against the WindowManager; the
/// form-internals tests render MaterialsInspectionForm(Chrome=false) directly —
/// the reverse-lookup header + line table + create path are unchanged.
/// </summary>
public sealed class IqcModuleTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();
    private readonly WindowManager _wm = new();
    private readonly IqcChangeNotifier _notifier = new();

    private void Wire(string role = "QC")
    {
        _session.SetUser(role.ToLowerInvariant() + "-user", role);
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddSingleton<IFloatingWindowStore>(new InMemoryFloatingWindowStore());
        Services.AddSingleton<IWindowManager>(_wm);
        Services.AddSingleton<IIqcChangeNotifier>(_notifier);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(role.ToLowerInvariant() + "-user");
        auth.SetRoles(role);
    }

    // ── feat/iqc-module-tabs — 3 sub-tab + group picker ──

    [Fact]
    public void Renders_three_subtabs_default_dashboard()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-subtab-dashboard]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-subtab-data]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-subtab-newticket]"));
        // Dashboard is the default active tab.
        Assert.NotNull(cut.Find("[data-testid=iqc-dash]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-data]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-newticket]"));
    }

    [Fact]
    public void Dashboard_renders_real_kpi_counts_from_api()
    {
        Wire();
        _api.IqcDashboardImpl = () => Task.FromResult(new IqcDashboardResponse
        {
            Total = 42, Materials = 30, Chemical = 7, Tools = 3, Other = 2,
            Pending = 10, Pass = 28, Fail = 4,
        });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));

        Assert.Contains("42", cut.Find("[data-testid=iqc-kpi-total]").TextContent);
        Assert.Contains("30", cut.Find("[data-testid=iqc-kpi-materials]").TextContent);
        Assert.Contains("7", cut.Find("[data-testid=iqc-kpi-chemical]").TextContent);
        Assert.Contains("28", cut.Find("[data-testid=iqc-kpi-pass]").TextContent);
    }

    [Fact]
    public void Newticket_tab_shows_four_group_picker_cards()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));

        cut.Find("[data-testid=iqc-subtab-newticket]").Click();

        Assert.NotNull(cut.Find("[data-testid=iqc-grouppick]"));
        foreach (var g in new[] { "materials", "chemical", "tools", "other" })
            Assert.NotNull(cut.Find($"[data-testid=iqc-groupcard-{g}]"));
    }

    // ── W5 showcard-migration — inspection opens a WindowManager window ──

    [Fact]
    public void Pick_materials_opens_a_new_inspection_window_create_mode()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-newticket]").Click();
        Assert.Empty(_wm.Windows);

        cut.Find("[data-testid=iqc-groupcard-materials]").Click();

        // A create window in the WindowManager: unique "iqc-new:" key (never
        // deduped), MaterialsInspectionWindow content, no Ticket param (=create).
        var win = Assert.Single(_wm.Windows);
        Assert.StartsWith(WindowRegistryKeys.IqcNewKeyPrefix, win.Key);
        Assert.Equal(typeof(MaterialsInspectionWindow), win.ContentType);
        Assert.False(win.Parameters!.ContainsKey("Ticket"));   // create mode
        Assert.Equal(0, win.Parameters!["DebounceMs"]);
        // No self-hosted FloatingWindow renders inside the page anymore.
        Assert.Empty(cut.FindAll("[data-testid=iqc-insp-form]"));
        Assert.Empty(cut.FindAll(".trace-win"));
    }

    [Fact]
    public void Each_new_ticket_tap_opens_an_independent_window()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-newticket]").Click();

        cut.Find("[data-testid=iqc-groupcard-materials]").Click();
        cut.Find("[data-testid=iqc-groupcard-materials]").Click();

        // Two independent create windows — the "iqc-new:{Guid}" keys never dedupe.
        Assert.Equal(2, _wm.Windows.Count);
        Assert.All(_wm.Windows, w => Assert.StartsWith(WindowRegistryKeys.IqcNewKeyPrefix, w.Key));
        Assert.Equal(2, _wm.Windows.Select(w => w.Key).Distinct().Count());
    }

    [Fact]
    public void Right_click_open_menu_item_opens_saved_ticket_window_with_row_data()
    {
        Wire();
        var row = new IqcTicketListItem
        {
            Id = 5, ReceiptNo = "IQC-260819-0005", Group = "Materials",
            CodeIfs = "MC-OPEN", MaterialDescription = "Keo mở phiếu",
            LotBatchNo = "LOT-OPEN", Inspector = "qc-user",
            Result = "Pass", ReceivedDate = DateTime.UtcNow,
        };
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        { Total = 1, Page = 1, PageSize = 20, Items = new() { row } });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-data]").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-data-receipt]")));

        // Right-click → shared RowContextMenu → "Open".
        cut.FindAll("[data-testid=iqc-data-table] tbody tr")[0].ContextMenu();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role=menu]")));
        var open = cut.FindAll("[role=menu] [role=menuitem]")
                      .First(mi => mi.TextContent.Contains("Open", StringComparison.OrdinalIgnoreCase)
                                || mi.TextContent.Contains("Mở"));
        open.Click();

        // A saved-ticket window keyed "ticket:{ReceiptNo}", carrying the row.
        var win = Assert.Single(_wm.Windows);
        Assert.Equal(WindowRegistryKeys.IqcTicketKeyPrefix + "IQC-260819-0005", win.Key);
        Assert.Equal(typeof(MaterialsInspectionWindow), win.ContentType);
        Assert.Same(row, win.Parameters!["Ticket"]);
        Assert.Equal("IQC-260819-0005", win.Title);
    }

    [Fact]
    public void Double_click_row_opens_saved_ticket_window()
    {
        Wire();
        var row = new IqcTicketListItem
        {
            Id = 6, ReceiptNo = "IQC-260819-0006", Group = "Materials",
            CodeIfs = "MC-DBL", MaterialDescription = "Dán đôi", Result = "Pending",
            ReceivedDate = DateTime.UtcNow,
        };
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        { Total = 1, Page = 1, PageSize = 20, Items = new() { row } });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-data]").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-data-receipt]")));

        cut.FindAll("[data-testid=iqc-data-table] tbody tr")[0].DoubleClick();

        var win = Assert.Single(_wm.Windows);
        Assert.Equal(WindowRegistryKeys.IqcTicketKeyPrefix + "IQC-260819-0006", win.Key);
        Assert.Same(row, win.Parameters!["Ticket"]);
    }

    [Fact]
    public void Reopening_same_saved_ticket_dedupes_not_duplicates()
    {
        Wire();
        var row = new IqcTicketListItem
        {
            Id = 7, ReceiptNo = "IQC-260819-0007", Group = "Materials",
            CodeIfs = "MC-DUP", Result = "Pending", ReceivedDate = DateTime.UtcNow,
        };
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        { Total = 1, Page = 1, PageSize = 20, Items = new() { row } });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-data]").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-data-receipt]")));

        cut.FindAll("[data-testid=iqc-data-table] tbody tr")[0].DoubleClick();
        var firstId = _wm.Windows[0].Id;
        cut.FindAll("[data-testid=iqc-data-table] tbody tr")[0].DoubleClick();

        // Dedupe by "ticket:{ReceiptNo}" → still one window, same instance (keep-alive).
        Assert.Equal(firstId, Assert.Single(_wm.Windows).Id);
    }

    [Fact]
    public void Opening_beyond_the_soft_cap_is_blocked_with_a_notice()
    {
        Wire();
        for (var i = 0; i < _wm.SoftCap; i++)
            _wm.Open($"other:{i}", $"Other {i}", null, typeof(IqcModule));

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-newticket]").Click();
        cut.Find("[data-testid=iqc-groupcard-materials]").Click();

        // No IQC window opened (cap block) + the toast shows the SoftCap.
        Assert.DoesNotContain(_wm.Windows, w => w.Key.StartsWith(WindowRegistryKeys.IqcNewKeyPrefix));
        Assert.Contains(_wm.SoftCap.ToString(), cut.Find("[data-testid=iqc-toast]").TextContent);
    }

    [Fact]
    public void Notifier_change_refreshes_dashboard_kpi()
    {
        Wire();
        var total = 3;
        _api.IqcDashboardImpl = () => Task.FromResult(new IqcDashboardResponse { Total = total });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        Assert.Contains("3", cut.Find("[data-testid=iqc-kpi-total]").TextContent);

        // A ticket saved in a WM-hosted window fires the notifier → re-pull KPI.
        total = 4;
        _notifier.NotifyChanged();

        cut.WaitForAssertion(() =>
            Assert.Contains("4", cut.Find("[data-testid=iqc-kpi-total]").TextContent));
    }

    [Fact]
    public void Disposing_module_unsubscribes_from_the_notifier()
    {
        Wire();
        var calls = 0;
        _api.IqcDashboardImpl = () => { calls++; return Task.FromResult(new IqcDashboardResponse()); };

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        var afterMount = calls;   // ≥1 from OnInitialized

        DisposeComponents();      // IqcModule.Dispose → unsubscribe
        _notifier.NotifyChanged();

        // No further LoadDashboardAsync after dispose (handler detached).
        Assert.Equal(afterMount, calls);
    }

    [Fact]
    public void Pick_chemical_opens_placeholder_form_and_saves_group_chemical()
    {
        Wire();
        CreateIqcTicketBody? posted = null;
        _api.CreateIqcTicketImpl = body =>
        {
            posted = body;
            return Task.FromResult(new CreateIqcTicketResponse
            { Group = body.Group ?? "", ReceiptNo = "IQC-260819-0009", IqcInspectionId = 9, MatchStatus = "unmatched" });
        };

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-newticket]").Click();
        cut.Find("[data-testid=iqc-groupcard-chemical]").Click();

        // Separate placeholder form (Chemical), NOT the reverse-lookup search.
        Assert.NotNull(cut.Find("[data-testid=iqc-ph-form]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-ph-badge]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-search-input]"));
        Assert.Equal("Chemical", cut.Find("[data-testid=iqc-ph-form]").GetAttribute("data-group"));

        cut.Find("[data-testid=iqc-ph-lot]").Change("CHEM-LOT-1");
        cut.Find("[data-testid=iqc-ph-qty]").Change("25");
        cut.Find("[data-testid=iqc-ph-save]").Click();

        Assert.NotNull(posted);
        Assert.Equal("Chemical", posted!.Group);
        Assert.Equal("CHEM-LOT-1", posted.LotBatchNo);
        Assert.Equal(25, posted.Quantity);
        // Saved → back on New Ticket tab with a receipt confirmation.
        Assert.Contains("IQC-260819-0009", cut.Find("[data-testid=iqc-newticket-saved]").TextContent);
    }

    [Fact]
    public void Iqc_data_tab_renders_list_with_rowcontextmenu_no_actions_column()
    {
        Wire();
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        {
            Total = 2, Page = 1, PageSize = 20,
            Items = new()
            {
                new IqcTicketListItem { Id = 1, ReceiptNo = "IQC-260819-0001", Group = "Materials", CodeIfs = "MC-A", Result = "Pending", ReceivedDate = DateTime.UtcNow },
                new IqcTicketListItem { Id = 2, ReceiptNo = "IQC-260819-0002", Group = "Chemical", CodeIfs = "CH-B", Result = "Pass", ReceivedDate = DateTime.UtcNow },
            },
        });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-data]").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-data-receipt]").Count));
        // NO inline "Actions" column header (L35 — actions via RowContextMenu only).
        var headers = cut.FindAll("[data-testid=iqc-data-table] thead th").Select(th => th.TextContent);
        Assert.DoesNotContain(headers, h => h.Contains("Actions", StringComparison.OrdinalIgnoreCase)
                                          || h.Contains("Hành động", StringComparison.OrdinalIgnoreCase));

        // Right-click a row opens the shared context menu (role=menu).
        cut.FindAll("[data-testid=iqc-data-table] tbody tr")[0].ContextMenu();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role=menu]")));
    }

    [Fact]
    public void Iqc_data_group_filter_passes_group_to_api()
    {
        Wire();
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        { Total = 0, Page = 1, PageSize = 20, Items = new() });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-data]").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(_api.ListIqcTicketsCalls));

        cut.Find("[data-testid=iqc-data-group-chemical]").Click();

        cut.WaitForAssertion(() => Assert.Contains(_api.ListIqcTicketsCalls, c => c.Group == "Chemical"));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MaterialsInspectionForm — rendered DIRECTLY with Chrome=false (the body
    //  the WM host wraps). W5 showcard-migration: the form body + reverse-lookup
    //  header + line table + create path are unchanged; only the chrome moved.
    // ══════════════════════════════════════════════════════════════════════

    private void WireForm(string role = "QC")
    {
        _session.SetUser(role.ToLowerInvariant() + "-user", role);
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized(role.ToLowerInvariant() + "-user");
    }

    // Render the create-mode form (Chrome=false = WM body) with an optional
    // OnSaved sink so multi-create tests can capture the last response.
    private IRenderedComponent<MaterialsInspectionForm> RenderCreateForm(
        Action<CreateIqcTicketResponse>? onSaved = null)
        => RenderComponent<MaterialsInspectionForm>(p =>
        {
            p.Add(x => x.Chrome, false);
            p.Add(x => x.DebounceMs, 0);
            if (onSaved is not null)
                p.Add(x => x.OnSaved, onSaved);
        });

    [Fact]
    public void Chrome_false_renders_body_only_no_floatingwindow_double_wrap()
    {
        // The no-double-wrap contract: in WM-hosted mode (Chrome=false) the form
        // renders ONLY its body — NO FloatingWindow chrome (.trace-win) — so the
        // host's own FloatingWindow is the single wrapper.
        WireForm();
        var cut = RenderCreateForm();

        Assert.Empty(cut.FindAll(".trace-win"));                                 // no chrome
        Assert.Empty(cut.FindComponents<CCL.MES.Hybrid.Razor.Shared.FloatingWindow>());
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-form]"));                 // body still there
        Assert.Equal("create", cut.Find("[data-testid=iqc-insp-form]").GetAttribute("data-mode"));
    }

    [Fact]
    public void Chrome_true_self_hosts_a_floatingwindow_for_standalone_callers()
    {
        // The legacy standalone path still works: Chrome=true (default) wraps the
        // body in one FloatingWindow (single wrapper) for any hand-hosting caller.
        WireForm();
        var cut = RenderComponent<MaterialsInspectionForm>(p => p
            .Add(x => x.Chrome, true).Add(x => x.DebounceMs, 0));

        Assert.Single(cut.FindComponents<CCL.MES.Hybrid.Razor.Shared.FloatingWindow>());
        Assert.Single(cut.FindAll(".trace-win"));
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-form]"));
    }

    [Fact]
    public void Create_form_renders_reverse_lookup_header_and_stepper()
    {
        WireForm();
        var cut = RenderCreateForm();

        Assert.NotNull(cut.Find("[data-testid=iqc-search-input]"));           // reverse-lookup header
        Assert.NotNull(cut.Find("[data-testid=qms-stepper]"));                // 5-step stepper
        Assert.Equal(5, cut.FindAll("[data-testid=qms-stepper] .qms-step").Count);
        Assert.NotNull(cut.Find("[data-testid=qms-iqc-hsf-table]"));          // HSF documents table
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-savedraft]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-complete]"));
    }

    [Fact]
    public void View_mode_form_shows_read_only_header_no_save_buttons()
    {
        WireForm();
        var ticket = new IqcTicketListItem
        {
            Id = 5, ReceiptNo = "IQC-260819-0005", Group = "Materials",
            CodeIfs = "MC-OPEN", MaterialDescription = "Keo mở phiếu",
            LotBatchNo = "LOT-OPEN", Inspector = "qc-user", Result = "Pass",
            ReceivedDate = DateTime.UtcNow,
        };
        var cut = RenderComponent<MaterialsInspectionForm>(p => p
            .Add(x => x.Chrome, false).Add(x => x.DebounceMs, 0).Add(x => x.Ticket, ticket));

        Assert.Equal("view", cut.Find("[data-testid=iqc-insp-form]").GetAttribute("data-mode"));
        Assert.Contains("IQC-260819-0005", cut.Find("[data-testid=iqc-insp-header-view]").TextContent);
        Assert.Contains("Keo mở phiếu", cut.Find("[data-testid=iqc-insp-header-view]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=qms-stepper]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-insp-savedraft]"));   // read-only
    }

    [Fact]
    public void OnSaved_fires_the_notifier_via_the_wrapper()
    {
        // MaterialsInspectionWindow (the WM ContentType) routes OnSaved → the
        // IIqcChangeNotifier so a mounted IqcModule refreshes. Assert the wrapper
        // pokes the notifier when the form saves.
        WireForm();
        Services.AddSingleton<IIqcChangeNotifier>(_notifier);
        var fired = 0;
        _notifier.Changed += () => fired++;

        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("WRAP-1", "wrap")));
        _api.CreateIqcTicketImpl = _ => Task.FromResult(new CreateIqcTicketResponse
        { ReceiptNo = "IQC-260819-0099", IqcInspectionId = 99, MatchStatus = "matched" });

        var cut = RenderComponent<MaterialsInspectionWindow>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-search-input]").Input("wrap");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-WRAP");
        FillAllLineQty(cut, "5");
        cut.Find("[data-testid=iqc-insp-complete]").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, fired));
    }

    // ── feat/iqc-search-by-desc — search-by-description + multi-select ──

    private static IqcMaterialSearchResponse Results(params (string Code, string Desc)[] rows) => new()
    {
        TooShort = false,
        Total = rows.Length,
        Page = 1,
        PageSize = 20,
        Items = rows.Select(r => new IqcMaterialSearchItem { CodeIfs = r.Code, IfsDescription = r.Desc }).ToList(),
    };

    // feat/iqc-materials-line-table — nhập Quantity cho MỌI dòng line-items đã tick.
    private static void FillAllLineQty(IRenderedFragment cut, string qty)
    {
        var codes = cut.FindAll("[data-testid=iqc-line-row]")
            .Select(r => r.GetAttribute("data-code")!).ToList();
        foreach (var code in codes)
            QtyInputForCode(cut, code).Change(qty);
    }

    private static IElement QtyInputForCode(IRenderedFragment cut, string code) =>
        cut.FindAll("[data-testid=iqc-line-row]")
            .First(r => r.GetAttribute("data-code") == code)
            .QuerySelector("[data-testid=iqc-line-qty]")!;

    private static IElement UomChipForCode(IRenderedFragment cut, string code, string uom) =>
        cut.FindAll("[data-testid=iqc-line-row]")
            .First(r => r.GetAttribute("data-code") == code)
            .QuerySelectorAll("[data-testid=iqc-line-uom-chip]")
            .First(c => c.GetAttribute("data-uom") == uom);

    [Fact]
    public void Search_input_populates_codeifs_multiselect_list()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(
            Results(("NITTO-5000NS-01", "NITTO 5000NS a"), ("NITTO-5000NS-02", "NITTO 5000NS b")));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("NITTO 5000NS");

        cut.WaitForAssertion(() =>
            Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));
        Assert.Single(_api.SearchIqcMaterialCalls);
        Assert.Equal("NITTO 5000NS", _api.SearchIqcMaterialCalls[0].Desc);
    }

    [Fact]
    public void TooShort_desc_shows_hint_and_no_list()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(new IqcMaterialSearchResponse { TooShort = true });

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("NI");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=iqc-search-tooshort]")));
        Assert.Empty(cut.FindAll("[data-testid=iqc-codeifs-tick]"));
    }

    [Fact]
    public void Tick_toggles_selection_and_updates_count()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(
            Results(("MC-A", "a"), ("MC-B", "b"), ("MC-C", "c")));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[2].Click();
        Assert.Contains("2/3", cut.Find("[data-testid=iqc-codeifs-count]").TextContent);

        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        Assert.Contains("1/3", cut.Find("[data-testid=iqc-codeifs-count]").TextContent);
    }

    [Fact]
    public void Multi_create_ticks_three_codes_and_posts_three_bodies_with_distinct_lots()
    {
        WireForm();
        var n = 0;
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(
            Results(("MC-A", "a"), ("MC-B", "b"), ("MC-C", "c")));
        _api.CreateIqcTicketImpl = body => Task.FromResult(new CreateIqcTicketResponse
        {
            ReceiptNo = $"IQC-260819-000{++n}",
            IqcInspectionId = n,
            MaterialLotId = n,
            MatchStatus = "matched",
            LotStatus = "Quarantine",
        });

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        var codeCount = cut.FindAll("[data-testid=iqc-codeifs-tick]").Count;
        for (var i = 0; i < codeCount; i++)
            cut.FindAll("[data-testid=iqc-codeifs-tick]")[i].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-MC");
        FillAllLineQty(cut, "100");

        cut.Find("[data-testid=iqc-insp-complete]").Click();

        Assert.Equal(3, _api.CreateIqcTicketCalls.Count);
        var codes = _api.CreateIqcTicketCalls.Select(b => b.CodeIfs).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "MC-A", "MC-B", "MC-C" }, codes);
        var lots = _api.CreateIqcTicketCalls.Select(b => b.LotBatchNo).ToList();
        Assert.Equal(3, lots.Distinct().Count());
        Assert.All(lots, l => Assert.StartsWith("LOT-MC-0", l));

        // All succeeded → window stays open (keep-open) with an in-window
        // saved confirmation.
        Assert.Contains("IQC-260819-000", cut.Find("[data-testid=iqc-insp-saved]").TextContent);
    }

    [Fact]
    public void Single_code_keeps_base_lot_without_suffix()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("SOLO-1", "solo")));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("solo");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));

        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-SOLO");
        FillAllLineQty(cut, "5");
        cut.Find("[data-testid=iqc-insp-complete]").Click();

        Assert.Single(_api.CreateIqcTicketCalls);
        Assert.Equal("LOT-SOLO", _api.CreateIqcTicketCalls[0].LotBatchNo);
    }

    [Fact]
    public void Save_draft_button_also_posts_group_materials()
    {
        WireForm();
        CreateIqcTicketBody? posted = null;
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("DRAFT-1", "draft")));
        _api.CreateIqcTicketImpl = body =>
        {
            posted = body;
            return Task.FromResult(new CreateIqcTicketResponse
            { ReceiptNo = "IQC-260819-0011", IqcInspectionId = 11, MatchStatus = "matched" });
        };

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("draft");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-DRAFT");
        FillAllLineQty(cut, "3");

        cut.Find("[data-testid=iqc-insp-savedraft]").Click();

        Assert.NotNull(posted);
        Assert.Equal("Materials", posted!.Group);
        Assert.Equal("LOT-DRAFT", posted.LotBatchNo);
    }

    [Fact]
    public void Multi_create_partial_failure_keeps_form_open_and_reports_counts()
    {
        WireForm();
        var n = 0;
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("P-A", "a"), ("P-B", "b")));
        _api.CreateIqcTicketImpl = body =>
        {
            n++;
            if (body.CodeIfs == "P-B")
                throw new ApiException(409, new ApiError { Code = "lot.duplicate" });
            return Task.FromResult(new CreateIqcTicketResponse
            { ReceiptNo = "IQC-260819-0001", IqcInspectionId = 1, MatchStatus = "matched" });
        };

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("P");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        var pCount = cut.FindAll("[data-testid=iqc-codeifs-tick]").Count;
        for (var i = 0; i < pCount; i++)
            cut.FindAll("[data-testid=iqc-codeifs-tick]")[i].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-P");
        FillAllLineQty(cut, "10");
        cut.Find("[data-testid=iqc-insp-complete]").Click();

        Assert.Equal(2, _api.CreateIqcTicketCalls.Count);
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-form]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-error]"));
    }

    [Fact]
    public void Complete_posts_body_and_confirms_receipt_from_response()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("IFS-AB-200", "Keo AB-200")));
        _api.CreateIqcTicketImpl = body => Task.FromResult(new CreateIqcTicketResponse
        {
            ReceiptNo = "IQC-260819-0007",
            IqcInspectionId = 42,
            MaterialLotId = 7,
            MaterialDescription = "desc from server",
            IfsDescription = "ifs from server",
            MatchStatus = "matched",
            LotStatus = "Quarantine",
        });

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("Keo");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-260819-01");
        FillAllLineQty(cut, "100");

        cut.Find("[data-testid=iqc-insp-complete]").Click();

        Assert.Single(_api.CreateIqcTicketCalls);
        Assert.Equal("IFS-AB-200", _api.CreateIqcTicketCalls[0].CodeIfs);
        Assert.Equal("LOT-260819-01", _api.CreateIqcTicketCalls[0].LotBatchNo);
        Assert.Equal(100, _api.CreateIqcTicketCalls[0].Quantity);
        Assert.Equal("Materials", _api.CreateIqcTicketCalls[0].Group);
        Assert.Contains("IQC-260819-0007", cut.Find("[data-testid=iqc-insp-saved]").TextContent);
    }

    // ── feat/iqc-materials-line-table — bảng dòng-vật-tư ──

    private static IqcMaterialSearchResponse EnrichedResults(
        params (string Code, string Desc, string? Mother, double? Width)[] rows) => new()
    {
        TooShort = false,
        Total = rows.Length,
        Page = 1,
        PageSize = 20,
        Items = rows.Select(r => new IqcMaterialSearchItem
        {
            CodeIfs = r.Code, IfsDescription = r.Desc,
            MotherCode = r.Mother, WidthMm = r.Width, PartDescription = r.Desc,
        }).ToList(),
    };

    [Fact]
    public void Ticking_two_codes_renders_two_line_rows_with_mother_and_width()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(EnrichedResults(
            ("MC-A", "desc A", "MOTHER-A", 320.5),
            ("MC-B", "desc B", "MOTHER-B", 210)));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        Assert.Empty(cut.FindAll("[data-testid=iqc-line-row]"));

        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[1].Click();

        var rows = cut.FindAll("[data-testid=iqc-line-row]");
        Assert.Equal(2, rows.Count);
        var mothers = cut.FindAll("[data-testid=iqc-line-mother]").Select(x => x.TextContent).ToList();
        Assert.Contains("MOTHER-A", mothers);
        Assert.Contains("MOTHER-B", mothers);
        var widths = cut.FindAll("[data-testid=iqc-line-width]").Select(x => x.TextContent).ToList();
        Assert.Contains("320.5", widths);
        Assert.Contains("210", widths);
    }

    [Fact]
    public void Unselecting_line_checkbox_removes_the_row()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(EnrichedResults(
            ("MC-A", "a", "M-A", 100),
            ("MC-B", "b", "M-B", 200)));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[1].Click();
        Assert.Equal(2, cut.FindAll("[data-testid=iqc-line-row]").Count);

        cut.FindAll("[data-testid=iqc-line-select]")[0].Change(false);
        Assert.Single(cut.FindAll("[data-testid=iqc-line-row]"));
        Assert.Contains("1/2", cut.Find("[data-testid=iqc-codeifs-count]").TextContent);
    }

    [Fact]
    public void Per_row_qty_and_uom_flow_to_create_bodies()
    {
        WireForm();
        var n = 0;
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(EnrichedResults(
            ("MC-A", "a", "M-A", 100),
            ("MC-B", "b", "M-B", 200)));
        _api.CreateIqcTicketImpl = _ => Task.FromResult(new CreateIqcTicketResponse
        { ReceiptNo = $"IQC-260819-000{++n}", IqcInspectionId = n, MatchStatus = "matched" });

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();  // MC-A
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[1].Click();  // MC-B
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-MC");

        QtyInputForCode(cut, "MC-A").Change("7");
        QtyInputForCode(cut, "MC-B").Change("9");
        UomChipForCode(cut, "MC-B", "Pcs").Click();

        cut.Find("[data-testid=iqc-insp-complete]").Click();

        Assert.Equal(2, _api.CreateIqcTicketCalls.Count);
        var a = _api.CreateIqcTicketCalls.Single(b => b.CodeIfs == "MC-A");
        var bb = _api.CreateIqcTicketCalls.Single(b => b.CodeIfs == "MC-B");
        Assert.Equal(7, a.Quantity);
        Assert.Equal("Roll", a.Uom);
        Assert.Equal(9, bb.Quantity);
        Assert.Equal("Pcs", bb.Uom);
    }

    [Fact]
    public void Cannot_save_when_any_selected_row_has_no_qty()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(EnrichedResults(
            ("MC-A", "a", "M-A", 100),
            ("MC-B", "b", "M-B", 200)));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[1].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-MC");

        QtyInputForCode(cut, "MC-A").Change("5");
        Assert.True(cut.Find("[data-testid=iqc-insp-complete]").HasAttribute("disabled"));

        QtyInputForCode(cut, "MC-B").Change("6");
        Assert.False(cut.Find("[data-testid=iqc-insp-complete]").HasAttribute("disabled"));
    }

    [Fact]
    public void Uom_chip_toggles_active_state()
    {
        WireForm();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(EnrichedResults(
            ("MC-A", "a", "M-A", 100)));

        var cut = RenderCreateForm();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();

        var chips = cut.FindAll("[data-testid=iqc-line-uom-chip]");
        var roll = chips.First(c => c.GetAttribute("data-uom") == "Roll");
        var pcs = chips.First(c => c.GetAttribute("data-uom") == "Pcs");
        Assert.Contains("iqc-uom-on", roll.GetAttribute("class"));
        Assert.DoesNotContain("iqc-uom-on", pcs.GetAttribute("class"));

        pcs.Click();
        chips = cut.FindAll("[data-testid=iqc-line-uom-chip]");
        Assert.DoesNotContain("iqc-uom-on", chips.First(c => c.GetAttribute("data-uom") == "Roll").GetAttribute("class"));
        Assert.Contains("iqc-uom-on", chips.First(c => c.GetAttribute("data-uom") == "Pcs").GetAttribute("class"));
    }
}
