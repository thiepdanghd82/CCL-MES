using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Auth;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Envelopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Hãm thử-sai cho <c>/auth/login</c> (2026-09-25) — điều kiện trước khi mở API
/// ra LAN xưởng (skill <c>cmes-secrets-jwt</c>: "Brute-force HTTP LAN là kịch bản
/// thật"). Mỗi test dùng một tên riêng vì bộ đếm là singleton dùng chung cả class.
/// </summary>
public sealed class LoginLockoutTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "Pa55w.rd!";
    private readonly MesApiFactory _fx;

    public LoginLockoutTests(MesApiFactory fx) => _fx = fx;

    private Task<HttpResponseMessage> LoginAsync(HttpClient c, string user, string pwd)
        => c.PostAsJsonAsync("/api/v2/auth/login", new LoginRequest { Username = user, Password = pwd });

    private async Task FailAsync(HttpClient c, string user, int times)
    {
        for (var i = 0; i < times; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(c, user, "wrong-" + i)).StatusCode);
    }

    [Fact]
    public async Task After_max_failures_even_the_correct_password_is_refused_with_429()
    {
        await _fx.SeedUserAsync("lock-a", Pwd, UserRole.Operator);
        var c = _fx.CreateClient();
        await FailAsync(c, "lock-a", ReauthThrottle.MaxFailures);

        var resp = await LoginAsync(c, "lock-a", Pwd);

        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
        Assert.Equal("auth.locked", (await resp.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.True(resp.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After header missing");
    }

    [Fact]
    public async Task Below_threshold_the_correct_password_still_works_and_resets_the_counter()
    {
        await _fx.SeedUserAsync("lock-b", Pwd, UserRole.Operator);
        var c = _fx.CreateClient();
        await FailAsync(c, "lock-b", ReauthThrottle.MaxFailures - 1);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(c, "lock-b", Pwd)).StatusCode);

        // Bộ đếm đã xoá: thêm (Max-1) lần sai nữa vẫn chưa khoá.
        await FailAsync(c, "lock-b", ReauthThrottle.MaxFailures - 1);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(c, "lock-b", Pwd)).StatusCode);
    }

    [Fact]
    public async Task Unknown_username_locks_the_same_way_so_lockout_is_not_an_account_oracle()
    {
        var c = _fx.CreateClient();
        await FailAsync(c, "no-such-user-x", ReauthThrottle.MaxFailures);

        var resp = await LoginAsync(c, "no-such-user-x", "anything");
        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
        Assert.Equal("auth.locked", (await resp.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task Lock_is_per_username_and_case_insensitive_other_accounts_unaffected()
    {
        await _fx.SeedUserAsync("lock-c", Pwd, UserRole.Operator);
        await _fx.SeedUserAsync("lock-d", Pwd, UserRole.Operator);
        var c = _fx.CreateClient();
        await FailAsync(c, "LOCK-C", ReauthThrottle.MaxFailures);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(c, "lock-c", Pwd)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(c, "lock-d", Pwd)).StatusCode);
    }

    [Fact]
    public async Task Locked_attempt_emits_LOGIN_LOCKED_audit_without_the_password()
    {
        await _fx.SeedUserAsync("lock-e", Pwd, UserRole.Operator);
        var c = _fx.CreateClient();
        await FailAsync(c, "lock-e", ReauthThrottle.MaxFailures);
        await LoginAsync(c, "lock-e", Pwd);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "LOGIN_LOCKED" && a.Detail!.Contains("lock-e"))
            .SingleAsync();
        Assert.DoesNotContain(Pwd, row.Detail);
        Assert.Contains("retry_after_sec", row.Detail);
    }
}
