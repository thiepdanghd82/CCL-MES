# PHASE 7 — HẠNG MỤC 3: Raw Materials

> Khảo sát + plan đồng bộ tab **Raw Materials** theo CMES tham chiếu.
> **Chưa code** — chờ anh duyệt Q1–Q9 trước khi tạo branch.
>
> Pattern reuse từ hạng mục 1+2: `ICsvImportTarget<TEntity>` + `NpiImportService`
> + `NpiImportModal<TEntity>` + .rt-* CSS + JSInterop localStorage. Hạng mục 3
> có 2 điểm KHÁC biệt cần anh quyết: **(a) xoá 5 field legacy "catalog"** vì
> chúng là proxy mapping sai semantic; **(b) IFS source là XLSX không phải CSV**
> — cần quyết wizard nhận file format gì.

## 1) State hiện tại (CCL-CMES `main` post-PR #23)

### 1.1 Entity `RawMaterial` (`src/CCL.MES.Domain/Entities/Npi.cs`)

12 field, 1 numeric (Price) là `double` non-nullable:

| Field | Type | Note |
|---|---|---|
| PartNo | string | required |
| PartDescription | string? | |
| SupplierId | string? | |
| SupplierName | string? | |
| Price | **double** | 0 nếu xlsx trống |
| Currency | string? | |
| PriceUom | string? | |
| **CatalogGroup** | string? | **LEGACY PROXY** — mapped từ `Acquisition Type` (IFS col 31), tên cột sai |
| **CatalogDesc** | string? | **LEGACY PROXY** — mapped từ `Site` (IFS col 10) |
| **Grp** | string? | **LEGACY PROXY** — mapped từ `Status Code` (IFS col 29) |
| **Type** | string? | **LEGACY PROXY** — mapped từ `Status Code Description` (col 30) |
| **TypeDesc** | string? | **LEGACY PROXY** — mapped từ `Status Code Description` (col 30, DUPLICATE) |

**Importer comment xác nhận** (`tools/import_npi.py:312-316`):
> "The current IFS export does NOT carry the historical 'Catalog Group' / 'Catalog Desc' columns the entity schema originally targeted. The closest-meaning columns above are used so the read-side UI has something to show; if these mappings are wrong for business use, flag for Phase-1 follow-up."

→ 5 field này là **technical debt** từ trước Phase 7. Giữ chúng = giữ data sai tên.

### 1.2 UI `RawMaterials.razor`

8 cột, không có freeze, không có Columns toggle, không có Import button:
`Part No | Part Description | Supplier ID | Supplier Name | Price | Cur | Price UOM | Catalog Group`

Cột "Catalog Group" thực ra hiện Acquisition Type — tên gây hiểu nhầm.

### 1.3 Importer (`tools/import_npi.py:read_raw_materials`)

Đọc xlsx 69 cột, map chỉ 12 cột vào entity. Dùng `openpyxl`. 2,127 rows.

### 1.4 Search hiện tại

`NpiService.RawMaterialsAsync` filter 4 field: `PartNo`, `PartDescription`, `SupplierName`, `SupplierId`.

### 1.5 Entity coupling (CRITICAL — không touched)

`RawMaterial` được link bởi 3 nơi ngoài tab Raw Materials:
1. **`Iqc.cs:23-25`**: hybrid FK `RawMaterial? RawMaterial { get; set; }` + `int? RawMaterialId`
2. **`IqcService.cs:40`**: lookup `RawMaterials.FirstOrDefaultAsync(x => x.PartNo == r.PartNo)` để snapshot SupplierName
3. **`DbSeeder.cs:97-102`**: lookup PartNo cho fixture seed

→ **Migration phải bảo toàn**: `Id`, `PartNo`, `SupplierName` (3 field IQC dùng). Tất cả NEW + RENAME khác đều an toàn.

---

## 2) Gap với CMES tham chiếu

### 2.1 CMES có 28 cột UI; IFS xlsx có 69 cột source

CMES surface 28 trong 69 cột: 14 visible mặc định + 14 hidden trong Columns picker. Còn lại 41 cột IFS được skip vì rarely consumed.

### 2.2 Mapping 28 cột CMES ↔ IFS xlsx ↔ CCL-CMES action

