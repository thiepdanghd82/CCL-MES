using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Grid;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Regression tests for the Specs (Engineer Spec list) toolbar:
///   • the "All types" chip clears the planner-TYPE axis (independent of the
///     active/trash/all VIEW axis) — fixes the "stuck on Silkscreen" trap;
///   • the CSV/Excel/PDF export buttons carry the secondary button classes
///     (the CSS fix restores their dark-bg contrast — previously the buttons
///     resolved to a transparent background via the undefined --c-ink-4 token).
/// </summary>
public sealed class SpecsToolbarTests : TestContext
{
    private readonly RecordingApi _api = new();

    private IRenderedComponent<Specs> Render()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        Services.AddSingleton<IGridPreferenceStore>(new InMemoryGridPreferenceStore());
        Services.AddSingleton<IFileOpener>(new StubFileOpener());
        Services.AddSingleton<IFileSaver>(new StubFileSaver());
        Services.AddSingleton<IFilePickerService>(new StubFilePickerService());
        Services.AddSingleton<IWindowManager>(new WindowManager());
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("op");
        return RenderComponent<Specs>();
    }

    [Fact]
    public void All_types_chip_clears_planner_filter_after_selecting_a_type()
    {
        var cut = Render();

        // Pick Silkscreen → the planner axis forwards SILK to the server.
        cut.FindAll(".spec-planner-filter-chip")
           .First(b => b.TextContent.Contains("Silkscreen")).Click();
        Assert.Equal("SILK", _api.GetSpecsCalls[^1].Planner);

        // "All types" resets the planner axis to null (grid no longer type-filtered).
        cut.Find("[data-testid='specs-planner-all']").Click();
        Assert.Null(_api.GetSpecsCalls[^1].Planner);

        // …and the all-types chip reads as the active one.
        Assert.Contains("is-active", cut.Find("[data-testid='specs-planner-all']").ClassList);
    }

    [Fact]
    public void All_types_chip_is_active_by_default_when_no_type_filter()
    {
        var cut = Render();
        Assert.Contains("is-active", cut.Find("[data-testid='specs-planner-all']").ClassList);
    }

    [Fact]
    public void Export_buttons_carry_secondary_button_classes_for_contrast()
    {
        var cut = Render();
        var exportBtns = cut.FindAll(".spec-export-group button");
        Assert.Equal(3, exportBtns.Count);   // CSV / Excel / PDF
        foreach (var b in exportBtns)
        {
            // The contrast fix keys off `.grid-btn.grid-btn-secondary` (dark bg +
            // light text). Assert the buttons still carry BOTH classes so the CSS
            // rule applies — a bare `.grid-btn` would render the invisible variant.
            Assert.Contains("grid-btn", b.ClassList);
            Assert.Contains("grid-btn-secondary", b.ClassList);
        }
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
