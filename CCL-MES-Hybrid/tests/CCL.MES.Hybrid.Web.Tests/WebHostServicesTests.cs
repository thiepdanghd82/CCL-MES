using System.Reflection;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace CCL.MES.Hybrid.Web.Tests;

/// <summary>
/// DI của web host (2026-09-25). Hai bug class phải khoá:
/// (1) MỌI dịch vụ component inject phải resolve được — thiếu một cái là cả trang
///     trắng ("Cannot provide a value for property 'Monitor'…", đo lần đầu chạy);
/// (2) mỗi trình duyệt (scope) một phiên — client đăng ký IAuthSession là Singleton,
///     bê nguyên sang web thì người thứ hai thấy phiên của người thứ nhất.
/// </summary>
public sealed class WebHostServicesTests
{
    private static ServiceProvider Build()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CclApi:BaseUrl"] = "http://127.0.0.1:5100" })
            .Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        // Framework Blazor cung cấp các dịch vụ này trong circuit thật.
        s.AddScoped<IJSRuntime, NoopJsRuntime>();
        s.AddScoped<NavigationManager, TestNavigationManager>();
        s.AddCclWebHost(cfg);
        return s.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static IEnumerable<object[]> InjectedServiceTypes() =>
        typeof(CCL.MES.Hybrid.Razor.App).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
            .Select(p => p.PropertyType)
            .Where(t => !t.ContainsGenericParameters)
            .Distinct()
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(InjectedServiceTypes))]
    public async Task Every_service_injected_by_a_component_resolves(Type serviceType)
    {
        // await using: Blazor giải phóng circuit bằng DisposeAsync; ShopfloorLiveService
        // chỉ cài IAsyncDisposable nên Dispose() đồng bộ sẽ ném.
        await using var sp = Build();
        await using var scope = sp.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetService(serviceType));
    }

    [Fact]
    public void Scan_found_the_components_it_is_meant_to_guard()
        => Assert.True(InjectedServiceTypes().Count() >= 20, "reflection scan found too few [Inject] types — scan is broken");

    [Theory]
    [InlineData(typeof(IAuthSession))]
    [InlineData(typeof(ITokenStore))]
    [InlineData(typeof(ICclApiClient))]
    [InlineData(typeof(ILanguageService))]
    [InlineData(typeof(Client.Grid.IGridPreferenceStore))]
    [InlineData(typeof(Client.Windows.IWindowManager))]
    public async Task Per_user_state_is_NOT_shared_between_two_browsers(Type serviceType)
    {
        await using var sp = Build();
        await using var a = sp.CreateAsyncScope();
        await using var b = sp.CreateAsyncScope();
        Assert.NotSame(a.ServiceProvider.GetRequiredService(serviceType), b.ServiceProvider.GetRequiredService(serviceType));
    }

    [Fact]
    public void No_app_service_is_left_singleton_except_the_allowlist()
    {
        var cfg = new ConfigurationBuilder().Build();
        var s = new ServiceCollection();
        s.AddCclWebHost(cfg);
        var leaked = s.Where(d => d.Lifetime == ServiceLifetime.Singleton
                               && d.ServiceType.Namespace?.StartsWith("CCL.MES.Hybrid", StringComparison.Ordinal) == true
                               && !WebHostServices.SharedSingletons.Contains(d.ServiceType))
                      .Select(d => d.ServiceType.FullName)
                      .ToList();
        Assert.Empty(leaked);
    }

    [Fact]
    public async Task Api_client_uses_the_token_store_of_its_own_browser()
    {
        await using var sp = Build();
        await using var scope = sp.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITokenStore>();
        var session = scope.ServiceProvider.GetRequiredService<IAuthSession>();
        // Cùng scope ⇒ cùng instance: handler gắn token đọc đúng phiên của tab này.
        Assert.Same(store, scope.ServiceProvider.GetRequiredService<ITokenStore>());
        Assert.Same(session, scope.ServiceProvider.GetRequiredService<IAuthSession>());
    }

    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/");
    }
}
