namespace CCL.MES.Shared.Specs;

/// <summary>
/// P10.5c-2 — Wire shapes for the Spec xlsx import flow. The full
/// <c>ParsedSpecDto</c> from the legacy
/// <c>CCL.MES.Application.SpecImport</c> namespace stays server-side; we
/// only project the fields the modal preview needs + an opaque
/// <see cref="SpecImportPreviewResponse.ParsedJson"/> envelope that the
/// client echoes back on save so the server can resume from the
/// already-parsed DTO without re-uploading the xlsx.
///
/// The opaque payload keeps two important properties:
///   - The client never has to know the full silk/flexo row shape — only
///     the showcard summary fields.
///   - Preview + Save are stateless on the server (no temp-file caching);
///     the parsed state rides the round-trip in the response body.
///
/// Because both endpoints are gated behind <c>NpiSpecWrite</c>
/// (Admin/Engineer) the only actors who can fabricate an opaque payload
/// already have write rights, so the trust window is the same as a hand-
/// written Create call. The deserialized DTO is re-validated through
/// <c>SpecImportService.SaveAsync</c> (Customer/PartNo required, dup
/// re-check) before any DB write.
/// </summary>
public sealed record SpecImportPreviewResponse
{
    public string FileName { get; init; } = "";
    public long FileSizeBytes { get; init; }

    /// <summary>silkscreen / flexo / letterpress / indigo / diecut.
    /// Echoed back to the save endpoint for audit + error tracing.</summary>
    public string PlannerCategory { get; init; } = "silkscreen";

    public bool ParseOk { get; init; }
    public string? ParseError { get; init; }
    public List<string> Warnings { get; init; } = new();

    /// <summary>Subset of <c>ParsedSpecDto</c> the modal renders via the
    /// showcard preview. Null when ParseOk = false.</summary>
    public SpecImportParsedSummary? Summary { get; init; }

    /// <summary>True when a non-trashed ProductRevision already has the
    /// same RefNo — guards the modal's dup-handling 3-option UX.</summary>
    public bool DuplicateRefNo { get; init; }

    /// <summary>Existing matching ProductRevision id; null when no dup.</summary>
    public long? DuplicateRevisionId { get; init; }

    /// <summary>Existing matching SpecCode (parent product code) for UI display.</summary>
    public string? DuplicateSpecCode { get; init; }

    /// <summary>Status of the existing matching revision — drives the
    /// dup-handling decision: Draft → reject; Approved/Released →
    /// offer Upgrade-Rev / Save-as-Copy / Cancel.</summary>
    public string? DuplicateStatus { get; init; }

    /// <summary>Opaque echo of the server-parsed DTO. Client treats as
    /// a black box — passes verbatim to <see cref="SpecImportSaveRequest.ParsedJson"/>
    /// on save. Server JSON-deserializes back to ParsedSpecDto.</summary>
    public string? ParsedJson { get; init; }
}

/// <summary>
/// Showcard-shape projection of <c>ParsedSpecDto</c> — only the fields
/// the modal renders via <c>SpecShowcardCompact</c>.
/// </summary>
public sealed record SpecImportParsedSummary
{
    public string Category { get; init; } = "silkscreen";
    public string? RefNo { get; init; }
    public string? Customer { get; init; }
    public string? PartNo { get; init; }
    public string? PartName { get; init; }
    public string? MaterialType { get; init; }
    public double? ProductSizeW { get; init; }
    public double? ProductSizeH { get; init; }
    public int? NumColors { get; init; }
    public int? Cavity { get; init; }
    public double? PitchMm { get; init; }
    public string? InspectionLevel { get; init; }
    public string? RemarksLeft { get; init; }
    public string? RemarksRight { get; init; }
    public int? PrintRowsCount { get; init; }
    public int? FlexoCuttingRowsCount { get; init; }
    public int? FlexoInkRowsCount { get; init; }
    public int? FlexoPrintRowsCount { get; init; }
}

/// <summary>
/// Save mode for the dup-handling decision flow. Matches the SpecHub UI
/// 3-option pattern shipped in spechub-prototype.html.
/// </summary>
public static class SpecImportSaveMode
{
    /// <summary>No dup detected OR operator explicitly bypassed (rare).
    /// Server reject if dup found at save time.</summary>
    public const string SaveNew = "SaveNew";

    /// <summary>Dup is Approved/Released. Soft-trash existing + create
    /// new draft pointing to the trashed parent (lineage preserved).</summary>
    public const string UpgradeRev = "UpgradeRev";

    /// <summary>Dup is Approved/Released. Create as a separate spec with
    /// operator-supplied <see cref="SpecImportSaveRequest.SpecCodeOverride"/>
    /// and RefNo cleared (no dup at save time).</summary>
    public const string SaveAsCopy = "SaveAsCopy";
}

/// <summary>
/// Save request — echoes the opaque parsed payload from preview + the
/// dup-handling decision. Server re-validates everything before writing.
/// </summary>
public sealed record SpecImportSaveRequest
{
    public string ParsedJson { get; init; } = "";
    public string FileName { get; init; } = "";
    public string PlannerCategory { get; init; } = "silkscreen";

    /// <summary>One of <see cref="SpecImportSaveMode"/> values.</summary>
    public string Mode { get; init; } = SpecImportSaveMode.SaveNew;

    /// <summary>Required when <see cref="Mode"/> = SaveAsCopy. Replaces
    /// the parsed PartNo so the new spec doesn't collide on
    /// (ProductId, RevisionCode) UNIQUE.</summary>
    public string? SpecCodeOverride { get; init; }
}

/// <summary>
/// Save response after successful DB write.
/// </summary>
public sealed record SpecImportSaveResponse
{
    public long ProductRevisionId { get; init; }
    public long ProductId { get; init; }
    public string SpecCode { get; init; } = "";
    public string? RefNo { get; init; }
    public int RowsParsed { get; init; }
    public bool CreatedNewProduct { get; init; }
    public List<string> Warnings { get; init; } = new();

    /// <summary>The save mode actually executed (echoes the request) so
    /// the audit trail + client UI stay aligned.</summary>
    public string Mode { get; init; } = SpecImportSaveMode.SaveNew;
}
