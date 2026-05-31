using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 2B — admin read-only list of users for the Account
/// Control tab.
/// Phase 6 Bước 4 — adds mutation methods (Create / UpdateDisplayName /
/// UpdateRole / ResetPassword / SetActive) backed by safety invariants
/// that make lockout impossible from the UI:
///   - cannot demote the last active Admin
///   - cannot disable the last active Admin
///   - cannot modify the signed-in user from this surface (UserProfileService
///     owns self-edits)
///   - role must be in <see cref="UserRole.All"/>
/// All mutations require the actor to already pass [Authorize(Policy =
/// "AdminOnly")] at the page level — this service trusts the caller to be
/// an admin (the page gate enforces that), and adds the lockout rails on
/// top. Returns a result enum so the Razor page can map to localized
/// messages without throwing.
/// </summary>
public class UserAdminService
{
    private readonly IMesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IAuditWriter _audit;

    public UserAdminService(IMesDbContext db, IPasswordHasher<User> hasher, IAuditWriter audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    public Task<PagedResult<User>> ListAsync(string? search, int page, int pageSize)
    {
        var q = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => EF.Functions.Like(u.Username, $"%{s}%")
                          || (u.DisplayName != null && EF.Functions.Like(u.DisplayName, $"%{s}%"))
                          || EF.Functions.Like(u.Role, $"%{s}%"));
        }
        return PagingHelper.PageAsync(q.OrderBy(u => u.Username), page, pageSize);
    }

    public Task<User?> GetByIdAsync(long id) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id);

    /// <summary>Create a new user. Username is unique (DB index from Phase 2).</summary>
    public async Task<UserAdminResult> CreateAsync(
        string username, string? displayName, string role, string password, ClaimsPrincipal actor)
    {
        if (string.IsNullOrWhiteSpace(username))
            return UserAdminResult.UsernameRequired;
        if (!UserRole.IsValid(role))
            return UserAdminResult.InvalidRole;
        if (string.IsNullOrEmpty(password) || password.Length < 4)
            return UserAdminResult.PasswordTooShort;

        username = username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == username))
            return UserAdminResult.UsernameInUse;

        var user = new User
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            Role = role,
            IsActive = true,
            MustChangePassword = true,
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (actorName, actorRole) = actor.AuditIdentity();
        await _audit.EmitAsync(
            AuditAction.UserCreate, actorName, actorRole,
            targetType: "User", targetId: user.Id.ToString(),
            detail: JsonSerializer.Serialize(new { username, role }));
        return UserAdminResult.Success;
    }

    /// <summary>
    /// Update display name of any user (admin-only surface). Username +
    /// role + password unchanged here.
    /// </summary>
    public async Task<UserAdminResult> UpdateDisplayNameAsync(
        long userId, string? newDisplayName, ClaimsPrincipal actor)
    {
        var user = await GetByIdAsync(userId);
        if (user is null) return UserAdminResult.NotFound;

        var before = user.DisplayName;
        user.DisplayName = string.IsNullOrWhiteSpace(newDisplayName) ? null : newDisplayName.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (actorName, actorRole) = actor.AuditIdentity();
        await _audit.EmitAsync(
            AuditAction.UserDisplayChange, actorName, actorRole,
            targetType: "User", targetId: user.Id.ToString(),
            detail: JsonSerializer.Serialize(new { username = user.Username, before, after = user.DisplayName }));
        return UserAdminResult.Success;
    }

    /// <summary>
    /// Change a user's role. Refuses if the target IS the actor (self-edit
    /// belongs on the Profile page; preventing here avoids accidental
    /// self-demote), if the new role is not in the whitelist, or if the
    /// change would demote the last active Admin.
    /// </summary>
    public async Task<UserAdminResult> UpdateRoleAsync(
        long userId, string newRole, ClaimsPrincipal actor)
    {
        if (!UserRole.IsValid(newRole)) return UserAdminResult.InvalidRole;

        var user = await GetByIdAsync(userId);
        if (user is null) return UserAdminResult.NotFound;

        if (IsActor(user, actor)) return UserAdminResult.SelfModificationBlocked;
        if (user.Role == newRole) return UserAdminResult.Success; // no-op

        // Last-active-admin protection.
        if (user.Role == UserRole.Admin && newRole != UserRole.Admin)
        {
            if (await ActiveAdminCountAsync() <= 1)
                return UserAdminResult.LastAdminProtected;
        }

        var before = user.Role;
        user.Role = newRole;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (actorName, actorRole) = actor.AuditIdentity();
        await _audit.EmitAsync(
            AuditAction.UserRoleChange, actorName, actorRole,
            targetType: "User", targetId: user.Id.ToString(),
            detail: JsonSerializer.Serialize(new { username = user.Username, before, after = newRole }));
        return UserAdminResult.Success;
    }

    /// <summary>
    /// Reset another user's password. Flips MustChangePassword=true so the
    /// next login forces them to set a new one (admin-handed temp pwds are
    /// never re-usable past the first sign-in).
    /// </summary>
    public async Task<UserAdminResult> ResetPasswordAsync(
        long userId, string newPassword, ClaimsPrincipal actor)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            return UserAdminResult.PasswordTooShort;

        var user = await GetByIdAsync(userId);
        if (user is null) return UserAdminResult.NotFound;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (actorName, actorRole) = actor.AuditIdentity();
        // Audit Detail INTENTIONALLY does NOT include the new password
        // (or hash) — sanitize whitelist convention from
        // docs/PHASE6-STEP5-PLAN.md.
        await _audit.EmitAsync(
            AuditAction.UserResetPassword, actorName, actorRole,
            targetType: "User", targetId: user.Id.ToString(),
            detail: JsonSerializer.Serialize(new { username = user.Username, must_change = true }));
        return UserAdminResult.Success;
    }

    /// <summary>
    /// Toggle active flag. Refuses self-disable + last-active-admin
    /// disable. Soft-delete only; no hard delete from this surface.
    /// </summary>
    public async Task<UserAdminResult> SetActiveAsync(
        long userId, bool isActive, ClaimsPrincipal actor)
    {
        var user = await GetByIdAsync(userId);
        if (user is null) return UserAdminResult.NotFound;

        if (IsActor(user, actor) && !isActive)
            return UserAdminResult.SelfModificationBlocked;

        if (!isActive && user.Role == UserRole.Admin)
        {
            if (await ActiveAdminCountAsync() <= 1)
                return UserAdminResult.LastAdminProtected;
        }

        if (user.IsActive == isActive) return UserAdminResult.Success; // no-op

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (actorName, actorRole) = actor.AuditIdentity();
        await _audit.EmitAsync(
            AuditAction.UserSetActive, actorName, actorRole,
            targetType: "User", targetId: user.Id.ToString(),
            detail: JsonSerializer.Serialize(new { username = user.Username, is_active = isActive }));
        return UserAdminResult.Success;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private Task<int> ActiveAdminCountAsync() =>
        _db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive);

    private static bool IsActor(User user, ClaimsPrincipal actor)
    {
        var idStr = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idStr, out var actorId) && actorId == user.Id;
    }
}

public enum UserAdminResult
{
    Success,
    NotFound,
    UsernameRequired,
    UsernameInUse,
    InvalidRole,
    PasswordTooShort,
    LastAdminProtected,
    SelfModificationBlocked,
}
