namespace CCL.MES.Hybrid.Client.Tests.Layout;

/// <summary>
/// P10.6a hotfix — source-level regression guards for the background-
/// crash containment pieces. The actual MAUI host code only builds
/// against the Catalyst TFM (UIKit + MAUI essentials), so this test
/// project (a plain .NET host) can't compile-link to it. We assert by
/// string-grep on the source files instead — same approach as the
/// MacCatalystKeyboardFix tests. A refactor that drops any of these
/// guard rails will break CI within seconds.
///
/// The Henry incident (now logged THREE times) was the "Blazor renderer
/// dies after N seconds, click does nothing" pattern caused by an
/// unobserved background-task exception OR a render-time throw.
/// Three layers prevent it:
///   1. GlobalErrorLogger arms AppDomain.UnhandledException +
///      TaskScheduler.UnobservedTaskException in MauiProgram before
///      any DI work runs.
///   2. DeviceHeartbeatHostedService wraps ExecuteAsync in an outer
///      try/catch that survives even if PeriodicTimer.WaitForNextTickAsync
///      throws (dotnet#71860).
///   3. App.xaml.cs catches per-service StartAsync failures so one bad
///      hosted service can't skip the others.
///
/// The Login page + Settings pages additionally avoid the
/// &lt;InputText&gt; + @@bind-Value:event="oninput" + @@onkeydown combo
/// that throws ChangeEventArgs→string on every keystroke under net10
/// MAUI Blazor Hybrid.
/// </summary>
public sealed class BackgroundCrashContainmentTests
{
    private static string SourceRoot
    {
        get
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(BackgroundCrashContainmentTests).Assembly.Location)!);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "CCL-MES-Hybrid")))
                dir = dir.Parent;
            return Path.Combine(
                dir?.FullName ?? throw new InvalidOperationException("repo root not found"),
                "CCL-MES-Hybrid", "src", "CCL.MES.Hybrid");
        }
    }

    private static string RazorPagePath(string filename) => Path.Combine(
        new DirectoryInfo(SourceRoot).Parent!.FullName,
        "CCL.MES.Hybrid.Razor", "Pages", filename);

    [Fact]
    public void MauiProgram_arms_GlobalErrorLogger_before_DI_wiring()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "MauiProgram.cs"));
        var installIdx = body.IndexOf("GlobalErrorLogger.Install()", StringComparison.Ordinal);
        var builderIdx = body.IndexOf("MauiApp.CreateBuilder()", StringComparison.Ordinal);
        Assert.True(installIdx > 0, "MauiProgram.cs must call GlobalErrorLogger.Install() at startup.");
        Assert.True(builderIdx > 0, "MauiProgram.cs must call MauiApp.CreateBuilder() (sanity check).");
        Assert.True(installIdx < builderIdx,
            "GlobalErrorLogger.Install() must run BEFORE MauiApp.CreateBuilder() so a DI throw is logged.");
    }

    [Fact]
    public void GlobalErrorLogger_wires_AppDomain_and_TaskScheduler_handlers()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "Services", "GlobalErrorLogger.cs"));
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", body, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException", body, StringComparison.Ordinal);
        Assert.Contains("SetObserved()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceHeartbeat_outer_loop_catches_anything_including_OCE_specially()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "Services", "DeviceHeartbeatHostedService.cs"));
        Assert.Contains("catch (OperationCanceledException)", body, StringComparison.Ordinal);
        Assert.Contains("GlobalErrorLogger.Log", body, StringComparison.Ordinal);
        Assert.Contains("SafeWaitNextTickAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void App_xaml_cs_catches_per_hosted_service_so_one_failure_does_not_skip_others()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "App.xaml.cs"));
        var foreachIdx = body.IndexOf("foreach", StringComparison.Ordinal);
        var tryIdx = body.IndexOf("try", foreachIdx, StringComparison.Ordinal);
        var startIdx = body.IndexOf("hs.StartAsync", StringComparison.Ordinal);
        Assert.True(foreachIdx > 0);
        Assert.True(tryIdx > foreachIdx,
            "App.xaml.cs must have a try block INSIDE the foreach so one StartAsync failure " +
            "doesn't skip the next service.");
        Assert.True(startIdx > tryIdx,
            "App.xaml.cs hs.StartAsync(...) must sit inside the per-iteration try block.");
        Assert.Contains("GlobalErrorLogger.Log(\"HostedServiceStartup\"",
            body, StringComparison.Ordinal);
    }

    // ── ChangeEventArgs throw — Login + Settings pages plain-input ──

    [Theory]
    [InlineData("Login.razor")]
    [InlineData("SettingsProfile.razor")]
    [InlineData("SettingsPassword.razor")]
    public void Page_uses_plain_input_not_InputText_to_avoid_ChangeEventArgs_throw(string filename)
    {
        // <InputText> + @bind-Value:event="oninput" + @onkeydown triple
        // combo throws ArgumentException
        //   "ChangeEventArgs cannot be converted to System.String"
        // on every keystroke under net10 MAUI Blazor Hybrid. The throw
        // used to silently corrupt the renderer dispatcher (the "click
        // does nothing" symptom Henry filed three times). Refuse the
        // re-introduction of the combo on these forms.
        var path = RazorPagePath(filename);
        Assert.True(File.Exists(path), $"Page missing: {path}");
        var body = File.ReadAllText(path);
        Assert.DoesNotContain("<InputText", body, StringComparison.Ordinal);
    }

    [Fact]
    public void No_Razor_page_combines_InputText_with_bind_Value_event_oninput()
    {
        // Repo-wide tripwire — any new page that re-introduces the
        // throwing combo will fail CI with the file path that broke.
        var razorRoot = Path.Combine(
            new DirectoryInfo(SourceRoot).Parent!.FullName,
            "CCL.MES.Hybrid.Razor");
        var offenders = Directory
            .EnumerateFiles(razorRoot, "*.razor", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p =>
            {
                var b = File.ReadAllText(p);
                return b.Contains("<InputText", StringComparison.Ordinal) &&
                       b.Contains("@bind-Value:event=\"oninput\"", StringComparison.Ordinal);
            })
            .ToList();
        Assert.True(offenders.Count == 0,
            $"<InputText> + @bind-Value:event=\"oninput\" combo throws " +
            $"ChangeEventArgs→string on Mac Catalyst Hybrid. Use plain <input> + " +
            $"@bind + @bind:event=\"oninput\" instead (see Lock.razor / Login.razor). " +
            $"Offending file(s): {string.Join(", ", offenders)}");
    }
}
