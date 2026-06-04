using System.Reflection;

namespace CCL.MES.Hybrid.Client.Tests.Layout;

/// <summary>
/// P10.5g regression guard — the Mac Catalyst Tab/Enter focus-trap
/// workaround (<c>MacCatalystKeyboardFix.razor</c>) MUST stay injected
/// into every layout that hosts an EditForm. The fix shipped in PR #75
/// closing dotnet/maui#13934; the symptom (Tab not advancing fields,
/// Enter not submitting) silently reappears the moment one of the
/// layout files loses the tag.
///
/// We assert by string-grep on the source files because (a) the .razor
/// surface is the canonical declaration site, and (b) parsing Razor at
/// test time would drag the whole Microsoft.AspNetCore.Components SDK
/// onto the test graph for a single substring check. The grep keeps the
/// guard a few milliseconds and 0 deps — exactly what a CI canary
/// should cost.
///
/// The boot-time JS console line ("[keyboard-fix] …") is asserted
/// separately so a future maintainer cannot accidentally drop it while
/// refactoring the script body.
/// </summary>
public sealed class MacCatalystKeyboardFixRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            // Walk up from the test assembly location until we find the
            // CCL-MES-Hybrid folder. This survives `dotnet test` from any
            // cwd + the bin/Debug/net10.0/ test host layout.
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

        // The component tag may be self-closing or explicit; accept both
        // shapes so a future stylistic refactor doesn't false-fail.
        Assert.True(
            body.Contains("<MacCatalystKeyboardFix />", StringComparison.Ordinal) ||
            body.Contains("<MacCatalystKeyboardFix></MacCatalystKeyboardFix>", StringComparison.Ordinal),
            $"{filename} no longer references <MacCatalystKeyboardFix /> — Tab + Enter on Mac " +
            $"Catalyst will silently break the moment this fix evaporates. Re-inject the tag " +
            $"and update PR #75 lineage in the file header. Body excerpt: {body[..Math.Min(400, body.Length)]}");
    }

    [Fact]
    public void Component_emits_boot_log_line_for_observability()
    {
        // The "[keyboard-fix]" prefix is the agreed observability marker
        // — it surfaces in Safari Web Inspector + the DEBUG cclLog
        // bridge so an operator (or this test, by inspection) can
        // confirm the workaround actually ran. If a refactor removes
        // the log line, the next regression will be invisible — break
        // CI now, not on the next Mac Catalyst SDK bump.
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
}
