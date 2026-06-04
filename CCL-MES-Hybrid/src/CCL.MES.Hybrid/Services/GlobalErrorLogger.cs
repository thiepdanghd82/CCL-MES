using System.Globalization;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// P10.6a hotfix — global last-chance error trap. Wires
/// <see cref="AppDomain.UnhandledException"/> and
/// <see cref="TaskScheduler.UnobservedTaskException"/> so a background
/// task or async-void style exception cannot silently kill the
/// BlazorWebView renderer. The trap is INSTALLED ONCE at app boot
/// (idempotent guard) BEFORE any DI work runs, so even crashes during
/// the host-startup phase leave a forensic trail.
///
/// Every captured exception lands in two places:
///   1. <see cref="Console.WriteLine"/> with a boot-relative timestamp.
///   2. A line in <c>FileSystem.Current.AppDataDirectory/logs/error.log</c>
///      (kept tiny — 50 most-recent entries, rolled in place).
///
/// This trap is production-safe — observation-only, no runtime
/// semantic change. Removing the trap is a one-line edit in
/// <c>MauiProgram.cs</c>.
/// </summary>
public static class GlobalErrorLogger
{
    private static readonly object _lock = new();
    private static bool _installed;
    private static DateTime _bootUtc = DateTime.UtcNow;
    private static string? _logFilePath;
    private const int MaxLines = 50;

    /// <summary>Hook the global error events. Idempotent.</summary>
    public static void Install()
    {
        lock (_lock)
        {
            if (_installed) return;
            _installed = true;
            _bootUtc = DateTime.UtcNow;
            try
            {
                var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "logs");
                Directory.CreateDirectory(dir);
                _logFilePath = Path.Combine(dir, "error.log");
            }
            catch
            {
                try
                {
                    var dir = Path.Combine(Path.GetTempPath(), "ccl-mes-hybrid", "logs");
                    Directory.CreateDirectory(dir);
                    _logFilePath = Path.Combine(dir, "error.log");
                }
                catch { _logFilePath = null; }
            }

            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;

            Log("install", "GlobalErrorLogger armed at boot.");
        }
    }

    public static void Log(string source, string message, Exception? ex = null)
    {
        try
        {
            var bootElapsed = DateTime.UtcNow - _bootUtc;
            var nowLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var line = ex is null
                ? $"[ccl-err][{nowLocal}][T+{bootElapsed.TotalSeconds:F1}s][{source}] {message}"
                : $"[ccl-err][{nowLocal}][T+{bootElapsed.TotalSeconds:F1}s][{source}] {message} :: {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine(line);
            if (ex is not null)
                Console.WriteLine($"[ccl-err-stack] {ex}");
            AppendToRollingFile(line, ex);
        }
        catch { /* never throw from the logger */ }
    }

    private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log("AppDomain.UnhandledException", $"IsTerminating={e.IsTerminating}", ex);
    }

    private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // .NET 6+ default doesn't crash on UTE; SetObserved() future-
        // proofs against a runtime policy flip that would.
        Log("TaskScheduler.UnobservedTaskException",
            "background task threw without await", e.Exception);
        e.SetObserved();
    }

    private static void AppendToRollingFile(string line, Exception? ex)
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;
        try
        {
            List<string> existing;
            try { existing = File.Exists(_logFilePath) ? File.ReadAllLines(_logFilePath).ToList() : new(); }
            catch { existing = new(); }

            existing.Add(line);
            if (ex is not null) existing.Add($"  {ex.GetType().Name}: {ex.Message}");

            if (existing.Count > MaxLines)
                existing.RemoveRange(0, existing.Count - MaxLines);

            File.WriteAllLines(_logFilePath, existing);
        }
        catch { /* disk pressure — Console.WriteLine path already wrote */ }
    }

    /// <summary>Test-only helper. NEVER call from production code.</summary>
    internal static (bool installed, string? logPath) DiagnosticState() => (_installed, _logFilePath);
}
