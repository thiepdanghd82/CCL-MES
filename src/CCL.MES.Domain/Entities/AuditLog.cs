namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phase 6 Bước 5 — append-only audit row. Business action trail: who
/// did what, when, on which target, with optional JSON detail. NEVER
/// store password / hash / cookie / token bytes in <see cref="Detail"/>;
/// the sanitize whitelist in <c>AuditService.EmitAsync</c> is the only
/// path that should write into it.
/// </summary>
public class AuditLog : BaseEntity
{
    /// <summary>UTC timestamp of the action.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Actor username at the time, or "anonymous" for pre-auth events (login fail).</summary>
    public string ActorUsername { get; set; } = "";

    /// <summary>
    /// Actor role snapshot. Captured at emit time so a later role change
    /// does not rewrite history — useful for incident review.
    /// </summary>
    public string ActorRole { get; set; } = "";

    /// <summary>Action code from <see cref="CCL.MES.Domain.Audit.AuditAction"/>.</summary>
    public string Action { get; set; } = "";

    /// <summary>Logical target type (User, WorkOrder, Spec, ...) or null.</summary>
    public string? TargetType { get; set; }

    /// <summary>Target id as string (long.ToString for entities, filename for backups, etc.).</summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// JSON string with action-specific detail (≤ 4 KB). MUST be sanitized
    /// (no password / hash / cookie / token). For login failures we record
    /// the typed username so an admin can spot brute-force attempts, but
    /// only after a generic Wrong-username-or-password error has gone back
    /// to the caller — the audit row does NOT change the response shape.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>Remote IP if available, null otherwise.</summary>
    public string? IpAddress { get; set; }

    /// <summary>"Web" | "Console" | "Hub" — which entrypoint emitted the row.</summary>
    public string Source { get; set; } = "Web";
}