| # | CMES field | CMES type | IFS xlsx column | IFS idx | Default | CCL action |
|---|---|---|---|---|---|---|
| 1 | part_no | string | Part No | 0 | visible (★ frozen) | keep PartNo |
| 2 | part_description | string | Part Description | 1 | visible | keep PartDescription |
| 3 | supplier_id | string | Supplier ID | 2 | visible | keep SupplierId |
| 4 | supplier_name | string | Supplier Name | 3 | visible | keep SupplierName |
| 5 | price | number/null | Price | 4 | visible | **Q2**: `double` → `double?` |
| 6 | **price_incl_tax** | number/null | Price incl. Tax | 5 | hidden | **NEW** `double?` |
| 7 | currency | string | Currency | 6 | visible | keep Currency |
| 8 | price_uom | string | Price Unit Measure | 7 | visible | keep PriceUom |
| 9 | **supplier_leadtime_days** | number/null | Supplier Manufacturing Leadtime | 8 | visible | **NEW** `double?` |
| 10 | **purch_uom** | string | Purch U/M | 9 | visible | **NEW** `string?` |
| 11 | **inventory_uom** | string | Inventory U/M | 12 | visible | **NEW** `string?` |
| 12 | **site** | string | Site | 10 | visible | **NEW** `string?` |
| 13 | **site_description** | string | Site Description | 11 | hidden | **NEW** `string?` |
| 14 | **status_code** | string | Status Code | 29 | visible (badge) | **NEW** `string?` |
| 15 | **minimum_quantity** | number/null | Minimum Quantity | 37 | hidden | **NEW** `double?` |
| 16 | **std_multiple_qty** | number/null | Std Multiple Qty | 38 | hidden | **NEW** `double?` |
| 17 | **standard_pack_size** | number/null | Standard Pack Size | 28 | hidden | **NEW** `double?` |
| 18 | **conversion_factor** | number/null | Conversion Factor | 13 | hidden | **NEW** `double?` |
| 19 | **tax_code** | string | Tax Code | 17 | hidden | **NEW** `string?` |
| 20 | **tax_code_description** | string | Tax Code Description | 18 | hidden | **NEW** `string?` |
| 21 | **country_of_origin** | string | Country of Origin | 39 | hidden | **NEW** `string?` |
| 22 | **acquisition_type** | string | Acquisition Type | 31 | hidden | **NEW** `string?` (was CatalogGroup proxy) |
| 23 | **supplier_part_no** | string | Supplier Part No | 32 | hidden | **NEW** `string?` |
| 24 | **supplier_part_description** | string | Supplier Part Description | 33 | hidden | **NEW** `string?` |
| 25 | **net_weight** | number/null | Net Weight | 64 | hidden | **NEW** `double?` |
| 26 | **net_weight_uom** | string | Net Weight UoM | 65 | hidden | **NEW** `string?` |
| 27 | **next_order_date** | string | Next Order Date | 62 | hidden | **NEW** `string?` |
| 28 | **notes** | string | Notes | 53 | hidden | **NEW** `string?` |

**Tổng cộng**: 7 field hiện có giữ + 21 field mới = **28 field** (= CMES).

### 2.3 Legacy fields cần DROP (Q1)

5 field CatalogGroup/CatalogDesc/Grp/Type/TypeDesc trỏ vào IFS columns mà CMES đã expose riêng:

