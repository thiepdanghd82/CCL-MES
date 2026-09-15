using CCL.MES.Domain.Auth;
using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Shared;
using CCL.MES.Shared.Accounts;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.6c — Admin Account Control endpoints.
///
/// Surfaces (all <c>AdminOnly</c>):
///   GET   /api/v2/admin/users               Paged JSON list.
///   POST  /api/v2/admin/users               Create new user.
///   PATCH /api/v2/admin/users/{id}          Update displayName / role /
///                                            department / IsActive.
///   POST  /api/v2/admin/users/{id}/reset-password
///                                            Admin force-reset; sets
///                                            MustChangePassword=true.
///
/// Defence in depth:
///   - FallbackPolicy=Authenticate → anon → 401 (covered by
///     RouteDiscoveryCanaryTests rows).
///   - AdminOnly policy on the controller → non-admin auth → 403
///     (covered by AccountControlControllerTests.Engineer_auth_gets_403...
///     Theory through real HTTP via WebApplicationFactory).
///
/// Lockout guards (see <see cref="AccountControlService"/> for details):
///   - Cannot disable / demote the LAST active admin.
///   - Cannot modify SELF role / IsActive on this surface.
///   - Reset-password is admin-handed; never applies to self
///     (self-change goes through /settings/password with current-pwd proof).
///   - Disable revokes refresh tokens immediately; access token lives
///     ≤15 min natural TTL.
///
/// Audit emit (reuses Phase 6 constants):
///   USER_CREATE         detail: { username, role, dept }
///   USER_DISPLAY_CHANGE detail: { username, before, after }
///   USER_ROLE_CHANGE    detail: { username, before, after }
///   USER_SET_ACTIVE     detail: { username, is_active, refresh_revoked }
///   USER_RESET_PASSWORD detail: { username, must_change: true }
///   (Password reset detail INTENTIONALLY does NOT carry the new pwd;
///   IAuditWriter sanitize whitelist convention from Phase 6 Bước 5.)
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/admin/users")]
[Authorize(Policy = "AdminOnly")]
public sealed class AccountControlController : ControllerBase
{
    private readonly AccountControlService _svc;
    private readonly IAuditWriter _audit;
    private readonly Auth.ElectronicSignatureVerifier _signature;

    public AccountControlController(AccountControlService svc, IAuditWriter audit,
        Auth.ElectronicSignatureVerifier signature)
    {
        _svc = svc;
        _audit = audit;
            _signature = signature;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AccountControlService.DefaultPageSize,
        CancellationToken ct = default)
    {
        var result = await _svc.ListAsync(search, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var account = await _svc.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new ApiError { Code = "accounts.not_found", MessageEn = "User not found." });
        return Ok(account);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        var result = await _svc.CreateAsync(req, User, ct);
        if (result.Outcome != AccountResult.Success)
            return MapError(result.Outcome);

        var dto = result.Account!;
        await _audit.EmitAsync(
            action: AuditAction.UserCreate,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "User",
            targetId: dto.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: JsonSerializer.Serialize(new
            {
                username = dto.Username,
                role = dto.Role,
                department = dto.Department ?? "",
            }));
        return Created($"/{ApiVersion.Prefix}/admin/users/{dto.Id}", dto);
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAccountRequest req, CancellationToken ct)
    {
        // Capture before-state for audit emit BEFORE we mutate.
        var before = await _svc.GetByIdAsync(id, ct);
        if (before is null)
            return NotFound(new ApiError { Code = "accounts.not_found", MessageEn = "User not found." });

        var result = await _svc.UpdateAsync(id, req, User, ct);
        if (result.Outcome != AccountResult.Success)
            return MapError(result.Outcome);

        var after = result.Account!;
        var actorName = ActorName();
        var actorRole = ActorRole();
        var targetIdStr = after.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (before.DisplayName != after.DisplayName)
        {
            await _audit.EmitAsync(
                action: AuditAction.UserDisplayChange,
                actor: actorName, actorRole: actorRole,
                targetType: "User", targetId: targetIdStr,
                detail: JsonSerializer.Serialize(new
                {
                    username = after.Username,
                    before = before.DisplayName,
                    after = after.DisplayName,
                }));
        }
        if (!string.Equals(before.Role, after.Role, StringComparison.Ordinal))
        {
            await _audit.EmitAsync(
                action: AuditAction.UserRoleChange,
                actor: actorName, actorRole: actorRole,
                targetType: "User", targetId: targetIdStr,
                detail: JsonSerializer.Serialize(new
                {
                    username = after.Username,
                    before = before.Role,
                    after = after.Role,
                }));
        }
        if (before.IsActive != after.IsActive)
        {
            await _audit.EmitAsync(
                action: AuditAction.UserSetActive,
                actor: actorName, actorRole: actorRole,
                targetType: "User", targetId: targetIdStr,
                detail: JsonSerializer.Serialize(new
                {
                    username = after.Username,
                    is_active = after.IsActive,
                    // When flipping to inactive the service revoked all
                    // refresh tokens for the target; surface as a flag
                    // so an audit reader can spot the revoke event.
                    refresh_revoked = !after.IsActive,
                }));
        }
        // Department change reuses USER_DISPLAY_CHANGE today
        // (no dedicated constant in the legacy enum); future PR can add
        // USER_DEPT_CHANGE if compliance needs the row to be greppable
        // by a distinct code.

        return Ok(after);
    }

