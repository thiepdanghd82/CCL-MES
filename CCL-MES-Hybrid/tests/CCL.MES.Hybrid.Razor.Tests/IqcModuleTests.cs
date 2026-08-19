using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
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
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(role.ToLowerInvariant() + "-user");
        auth.SetRoles(role);
    }

    [Fact]
    public void Header_renders_nine_fields_and_add_button()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-header]"));
        // 9 header fields present.
        foreach (var id in new[] { "receipt", "codeifs", "matdesc", "ifsdesc",
                     "lotbatch", "manfdate", "maker", "supplier", "inspector" })
            Assert.NotNull(cut.Find($"[data-testid=iqc-h-{id}]"));

        Assert.NotNull(cut.Find("[data-testid=iqc-add-ticket]"));
    }

    [Fact]
    public void Add_button_opens_transactional_modal_form()
    {
        Wire();
        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        Assert.Empty(cut.FindAll("[data-testid=iqc-form]"));

        cut.Find("[data-testid=iqc-add-ticket]").Click();

        Assert.NotNull(cut.Find("[data-testid=iqc-form]"));
        // Centred Modal (scrim), NOT a FloatingWindow showcard (L34).
        Assert.NotNull(cut.Find(".modal-scrim"));
        Assert.Empty(cut.FindAll(".fw-window"));
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

    [Fact]
    public void Search_input_populates_codeifs_multiselect_list()
    {
        Wire();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(
            Results(("NITTO-5000NS-01", "NITTO 5000NS a"), ("NITTO-5000NS-02", "NITTO 5000NS b")));

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-add-ticket]").Click();
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
        cut.Find("[data-testid=iqc-add-ticket]").Click();
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
        cut.Find("[data-testid=iqc-add-ticket]").Click();
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
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-search-input]").Input("MC");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        // Re-query each iteration: a click re-renders + invalidates the list.
        var codeCount = cut.FindAll("[data-testid=iqc-codeifs-tick]").Count;
        for (var i = 0; i < codeCount; i++)
            cut.FindAll("[data-testid=iqc-codeifs-tick]")[i].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-MC");
        cut.Find("[data-testid=iqc-f-qty]").Change("100");

        cut.Find("[data-testid=iqc-form-save]").Click();

        // Three POSTs, one per ticked code, distinct suffixed lots.
        Assert.Equal(3, _api.CreateIqcTicketCalls.Count);
        var codes = _api.CreateIqcTicketCalls.Select(b => b.CodeIfs).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "MC-A", "MC-B", "MC-C" }, codes);
        var lots = _api.CreateIqcTicketCalls.Select(b => b.LotBatchNo).ToList();
        Assert.Equal(3, lots.Distinct().Count());
        Assert.All(lots, l => Assert.StartsWith("LOT-MC-0", l));

        // All succeeded → modal closed + header shows a receipt.
        Assert.Empty(cut.FindAll("[data-testid=iqc-form]"));
        Assert.Contains("IQC-260819-000", cut.Find("[data-testid=iqc-h-receipt]").TextContent);
    }

    [Fact]
    public void Single_code_keeps_base_lot_without_suffix()
    {
        Wire();
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(Results(("SOLO-1", "solo")));

        var cut = RenderComponent<IqcModule>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-search-input]").Input("solo");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));

        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-SOLO");
        cut.Find("[data-testid=iqc-f-qty]").Change("5");
        cut.Find("[data-testid=iqc-form-save]").Click();

        Assert.Single(_api.CreateIqcTicketCalls);
        Assert.Equal("LOT-SOLO", _api.CreateIqcTicketCalls[0].LotBatchNo);   // no -01
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
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-search-input]").Input("P");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=iqc-codeifs-tick]").Count));

        var pCount = cut.FindAll("[data-testid=iqc-codeifs-tick]").Count;
        for (var i = 0; i < pCount; i++)
            cut.FindAll("[data-testid=iqc-codeifs-tick]")[i].Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-P");
        cut.Find("[data-testid=iqc-f-qty]").Change("10");
        cut.Find("[data-testid=iqc-form-save]").Click();

        // Both attempted; one failed → form stays open with a partial banner.
        Assert.Equal(2, _api.CreateIqcTicketCalls.Count);
        Assert.NotNull(cut.Find("[data-testid=iqc-form]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-form-error]"));
    }

    [Fact]
    public void Save_posts_body_and_refreshes_header_from_response()
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
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-search-input]").Input("Keo");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-codeifs-tick]")));
        cut.Find("[data-testid=iqc-codeifs-tick]").Click();
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-260819-01");
        cut.Find("[data-testid=iqc-f-qty]").Change("100");

        cut.Find("[data-testid=iqc-form-save]").Click();

        // POST fired with the ticked code + operator's input.
        Assert.Single(_api.CreateIqcTicketCalls);
        Assert.Equal("IFS-AB-200", _api.CreateIqcTicketCalls[0].CodeIfs);
        Assert.Equal("LOT-260819-01", _api.CreateIqcTicketCalls[0].LotBatchNo);
        Assert.Equal(100, _api.CreateIqcTicketCalls[0].Quantity);

        // Header refreshed + modal closed.
        Assert.Empty(cut.FindAll("[data-testid=iqc-form]"));
        Assert.Contains("IQC-260819-0007", cut.Find("[data-testid=iqc-h-receipt]").TextContent);
        Assert.Contains("desc from server", cut.Find("[data-testid=iqc-h-matdesc]").TextContent);
    }
}
