using System.Reflection;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Connectivity;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Razor;
using CCL.MES.Hybrid.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid;

/// <summary>
/// MAUI bootstrap. Wires the platform-specific implementations of
/// <see cref="ITokenStore"/> + <see cref="IConnectivityMonitor"/> behind
/// the platform-agnostic abstractions consumed by the RCL UI.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Bundled appsettings.json packaged as a MauiAsset — read it as a
        // stream from the platform-specific file system. Operators can
        // ship a stationspecific copy by replacing the asset before sign.
        var asm = typeof(MauiProgram).GetTypeInfo().Assembly;
        var configBuilder = new ConfigurationBuilder();
        // The JsonStreamConfigurationProvider reads the stream lazily on
        // Build(), so we MUST keep it alive past this scope — using/dispose
        // here closes it too early and we surface "Stream was not readable".
        // We materialise the JSON into memory + hand a fresh MemoryStream
        // each time the provider reloads so disposal is harmless.
        var settingsStream = asm.GetManifestResourceStream("CCL.MES.Hybrid.appsettings.json");
        if (settingsStream is not null)
        {
            using var ms = new MemoryStream();
            settingsStream.CopyTo(ms);
            settingsStream.Dispose();
            var bytes = ms.ToArray();
            configBuilder.AddJsonStream(new MemoryStream(bytes, writable: false));
#if DEBUG
            Console.WriteLine($"[boot] appsettings.json loaded — {bytes.Length} bytes.");
#endif
        }
#if DEBUG
        else
        {
            Console.WriteLine("[boot] WARN: appsettings.json manifest stream is null — defaults will apply.");
        }
#endif
        var configuration = configBuilder.Build();
        builder.Configuration.AddConfiguration(configuration);
#if DEBUG
        Console.WriteLine($"[boot] CclApi:BaseUrl resolved => {configuration["CclApi:BaseUrl"] ?? "(null)"}");
#endif

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ── Auth + API client foundation (platform-agnostic) ─────────
        builder.Services.AddSingleton<ITokenStore, MauiSecureTokenStore>();
        builder.Services.AddSingleton<IConnectivityMonitor, MauiConnectivityMonitor>();
        builder.Services.AddCclHybridClient(configuration);
        builder.Services.AddScoped<AuthenticationStateProvider, HybridAuthStateProvider>();
        builder.Services.AddAuthorizationCore();

        // P10.3 hardware overrides — replace the in-memory default
        // device-mode service with the Preferences-backed one so settings
        // persist across launches. Printer + scale impls stay on the
        // stubs from CCL.MES.Hybrid.Client until concrete workflows
        // light them up. The DI Replace pattern preserves any other
        // registration the client extension already made.
        builder.Services.Replace(ServiceDescriptor.Singleton<IDeviceModeService, MauiDeviceModeService>());

        // P10.3 W4 — feed the persisted device id into ApiClientOptions so
        // every device-scoped endpoint call (scan-log / heartbeat / info)
        // carries the station's stable guid. PostConfigure runs AFTER the
        // initial Configure(configuration.GetSection("CclApi")) so we can
        // mutate the bound options without losing BaseUrl/Timeout from
        // appsettings.json. We can't inject IDeviceModeService into the
        // closure directly via the IServiceCollection extension, so the
        // BuildServiceProvider-once-during-options-init trick is needed —
        // safe because IDeviceModeService is a singleton with no scoped
        // dependencies (just hits MAUI Preferences).
        builder.Services.AddOptions<ApiClientOptions>()
            .PostConfigure<IServiceProvider>((opts, sp) =>
            {
                var device = sp.GetRequiredService<IDeviceModeService>();
                if (string.IsNullOrWhiteSpace(opts.DeviceId))
                    opts.DeviceId = device.DeviceId;
            });

        // P10.3 W4 — background heartbeat. 60-second cadence, suspends
        // when no logged-in user.
        builder.Services.AddHostedService<DeviceHeartbeatHostedService>();

#if MACCATALYST || IOS
        // P10.3 W2 — Mac Catalyst camera scanner (AVFoundation). Drops
        // in over the StubBarcodeScannerService + StubDeviceSettings-
        // Launcher from the client lib.
        builder.Services.Replace(ServiceDescriptor.Singleton<IBarcodeScannerService,
            CCL.MES.Hybrid.Platforms.MacCatalyst.CatalystBarcodeScanner>());
        builder.Services.Replace(ServiceDescriptor.Singleton<IDeviceSettingsLauncher,
            CCL.MES.Hybrid.Platforms.MacCatalyst.MauiCatalystDeviceSettingsLauncher>());
#endif

        return builder.Build();
    }
}
