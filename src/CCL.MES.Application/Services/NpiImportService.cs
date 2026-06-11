using System.Globalization;
using System.Text;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>Result of an NPI CSV import.</summary>
public sealed record NpiImportResult(string Kind, int Inserted, int Skipped);

/// <summary>
/// P10.5 follow-up — CSV import for the NPI master-data grids
/// (Structure / Routing / Raw Materials), mirroring SpecHub's "Import…"
/// button. Tolerant header mapping: each entity field is read by trying
/// several candidate column names, so slightly different IFS export
/// layouts still load. Rows missing the key part number are skipped.
/// Append semantics (rows are added, not deduped) — use Clear first for
/// a full reload.
/// </summary>
public sealed class NpiImportService
{
    private readonly IMesDbContext _db;
    public NpiImportService(IMesDbContext db) => _db = db;

    public async Task<NpiImportResult> ImportAsync(
        string kind, Stream csv, string? actor, CancellationToken ct = default)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        var rows = ParseCsv(text);
        if (rows.Count < 2) return new NpiImportResult(kind, 0, 0);

        var idx = BuildHeaderIndex(rows[0]);
        var now = DateTime.UtcNow;
        int inserted = 0, skipped = 0;

        switch (kind.ToLowerInvariant())
        {
            case "structures":
                for (var i = 1; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var parent = Get(r, idx, "Parent Part No", "ParentPart", "Parent Part");
                    var comp = Get(r, idx, "Component Part", "ComponentPart", "Component Part No");
                    if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(comp)) { skipped++; continue; }
                    _db.ManufacturingStructures.Add(new ManufacturingStructure
                    {
                        ParentPart = parent!.Trim(),
                        ParentDescription = Get(r, idx, "Parent Part Description"),
                        ComponentPart = comp!.Trim(),
                        ComponentDescription = Get(r, idx, "Component Part Description"),
                        QtyAssembly = Num(Get(r, idx, "Qty Per Assembly", "Qty/Assembly", "Quantity")) ?? 0,
                        ScrapFactor = Num(Get(r, idx, "Component Scrap", "Scrap Factor")) ?? 0,
                        ScrapPct = Num(Get(r, idx, "Scrap Factor (%)", "Scrap %")),
                        Pitch = Num(Get(r, idx, "Pitch")),
                        Cavity = Num(Get(r, idx, "Cavity")),
                        Color = Get(r, idx, "Color Nums", "Colors", "Color"),
                        StructureType = Get(r, idx, "Structure Type"),
                        Alt = Get(r, idx, "Alternative No", "Alt"),
                        Effectivity = Get(r, idx, "Structure Effectivity", "Effectivity"),
                        Uom = Get(r, idx, "UOM", "Uom", "Unit of Measure"),
                        Planner = Get(r, idx, "Planner"),
                        CreatedAt = now,
                        CreatedBy = actor,
                    });
                    inserted++;
                }
                break;

            case "routings":
                for (var i = 1; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var part = Get(r, idx, "Part No", "PartNo", "Part");
                    if (string.IsNullOrWhiteSpace(part)) { skipped++; continue; }
                    _db.RoutingOperations.Add(new RoutingOperation
                    {
                        PartNo = part!.Trim(),
                        PartDescription = Get(r, idx, "Part Description"),
                        OpNo = Get(r, idx, "Operation No", "Op No", "OpNo"),
                        Operation = Get(r, idx, "Operation Description", "Operation"),
                        WorkCenterNo = Get(r, idx, "Work Center No", "Work Center"),
                        WorkCenterDescription = Get(r, idx, "Work Center Description"),
                        MachineSetupTime = Num(Get(r, idx, "Machine Setup Time")),
                        LaborSetupTime = Num(Get(r, idx, "Labor Setup Time")),
                        MachineRunTime = Num(Get(r, idx, "Machine Run Time")),
                        LaborRunTime = Num(Get(r, idx, "Labor Run Time")),
                        Unit = Get(r, idx, "Unit", "UOM"),
                        LaborClass = Get(r, idx, "Labor Class"),
                        Alt = Get(r, idx, "Alternative No", "Alt"),
                        Effectivity = Get(r, idx, "Effectivity"),
                        RoutingType = Get(r, idx, "Routing Type"),
                        Planner = Get(r, idx, "Planner"),
                        CreatedAt = now,
                        CreatedBy = actor,
                    });
                    inserted++;
                }
                break;

            case "rawmaterials":
                for (var i = 1; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var part = Get(r, idx, "Part No", "PartNo", "Part");
                    if (string.IsNullOrWhiteSpace(part)) { skipped++; continue; }
                    _db.RawMaterials.Add(new RawMaterial
                    {
                        PartNo = part!.Trim(),
                        PartDescription = Get(r, idx, "Part Description"),
                        SupplierId = Get(r, idx, "Supplier Id", "Supplier ID"),
                        SupplierName = Get(r, idx, "Supplier Name", "Supplier"),
                        Price = Num(Get(r, idx, "Price")),
                        Currency = Get(r, idx, "Currency"),
                        PurchUom = Get(r, idx, "Purch UOM", "Purchase UOM"),
                        InventoryUom = Get(r, idx, "Inventory UOM"),
                        StatusCode = Get(r, idx, "Status Code", "Status"),
                        CountryOfOrigin = Get(r, idx, "Country Of Origin", "Country of Origin"),
                        CreatedAt = now,
                        CreatedBy = actor,
                    });
                    inserted++;
                }
                break;

            default:
                throw new ArgumentException($"Unknown NPI import kind: {kind}", nameof(kind));
        }

        await _db.SaveChangesAsync(ct);
        return new NpiImportResult(kind, inserted, skipped);
    }

    // ── helpers ─────────────────────────────────────────────────────

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var key = header[i].Trim();
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = i;
        }
        return map;
    }

    private static string? Get(IReadOnlyList<string> row, Dictionary<string, int> idx, params string[] names)
    {
        foreach (var n in names)
        {
            if (idx.TryGetValue(n, out var i) && i < row.Count)
            {
                var v = row[i].Trim();
                if (v.Length > 0) return v;
            }
        }
        return null;
    }

    private static double? Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Replace("%", "").Replace(",", "").Trim();
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Minimal RFC-4180 CSV parser — handles quoted fields with
    /// embedded commas, doubled quotes, and CRLF/LF line endings.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default: field.Append(c); break;
                }
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
