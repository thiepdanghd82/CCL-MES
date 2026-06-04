namespace CCL.MES.Hybrid.Client.Tests.Layout;

/// <summary>
/// P10.5g hotfix — source-level regression guards for the background-
/// crash containment pieces. The actual MAUI host code only builds
/// against the Catalyst TFM (UIKit + MAUI essentials), so this test
/// project (a plain .NET host) can't compile-link to it. We assert by
/// string-grep on the source files instead — same approach as the
/// MacCatalystKeyboardFix tests. A refactor that drops any of these
/// guard rails will break CI within seconds; a refactor that genuinely
/// re-shapes the code can update the test sentinel in the same commit.
///
/// The Henry incident (5g branch) was the "Blazor renderer dies after
/// N seconds, click does nothing" pattern caused by an unobserved
/// background-task exception. Three layers prevent it now:
///   1. GlobalErrorLogger arms AppDomain.UnhandledException +
///      TaskScheduler.UnobservedTaskException in MauiProgram before
///      any DI work runs.
///   2. DeviceHeartbeatHostedService wraps ExecuteAsync in an outer
///      try/catch that survives even if PeriodicTimer.WaitForNextTickAsync
///      throws (dotnet#71860).
///   3. App.xaml.cs catches per-service StartAsync failures so one bad
///      hosted service can't skip the others.
///
/// Each layer has at least one assertion below.
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

    [Fact]
    public void MauiProgram_arms_GlobalErrorLogger_before_DI_wiring()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "MauiProgram.cs"));
        // The Install() call must precede MauiApp.CreateBuilder() so a
        // throw inside the host build itself surfaces in the rolling
        // error log. We check ordering by index — Install < CreateBuilder.
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
        // SetObserved() prevents a future runtime policy flip from
        // crashing the process on unobserved task exceptions.
        Assert.Contains("SetObserved()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceHeartbeat_outer_loop_catches_anything_including_OCE_specially()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "Services", "DeviceHeartbeatHostedService.cs"));
        // Outer guard wrapping the entire RunLoopAsync. Two patterns we
        // assert are present:
        //  (a) explicit OperationCanceledException catch (normal shutdown)
        //  (b) catch-all Exception that logs via GlobalErrorLogger
        Assert.Contains("catch (OperationCanceledException)", body, StringComparison.Ordinal);
        Assert.Contains("GlobalErrorLogger.Log", body, StringComparison.Ordinal);
        // Inner WaitForNextTickAsync wrapper must exist + log on throw.
        Assert.Contains("SafeWaitNextTickAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void App_xaml_cs_catches_per_hosted_service_so_one_failure_does_not_skip_others()
    {
        var body = File.ReadAllText(Path.Combine(SourceRoot, "App.xaml.cs"));
        // The per-service try/catch sits INSIDE the foreach loop so a
        // throw from one StartAsync doesn't break the iteration. The
        // original P10.3 W4 code wrapped the whole foreach which meant
        // service N's failure prevented service N+1 from starting.
        var foreachIdx = body.IndexOf("foreach", StringComparison.Ordinal);
        var tryIdx = body.IndexOf("try", foreachIdx, StringComparison.Ordinal);
        var startIdx = body.IndexOf("hs.StartAsync", StringComparison.Ordinal);
        Assert.True(foreachIdx > 0, "App.xaml.cs missing foreach over hosted services.");
        Assert.True(tryIdx > foreachIdx,
            "App.xaml.cs must have a try block INSIDE the foreach so one StartAsync failure doesn't skip the next service.");
        Assert.True(startIdx > tryIdx,
            "App.xaml.cs hs.StartAsync(...) must sit inside the per-iteration try block.");
        Assert.Contains("GlobalErrorLogger.Log(\"HostedServiceStartup\"",
            body, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_page_carries_API_health_indicator()
    {
        var loginPath = Path.Combine(
            new DirectoryInfo(SourceRoot).Parent!.FullName,
            "CCL.MES.Hybrid.Razor", "Pages", "Login.razor");
        Assert.True(File.Exists(loginPath));
        var body = File.ReadAllText(loginPath);
        Assert.Contains("PingHealthAsync", body, StringComparison.Ordinal);
        Assert.Contains("login-health", body, StringComparison.Ordinal);
        Assert.Contains("RunHealthLoopAsync", body, StringComparison.Ordinal);
    }
}
