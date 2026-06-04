namespace CCL.MES.Shared.Settings;

/// <summary>
/// P10.6d — server-side build / runtime / DB-size snapshot for the
/// Settings/About page. Anonymous-friendly: nothing here exposes
/// credentials or PII, so the controller could in principle drop
/// the [Authorize] gate. We keep auth on to match every other
/// Settings endpoint — operators authenticate before they see About,
/// which matches SpecHub §11.
///
/// Counts come from cheap COUNT(*) queries — the DB sizes use
/// <see cref="FileInfo.Length"/> on the SQLite file when the
/// provider is Sqlite. SqlServer provider returns null sizes.
/// </summary>
public sealed record AboutDto
{
    /// <summary>Informational version pulled from
    /// <see cref="System.Reflection.AssemblyName.Version"/> of the
    /// API entry assembly (e.g. "1.0.0.0").</summary>
    public string ServerVersion { get; init; } = "";

    /// <summary>Optional informational SHA / build label from
    /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>;
    /// empty when the build pipeline didn't stamp one.</summary>
    public string InformationalVersion { get; init; } = "";

    /// <summary>ASP.NET Core environment name — Development /
    /// Production / Test. Operators see this so they know which
    /// install they're staring at.</summary>
    public string EnvironmentName { get; init; } = "";

    /// <summary>Server clock at response time (UTC). Lets the client
    /// flag time skew against the device clock.</summary>
    public DateTime ServerTimeUtc { get; init; }

    /// <summary>Best-effort host name (from <c>Environment.MachineName</c>).
    /// Useful when the operator is bouncing between staging / prod.</summary>
    public string MachineName { get; init; } = "";

    // ── Inventory counters (cheap COUNT(*) queries) ─────────────────

    /// <summary>Number of users in the active store.</summary>
    public int UserCount { get; init; }

    /// <summary>Number of customers.</summary>
    public int CustomerCount { get; init; }

    /// <summary>Number of products.</summary>
    public int ProductCount { get; init; }

    /// <summary>Total revisions across all products (Draft + Approved
    /// + Released + Superseded + Trashed). The Spec landing already
    /// shows "active" — this counter is the raw row total for ops
    /// review.</summary>
    public int SpecRevisionCount { get; init; }

    /// <summary>Total drawing version rows (file + DB).</summary>
    public int DrawingVersionCount { get; init; }

    /// <summary>Total audit-log row count. Useful so admins know how
    /// far back the forensic trail extends without opening the
    /// Audit Log page in P10.6e.</summary>
    public long AuditEntryCount { get; init; }

    // ── Storage on disk (Sqlite only) ───────────────────────────────

    /// <summary>Absolute size of the SQLite DB file in bytes; null
    /// when the provider is SqlServer or the file path doesn't
    /// resolve.</summary>
    public long? DbFileBytes { get; init; }

    /// <summary>Drawing blob store on-disk size — the <c>data/blobs/</c>
    /// tree the <c>FilesystemBlobStore</c> writes to. Null when the
    /// dir doesn't exist yet.</summary>
    public long? BlobStoreBytes { get; init; }

    /// <summary>Absolute path of the resolved data dir (DB + blobs).
    /// Operators forward this to ops when triaging a sync issue.</summary>
    public string DataDir { get; init; } = "";
}
