namespace CCL.MES.Shared.Backup;

/// <summary>
/// P-Backup (Hybrid) — current state of the automated backup scheduler,
/// surfaced to the admin Settings → Backup page. Shared between the API
/// (producer) and the Blazor client (consumer).
/// </summary>
public sealed class BackupScheduleStatusDto
{
    public bool Enabled { get; set; }
    public int Hour { get; set; }                 // 0–23, ICT
    public int RetentionDays { get; set; }
    public int MinKeep { get; set; }
    public bool BlobsEnabled { get; set; }
    public bool WebhookConfigured { get; set; }
    public string TimeZone { get; set; } = "";
    public DateTime? NextRunAtUtc { get; set; }

    // Last cycle summary (null until the first run this process lifetime).
    public DateTime? LastRunAtUtc { get; set; }
    public bool? LastRunOk { get; set; }
    public string? LastSqliteFile { get; set; }
    public string? LastError { get; set; }

    public string PersistedAt { get; set; } = "";
}

/// <summary>
/// Admin edit payload. Any null field is left unchanged. Hour 0–23,
/// RetentionDays 1–3650, MinKeep 1–1000 — validated server-side.
/// </summary>
public sealed class BackupScheduleUpdateRequest
{
    public bool? Enabled { get; set; }
    public int? Hour { get; set; }
    public int? RetentionDays { get; set; }
    public int? MinKeep { get; set; }
}

/// <summary>Outcome of a single backup cycle (run-now or nightly).</summary>
public sealed class BackupRunResultDto
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? SqliteFile { get; set; }
    public double SqliteMb { get; set; }
    public string? BlobFile { get; set; }
    public double BlobMb { get; set; }
    public bool VerifyOk { get; set; }
    public string Integrity { get; set; } = "";
    public int Pruned { get; set; }
    public long DurationMs { get; set; }
}
