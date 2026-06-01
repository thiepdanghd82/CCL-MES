# Lessons Learned — Dự án CCL-MES (MVP)

Tài liệu này ghi lại những bài học rút ra trong quá trình thiết kế & dựng khung MVP hệ thống MES cho nhà máy CCL Design. Cập nhật dần theo từng giai đoạn.

---

## 1. Bối cảnh & quyết định kiến trúc

| Quyết định | Lý do | Bài học |
|---|---|---|
| Clean Architecture (Domain / Application / Infrastructure / Web) | Tách nghiệp vụ khỏi framework, dễ test, dễ thay DB | Đáng giá ngay cả với MVP — khi đổi SQLite → SQL Server chỉ sửa 1 dòng ở tầng Infrastructure |
| SQLite cho dev, SQL Server cho prod | Chạy được ngay bằng `dotnet run`, không cần cài DB server | EF Core viết chuẩn provider-agnostic thì chuyển đổi gần như miễn phí |
| State Machine cho Work Order | Luật chuyển 7 bước tập trung 1 chỗ, có guard | Không rải logic "if status ==" khắp nơi; mọi thay đổi quy trình chỉ sửa `WorkOrderStateMachine` |
| Lưu enum dạng string trong DB | Đọc DB dễ hiểu (PrePressCheck thay vì 1) | Phải khai báo `.HasConversion<string>()` cho TẤT CẢ enum, nếu quên sẽ lưu số |

## 2. Bài học kỹ thuật (.NET / EF Core)

- **Target framework phải khớp SDK của máy chạy.** Máy CCL dùng .NET 10 (SDK 10.0.300) nên phải đổi 4 file `.csproj` từ `net8.0` → `net10.0` và version package EF/Extensions tương ứng (`10.0.0`). Nếu để net8.0 mà máy không có runtime 8 sẽ lỗi *"framework 'Microsoft.NETCore.App' version '8.0.0' was not found"*.
- **Computed property không được map vào DB.** `WorkOrder.LastQc(...)` (method) và `ProductionLog.DurationMinutes` (get-only) phải `Ignore(...)` trong `OnModelCreating`, nếu không EF cố tạo cột và build/migrate lỗi.
- **`EnsureCreated()` chỉ hợp cho demo.** Nó tạo schema 1 lần, KHÔNG hỗ trợ thay đổi schema sau này. Sang production phải chuyển sang **EF Core Migrations** (`dotnet ef migrations add`, `database update`).
- **Tránh vòng lặp JSON khi serialize entity có quan hệ 2 chiều** (WorkOrder ↔ QcInspection). Đã bật `ReferenceHandler.IgnoreCycles` + `JsonStringEnumConverter` trong `Program.cs`.
- **`cd` đúng thư mục trước khi `dotnet run`.** Lỗi `MSB1003: Specify a project or solution file` chỉ vì đang đứng ở `~`. Luôn `cd` vào thư mục chứa `.sln`.
- **Comment `#` dán cùng dòng lệnh trong zsh** gây lỗi `unknown file attribute: h`. Khi hướng dẫn lệnh, để comment ở dòng riêng.

## 3. Bài học về OEE

- **Định nghĩa "Planned time" phải rõ ràng.** Trong model này `Planned = Run + Stop + Setup`. Nếu muốn khớp ví dụ chuẩn ngành (Vorne) thì loại break ra khỏi planned.
- **Performance phải chặn trần 100%.** Do sai số đo cycle-time, `idealMin/runMin` có thể > 1; dùng `Math.Min(1.0, ...)`.
- **Đã kiểm chứng công thức** khớp ví dụ chuẩn ngành: Availability 88.8% × Performance 86.1% × Quality 97.8% = **OEE 74.8%**. Luôn viết một test đối chiếu số trước khi tin vào công thức.

## 4. Bài học quy trình làm việc

- **Hỏi rõ phạm vi trước khi code** (CSDL dev, phạm vi UI, module nào) giúp không làm thừa.
- **Dựng MVP chạy được rồi mới mở rộng.** Bản đầu chỉ WO+Spec+QC; sau khi user chạy OK mới thêm OEE/WI/Dashboard.
- **Seed dữ liệu mẫu sát thực tế** (Brady Asia, BRD-7656-D, WO-26-3683, máy ACNC3) giúp demo trực quan và phát hiện lỗi sớm.

## 5. Việc cần làm tiếp (carry-over)

