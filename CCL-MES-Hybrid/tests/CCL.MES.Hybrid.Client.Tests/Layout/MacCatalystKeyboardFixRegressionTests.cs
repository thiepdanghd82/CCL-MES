using System.Reflection;

namespace CCL.MES.Hybrid.Client.Tests.Layout;

/// <summary>
/// P10.6a regression guard — the Mac Catalyst Tab/Enter focus-trap
/// workaround (<c>MacCatalystKeyboardFix.razor</c>) MUST stay injected
/// into every layout that hosts an EditForm. The fix shipped in PR #75
/// closing dotnet/maui#13934; the symptom (Tab not advancing fields,
/// Enter not submitting) silently reappears the moment one of the
/// layout files loses the tag.
///
/// These tests are string-grep on the source files because (a) the
/// .razor surface is the canonical declaration site, and (b) parsing
/// Razor at test time would drag the Components SDK onto the test
/// graph for a single substring check. The grep keeps the guard a
/// few milliseconds and 0 deps.
///
/// HISTORY:
///   - Initial guard shipped 3fff2d0 (P10.5g) after the same bug
///     repro'd twice.
///   - REGRESSED on main when the P10.5g hotfix series (4 commits) was
///     not fully merged — the lessons were documented but the code
///     was missing. PR #91 verify uncovered Tab + Enter both broken
///     again. This hotfix re-applies + re-enables the guards
///     unconditionally so a future revert breaks CI not operators.
/// </summary>
public sealed class MacCatalystKeyboardFixRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(MacCatalystKeyboardFixRegressionTests).Assembly.Location)!);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "CCL-MES-Hybrid")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate repo root from test assembly path.");
        }
    }

    private static string LayoutPath(string filename) => Path.Combine(
        RepoRoot,
        "CCL-MES-Hybrid", "src", "CCL.MES.Hybrid.Razor", "Shared", filename);

    [Theory]
    [InlineData("MainLayout.razor")]
    [InlineData("EmptyLayout.razor")]
    public void Layout_includes_MacCatalystKeyboardFix_component(string filename)
    {
        var path = LayoutPath(filename);
        Assert.True(File.Exists(path), $"Layout file missing: {path}");
        var body = File.ReadAllText(path);

        Assert.True(
            body.Contains("<MacCatalystKeyboardFix />", StringComparison.Ordinal) ||
            body.Contains("<MacCatalystKeyboardFix></MacCatalystKeyboardFix>", StringComparison.Ordinal),
            $"{filename} no longer references <MacCatalystKeyboardFix /> — Tab + Enter on Mac " +
            $"Catalyst will silently break the moment this fix evaporates. Re-inject the tag " +
            $"and update PR #75 lineage in the file header.");
    }

    [Fact]
    public void Component_emits_boot_log_line_for_observability()
    {
        // "[keyboard-fix]" prefix is the agreed observability marker —
        // surfaces in Safari Web Inspector + the DEBUG cclLog bridge so
        // an operator (or this test) can confirm the workaround
        // actually ran. If a refactor removes the log line, the next
        // regression will be invisible — break CI now.
        var path = LayoutPath("MacCatalystKeyboardFix.razor");
        Assert.True(File.Exists(path), $"Component file missing: {path}");
        var body = File.ReadAllText(path);
        Assert.Contains("[keyboard-fix]", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Component_carries_both_UA_and_WKWebView_detection_signals()
    {
        // Two-signal detection (UA token combo OR WKWebView surface
        // probe) is the guard against Catalyst SDK bumps that drop the
        // "Mobile/" UA token. Removing either signal narrows the
        // detect window — refuse it.
        var path = LayoutPath("MacCatalystKeyboardFix.razor");
        var body = File.ReadAllText(path);
        Assert.Contains("uaMatchesCatalyst", body, StringComparison.Ordinal);
        Assert.Contains("wkMatchesCatalyst", body, StringComparison.Ordinal);
        Assert.Contains("window.webkit", body, StringComparison.Ordinal);
    }

    // ── P10.6a hotfix — renderer crash containment ──────────────────

    [Theory]
    [InlineData("MainLayout.razor")]
    [InlineData("EmptyLayout.razor")]
    public void Layout_wraps_body_in_RendererCrashBoundary(string filename)
    {
        // The crash boundary is the foreground-path companion to the
        // background-path GlobalErrorLogger. Dropping it from either
        // layout means a render-time throw in a child page takes the
        // BlazorWebView dispatcher with it — the "click does nothing"
        // symptom Henry has filed three times now. Refuse the merge.
        var path = LayoutPath(filename);
        Assert.True(File.Exists(path));
        var body = File.ReadAllText(path);
        Assert.Contains("<RendererCrashBoundary>", body, StringComparison.Ordinal);
        Assert.Contains("</RendererCrashBoundary>", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RendererCrashBoundary_inherits_ErrorBoundaryBase_and_logs_via_OnErrorAsync()
    {
        var path = LayoutPath("RendererCrashBoundary.razor");
        Assert.True(File.Exists(path), $"Component missing: {path}");
        var body = File.ReadAllText(path);
        Assert.Contains("ErrorBoundaryBase", body, StringComparison.Ordinal);
        Assert.Contains("OnErrorAsync", body, StringComparison.Ordinal);
        Assert.Contains("[renderer-crash]", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MacCatalystKeyboardFix_carries_always_on_JS_error_capture()
    {
        // Production-safe JS-side capture for window.onerror +
        // unhandledrejection. Sentinels "[js-uncaught]" +
        // "[js-unhandled-rejection]" let ops-side log tails grep
        // without parsing structure.
        var path = LayoutPath("MacCatalystKeyboardFix.razor");
        var body = File.ReadAllText(path);
        Assert.Contains("[js-uncaught]", body, StringComparison.Ordinal);
        Assert.Contains("[js-unhandled-rejection]", body, StringComparison.Ordinal);
        Assert.Contains("__cclJsErrorLogger", body, StringComparison.Ordinal);
    }
}
