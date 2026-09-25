using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Web.Session;
using CCL.MES.Shared.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Web.Tests;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class FakeJwt
{
    /// <summary>JWT không chữ ký — JwtClaims phía client chỉ giải mã payload.</summary>
    public static string For(string userId, string username = "user", string jti = "")
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["nameid"] = userId, ["unique_name"] = username, ["role"] = "Admin", ["jti"] = jti.Length > 0 ? jti : Guid.NewGuid().ToString("N"),
        });
        return $"{B64("{\"alg\":\"none\"}")}.{B64(payload)}.";
    }
}

internal sealed class RecordingCookie : IWebSessionCookie
{
    public List<string> Claimed { get; } = new();
    public int Cleared { get; private set; }
    public bool ClaimResult { get; set; } = true;
    public Task<bool> ClaimAsync(string ticket, CancellationToken ct = default) { Claimed.Add(ticket); return Task.FromResult(ClaimResult); }
    public Task ClearAsync(CancellationToken ct = default) { Cleared++; return Task.CompletedTask; }
}

/// <summary>Kho phiên phía server — hạn 12 giờ, vé một lần.</summary>
public sealed class WebSessionStoreTests
{
    private readonly ManualClock _clock = new();
    private WebSessionStore NewStore() => new(Options.Create(new WebSessionOptions()), _clock);

    [Fact]
    public void Session_lives_exactly_one_shift_then_is_gone()
    {
        var store = NewStore();
        var key = store.Create("a", "r", "1");
        _clock.Now += TimeSpan.FromHours(12) - TimeSpan.FromSeconds(1);
        Assert.True(store.TryGet(key, out _));
        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.False(store.TryGet(key, out _));
        Assert.False(store.Update(key, "a2", "r2"));   // hết hạn ⇒ không hồi sinh bằng làm mới
    }

    [Fact]
    public void Keys_are_long_and_unique()
    {
        var store = NewStore();
        var keys = Enumerable.Range(0, 200).Select(_ => store.Create("a", "r", "1")).ToList();
        Assert.Equal(200, keys.Distinct().Count());
        Assert.All(keys, k => Assert.True(k.Length >= 43, "key must carry 256 bits"));
    }

    [Fact]
    public void Ticket_is_single_use()
    {
        var store = NewStore();
        var key = store.Create("a", "r", "1");
        var ticket = store.IssueClaimTicket(key);
        Assert.True(store.TryRedeemTicket(ticket, out var redeemed));
        Assert.Equal(key, redeemed);
        Assert.False(store.TryRedeemTicket(ticket, out _));
    }

    [Fact]
    public void Ticket_expires_after_30_seconds()
    {
        var store = NewStore();
        var ticket = store.IssueClaimTicket(store.Create("a", "r", "1"));
        _clock.Now += TimeSpan.FromSeconds(30);
        Assert.False(store.TryRedeemTicket(ticket, out _));
    }

    [Fact]
    public void Unknown_ticket_is_rejected()
        => Assert.False(NewStore().TryRedeemTicket("not-a-ticket", out _));
}

/// <summary>Token store theo tab: đăng nhập tạo phiên + cookie; làm mới ghi đè; hai tab
/// cùng phiên thấy CÙNG bản token; đăng xuất xoá phiên phía server trước.</summary>
public sealed class WebTokenStoreTests
{
    private readonly WebSessionStore _store = new(Options.Create(new WebSessionOptions()), new ManualClock());

    private (WebTokenStore Tokens, WebCircuitSession Circuit, RecordingCookie Cookie) NewTab(string? key = null)
    {
        var circuit = new WebCircuitSession { Key = key };
        var cookie = new RecordingCookie();
        return (new WebTokenStore(_store, circuit, cookie, NullLogger<WebTokenStore>.Instance), circuit, cookie);
    }

