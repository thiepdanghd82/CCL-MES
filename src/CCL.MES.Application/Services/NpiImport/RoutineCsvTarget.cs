using System.Globalization;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services.NpiImport;

/// <summary>
/// Phase 7 hạng mục 2 — concrete CSV target cho RoutingOperations
/// (Engineer Routine tab). Aliases header lấy nguyên từ CMES tham
/// chiếu (apps/server/src/modules/engineer-routine/engineer-routine.service.ts:HEADER_ALIASES)
/// + thêm `planner` alias (CCL Vietnam parity với Structure tab, CSV col 26).
///
/// IFS export "RoutingOperations *.csv" có 62 cột; map đúng 11 cột
/// engine cần (part_no/op_no = required; 8 cột mới index 7/13/19/20/21/24/43/58/60
/// + Planner index 26). Min column count = 13 (tới Factor Unit) để row
/// không lừng tâm trong các IFS export sleeves cũ.
/// </summary>
public sealed class RoutineCsvTarget : ICsvImportTarget<RoutingOperation>
{
    public string TableName => "RoutingOperations";
    public string EntityKey => "routine";
    public int MinColumnCount => 13;

    public IReadOnlyList<string> RequiredFields { get; } = new[] { "part_no", "op_no" };

    public IReadOnlyDictionary<string, string[]> HeaderAliases { get; } = new Dictionary<string, string[]>
    {
        ["part_no"]      = new[] { "part no", "part_no", "partno" },
        ["part_desc"]    = new[] { "part description", "part_description", "part desc" },
        ["op_no"]        = new[] { "operation no", "op no", "op_no", "opno" },
        ["operation"]    = new[] { "operation description", "operation", "op_desc" },
        ["work_center"]  = new[] { "work centre no", "work center no", "work_centre_no", "work_center" },
        ["wc_desc"]      = new[] { "work centre desc", "work center desc", "wc_desc" },
        ["mach_setup"]   = new[] { "mach setup time", "machine setup time", "mach_setup" },
        ["labor_setup"]  = new[] { "labour setup time", "labor setup time", "labor_setup" },
        ["mach_run"]     = new[] { "mach run factor", "machine run factor", "mach_run" },
        ["labor_run"]    = new[] { "labour run factor", "labor run factor", "labor_run" },
        // Phase 7 hạng mục 2 — 10 cột mới.
        ["unit"]         = new[] { "factor unit", "unit" },
        ["crew"]         = new[] { "crew size", "crew" },
        ["setup_crew"]   = new[] { "setup crew size", "setup_crew" },
        ["labor_class"]  = new[] { "labour class", "labor class", "labor_class" },
        ["alt"]          = new[] { "alternative", "alt" },
        ["effectivity"]  = new[] { "routing effectivity", "effectivity" },
        ["efficiency"]   = new[] { "efficiency factor", "efficiency" },
        ["site"]         = new[] { "site" },
        ["routing_type"] = new[] { "routing type", "routing_type" },
        // Planner — parity với Structure tab (Q2 chốt). IFS col 26.
        ["planner"]      = new[] { "planner" },
    };

    public RoutingOperation? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap)
    {
        var partNo = Pick(row, indexMap, "part_no");
        var opNo = Pick(row, indexMap, "op_no");
        if (string.IsNullOrWhiteSpace(partNo) || string.IsNullOrWhiteSpace(opNo))
            return null;

        return new RoutingOperation
        {
            PartNo                = partNo.Trim(),
            PartDescription       = NullIfEmpty(Pick(row, indexMap, "part_desc")),
            OpNo                  = opNo.Trim(),
            Operation             = NullIfEmpty(Pick(row, indexMap, "operation")),
            WorkCenterNo          = NullIfEmpty(Pick(row, indexMap, "work_center")),
            WorkCenterDescription = NullIfEmpty(Pick(row, indexMap, "wc_desc")),
            MachineSetupTime      = ToDoubleOrNull(Pick(row, indexMap, "mach_setup")),
            LaborSetupTime        = ToDoubleOrNull(Pick(row, indexMap, "labor_setup")),
            MachineRunTime        = ToDoubleOrNull(Pick(row, indexMap, "mach_run")),
            LaborRunTime          = ToDoubleOrNull(Pick(row, indexMap, "labor_run")),
            Unit                  = NullIfEmpty(Pick(row, indexMap, "unit")),
            Crew                  = ToDoubleOrNull(Pick(row, indexMap, "crew")),
            SetupCrew             = ToDoubleOrNull(Pick(row, indexMap, "setup_crew")),
            LaborClass            = NullIfEmpty(Pick(row, indexMap, "labor_class")),
            Alt                   = NullIfEmpty(Pick(row, indexMap, "alt")),
            Effectivity           = NullIfEmpty(Pick(row, indexMap, "effectivity")),
            Efficiency            = ToDoubleOrNull(Pick(row, indexMap, "efficiency")),
            Site                  = NullIfEmpty(Pick(row, indexMap, "site")),
            RoutingType           = NullIfEmpty(Pick(row, indexMap, "routing_type")),
            Planner               = NullIfEmpty(Pick(row, indexMap, "planner")),
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
        var t = s.Trim().TrimEnd('%').Replace(",", "");
        if (t.Length == 0 || t == "-") return null;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