| Legacy field | Actual IFS mapping | CMES new equivalent | Decision |
|---|---|---|---|
| CatalogGroup | Acquisition Type (col 31) | `acquisition_type` (#22) | DROP — replaced by clearer name |
| CatalogDesc | Site (col 10) | `site` (#12) | DROP — replaced |
| Grp | Status Code (col 29) | `status_code` (#14) | DROP — replaced |
| Type | Status Code Description (col 30) | (covered by Status Code badge) | DROP — replaced, was duplicate |
| TypeDesc | Status Code Description (col 30, dup) | (same) | DROP — was literal duplicate of Type |

→ Drop cả 5 + giữ data cleaner. **IQC FK NOT affected** (FK based on Id, PartNo, SupplierName only).

### 2.4 Search field expansion (Q7)

CMES filter: `part_no, part_description, supplier_id, supplier_name, status_code, country_of_origin` (6 field).
CCL-CMES hiện tại: 4 field (PartNo, PartDescription, SupplierName, SupplierId).

**Q7**: mở rộng thêm `StatusCode + CountryOfOrigin` (2 field mới sau khi ADD COLUMN)?

---

## 3) Plan code (sau khi anh chốt Q1–Q9)

### 3.1 Migration v6+1 `AddRawMaterialExtendedFields` (provider-agnostic, A→B→C SAFE)

A→B→C SAFE pattern (giống Routine + Structure):
- **A**: backup SHA256 của `data/ccl_mes.db`
- **B**: test isolated trên `/tmp/rawmaterial-design.db` — generate migration, apply, verify row count = 2,127 unchanged, IQC entity FK still functional (verify với simple SELECT JOIN)
- **C**: apply real trên live DB, verify row count = 2,127 + Routing 38,441 + Structure 20,530 + WC 43 (4 NPI tables unchanged)

Migration ops (SQLite tự rebuild table):
- ALTER COLUMN `Price` from `double` to `double?` (Q2 dependency)
- DROP COLUMN `CatalogGroup`, `CatalogDesc`, `Grp`, `Type`, `TypeDesc` (Q1 dependency)
- ADD COLUMN × 21 new fields (Q3)

Provider-agnostic strip: script Python 3.2.B remove `type:"REAL"/oldType:"REAL"` từ migration .cs + Designer.cs + ModelSnapshot.cs (sau khi apply).

### 3.2 Entity update (`Npi.cs`)

```csharp
public class RawMaterial : BaseEntity
{
    public string PartNo { get; set; } = "";
    public string? PartDescription { get; set; }
    public string? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public double? Price { get; set; }              // Q2 — nullable
    public double? PriceInclTax { get; set; }       // NEW
    public string? Currency { get; set; }
    public string? PriceUom { get; set; }
    public double? SupplierLeadtimeDays { get; set; } // NEW
    public string? PurchUom { get; set; }           // NEW
    public string? InventoryUom { get; set; }       // NEW
    public string? Site { get; set; }               // NEW
    public string? SiteDescription { get; set; }    // NEW
    public string? StatusCode { get; set; }         // NEW
    public double? MinimumQuantity { get; set; }    // NEW
    public double? StdMultipleQty { get; set; }     // NEW
    public double? StandardPackSize { get; set; }   // NEW
    public double? ConversionFactor { get; set; }   // NEW
    public string? TaxCode { get; set; }            // NEW
    public string? TaxCodeDescription { get; set; } // NEW
    public string? CountryOfOrigin { get; set; }    // NEW
    public string? AcquisitionType { get; set; }    // NEW
    public string? SupplierPartNo { get; set; }     // NEW
    public string? SupplierPartDescription { get; set; } // NEW
    public double? NetWeight { get; set; }          // NEW
    public string? NetWeightUom { get; set; }       // NEW
    public string? NextOrderDate { get; set; }      // NEW
    public string? Notes { get; set; }              // NEW
    // DROPPED: CatalogGroup, CatalogDesc, Grp, Type, TypeDesc
}
```

### 3.3 Importer update (`tools/import_npi.py`)

Mở rộng `read_raw_materials` từ 12 → 28 column map (column indices đã verify trên xlsx thật).
`insert_raw_materials` INSERT statement chuyển từ 13 tuple → 29 tuple.

### 3.4 UI redesign (`Pages/Npi/RawMaterials.razor`)

100% pattern từ `EngineerStructure.razor` + `EngineerRoutine.razor`:
- 28 ColumnDef array với i18n labels
- Freeze sticky thead (`.rt-table-wrap` + `max-height: calc(100vh-240px)`)
- **Q4**: optional thêm frozen first column PartNo (CMES có, Structure/Routine không)
- Columns toggle popover + localStorage `cclmes.raw-materials.columns-hidden.v1`
- **Q5**: default hidden 14 cột (mirror CMES) hoặc show all
- Import button (AuthorizeView Admin/Engineer) → `<NpiImportModal TEntity="RawMaterial">`
- Search-as-you-type + Enter trigger
- Pager 50/page
- Status code badge styling (`.rm-status .rm-status--inactive` etc.)

### 3.5 `RawMaterialCsvTarget` (concrete `ICsvImportTarget<RawMaterial>`)

```csharp
public sealed class RawMaterialCsvTarget : ICsvImportTarget<RawMaterial>
{
    public string TableName => "RawMaterials";
    public string EntityKey => "raw_material";
    public int MinColumnCount => 8;  // tới Price UoM
    public IReadOnlyList<string> RequiredFields { get; } = new[] { "part_no" };
    public IReadOnlyDictionary<string, string[]> HeaderAliases { get; } = new Dictionary<string, string[]>
    {
        ["part_no"]                  = new[] { "part no", "part_no", "partno" },
        ["part_description"]         = new[] { "part description", "part_description", "part desc" },
        ["supplier_id"]              = new[] { "supplier id", "supplier_id" },
        ["supplier_name"]            = new[] { "supplier name", "supplier_name" },
        ["price"]                    = new[] { "price" },
        ["price_incl_tax"]           = new[] { "price incl. tax", "price incl tax", "price_incl_tax" },
        ["currency"]                 = new[] { "currency", "cur" },
        ["price_uom"]                = new[] { "price unit measure", "price uom", "price_uom" },
        ["supplier_leadtime_days"]   = new[] { "supplier manufacturing leadtime", "leadtime", "supplier_leadtime_days" },
        ["purch_uom"]                = new[] { "purch u/m", "purch uom", "purch_uom" },
        ["inventory_uom"]            = new[] { "inventory u/m", "inventory uom", "inventory_uom" },
        ["site"]                     = new[] { "site" },
        ["site_description"]         = new[] { "site description", "site_description" },
        ["status_code"]              = new[] { "status code", "status_code" },
        ["minimum_quantity"]         = new[] { "minimum quantity", "minimum_quantity" },
        ["std_multiple_qty"]         = new[] { "std multiple qty", "std_multiple_qty" },
        ["standard_pack_size"]       = new[] { "standard pack size", "standard_pack_size", "pack size" },
        ["conversion_factor"]        = new[] { "conversion factor", "conversion_factor" },
        ["tax_code"]                 = new[] { "tax code", "tax_code" },
        ["tax_code_description"]     = new[] { "tax code description", "tax_code_description" },
        ["country_of_origin"]        = new[] { "country of origin", "country_of_origin" },
        ["acquisition_type"]         = new[] { "acquisition type", "acquisition_type" },
        ["supplier_part_no"]         = new[] { "supplier part no", "supplier_part_no" },
        ["supplier_part_description"]= new[] { "supplier part description", "supplier_part_description" },
        ["net_weight"]               = new[] { "net weight", "net_weight" },
        ["net_weight_uom"]           = new[] { "net weight uom", "net_weight_uom" },
        ["next_order_date"]          = new[] { "next order date", "next_order_date" },
        ["notes"]                    = new[] { "notes" },
    };
    public RawMaterial? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap) { ... }
}
```

Engine `NpiImportService.ApplyAsync<RawMaterial>` đã sẵn sàng — không sửa.
Modal `NpiImportModal<TEntity>` đã generic — không sửa.

### 3.6 i18n keys (EN + VI parity)

Thêm `~37 keys` cho `npi.rawmaterials.*` mirror `npi.routine.*` + `npi.structure.*`:
- `npi.rawmaterials.title`, `.breadcrumb`, `.rows_loaded`, `.rows_count`, `.search_placeholder`
- `.btn_columns`, `.btn_show_all`, `.empty`, `.empty_filter`
- 28 keys `npi.rawmaterials.col.<key>`
- Status code badge classes giữ inline (CSS, không cần i18n)

`npi.import.*` keys đã share — không cần thêm.

---

## 4) Scope contract (vùng cấm)

Hạng mục 3 **KHÔNG** đụng:
- Ops Control v1.2 (sibling project — read-only)
- CMES sibling (read-only reference cho UI/UX)
- SpecHub sibling
- "Old ver" folder
- Tab khác của CCL-CMES (Routine / Structure / WorkCenter / Spec / IQC / Settings...)
- IQC entity + `Iqc.cs` (hybrid FK đến RawMaterial — kiểm chứng FK vẫn work sau migration)
- Library / PermissionGroups RBAC matrix (reuse Admin/Engineer cho NpiImport)
- Audit infrastructure (AuditAction.NpiImport, IAuditWriter — reuse)

Chỉ touch:
- `src/CCL.MES.Domain/Entities/Npi.cs` (RawMaterial entity — drop 5, add 21, Price nullable)
- `src/CCL.MES.Infrastructure/Migrations/<timestamp>_AddRawMaterialExtendedFields.{cs,Designer.cs}` (new)
- `src/CCL.MES.Infrastructure/MesDbContextModelSnapshot.cs` (regen)
- `src/CCL.MES.Application/Services/NpiImport/RawMaterialCsvTarget.cs` (new)
- `src/CCL.MES.Application/Services/NpiService.cs` (search filter expand)
- `src/CCL.MES.Web/Pages/Npi/RawMaterials.razor` (full rewrite)
- `src/CCL.MES.Web/Resources/SharedResource.{resx,vi.resx}` (~37 keys × 2)
- `src/CCL.MES.Web/wwwroot/css/site.css` (status badge inline styles)
- `tools/import_npi.py` (read_raw_materials + insert_raw_materials expand)
- `docs/PHASE7-RAWMATERIALS-PLAN.md` (this file)

---

## 5) Q-questions cần anh chốt

| Q# | Câu hỏi | Default em đề xuất | Lý do |
|---|---|---|---|
| **Q1** | Drop 5 field legacy `CatalogGroup/CatalogDesc/Grp/Type/TypeDesc`? | **YES, drop** | Importer comment xác nhận đây là proxy-mapping sai semantic; CMES schema có equivalent rõ ràng hơn (acquisition_type, site, status_code). IQC FK không ảnh hưởng. |
| **Q2** | Đổi `Price` từ `double` sang `double?`? | **YES** | Parity Routine + Structure; phân biệt 0 vs missing (raw mat catalog cũng có giá rỗng — IFS export sometimes blank). |
| **Q3** | Add 21 cột mới (full CMES parity)? | **YES** | Khớp khảo sát ban đầu "đồng bộ theo CMES". |
| **Q4** | Frozen first column `PartNo` (CMES có; Structure/Routine không)? | **YES** | 28 cột widescroll dễ lạc; PartNo là anchor primary key — sticky giúp operator scroll ngang vẫn thấy. |
| **Q5** | 14 cột mặc định hidden (mirror CMES) hay show all 28? | **mirror CMES** | Tránh information overload lần đầu; operator vẫn vào Columns popover bật được. Hidden defaults: price_incl_tax, site_description, minimum_quantity, std_multiple_qty, standard_pack_size, conversion_factor, tax_code, tax_code_description, country_of_origin, acquisition_type, supplier_part_no, supplier_part_description, net_weight, net_weight_uom, next_order_date, notes (16 hidden → 12 visible đầu). |
| **Q6** | Import file format wizard nhận gì? IFS Raw Materials.xlsx là XLSX (không phải CSV). | **(a) CSV only — operator pre-convert** | Giữ NpiCsvParser pure (không thêm xlsx dep vào Application layer). Operator có thể "Save As .csv" trong Excel. Trade-off: 1 step thêm cho operator. Alternative (b) extend parser nhận cả .xlsx — phức tạp hơn + cần ClosedXML/NPOI dep. |
| **Q7** | Search expand thêm `StatusCode + CountryOfOrigin`? | **YES** | Khớp CMES; status code rất hữu ích cho operator lọc "active vs obsolete". |
| **Q8** | Re-import data sau migration để hydrate 21 field mới + replace 5 legacy bằng actual fields? | **YES** | Migration chỉ DROP 5 + ADD COLUMN NULL; data hiện tại 10/12 field còn lại stale (vẫn dùng được nhưng Acquisition Type rỗng cho hàng cũ). Re-import từ xlsx gốc cho data fresh. |
| **Q9** | PR strategy — gộp 1 PR? | **YES, 1 PR** | Đồng pattern hạng mục 2; infrastructure đã có. |

---

## 6) Sau khi anh chốt — em sẽ:

1. Tạo branch `feat/phase7-raw-materials` base `main`
2. A→B→C SAFE migration (backup SHA256 → /tmp test → live apply → verify 2,127 + IQC FK still works)
3. Code entity + RawMaterialCsvTarget + UI + importer + i18n + status badge CSS
4. `dotnet build` clean
5. Smoke test trên `/tmp` DB:
   - Re-import xlsx → 2,127 row, 28 field populate đúng
   - IQC service.SnapshotAsync vẫn lookup PartNo → SupplierName OK
6. Mở PR, **STOP chờ anh review + merge**
7. Sau merge → lặp tương tự cho hạng mục 4 Spec Master, rồi hạng mục 5 Machine List (WorkCenter)

---

**STOP — chờ anh duyệt Q1–Q9 + xác nhận hard constraints.**
