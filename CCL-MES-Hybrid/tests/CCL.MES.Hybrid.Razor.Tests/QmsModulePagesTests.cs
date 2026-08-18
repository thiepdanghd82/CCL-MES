using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// IQC (/qms/iqc) + iCRA (/qms/icra) scaffolds — the two QMS modules without a
/// full Hybrid UI yet. Rendering the page components proves the routes resolve
/// (compile + render, no 404 / dead link) and show a professional placeholder
/// card, not an empty screen.
/// </summary>
public sealed class QmsModulePagesTests : TestContext
{
    public QmsModulePagesTests()
    {
        Services.AddI18n();
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    [Fact]
    public void Iqc_page_renders_title_and_placeholder_card()
    {
        var cut = RenderComponent<IqcQueue>();
        Assert.Contains("IQC", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid='qms-placeholder-card']"));
        Assert.Contains("đầu vào", cut.Markup);   // qms.iqc.subtitle VI
    }

    [Fact]
    public void Icra_page_renders_title_and_placeholder_card()
    {
        var cut = RenderComponent<IcraBoard>();
        Assert.Contains("iCRA", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid='qms-placeholder-card']"));
        Assert.Contains("CAPA", cut.Markup);
    }
}
