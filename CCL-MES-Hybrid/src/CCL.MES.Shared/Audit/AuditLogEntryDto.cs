namespace CCL.MES.Shared.Audit;

/// <summary>
/// P10.6e — projection of <c>CCL.MES.Domain.Entities.AuditLog</c> to a
/// wire shape that's stable across schema migrations. Mirror of the
/// 9-column CSV exporter so admins reading the JSON list see the same
/// columns they'd find in the exported file.
///
/// Sensitive fields stay where the legacy IAuditWriter sanitises them
/// (no password / hash / cookie / token bytes). The wire shape only
/// re-exposes what the entity already carries.
/// </summary>
public sealed record AuditLogEntryDto
{
    /// <summary>Database id — also the natural sort key.</summary>
    public long Id { get; init; }

    /// <summary>UTC timestamp of the action.</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>Actor username at emit time. "anonymous" for pre-auth events.</summary>
    public string ActorUsername { get; init; } = "";

    /// <summary>Actor role snapshot.</summary>
    public string ActorRole { get; init; } = "";

    /// <summary>Action code (see <c>AuditAction</c> constants).</summary>
    public string Action { get; init; } = "";

    public string? TargetType { get; init; }

    public string? TargetId { get; init; }

    /// <summary>Raw action-specific JSON detail. Renderer-side may
    /// pretty-print but MUST NOT mutate.</summary>
    public string? Detail { get; init; }

    public string? IpAddress { get; init; }

    /// <summary>"Web" / "Console" / "Hub" — entrypoint that emitted.</summary>
    public string Source { get; init; } = "";
}
