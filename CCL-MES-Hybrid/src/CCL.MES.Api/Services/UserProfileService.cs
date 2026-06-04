using System.Security.Claims;
using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// P10.6a — self-service profile/password service for the MAUI Hybrid
/// client. Mirrors <c>CCL.MES.Web.Services.UserProfileService</c> 1:1
/// (same EF queries, same password-verify pattern, same return enum)
/// so behaviour stays bit-identical across the two surfaces. The Web
/// service stays untouched (legacy 0 diff).
///
/// Why a parallel impl instead of a project reference: the Web service
/// lives in <c>CCL.MES.Web</c> which is a Blazor Server app (not a
/// class library), and depending the Api project on Web would invert
/// the architecture. Both files are ~95 LOC of straight EF + hasher
/// calls — duplication cost is small.
///
/// Self-service only — every method loads the current user via the
/// <see cref="ClaimsPrincipal"/> NameIdentifier claim. No
/// <c>userId</c> parameter from the page, so a UI bug cannot
/// accidentally edit another account.
/// </summary>
public sealed class UserProfileService
{
    private readonly IMesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public UserProfileService(IMesDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>Resolve the signed-in <see cref="User"/> row from the
    /// NameIdentifier claim. Null when the principal is anonymous or
    /// the id doesn't parse / match a row.</summary>
    public async Task<User?> GetCurrentAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var id)) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    /// <summary>
    /// Update <see cref="User.DisplayName"/> for the signed-in user.
    /// Trim + null when blank so the DB column never carries
    /// whitespace-only strings. Returns false when the principal does
    /// not resolve to a row (mirrors Web behaviour — caller renders
    /// 404 in that case).
    /// </summary>
    public async Task<bool> UpdateDisplayNameAsync(
        ClaimsPrincipal principal, string? newDisplayName, CancellationToken ct = default)
    {
        var user = await GetCurrentAsync(principal, ct);
        if (user is null) return false;
        user.DisplayName = string.IsNullOrWhiteSpace(newDisplayName) ? null : newDisplayName.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Change the signed-in user's password. Returns one of
    /// <see cref="PasswordChangeResult"/>; the controller maps each
    /// outcome to an HTTP envelope.
    /// </summary>
    public async Task<PasswordChangeResult> ChangePasswordAsync(
        ClaimsPrincipal principal, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await GetCurrentAsync(principal, ct);
        if (user is null) return PasswordChangeResult.UserNotFound;

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword);
        if (verify == PasswordVerificationResult.Failed) return PasswordChangeResult.WrongCurrent;

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            return PasswordChangeResult.NewTooShort;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        // Mirror Web Bước 4 — successful self-change clears the admin-
        // handed-pwd flag so the user is not forced to change again on
        // the next sign-in.
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return PasswordChangeResult.Success;
    }
}

/// <summary>Outcome of <see cref="UserProfileService.ChangePasswordAsync"/>.</summary>
public enum PasswordChangeResult
{
    Success,
    UserNotFound,
    WrongCurrent,
    NewTooShort,
}
