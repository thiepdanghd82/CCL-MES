using System.Globalization;
using System.Text;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>Result of an NPI import (CSV or XLSX).</summary>
public sealed record NpiImportResult(string Kind, int Inserted, int Updated, int Skipped);

/// <summary>Thrown when the header row cannot be located in a parsed grid
/// (no row within the first <see cref="HeaderScanRows"/> rows contains a
/// recognisable "Part No" / "Parent Part No" header). The controller maps
/// this to 422 <c>import.header_not_found</c>.</summary>
public sealed class ImportHeaderNotFoundException(string kind)
    : Exception($"Could not locate a header row for NPI import kind '{kind}'.")
{
    public string Kind { get; } = kind;
}

/// <summary>
/// P10.5 follow-up — import for the NPI master-data grids
/// (Structure / Routing / Raw Materials), mirroring SpecHub's "Import…"
/// button. Tolerant header mapping: each entity field is read by trying
/// several candidate column names (case-insensitive), so slightly different
/// IFS export layouts still load. Rows missing the key part number are
/// skipped.
///
/// rawmaterials-bom-xlsx-import — this service is now format-agnostic: it
/// operates on an already-parsed grid (string[][]). The controller decodes
/// .xlsx (ClosedXML) or .csv into that grid before calling in. The header
/// row is auto-detected (it need not be row 0 — the "Materials BOM" export
/// has a blank first row). Raw Materials import is upsert-by-PartNo
/// (idempotent); Structure/Routing keep append semantics.
/// </summary>
public sealed class NpiImportService
{
    /// <summary>How many leading rows to scan when auto-detecting the header.</summary>
    public const int HeaderScanRows = 10;

    private readonly IMesDbContext _db;
    public NpiImportService(IMesDbContext db) => _db = db;

