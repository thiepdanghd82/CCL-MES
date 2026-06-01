using System.Globalization;
using CCL.MES.Application.Services;

namespace CCL.MES.Application.SpecExport;

/// <summary>
/// Phase 8 PR #31c — Canonical 14-cột definition cho NPI Spec Library list
/// view. Single source of truth dùng cho cả 3 exporter (CSV/Excel/PDF) +
/// EngineerSpec grid (cột Planner đã DERIVE từ ProcessCode via
/// <see cref="SpecService.PlannerFromProcessCode"/>).
///
/// Thứ tự + nhãn KHỚP SpecHub list view + EngineerSpec.razor `AllCols` (PR #30).
/// Cột # render từ row index → KHÔNG có ở danh sách export (export thêm cột #
/// riêng ở mỗi exporter).
///
/// Reusable cho PR #33 single-spec detail sheet PDF — header layout chung.
/// </summary>
public static class SpecListColumns
{
    /// <summary>13 cột data + 1 cột "#" sequence — tổng 14 grid cells.</summary>
    public static readonly IReadOnlyList<SpecListColumn> All = new[]
    {
        new SpecListColumn("planner",       "Planner",   ColumnType.Text,     12),
        new SpecListColumn("ref_no",        "Ref No",    ColumnType.Text,     16),
        new SpecListColumn("customer",      "Customer",  ColumnType.Text,     20),
        new SpecListColumn("part_no",       "Part No",   ColumnType.Text,     22),
        new SpecListColumn("part_name",     "Part Name", ColumnType.Text,     28),
        new SpecListColumn("colors",        "Colors",    ColumnType.Int,       8),
        new SpecListColumn("cavity",        "Cavity",    ColumnType.Int,       8),
        new SpecListColumn("pitch",         "Pitch",     ColumnType.Decimal1,  9),
        new SpecListColumn("inspection",    "Spec",      ColumnType.Text,      9),
        new SpecListColumn("status",        "Status",    ColumnType.Text,     12),
        new SpecListColumn("revision_code", "Rev",       ColumnType.Text,      6),
        new SpecListColumn("rev_date",      "Rev Date",  ColumnType.Date,     11),
        new SpecListColumn("rev_by",        "By",        ColumnType.Text,     14),
    };

    /// <summary>
    /// Pre-flatten row → 13 string fields cho CSV/PDF rendering (display-ready).
    /// Excel keeps typed values qua <see cref="ToTypedCells"/>.
    /// </summary>
    public static string[] ToDisplayCells(ProductRevisionListItem row, CultureInfo culture)
    {
        return new[]
        {
            row.Planner ?? "UNKNOWN",
            row.RefNo ?? "",
            row.CustomerName ?? "",
            row.ProductCode ?? "",
            string.IsNullOrEmpty(row.ProductName) ? row.Title : row.ProductName,
            row.NumColors > 0 ? row.NumColors.ToString(culture) : "",
            row.Cavity?.ToString(culture) ?? "",
            row.PitchMm?.ToString("N1", culture) ?? "",
            row.InspectionLevel ?? "",
            StatusDisplay(row.Status),
            row.RevisionCode ?? "",
            row.LastUpdatedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            row.LastUpdatedBy ?? "",
        };
    }

    /// <summary>
    /// Typed projection cho Excel — số nguyên/double/datetime giữ native type
    /// để ClosedXML format cells đúng. Null cho ô rỗng.
    /// </summary>
    public static object?[] ToTypedCells(ProductRevisionListItem row)
    {
        return new object?[]
        {
            row.Planner ?? "UNKNOWN",
            row.RefNo,
            row.CustomerName,
            row.ProductCode,
            string.IsNullOrEmpty(row.ProductName) ? row.Title : row.ProductName,
            row.NumColors > 0 ? row.NumColors : (int?)null,
            row.Cavity,
            row.PitchMm,
            row.InspectionLevel,
            StatusDisplay(row.Status),
            row.RevisionCode,
            row.LastUpdatedAt,
            row.LastUpdatedBy,
        };
    }

    /// <summary>
    /// PR #30 Q13 — 5-state enum mapped to 3-state SpecHub display
    /// (Draft+InReview→DRAFT, Approved+Released→APPROVED, Superseded→Superseded).
    /// </summary>
    public static string StatusDisplay(CCL.MES.Domain.ProductRevisionStatus status) => status switch
    {
        CCL.MES.Domain.ProductRevisionStatus.Draft       or CCL.MES.Domain.ProductRevisionStatus.InReview  => "DRAFT",
        CCL.MES.Domain.ProductRevisionStatus.Approved    or CCL.MES.Domain.ProductRevisionStatus.Released  => "APPROVED",
        CCL.MES.Domain.ProductRevisionStatus.Superseded                                                    => "Superseded",
        _                                                                                                   => status.ToString(),
    };
}

/// <summary>Type hint cho exporter cells (Excel + PDF use).</summary>
public enum ColumnType
{
    Text,
    Int,
    Decimal1,
    Date,
}

/// <summary>
/// Column definition — single source of truth. <paramref name="WidthCh"/> đơn
/// vị character (Excel column width + PDF table width tỉ lệ).
/// </summary>
public record SpecListColumn(string Key, string Label, ColumnType Type, int WidthCh);
