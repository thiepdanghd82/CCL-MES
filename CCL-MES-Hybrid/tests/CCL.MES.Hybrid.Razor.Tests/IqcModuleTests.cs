using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// feat/iqc-ticket — bUnit for the IQC module header + Add-ticket form.
/// Verifies the 9-field header renders, the Add button opens a centred Modal
/// (L34 transactional), resolve auto-fills description, and Save posts a
/// CreateIqcTicketBody then refreshes the header from the server response.
/// </summary>
public sealed class IqcModuleTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    private void Wire(string role = "QC")
    {
        _session.SetUser(role.ToLowerInvariant() + "-user", role);
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddSingleton<IFloatingWindowStore>(new InMemoryFloatingWindowStore());
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

    [Fact]
    public void Pick_materials_opens_full_inspection_showcard_create_mode()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-newticket]").Click();
        Assert.Empty(cut.FindAll("[data-testid=iqc-insp-form]"));

        cut.Find("[data-testid=iqc-groupcard-materials]").Click();

        // Full inspection form (MaterialsInspectionForm) in a FloatingWindow
        // showcard (L34), create mode → reverse-lookup header + 5-step stepper
        // + HSF table + Save draft / Complete ticket. NOT a centred Modal.
        var form = cut.Find("[data-testid=iqc-insp-form]");
        Assert.Equal("create", form.GetAttribute("data-mode"));
        Assert.NotNull(cut.Find("[data-testid=iqc-search-input]"));           // reverse-lookup header
        Assert.NotNull(cut.Find("[data-testid=qms-stepper]"));                // 5-step stepper restored
        Assert.Equal(5, cut.FindAll("[data-testid=qms-stepper] .qms-step").Count);
        Assert.NotNull(cut.Find("[data-testid=qms-iqc-hsf-table]"));          // HSF documents table
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-savedraft]"));        // Save draft
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-complete]"));         // Complete ticket
        Assert.Single(cut.FindAll(".trace-win"));                            // FloatingWindow chrome
        Assert.Empty(cut.FindAll(".modal-scrim"));                           // NOT a centred modal
    }

    [Fact]
    public void Right_click_open_menu_item_opens_inspection_showcard_with_row_data()
    {
        Wire();
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        {
            Total = 1, Page = 1, PageSize = 20,
            Items = new()
            {
                new IqcTicketListItem
                {
                    Id = 5, ReceiptNo = "IQC-260819-0005", Group = "Materials",
                    CodeIfs = "MC-OPEN", MaterialDescription = "Keo mở phiếu",
                    LotBatchNo = "LOT-OPEN", Inspector = "qc-user",
                    Result = "Pass", ReceivedDate = DateTime.UtcNow,
                },
            },
        });

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

        // Inspection showcard opens in VIEW mode, loaded with the row's data.
        var form = cut.Find("[data-testid=iqc-insp-form]");
        Assert.Equal("view", form.GetAttribute("data-mode"));
        Assert.Contains("IQC-260819-0005", cut.Find("[data-testid=iqc-insp-header-view]").TextContent);
        Assert.Contains("Keo mở phiếu", cut.Find("[data-testid=iqc-insp-header-view]").TextContent);
        // Same full form: stepper + HSF present in view mode too.
        Assert.NotNull(cut.Find("[data-testid=qms-stepper]"));
        Assert.NotNull(cut.Find("[data-testid=qms-iqc-hsf-table]"));
        // View mode is read-only → no Save draft / Complete buttons.
        Assert.Empty(cut.FindAll("[data-testid=iqc-insp-savedraft]"));
        Assert.Single(cut.FindAll(".trace-win"));
    }

    [Fact]
    public void Double_click_row_opens_inspection_showcard_with_row_data()
    {
        Wire();
        _api.ListIqcTicketsImpl = (_, _, _, _) => Task.FromResult(new IqcTicketListResponse
        {
            Total = 1, Page = 1, PageSize = 20,
            Items = new()
            {
                new IqcTicketListItem
                {
                    Id = 6, ReceiptNo = "IQC-260819-0006", Group = "Materials",
                    CodeIfs = "MC-DBL", MaterialDescription = "Dán đôi", Result = "Pending",
                    ReceivedDate = DateTime.UtcNow,
                },
            },
        });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-subtab-data]").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-data-receipt]")));

        // Double-click the row → same behaviour as right-click "Open".
        cut.FindAll("[data-testid=iqc-data-table] tbody tr")[0].DoubleClick();

        var form = cut.Find("[data-testid=iqc-insp-form]");
        Assert.Equal("view", form.GetAttribute("data-mode"));
        Assert.Contains("IQC-260819-0006", cut.Find("[data-testid=iqc-insp-header-view]").TextContent);
        Assert.Single(cut.FindAll(".trace-win"));
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

    // ── feat/iqc-search-by-desc — search-by-description + multi-select ──

    // Open the Materials reverse-lookup form via New Ticket tab → Materials card.
    private static void OpenMaterialsForm(IRenderedComponent<IqcModule> cut)
    {
        cut.Find("[data-testid=iqc-subtab-newticket]").Click();
        cut.Find("[data-testid=iqc-groupcard-materials]").Click();
    }

    private static IqcMaterialSearchResponse Results(params (string Code, string Desc)[] rows) => new()
    {
        TooShort = false,
        Total = rows.Length,
        Page = 1,
        PageSize = 20,
        Items = rows.Select(r => new IqcMaterialSearchItem { CodeIfs = r.Code, IfsDescription = r.Desc }).ToList(),
    };

    [Fact]
    public void Search_input_populates_codeifs_multiselect_list()
    {
        Wire();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(
            Results(("NITTO-5000NS-01", "NITTO 5000NS a"), ("NITTO-5000NS-02", "NITTO 5000NS b")));

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("NITTO 5000NS");

        // Debounce fires; wait for the two rows to render.
        cut.WaitForAssertion(() =>
            Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));
        Assert.Single(_api.SearchIqcMaterialCalls);
        Assert.Equal("NITTO 5000NS", _api.SearchIqcMaterialCalls[0].Desc);
    }

    [Fact]
    public void TooShort_desc_shows_hint_and_no_list()
    {
        Wire();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(new IqcMaterialSearchResponse { TooShort = true });

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("NI");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=iqc-search-tooshort]")));
        Assert.Empty(cut.FindAll("[data-testid=iqc-codeifs-tick]"));
    }

    [Fact]
    public void Tick_toggles_selection_and_updates_count()
    {
        Wire();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(
            Results(("MC-A", "a"), ("MC-B", "b"), ("MC-C", "c")));

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        // Tick two rows → count reads "2/3".
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[2].Click();
        Assert.Contains("2/3", cut.Find("[data-testid=iqc-codeifs-count]").TextContent);

        // Untick the first → "1/3".
        cut.FindAll("[data-testid=iqc-codeifs-tick]")[0].Click();
        Assert.Contains("1/3", cut.Find("[data-testid=iqc-codeifs-count]").TextContent);
    }

    [Fact]
    public void Multi_create_ticks_three_codes_and_posts_three_bodies_with_distinct_lots()
    {
        Wire();
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

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        // Re-query each iteration: a click re-renders + invalidates the list.
        var codeCount = cut.FindAll("[data-testid=iqc-codeifs-tick]").Count;
        for (var i = 0; i < codeCount; i++)
            cut.FindAll("[data-testid=iqc-codeifs-tick]")[i].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-MC");
        cut.Find("[data-testid=iqc-f-qty]").Change("100");

        cut.Find("[data-testid=iqc-insp-complete]").Click();

        // Three POSTs, one per ticked code, distinct suffixed lots.
        Assert.Equal(3, _api.CreateIqcTicketCalls.Count);
        var codes = _api.CreateIqcTicketCalls.Select(b => b.CodeIfs).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "MC-A", "MC-B", "MC-C" }, codes);
        var lots = _api.CreateIqcTicketCalls.Select(b => b.LotBatchNo).ToList();
        Assert.Equal(3, lots.Distinct().Count());
        Assert.All(lots, l => Assert.StartsWith("LOT-MC-0", l));

        // All succeeded → showcard stays open (keep-open) with an in-window
        // saved confirmation; New Ticket tab underneath also confirms a receipt.
        Assert.Contains("IQC-260819-000", cut.Find("[data-testid=iqc-insp-saved]").TextContent);
        Assert.Contains("IQC-260819-000", cut.Find("[data-testid=iqc-newticket-saved]").TextContent);
    }

    [Fact]
    public void Single_code_keeps_base_lot_without_suffix()
    {
        Wire();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("SOLO-1", "solo")));

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("solo");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));

        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-SOLO");
        cut.Find("[data-testid=iqc-f-qty]").Change("5");
        cut.Find("[data-testid=iqc-insp-complete]").Click();

        Assert.Single(_api.CreateIqcTicketCalls);
        Assert.Equal("LOT-SOLO", _api.CreateIqcTicketCalls[0].LotBatchNo);   // no -01
    }

    [Fact]
    public void Save_draft_button_also_posts_group_materials()
    {
        Wire();
        CreateIqcTicketBody? posted = null;
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("DRAFT-1", "draft")));
        _api.CreateIqcTicketImpl = body =>
        {
            posted = body;
            return Task.FromResult(new CreateIqcTicketResponse
            { ReceiptNo = "IQC-260819-0011", IqcInspectionId = 11, MatchStatus = "matched" });
        };

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("draft");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-DRAFT");
        cut.Find("[data-testid=iqc-f-qty]").Change("3");

        cut.Find("[data-testid=iqc-insp-savedraft]").Click();

        Assert.NotNull(posted);
        Assert.Equal("Materials", posted!.Group);          // same PA-A create path
        Assert.Equal("LOT-DRAFT", posted.LotBatchNo);
    }

    [Fact]
    public void Multi_create_partial_failure_keeps_form_open_and_reports_counts()
    {
        Wire();
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

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("P");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        var pCount = cut.FindAll("[data-testid=iqc-codeifs-tick]").Count;
        for (var i = 0; i < pCount; i++)
            cut.FindAll("[data-testid=iqc-codeifs-tick]")[i].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-P");
        cut.Find("[data-testid=iqc-f-qty]").Change("10");
        cut.Find("[data-testid=iqc-insp-complete]").Click();

        // Both attempted; one failed → showcard stays open with a partial banner.
        Assert.Equal(2, _api.CreateIqcTicketCalls.Count);
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-form]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-insp-error]"));
    }

    [Fact]
    public void Complete_posts_body_and_confirms_receipt_from_response()
    {
        Wire();
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

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        OpenMaterialsForm(cut);
        cut.Find("[data-testid=iqc-search-input]").Input("Keo");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-260819-01");
        cut.Find("[data-testid=iqc-f-qty]").Change("100");

        cut.Find("[data-testid=iqc-insp-complete]").Click();

        // POST fired with the ticked code + operator's input.
        Assert.Single(_api.CreateIqcTicketCalls);
        Assert.Equal("IFS-AB-200", _api.CreateIqcTicketCalls[0].CodeIfs);
        Assert.Equal("LOT-260819-01", _api.CreateIqcTicketCalls[0].LotBatchNo);
        Assert.Equal(100, _api.CreateIqcTicketCalls[0].Quantity);

        // Materials POST carries Group=Materials (additive default via form).
        Assert.Equal("Materials", _api.CreateIqcTicketCalls[0].Group);
        // Showcard keep-open with in-window confirmation + New Ticket tab receipt.
        Assert.Contains("IQC-260819-0007", cut.Find("[data-testid=iqc-insp-saved]").TextContent);
        Assert.Contains("IQC-260819-0007", cut.Find("[data-testid=iqc-newticket-saved]").TextContent);
    }
}
