using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Grid;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.RecentScans;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Hybrid.Client;

/// <summary>
/// One-call wiring for the MAUI app (or any other host) to register the
/// typed API client + auth machinery.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICclApiClient"/> backed by a typed HttpClient
    /// that flows through <see cref="AuthorizationDelegatingHandler"/>.
    /// The caller MUST also register an <see cref="ITokenStore"/>
    /// implementation — the MAUI host wires <c>MauiSecureTokenStore</c>;
    /// tests wire <see cref="InMemoryTokenStore"/>.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Reads section <c>CclApi</c> for
    /// <see cref="ApiClientOptions"/>.</param>
    public static IServiceCollection AddCclHybridClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiClientOptions>(configuration.GetSection("CclApi"));
        services.AddSingleton<IAuthSession, AuthSession>();

        // Refresh-client name — used internally by the auth handler so it
        // does NOT recurse through itself when calling /auth/refresh.
        services.AddHttpClient(RefreshHttpClientName, (sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiClientOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
            http.Timeout = opts.Timeout;
        });

        services.AddTransient<AuthorizationDelegatingHandler>(sp => new AuthorizationDelegatingHandler(
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<IAuthSession>(),
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(RefreshHttpClientName),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<AuthorizationDelegatingHandler>>()));

        services.AddHttpClient<ICclApiClient, CclApiClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiClientOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
            http.Timeout = opts.Timeout;
        })
        .AddHttpMessageHandler<AuthorizationDelegatingHandler>();

        // P10.3 hardware abstraction. The host project MAY override any of
        // these with platform-specific impls (Catalyst AVFoundation in W2,
        // Windows MediaCapture+ZXing in W3, Maui Preferences-backed
        // DeviceModeService — see CCL.MES.Hybrid host registration).
        // Defaults here are the stubs so a test harness or stripped-down
        // host can still resolve the graph.
        services.Configure<HardwareOptions>(configuration.GetSection("Hardware"));
        services.AddSingleton<IBarcodeScannerService, StubBarcodeScannerService>();
        services.AddSingleton<ILabelPrinterService, StubLabelPrinterService>();
        services.AddSingleton<IWeighScaleService, StubWeighScaleService>();
        services.AddSingleton<IIdleMonitor, StubIdleMonitor>();
        services.AddSingleton<IDeviceModeService, InMemoryDeviceModeService>();
        services.AddSingleton<IDeviceSettingsLauncher, StubDeviceSettingsLauncher>();

        // P10.5a grid preference store. The MAUI host overrides with the
        // Preferences-backed impl; tests + non-MAUI consumers keep the
        // in-memory default so DI resolution is always safe.
        services.AddSingleton<IGridPreferenceStore, InMemoryGridPreferenceStore>();

        // P10.5c-2 — file picker abstraction. MAUI host replaces with
        // CatalystFilePicker (FilePicker.Default backed). Tests + non-MAUI
        // consumers keep the stub which always returns null (operator-
        // cancelled), so import flows degrade cleanly without crashing.
        services.AddSingleton<IFilePickerService, StubFilePickerService>();

        // P10.5e-1 — file opener abstraction (Launcher.OpenAsync /
        // QuickLook). MAUI host replaces with CatalystFileOpener; tests
        // wire the stub which returns false from TryOpenAsync (caller
        // shows "Đã lưu file" without attempting to open).
        services.AddSingleton<IFileOpener, StubFileOpener>();

        // P10.5g — Save-As dialog abstraction (UIDocumentPicker
        // forExporting:). MAUI host replaces with CatalystFileSaver;
        // tests + non-MAUI consumers keep the stub which returns
        // SaveOutcome.Cancelled, so export flows degrade to "file kept
        // in sandbox" UX without crashing.
        services.AddSingleton<IFileSaver, StubFileSaver>();

        // P10.6f — Recent Scans sidebar widget store. MAUI host replaces
        // with MauiRecentScansService (Preferences-backed) so the list
        // survives app restarts. Tests + non-MAUI consumers keep the
        // in-memory default so DI resolution is safe in any harness.
        services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();

        // P10.6b — Appearance language preference store. MAUI host
        // replaces with MauiLanguageService (Preferences-backed) so the
        // operator's VN/EN choice survives app restarts. Tests + non-
        // MAUI hosts keep the in-memory default. Theme switcher is
        // out of scope for P10.6b — it ships in P10.6g.
        services.AddSingleton<ILanguageService, InMemoryLanguageService>();

        return services;
    }

    public const string RefreshHttpClientName = "CclApiRefresh";
}
