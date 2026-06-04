using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Auth;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// JWT login + refresh + logout + me. The login flow mirrors the legacy
/// <c>Login.cshtml.cs</c> Razor Page semantically: same Users table,
/// same <see cref="IPasswordHasher{User}"/>, same generic-error policy
/// (so wrong-username and wrong-password look identical to attackers),
/// same <c>LoginFail</c>/<c>LoginDisabled</c>/<c>LoginOk</c> audit codes.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly JwtTokenIssuer _tokens;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IAuditWriter _audit;
    private readonly Auth.JwtOptions _jwtOpts;

    public AuthController(
        IMesDbContext db,
        IPasswordHasher<User> hasher,
        JwtTokenIssuer tokens,
        IRefreshTokenStore refreshStore,
        IAuditWriter audit,
        Microsoft.Extensions.Options.IOptions<Auth.JwtOptions> jwtOpts)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _refreshStore = refreshStore;
        _audit = audit;
        _jwtOpts = jwtOpts.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiError.Of("auth.missing_fields", "Username and password required."));

        var username = req.Username.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

        // Same generic error for "user not found" + "wrong password" so attackers
        // can't probe for valid usernames. Legacy Login.cshtml.cs:69-83 pattern.
        if (user is null
            || _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
        {
            await _audit.EmitAsync(
                AuditAction.LoginFail,
                actor: "anonymous", actorRole: "",
                targetType: "User", targetId: null,
                detail: JsonSerializer.Serialize(new { typed_username = username, device_id = req.DeviceId }));
            return Unauthorized(ApiError.Of("auth.invalid_credentials", "Invalid username or password."));
        }

        // Disabled accounts share the same generic error so a disabled
        // username doesn't become a probe oracle. Legacy parity.
        if (!user.IsActive)
        {
            await _audit.EmitAsync(
                AuditAction.LoginDisabled,
                actor: "anonymous", actorRole: "",
                targetType: "User", targetId: user.Id.ToString(),
                detail: JsonSerializer.Serialize(new { typed_username = username, device_id = req.DeviceId }));
            return Unauthorized(ApiError.Of("auth.invalid_credentials", "Invalid username or password."));
        }

        var now = DateTime.UtcNow;
        var resp = IssueTokenPair(user, now, familyId: Guid.NewGuid());

        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.LoginOk,
            actor: user.Username, actorRole: user.Role,
            targetType: "User", targetId: user.Id.ToString(),
            detail: req.DeviceId is null ? null
                : JsonSerializer.Serialize(new { device_id = req.DeviceId, scheme = "jwt" }));

        return Ok(resp);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest(ApiError.Of("auth.missing_refresh", "Refresh token required."));

        var info = _refreshStore.Find(req.RefreshToken);
        var now = DateTime.UtcNow;

        if (info is null || info.ExpiresAt <= now)
            return Unauthorized(ApiError.Of("auth.refresh_invalid", "Refresh token is unknown or expired."));

        // Re-use detection: a revoked token is showing up again. Assume the
        // family is leaking — revoke every sibling. Caller must log in again.
        if (info.Revoked)
        {
            _refreshStore.RevokeFamily(info.FamilyId);
            await _audit.EmitAsync(
                "AUTH_REFRESH_REUSE", actor: "anonymous", actorRole: "",
                targetType: "User", targetId: info.UserId.ToString(),
                detail: JsonSerializer.Serialize(new { family_id = info.FamilyId }));
            return Unauthorized(ApiError.Of("auth.refresh_replay",
                "Refresh token was already used. Please sign in again."));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == info.UserId);
        if (user is null || !user.IsActive)
        {
            // User deleted or disabled between refreshes — revoke + 401.
            _refreshStore.Revoke(req.RefreshToken);
            return Unauthorized(ApiError.Of("auth.user_unavailable",
                "User no longer eligible for refresh."));
        }

        // One-time-use rotation: revoke the supplied token, mint a fresh
        // pair, keep the same family id so re-use detection survives.
        _refreshStore.Revoke(req.RefreshToken);
        var resp = IssueTokenPair(user, now, info.FamilyId);
        return Ok(resp);
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout([FromBody] RefreshTokenRequest req)
    {
        // Logout = revoke the refresh token. Access tokens stay valid until
        // their 15-minute window expires — caller drops them locally.
        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
            _refreshStore.Revoke(req.RefreshToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserInfo> Me()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var id))
            return Unauthorized(ApiError.Of("auth.bad_claim", "Token is missing user id."));

        // P10.6c — surface MustChangePassword so the client routes the
        // user to the change-pwd flow if they've just been admin-reset
        // or freshly created. Cheap single-row lookup.
        var mustChange = _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.MustChangePassword)
            .FirstOrDefault();

        return Ok(new UserInfo
        {
            Id = id,
            Username = User.FindFirstValue(ClaimTypes.Name) ?? "",
            Role = User.FindFirstValue(ClaimTypes.Role) ?? "",
            DisplayName = User.FindFirstValue("display_name") ?? "",
            Department = User.FindFirstValue("department") ?? "",
            Language = "",
            MustChangePassword = mustChange,
        });
    }

    // ── helpers ──────────────────────────────────────────────────────

    private LoginResponse IssueTokenPair(User user, DateTime now, Guid familyId)
    {
        var (access, accessExpires) = _tokens.CreateAccessToken(user, now);
        var refresh = JwtTokenIssuer.CreateRefreshToken();
        var refreshExpires = now.Add(_jwtOpts.RefreshTokenLifetime);

        _refreshStore.Store(refresh, new RefreshTokenInfo(
            UserId: user.Id,
            ExpiresAt: refreshExpires,
            FamilyId: familyId,
            Revoked: false));

        return new LoginResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
            AccessTokenExpiresAt = accessExpires,
            RefreshTokenExpiresAt = refreshExpires,
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role,
                DisplayName = user.DisplayName ?? user.Username,
                Department = user.Department ?? "",
                Language = "",
                MustChangePassword = user.MustChangePassword,
            },
        };
    }
}
