using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.6a — Settings sub-tabs for the MAUI Hybrid client.
///
/// Surfaces this PR ships:
///   GET   <c>/api/v2/settings/me</c>          — My Profile read
///   PATCH <c>/api/v2/settings/me</c>          — My Profile edit (DisplayName)
///   POST  <c>/api/v2/settings/password</c>    — My Password change
///
/// Auth: every endpoint requires an authenticated principal but NO
/// role gate — these are self-service surfaces. The
/// <see cref="UserProfileService"/> loads the user via the JWT
/// NameIdentifier claim so a UI bug cannot edit a different account.
///
/// Audit: profile edit emits <c>PROFILE_UPDATE</c>; password change
/// emits <c>PASSWORD_CHANGE</c>. Both carry the actor + (for
/// profile) the before/after DisplayName so a forensic search can
/// trail the rename.
///
/// Error envelope follows the existing <see cref="ApiError"/>
/// pattern so the client mapper (SpecMutationErrorMapper) renders
/// VN messages keyed by <c>auth.*</c> / <c>profile.*</c> codes
/// without bespoke per-controller translations.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/settings")]
[Authorize]
public sealed class SettingsController : ControllerBase
{
    private readonly UserProfileService _users;
    private readonly IAuditWriter _audit;

    public SettingsController(UserProfileService users, IAuditWriter audit)
    {
        _users = users;
        _audit = audit;
    }

    // ── My Profile ──────────────────────────────────────────────────

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var user = await _users.GetCurrentAsync(User, ct);
        if (user is null) return NotFound(new ApiError { Code = "profile.not_found", MessageEn = "Profile not found." });

        return Ok(new SettingsProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role,
            DisplayName = user.DisplayName,
            Department = user.Department,
            MustChangePassword = user.MustChangePassword,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        });
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        if (req is null)
            return BadRequest(new ApiError { Code = "profile.invalid_body", MessageEn = "Request body is required." });

        // Cap the display name at the same length as Web's UI
        // affordance — 100 chars covers any realistic operator name
        // without inviting unbounded blob writes.
        if (req.DisplayName is not null && req.DisplayName.Length > 100)
            return UnprocessableEntity(new ApiError
            {
                Code = "profile.display_name_too_long",
                MessageEn = "Display name must be at most 100 characters.",
            });

        var before = await _users.GetCurrentAsync(User, ct);
        if (before is null)
            return NotFound(new ApiError { Code = "profile.not_found", MessageEn = "Profile not found." });

        var oldName = before.DisplayName;
        var ok = await _users.UpdateDisplayNameAsync(User, req.DisplayName, ct);
        if (!ok)
            return NotFound(new ApiError { Code = "profile.not_found", MessageEn = "Profile not found." });

        await _audit.EmitAsync(
            action: AuditAction.UserSelfDisplayChange,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "User",
            targetId: before.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: JsonSerializer.Serialize(new
            {
                field = "DisplayName",
                old = oldName,
                @new = req.DisplayName,
            }));

        var updated = await _users.GetCurrentAsync(User, ct);
        return Ok(new SettingsProfileDto
        {
            Id = updated!.Id,
            Username = updated.Username,
            Role = updated.Role,
            DisplayName = updated.DisplayName,
            Department = updated.Department,
            MustChangePassword = updated.MustChangePassword,
            CreatedAt = updated.CreatedAt,
            UpdatedAt = updated.UpdatedAt,
        });
    }

    // ── My Password ─────────────────────────────────────────────────

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.CurrentPassword)
            || string.IsNullOrWhiteSpace(req.NewPassword))
        {
            return UnprocessableEntity(new ApiError
            {
                Code = "auth.missing_fields",
                MessageEn = "Both current and new password are required.",
            });
        }

        var outcome = await _users.ChangePasswordAsync(User, req.CurrentPassword, req.NewPassword, ct);
        switch (outcome)
        {
            case PasswordChangeResult.Success:
                await _audit.EmitAsync(
                    action: AuditAction.UserSelfPasswordChange,
                    actor: ActorName(),
                    actorRole: ActorRole(),
                    targetType: "User",
                    targetId: ActorIdOrEmpty(),
                    detail: JsonSerializer.Serialize(new { self = true }));
                return Ok(new ChangePasswordResponse { Success = true });

            case PasswordChangeResult.WrongCurrent:
                return UnprocessableEntity(new ApiError
                {
                    Code = "auth.wrong_current",
                    MessageEn = "Current password is incorrect.",
                });

            case PasswordChangeResult.NewTooShort:
                return UnprocessableEntity(new ApiError
                {
                    Code = "auth.new_too_short",
                    MessageEn = "New password must be at least 4 characters.",
                });

            case PasswordChangeResult.UserNotFound:
            default:
                return NotFound(new ApiError
                {
                    Code = "profile.not_found",
                    MessageEn = "Profile not found.",
                });
        }
    }

    // ── helpers ─────────────────────────────────────────────────────

    private string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    private string ActorRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";
    private string ActorIdOrEmpty() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
}