- [x] Chuyển `EnsureCreated()` → EF Migrations + cấu hình SQL Server. *(provider switch qua `Database:Provider`; SqlServer dùng Migrate, Sqlite dùng EnsureCreated)*
- [x] SignalR realtime cho Dashboard. *(ShopfloorHub + ShopfloorNotifier; Dashboard & WO tự cập nhật)*
- [x] Bộ công cụ Python `tools/` (verify OEE, OEE từ CSV, ETL Excel→DB).
- [ ] Thêm xác thực & phân quyền (RBAC / Entra ID).
- [ ] Tích hợp SAP (đơn hàng, vật tư, costing) và Warehouse.
- [ ] Thu thập OEE tự động từ PLC (OPC-UA/Modbus) thay vì bấm tay.
- [ ] Unit test cho `WorkOrderStateMachine` và `OeeService`.

## 6. Bài học bổ sung (đợt 2)

- **EF migrations là provider-specific.** Migration sinh cho SQL Server không chạy được trên SQLite. Giải pháp: SQLite (dev) dùng `EnsureCreated()`, SQL Server (prod) dùng `Migrate()` — chọn theo `Database:Provider`. Cần `IDesignTimeDbContextFactory` để `dotnet ef` chạy được ngoài runtime web.
- **Blazor Server vẫn cần HubConnection client riêng** để nhận broadcast realtime giữa các phiên (circuit). Pattern: service `ShopfloorNotifier` (singleton, bọc `IHubContext`) phát sự kiện; mỗi trang tạo `HubConnection` tới `/hubs/shopfloor` và `On(...)` để reload.
- **Nhớ `IAsyncDisposable`** trên component Blazor có `HubConnection` để giải phóng kết nối khi rời trang.

## 7. Bài học bổ sung (đợt 3) — Phase 6 Bước 6.5/7 EF Core safety

### 7.1 ⚠ KHÔNG dùng `dotnet ef migrations remove` trên repo có live DB

`dotnet ef migrations remove` **tự động connect tới live DB** và áp dụng
`Down()` của migration cuối để revert schema THẬT, không chỉ xoá file
`.cs` local như thường tưởng.

**Sự cố Bước 6.5 (2026-05-31)**: chạy `dotnet ef migrations remove` để
dọn dẹp một migration verify đã add → tool revert `AddAuditLog` trên
SQLite live DB → DROP TABLE AuditLogs + xoá 1 row trong
`__EFMigrationsHistory`. SHA `04545cc5...` đổi thành `b7f38b5a...`. Phải
restore từ Phase A backup byte-identical để recovery (`cp $BACKUP
data/ccl_mes.db`). Không mất data vĩnh viễn nhờ A→B→C protocol.

### 7.2 ⚠ KHÔNG `ef migrations add` mà không set `MES_CONNSTR`

Mặc định `ef migrations add` đọc connection string từ `appsettings.json`
qua `MesDbContextFactory` → trỏ về `data/ccl_mes.db` (live). Tool sẽ
inspect schema live khi build model snapshot → ghi metadata vào file
`Designer.cs`, có thể ảnh hưởng/xung đột với state thật.

### 7.3 Pattern an toàn — luôn dùng

```bash
# Snapshot CURRENT model snapshot trước khi sửa
cp src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs \
   /tmp/snapshot-pre-<name>.cs

# Generate trên ISOLATED DB
rm -f /tmp/<name>-design.db
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" \
  dotnet ef migrations add <Name> -p src/CCL.MES.Infrastructure \
  -s src/CCL.MES.Web -o Migrations --no-build

# Verify content bằng cat / Read tool — đừng apply
cat src/CCL.MES.Infrastructure/Migrations/*<Name>.cs

# Optionally — apply trên isolated DB để verify schema
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" \
  dotnet ef database update -p src/CCL.MES.Infrastructure \
  -s src/CCL.MES.Web --no-build
sqlite3 /tmp/<name>-design.db ".schema <NewTable>"

# UNDO bằng manual rm + git checkout snapshot — KHÔNG dùng remove
rm -f src/CCL.MES.Infrastructure/Migrations/*<Name>*
cp /tmp/snapshot-pre-<name>.cs \
   src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
```

### 7.4 Diagnostic — khi cần xác nhận live DB chưa bị đụng

