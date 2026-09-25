using CCL.MES.Hybrid.Client.Hardware;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Web.Tests;

/// <summary>
/// Cấu hình web host phải khớp appsettings.json đóng gói của app MAUI (2026-09-25).
/// Bản đầu của web host thiếu <c>Hardware:ScanEnabled</c> ⇒ mặc định false ⇒ trang
/// "Lệnh SX — Quét" báo "Chức năng quét đang tắt" trên web trong khi app chạy bình
/// thường. So THEO KHOÁ: app thêm cờ mới mà web thiếu ⇒ test đỏ ngay.
/// </summary>
public sealed class WebConfigParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public WebConfigParityTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string SrcDir(string project)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", project)))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src", project);
    }

    private static IReadOnlyDictionary<string, string?> Flatten(string file) =>
        new ConfigurationBuilder().AddJsonFile(file).Build().AsEnumerable()
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    [Fact]
    public void Every_setting_bundled_in_the_Mac_app_exists_in_the_web_host_with_the_same_value()
    {
        var app = Flatten(Path.Combine(SrcDir("CCL.MES.Hybrid"), "appsettings.json"));
        var web = Flatten(Path.Combine(SrcDir("CCL.MES.Hybrid.Web"), "appsettings.json"));

        Assert.NotEmpty(app);
        foreach (var (key, value) in app)
        {
            Assert.True(web.TryGetValue(key, out var w), $"web appsettings.json is missing '{key}' (app has '{value}')");
            Assert.Equal(value, w);
        }
    }

    [Fact]
    public void Running_web_host_has_scanning_enabled_like_the_app()
        => Assert.True(_factory.Services.GetRequiredService<IOptions<HardwareOptions>>().Value.ScanEnabled);
}
