using System.Globalization;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services.NpiImport;

/// <summary>
/// Phase 7 hạng mục 1 — concrete CSV target cho ManufacturingStructures
/// (Engineer Structure tab). Aliases header lấy nguyên từ CMES tham
/// chiếu (apps/server/src/modules/engineer-structure/engineer-structure.service.ts:HEADER_ALIASES)
/// đã verified khớp với IFS export thật của CCL Vietnam (31 cột,
/// 20,530 rows).
/// </summary>
public sealed class StructureCsvTarget : ICsvImportTarget<ManufacturingStructure>
{
    public string TableName => "ManufacturingStructures";
    public string EntityKey => "structure";
    public int MinColumnCount => 11;

    public IReadOnlyList<string> RequiredFields { get; } = new[] { "parent_part", "component_part" };

    public IReadOnlyDictionary<string, string[]> HeaderAliases { get; } = new Dictionary<string, string[]>
    {
        ["parent_part"]      = new[] { "parent part no", "parent part", "parent_part" },
        ["parent_desc"]      = new[] { "parent part description", "parent description", "parent_description", "parent desc" },
        ["component_part"]   = new[] { "component part", "component part no", "component_part" },
        ["component_desc"]   = new[] { "component part description", "component description", "component_description", "component desc" },
        ["qty_per_assembly"] = new[] { "qty per assembly", "qty/assembly", "qty per assy", "quantity", "qty_per_assembly" },
        ["uom"]              = new[] { "uom", "unit" },
        // CCL distinguishes the absolute scrap quantity ("Component Scrap") from the scrap factor percentage ("Scrap Factor (%)").
        ["scrap"]            = new[] { "component scrap", "scrap" },
        ["scrap_pct"]        = new[] { "scrap factor (%)", "scrap factor", "scrap %", "scrap_pct", "scrap percent", "scrap%" },
        ["pitch"]            = new[] { "pitch" },
        ["cavity"]           = new[] { "cavity" },
        // IFS column is plural — "Color Nums".
        ["colors"]           = new[] { "color nums", "color num", "colors", "color", "colours" },
        // IFS prefixes "Structure" — the bare "Effectivity" never appears.
        ["effectivity"]      = new[] { "structure effectivity", "effectivity" },
        // "Phase In" is the date the BOM line becomes effective; we surface it as the operator-facing "Date" column.
        ["effectivity_date"] = new[] { "phase in", "phase_in", "last activity date", "date", "effectivity date", "rev date" },
        // IFS uses "Alternative No"; the bare "Alt" stays as a fallback.
        ["alt"]              = new[] { "alternative no", "alternative", "alt" },
        ["structure_type"]   = new[] { "structure type", "structure_type", "type" },
        ["planner"]          = new[] { "planner" },
    };

    public ManufacturingStructure? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap)
    {
        var parent = Pick(row, indexMap, "parent_part");
        var component = Pick(row, indexMap, "component_part");
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(component))
            return null;

        return new ManufacturingStructure
        {
            ParentPart           = parent.Trim(),
            ParentDescription    = NullIfEmpty(Pick(row, indexMap, "parent_desc")),
            ComponentPart        = component.Trim(),
            ComponentDescription = NullIfEmpty(Pick(row, indexMap, "component_desc")),
            QtyAssembly          = ToDoubleOrZero(Pick(row, indexMap, "qty_per_assembly")),
            Uom                  = NullIfEmpty(Pick(row, indexMap, "uom")),
            ScrapFactor          = ToDoubleOrZero(Pick(row, indexMap, "scrap")),
            ScrapPct             = ToDoubleOrNull(Pick(row, indexMap, "scrap_pct")),
            Pitch                = ToDoubleOrNull(Pick(row, indexMap, "pitch")),
            Cavity               = ToDoubleOrNull(Pick(row, indexMap, "cavity")),
            Color                = NullIfEmpty(Pick(row, indexMap, "colors")),
            StructureType        = NullIfEmpty(Pick(row, indexMap, "structure_type")),
            Alt                  = NullIfEmpty(Pick(row, indexMap, "alt")),
            Effectivity          = NullIfEmpty(Pick(row, indexMap, "effectivity")),
            EffectivityDate      = NullIfEmpty(Pick(row, indexMap, "effectivity_date")),
            Planner              = NullIfEmpty(Pick(row, indexMap, "planner")),
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

    private static double ToDoubleOrZero(string s)
    {
        var t = s.Trim().TrimEnd('%').Replace(",", "");
        if (t.Length == 0 || t == "-") return 0;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static double? ToDoubleOrNull(string s)
    {
        var t = s.Trim().TrimEnd('%').Replace(",", "");
        if (t.Length == 0 || t == "-") return null;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
