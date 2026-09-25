using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CCL.MES.Hybrid.Web.Tests;

/// <summary>
/// Trang host + tài nguyên tĩnh của web host (2026-09-25). Hai lần "trang trắng"
/// đo được khi dựng: (1) chỉ UseStaticFiles ⇒ _framework/blazor.server.js 404;
/// (2) project không có .razor riêng ⇒ SDK không kéo tài nguyên Blazor, vẫn 404.
/// Cả hai đều trả 200 cho trang host nên nhìn log server không thấy gì.
/// </summary>
public sealed class WebHostHttpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http;

    public WebHostHttpTests(WebApplicationFactory<Program> factory) => _http = factory.CreateClient();

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/workorders")]      // deep link: fallback trả trang host, không 404
    [InlineData("/qms/iqc")]
    public async Task Every_route_serves_the_host_page_that_boots_blazor_server(string path)
    {
        var resp = await _http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("_framework/blazor.server.js", html);
        Assert.Contains("_content/CCL.MES.Hybrid.Razor/css/app.css", html);
    }

    [Theory]
    [InlineData("/_framework/blazor.server.js")]
    [InlineData("/_content/CCL.MES.Hybrid.Razor/css/app.css")]
    [InlineData("/_content/CCL.MES.Hybrid.Razor/css/ix.css")]
    [InlineData("/_content/CCL.MES.Hybrid.Razor/js/density.js")]
    [InlineData("/_content/CCL.MES.Hybrid.Razor/js/print.js")]
    public async Task Assets_the_host_page_needs_are_served(string path)
        => Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(path)).StatusCode);

    [Fact]
    public async Task Every_script_and_stylesheet_referenced_by_the_host_page_resolves()
    {
        var html = await (await _http.GetAsync("/")).Content.ReadAsStringAsync();
        var refs = System.Text.RegularExpressions.Regex
            .Matches(html, "(?:src|href)=\"(_[^\"]+)\"")
            .Select(m => "/" + m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.True(refs.Count >= 8, $"expected the host page to reference its assets, found {refs.Count}");
        foreach (var r in refs)
            Assert.True((await _http.GetAsync(r)).StatusCode == HttpStatusCode.OK, $"{r} is not served");
    }
}
