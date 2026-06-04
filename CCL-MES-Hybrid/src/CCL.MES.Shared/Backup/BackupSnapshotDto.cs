namespace CCL.MES.Shared.Backup;

/// <summary>
/// P10.6h — one row in the admin Settings → Backup snapshot list.
/// Mirrors the legacy <c>CCL.MES.Web.Services.BackupFile</c> shape but
/// adds a Sha256 column so admins can match a downloaded snapshot
/// back to its server-side audit row.
///
/// Filename convention: <c>ccl_mes.db.bak.snapshot-yyyyMMdd-HHmmss</c>
/// (legacy convention, preserved so the legacy console restore tool
/// keeps working on Hybrid-era snapshots if needed). Pre-restore
/// snapshots taken automatically before a Restore use prefix
/// <c>pre-restore-</c> so admins can spot them in the list.
/// </summary>
public sealed record BackupSnapshotDto
{
    /// <summary>Basename only — never the absolute path. The server
    /// resolves the absolute path internally on download / restore
    /// calls; exposing absolute paths would leak the data dir.</summary>
    public string FileName { get; init; } = "";

    /// <summary>File size in bytes at <c>stat()</c> time.</summary>
    public long SizeBytes { get; init; }

    /// <summary>UTC mtime of the file.</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Lower-case hex SHA-256 of the file contents. The list
    /// endpoint hashes the file at list-time — not cached. For very
    /// large backup dirs (say 100+ snapshots) this is the
    /// most-expensive operation in the list response; ops can prune
    /// older snapshots if it gets noticeable.</summary>
    public string Sha256 { get; init; } = "";

    /// <summary>True when the file name carries the <c>pre-restore-</c>
    /// prefix the server stamps automatically before a Restore. UI
    /// renders these with a different badge so an admin knows they
    /// are auto-generated rollback points, not operator-initiated
    /// snapshots.</summary>
    public bool IsPreRestore { get; init; }
}
