using CCL.MES.Hybrid.Client.Auth;
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

        return services;
    }

    public const string RefreshHttpClientName = "CclApiRefresh";
}
