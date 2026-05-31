namespace CCL.MES.Application.Services.NpiImport;

/// <summary>Kết quả Phase 1 — parse + preview KHÔNG ghi DB.</summary>
public sealed class CsvParseResult<TEntity> where TEntity : class
{
    public required IReadOnlyList<string> HeaderRaw { get; init; }
    public required IReadOnlyList<string> MappedFields { get; init; }
    public required IReadOnlyList<TEntity> Rows { get; init; }
    public required int Parsed { get; init; }                       // total non-header rows in CSV
    public required int Skipped { get; init; }                      // dropped (short_row | missing required)
    public required IReadOnlyDictionary<string, int> SkipReasons { get; init; }
    public required IReadOnlyList<string> MissingRequired { get; init; } // required fields NOT found in header → fail
    public required int ElapsedMs { get; init; }
    /// <summary>True ⇔ ready to call <see cref="NpiImportService.ApplyAsync{T}"/>.</summary>
    public bool CanApply => MissingRequired.Count == 0 && Rows.Count > 0;
}

/// <summary>Kết quả Phase 2 — apply (auto-backup + DELETE+INSERT atomic + audit).</summary>
public sealed class CsvImportResult
{
    public required string Table { get; init; }
    public required int OldCount { get; init; }
    public required int NewCount { get; init; }
    public required int Skipped { get; init; }
    public required string BackupFile { get; init; }
    public required string BackupSha256Short { get; init; }
    public required int ElapsedMs { get; init; }
}

/// <summary>Soft fail trên import path — caller render lên UI.</summary>
public sealed class CsvImportException : Exception
{
    public string ErrorKey { get; }
    public CsvImportException(string errorKey, string message, Exception? inner = null) : base(message, inner)
    {
        ErrorKey = errorKey;
    }
}
