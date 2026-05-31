using System.Globalization;
using System.Text.RegularExpressions;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services.NpiImport;

/// <summary>
/// Phase 7 hạng mục 5 — concrete CSV target cho WorkCenters
/// (Machine List / Work Center tab). Mirror CMES tham chiếu
/// (apps/web/src/modules/work-center/wcImport.ts) cho header aliases +
/// strict WC_CODE_RE validation `^[A-Z0-9_-]{3,12}$`.
///
/// Khác hạng mục 1-3 ở 2 điểm:
///   1. Source data CCL-CMES hiện tại DERIVED từ Routing CSV (43 WC).
///      Import CSV mở khả năng operator add WC mới (vd cell test chưa có
///      routing) hoặc edit IdealSpeedPcsH/ShiftPattern/Active bulk.
///   2. Strict validation: Code không khớp regex → row bị skip, append
///      vào SkipReasons để Step 2 preview hiển thị rõ. Mirror CMES
///      "fail-fast on bad code" semantic.
///
/// Replace-all semantic (Q4 chốt) — KHÔNG upsert. Operator phải gộp
/// catalog vào 1 CSV trước khi import (KHÔNG append). Lưu ý: chạy
/// `tools/import_npi.py` sau sẽ overwrite WC bằng derived-from-Routing
/// → mất 3 field mới. Format hint trong UI đã ghi rõ.
/// </summary>
public sealed class WorkCenterCsvTarget : ICsvImportTarget<WorkCenter>
{
    public string TableName => "WorkCenters";
    public string EntityKey => "work_center";
    public int MinColumnCount => 2;

    public IReadOnlyList<string> RequiredFields { get; } = new[] { "code" };

    // WC_CODE_RE mirror CMES — 3-12 uppercase alphanumeric + `-` + `_`.
    // Pre-compiled cho throughput (Import có thể 100+ rows).
    private static readonly Regex WcCodeRe = new(@"^[A-Z0-9_-]{3,12}$", RegexOptions.Compiled);

    public IReadOnlyDictionary<string, string[]> HeaderAliases { get; } = new Dictionary<string, string[]>
    {
        ["code"]         = new[] { "wc code", "wc_code", "code", "work center code", "workcenter code", "machine code", "mã wc", "mã máy" },
        ["description"]  = new[] { "desc", "description", "work center", "mô tả", "machine name", "name", "tên máy" },
        ["area"]         = new[] { "area", "section", "nhóm", "khu vực", "department" },
        ["ideal_speed"]  = new[] { "ideal speed", "ideal_speed_pcs_h", "ideal speed pcs/h", "speed", "pcs/h", "ideal pcs h" },
        ["shift"]        = new[] { "shift", "shift pattern", "shift_pattern", "ca", "ca làm việc" },
        ["active"]       = new[] { "active", "enabled", "trạng thái" },
    };

    public WorkCenter? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap)
    {
        var rawCode = Pick(row, indexMap, "code").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(rawCode))
            return null;
        // Strict validation (Q3 chốt — REJECT + report skip count).
        // NpiCsvParser sẽ tăng counters.SkipReasons["invalid_code_format"]
        // dựa trên null return; preview Step 2 hiển thị rõ.
        if (!WcCodeRe.IsMatch(rawCode))
            return null;

        return new WorkCenter
        {
            Code           = rawCode,
            Description    = Pick(row, indexMap, "description").Trim(),
            Area           = NullIfEmpty(Pick(row, indexMap, "area")),
            IdealSpeedPcsH = ToDoubleOrNull(Pick(row, indexMap, "ideal_speed")),
            ShiftPattern   = NormalizeShift(Pick(row, indexMap, "shift")),
            Active         = ParseActive(Pick(row, indexMap, "active")),
        };
    }

    private static string Pick(string[] row, IReadOnlyDictionary<string, int> indexMap, string field)
    {
        if (!indexMap.TryGetValue(field, out var idx)) return "";
        if (idx < 0 || idx >= row.Length) return "";
        return row[idx] ?? "";
    }

    private static string? NullIfEmpty(string s)
    {
        var t = s.Trim();
        return t.Length == 0 ? null : t;
    }

    private static double? ToDoubleOrNull(string s)
    {
        var t = s.Trim().Replace(",", "");
        if (t.Length == 0 || t == "-") return null;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>
    /// Normalize shift pattern. Accept Q5 5 options + variants không phân
    /// biệt hoa thường + bỏ space. Vd "a + b" → "A+B". Không khớp → null
    /// (operator chỉnh sau qua UI dropdown).
    /// </summary>
    private static string? NormalizeShift(string s)
    {
        var t = s.Trim().ToUpperInvariant().Replace(" ", "");
        return t switch
        {
            "A"     => "A",
            "B"     => "B",
            "C"     => "C",
            "A+B"   => "A+B",
            "A+B+C" => "A+B+C",
            _       => null,
        };
    }

    /// <summary>
    /// Parse boolean tolerant các format thường gặp trong CSV:
    /// "1"/"true"/"yes"/"y"/"active" → true; "0"/"false"/"no"/"n"/"inactive" → false;
    /// empty/unknown → null (operator chỉnh qua UI).
    /// </summary>
    private static bool? ParseActive(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        return t switch
        {
            "" or "-"                                              => null,
            "1" or "true"  or "yes" or "y" or "active"   or "✓"   => true,
            "0" or "false" or "no"  or "n" or "inactive" or "✗"   => false,
            _                                                      => null,
        };
    }
}
