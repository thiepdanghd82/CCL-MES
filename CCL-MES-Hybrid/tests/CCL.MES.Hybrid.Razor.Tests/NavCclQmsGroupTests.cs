using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// CCL-QMS sidebar group — one home for every quality surface, mirroring the
/// NPI DATA group. Proves: (1) the 7 sub-links render for QC-policy roles;
/// (2) RBAC-by-omission — a role outside Admin/Supervisor/QC never sees the
/// group; (3) QC History / Library / Inspection Queue moved OUT of MONITORING
/// (an Operator still sees MONITORING but none of the QC links); (4) the new
/// labels flip EN/VI live.
/// </summary>
public sealed class NavCclQmsGroupTests : TestContext
{
    private readonly InMemoryLanguageService _lang = new();

    // The 7 CCL-QMS sub-links (href → present-in-markup check).
    private static readonly string[] QmsHrefs =
    {
        "/qms",            // Inspection Queue hub
        "/qms/ipqc",       // IPQC stage
        "/qms/fqc",        // FQC stage
        "/qms/oqc",        // OQC stage
        "/qms/history",    // QC History
        "/qc/library",     // QC Library
        "/quality/traceability", // Traceability data
    };

    private void Wire(string role)
    {
        var session = new StubAuthSession();
        session.SetUser("u", role);
        Services.AddSingleton<IAuthSession>(session);
        Services.AddSingleton<ILanguageService>(_lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("u");
        auth.SetRoles(role);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Supervisor")]
    [InlineData("QC")]
    public void Qms_group_renders_all_seven_links_for_qc_roles(string role)
    {
        Wire(role);
        var cut = RenderComponent<NavMenu>();

        Assert.Contains("CCL-QMS", cut.Markup);              // section header
        foreach (var href in QmsHrefs)
            Assert.Contains(cut.FindAll("a.app-nav-link-sub"),
                a => a.GetAttribute("href") == href);

        // The hub link pins Match.All so it doesn't stay lit on deeper routes.
        var hub = cut.FindAll("a.app-nav-link-sub").First(a => a.GetAttribute("href") == "/qms");
        Assert.NotNull(hub);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Engineer")]
    public void Qms_group_hidden_for_non_qc_roles_rbac_by_omission(string role)
    {
        Wire(role);
        var cut = RenderComponent<NavMenu>();

        // Group + every QC link gone…
        Assert.DoesNotContain("CCL-QMS", cut.Markup);
        foreach (var href in new[] { "/qms/history", "/qc/library", "/quality/traceability", "/qms/ipqc" })
            Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href") == href);

        // …but MONITORING (ungated) still renders, proving the QC links were
        // MOVED out of MONITORING, not merely role-gated inside it.
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/machines");
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/shop-orders");
    }

    [Fact]
    public void Qc_history_and_library_are_not_in_monitoring_section()
    {
        // Operator sees MONITORING but NOT the QC links → they left MONITORING.
        Wire("Operator");
        var cut = RenderComponent<NavMenu>();

        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/machines"); // MONITORING present
        Assert.DoesNotContain("QC History", cut.Markup);
        Assert.DoesNotContain("QC Library", cut.Markup);
        Assert.DoesNotContain("Lịch sử QC", cut.Markup);
        Assert.DoesNotContain("Thư viện QC", cut.Markup);
    }

    [Fact]
    public void New_qms_labels_flip_en_vi_live()
    {
        Wire("QC");
        var cut = RenderComponent<NavMenu>();

        // Default Vietnamese.
        Assert.Contains("Hàng chờ kiểm", cut.Markup);   // nav.qms.queue VI
        Assert.Contains("Lịch sử QC", cut.Markup);       // nav.qms.history VI

        _lang.Set(LanguageCode.English);                 // live flip

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Inspection Queue", cut.Markup); // nav.qms.queue EN
            Assert.Contains("QC History", cut.Markup);        // nav.qms.history EN
            Assert.Contains("CCL-QMS", cut.Markup);           // brand identical both langs
        });
    }
}