```bash
shasum -a 256 data/ccl_mes.db    # so với SHA Phase A backup
sqlite3 data/ccl_mes.db "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"
sqlite3 data/ccl_mes.db "SELECT COUNT(*) FROM WorkCenters;"  # = 43 baseline
```

### 7.5 Recovery playbook khi đã lỡ

1. **STOP** mọi thao tác trên repo (đừng commit / push).
2. `cp $BACKUP data/ccl_mes.db` (Phase A backup luôn phải có sẵn).
3. `shasum -a 256` để xác nhận byte-identical.
4. `git checkout src/CCL.MES.Infrastructure/Migrations/` để restore file
   gốc — KHÔNG dùng `ef migrations remove` lần nữa.

### 7.6 Áp dụng cho Phase 6 Bước 6.5 (3.2.B affinity fix)

Sau khi strip `type:` strings + `HasColumnType()` calls khỏi 3 migration
existing + ModelSnapshot:
- `MES_CONNSTR=Data Source=/tmp/verify-sqlite.db ef migrations add
  VerifyNoChange_Sqlite` → file generated empty → confirm no drift
- `MES_CONNSTR=Data Source=/tmp/verify-sqlserver.db MES_PROVIDER=SqlServer
  ef migrations add VerifyNoChange_SqlServer` → file chỉ có AlterColumn
  identity annotation (expected cross-provider quirk, không phải type
  drift)
- `rm` 4 verify files + `cp /tmp/snapshot-pre-verify.cs` để restore
  ModelSnapshot — KHÔNG dùng `ef migrations remove`

### 7.7 Áp dụng cho Phase 6 Bước 7 (AddIqcInspection migration v4)

Sau khi thêm IqcInspection + IqcResultDetail entity:
- `MES_CONNSTR=Data Source=/tmp/iqc-design.db ef migrations add
  AddIqcInspection` → file generated với 2 CreateTable + 5 CreateIndex
- Python type-strip script áp lên file mới để bảo toàn 3.2.B
- `ef database update` trên `/tmp/iqc-design.db` → verify `.schema
  IqcInspections` đúng
- Live DB SHA `850fbf56...` UNCHANGED throughout — protocol works.

---

## 8. `git merge -X ours` strategy — blunt cho overlapping additive edits (Phase 6 close-out)

### 8.1 Bối cảnh