    // ── Legacy Stream entry point (CSV only) — kept so existing callers +
    //    tests still compile; delegates to the grid overload. ──────────────
    public Task<NpiImportResult> ImportAsync(
        string kind, Stream csv, string? actor, CancellationToken ct = default)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var grid = ParseCsv(text);
        return ImportAsync(kind, grid, actor, ct);
    }

    // ── Format-agnostic entry point — the grid is already parsed. ──────────
    public async Task<NpiImportResult> ImportAsync(
        string kind, IReadOnlyList<IReadOnlyList<string>> rows, string? actor,
        CancellationToken ct = default)
    {
        if (rows.Count == 0) return new NpiImportResult(kind, 0, 0, 0);

        var headerRow = FindHeaderRow(rows);
        if (headerRow < 0) throw new ImportHeaderNotFoundException(kind);

        var idx = BuildHeaderIndex(rows[headerRow]);
        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0, skipped = 0;

        switch (kind.ToLowerInvariant())
        {
            case "structures":
                for (var i = headerRow + 1; i < rows.Count; i++)
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
                for (var i = headerRow + 1; i < rows.Count; i++)
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
                // Upsert-by-PartNo. Preload the existing catalog keyed by
                // trimmed PartNo so re-importing the same file is idempotent
                // (row appears as Updated, not a duplicate insert).
                // First-wins on collisions: the live table may already hold
                // duplicate PartNo rows from historical append-only imports
                // (92 groups observed on prod 2026-08-20). ToDictionary would
                // throw on those; TryAdd tolerates them — the first row wins
                // the update target, the stale twins are left untouched.
                var existing = new Dictionary<string, RawMaterial>(StringComparer.Ordinal);
                foreach (var e in await _db.RawMaterials.ToListAsync(ct))
                    existing.TryAdd(e.PartNo, e);

                for (var i = headerRow + 1; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var part = Get(r, idx, "Part No", "PartNo", "Part");
                    if (string.IsNullOrWhiteSpace(part)) { skipped++; continue; }
                    var partNo = part!.Trim();

                    if (existing.TryGetValue(partNo, out var row))
                    {
                        MapRawMaterial(row, r, idx, overwriteNulls: false);
                        row.UpdatedAt = now;
                        row.UpdatedBy = actor;
                        updated++;
                    }
                    else
                    {
                        var fresh = new RawMaterial { PartNo = partNo, CreatedAt = now, CreatedBy = actor };
                        MapRawMaterial(fresh, r, idx, overwriteNulls: true);
                        _db.RawMaterials.Add(fresh);
                        existing[partNo] = fresh;   // dedupe within the same file
                        inserted++;
                    }
                }
                break;

            default:
                throw new ArgumentException($"Unknown NPI import kind: {kind}", nameof(kind));
        }

        await _db.SaveChangesAsync(ct);
        return new NpiImportResult(kind, inserted, updated, skipped);
    }

    // ── Raw-material field mapping (case-insensitive header aliases). ───────
    // overwriteNulls=false → never clobber an existing value with a blank
    // cell (partial re-imports keep prior data).
    private static void MapRawMaterial(
        RawMaterial e, IReadOnlyList<string> r, Dictionary<string, int> idx, bool overwriteNulls)
    {
        SetStr(v => e.PartDescription = v, Get(r, idx, "Part Description In Use", "Part Description", "Part Desc"), overwriteNulls);
        SetStr(v => e.MotherCode = v, Get(r, idx, "Mother code", "Mother Code"), overwriteNulls);
        SetStr(v => e.DimensionQuality = v, Get(r, idx, "Dimension/ Quality", "Dimension/Quality", "Dimension / Quality"), overwriteNulls);
        SetNum(v => e.WidthMm = v, Get(r, idx, "Width (mm)", "Width"), overwriteNulls);
        SetStr(v => e.PartType = v, Get(r, idx, "Part Type"), overwriteNulls);
        SetStr(v => e.Planner = v, Get(r, idx, "Planner"), overwriteNulls);
        SetStr(v => e.InventoryUom = v, Get(r, idx, "Inventory UoM", "Inventory UOM", "Inventory U/M"), overwriteNulls);
        SetStr(v => e.AccountingGroupDescription = v, Get(r, idx, "Accounting Group Description"), overwriteNulls);
        SetStr(v => e.ProductFamily = v, Get(r, idx, "Part Product Family"), overwriteNulls);
        SetStr(v => e.ProductFamilyDescription = v, Get(r, idx, "Part Product Family Description"), overwriteNulls);
        SetStr(v => e.TypeDesignation = v, Get(r, idx, "Type Designation"), overwriteNulls);
        SetNum(v => e.Price = v, Get(r, idx, "Price"), overwriteNulls);
        SetNum(v => e.PriceInclTax = v, Get(r, idx, "Price incl. Tax", "Price incl Tax"), overwriteNulls);
        SetStr(v => e.Currency = v, Get(r, idx, "Currency"), overwriteNulls);
        SetStr(v => e.PriceUom = v, Get(r, idx, "Price Unit Measure", "Price UOM"), overwriteNulls);
        SetNum(v => e.SupplierLeadtimeDays = v, Get(r, idx, "Supplier Manufacturing Leadtime", "Leadtime"), overwriteNulls);
        SetNum(v => e.Thickness = v, Get(r, idx, "Thickness"), overwriteNulls);
        SetStr(v => e.LeadTimeCode = v, Get(r, idx, "Lead Time Code"), overwriteNulls);
        SetStr(v => e.SupplierId = v, Get(r, idx, "Supplier ID", "Supplier Id"), overwriteNulls);
        SetStr(v => e.SupplierName = v, Get(r, idx, "Supplier Name", "Supplier"), overwriteNulls);
        // Legacy fields still honoured if present in a wider IFS export.
        SetStr(v => e.PurchUom = v, Get(r, idx, "Purch UOM", "Purchase UOM"), overwriteNulls);
        SetStr(v => e.StatusCode = v, Get(r, idx, "Status Code", "Status"), overwriteNulls);
        SetStr(v => e.CountryOfOrigin = v, Get(r, idx, "Country Of Origin", "Country of Origin"), overwriteNulls);
    }

    private static void SetStr(Action<string?> set, string? value, bool overwriteNulls)
    {
        if (value is not null) set(value);
        else if (overwriteNulls) set(null);
    }

    private static void SetNum(Action<double?> set, string? value, bool overwriteNulls)
    {
        var n = Num(value);
        if (n is not null) set(n);
        else if (overwriteNulls) set(null);
    }

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>Scan the first <see cref="HeaderScanRows"/> rows and return
    /// the index of the first row whose normalised (lower+trim) cells contain
    /// a recognised key header ("part no" / "part_no" / "parent part no").
    /// Returns -1 when none is found.</summary>
    private static int FindHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var limit = Math.Min(HeaderScanRows, rows.Count);
        for (var i = 0; i < limit; i++)
        {
            foreach (var cell in rows[i])
            {
                var c = cell.Trim().ToLowerInvariant();
                if (c is "part no" or "part_no" or "partno"
                      or "parent part no" or "parent_part_no")
                    return i;
            }
        }
        return -1;
    }

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
        // NumberStyles.Float allows scientific notation (e.g. "4.5E-2").
        return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var d) ? d : null;
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
