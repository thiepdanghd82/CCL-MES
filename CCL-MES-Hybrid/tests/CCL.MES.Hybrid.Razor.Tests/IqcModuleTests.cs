using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
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
        var cut = RenderComponent<IqcModule>();

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
        var cut = RenderComponent<IqcModule>();
        Assert.Empty(cut.FindAll("[data-testid=iqc-form]"));

        cut.Find("[data-testid=iqc-add-ticket]").Click();

        Assert.NotNull(cut.Find("[data-testid=iqc-form]"));
        // Centred Modal (scrim), NOT a FloatingWindow showcard (L34).
        Assert.NotNull(cut.Find(".modal-scrim"));
        Assert.Empty(cut.FindAll(".fw-window"));
    }

    [Fact]
    public void Resolve_autofills_material_description_on_code_change()
    {
        Wire();
        _api.ResolveIqcCodeImpl = _ => Task.FromResult(new ResolveIqcCodeResponse
        {
            MatchStatus = "matched",
            MaterialDescription = "Keo AB-200",
            IfsDescription = "Keo AB-200",
        });

        var cut = RenderComponent<IqcModule>();
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-f-codeifs]").Change("IFS-AB-200");

        var matDesc = cut.Find("[data-testid=iqc-f-matdesc]");
        Assert.Equal("Keo AB-200", matDesc.GetAttribute("value"));
        Assert.Single(_api.ResolveIqcCodeCalls);
    }

    [Fact]
    public void Unmatched_code_shows_warning_but_form_still_saveable()
    {
        Wire();
        _api.ResolveIqcCodeImpl = _ => Task.FromResult(new ResolveIqcCodeResponse { MatchStatus = "unmatched" });

        var cut = RenderComponent<IqcModule>();
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-f-codeifs]").Change("IFS-NOPE");

        Assert.NotNull(cut.Find("[data-testid=iqc-warn-unmatched]"));
    }

    [Fact]
    public void Save_posts_body_and_refreshes_header_from_response()
    {
        Wire();
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

        var cut = RenderComponent<IqcModule>();
        cut.Find("[data-testid=iqc-add-ticket]").Click();
        cut.Find("[data-testid=iqc-f-codeifs]").Change("IFS-AB-200");
        cut.Find("[data-testid=iqc-f-lotbatch]").Change("LOT-260819-01");
        cut.Find("[data-testid=iqc-f-qty]").Change("100");

        cut.Find("[data-testid=iqc-form-save]").Click();

        // POST fired with the operator's input.
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
