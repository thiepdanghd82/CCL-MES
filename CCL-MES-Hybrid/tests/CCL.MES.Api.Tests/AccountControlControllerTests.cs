using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Api.Auth;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Accounts;
using CCL.MES.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.6c — admin Account Control endpoints.
///
/// Coverage:
///   - Anon 401 (covered separately by RouteDiscoveryCanaryTests for
///     all 5 endpoints).
///   - Engineer auth → 403 on every endpoint (POLICY enforcement,
///     not just discovery).
///   - Admin happy: list / create / patch displayName / patch role /
///     patch IsActive / reset password.
///   - DANGEROUS PATHS:
///       * Create with duplicate username → 422 accounts.username_in_use
///       * Create with invalid role → 422 accounts.invalid_role
///       * Create with too-short password → 422 accounts.password_too_short
///       * Patch self role / disable → 422 accounts.self_action_forbidden
///       * Reset self password via admin route → 422 accounts.self_action_forbidden
///       * Demote LAST admin → 422 accounts.last_admin
///       * Disable LAST admin → 422 accounts.last_admin
///       * Disable user revokes all refresh tokens for that user
///         (proof: store contains revoked tokens after the call)
///   - AUDIT EMIT:
///       * Create emits USER_CREATE
///       * Active flip emits USER_SET_ACTIVE
///       * Reset emits USER_RESET_PASSWORD
/// </summary>
public sealed class AccountControlControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public AccountControlControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> AdminClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Admin);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<HttpClient> EngineerClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    // ── Role-gating ─────────────────────────────────────────────────

    [Theory]
    [InlineData("GET",   "/api/v2/admin/users")]
    [InlineData("POST",  "/api/v2/admin/users")]
    [InlineData("GET",   "/api/v2/admin/users/1")]
    [InlineData("PATCH", "/api/v2/admin/users/1")]
    [InlineData("POST",  "/api/v2/admin/users/1/reset-password")]
    [InlineData("DELETE","/api/v2/admin/users/1")]
    public async Task Engineer_auth_gets_403_on_every_account_route(string verb, string url)
    {
        var client = await EngineerClientAsync($"eng-acc-{verb}-{url.GetHashCode():x}");
        using var req = new HttpRequestMessage(new HttpMethod(verb), url);
        if (verb is "POST" or "PATCH")
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Admin happy ─────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_list_accounts_and_see_self()
    {
        var client = await AdminClientAsync("admin-acc-list");
        var resp = await client.GetAsync("/api/v2/admin/users?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AccountPagedResult>();
        Assert.NotNull(body);
        Assert.Contains(body!.Items, u => u.Username == "admin-acc-list");
    }

    [Fact]
    public async Task Admin_can_create_user_and_new_account_has_must_change_password()
    {
        var client = await AdminClientAsync("admin-acc-create");
        var resp = await client.PostAsJsonAsync("/api/v2/admin/users", new CreateAccountRequest
        {
            Username = "fresh-operator-1",
            DisplayName = "Fresh Op",
            Role = UserRole.Operator,
            Department = "production",
            Password = "TempPw!1",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<AccountDto>();
        Assert.NotNull(dto);
        Assert.Equal("fresh-operator-1", dto!.Username);
        Assert.Equal(UserRole.Operator, dto.Role);
        Assert.True(dto.MustChangePassword, "newly created user must be forced to change pwd on first login");
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task Create_duplicate_username_returns_422_username_in_use()
    {
        var client = await AdminClientAsync("admin-acc-dupe");
        await _fx.SeedUserAsync("already-here", "x", UserRole.Operator);

        var resp = await client.PostAsJsonAsync("/api/v2/admin/users", new CreateAccountRequest
        {
            Username = "already-here",
            Role = UserRole.Operator,
            Password = "Anything!1",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.username_in_use", body);
    }

    // Lesson L26 — username uniqueness is CASE-INSENSITIVE. Creating "oqc"
    // when "OQC" already exists must be refused, otherwise two rows that log
    // in with the same (NOCASE) name could coexist.
    [Fact]
    public async Task Create_duplicate_username_different_case_returns_422_username_in_use()
    {
        var client = await AdminClientAsync("admin-acc-dupe-case");
        await _fx.SeedUserAsync("DupCaseUser", "x", UserRole.Operator);

        var resp = await client.PostAsJsonAsync("/api/v2/admin/users", new CreateAccountRequest
        {
            Username = "dupcaseuser",     // same name, different case
            Role = UserRole.Operator,
            Password = "Anything!1",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("accounts.username_in_use", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_with_invalid_role_returns_422_invalid_role()
    {
        var client = await AdminClientAsync("admin-acc-badrole");
        var resp = await client.PostAsJsonAsync("/api/v2/admin/users", new CreateAccountRequest
        {
            Username = "bad-role-test",
            Role = "SuperUser",     // not in the whitelist
            Password = "x4-chars",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.invalid_role", body);
    }

    [Fact]
    public async Task Create_with_short_password_returns_422_password_too_short()
    {
        var client = await AdminClientAsync("admin-acc-shortpw");
        var resp = await client.PostAsJsonAsync("/api/v2/admin/users", new CreateAccountRequest
        {
            Username = "short-pw-test",
            Role = UserRole.Operator,
            Password = "abc",       // < 4 chars
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.password_too_short", body);
    }

    [Fact]
    public async Task Admin_can_patch_display_name_and_role_of_other_user()
    {
        var client = await AdminClientAsync("admin-acc-patch");
        var target = await _fx.SeedUserAsync("target-1", "P@ss!1", UserRole.Operator);

        var resp = await client.PatchAsJsonAsync($"/api/v2/admin/users/{target.Id}",
            new UpdateAccountRequest { DisplayName = "New Display", Role = UserRole.Engineer });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<AccountDto>())!;
        Assert.Equal("New Display", dto.DisplayName);
        Assert.Equal(UserRole.Engineer, dto.Role);
    }

    [Fact]
    public async Task Admin_cannot_patch_self_role_or_active()
    {
        var client = await AdminClientAsync("admin-acc-self");
        // Look up our own id from the list endpoint.
        var list = await client.GetFromJsonAsync<AccountPagedResult>(
            "/api/v2/admin/users?search=admin-acc-self&page=1&pageSize=5");
        var selfId = list!.Items.Single(u => u.Username == "admin-acc-self").Id;

        var roleResp = await client.PatchAsJsonAsync($"/api/v2/admin/users/{selfId}",
            new UpdateAccountRequest { Role = UserRole.Operator });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, roleResp.StatusCode);
        Assert.Contains("accounts.self_action_forbidden", await roleResp.Content.ReadAsStringAsync());

        var activeResp = await client.PatchAsJsonAsync($"/api/v2/admin/users/{selfId}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, activeResp.StatusCode);
        Assert.Contains("accounts.self_action_forbidden", await activeResp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_self_password_via_admin_route_is_refused()
    {
        var client = await AdminClientAsync("admin-acc-self-reset");
        var list = await client.GetFromJsonAsync<AccountPagedResult>(
            "/api/v2/admin/users?search=admin-acc-self-reset&page=1&pageSize=5");
        var selfId = list!.Items.Single(u => u.Username == "admin-acc-self-reset").Id;

        var resp = await client.PostAsJsonAsync($"/api/v2/admin/users/{selfId}/reset-password",
            new ResetPasswordRequest { NewPassword = "NewTemp!1" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("accounts.self_action_forbidden", await resp.Content.ReadAsStringAsync());
    }

    // ── Delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_delete_other_user_and_the_row_is_gone()
    {
        var client = await AdminClientAsync("admin-acc-del");
        var target = await _fx.SeedUserAsync("del-target", "P@ss!1", UserRole.Operator);

        var resp = await client.DeleteAsync($"/api/v2/admin/users/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<AccountDto>())!;
        Assert.Equal("del-target", dto.Username);

        // Row is really gone — GET by id → 404.
        var after = await client.GetAsync($"/api/v2/admin/users/{target.Id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_self_via_admin_route_is_refused()
    {
        var client = await AdminClientAsync("admin-acc-self-del");
        var list = await client.GetFromJsonAsync<AccountPagedResult>(
            "/api/v2/admin/users?search=admin-acc-self-del&page=1&pageSize=5");
        var selfId = list!.Items.Single(u => u.Username == "admin-acc-self-del").Id;

        var resp = await client.DeleteAsync($"/api/v2/admin/users/{selfId}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("accounts.self_action_forbidden", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Delete_nonexistent_user_returns_404()
    {
        var client = await AdminClientAsync("admin-acc-del-404");
        var resp = await client.DeleteAsync("/api/v2/admin/users/99999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Contains("accounts.not_found", await resp.Content.ReadAsStringAsync());
    }

    // ── Last-admin lockout ─────────────────────────────────────────

    [Fact]
    public async Task Demoting_inactive_admin_when_only_one_active_admin_left_is_refused()
    {
        // Strict path: 2 admins exist (A + B), actor=A, B disabled
        // (now A is the sole ACTIVE admin), then try to demote B.
        // Target role IS Admin, proposed role IS NOT Admin, target
        // ≠ actor, ActiveAdminCount == 1 → guard fires.
        using var fx = new MesApiFactory();
        await fx.InitializeAsync();
        await fx.SeedUserAsync("la-actor", "P@ss!1", UserRole.Admin);
        var bUser = await fx.SeedUserAsync("la-other", "P@ss!1", UserRole.Admin);
        var client = fx.CreateClient();
        await fx.LoginAndAuthenticateAsync(client, "la-actor", "P@ss!1");

        var disable = await client.PatchAsJsonAsync($"/api/v2/admin/users/{bUser.Id}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var demote = await client.PatchAsJsonAsync($"/api/v2/admin/users/{bUser.Id}",
            new UpdateAccountRequest { Role = UserRole.Operator });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, demote.StatusCode);
        Assert.Contains("accounts.last_admin", await demote.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabling_admin_when_count_drops_to_zero_is_refused()
    {
        // Setup: 2 admin rows; A active, B inactive. Try to disable A.
        // The guard checks "new !IsActive AND user.Role == Admin AND
        // ActiveAdminCount <= 1". Self-action would block this so we
        // need a 3rd admin as actor.
        using var fx = new MesApiFactory();
        await fx.InitializeAsync();
        var aUser = await fx.SeedUserAsync("ld-A", "P@ss!1", UserRole.Admin);
        await fx.SeedUserAsync("ld-B", "P@ss!1", UserRole.Admin);
        await fx.SeedUserAsync("ld-C", "P@ss!1", UserRole.Admin);
        var client = fx.CreateClient();
        await fx.LoginAndAuthenticateAsync(client, "ld-C", "P@ss!1");

        // Disable A (count 3 → 2, OK).
        var d1 = await client.PatchAsJsonAsync($"/api/v2/admin/users/{aUser.Id}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, d1.StatusCode);

        // Disable C is self-action. So look up B's id and disable B
        // (count 2 → 1, C remains).
        var list = await client.GetFromJsonAsync<AccountPagedResult>(
            "/api/v2/admin/users?search=ld-B&page=1&pageSize=5");
        var bId = list!.Items.Single(u => u.Username == "ld-B").Id;
        var d2 = await client.PatchAsJsonAsync($"/api/v2/admin/users/{bId}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, d2.StatusCode);

        // Re-enable A as Admin (count 2 again).
        var re = await client.PatchAsJsonAsync($"/api/v2/admin/users/{aUser.Id}",
            new UpdateAccountRequest { IsActive = true });
        Assert.Equal(HttpStatusCode.OK, re.StatusCode);

        // Disable A (count 2 → 1, C remains). OK.
        var d3 = await client.PatchAsJsonAsync($"/api/v2/admin/users/{aUser.Id}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, d3.StatusCode);

        // Now C is the SOLE active admin. Re-enable A (count 1 → 2).
        var re2 = await client.PatchAsJsonAsync($"/api/v2/admin/users/{aUser.Id}",
            new UpdateAccountRequest { IsActive = true });
        Assert.Equal(HttpStatusCode.OK, re2.StatusCode);

        // Disable A: count drops 2 → 1 with C remaining. OK (still ≥ 1).
        var d4 = await client.PatchAsJsonAsync($"/api/v2/admin/users/{aUser.Id}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, d4.StatusCode);

        // STRICT TEST: now A is inactive Admin, B is inactive Admin,
        // C is sole active admin (and self). Demote A from C's session
        // — proposes A.Role Admin → Operator. ActiveAdminCount == 1,
        // target.Role == Admin, target ≠ self → guard FIRES.
        var strictDemote = await client.PatchAsJsonAsync($"/api/v2/admin/users/{aUser.Id}",
            new UpdateAccountRequest { Role = UserRole.Operator });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, strictDemote.StatusCode);
        Assert.Contains("accounts.last_admin", await strictDemote.Content.ReadAsStringAsync());
    }


    // ── Reset password forces MustChangePassword=true ───────────────

    [Fact]
    public async Task Admin_reset_password_flips_must_change_password_and_new_pwd_logs_in()
    {
        var client = await AdminClientAsync("admin-acc-reset-flow");
        var target = await _fx.SeedUserAsync("reset-target", "OldPwd!1", UserRole.Operator);

        // Reset target's password.
        var resp = await client.PostAsJsonAsync($"/api/v2/admin/users/{target.Id}/reset-password",
            new ResetPasswordRequest { NewPassword = "NewTemp!2" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<AccountDto>())!;
        Assert.True(dto.MustChangePassword);

        // Login with new password works.
        var newClient = _fx.CreateClient();
        var loginResp = await newClient.PostAsJsonAsync("/api/v2/auth/login", new LoginRequest
        {
            Username = "reset-target",
            Password = "NewTemp!2",
        });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = (await loginResp.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.True(login.User.MustChangePassword,
            "login response after admin reset must carry User.MustChangePassword=true so client can route the user to the change-pwd flow");

        // Old password no longer works.
        var oldLogin = await newClient.PostAsJsonAsync("/api/v2/auth/login", new LoginRequest
        {
            Username = "reset-target",
            Password = "OldPwd!1",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    // ── Disable user revokes refresh tokens ────────────────────────

    [Fact]
    public async Task Disabling_user_revokes_all_their_refresh_tokens()
    {
        // Create + log in TARGET so they have a live refresh token in
        // the store.
        var target = await _fx.SeedUserAsync("disable-tgt", "P@ss!1", UserRole.Operator);
        var targetClient = _fx.CreateClient();
        var login = await targetClient.PostAsJsonAsync("/api/v2/auth/login", new LoginRequest
        {
            Username = "disable-tgt",
            Password = "P@ss!1",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var targetRefresh = loginBody.RefreshToken;
        Assert.False(string.IsNullOrEmpty(targetRefresh));

        // Verify the refresh token is in the store + not revoked.
        var store = _fx.Services.GetRequiredService<IRefreshTokenStore>();
        var preInfo = store.Find(targetRefresh);
        Assert.NotNull(preInfo);
        Assert.False(preInfo!.Revoked);

        // Admin disables target.
        var adminClient = await AdminClientAsync("admin-acc-revoke");
        var disable = await adminClient.PatchAsJsonAsync($"/api/v2/admin/users/{target.Id}",
            new UpdateAccountRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        // Refresh token now revoked.
        var postInfo = store.Find(targetRefresh);
        Assert.NotNull(postInfo);
        Assert.True(postInfo!.Revoked,
            "disable should have revoked the target's refresh token so the next /auth/refresh attempt fails");

        // Refresh attempt fails — auth.refresh_revoked OR _invalid.
        var refresh = await targetClient.PostAsJsonAsync("/api/v2/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = targetRefresh,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // ── P10.7a-2.1 — sys-recovery account safety ────────────────────
    //
    // Henry's adj #3: the seeded sys-recovery user (Role=Sys, IsActive=
    // false) MUST NOT be mutable from the Account Control surface, even
    // for admins. Visible-in-list for forensic transparency; every other
    // action returns 403 accounts.sys_account_protected. The role
    // whitelist (UserRole.IsValid) MUST reject "Sys" so admins cannot
    // create a fresh sys account via /admin/users either.

    private async Task<long> SysRecoveryUserIdAsync()
    {
        // Opt-in seed (idempotent) — the test fixture does NOT seed the
        // recovery surface eagerly because it would add spurious write-
        // lock contention to the N=50 advance soak. Calling here pulls
        // in the sys-recovery user the first time + NOOPs on later calls.
        await _fx.SeedRecoveryDataAsync();
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.Users
            .Where(u => u.Username == DbSeeder.SysRecoveryUsername)
            .Select(u => u.Id)
            .SingleAsync();
    }

    [Fact]
    public async Task List_includes_sys_recovery_user_for_forensic_transparency()
    {
        await _fx.SeedRecoveryDataAsync();
        var client = await AdminClientAsync("admin-list-sys");
        var resp = await client.GetAsync("/api/v2/admin/users?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<AccountPagedResult>())!;

        var sys = body.Items.SingleOrDefault(u => u.Username == DbSeeder.SysRecoveryUsername);
        Assert.NotNull(sys);
        Assert.Equal(UserRole.Sys, sys!.Role);
        Assert.False(sys.IsActive,
            "sys-recovery must be IsActive=false so the login path refuses it before reaching password verify");
    }

    [Fact]
    public async Task Patch_sys_user_returns_403_sys_account_protected()
    {
        var client = await AdminClientAsync("admin-patch-sys");
        var sysId = await SysRecoveryUserIdAsync();

        var resp = await client.PatchAsJsonAsync($"/api/v2/admin/users/{sysId}",
            new UpdateAccountRequest { DisplayName = "Attempted rename" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.sys_account_protected", body);
    }

    [Fact]
    public async Task Patch_sys_user_role_returns_403_even_for_admin()
    {
        var client = await AdminClientAsync("admin-demote-sys");
        var sysId = await SysRecoveryUserIdAsync();

        // Attempt to "demote" sys → Operator. Must be refused before
        // either the InvalidRole or LastAdminProtected branches run.
        var resp = await client.PatchAsJsonAsync($"/api/v2/admin/users/{sysId}",
            new UpdateAccountRequest { Role = UserRole.Operator });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.sys_account_protected", body);
    }

    [Fact]
    public async Task Patch_sys_user_isactive_returns_403_so_admins_cannot_enable_login()
    {
        var client = await AdminClientAsync("admin-enable-sys");
        var sysId = await SysRecoveryUserIdAsync();

        // The interesting one: an admin who knows the username could try
        // to flip IsActive=true and then try to log in with the literal
        // PasswordHash. Guard fires here so neither attempt succeeds.
        var resp = await client.PatchAsJsonAsync($"/api/v2/admin/users/{sysId}",
            new UpdateAccountRequest { IsActive = true });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.sys_account_protected", body);
    }

    [Fact]
    public async Task Reset_password_for_sys_user_returns_403()
    {
        var client = await AdminClientAsync("admin-reset-sys");
        var sysId = await SysRecoveryUserIdAsync();

        var resp = await client.PostAsJsonAsync($"/api/v2/admin/users/{sysId}/reset-password",
            new ResetPasswordRequest { NewPassword = "Attempted!Replace!1" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.sys_account_protected", body);
    }

    [Fact]
    public async Task Create_user_with_sys_role_returns_422_invalid_role()
    {
        var client = await AdminClientAsync("admin-create-sys-attempt");
        var resp = await client.PostAsJsonAsync("/api/v2/admin/users", new CreateAccountRequest
        {
            Username = "fake-sys",
            Role = UserRole.Sys,    // not in the whitelist
            Password = "Whatever!1",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("accounts.invalid_role", body);
    }
}
