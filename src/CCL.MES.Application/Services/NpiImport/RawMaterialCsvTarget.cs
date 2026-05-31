using System.Globalization;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services.NpiImport;

/// <summary>
/// Phase 7 hạng mục 3 — concrete CSV target cho RawMaterials
/// (Raw Materials tab). Aliases header lấy nguyên từ CMES tham
/// chiếu (apps/web/src/modules/raw-materials/columns.tsx:RM_ALIASES)
/// đã verified khớp với IFS export thật của CCL Vietnam.
///
/// IFS source là XLSX 69 cột; operator cần "Save As .csv (UTF-8)"
/// trong Excel trước khi import. Min column count = 8 (tới Price UoM)
/// — required field chỉ `part_no` để mapping linh hoạt với IFS export
/// variants. Wizard `NpiImportModal` sẽ surface "rows skipped: missing
/// part_no" nếu CSV có dòng rỗng.
/// </summary>
public sealed class RawMaterialCsvTarget : ICsvImportTarget<RawMaterial>
{
    public string TableName => "RawMaterials";
    public string EntityKey => "raw_material";
    public int MinColumnCount => 8;

    public IReadOnlyList<string> RequiredFields { get; } = new[] { "part_no" };

    public IReadOnlyDictionary<string, string[]> HeaderAliases { get; } = new Dictionary<string, string[]>
    {
        ["part_no"]                   = new[] { "part no", "part_no", "partno" },
        ["part_description"]          = new[] { "part description", "part_description", "part desc" },
        ["supplier_id"]               = new[] { "supplier id", "supplier_id" },
        ["supplier_name"]             = new[] { "supplier name", "supplier_name" },
        ["price"]                     = new[] { "price" },
        ["price_incl_tax"]            = new[] { "price incl. tax", "price incl tax", "price_incl_tax" },
        ["currency"]                  = new[] { "currency", "cur" },
        ["price_uom"]                 = new[] { "price unit measure", "price uom", "price_uom" },
        ["supplier_leadtime_days"]    = new[] { "supplier manufacturing leadtime", "leadtime", "supplier_leadtime_days" },
        ["purch_uom"]                 = new[] { "purch u/m", "purch uom", "purch_uom" },
        ["inventory_uom"]             = new[] { "inventory u/m", "inventory uom", "inventory_uom" },
        ["site"]                      = new[] { "site" },
        ["site_description"]          = new[] { "site description", "site_description" },
        ["status_code"]               = new[] { "status code", "status_code" },
        ["minimum_quantity"]          = new[] { "minimum quantity", "minimum_quantity" },
        ["std_multiple_qty"]          = new[] { "std multiple qty", "std_multiple_qty" },
        ["standard_pack_size"]        = new[] { "standard pack size", "standard_pack_size", "pack size" },
        ["conversion_factor"]         = new[] { "conversion factor", "conversion_factor" },
        ["tax_code"]                  = new[] { "tax code", "tax_code" },
        ["tax_code_description"]      = new[] { "tax code description", "tax_code_description" },
        ["country_of_origin"]         = new[] { "country of origin", "country_of_origin" },
        ["acquisition_type"]          = new[] { "acquisition type", "acquisition_type" },
        ["supplier_part_no"]          = new[] { "supplier part no", "supplier_part_no" },
        ["supplier_part_description"] = new[] { "supplier part description", "supplier_part_description" },
        ["net_weight"]                = new[] { "net weight", "net_weight" },
        ["net_weight_uom"]            = new[] { "net weight uom", "net_weight_uom" },
        ["next_order_date"]           = new[] { "next order date", "next_order_date" },
        ["notes"]                     = new[] { "notes" },
    };

    public RawMaterial? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap)
    {
        var partNo = Pick(row, indexMap, "part_no");
        if (string.IsNullOrWhiteSpace(partNo))
            return null;

        return new RawMaterial
        {
            PartNo                  = partNo.Trim(),
            PartDescription         = NullIfEmpty(Pick(row, indexMap, "part_description")),
            SupplierId              = NullIfEmpty(Pick(row, indexMap, "supplier_id")),
            SupplierName            = NullIfEmpty(Pick(row, indexMap, "supplier_name")),
            Price                   = ToDoubleOrNull(Pick(row, indexMap, "price")),
            PriceInclTax            = ToDoubleOrNull(Pick(row, indexMap, "price_incl_tax")),
            Currency                = NullIfEmpty(Pick(row, indexMap, "currency")),
            PriceUom                = NullIfEmpty(Pick(row, indexMap, "price_uom")),
            SupplierLeadtimeDays    = ToDoubleOrNull(Pick(row, indexMap, "supplier_leadtime_days")),
            PurchUom                = NullIfEmpty(Pick(row, indexMap, "purch_uom")),
            InventoryUom            = NullIfEmpty(Pick(row, indexMap, "inventory_uom")),
            Site                    = NullIfEmpty(Pick(row, indexMap, "site")),
            SiteDescription         = NullIfEmpty(Pick(row, indexMap, "site_description")),
            StatusCode              = NullIfEmpty(Pick(row, indexMap, "status_code")),
            MinimumQuantity         = ToDoubleOrNull(Pick(row, indexMap, "minimum_quantity")),
            StdMultipleQty          = ToDoubleOrNull(Pick(row, indexMap, "std_multiple_qty")),
            StandardPackSize        = ToDoubleOrNull(Pick(row, indexMap, "standard_pack_size")),
            ConversionFactor        = ToDoubleOrNull(Pick(row, indexMap, "conversion_factor")),
            TaxCode                 = NullIfEmpty(Pick(row, indexMap, "tax_code")),
            TaxCodeDescription      = NullIfEmpty(Pick(row, indexMap, "tax_code_description")),
            CountryOfOrigin         = NullIfEmpty(Pick(row, indexMap, "country_of_origin")),
            AcquisitionType         = NullIfEmpty(Pick(row, indexMap, "acquisition_type")),
            SupplierPartNo          = NullIfEmpty(Pick(row, indexMap, "supplier_part_no")),
            SupplierPartDescription = NullIfEmpty(Pick(row, indexMap, "supplier_part_description")),
            NetWeight               = ToDoubleOrNull(Pick(row, indexMap, "net_weight")),
            NetWeightUom            = NullIfEmpty(Pick(row, indexMap, "net_weight_uom")),
            NextOrderDate           = NullIfEmpty(Pick(row, indexMap, "next_order_date")),
            Notes                   = NullIfEmpty(Pick(row, indexMap, "notes")),
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
