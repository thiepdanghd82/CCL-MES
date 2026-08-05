using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Printing;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Specs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// The Spec sheet print/export toolbar: In (native — hidden on non-Catalyst
/// hosts) · PDF (server MigraDoc) · Excel (server .xlsx). These render the REAL
/// page against a stub host (native print unavailable) so the PDF + Excel
/// buttons must route to their respective server downloads.
/// </summary>
public sealed class SpecDetailPrintTests : TestContext
{
    private RecordingApi WireCommon()
    {
        var api = new RecordingApi
        {
            GetSpecDetailImpl = (_, _) => Task.FromResult<SpecDetailItem?>(new SpecDetailItem
            {
                Id = 42,
                SpecCode = "SPEC-42",
                RevisionCode = "A",
                RefNo = "REF-9",
                ProductCode = "P-1",
                Status = SpecRevisionStatus.Approved,
            }),
            GetDrawingsByRevisionImpl = (_, _) => Task.FromResult(new List<DrawingKindSlot>()),
        };
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddI18n();
        Services.AddSingleton<IFileOpener>(new StubFileOpener());
        Services.AddSingleton<IFileSaver>(new StubFileSaver());
        Services.AddSingleton<IPrintService>(new StubPrintService()); // native unavailable → In hidden
        this.AddTestAuthorization().SetAuthorized("engineer");
        return api;
    }

    [Fact]
    public void Pdf_button_downloads_the_migradoc_sheet_pdf()
    {
        var api = WireCommon();
        var cut = RenderComponent<SpecDetailPage>(p => p.Add(x => x.RevisionId, 42L));

        // Native print unavailable → only PDF + Excel buttons render.
        var buttons = cut.FindAll(".spec-print-group button");
        Assert.Equal(2, buttons.Count);
        buttons[0].Click();   // PDF

        cut.WaitForAssertion(() => Assert.Single(api.DownloadSpecSheetPdfCalls));
        Assert.Empty(api.DownloadSpecSheetXlsxCalls);
    }

    [Fact]
    public void Excel_button_downloads_the_sheet_xlsx()
    {
        var api = WireCommon();
        var cut = RenderComponent<SpecDetailPage>(p => p.Add(x => x.RevisionId, 42L));

        cut.FindAll(".spec-print-group button")[1].Click();   // Excel

        cut.WaitForAssertion(() => Assert.Single(api.DownloadSpecSheetXlsxCalls));
        Assert.Empty(api.DownloadSpecSheetPdfCalls);
    }
}
