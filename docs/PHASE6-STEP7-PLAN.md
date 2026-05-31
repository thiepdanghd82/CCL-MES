# Phase 6 — Bước 7: IQC entity + tab (đóng stub Bước 3) — KHẢO SÁT + PHƯƠNG ÁN

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa branch.
>
> **Bối cảnh (chốt từ Bước 3)**: IQC = **Incoming Quality Check** —
> kiểm chất lượng nguyên liệu khi nhập kho, gắn **raw-material batch**
> chứ KHÔNG phải WorkOrder → cần entity riêng, không nhét vào `QcType`
> enum hay reuse `QcInspection`.
>
> Bước này thêm entity mới + migration v4 (chạm schema) → khảo sát + báo
> cáo phương án trước. Bonus: encode lesson EF migrations safety (rút từ
> sự cố Bước 6.5) ngay trong PR này để không tái phạm.
>
> Sau khi anh chốt → 1 PR `feat/phase6-iqc` stack trên
> `feat/phase6-deploy-sqlite-and-sqlserver-gate` (PR #16).
>
> **Không đụng**: `Ops Control v1.2/`, `CMES/`, `Old ver ( DO NOT USE)/`,
> `SpecHub/`.

---

## 1. Khảo sát source — vì sao IQC KHÔNG thể reuse QcInspection

### 1.1 QcInspection entity

[src/CCL.MES.Domain/Entities/Qc.cs:3-14](src/CCL.MES.Domain/Entities/Qc.cs#L3-L14):

```csharp
public class QcInspection : BaseEntity
{
    public long WorkOrderId { get; set; }      // ← REQUIRED, non-nullable
    public WorkOrder? WorkOrder { get; set; }
    public QcType Type { get; set; }            // ← IPQC | FQC | OQC
    public QcResult Result { get; set; } = QcResult.Pending;
    public string? InspectorId { get; set; }
    public int SampleSize { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<QcResultDetail> Details { get; set; } = new();
}
```

**Lý do KHÔNG thể reuse**:

1. `WorkOrderId` là khoá ngoại **bắt buộc** (`long`, non-nullable). IQC chạy
   TRƯỚC khi có WO (nguyên liệu mới về kho, chưa biết WO nào sẽ dùng).
   Cố nhét sẽ phải invent fake WO ID hoặc đổi sang nullable → schema migration
   xảy ra, ảnh hưởng tới 56 row WO hiện có.
2. `QcType` enum chỉ có 3 value `IPQC/FQC/OQC` ([Enums.cs:18](src/CCL.MES.Domain/Enums.cs#L18)).
   Thêm `IQC` value ngay được, nhưng cascade ràng buộc với WorkOrderId vẫn vướng.
3. `QcService.ApproveAsync` ([:69-71](src/CCL.MES.Application/Services/QcService.cs#L69-L71))
   khi Fail sẽ set `WorkOrder.Status = OnHold` — semantically wrong cho IQC
   (raw mat fail → quarantine raw mat, không phải hold WO).

**Quyết định**: **separate entity `IqcInspection`** (option B trong cây quyết định).

### 1.2 RawMaterial entity

[src/CCL.MES.Domain/Entities/Npi.cs:11-26](src/CCL.MES.Domain/Entities/Npi.cs#L11-L26):

```csharp
public class RawMaterial : BaseEntity
{
    public string PartNo { get; set; } = "";
    public string? PartDescription { get; set; }
    public string? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public double Price { get; set; }
    // ... Currency, PriceUom, CatalogGroup, CatalogDesc, Grp, Type, TypeDesc
}
```

- Đây là **catalog cấp IFS** (master list 2127 row) — `PartNo` là mã IFS
  part, **không phải batch/lot**.
- KHÔNG có field `BatchNumber` / `LotNumber` / `ReceivedDate` — vì 1 part
  nhập nhiều lô khác nhau, IFS Raw Material là catalog mô tả.
- IQC cần track **mỗi lần nhập** = part + batch + ngày + supplier snapshot
  + qty + UOM.

**Quyết định**: IqcInspection có field riêng cho batch info, **không**
thêm vào RawMaterial.

### 1.3 Iqc.razor + Ipqc.razor + Oqc.razor — stub hiện tại

3 file giống nhau (Phase 6 Bước 3 placeholder):

```razor
@page "/qcqa/iqc"
@attribute [Authorize(Policy = "QcRead")]
<h1>@Loc["qcqa.iqc.title"]</h1>
<div class="npi-placeholder">
    <p class="muted">@Loc["qcqa.iqc.placeholder_lead"]</p>
    <ul>
        <li>@Loc["qcqa.iqc.placeholder_li1"]</li>
        <li>@Loc["qcqa.iqc.placeholder_li2"]</li>
        <li>@Loc["qcqa.iqc.placeholder_li3"]</li>
    </ul>
</div>
```

- Route `/qcqa/iqc`, RBAC `QcRead` (Admin/Supervisor/QC) — keep.
- i18n prefix `qcqa.iqc.*` đã có 4 stub key — sẽ MỞ RỘNG cho UI thật.

**Bước 7 scope**: chỉ đóng Iqc stub. Ipqc + Oqc stub vẫn để placeholder vì:
- IPQC + OQC dùng QcInspection sẵn có (đã có service + entity Phase 6 Bước 5)
- Chỉ cần wire UI lên — defer sang Phase 7 (không phải Phase 6 close-out scope)

### 1.4 MesDbContext

[src/CCL.MES.Infrastructure/MesDbContext.cs:11-30](src/CCL.MES.Infrastructure/MesDbContext.cs#L11-L30) có 20 DbSet. Bước 7 sẽ thêm 2:
- `DbSet<IqcInspection> IqcInspections`
- `DbSet<IqcResultDetail> IqcResultDetails`

+ `OnModelCreating` thêm: HasConversion<string>() cho IqcInspection.Result, index theo PartNo + BatchNumber + ReceivedDate.

### 1.5 AuditAction enum (const string class)

[src/CCL.MES.Domain/Audit/AuditAction.cs](src/CCL.MES.Domain/Audit/AuditAction.cs) alphabetical, 19 codes. Thêm 2 codes mới giữa BackupRestore và LoginDisabled:
- `IqcApprove = "IQC_APPROVE"` (pass/fail in Detail)
- `IqcCreate = "IQC_CREATE"`

---

## 2. Phương án schema IqcInspection

### 2.1 IqcInspection entity (đề xuất shape)

```csharp
public class IqcInspection : BaseEntity
{
    // Liên kết về RawMaterial — see Q1 cho phương án FK
    public long? RawMaterialId { get; set; }      // optional hard FK (hybrid)
    public RawMaterial? RawMaterial { get; set; }
    public string PartNo { get; set; } = "";      // snapshot (always populated)

    // Batch + nhập kho
    public string BatchNumber { get; set; } = ""; // required, supplier batch/lot
    public string? LotNumber { get; set; }        // optional sub-batch
    public DateTime ReceivedDate { get; set; }    // when material arrived
    public string? SupplierName { get; set; }     // snapshot tại thời điểm nhập
    public double Quantity { get; set; }
    public string? UomQty { get; set; }

    // Inspection
    public string? InspectorId { get; set; }
    public int SampleSize { get; set; }
    public QcResult Result { get; set; } = QcResult.Pending;  // reuse enum
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Details (như QcResultDetail nhưng table riêng)
    public List<IqcResultDetail> Details { get; set; } = new();
}

public class IqcResultDetail : BaseEntity
{
    public long IqcInspectionId { get; set; }
    public string ItemName { get; set; } = "";
    public string? MeasuredValue { get; set; }
    public bool Pass { get; set; }
    public string? DefectCode { get; set; }
    public int Qty { get; set; }
}
```

### 2.2 Indexes đề xuất

- `IqcInspections.PartNo` — tra cứu inspection theo part
- `IqcInspections.BatchNumber` — tra cứu theo lô (operator gõ batch số)
- `IqcInspections.ReceivedDate DESC` — sort timeline
- `IqcResultDetails.IqcInspectionId` (auto từ FK)

### 2.3 Quan hệ FK với RawMaterial — 3 option

| Option | Mô tả | Ưu | Nhược |
|---|---|---|---|
| **2.3.A. Hard FK only** | `long RawMaterialId` non-nullable + remove `string PartNo` field (lấy qua join) | Strict referential integrity. Join hiển thị PartDescription/Supplier dễ. | RawMaterial.PartNo trong catalog có thể không cover hết — operator nhập 1 batch của part không có trong catalog → fail. Phải seed RM catalog trước. |
| **2.3.B. Soft text only** | Bỏ FK, chỉ giữ `string PartNo` text | Linh hoạt, không phụ thuộc seed. | Mất referential integrity. Typo PartNo trên IQC row → không match. UI phải hiển thị PartDescription bằng cách query 2 lần. |
| **2.3.C. Hybrid (em đề xuất)** | `long? RawMaterialId` nullable optional FK + `string PartNo` snapshot bắt buộc | Best of both: nếu RM tồn tại → set FK + lookup nhanh; nếu không → giữ text. PartNo snapshot tránh dirty-read khi catalog rename. | Hơi cruft schema (2 trường overlap). Logic create cần resolve PartNo → RawMaterialId. |

**Em đề xuất 2.3.C (hybrid)** — match real-world: operator gõ PartNo, app
auto-lookup RawMaterial. Nếu thấy → set FK; nếu không → bỏ FK, vẫn save.
Hiển thị join khi có FK, fallback text khi không.

### 2.4 IqcResultDetail — riêng vs reuse QcResultDetail

| Option | Mô tả | Ưu | Nhược |
|---|---|---|---|
| **2.4.A. Separate IqcResultDetail** | Table mới mirror QcResultDetail | Clean schema, FK đơn nghĩa. | 1 entity + 1 table thêm. |
| **2.4.B. Reuse QcResultDetail (polymorphic)** | Thêm `IqcInspectionId?` nullable vào QcResultDetail, 1 trong 2 FK luôn có giá trị | Tiết kiệm 1 table. | Schema cruft: 2 FK nullable, không check constraint native. Query phức tạp. |

**Em đề xuất 2.4.A (separate)** — match Clean Architecture, schema sạch.

### 2.5 QcResult enum — reuse vs IqcResult riêng

| Option | Mô tả | Em đề xuất |
|---|---|---|
| **2.5.A. Reuse QcResult (Pending/Pass/Fail)** | IqcInspection.Result dùng chung enum | ✅ — semantically identical, không phải migrate enum khi sau muốn merge |
| **2.5.B. New IqcResult** | Enum mới với Pending/Accept/Reject/Quarantine | ❌ — over-engineering. "Quarantine" là post-Fail action, không phải state IQC |

---

## 3. Phương án service + UI

### 3.1 IqcService (mirror QcService pattern)

```csharp
public class IqcService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public async Task<IqcInspection> CreateAsync(CreateIqcRequest r, string actor)
    {
        // 1. Resolve PartNo → RawMaterialId (optional)
        // 2. Snapshot SupplierName từ RawMaterial nếu match
        // 3. Add IqcInspection + Details
        // 4. SaveChanges
        // 5. Emit IQC_CREATE audit
    }

    public async Task<IqcInspection?> ApproveAsync(long id, bool pass, string actor)
    {
        // 1. Load + set Result
        // 2. Emit IQC_APPROVE audit (Detail = {part_no, batch_number, result})
        // 3. KHÔNG cascade WorkOrder (khác QcService — IQC pre-WO)
    }

    public async Task<PagedResult<IqcInspection>> ListAsync(
        string? search, QcResult? status, DateTime? from, DateTime? to,
        int page, int pageSize)
    {
        // EF.Functions.Like trên PartNo/BatchNumber/SupplierName
        // PagingHelper.PageAsync — same pattern as AuditLogService
    }
}
```

### 3.2 Iqc.razor — UI thay stub

3 section:
1. **Toolbar**: search box (PartNo/Batch/Supplier) + Status dropdown
   (All/Pending/Pass/Fail) + 2 date inputs (from/to) + **New IQC** button
2. **Table** (8 col): Received | PartNo | Batch | Supplier | Qty | Sample | Result (badge) | Actions
3. **Pager** (PagingHelper).

Actions per row:
- **View Detail** → drawer/modal (hiển thị Details list — items + measured + pass/fail flag)
- **Approve Pass** + **Approve Fail** → confirm modal → service call → audit emit

Create New modal:
- PartNo (autocomplete từ RawMaterial.PartNo top N matches)
- BatchNumber (text)
- LotNumber (optional)
- ReceivedDate (date picker, default today UTC)
- Quantity + UomQty
- SupplierName (auto-fill khi PartNo match RM)
- InspectorId (auto-fill user.Username)
- SampleSize
- Details inline (Item Name + Measured + Pass/Fail + Qty + Defect Code) — add row button

### 3.3 RBAC

| Action | Policy/Role | Lý do |
|---|---|---|
| GET /qcqa/iqc (page view + list) | `[Authorize(Policy = "QcRead")]` | Same as IPQC/OQC — Admin/Supervisor/QC |
| Create IQC | Role check inline: Admin/Supervisor/Qc | Engineer + Operator không tạo IQC |
| Approve IQC | Role check inline: Admin/Supervisor/Qc | Same as Create |

`<AuthorizeView Roles="Admin,Supervisor,QC">` quanh New + Approve buttons,
+ server-side check trong IqcService.

---

## 4. Migration v4 — AddIqcInspection — A→B→C SAFE

### 4.1 Pattern an toàn (rút từ Bước 6.5)

**TUYỆT ĐỐI KHÔNG**:
- `dotnet ef migrations remove` (tự revert migration cuối trên live DB qua Down())
- `dotnet ef migrations add` mà không set `MES_CONNSTR` → connect tới live DB

**PHẢI**:
- Set `MES_CONNSTR=Data Source=/tmp/iqc-design.db` khi `ef migrations add`
  → design-time only, không chạm live DB
- Manual `rm` migration file + `git checkout MesDbContextModelSnapshot.cs`
  nếu cần undo (không dùng `ef migrations remove`)

### 4.2 Quy trình A→B→C

**Phase A (backup tường minh)**:

```bash
TS=$(date -u +%Y%m%d-%H%M%S)
cp data/ccl_mes.db /tmp/ccl_mes.db.before-step7.$TS
shasum -a 256 data/ccl_mes.db
sqlite3 data/ccl_mes.db "SELECT COUNT(*) FROM WorkCenters; ..."  # baseline rowcounts
```

**Phase B (generate migration trên isolated DB)**:

```bash
# Save snapshot before generate
cp src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs \
   /tmp/snapshot-pre-iqc.cs

# Generate migration POINTED AT ISOLATED /tmp DB (key safety move)
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/iqc-design.db" \
  dotnet ef migrations add AddIqcInspection \
  -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations --no-build

# Verify generated migration
cat src/CCL.MES.Infrastructure/Migrations/*AddIqcInspection.cs
# - CreateTable IqcInspections (12 cols + indexes)
# - CreateTable IqcResultDetails (8 cols + FK)
# - 3 CreateIndex
# - Down: 2 DropTable (clean reverse)
```

**Phase C (apply qua DbInitializer khi boot app)**:

```bash
# Boot — DbInitializer.Migrate() áp dụng AddIqcInspection lên live DB
ASPNETCORE_URLS="http://0.0.0.0:5050" \
  dotnet run --project src/CCL.MES.Web --no-launch-profile > /tmp/boot.log 2>&1 &
sleep 7

# Verify
sqlite3 data/ccl_mes.db "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'Iqc%';"
# Expect: IqcInspections, IqcResultDetails

sqlite3 data/ccl_mes.db "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"
# Expect: 4 entries (3 cũ + AddIqcInspection)

# Row counts unchanged
sqlite3 data/ccl_mes.db "SELECT 'WC',COUNT(*) FROM WorkCenters UNION ALL ..."
# 43 / 2127 / 38441 / 20530 / 5 / 1+ (audit rows)
```

### 4.3 Rollback nếu fail giữa chừng

Nếu sau Phase B verify migration file SAI shape:
1. **KHÔNG** dùng `ef migrations remove`
2. Manual:
   ```bash
   rm src/CCL.MES.Infrastructure/Migrations/*AddIqcInspection*
   cp /tmp/snapshot-pre-iqc.cs \
      src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
   ```
3. Fix entity code → quay lại Phase B.

Nếu sau Phase C apply, dữ liệu live có vấn đề:
- Restore từ `/tmp/ccl_mes.db.before-step7.$TS` qua `cp` + verify SHA.
- Remove migration files + snapshot restore như trên.

---

## 5. Encode lesson EF migrations safety (mục tiêu phụ Bước 7)

### 5.1 Lesson cần encode

> **TRÁNH `dotnet ef migrations remove`** và **TRÁNH `dotnet ef migrations
> add` trỏ vào live DB** — cả 2 tool có thể tự động Down()/sync trên DB
> đang phục vụ + xoá file local.
>
> Pattern an toàn:
> - Set `MES_CONNSTR=Data Source=/tmp/isolated.db` (HOẶC env tương đương)
>   trước mọi lệnh design-time
> - Verify nội dung migration file (`.cs`) bằng `cat`, không apply
> - Undo bằng manual `rm` migration files + `git checkout
>   MesDbContextModelSnapshot.cs` — KHÔNG dùng `ef migrations remove`
>
> Bài học rút từ sự cố Bước 6.5 (2026-05-31): `ef migrations remove` đã
> connect live SQLite + revert AddAuditLog migration → DROP TABLE
> AuditLogs + xoá 1 row __EFMigrationsHistory. Phải restore từ Phase A
> backup byte-identical để recovery. SHA `04545cc5...` đã chứng minh
> data nguyên vẹn 100% post-recovery.

### 5.2 Vị trí encode

3 lựa chọn:

| Option | Mô tả | Em đề xuất |
|---|---|---|
| **5.2.A. Append `docs/LESSONS_LEARNED.md` §7** | Hiện đã có §6 "Bài học bổ sung (đợt 2)". Thêm §7 "Bài học bổ sung (đợt 3) — Bước 6.5+7 EF Core safety". | ✅ — consistent với existing convention, không tạo file mới |
| **5.2.B. Tạo `CLAUDE.md` ở repo root** | Mirror Ops Control v1.2 — single agent-facing playbook. | ❌ — file mới + duplicate với LESSONS_LEARNED hiện có |
| **5.2.C. Thêm vào docs/PHASE6-STEP6.5-PLAN.md "Lesson" section** | Treo dưới survey doc đã ship | ❌ — survey doc archived, không phải runbook |

**Em đề xuất 5.2.A**. Anh duyệt → em append.

---

## 6. i18n — keys cần thêm

Hiện có 4 stub keys (`qcqa.iqc.title`, `placeholder_lead`, `placeholder_li1-3`).
Bước 7 thêm ~28 keys/locale × 2 locale = 56 entries:

```
qcqa.iqc.title                    # giữ "Incoming QC" / "IQC nguyên liệu"
qcqa.iqc.search_placeholder       # "Search part / batch / supplier..."
qcqa.iqc.btn.new                  # "New IQC" / "Tạo phiếu IQC"
qcqa.iqc.status_all               # "All statuses"
qcqa.iqc.status_pending           # "Pending"  (= QcResult.Pending)
qcqa.iqc.status_pass              # "Pass"
qcqa.iqc.status_fail              # "Fail"
qcqa.iqc.col.received             # "Received"
qcqa.iqc.col.part_no              # "Part No"
qcqa.iqc.col.batch                # "Batch"
qcqa.iqc.col.supplier             # "Supplier"
qcqa.iqc.col.qty                  # "Qty"
qcqa.iqc.col.sample_size          # "Sample Size"
qcqa.iqc.col.result               # "Result"
qcqa.iqc.col.actions              # "Actions"
qcqa.iqc.btn.view                 # "View detail"
qcqa.iqc.btn.approve_pass         # "Approve Pass"
qcqa.iqc.btn.approve_fail         # "Approve Fail"
qcqa.iqc.form.part_no             # "Part No (Raw Material)"
qcqa.iqc.form.batch               # "Batch Number"
qcqa.iqc.form.lot                 # "Lot Number (optional)"
qcqa.iqc.form.received_date       # "Received date (UTC)"
qcqa.iqc.form.qty                 # "Quantity"
qcqa.iqc.form.uom                 # "UOM"
qcqa.iqc.form.supplier            # "Supplier (snapshot)"
qcqa.iqc.form.inspector           # "Inspector"
qcqa.iqc.form.sample_size         # "Sample Size"
qcqa.iqc.form.add_detail          # "+ Add result detail"
qcqa.iqc.detail.item_name         # "Item name"
qcqa.iqc.detail.measured          # "Measured value"
qcqa.iqc.detail.pass              # "Pass?"
qcqa.iqc.detail.qty               # "Qty"
qcqa.iqc.detail.defect_code       # "Defect code (if fail)"
qcqa.iqc.msg.created              # "IQC created: {0}"
qcqa.iqc.msg.approved             # "IQC {0}: {1}"
qcqa.iqc.list.empty               # "No IQC inspections yet."
```

Bỏ 3 stub `placeholder_*` (không còn dùng).

---

## 7. Sample seed data — yes/no?

| Option | Mô tả |
|---|---|
| **7.A. Không seed** | UI lần đầu trống, operator tạo từ đầu |
| **7.B. Seed 3 demo** | 1 Pending (đợi approve), 1 Pass, 1 Fail — UI testing nhanh, demo screenshot dễ |

Em đề xuất **7.B** — DbSeeder idempotent (chỉ insert nếu `IqcInspections.Count == 0`), match pattern Brady Asia / BRD-7656-D seed hiện có.

---

## 8. Phạm vi PR đề xuất

### Phạm vi IN (Bước 7 PR #17)

| Sub-step | Mô tả | LOC ước |
|---|---|---|
| 7.1 | `Iqc.cs` entity + `IqcResultDetail` entity + `MesDbContext` DbSet | ~60 |
| 7.2 | `IqcService.cs` (Create + Approve + ListAsync via PagingHelper) | ~150 |
| 7.3 | `AuditAction.cs` — thêm IqcCreate + IqcApprove | ~4 |
| 7.4 | Migration v4 `AddIqcInspection` qua isolated /tmp pattern | ~100 LOC generated |
| 7.5 | `Iqc.razor` — thay stub bằng grid + toolbar + create modal + approve | ~400 |
| 7.6 | DbSeeder — 3 demo IQC inspections idempotent | ~50 |
| 7.7 | i18n EN + VI — ~28 keys × 2 | ~60 entries |
| 7.8 | RBAC — `AuthorizeView` + server-side check trong IqcService | ~20 |
| 7.9 | LESSONS_LEARNED.md §7 append (EF migration safety) | ~30 |
| 7.10 | Update README §3 nếu cần (nhắc tới /qcqa/iqc) | ~5 |

**Tổng**: ~900 LOC, 1 PR.

### Phạm vi OUT (defer)

- IPQC + OQC stub đóng — defer Phase 7 (cần wire QcService vào UI, song song với batch operator-flow design)
- IQC dashboard analytics (Pass rate %, top defect codes) — defer Phase 7
- Email/notification on FAIL — out of scope
- RawMaterial.IsQuarantined flag (auto-quarantine khi IQC fail) — defer
- IQC export CSV/PDF — defer Phase 7

---

## 9. Rủi ro + mitigation

| ID | Rủi ro | Severity | Mitigation |
|---|---|---|---|
| R1 | Migration generate sai shape (vd Down() drop quá nhiều) | Medium | Phase B verify file content bằng `cat`. Block nếu Down() drops bảng ngoài IqcInspections + IqcResultDetails. |
| R2 | `ef migrations add` tái phạm Bước 6.5 incident (chạm live DB) | **Critical** | **MES_CONNSTR isolated /tmp/iqc-design.db** bắt buộc. Lesson 5.1 encode vào LESSONS_LEARNED.md. |
| R3 | RawMaterial autocomplete làm chậm UI nếu query toàn bộ 2127 row | Low | Server-side `EF.Functions.Like` + top 10 result + index PartNo đã có. |
| R4 | Audit Detail JSON leak Personal data (InspectorId là username) | Low | Username = identifier không phải PII; tương tự QC_CREATE pattern hiện có. |
| R5 | DbSeeder demo IQC overwrite tay operator-created IQC | Medium | Idempotent: skip nếu `IqcInspections.Any()` (kiểm tra `Any()`, không kiểm tra theo BatchNumber để tránh re-seed nhầm). |
| R6 | i18n key bị missing → Loc[key] trả raw key | Low | Visual check sau khi build qua self-test EN+VI flow. |
| R7 | RBAC bypass — Engineer/Operator gọi POST IqcService.CreateAsync | Medium | Server-side check role trong IqcService + `[ValidateAntiForgeryToken]` trên route. |
| R8 | Mất data nếu Phase C apply migration fail giữa chừng | **Critical** | Phase A backup tường minh. EF migration là transactional với SQLite (BEGIN; ... COMMIT;) — nếu DDL fail → rollback. Vẫn restore từ Phase A nếu cần. |

---

## 10. Branch + câu hỏi cần anh quyết

### 10.1 Branch base

`feat/phase6-iqc` stack trên `feat/phase6-deploy-sqlite-and-sqlserver-gate` (PR #16 đang open).

Khi PR #16 merge → tự rebase. Nếu PR #16 chưa merge khi Bước 7 ship → PR #17 stack tiếp tục, mention dependency trong description.

### 10.2 Câu hỏi cần anh quyết (12 mục)

| Q | Câu hỏi | Em đề xuất |
|---|---|---|
| **Q1** | IQC ↔ RawMaterial FK: 2.3.A hard / 2.3.B soft text / 2.3.C hybrid (nullable FK + PartNo snapshot)? | **2.3.C hybrid** — best of both, lookup nhanh khi catalog match + linh hoạt khi không |
| **Q2** | IqcResultDetail riêng (2.4.A) hay reuse QcResultDetail polymorphic (2.4.B)? | **2.4.A separate** — schema sạch |
| **Q3** | QcResult enum: reuse Pending/Pass/Fail (2.5.A) hay IqcResult mới (2.5.B)? | **2.5.A reuse** — semantically identical |
| **Q4** | IQC FAIL behavior — chỉ record + audit, OR set RawMaterial.IsQuarantined=true? | **chỉ record + audit** — defer auto-quarantine sang Phase 7. Operator vẫn thấy Fail trong list để quyết action ngoài app. |
| **Q5** | Create UI — 1 modal đầy đủ (form + Details inline) HOẶC 2-step wizard (header → details)? | **1 modal** — gọn cho operator |
| **Q6** | AuditAction naming — IqcCreate/IqcApprove (camelCase) → const `IQC_CREATE`/`IQC_APPROVE`? | **Yes** — consistent với QC_CREATE / QC_APPROVE đã có |
| **Q7** | Migration filename — `AddIqcInspection`? | **Yes** |
| **Q8** | i18n prefix — giữ `qcqa.iqc.*` (đã có 4 stub) hay đổi `inspection.iqc.*`? | **Giữ `qcqa.iqc.*`** — backward-compat, đỡ phải sửa Iqc.razor route + nav menu |
| **Q9** | Encode lesson EF migration safety — append `docs/LESSONS_LEARNED.md` §7 (5.2.A) / tạo `CLAUDE.md` root (5.2.B) / patch survey doc (5.2.C)? | **5.2.A append LESSONS_LEARNED.md** — consistent convention |
| **Q10** | Seed data — 3 demo IQC inspections idempotent (7.B)? | **Yes** — pattern Brady Asia / BRD-7656-D đã làm |
| **Q11** | Search/filter MVP scope — 3 fields (PartNo + status + date) hay thêm Inspector/Supplier? | **3 fields** — defer extras Phase 7 |
| **Q12** | Migration verify approach — chỉ inspect file content, OR cũng apply trên isolated /tmp DB rồi `sqlite3 .schema` verify? | **Cả 2**: inspect file content + apply trên `/tmp/iqc-design.db` + `.schema Iqc*` so với spec |

---

## 11. Sau Bước 7 — Phase 6 CLOSE-OUT (out of Bước 7 scope, but logged)

User đã chốt: sau Bước 7 duyệt → close-out Phase 6. Em sẽ:

1. **Merge tuần tự PR chain** vào main (#10→#11→#12→#13→#14→#15→#16→#17).
   - Squash hay merge commit — anh chốt trong Bước 7 duyệt (em đề xuất merge
     commit để giữ stacked history sạch).
   - Conflict → dừng báo, hỏi anh resolve.

2. **Cleanup carry-over**:
   - `SpecService` local PageAsync → `PagingHelper.PageAsync` (Bước 1 carry-over)
   - Cleanup stale `_message` references nếu còn

3. **Verify post-merge**:
   - `dotnet build` clean
   - Restart app + boot probe
   - SHA256 + row counts unchanged on main

4. **Documentation**:
   - `docs/PHASE6-REPORT-2026-05-31.md` — wrap-up report (mirror Phase 5)
   - Update `docs/MINDMAP.md` Phase 6 nodes
   - Update `README.md` if needed

5. **Verify forbidden dirs** intact — `git diff main..HEAD --name-only | grep -E
   "Ops Control v1\.2|^CMES/|Old ver|SpecHub"` = empty

6. **Push main**.

---

## 12. STOP — chờ phương án

Khảo sát xong. Doc **untracked**, chưa code, chưa branch.

Em chờ anh chốt **Q1–Q12** rồi triển khai 1 PR `feat/phase6-iqc` stack trên
`feat/phase6-deploy-sqlite-and-sqlserver-gate`, 10 sub-step 7.1→7.10 + A→B→C
SAFE migration v4 + lesson encode.

**Không đụng**: `Ops Control v1.2/`, `CMES/`, `Old ver ( DO NOT USE)/`, `SpecHub/`.
