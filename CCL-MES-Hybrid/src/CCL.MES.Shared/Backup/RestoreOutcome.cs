namespace CCL.MES.Shared.Backup;

/// <summary>
/// P10.6h — discriminator for restore outcomes. The controller maps
/// each non-success outcome to an <see cref="Envelopes.ApiError"/>
/// with a stable <c>backup.*</c> error code so the VN mapper can
/// translate without bespoke per-endpoint handling.
/// </summary>
public enum RestoreOutcome
{
    /// <summary>Restore completed; pre-restore snapshot saved.</summary>
    Success = 0,

    /// <summary>Provider is SqlServer — the API never overwrites a
    /// SQL Server DB. Operator gets a "use SSMS" hint.</summary>
    SqlServerUnsupported = 1,

    /// <summary>Upload byte stream was empty.</summary>
    EmptyUpload = 2,

    /// <summary>File doesn't start with the 16-byte SQLite magic
    /// header ("SQLite format 3\0"). Catches the "operator picked a
    /// random file" + "file truncated mid-upload" cases up front.</summary>
    InvalidHeader = 3,

    /// <summary>SQLite opened the file but the schema doesn't carry
    /// the tables we require (Users, Customers, Products at minimum).
    /// Protects against pasting a totally unrelated SQLite DB.</summary>
    SchemaMismatch = 4,

    /// <summary>Anything else — disk full, permission denied,
    /// SQLite internal error. The controller returns the message in
    /// the audit detail; the UI gets a generic "không thể khôi phục"
    /// banner so we don't leak FS paths.</summary>
    Error = 99,
}

/// <summary>
/// Response shape for the <c>POST /api/v2/backup/restore</c> call.
/// Carries the basename of the pre-restore snapshot the server took
/// before applying so the admin can roll back in one click if the
/// new DB turns out to be wrong.
/// </summary>
public sealed record RestoreResultDto
{
    public RestoreOutcome Outcome { get; init; }
    public string? PreRestoreSnapshot { get; init; }
    public long RestoredBytes { get; init; }
}
