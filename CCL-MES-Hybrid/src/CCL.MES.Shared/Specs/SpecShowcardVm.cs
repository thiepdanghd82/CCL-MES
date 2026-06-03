using System.Globalization;

namespace CCL.MES.Shared.Specs;

/// <summary>
/// P10.5b — flatten helper extracted from
/// <c>CCL.MES.Web/Pages/Npi/SpecShowcard.razor</c> (1,024 LOC legacy)
/// per Henry's spec: ship to <c>CCL.MES.Shared</c> so both web (Phase 8)
/// and MAUI (P10.5b+) consume the same view-model. The legacy Razor
/// computes Status/Planner/ProductSize labels inline; this helper makes
/// them deterministic + unit-testable from xUnit.
///
/// Compact mode (P10.5b) returns the identity-only subset that the
/// list double-click peek + detail header consume. Full mode (P10.5c)
/// will extend with material / print / lineage flatten on top of the
/// same VM class.
///
/// **All output strings are operator-facing Vietnamese (Q12 — i18n
/// inline VN + marker comments).** English fallbacks live on the enum
/// switch defaults so a future resx port has one source of truth.
/// </summary>
public sealed record SpecShowcardVm
{
    public long Id { get; init; }
    public string SpecCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string RevisionCode { get; init; } = "A";

    /// <summary>Raw 5-state enum — UI may render the pill directly via
    /// <see cref="StatusLabel"/> + <see cref="StatusCssClass"/>.</summary>
    public SpecRevisionStatus Status { get; init; }
    public string StatusLabel { get; init; } = "";
    public string StatusCssClass { get; init; } = "";

    /// <summary>Planner slug normalised to one of
    /// SILK / FLEXO / LETTER / INDIGO / DIECUT / UNKNOWN.</summary>
    public string PlannerCode { get; init; } = "UNKNOWN";
    public string PlannerLabel { get; init; } = "";
    public string PlannerCssClass { get; init; } = "";

