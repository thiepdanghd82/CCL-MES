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

*Cập nhật lần cuối: 01/06/2026 — Phase 8 PR #31c (PDFsharp-MigraDoc dep + cross-platform font resolver + API auth route patterns).*