    [Fact]
    public async Task Login_creates_a_server_session_and_claims_a_cookie_with_a_valid_ticket()
    {
        var tab = NewTab();
        await tab.Tokens.SaveAsync(FakeJwt.For("1"), "r1");

        Assert.NotNull(tab.Circuit.Key);
        var ticket = Assert.Single(tab.Cookie.Claimed);
        Assert.True(_store.TryRedeemTicket(ticket, out var key));
        Assert.Equal(tab.Circuit.Key, key);
        Assert.Equal("r1", await tab.Tokens.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Token_rotation_for_the_same_user_updates_in_place_without_a_new_cookie()
    {
        var tab = NewTab();
        await tab.Tokens.SaveAsync(FakeJwt.For("1"), "r1");
        var key = tab.Circuit.Key;

        await tab.Tokens.SaveAsync(FakeJwt.For("1"), "r2");

        Assert.Equal(key, tab.Circuit.Key);
        Assert.Single(tab.Cookie.Claimed);
        Assert.Equal("r2", await tab.Tokens.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Two_tabs_of_one_session_see_each_others_rotation()
    {
        var a = NewTab();
        await a.Tokens.SaveAsync(FakeJwt.For("1"), "r1");
        var b = NewTab(a.Circuit.Key);   // tab thứ hai mở bằng cùng cookie

        await a.Tokens.SaveAsync(FakeJwt.For("1"), "r2");

        Assert.Equal("r2", await b.Tokens.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Another_user_signing_in_on_the_same_browser_gets_a_NEW_session()
    {
        var tab = NewTab();
        await tab.Tokens.SaveAsync(FakeJwt.For("1", "alice"), "r1");
        var aliceKey = tab.Circuit.Key!;

        await tab.Tokens.SaveAsync(FakeJwt.For("2", "bob"), "r9");

        Assert.NotEqual(aliceKey, tab.Circuit.Key);
        Assert.False(_store.TryGet(aliceKey, out _));   // phiên của alice bị bỏ
        Assert.Equal(2, tab.Cookie.Claimed.Count);
    }

    [Fact]
    public async Task Sign_out_removes_the_server_session_first_so_a_leftover_cookie_is_useless()
    {
        var a = NewTab();
        await a.Tokens.SaveAsync(FakeJwt.For("1"), "r1");
        var key = a.Circuit.Key!;
        var b = NewTab(key);

        await a.Tokens.ClearAsync();

        Assert.Null(a.Circuit.Key);
        Assert.Equal(1, a.Cookie.Cleared);
        Assert.False(_store.TryGet(key, out _));
        Assert.Null(await b.Tokens.GetAccessTokenAsync());   // tab còn lại cũng mất phiên
        Assert.Null(b.Circuit.Key);
    }

    [Fact]
    public async Task Tab_without_a_session_has_no_tokens()
    {
        var tab = NewTab("key-that-does-not-exist");
        Assert.Null(await tab.Tokens.GetAccessTokenAsync());
        Assert.Null(tab.Circuit.Key);
    }

    [Fact]
    public void Subject_is_the_user_id_claim_the_API_issues()
        => Assert.Equal("42", WebTokenStore.SubjectOf(FakeJwt.For("42", "someone")));
}

/// <summary>Hai tab cùng làm mới bằng CÙNG refresh token ⇒ đúng MỘT request tới API.</summary>
public sealed class RefreshCoalescingTests
{
    private sealed class CountingApi : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Gate.Task;
            return new HttpResponseMessage(Status) { Content = new StringContent($"{{\"accessToken\":\"a{Calls}\",\"refreshToken\":\"r{Calls}\"}}", Encoding.UTF8, "application/json") };
        }
    }

    private static (HttpClient Http, CountingApi Api) Build(RefreshCoalescer coalescer)
    {
        var api = new CountingApi();
        var http = new HttpClient(new RefreshCoalescingHandler(coalescer) { InnerHandler = api }) { BaseAddress = new Uri("http://api.test") };
        return (http, api);
    }

    private static RefreshCoalescer NewCoalescer(ManualClock? clock = null)
        => new(Options.Create(new WebSessionOptions()), clock ?? new ManualClock());

    private static Task<HttpResponseMessage> Refresh(HttpClient http, string token)
        => http.PostAsJsonAsync("/api/v2/auth/refresh", new RefreshTokenRequest { RefreshToken = token });

    [Fact]
    public async Task Concurrent_refreshes_with_the_same_token_hit_the_API_once_and_share_the_result()
    {
        var coalescer = NewCoalescer();
        var (tabA, api) = Build(coalescer);
        var tabB = new HttpClient(new RefreshCoalescingHandler(coalescer) { InnerHandler = api }) { BaseAddress = new Uri("http://api.test") };

        var ra = Refresh(tabA, "old");
        var rb = Refresh(tabB, "old");
        api.Gate.SetResult();
        var (a, b) = (await ra, await rb);

        Assert.Equal(1, api.Calls);
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.Equal("application/json", b.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_late_tab_within_the_window_reuses_the_result_instead_of_replaying_the_old_token()
    {
        var clock = new ManualClock();
        var (http, api) = Build(NewCoalescer(clock));
        api.Gate.SetResult();
        await Refresh(http, "old");
        clock.Now += TimeSpan.FromSeconds(90);
        await Refresh(http, "old");
        Assert.Equal(1, api.Calls);

        clock.Now += TimeSpan.FromMinutes(2);   // hết cửa sổ ⇒ gọi thật
        await Refresh(http, "old");
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task Different_tokens_are_not_coalesced()
    {
        var (http, api) = Build(NewCoalescer());
        api.Gate.SetResult();
        await Refresh(http, "t1");
        await Refresh(http, "t2");
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var (http, api) = Build(NewCoalescer());
        api.Status = HttpStatusCode.Unauthorized;
        api.Gate.SetResult();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(http, "old")).StatusCode);
        await Refresh(http, "old");
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task Non_refresh_requests_pass_through_untouched()
    {
        var (http, api) = Build(NewCoalescer());
        api.Gate.SetResult();
        await http.GetAsync("/api/v2/auth/me");
        await http.GetAsync("/api/v2/auth/me");
        Assert.Equal(2, api.Calls);
    }
}

/// <summary>Tải lại trang ⇒ khôi phục phiên ĐỦ UserInfo, hoặc đăng xuất nếu API từ chối.</summary>
public sealed class WebSessionBootstrapperTests
{
    private sealed class MeApi : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Throw { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw) throw new HttpRequestException("api down");
            var body = Status == HttpStatusCode.OK
                ? JsonSerializer.Serialize(new UserInfo { Id = 1, Username = "admin", DisplayName = "Đặng Thế Thiệp", Role = "Admin" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : "{\"code\":\"auth.invalid\",\"messageEn\":\"x\"}";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (WebSessionBootstrapper Boot, IAuthSession Session, InMemoryTokenStore Tokens, MeApi Api) Build()
    {
        var tokens = new InMemoryTokenStore();
        var session = new AuthSession(tokens);
        var api = new MeApi();
        var client = new CclApiClient(new HttpClient(api) { BaseAddress = new Uri("http://api.test") },
            Options.Create(new ApiClientOptions { BaseUrl = "http://api.test" }));
        return (new WebSessionBootstrapper(tokens, session, client, NullLogger<WebSessionBootstrapper>.Instance), session, tokens, api);
    }

    [Fact]
    public async Task Restores_identity_AND_user_info_from_the_API()
    {
        var (boot, session, tokens, _) = Build();
        await tokens.SaveAsync(FakeJwt.For("1", "admin"), "r1");

        await boot.RestoreAsync();

        Assert.True(session.CurrentUser.Identity?.IsAuthenticated);
        Assert.Equal("admin", session.CurrentUserInfo?.Username);
        Assert.Equal("Đặng Thế Thiệp", session.CurrentUserInfo?.DisplayName);
    }

    [Fact]
    public async Task Api_rejecting_the_session_signs_out()
    {
        var (boot, session, tokens, api) = Build();
        await tokens.SaveAsync(FakeJwt.For("1"), "r1");
        api.Status = HttpStatusCode.Unauthorized;

        await boot.RestoreAsync();

        Assert.False(session.CurrentUser.Identity?.IsAuthenticated);
        Assert.Null(await tokens.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Api_unreachable_keeps_the_session_from_token_claims()
    {
        var (boot, session, tokens, api) = Build();
        await tokens.SaveAsync(FakeJwt.For("1"), "r1");
        api.Throw = true;

        await boot.RestoreAsync();

        Assert.True(session.CurrentUser.Identity?.IsAuthenticated);
        Assert.NotNull(await tokens.GetAccessTokenAsync());
    }

    [Fact]
    public async Task No_stored_session_stays_anonymous_without_calling_the_API()
    {
        var (boot, session, _, api) = Build();
        api.Throw = true;   // gọi API sẽ ném — chứng minh không gọi
        await boot.RestoreAsync();
        Assert.False(session.CurrentUser.Identity?.IsAuthenticated);
    }
}
