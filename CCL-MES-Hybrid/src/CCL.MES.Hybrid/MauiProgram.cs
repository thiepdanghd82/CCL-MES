using System.Reflection;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Connectivity;
using CCL.MES.Hybrid.Razor;
using CCL.MES.Hybrid.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        // DEEP-DEBUG boot trace — surfaces in Console.app under the
        // CCL MES process. Confirms config loaded + which BaseUrl wins.
        Console.WriteLine("[boot] MauiProgram.CreateMauiApp starting.");
        var allManifestNames = asm.GetManifestResourceNames();
        Console.WriteLine($"[boot] embedded resources: {string.Join(", ", allManifestNames)}");

        var settingsStream = asm.GetManifestResourceStream("CCL.MES.Hybrid.appsettings.json");
        if (settingsStream is not null)
        {
            using var ms = new MemoryStream();
            settingsStream.CopyTo(ms);
            settingsStream.Dispose();
            var bytes = ms.ToArray();
            Console.WriteLine($"[boot] appsettings.json loaded — {bytes.Length} bytes.");
            configBuilder.AddJsonStream(new MemoryStream(bytes, writable: false));
        }
        else
        {
            Console.WriteLine("[boot] WARN: appsettings.json manifest stream is null — defaults will apply.");
        }
        var configuration = configBuilder.Build();
        builder.Configuration.AddConfiguration(configuration);
        Console.WriteLine($"[boot] CclApi:BaseUrl resolved => {configuration["CclApi:BaseUrl"] ?? "(null)"}");

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

        var app = builder.Build();
        Console.WriteLine("[boot] MauiApp.Build completed without throwing.");
        return app;
    }
}