    [HttpPost("{id:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var result = await _svc.ResetPasswordAsync(id, req, User, ct);
        if (result.Outcome != AccountResult.Success)
            return MapError(result.Outcome);

        var dto = result.Account!;
        await _audit.EmitAsync(
            action: AuditAction.UserResetPassword,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "User",
            targetId: dto.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: JsonSerializer.Serialize(new
            {
                username = dto.Username,
                must_change = true,
            }));
        return Ok(dto);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await _svc.DeleteAsync(id, User, ct);
        if (result.Outcome != AccountResult.Success)
            return MapError(result.Outcome);

        var dto = result.Account!;
        await _audit.EmitAsync(
            action: AuditAction.UserDelete,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "User",
            targetId: dto.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: JsonSerializer.Serialize(new
            {
                username = dto.Username,
                role = dto.Role,
            }));
        return Ok(dto);
    }

    // ── Error mapping ───────────────────────────────────────────────

    private IActionResult MapError(AccountResult code) => code switch
    {
        AccountResult.NotFound => NotFound(new ApiError
        {
            Code = "accounts.not_found",
            MessageEn = "User not found.",
        }),
        AccountResult.InvalidBody => UnprocessableEntity(new ApiError
        {
            Code = "accounts.invalid_body",
            MessageEn = "Request body is required.",
        }),
        AccountResult.UsernameRequired => UnprocessableEntity(new ApiError
        {
            Code = "accounts.username_required",
            MessageEn = "Username is required.",
        }),
        AccountResult.UsernameInUse => UnprocessableEntity(new ApiError
        {
            Code = "accounts.username_in_use",
            MessageEn = "Username is already in use.",
        }),
        AccountResult.InvalidRole => UnprocessableEntity(new ApiError
        {
            Code = "accounts.invalid_role",
            MessageEn = "Role must be one of Admin / Supervisor / Engineer / QC / Operator.",
        }),
        AccountResult.PasswordTooShort => UnprocessableEntity(new ApiError
        {
            Code = "accounts.password_too_short",
            MessageEn = $"Password must be at least {AccountControlService.MinPasswordLength} characters.",
        }),
        AccountResult.SelfModificationBlocked => UnprocessableEntity(new ApiError
        {
            Code = "accounts.self_action_forbidden",
            MessageEn = "Admins cannot disable, demote, or reset their own account from this surface.",
        }),
        AccountResult.LastAdminProtected => UnprocessableEntity(new ApiError
        {
            Code = "accounts.last_admin",
            MessageEn = "Action refused — would leave the system with zero active admins.",
        }),
        // P10.7a-2.1 — 403 (not 422) because this is an authorization
        // signal (sys account is out of bounds for this surface) not a
        // validation failure. Matches the BackupController "AdminOnly"
        // pattern for off-limits operations: surface declines, not
        // request shape failure.
        AccountResult.SysAccountProtected => StatusCode(403, new ApiError
        {
            Code = "accounts.sys_account_protected",
            MessageEn = "System recovery accounts are audit-only and cannot be modified through this surface.",
        }),
        _ => StatusCode(500, new ApiError
        {
            Code = "accounts.unknown_error",
            MessageEn = "Unknown failure.",
        }),
    };

    private string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    private string ActorRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    // ── Bảng phân quyền (Thiệp chốt 2026-09-15) ─────────────────────

    /// <summary>Đọc bảng phân quyền. Class đã `AdminOnly` nên không ai khác vào được.</summary>
    [HttpGet("permissions")]
    public async Task<IActionResult> PermissionMatrix(CancellationToken ct = default)
        => Ok(await _svc.PermissionMatrixAsync(ActorName(), ActorRole(), ct));

    /// <summary>
    /// Sửa quyền riêng của một người.
    ///
    /// <para><b>Bắt ký lại mật khẩu.</b> Đổi phân quyền là hành vi nhạy cảm nhất
    /// trong hệ — ai làm được việc này thì tự cấp được mọi quyền còn lại. Một
    /// máy bỏ quên phiên admin đang mở KHÔNG được để người đi ngang sửa quyền.
    /// Dùng lại đúng đường ký của IPQC: cùng hãm thử-sai, cùng mã lỗi chung,
    /// mật khẩu không bao giờ vào audit.</para>
    /// </summary>
    [HttpPut("{id:long}/permissions")]
    public async Task<IActionResult> SetPermissions(
        long id, [FromBody] UpdateUserPermissionsRequest? req, CancellationToken ct = default)
    {
        var actor = ActorName();
        var role  = ActorRole();

        if (req is null)
            return UnprocessableEntity(ApiError.Of("accounts.invalid_body", "Thiếu nội dung yêu cầu."));

        // Chữ ký TRƯỚC mọi phép ghi.
        var sig = await _signature.VerifyAsync(
            req.SignerUsername, req.SignerPassword,
            r => string.Equals(r, UserRole.Admin, StringComparison.OrdinalIgnoreCase), ct);

        if (!sig.Ok)
        {
            await _audit.EmitAsync(
                action: AuditAction.UserPermissionSignDenied, actor: actor, actorRole: role,
                targetType: "User", targetId: id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    target_user_id = id,
                    typed_username = sig.TypedUsername,
                    reason = sig.ErrorCode,
                    lock_minutes = sig.LockMinutes,
                    session_actor = actor,
                }));
            return UnprocessableEntity(ApiError.Of(sig.ErrorCode!, sig.Message!));
        }

        var result = await _svc.SetPermissionsAsync(id, req.Permissions, ct);
        if (result.Outcome != AccountResult.Success)
            return MapError(result.Outcome);

        await _audit.EmitAsync(
            action: AuditAction.UserPermissionSet, actor: actor, actorRole: role,
            targetType: "User", targetId: id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                target_user_id = id,
                target_username = result.Account?.Username,
                signer = sig.Username,          // NGƯỜI KÝ, có thể khác phiên
                session_actor = actor,
                changed = req.Permissions,      // cờ nào đổi thành gì
            }));

        return Ok(result.Account);
    }
}
