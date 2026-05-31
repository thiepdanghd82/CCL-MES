namespace CCL.MES.Application.Audit;

/// <summary>
/// Phase 6 Bước 5 — abstract emit-an-audit-row operation. Application
/// services depend on this interface; the Web layer supplies the
/// implementation (<c>CCL.MES.Web.Services.AuditService</c>) so the IP
/// + HttpContext stay out of the Application class lib.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Append one audit row. Caller MUST sanitize <paramref name="detail"/>
    /// — never include password / hash / cookie / token bytes.
    /// </summary>
    /// <param name="action">Code from <c>CCL.MES.Domain.Audit.AuditAction</c>.</param>
    /// <param name="actor">Username; "anonymous" for pre-auth events.</param>
    /// <param name="actorRole">Role snapshot at emit time.</param>
    /// <param name="targetType">Logical target type (User, WorkOrder, …) or null.</param>
    /// <param name="targetId">Target id (long.ToString, filename, …) or null.</param>
    /// <param name="detail">JSON string with sanitized action-specific fields or null.</param>
    /// <param name="source">"Web" | "Console" | "Hub". Default "Web".</param>
    Task EmitAsync(
        string action,
        string actor,
        string actorRole,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
        string source = "Web");
}