Sprint Phase 6 close-out có 4 stacked PR (#14 → #15 → #16 → #17) + 1 chore (#18) + 1 P0 fix (#19). Mỗi lần anh merge 1 PR vào `main`, các PR còn lại bị conflict. Strategy dùng để re-merge: `git merge origin/main --no-ff -X ours`.

Cho 90% conflict (cùng region edited khác nhau từ 2 side), `-X ours` chọn HEAD version → preserve toàn bộ Bước N changes, đẩy thêm files mới từ main vào. PR sau khi push lại CLEAN MERGEABLE.

### 8.2 Bug PR #19

Cho overlapping **additive** edits trong **cùng block** (PR #14 thêm 3 policies + PR #18 chỉ sửa comment trong cùng `AddAuthorization` block), `-X ours` chọn ours → **mất 3 policies từ theirs**.

Cụ thể `Program.cs:138-150`:
- PR #18 (chore) sửa comment AdminOnly từ "4 admin sub-tabs" → "3 admin sub-tabs" (vì xoá Import data v1.0)
- PR #14 (Bước 4) thêm 3 policies `NpiRead` + `NpiSpecRead` + `QcRead` vào cùng block
- Branch PR #18 fork TRƯỚC khi PR #14 merge → PR #18 không có 3 policies
- Re-merge PR #18 với `-X ours` → giữ ours (PR #18 version) → drop 3 policies + revert `AdminOnly` từ `UserRole.Admin` về string `"Admin"`

Hậu quả: mọi GET `/npi/engineer-spec` + `/qcqa/*` → **HTTP 500** `AuthorizationPolicy named: 'QcRead' was not found` ngay sau khi PR #18 merge.

Phát hiện qua smoke verify trên main (curl admin login + GET 13 routes) — KHÔNG có chuyện này nếu skip smoke step.

### 8.3 Pattern an toàn

1. **Identify additive blocks before merge** — block có overlapping ADD-ONLY trên cả 2 side cần resolve THỦ CÔNG, giữ cả 2 set of additions.
2. **Tránh `-X ours` blanket** cho PR scope chỉnh nhiều file shared/config. Sprint close-out như Phase 6 nên dùng default 3-way merge + resolve từng conflict carefully.
3. **Smoke verify trên main sau từng PR merge** (KHÔNG chờ tới cuối). Mỗi PR merged → quick curl smoke với admin login + 5 representative routes phủ 5 policy. Phát hiện regression sớm.
4. **Sprint close-out checklist** (Phase 6 added):
   - Sau merge mỗi PR: `dotnet build` + curl smoke `/login` + `/` + `/npi/engineer-spec` + `/qcqa/iqc` + `/settings/account` (5 route đại diện 5 policy)
   - Nếu phát hiện 500 → check ngay `AuthorizationPolicy` registration trong Program.cs
   - Final smoke matrix có cả admin (200 hết) + operator (AccessDenied panel rendered)

### 8.4 Code review heuristic

Trong PR review, khi thấy diff có cả 2 phía cùng modify 1 block configuration (DI setup, policy registration, route mapping, middleware pipeline) — flag để reviewer kiểm tra MANUAL chứ đừng tin `-X ours`.

---

## 9. ClosedXML dependency — exception to "CSV-only" rule (Phase 8 PR #31a)

CSV-only data interchange rule (originally from Phase 7 NPI import) **does not
work** for Spec sheet import. SpecHub silkscreen/flexo templates pack the
header + data table + sub-headers + revision history into a SINGLE worksheet
using merged cells + 2-row sub-headers + positional cell layouts. CSV would
need 9 separate files per spec, and the cell-position semantics would be lost.

**Exception**: PR #31a pins `ClosedXML 0.104.2` in `CCL.MES.Infrastructure.csproj`.
Pure-managed .NET, MIT licensed, no native deps, cross-platform. Pulls
`DocumentFormat.OpenXml` + `ExcelNumberFormat` + `SixLabors.Fonts` (also
permissive licenses). ~12 MB packages, ~4 MB gzipped — acceptable for the
Web project.

**Why ClosedXML over alternatives**:

| Lib | Why rejected |
|---|---|
| EPPlus v5+ | Polyform commercial license restrictive — risk to commercial CCL deploy |
| NPOI | API rườm rà, less idiomatic .NET |
| OpenXml SDK raw | Too low-level — verbose 3-4× LOC for column lookup |

**Operational caveats**:

1. **OOXML zip case sensitivity** — `XlsxNormalizer` rewrites Content_Types
   Override `PartName` casing before handing to OpenXml SDK. SpecHub samples
   produced by SheetJS contain mismatched case (`/xl/sharedStrings.xml`
   override vs `xl/SharedStrings.xml` actual file). SheetJS is lenient;
   OpenXml is strict. Normalizer is unconditional — adds <100ms overhead
   per file but ensures any SheetJS-produced xlsx works.

2. **Number formatting** — ClosedXML `cell.GetFormattedString()` respects
   workbook locale. SpecHub VN locale uses comma decimal (`78,5` instead
   of `78.5`). Parser `ParseDouble` normalizes by stripping non-digits +
   replacing comma with dot before `double.TryParse`. Don't read raw
   `cell.Value.ToString()` directly — locale leak.

3. **Upgrade path** — when bumping ClosedXML, run the parser harness at
   `/tmp/parser-harness` against `wwwroot/Data/Specs/DEMO_SILK_*.xlsx`
   and verify all 4 files still produce expected (Customer, PartNo, RefNo,
   NumColors). Sub-deps may change OOXML strict-mode behavior.

4. **No Excel installed on server** — ClosedXML is pure .NET, runs anywhere
   net8+ runs. macOS dev + Windows IIS prod both supported.

## 10. Test-driven sample bundle + sanitization workflow (PR #31a)

When bundling customer-derived data into the repo as fixtures:

1. **Read source files via parser**, capture expected (customer, partno,
   partname). These become the "before" check.
2. **Edit cells via ClosedXML** in a one-shot script (NOT in production
   code path), replacing customer-identifying fields with fully-synthetic
   demo values.
3. **Re-parse the sanitized files** + assert `parsed.Customer`,
   `parsed.PartNo`, `parsed.PartName` no longer contain ANY original PII
   substring. The assertion must use `Contains` not `Equals` to catch
   partial-substring leaks (e.g. `DEMO-DLT-80644547` still leaks SAP
   code 80644547 — first iteration of PR #31a failed this; iterated to
   `DEMO-DT-001`).
4. **Document mapping in `README.md` next to the bundled files** — what
   was replaced + what was preserved + why. Auditor must be able to
   reconcile a year later when someone asks "where did these samples
   come from?".
5. **Commit only sanitized files**. Source files in SpecHub/Data/Specs/
   stay READ-ONLY and out-of-tree.

## 11. Customer auto-create FK chain (PR #31a)

`Product.CustomerId` is non-nullable FK → Customer. When importing a spec
from an unknown customer, the Spec import service must `lookup-or-create`
Customer *before* `lookup-or-create` Product *before* the ProductRevision
+ siblings. Forgetting this throws `SQLite Error 19: FOREIGN KEY constraint
failed` at first SaveChangesAsync — opaque error that took 1 round of
debugging during PR #31a build.

Pattern (preserved in `SpecImportService.SaveAsync`):

```csharp
var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Name.ToLower() == parsed.Customer.ToLower())
              ?? (await CreateCustomer(parsed.Customer));
var product = await _db.Products
    .FirstOrDefaultAsync(p => p.ProductCode == parsed.PartNo && p.CustomerId == customer.Id)
    ?? (await CreateProduct(customer.Id, parsed.PartNo, parsed.PartName));
var revision = new ProductRevision { ProductId = product.Id, ... };
```

Each "create" requires `SaveChangesAsync()` BEFORE downstream foreign key
references resolve. The whole sequence stays inside one transaction so a
late throw rolls back Customer + Product + Revision atomically.

---

## 12. PDFsharp + MigraDoc dependency cho server-side PDF gen (Phase 8 PR #31c)

PR #31c thêm `PDFsharp-MigraDoc 6.2.4` (MIT license) cho server-side PDF gen
trong export feature. **Lý do chọn MIT thay QuestPDF Community License**:
QuestPDF Community License free CHỈ ≤ $1M USD revenue/year — CCL Vietnam
business có khả năng vượt ngưỡng → MIT safer legally + KHÔNG cần revenue
audit yearly.

**Why this specific package variant**:

| Variant | Use when |
|---|---|
| `PDFsharp-MigraDoc` 6.2.4 | **Chọn** — cross-platform pure .NET (no System.Drawing/GDI dep). Chạy trên Linux/macOS/Windows server. |
| `PDFsharp-MigraDoc-GDI` 6.2.4 | LOẠI — Windows-only via System.Drawing.Common (Linux deploy fail). |
| `PDFsharp-MigraDoc-WPF` 6.2.4 | LOẠI — WPF dep, không phù hợp ASP.NET Core Linux. |

**Architectural pattern — reusable cho PR #33 detail sheet PDF**:

PR #31c thiết kế PDF layer thành 3 component tách biệt:
1. `SpecPdfDocumentBuilder.cs` — shared DOM builder + style constants
   (PrimaryColorHex, header/footer fonts, page setup). Public methods:
   `BuildEmpty(title, orientation)` cho caller append sections (PR #33 detail
   sheet sẽ dùng) + `BuildListView(rows, ctx)` cho PR #31c list export.
2. `PdfSpecListExporter.cs` — list-specific impl của `ISpecListExporter`.
3. `SystemFontResolver.cs` — cross-platform IFontResolver (xem mục 12.1 dưới).

Khi PR #33 build detail sheet PDF: chỉ cần extend `SpecPdfDocumentBuilder`
thêm `BuildDetailSheet(spec, ctx)` reusing cùng StyleConstants. KHÔNG hard-
code list cụ thể trong PdfSpecListExporter.

### 12.1 Cross-platform font resolution

PDFsharp 6.2.4 KHÔNG bundle font; phải register `IFontResolver` provide TTF
bytes. SystemFontResolver tìm font theo platform-specific path:

- **macOS**: `/System/Library/Fonts/Supplemental/Arial.ttf` (+ Bold/Italic/Bold-Italic
  variants)
- **Linux**: `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf` (+ variants).
  **Deploy requirement**: `apt install fonts-dejavu-core` (~5MB, preinstalled
  trên hầu hết Debian/Ubuntu/RHEL distros) — fail với `FileNotFoundException`
  + message gợi ý apt command nếu missing.
- **Windows**: `%WINDIR%\Fonts\arial.ttf` (+ variants, preinstalled native).

Cache TTF bytes trong static Dictionary — load 1 lần per font face (size +
io overhead chỉ ở first export). Reusable cho PR #33 + future detail sheet.

**Deploy gate**: nếu deploy CMES lên Linux server, verify fonts-dejavu-core
preinstalled BEFORE first PDF export request. Add vào deploy checklist.

## 13. ASP.NET Core controller route + dot extension (PR #31c trap)

PR #31c originally route `/api/specs/export.csv` với `[HttpGet(".csv")]` —
route resolves 404 vì static-file middleware claim path-with-dot-extension
trước controller routing. **Fix**: switch sang path segment `/api/specs/
export/csv` với `[HttpGet("csv")]`.

**General rule**: tránh dot extension trong API route templates. Dùng path
segment cho format dispatch (`/export/{format}`) hoặc query string
(`?format=csv`).

## 14. `[Authorize(Policy=...)]` trên API controller redirect tới SPA fallback

PR #31c originally `[Authorize(Policy = "NpiSpecRead")]` — request từ
authenticated user vẫn return 200 HTML (SPA shell) thay vì 200 file content.
Cause: policy challenge invoke cookie-auth challenge → redirect tới `/login`
→ MapFallbackToPage("/_Host") catches redirect + returns SPA shell HTML 200.

**Fix**: dùng `[Authorize(Roles = "Admin,Supervisor,Engineer")]` — Roles
attribute returns 403 Forbidden cho ApiController route thay vì challenge
redirect. Behavior consistent với `[ApiController]` convention.

**General rule**: API controller (`[ApiController]`) prefer `[Authorize
(Roles=...)]` over `[Authorize(Policy=...)]` để skip challenge redirect path.
Mirror policy intent trong Roles list để giữ matrix consistent với Razor Page
`<AuthorizeView Roles="...">`.

---

## 15. Backfill mode tách khỏi force=re-create (PR #31d)

PR #31a `RefreshSamplesAsync(force=true)` SOFT-TRASH existing revision + tạo
mới (revision Ids tăng, count tăng). Đó là pattern đúng cho "fresh sample
sau khi parser bug fix" — clean audit trail forensic + reset state.

NHƯNG khi schema thêm field mới (PR #31d ADD 4 nullable cols trên SpecPrint)
và muốn populate cho data existing — `force=true` SAI vì:
- Tạo revision trùng (ProductRevisions count tăng 6 → 13: 6 trashed + 6 new)
- Reset operator-edited fields (nếu có) — không bảo toàn data
- Audit log nhiễu (6 trash + 6 import events)

**Fix pattern**: `BackfillDetailSheetFieldsAsync` riêng (KHÔNG reuse force=):
- Match RefNo → fetch existing revision
- Update ONLY fields hiện NULL (preserve operator-edited values)
- KHÔNG tạo revision mới
- Idempotent: 2nd run = 0 backfilled
- Single `SPEC_BACKFILL_DETAIL` audit event per cycle (gọn)

**General rule**: khi schema migration ADD nullable fields, design backfill
service riêng — KHÔNG reuse force=re-create của import flow. Decoupling
backfill (data-only update) vs force (clean re-import) giữ semantic rõ.

## 16. Razor `@page` route parameter binding (PR #31d trap)

`@page "/path/{revisionId:long}"` Razor route parameter PHẢI declare
`[Parameter]` INSIDE `@code { }` block, KHÔNG OUTSIDE (như inline Razor
expression). Outside `@code` block trigger compile error CS0103 "name not
exists".

Pattern đúng:
```razor
@page "/npi/engineer-spec/{revisionId:long}"
@code {
    [Parameter] public long RevisionId { get; set; }
}
```

Lệch pattern (compile fail):
```razor
@page "/path/{revisionId:long}"
[Parameter] public long RevisionId { get; set; }  // ❌ CS0103
@code { ... }
```

## 17. SqlServer dot-extension trong API route (lặp lại bài học #33)

PR #31d `[HttpGet("sheet/{revisionId:long}.pdf")]` — dot extension trong
route template re-trigger bài học #33 (static-file middleware claim
path-with-dot trước controller routing → 404 / SPA fallback).

**Pattern an toàn**: chỉ dùng path segment, NO dot extension ANYWHERE
trong route template. Browser tự thêm extension qua Content-Disposition
header response → operator download `.pdf` filename đúng.

Sửa: `[HttpGet("sheet/{revisionId:long}")]` → URL `/api/specs/export/sheet/123`
trả PDF binary với Content-Type `application/pdf` + Content-Disposition
attachment filename. Browser tự handle.

---

## Phase 8 PR-D-5a — Blob storage path scheme + security rules (FilesystemBlobStore)

Drawings được persist qua `IBlobStore`. PR-D-5a implement `FilesystemBlobStore` —
nền cho PR-D-5b (upload UI) và PR-D-5c (3-role approval). Pin lại quy ước ở đây
để PR sau (và onboarding) không phải tự re-discover.

### Path scheme — fix cứng, đừng đổi mà không cập nhật mọi tầng

```
<DataDir>/blobs/drawings/<revisionId>/<drawingId>/v<n>_<sha8>.<ext>
```

- `DataDir` re-use lại đúng giá trị mà `Program.cs` resolve cho SQLite DB
  (env `MES_DATA_DIR`, mặc định `<repo-root>/data/`). Boot log dòng
  `[boot] Blob root: …/blobs/` confirm path.
- **Storage key persist vào DB là RELATIVE path** (`drawings/1/2/v1_3a7e9f12.pdf`),
  không phải absolute. Sang prod copy DataDir đi chỗ khác, key vẫn resolve được.
- `sha8` là 8 hex char đầu của SHA256 — dedup + integrity check.
- `revisionId` + `drawingId` là số nguyên `> 0` (KHÔNG leading-zero — tránh
  `001` lừa regex thành alias của `1`).

### 6 security guards (KHÔNG bỏ guard nào khi extend)

1. **Suggested-key regex** trên `PutAsync`:
   `^drawings/([1-9]\d*)/([1-9]\d*)/v([1-9]\d*)\.([a-zA-Z0-9]{1,8})$` —
   reject `..`, leading `/`, drive letters, null bytes, NFD tricks.
2. **Stored-key regex** trên `GetAsync` / `ExistsAsync` / `DeleteAsync` —
   sha8 BẮT BUỘC để caller không probe key tuỳ ý.
3. **Extension allowlist** — `pdf png jpg jpeg svg gif webp dwg dxf ai`
   (override env `MES_BLOB_ALLOWED_EXTENSIONS` CSV nếu cần).
4. **Size cap** — env `MES_BLOB_MAX_BYTES`, default 10 MiB. Counted
   per-chunk trong vòng `ReadAsync`; throw EARLY trước khi commit file.
5. **Containment check** — resolved `Path.GetFullPath` MUST `StartsWith`
   `<DataDir>/blobs/` (với trailing separator). Defense-in-depth khi
   regex bị bypass qua symlink / NFD canonical tricks.
6. **Atomic rename** — write `.tmp.<guid>` rồi `File.Move`. Partial
   write không bao giờ hiện ra như readable blob.

### Idempotency by content

Nếu PutAsync nhận stream byte trùng với file đã có (cùng `revId/drwId/v/sha8/ext`):
drop temp + return existing key. 2 parallel uploads cùng content converge an toàn.
Operator UX: re-upload "cùng file" lần 2 không tạo duplicate.

### Harness — `dotnet run --project scripts/VerifyBlobStore`

8 test cases + 1 containment audit. Pass criterion: `Result: PASS 9 FAIL 0` +
exit code 0. Out-of-sln giống `VerifyPrB` — engineer tool, không phải CI.
Nếu sửa `FilesystemBlobStore` (đổi regex, đổi allowlist, đổi path scheme),
**phải re-run harness trước khi mở PR**.

### Khi mở PR-D-5b (upload UI)

- Caller `PutAsync` truyền suggestedKey theo template
  `drawings/<revId>/<drwId>/v<n>.<ext>` — `<n>` là next version no operator
  vừa bump. Service trả về stored key chính là cái cần ghi vào
  `DrawingVersion.StorageKey`.
- Multer-equivalent (`IFormFile`) bound to a Blazor server-side handler;
  pass `IFormFile.OpenReadStream()` thẳng vào `PutAsync`. Đừng buffer
  full vào memory — size cap chỉ hữu dụng khi stream đi qua store.
- Persist `FileHash` (SHA256 hex 64 char đầy đủ) — Put trả về cả key
  (chứa sha8 prefix). Hash đầy đủ derive bằng cách `SHA256.HashData` lại
  sau khi đọc back, HOẶC PutAsync expose full sha qua return tuple
  (decision này PR-D-5b chốt).
- DI: `IBlobStore` đã đăng ký Singleton — inject thẳng vào
  service Layer hoặc Razor page.

---

## Phase 8 PR-D-5b — IBlobStore return shape widened + Blazor InputFile cap trap + controller route discipline

PR-D-5b consume `IBlobStore` từ D-5a cho tab Drawings (upload/version/view).
3 lesson lớn rút ra khi wire end-to-end:

### `IBlobStore.PutAsync` return shape widened (small breaking change)

D-5a contract: `Task<string> PutAsync(...)` — chỉ trả về storage key. Key chứa sha8 prefix, nhưng caller nào cần **full SHA256** (DrawingVersion.FileHash = 64 hex chars) phải re-stream file để compute SHA mới — double IO trên mỗi upload.

D-5b widen return về `BlobPutResult(Key, Sha256Hex, SizeBytes)`. Implementation đã compute full sha256 trong single write pass — chỉ thay đổi expose nó qua return. Harness D-5a update accordingly (test #1 + #2 verify all 3 fields). 1-line interface change, 1-line impl change, lý do được pin trong XML doc của `IBlobStore.PutAsync`.

**Bài học**: contract design phase, nếu store cần compute metadata internally để serve write, expose ngay từ đầu — đừng để caller round-trip lại. Lý tưởng nhất từ D-5a, nhưng accepted cost để D-5a focused trên security guards.

### Blazor `InputFile.OpenReadStream(maxAllowedSize)` default 500 KB — phải override

Blazor Server `InputFile.OpenReadStream()` mà KHÔNG truyền `maxAllowedSize` argument sẽ throw cho file > **524288 bytes (~512 KB)**. Operator upload PDF 2 MB sẽ thấy `IOException: Supplied stream is too long`.

Fix: inject `BlobStoreOptions` vào Razor page, gọi `_uploadModalFile.OpenReadStream(BlobOpts.MaxBytes)` để khớp server cap (env `MES_BLOB_MAX_BYTES`, default 10 MiB). Khi mở UI: pass `BlobOpts.MaxBytes / (1024 * 1024)` xuống modal hint string để operator biết cap.

Integration test `scripts/VerifyDrawingsUpload` chứng minh: 1.2 MiB file upload thành công end-to-end (> Blazor default 500 KB).

### Drawing download controller — path-segment route, NO dot-extension (Lesson #33 reuse)

PR #31d trước đây bị trap khi route template chứa `{filename}.pdf` — static-file middleware match dot-extension và route nhầm sang file lookup → 404.

D-5b reuse lesson: route là `GET /api/specs/{revisionId:long}/drawings/{versionId:long}/file` — pure path-segment, không dot. Content-Type + Content-Disposition response header điều khiển browser behavior:
- `Content-Type` map từ file extension qua whitelist dictionary (10 MIME types khớp BlobStoreOptions.AllowedExtensions; unknown → `application/octet-stream` fallback).
- `Content-Disposition: inline; filename="<sanitized>"` — browser preview PDF/image in-tab + cho phép Cmd+S download với filename gốc.
- `X-Content-Type-Options: nosniff` — chặn browser MIME sniff override (defense vs disguised content).
- `Cache-Control: private, max-age=300` — auth-scoped, browser cache 5 phút (mặc dù blob content immutable per sha8 key thì cache có thể dài hơn — to-do PR-D-5c).

**Bổ sung**: revision-scoped download. Controller route nhận cả `revisionId` + `versionId`; service `GetForDownloadAsync` verify version's parent drawing thuộc đúng revisionId được pass vào (defense-in-depth — caller URL phải khớp cả 2 ids, forging chỉ versionId mà sai revisionId trả 404).

### `[Authorize(Roles=...)]` over `[Authorize(Policy=...)]` cho API endpoints

`SpecsExportController` (PR #31c) đã pin pattern: dùng `Roles="Admin,Supervisor,Engineer"` thay vì `Policy="NpiSpecRead"` cho API controllers. Lý do: API auth response 401/403 thay vì cookie-auth challenge redirect → SPA fallback `_Host.cshtml` (HTTP 200 HTML response, gây client nhầm thành thành công).

`DrawingsController` reuse cùng pattern. Roles list khớp Program.cs:NpiSpecRead policy.

---

*Cập nhật lần cuối: 01/06/2026 — Phase 8 PR-D-5b (IBlobStore return shape + Blazor InputFile cap + controller route discipline).*
