using System.Security.Claims;
using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 2A — service layer cho 3 Settings tab User group
/// (My Profile, My Password, Appearance). Lives in Web (not Application)
/// because password verify/hash uses <see cref="IPasswordHasher{User}"/>
/// from <c>Microsoft.AspNetCore.Identity</c>, which the Application class
/// lib intentionally does NOT depend on. Self-service only: every method
/// loads the current user via the ClaimsPrincipal — no userId parameter
/// from the page, so a UI bug cannot accidentally edit another account.
/// </summary>
public class UserProfileService
{
    private readonly IMesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public UserProfileService(IMesDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>Resolve the currently signed-in <see cref="User"/> row.</summary>
    public async Task<User?> GetCurrentAsync(ClaimsPrincipal principal)
    {
        var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var id)) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    /// <summary>
    /// Update <see cref="User.DisplayName"/> for the signed-in user only.
    /// Returns <c>true</c> on success; <c>false</c> if the principal does
    /// not resolve to a row.
    /// </summary>
    public async Task<bool> UpdateDisplayNameAsync(ClaimsPrincipal principal, string? newDisplayName)
    {
        var user = await GetCurrentAsync(principal);
        if (user is null) return false;
        user.DisplayName = string.IsNullOrWhiteSpace(newDisplayName) ? null : newDisplayName.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Change the signed-in user's password. Returns one of
    /// <see cref="PasswordChangeResult"/>:
    /// <list type="bullet">
    /// <item><c>Success</c> — old verified + new hashed + saved.</item>
    /// <item><c>UserNotFound</c> — principal does not resolve to a row.</item>
    /// <item><c>WrongCurrent</c> — old password did not verify.</item>
    /// <item><c>NewTooShort</c> — new password &lt; 4 chars (Phase 2 demo
    /// minimum; Phase 6+ can tighten via Settings policy).</item>
    /// </list>
    /// </summary>
    public async Task<PasswordChangeResult> ChangePasswordAsync(
        ClaimsPrincipal principal, string oldPassword, string newPassword)
    {
        var user = await GetCurrentAsync(principal);
        if (user is null) return PasswordChangeResult.UserNotFound;

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword);
        if (verify == PasswordVerificationResult.Failed) return PasswordChangeResult.WrongCurrent;

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            return PasswordChangeResult.NewTooShort;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        // Phase 6 Bước 4 follow-up — successful self-change clears the
        // admin-handed-pwd flag so the user is not forced to change again
        // on the next sign-in. The column exists from Bước 4's migration
        // v2; on a pre-v2 DB this assignment is a no-op against an entity
        // property that doesn't map yet.
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return PasswordChangeResult.Success;
    }
}

public enum PasswordChangeResult
{
    Success,
    UserNotFound,
    WrongCurrent,
    NewTooShort,
}
