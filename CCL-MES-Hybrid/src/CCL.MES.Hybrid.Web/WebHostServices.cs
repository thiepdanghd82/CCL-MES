using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Web;

/// <summary>
/// DI của web host. Tái dùng nguyên <see cref="ServiceCollectionExtensions.AddCclHybridClient"/>
/// của app MAUI, rồi sửa đúng HAI chỗ mà trình duyệt nhiều người dùng khác app một người:
///
/// <para><b>1. Singleton ⇒ Scoped.</b> App desktop có đúng MỘT người dùng nên client
/// đăng ký gần hết dịch vụ là Singleton — kể cả <see cref="IAuthSession"/>. Trong
/// Blazor Server, singleton dùng CHUNG cho mọi trình duyệt: người thứ hai mở web sẽ
/// thấy phiên đăng nhập của người thứ nhất. Scoped = một bản cho mỗi circuit (mỗi tab).
/// Chỉ <see cref="SharedSingletons"/> — dữ liệu tĩnh chỉ đọc — được giữ dùng chung.</para>
///
/// <para><b>2. ICclApiClient dựng trong scope của circuit.</b> Handler của
/// IHttpClientFactory được tạo trong scope RIÊNG của factory, nên
/// <see cref="AuthorizationDelegatingHandler"/> sẽ cầm token store/session của một
/// scope khác, không phải của người đang dùng tab này.</para>
/// </summary>
public static class WebHostServices
{
    /// <summary>Singleton được phép dùng chung giữa mọi người dùng — KHÔNG có trạng thái
    /// riêng của ai. Thêm vào đây phải chứng minh được điều đó.</summary>
    public static readonly IReadOnlySet<Type> SharedSingletons = new HashSet<Type>
    {
        typeof(ITranslationCatalog),   // bảng dịch tĩnh, dựng một lần trong ctor
    };

    public static IServiceCollection AddCclWebHost(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCclHybridClient(configuration);
        ScopeAppServicesPerUser(services);

        // Phần 1: token chỉ sống trong circuit. Phần 2 thêm cookie để tải lại trang không mất phiên.
        services.RemoveAll<ITokenStore>();
        services.AddScoped<ITokenStore, InMemoryTokenStore>();

        services.RemoveAll<ICclApiClient>();
        services.AddScoped<ICclApiClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ApiClientOptions>>();
            var handler = new AuthorizationDelegatingHandler(
                sp.GetRequiredService<ITokenStore>(),
                sp.GetRequiredService<IAuthSession>(),
                () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(ServiceCollectionExtensions.RefreshHttpClientName),
                sp.GetService<ILogger<AuthorizationDelegatingHandler>>())
            {
                // Handler gốc lấy từ pool của factory (tái dùng kết nối); bọc ngoài là
                // handler token CỦA CIRCUIT NÀY.
                InnerHandler = sp.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(),
            };
            var http = new HttpClient(handler) { BaseAddress = new Uri(opts.Value.BaseUrl), Timeout = opts.Value.Timeout };
            return new CclApiClient(http, opts);
        });

        // App MAUI đăng ký bản đo mạng của máy. Trên web, mất kết nối trình duyệt ↔
        // server đã có màn "đang kết nối lại" của Blazor; server ↔ API cùng máy.
        services.AddScoped<Client.Connectivity.IConnectivityMonitor, Client.Connectivity.AlwaysOnlineConnectivityMonitor>();

        services.AddScoped<AuthenticationStateProvider, HybridAuthStateProvider>();
        services.AddAuthorizationCore();
        return services;
    }

    /// <summary>Mọi singleton do app đăng ký (namespace <c>CCL.MES.Hybrid.*</c>) ⇒ Scoped,
    /// trừ <see cref="SharedSingletons"/>. Làm theo QUY TẮC chứ không theo danh sách tay,
    /// để dịch vụ mới thêm vào client về sau không lặng lẽ thành dùng chung.</summary>
    internal static void ScopeAppServicesPerUser(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var d = services[i];
            if (d.Lifetime != ServiceLifetime.Singleton || d.IsKeyedService) continue;
            if (SharedSingletons.Contains(d.ServiceType)) continue;
            if (d.ServiceType.Namespace?.StartsWith("CCL.MES.Hybrid", StringComparison.Ordinal) != true) continue;
            if (d.ImplementationInstance is not null)
                throw new InvalidOperationException(
                    $"{d.ServiceType} is registered as a singleton INSTANCE — cannot be made per-user. Register a type or factory instead.");

            services[i] = d.ImplementationFactory is not null
                ? ServiceDescriptor.Scoped(d.ServiceType, d.ImplementationFactory)
                : ServiceDescriptor.Scoped(d.ServiceType, d.ImplementationType!);
        }
    }
}
