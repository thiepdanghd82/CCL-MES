using System.Net;
using System.Net.Http.Json;
using CCL.MES.Hybrid.Web.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Hybrid.Web.Tests;

/// <summary>
/// Endpoint đổi vé ⇒ cookie, xoá phiên, và trang host đọc cookie — qua HTTP thật.
/// Không có mật khẩu nào ở đây: phiên được tạo thẳng trong kho phía server.
/// </summary>
public sealed class WebSessionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;
    private readonly WebSessionStore _store;

    public WebSessionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        _store = factory.Services.GetRequiredService<WebSessionStore>();
    }

    private string NewSession() => _store.Create(FakeJwt.For("1"), "r1", "1");

    private Task<HttpResponseMessage> Claim(string ticket, bool withHeader = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/_session/claim") { Content = JsonContent.Create(new { ticket }) };
        if (withHeader) req.Headers.Add(WebSessionEndpoints.HeaderName, "1");
        return _http.SendAsync(req);
    }

    private static string SetCookie(HttpResponseMessage r)
        => r.Headers.TryGetValues("Set-Cookie", out var v) ? string.Join("\n", v) : "";

    private static string CookieValue(HttpResponseMessage r)
    {
        var sc = SetCookie(r);
        var start = sc.IndexOf(WebSessionCookie.Name + "=", StringComparison.Ordinal) + WebSessionCookie.Name.Length + 1;
        return sc[start..sc.IndexOf(';', start)];
    }

    [Fact]
    public async Task Valid_ticket_sets_an_HttpOnly_Strict_cookie_that_holds_no_token()
    {
        var resp = await Claim(_store.IssueClaimTicket(NewSession()));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var sc = SetCookie(resp).ToLowerInvariant();
        Assert.Contains(WebSessionCookie.Name + "=", sc);
        Assert.Contains("httponly", sc);
        Assert.Contains("samesite=strict", sc);
        Assert.Contains("path=/", sc);
        Assert.Contains("expires=", sc);
        Assert.DoesNotContain("secure", sc);   // HTTP LAN: Secure bật cùng HTTPS
        Assert.DoesNotContain("r1", CookieValue(resp));   // không mang refresh token
        Assert.Contains("no-store", resp.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Replayed_ticket_is_rejected()
    {
        var ticket = _store.IssueClaimTicket(NewSession());
        Assert.Equal(HttpStatusCode.NoContent, (await Claim(ticket)).StatusCode);
        var replay = await Claim(ticket);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("", SetCookie(replay));
    }

    [Fact]
    public async Task Missing_anti_csrf_header_is_rejected_WITHOUT_burning_the_ticket()
    {
        var ticket = _store.IssueClaimTicket(NewSession());
        Assert.Equal(HttpStatusCode.BadRequest, (await Claim(ticket, withHeader: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Claim(ticket)).StatusCode);
    }

    [Theory]
    [InlineData("forged-ticket")]
    [InlineData("")]
    public async Task Bad_ticket_is_rejected(string ticket)
        => Assert.Equal(HttpStatusCode.BadRequest, (await Claim(ticket)).StatusCode);

    [Fact]
    public async Task Ticket_for_a_session_that_ended_is_rejected()
    {
        var key = NewSession();
        var ticket = _store.IssueClaimTicket(key);
        _store.Remove(key);
        Assert.Equal(HttpStatusCode.BadRequest, (await Claim(ticket)).StatusCode);
    }

    [Fact]
    public async Task Clear_removes_the_server_session_and_expires_the_cookie()
    {
        var key = NewSession();
        var cookie = CookieValue(await Claim(_store.IssueClaimTicket(key)));

        var req = new HttpRequestMessage(HttpMethod.Post, "/_session/clear");
        req.Headers.Add(WebSessionEndpoints.HeaderName, "1");
        req.Headers.Add("Cookie", $"{WebSessionCookie.Name}={cookie}");
        var resp = await _http.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.False(_store.TryGet(key, out _));
        Assert.Contains("expires=thu, 01 jan 1970", SetCookie(resp).ToLowerInvariant());
    }

    [Fact]
    public async Task Clear_without_header_is_rejected_and_keeps_the_session()
    {
        var key = NewSession();
        var cookie = CookieValue(await Claim(_store.IssueClaimTicket(key)));
        var req = new HttpRequestMessage(HttpMethod.Post, "/_session/clear");
        req.Headers.Add("Cookie", $"{WebSessionCookie.Name}={cookie}");

        Assert.Equal(HttpStatusCode.BadRequest, (await _http.SendAsync(req)).StatusCode);
        Assert.True(_store.TryGet(key, out _));
    }

    [Fact]
    public async Task Host_page_is_never_cached()
        => Assert.Contains("no-store", (await _http.GetAsync("/")).Headers.CacheControl?.ToString());

    [Fact]
    public async Task Host_page_deletes_a_forged_cookie()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add("Cookie", $"{WebSessionCookie.Name}=forged-value");
        var resp = await _http.SendAsync(req);
        Assert.Contains("expires=thu, 01 jan 1970", SetCookie(resp).ToLowerInvariant());
    }

    [Fact]
    public async Task Host_page_deletes_the_cookie_of_a_session_that_ended_and_keeps_a_live_one()
    {
        var key = NewSession();
        var cookie = CookieValue(await Claim(_store.IssueClaimTicket(key)));

        var live = new HttpRequestMessage(HttpMethod.Get, "/");
        live.Headers.Add("Cookie", $"{WebSessionCookie.Name}={cookie}");
        Assert.Equal("", SetCookie(await _http.SendAsync(live)));

        _store.Remove(key);
        var ended = new HttpRequestMessage(HttpMethod.Get, "/");
        ended.Headers.Add("Cookie", $"{WebSessionCookie.Name}={cookie}");
        Assert.Contains("expires=thu, 01 jan 1970", SetCookie(await _http.SendAsync(ended)).ToLowerInvariant());
    }

    [Fact]
    public async Task Host_page_boots_the_web_root_that_restores_the_session()
    {
        var html = await (await _http.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("js/web-session.js", html);
        Assert.Contains("_framework/blazor.server.js", html);
        Assert.True(html.IndexOf("js/web-session.js", StringComparison.Ordinal) < html.IndexOf("_framework/blazor.server.js", StringComparison.Ordinal),
            "web-session.js must load before Blazor starts calling it");
    }
}
