using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.6a — Settings/My Profile + My Password integration tests.
/// Three endpoints, six guarded behaviours each test class covers:
///   1. GET /me returns the signed-in profile.
///   2. GET /me requires auth (401 anonymous).
///   3. PATCH /me updates DisplayName + audit row written.
///   4. PATCH /me 422 on overlong name.
///   5. POST /password 200 on correct current + ≥4 char new.
///   6. POST /password 422 on wrong current / too-short new.
///   7. POST /password emits USER_SELF_PWD_CHANGE audit.
/// </summary>
public sealed class SettingsControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SettingsControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<(HttpClient client, long userId)> EngineerClientAsync(string username = "eng-settings")
    {
        var u = await _fx.SeedUserAsync(username, "P@ss!1", UserRole.Engineer,
            displayName: "Eng Initial", department: "NPI");
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!1");
        return (client, u.Id);
    }

    // ── GET /me ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_me_returns_profile_for_authenticated_user()
    {
        var (client, _) = await EngineerClientAsync("eng-get-me");
        var resp = await client.GetAsync("/api/v2/settings/me");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SettingsProfileDto>();
        Assert.NotNull(body);
        Assert.Equal("eng-get-me", body!.Username);
        Assert.Equal("Engineer", body.Role);
        Assert.Equal("Eng Initial", body.DisplayName);
        Assert.Equal("NPI", body.Department);
        Assert.False(body.MustChangePassword);
    }

    [Fact]
    public async Task Get_me_denies_anonymous_caller()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/settings/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── PATCH /me ───────────────────────────────────────────────────

    [Fact]
    public async Task Patch_me_updates_display_name_and_emits_audit()
    {
        var (client, userId) = await EngineerClientAsync("eng-patch-me");
        var resp = await client.PatchAsJsonAsync("/api/v2/settings/me",
            new UpdateProfileRequest { DisplayName = "Eng Updated" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SettingsProfileDto>();
        Assert.Equal("Eng Updated", body!.DisplayName);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var refreshed = await db.Users.FirstAsync(u => u.Id == userId);
        Assert.Equal("Eng Updated", refreshed.DisplayName);

        var audit = await db.AuditLogs
            .Where(a => a.Action == "USER_SELF_DISPLAY_CHANGE" && a.ActorUsername == "eng-patch-me")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains("Eng Updated", audit!.Detail ?? "");
        Assert.Contains("Eng Initial", audit.Detail ?? "");
    }

    [Fact]
    public async Task Patch_me_clears_display_name_when_blank()
    {
        var (client, userId) = await EngineerClientAsync("eng-patch-clear");
        var resp = await client.PatchAsJsonAsync("/api/v2/settings/me",
            new UpdateProfileRequest { DisplayName = "   " });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var refreshed = await db.Users.FirstAsync(u => u.Id == userId);
        Assert.Null(refreshed.DisplayName);
    }

    [Fact]
    public async Task Patch_me_rejects_overlong_display_name_with_422()
    {
        var (client, _) = await EngineerClientAsync("eng-patch-long");
        var resp = await client.PatchAsJsonAsync("/api/v2/settings/me",
            new UpdateProfileRequest { DisplayName = new string('x', 101) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("profile.display_name_too_long", err!.Code);
    }

    // ── POST /password ──────────────────────────────────────────────

    [Fact]
    public async Task Change_password_with_correct_current_succeeds()
    {
        var (client, _) = await EngineerClientAsync("eng-pwd-ok");
        var resp = await client.PostAsJsonAsync("/api/v2/settings/password",
            new ChangePasswordRequest { CurrentPassword = "P@ss!1", NewPassword = "BRAND-NEW" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ChangePasswordResponse>();
        Assert.True(body!.Success);

        // Re-login with new password to prove it landed.
        var client2 = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client2, "eng-pwd-ok", "BRAND-NEW");
        var ping = await client2.GetAsync("/api/v2/settings/me");
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_wrong_current_with_422_wrong_current()
    {
        var (client, _) = await EngineerClientAsync("eng-pwd-wrong");
        var resp = await client.PostAsJsonAsync("/api/v2/settings/password",
            new ChangePasswordRequest { CurrentPassword = "WRONG", NewPassword = "ALSO-VALID" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("auth.wrong_current", err!.Code);
    }

    [Fact]
    public async Task Change_password_rejects_short_new_with_422_new_too_short()
    {
        var (client, _) = await EngineerClientAsync("eng-pwd-short");
        var resp = await client.PostAsJsonAsync("/api/v2/settings/password",
            new ChangePasswordRequest { CurrentPassword = "P@ss!1", NewPassword = "abc" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("auth.new_too_short", err!.Code);
    }

    [Fact]
    public async Task Change_password_rejects_blank_fields_with_422_missing_fields()
    {
        var (client, _) = await EngineerClientAsync("eng-pwd-blank");
        var resp = await client.PostAsJsonAsync("/api/v2/settings/password",
            new ChangePasswordRequest { CurrentPassword = "", NewPassword = "WHATEVER" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("auth.missing_fields", err!.Code);
    }

    [Fact]
    public async Task Change_password_emits_USER_SELF_PWD_CHANGE_audit()
    {
        var (client, _) = await EngineerClientAsync("eng-pwd-audit");
        var resp = await client.PostAsJsonAsync("/api/v2/settings/password",
            new ChangePasswordRequest { CurrentPassword = "P@ss!1", NewPassword = "AUDIT-CHECK" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var audit = await db.AuditLogs
            .Where(a => a.Action == "USER_SELF_PWD_CHANGE" && a.ActorUsername == "eng-pwd-audit")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Change_password_clears_must_change_password_flag()
    {
        var u = await _fx.SeedUserAsync("eng-must-change", "TEMP-1", UserRole.Engineer);
        // Flip the must-change-password flag the way the admin reset
        // flow would (Phase 6 Bước 4 pattern).
        using (var setup = _fx.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<MesDbContext>();
            var user = await db.Users.FirstAsync(x => x.Id == u.Id);
            user.MustChangePassword = true;
            await db.SaveChangesAsync();
        }

        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "eng-must-change", "TEMP-1");
        var resp = await client.PostAsJsonAsync("/api/v2/settings/password",
            new ChangePasswordRequest { CurrentPassword = "TEMP-1", NewPassword = "CHOSEN-1" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var verify = _fx.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<MesDbContext>();
        var refreshed = await db2.Users.FirstAsync(x => x.Id == u.Id);
        Assert.False(refreshed.MustChangePassword);
    }
}
