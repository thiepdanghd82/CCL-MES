using System.Security.Claims;
using CCL.MES.Api.Auth;
using CCL.MES.Application;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// P10.6c — Admin Account Control service for the Hybrid API.
///
/// Slim port of the legacy <c>CCL.MES.Web.Services.UserAdminService</c>
/// (which the API project does NOT reference). Same lockout-safety
/// invariants, same audit-event contract — only the call surface
/// differs: DTOs in / out (not entities), <see cref="AccountResult"/>
/// discriminator instead of legacy <c>UserAdminResult</c>, JSON
/// detail emitted by the controller (same shape, kept central so a
/// service change can't drift from the audit fixture).
///
/// LOCKOUT GUARDS (the dangerous paths):
///   1. Cannot disable / demote the last active Admin. Pre-flight
///      <c>ActiveAdminCountAsync()</c> against the proposed change.
///   2. Cannot modify SELF on this surface — display name + password
///      changes for one's own account belong on
///      <c>UserProfileService</c> (/settings/me + /settings/password).
///      Disable-self + demote-self specifically prevent an admin from
///      booting themselves out of the system.
///   3. Username uniqueness — DB index from Phase 2 is the final
///      guard; pre-flight COUNT keeps the error code stable
///      (<c>accounts.username_in_use</c>) instead of leaking a 500
///      from the index violation.
///   4. Role whitelist — <see cref="UserRole.IsValid"/> mirrors
///      legacy seed migration; bad role → <c>accounts.invalid_role</c>.
///   5. Password length — minimum 4 chars (matches
///      <c>UserProfileService.ChangePassword</c> + legacy
///      <c>UserAdminService.ResetPassword</c>).
///
/// SESSION REVOKE POLICY (P10.6c decision):
///   Disable user → IsActive flips false (login + refresh both
///   refuse on the existing AuthController guards) + all of that
///   user's refresh tokens are immediately revoked via
///   <see cref="IRefreshTokenStore.RevokeAllForUser"/>. The access
///   token (≤15 min TTL per JwtOptions) remains valid; effective
///   logout window ≤ 15 minutes. Reset password does NOT revoke
///   refresh tokens — the target user might still be using the
///   active session; <see cref="User.MustChangePassword"/> = true
///   forces them through the change flow at their next login.
/// </summary>
public sealed class AccountControlService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MinPasswordLength = 4;

    private readonly IMesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IRefreshTokenStore _refreshStore;

    public AccountControlService(
        IMesDbContext db,
        IPasswordHasher<User> hasher,
        IRefreshTokenStore refreshStore)
    {
        _db = db;
        _hasher = hasher;
        _refreshStore = refreshStore;
    }

    // ── List ────────────────────────────────────────────────────────

    public async Task<AccountPagedResult> ListAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize;

        var q = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u =>
                EF.Functions.Like(u.Username, $"%{s}%")
                || (u.DisplayName != null && EF.Functions.Like(u.DisplayName, $"%{s}%"))
                || EF.Functions.Like(u.Role, $"%{s}%")
                || (u.Department != null && EF.Functions.Like(u.Department, $"%{s}%")));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => ToDto(u))
            .ToListAsync(ct);

        return new AccountPagedResult
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<AccountDto?> GetByIdAsync(long id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? null : ToDto(u);
    }

    // ── Create ──────────────────────────────────────────────────────

    public async Task<AccountMutationResult> CreateAsync(
        CreateAccountRequest req, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (req is null) return AccountMutationResult.Failed(AccountResult.InvalidBody);
        if (string.IsNullOrWhiteSpace(req.Username))
            return AccountMutationResult.Failed(AccountResult.UsernameRequired);
        // P10.7a-2.1 — UserRole.IsValid already excludes "Sys" from the
        // whitelist; the InvalidRole branch covers attempted Sys creation.
        if (!UserRole.IsValid(req.Role))
            return AccountMutationResult.Failed(AccountResult.InvalidRole);
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < MinPasswordLength)
            return AccountMutationResult.Failed(AccountResult.PasswordTooShort);

        var username = req.Username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            return AccountMutationResult.Failed(AccountResult.UsernameInUse);

        var user = new User
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName!.Trim(),
            Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department!.Trim(),
            Role = req.Role,
            IsActive = true,
            // Phase 6 Bước 4 convention: every admin-handed temp pwd forces
            // a change at the first sign-in. Operator → reset → set their
            // own pwd → MustChangePassword auto-clears on success.
            MustChangePassword = true,
        };
        user.PasswordHash = _hasher.HashPassword(user, req.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return AccountMutationResult.Success(ToDto(user));
    }

    // ── Update (role / displayName / department / active) ──────────

    public async Task<AccountMutationResult> UpdateAsync(
        long userId, UpdateAccountRequest req, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (req is null) return AccountMutationResult.Failed(AccountResult.InvalidBody);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AccountMutationResult.Failed(AccountResult.NotFound);

        // P10.7a-2.1 — sys-role accounts are audit-only. Mutation MUST be
        // blocked at this surface (even for admins) so the sys-recovery
        // user can never have its role/IsActive/displayName/department
        // changed via the UI. Guard sits BEFORE the SelfModificationBlocked
        // + LastAdminProtected checks since those reason codes would leak
        // the wrong VN error to the operator.
        if (UserRole.IsSystemAccount(user.Role))
            return AccountMutationResult.Failed(AccountResult.SysAccountProtected);

        var isSelf = IsActor(user, actor);

        // Self-modification is allowed for cosmetic fields only — display
        // name + department. Role + active flag for SELF would let an
        // admin permanently lock themselves out, so refuse loudly.
        if (req.Role is not null && !string.Equals(req.Role, user.Role, StringComparison.Ordinal))
        {
            if (!UserRole.IsValid(req.Role))
                return AccountMutationResult.Failed(AccountResult.InvalidRole);
            if (isSelf)
                return AccountMutationResult.Failed(AccountResult.SelfModificationBlocked);
            if (user.Role == UserRole.Admin && req.Role != UserRole.Admin
                && await ActiveAdminCountAsync(ct) <= 1)
            {
                return AccountMutationResult.Failed(AccountResult.LastAdminProtected);
            }
            user.Role = req.Role;
        }

        if (req.IsActive is { } targetActive && targetActive != user.IsActive)
        {
            if (isSelf && !targetActive)
                return AccountMutationResult.Failed(AccountResult.SelfModificationBlocked);
            if (!targetActive && user.Role == UserRole.Admin
                && await ActiveAdminCountAsync(ct) <= 1)
            {
                return AccountMutationResult.Failed(AccountResult.LastAdminProtected);
            }
            user.IsActive = targetActive;
            // Per the session-revoke policy doc-comment: disable kills
            // all of this user's refresh tokens immediately. Access
            // token lives the natural ≤15-min remainder.
            if (!targetActive) _refreshStore.RevokeAllForUser(user.Id);
        }

        if (req.DisplayName is not null)
            user.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName.Trim();
        if (req.Department is not null)
            user.Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim();

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return AccountMutationResult.Success(ToDto(user));
    }

    // ── Reset Password ──────────────────────────────────────────────

    public async Task<AccountMutationResult> ResetPasswordAsync(
        long userId, ResetPasswordRequest req, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (req is null) return AccountMutationResult.Failed(AccountResult.InvalidBody);
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < MinPasswordLength)
            return AccountMutationResult.Failed(AccountResult.PasswordTooShort);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AccountMutationResult.Failed(AccountResult.NotFound);

        // P10.7a-2.1 — same sys-role guard as UpdateAsync. The sys-recovery
        // password is a sentinel literal that the seed plants; resetting it
        // would expose a login path on the audit-only account.
        if (UserRole.IsSystemAccount(user.Role))
            return AccountMutationResult.Failed(AccountResult.SysAccountProtected);

        // Self-reset goes through /settings/password not here — that
        // route requires the current password as a re-auth proof; this
        // admin route skips that proof so it MUST NOT apply to self.
        if (IsActor(user, actor))
            return AccountMutationResult.Failed(AccountResult.SelfModificationBlocked);

        user.PasswordHash = _hasher.HashPassword(user, req.NewPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return AccountMutationResult.Success(ToDto(user));
    }

    // ── Delete ──────────────────────────────────────────────────────

    /// <summary>
    /// Permanently remove a user row. Same guard set as reset/update:
    ///   - sys-role accounts are audit-only (403);
    ///   - self-delete is refused (an admin can't remove their own login);
    ///   - deleting the LAST active admin is refused (zero-admin lockout).
    /// There are no FK references to Users (audit + WO rows carry the
    /// username as a plain string), so the row deletes cleanly; the
    /// audit trail keeps the historical username.
    /// </summary>
    public async Task<AccountMutationResult> DeleteAsync(
        long userId, ClaimsPrincipal actor, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AccountMutationResult.Failed(AccountResult.NotFound);

        if (UserRole.IsSystemAccount(user.Role))
            return AccountMutationResult.Failed(AccountResult.SysAccountProtected);

        if (IsActor(user, actor))
            return AccountMutationResult.Failed(AccountResult.SelfModificationBlocked);

        // Refuse if this is the last active admin — deleting it would leave
        // the system with no way in (mirrors the demote/disable guard).
        if (user.Role == UserRole.Admin && user.IsActive
            && await ActiveAdminCountAsync(ct) <= 1)
        {
            return AccountMutationResult.Failed(AccountResult.LastAdminProtected);
        }

        // Snapshot for the audit + response BEFORE the row is gone.
        var dto = ToDto(user);
        // Kill any live sessions immediately; the access token lives out its
        // ≤15-min natural TTL but no new pair can be minted.
        _refreshStore.RevokeAllForUser(user.Id);
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return AccountMutationResult.Success(dto);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private Task<int> ActiveAdminCountAsync(CancellationToken ct) =>
        _db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, ct);

    private static bool IsActor(User user, ClaimsPrincipal actor)
    {
        var idStr = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idStr, out var actorId) && actorId == user.Id;
    }

    private static AccountDto ToDto(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Role = u.Role,
        DisplayName = u.DisplayName,
        Department = u.Department,
        IsActive = u.IsActive,
        MustChangePassword = u.MustChangePassword,
        LastLoginAtUtc = u.LastLoginAt,
        CreatedAtUtc = u.CreatedAt,
        UpdatedAtUtc = u.UpdatedAt,
    };
}

/// <summary>P10.6c — discriminator for the 5 admin-account mutations.
/// Maps 1:1 to the stable <c>accounts.*</c> error codes the VN mapper
/// translates.</summary>
public enum AccountResult
{
    Success = 0,
    NotFound = 1,
    InvalidBody = 2,
    UsernameRequired = 10,
    UsernameInUse = 11,
    InvalidRole = 12,
    PasswordTooShort = 13,
    SelfModificationBlocked = 20,
    LastAdminProtected = 21,
    // P10.7a-2.1 — sys-role audit-only account protection.
    SysAccountProtected = 22,
}

/// <summary>Envelope so the controller can return both the outcome
/// + the fresh DTO on success in a single call.</summary>
public sealed record AccountMutationResult
{
    public AccountResult Outcome { get; init; }
    public AccountDto? Account { get; init; }

    public static AccountMutationResult Success(AccountDto dto) =>
        new() { Outcome = AccountResult.Success, Account = dto };

    public static AccountMutationResult Failed(AccountResult code) =>
        new() { Outcome = code, Account = null };
}
