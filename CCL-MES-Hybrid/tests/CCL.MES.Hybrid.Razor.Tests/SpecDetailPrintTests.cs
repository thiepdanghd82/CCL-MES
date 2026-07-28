using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Specs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// The Spec sheet "Print" button downloads the server MigraDoc sheet PDF — the
/// guaranteed 1-page A4-landscape export. (Native WebView print was dropped for
/// this button: the OS paginates the DOM by height and spilled to 3 pages, so
/// it can't fit-to-one-page.) This renders the REAL page so a future re-wire
/// that stops downloading the PDF surfaces in CI.
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
        this.AddTestAuthorization().SetAuthorized("engineer");
        return api;
    }

    [Fact]
    public void Print_button_downloads_the_migradoc_sheet_pdf()
    {
        var api = WireCommon();

        var cut = RenderComponent<SpecDetailPage>(p => p.Add(x => x.RevisionId, 42L));
        cut.Find(".spec-print-pdf").Click();

        // The button routes to the server MigraDoc PDF (1-page export).
        cut.WaitForAssertion(() => Assert.Single(api.DownloadSpecSheetPdfCalls));
    }
}
