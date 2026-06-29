namespace CCL.MES.Shared.CheckLibrary;

/// <summary>QC Library item — full row for the admin grid (read + inline edit).
/// <see cref="Id"/> + <see cref="RowVersion"/> drive optimistic-concurrency PUT.</summary>
public sealed record CheckLibraryItemDto
{
    public long Id { get; init; }
    public string ItemId { get; init; } = "";
    public string ProcessLine { get; init; } = "";
    public string? ProductCode { get; init; }
    public string QcStage { get; init; } = "IPQC";
    public string GroupLabel { get; init; } = "";
    public string Code { get; init; } = "";
    public string ItemVi { get; init; } = "";
    public string ItemEn { get; init; } = "";
    public string AcceptanceVi { get; init; } = "";
    public string AcceptanceEn { get; init; } = "";
    public string? Method { get; init; }
    public string? Severity { get; init; }
    public string? DefectCode { get; init; }
    public bool Active { get; init; }
    public int Sort { get; init; }
    /// <summary>Optimistic-concurrency token — echo back in the PUT If-Match.</summary>
    public string RowVersion { get; init; } = "";
}

/// <summary>QC Library add (POST) / edit (PUT) body. Mutable (get/set) so the
/// admin page can two-way <c>@bind</c> the inline edit form. PUT carries the
/// prior <see cref="RowVersion"/> as the If-Match token for 409-on-stale.</summary>
public sealed record UpsertCheckLibraryItemRequest
{
    public string ItemId { get; set; } = "";
    public string ProcessLine { get; set; } = "";
    public string? ProductCode { get; set; }
    public string QcStage { get; set; } = "IPQC";
    public string GroupLabel { get; set; } = "";
    public string Code { get; set; } = "";
    public string ItemVi { get; set; } = "";
    public string ItemEn { get; set; } = "";
    public string AcceptanceVi { get; set; } = "";
    public string AcceptanceEn { get; set; } = "";
    public string? Method { get; set; }
    public string? Severity { get; set; }
    public string? Aql { get; set; }
    public string? Sampling { get; set; }
    public string? CheckType { get; set; }
    public string? DefectCode { get; set; }
    public string? ParetoPct { get; set; }
    public string? ShortForm { get; set; }
    public string? IsoRef { get; set; }
    public string? AppliesWhen { get; set; }
    public string? Note { get; set; }
    public int Sort { get; set; }
    public bool Active { get; set; } = true; // new rows default active
    /// <summary>Concurrency token from the row being edited (PUT only).</summary>
    public string? RowVersion { get; set; }
}

/// <summary>Result of an .xlsx import — surfaced to the operator as a summary.</summary>
public sealed record CheckLibraryImportResultDto
{
    public int Parsed { get; init; }
    public int Inserted { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = System.Array.Empty<string>();
}

/// <summary>Phương án C — Bước 6. Tổng quan 1 process line (đếm hạng mục).</summary>
public sealed record CheckLibraryLineDto
{
    public string ProcessLine { get; init; } = "";
    public string QcStage { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>Phương án C — Bước 6. 1 luật map process→QC line (data-driven, quyết định #5).</summary>
public sealed record ProcessLineMapDto
{
    public string MatchType { get; init; } = "";
    public string MatchValue { get; init; } = "";
    public string QcLine { get; init; } = "";
    public int Sort { get; init; }
    public bool Active { get; init; }
    public string? Note { get; init; }
}