    public string? RefNo { get; init; }
    public string? InspectionLevel { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? CustomerName { get; init; }
    public string ProcessCode { get; init; } = "";
    public bool IsSilkscreen { get; init; }
    public bool IsFlexo { get; init; }

    public string ProductSizeDisplay { get; init; } = "—";
    public string? RemarksText { get; init; }
    public string? RemarksCutText { get; init; }

    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? ReleasedBy { get; init; }
    public DateTime? ReleasedAt { get; init; }

    /// <summary>Q13-equivalent compliance chips (3 items). Stable
    /// order: HSF → Spec level → RoHS.</summary>
    public IReadOnlyList<string> ComplianceChips { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Flatten a <see cref="SpecDetailItem"/> into the compact VM the
    /// MAUI list peek + detail header consume. Pure function — no DB,
    /// no HTTP, no DateTime.Now reads — so xUnit can verify the entire
    /// label + class derivation table.
    /// </summary>
    public static SpecShowcardVm FromDetail(SpecDetailItem detail)
    {
        var planner = NormalisePlannerCode(detail.Planner, detail.ProcessCode);
        return new SpecShowcardVm
        {
            Id = detail.Id,
            SpecCode = detail.SpecCode,
            Title = detail.Title,
            RevisionCode = string.IsNullOrWhiteSpace(detail.RevisionCode) ? "A" : detail.RevisionCode,
            Status = detail.Status,
            StatusLabel = StatusLabelVi(detail.Status),
            StatusCssClass = StatusCssClassFor(detail.Status),
            PlannerCode = planner,
            PlannerLabel = PlannerLabelVi(planner),
            PlannerCssClass = PlannerCssClassFor(planner),
            RefNo = detail.RefNo,
            InspectionLevel = detail.InspectionLevel,
            ProductCode = detail.ProductCode,
            ProductName = detail.ProductName,
            CustomerName = detail.CustomerName,
            ProcessCode = detail.ProcessCode,
            IsSilkscreen = detail.IsSilkscreen,
            IsFlexo = detail.IsFlexo,
            ProductSizeDisplay = FormatProductSize(detail.ProductSizeWmm, detail.ProductSizeHmm),
            RemarksText = detail.RemarksText,
            RemarksCutText = detail.RemarksCutText,
            CreatedAt = detail.CreatedAt,
            CreatedBy = detail.CreatedBy,
            UpdatedAt = detail.UpdatedAt,
            UpdatedBy = detail.UpdatedBy,
            ApprovedBy = detail.ApprovedBy,
            ApprovedAt = detail.ApprovedAt,
            ReleasedBy = detail.ReleasedBy,
            ReleasedAt = detail.ReleasedAt,
            ComplianceChips = BuildComplianceChips(detail.InspectionLevel),
        };
    }

    /// <summary>
    /// Flatten a <see cref="SpecListItem"/> into the compact VM. List
    /// rows lack the full detail graph (no Process flags, no remarks),
    /// so the returned VM omits those fields — caller renders only
    /// identity + status + planner.
    /// </summary>
    public static SpecShowcardVm FromListItem(SpecListItem item)
    {
        var planner = NormalisePlannerCode(item.Planner, item.ProcessCode);
        return new SpecShowcardVm
        {
            Id = item.Id,
            SpecCode = item.SpecCode,
            Title = item.Title,
            RevisionCode = string.IsNullOrWhiteSpace(item.RevisionCode) ? "A" : item.RevisionCode,
            Status = item.Status,
            StatusLabel = StatusLabelVi(item.Status),
            StatusCssClass = StatusCssClassFor(item.Status),
            PlannerCode = planner,
            PlannerLabel = PlannerLabelVi(planner),
            PlannerCssClass = PlannerCssClassFor(planner),
            RefNo = item.RefNo,
            InspectionLevel = item.InspectionLevel,
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            CustomerName = item.CustomerName,
            ProcessCode = item.ProcessCode ?? "",
            CreatedAt = item.LastUpdatedAt ?? default,
            UpdatedBy = item.LastUpdatedBy,
            ComplianceChips = BuildComplianceChips(item.InspectionLevel),
        };
    }

    /// <summary>
    /// P10.5c-2 — Flatten a Spec xlsx import preview summary into a
    /// compact VM so <c>SpecShowcardCompact</c> can render the parsed
    /// payload before the operator commits to a save. Status is pinned
    /// to <see cref="SpecRevisionStatus.Draft"/> because the legacy
    /// importer always creates Draft revisions.
    /// </summary>
    public static SpecShowcardVm FromImportSummary(SpecImportParsedSummary s, string fileName)
    {
        var processCode = MapCategoryToProcessCode(s.Category);
        var planner = NormalisePlannerCode(null, processCode);
        var isFlexo = string.Equals(s.Category, "flexo", StringComparison.OrdinalIgnoreCase);
        var isSilk = string.Equals(s.Category, "silkscreen", StringComparison.OrdinalIgnoreCase);
        return new SpecShowcardVm
        {
            Id = 0,
            SpecCode = s.PartNo ?? "(chưa có)",
            Title = !string.IsNullOrWhiteSpace(s.PartName) ? s.PartName! : (s.PartNo ?? fileName),
            RevisionCode = "A",
            Status = SpecRevisionStatus.Draft,
            StatusLabel = StatusLabelVi(SpecRevisionStatus.Draft),
            StatusCssClass = StatusCssClassFor(SpecRevisionStatus.Draft),
            PlannerCode = planner,
            PlannerLabel = PlannerLabelVi(planner),
            PlannerCssClass = PlannerCssClassFor(planner),
            RefNo = s.RefNo,
            InspectionLevel = s.InspectionLevel,
            ProductCode = s.PartNo ?? "",
            ProductName = s.PartName ?? "",
            CustomerName = s.Customer,
            ProcessCode = processCode,
            IsSilkscreen = isSilk,
            IsFlexo = isFlexo,
            ProductSizeDisplay = FormatProductSize(s.ProductSizeW, s.ProductSizeH),
            RemarksText = s.RemarksLeft,
            RemarksCutText = s.RemarksRight,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "(xem trước)",
            ComplianceChips = BuildComplianceChips(s.InspectionLevel),
        };
    }

    private static string MapCategoryToProcessCode(string category) => (category ?? "").ToLowerInvariant() switch
    {
        "silkscreen"  => "SILKSCREEN",
        "flexo"       => "FLEXO",
        "letterpress" => "LETTERPRESS",
        "indigo"      => "INDIGO",
        "diecut"      => "FLATBED_CUT",
        _             => "SILKSCREEN",
    };

    // ── status helpers ──────────────────────────────────────────────

    /// <summary>VN label cho 5-state status (Q2 — keep 5-state).</summary>
    public static string StatusLabelVi(SpecRevisionStatus status) => status switch
    {
        SpecRevisionStatus.Draft       => "Bản nháp",       // i18n: spec.status.draft
        SpecRevisionStatus.InReview    => "Đang xét",       // i18n: spec.status.in_review
        SpecRevisionStatus.Approved    => "Đã duyệt",       // i18n: spec.status.approved
        SpecRevisionStatus.Released    => "Đã phát hành",   // i18n: spec.status.released
        SpecRevisionStatus.Superseded  => "Đã thay thế",    // i18n: spec.status.superseded
        _                              => status.ToString(),
    };

    /// <summary>CSS class slug for the 5-state status pill —
    /// <c>spec-status-{slug}</c> with stable kebab slugs.</summary>
    public static string StatusCssClassFor(SpecRevisionStatus status) => status switch
    {
        SpecRevisionStatus.Draft       => "spec-status-draft",
        SpecRevisionStatus.InReview    => "spec-status-in-review",
        SpecRevisionStatus.Approved    => "spec-status-approved",
        SpecRevisionStatus.Released    => "spec-status-released",
        SpecRevisionStatus.Superseded  => "spec-status-superseded",
        _                              => "spec-status-unknown",
    };

    // ── planner helpers ─────────────────────────────────────────────

    /// <summary>
    /// Map a server-reported Planner string (or fall back to ProcessCode
    /// derive) onto the canonical SpecHub palette codes:
    /// SILK / FLEXO / LETTER / INDIGO / DIECUT / UNKNOWN.
    /// Mirrors the legacy Web's planner-derive logic.
    /// </summary>
    public static string NormalisePlannerCode(string? planner, string? processCode)
    {
        var raw = (planner ?? "").Trim().ToUpperInvariant();
        if (raw is "SILK" or "FLEXO" or "LETTER" or "INDIGO" or "DIECUT" or "UNKNOWN")
            return raw;

        var pc = (processCode ?? "").Trim().ToUpperInvariant();
        if (pc.Contains("SILK"))           return "SILK";
        if (pc.Contains("FLEXO"))          return "FLEXO";
        if (pc.Contains("LETTER"))         return "LETTER";
        if (pc.Contains("INDIGO"))         return "INDIGO";
        if (pc.Contains("DIECUT") || pc.Contains("DIE-CUT") || pc.Contains("DIE_CUT"))
            return "DIECUT";
        return "UNKNOWN";
    }

    public static string PlannerLabelVi(string plannerCode) => plannerCode switch
    {
        "SILK"    => "Lụa",            // i18n: spec.planner.silk
        "FLEXO"   => "Flexo",          // i18n: spec.planner.flexo
        "LETTER"  => "Letterpress",    // i18n: spec.planner.letter
        "INDIGO"  => "Indigo",         // i18n: spec.planner.indigo
        "DIECUT"  => "Bế",             // i18n: spec.planner.diecut
        _         => "Chưa rõ",        // i18n: spec.planner.unknown
    };

    public static string PlannerCssClassFor(string plannerCode) => plannerCode switch
    {
        "SILK"    => "spec-planner-silk",
        "FLEXO"   => "spec-planner-flexo",
        "LETTER"  => "spec-planner-letter",
        "INDIGO"  => "spec-planner-indigo",
        "DIECUT"  => "spec-planner-diecut",
        _         => "spec-planner-unknown",
    };

    // ── format helpers ──────────────────────────────────────────────

    /// <summary>"W × H mm" or "—" when both nil. Uses invariant decimal
    /// formatting for stable test comparison.</summary>
    public static string FormatProductSize(double? wMm, double? hMm)
    {
        if (wMm is null && hMm is null) return "—";
        var w = wMm?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
        var h = hMm?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
        return $"{w} × {h} mm";
    }

    /// <summary>3-chip compliance row (HSF / Spec level / RoHS).
    /// Inspection level fallback to "A" when blank — mirrors legacy
    /// SpecDetailDto.ComplianceChips.</summary>
    public static IReadOnlyList<string> BuildComplianceChips(string? inspectionLevel) => new[]
    {
        "HSF strict control",
        string.IsNullOrWhiteSpace(inspectionLevel) ? "Spec A" : $"Spec {inspectionLevel}",
        "RoHS Compliance",
    };
}
