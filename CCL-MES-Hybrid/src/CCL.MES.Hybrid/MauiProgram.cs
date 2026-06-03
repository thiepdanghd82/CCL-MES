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
        using (var settingsStream = asm.GetManifestResourceStream("CCL.MES.Hybrid.appsettings.json"))
        {
            if (settingsStream is not null)
                configBuilder.AddJsonStream(settingsStream);
        }
        // Allow env overrides for dev iteration — eg. CCL_CCLAPI__BASEURL.
        configBuilder.AddEnvironmentVariables(prefix: "CCL_");
        var configuration = configBuilder.Build();
        builder.Configuration.AddConfiguration(configuration);

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

        return builder.Build();
    }
}
