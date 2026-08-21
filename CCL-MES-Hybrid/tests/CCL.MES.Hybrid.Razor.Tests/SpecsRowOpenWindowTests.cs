using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Grid;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Specs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P2-PR3 — Spec list row-open opens the detail in its OWN keep-alive floating
/// window (spec:{id}) via IWindowManager, NOT a shell navigation. The per-id
/// key dedupes so re-opening the same spec re-focuses the single window; the
/// RevisionId param is handed through so SpecDetailPage renders that revision.
/// </summary>
public sealed class SpecsRowOpenWindowTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly WindowManager _wm = new();

    private IRenderedComponent<Specs> Render()
    {
        _api.GetSpecsImpl = (_, _, _, _, _) => Task.FromResult(new NpiPagedRaw<SpecListItem>
        {
            Items = new[]
            {
                new SpecListItem { Id = 4242, SpecCode = "SP-42", Title = "Label A", RevisionCode = "R1", ProductCode = "P-1", ProductName = "Widget" },
                new SpecListItem { Id = 7, SpecCode = "SP-7", Title = "Label B", RevisionCode = "R1", ProductCode = "P-2", ProductName = "Gadget" },
            },
            Total = 2, Page = 1, PageSize = 50,
        });
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        Services.AddSingleton<IGridPreferenceStore>(new InMemoryGridPreferenceStore());
        Services.AddSingleton<IFileOpener>(new StubFileOpener());
        Services.AddSingleton<IFileSaver>(new StubFileSaver());
        Services.AddSingleton<IFilePickerService>(new StubFilePickerService());
        Services.AddSingleton<IWindowManager>(_wm);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("engineer");
        return RenderComponent<Specs>();
    }

    [Fact]
    public void Row_dblclick_opens_a_per_id_spec_detail_window_with_revisionid_param()
    {
        var cut = Render();

        cut.FindAll("tr.spec-row").First().DoubleClick();

        var win = Assert.Single(_wm.Windows);
        Assert.Equal("spec:4242", win.Key);
        Assert.Equal(typeof(SpecDetailPage), win.ContentType);
        Assert.NotNull(win.Parameters);
        Assert.Equal(4242L, Assert.IsType<long>(win.Parameters!["RevisionId"]));
    }

    [Fact]
    public void Reopening_the_same_spec_dedupes_to_one_window()
    {
        var cut = Render();

        cut.FindAll("tr.spec-row").First().DoubleClick();   // spec 4242
        cut.FindAll("tr.spec-row").First().DoubleClick();   // same id again

        Assert.Single(_wm.Windows);   // dedupe by key → still one window
    }

    [Fact]
    public void Two_different_specs_open_two_windows()
    {
        var cut = Render();

        // Re-query between clicks — the render tree is refreshed after the first
        // dispatch, so a cached NodeList would hold stale event handlers.
        cut.FindAll("tr.spec-row")[0].DoubleClick();   // spec 4242
        cut.FindAll("tr.spec-row")[1].DoubleClick();   // spec 7

        Assert.Equal(2, _wm.Windows.Count);
        Assert.Contains(_wm.Windows, w => w.Key == "spec:4242");
        Assert.Contains(_wm.Windows, w => w.Key == "spec:7");
    }

    private sealed class StubFileOpener : IFileOpener
    {
        public Task<bool> TryOpenAsync(string absolutePath) => Task.FromResult(true);
        public string GetSafeDownloadDirectory() => System.IO.Path.GetTempPath();
    }

    private sealed class StubFileSaver : IFileSaver
    {
        public Task<SaveOutcome> SaveAsync(string sourceFilePath, string suggestedFileName, CancellationToken ct = default)
            => Task.FromResult(new SaveOutcome { Saved = false });
    }
}
