using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// P11.x — the Spec sheet "Print" button routes through <see cref="IPrintService"/>.
/// On a host that supports native WebView print (Mac Catalyst) it must take
/// the native path (WYSIWYG OS print panel) and NOT hit the server MigraDoc
/// download; on a host without native print (Windows / tests) it must fall
/// back to the MigraDoc sheet PDF. These render the REAL page so a future
/// re-wire that drops the fallback (or double-prints) surfaces in CI.
/// </summary>
public sealed class SpecDetailPrintTests : TestContext
{
    private sealed class FakePrintService : IPrintService
    {
        public bool NativeSupported { get; init; }
        public bool ReturnValue { get; init; }
        public List<string?> Calls { get; } = new();

        public bool IsNativePrintSupported => NativeSupported;

        public Task<bool> PrintCurrentViewAsync(string? jobName = null)
        {
            Calls.Add(jobName);
            return Task.FromResult(ReturnValue);
        }
    }

    private RecordingApi WireCommon(IPrintService print)
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
        Services.AddSingleton(print);
        this.AddTestAuthorization().SetAuthorized("engineer");
        return api;
    }

    [Fact]
    public void Native_print_supported_uses_native_path_and_skips_migradoc()
    {
        var print = new FakePrintService { NativeSupported = true, ReturnValue = true };
        var api = WireCommon(print);

        var cut = RenderComponent<SpecDetailPage>(p => p.Add(x => x.RevisionId, 42L));
        cut.Find(".spec-print-pdf").Click();

        // The handler switches to Full mode + awaits a render flush before
        // presenting, so poll until the native call lands.
        cut.WaitForAssertion(() => Assert.Single(print.Calls));
        // Native panel presented with a spec-scoped job name…
        Assert.StartsWith("SpecSheet_", print.Calls[0]);
        // …and the server MigraDoc PDF was NOT downloaded.
        Assert.Empty(api.DownloadSpecSheetPdfCalls);
        // Operator sees the "system print panel opened" info banner.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".grid-export-banner.is-info")));
    }

    [Fact]
    public void No_native_print_falls_back_to_migradoc_download()
    {
        var print = new FakePrintService { NativeSupported = false, ReturnValue = false };
        var api = WireCommon(print);

        var cut = RenderComponent<SpecDetailPage>(p => p.Add(x => x.RevisionId, 42L));
        cut.Find(".spec-print-pdf").Click();

        // Native never attempted; MigraDoc fallback downloaded the sheet.
        cut.WaitForAssertion(() => Assert.Single(api.DownloadSpecSheetPdfCalls));
        Assert.Empty(print.Calls);
    }
}
