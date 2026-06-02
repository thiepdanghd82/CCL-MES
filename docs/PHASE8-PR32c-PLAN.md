# Phase 8 — PR #32c plan: WO Export + History (consolidated `/workorders`)

**Status**: DRAFT — chờ duyệt scope History trước khi code
**Parent**: PR #54 merged (137cdb4) — `/workorders` is the single hợp nhất surface
**Branch (target)**: `feat/phase8-workorders-export-history`
**Hard freeze**: KHÔNG migration, KHÔNG dep mới, sibling projects READ-ONLY, baseline (ProductRevisions=6, WorkOrders=1, IqcInspections=3, FK ProductRevision↔WO) intact, Phase 6 state machine + service mutation methods UNTOUCHED

---

## 1. Scope (3 items)

| # | Item | Status sau plan | Code-ready? |
|---|---|---|---|
| A | **Export CSV + Excel** — toolbar button trên `/workorders`, export Active+Closed combined theo `_result` hiện tại | RÕ — reuse pattern PR #33 | YES — code luôn khi anh chốt History |
| B | **History** | CẦN CHỐT SCOPE (Q1–Q4) | NO — chờ duyệt |
| C | **SignalR auto-refresh** (Q5 defer từ #32b) | ĐÃ XONG ở PR #54 | N/A — close ticket Q5 |

### C — SignalR đã wired sẵn (CLOSE Q5)

`src/CCL.MES.Web/Pages/WorkOrders.razor:316-321` đã subscribe `_hub.On<string>("shopfloorChanged", _ → RefreshAllAsync())` từ PR #54. Mỗi mutation (Advance / Flags / QC / OEE) gọi `ShopfloorNotifier.NotifyChangedAsync(reason)` → hub broadcast → card view + drawer DTO refresh. **Live refresh card view đang BẬT.** Không cần làm thêm. Plan chỉ note để khép Q5.

---

## 2. History — scope clarification (cần chốt)

Theo brief: 3 nghĩa khả dĩ.

| Option | Nghĩa | Effort | Trùng feature hiện có? |
|---|---|---|---|
| **(a) Timeline audit per-WO** | Section "History" mới trong drawer, đọc `AuditLogs` của WO này (WO_ADVANCE + WO_FLAGS_UPDATE), DESC theo `Timestamp`, read-only | S | Không |
| (b) Archive Closed WO | Trang riêng list Closed WO | S | **TRÙNG** — Closed card section đã có ở `/workorders` |
| (c) Cả (a) + (b) | — | M | Một nửa trùng |

### Đề xuất default: **(a) Timeline audit per-WO trong drawer**

Lý do:
- Closed card section đã thoả mãn nhu cầu archive (b) — KHÔNG cần làm thêm.
- (a) là gap thực sự — operator hiện không thấy WO này đã đổi state khi nào, ai đổi. Hiện chỉ thấy `CurrentStep` cuối cùng.
- Read-only AuditLog → KHÔNG field/migration mới. Reuse `_db.AuditLogs` query trực tiếp giống `AuditLogService.cs:19`.
- Drawer đã có 5 section (Header / Production / Materials / **QC History** / Action). Thêm section "History" giữa QC History và Action sẽ giữ Action sticky cuối.

### Query design (a)

```csharp
// Trong WorkOrderService.GetDrawerAsync (extend existing)
var historyRows = await _db.AuditLogs.AsNoTracking()
    .Where(a => a.TargetType == "WorkOrder" && a.TargetId == wo.Id.ToString())
    .OrderByDescending(a => a.Timestamp)
    .Take(50)  // Q3 default
    .Select(a => new WoHistoryRow(
        a.Timestamp, a.ActorUsername, a.Action, a.Detail))
    .ToListAsync();
```

- **QC events KHÔNG include** (TargetType="QcInspection") — đã có section "QC History" riêng đọc QcInspections trực tiếp; tránh duplicate.
- **OEE events KHÔNG có audit emit** (xác nhận grep `OeeService.cs` — 0 `_audit.EmitAsync`) → không xuất hiện trong timeline. Acceptable; OEE state hiện hữu qua flags + ProductionLog.

### Câu hỏi cho anh duyệt (Q1–Q4)

- **Q1**: Default (a) timeline audit per-WO trong drawer — **OK**? (Hay anh muốn (c) thêm trang archive Closed riêng?)
- **Q2**: Vị trí section "History" trong drawer — **đặt giữa QC History và Action** (giữ Action sticky cuối) — **OK**?
- **Q3**: Limit số dòng — **50 most recent**, không paginate (drawer-scoped) — **OK**? Nếu cần xem cũ hơn → link sang `/settings/syslog` filter trước (đã có ở Phase 6).
- **Q4**: Detail rendering — đề xuất **pretty-fields** thay raw JSON:
  - `WO_ADVANCE` → `Step <from> → <to>` (parse `detail.from` + `detail.to`)
  - `WO_FLAGS_UPDATE` → `Materials: ✓ · Setup: ✓ · RoHS: ✓ · Produced: 1200` (chỉ in fields non-null)
  - Fallback (action lạ) → raw JSON one-liner truncate 200 chars
  - **OK**? Hay anh muốn raw JSON luôn cho simplicity?

---

## 3. Export — design (RÕ, code-ready khi History chốt)

### 3.1 Reuse pattern PR #33

| Layer | PR #33 (Spec) | PR #32c (WO) | New file? |
|---|---|---|---|
| Column SSoT | `SpecListColumns.cs` | `WoListColumns.cs` | YES — Application/WorkOrderExport/ |
| Exporter abstraction | `ISpecListExporter` | `IWorkOrderListExporter` | YES — same shape (Format/ContentType/Ext/Export) |
| CSV impl | `CsvSpecListExporter` (Application) | `CsvWorkOrderListExporter` (Application) | YES — pure .NET, RFC 4180, UTF-8 BOM |
| XLSX impl | `XlsxSpecListExporter` (Infrastructure) | `XlsxWorkOrderListExporter` (Infrastructure) | YES — ClosedXML 0.104.2 (đã có) |
| Controller | `SpecsExportController` | `WorkOrdersExportController` | YES — `/api/workorders/export/{csv\|xlsx}` |
| DI registration | Program.cs | Program.cs | EDIT — `+3 lines AddSingleton<Csv/Xlsx exporter>` |

**KHÔNG dep mới.** ClosedXML đã trong `CCL.MES.Infrastructure.csproj:20`.

### 3.2 Cột export (đề xuất default)

Mirror **card view body fields** (richer than table 7-col):

| # | Cột | Source |
|---|---|---|
| 1 | WO No | `wo.WoNo` |
| 2 | Customer | `wo.CustomerName` |
| 3 | Product Code | `wo.ProductCode` |
| 4 | Product Name | `wo.ProductName` |
| 5 | Machine | `wo.MachineCode` |
| 6 | Process | `wo.ProcessLabel` |
| 7 | Target Qty | `wo.TargetQty` (int) |
| 8 | UoM | `wo.Uom` |
| 9 | Produced Qty | `wo.ProducedQty` (int) |
| 10 | Current Step | `StepName(wo.CurrentStep)` |
| 11 | Status | `wo.BadgeLabelKey` resolved → text |
| 12 | Section | "Active" / "Closed" (để filter sort trong Excel) |

12 cột. KHÔNG xuất `_drawerView` mở (export là list-level, không per-WO detail sheet).

### 3.3 Scope dữ liệu

- Brief: "Export CSV + Excel danh sách WO (filter/view hiện tại)".
- `/workorders` hiện **KHÔNG có search/filter** ở toolbar (chỉ có scan lookup → mở drawer 1 WO, không filter list).
- → Export = **toàn bộ** `_result.Active.Concat(_result.Closed)` (cùng dữ liệu cả card view và table view dùng).
- View toggle (card/table) chỉ ảnh hưởng UI render; export độc lập view.
- **Không cần truyền filter param qua URL** ở PR này. Nếu sau này thêm search/filter → extend controller query.

### 3.4 Endpoint design

```csharp
[ApiController]
[Route("api/workorders/export")]
[Authorize]  // Match page FallbackPolicy = RequireAuthenticatedUser
public class WorkOrdersExportController : ControllerBase
{
    [HttpGet("csv")]  public Task<IActionResult> ExportCsv()  => ExportAsync(_csv);
    [HttpGet("xlsx")] public Task<IActionResult> ExportXlsx() => ExportAsync(_xlsx);

    private async Task<IActionResult> ExportAsync(IWorkOrderListExporter exporter)
    {
        try
        {
            var data = await _wo.ShopOrderListAsync();  // existing service method
            var rows = data.Active.Concat(data.Closed).ToList();
            var ctx = new WoExportContext(
                Title: "Work Orders",
                GeneratedAt: DateTime.UtcNow.ToLocalTime(),
                GeneratedBy: User?.Identity?.Name ?? "anonymous",
                Culture: CultureInfo.InvariantCulture);
            var bytes = exporter.Export(rows, ctx);
            var ts = DateTime.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss");
            var filename = $"WorkOrders_{ts}.{exporter.FileExtension}";

            await _audit.EmitAsync(
                AuditAction.WoExport, User?.Identity?.Name ?? "anonymous",
                actorRole: "", targetType: "WorkOrderList", targetId: "(batch)",
                detail: JsonSerializer.Serialize(new {
                    format = exporter.Format, rows = rows.Count,
                    filename, content_length = bytes.Length }));

            return File(bytes, exporter.ContentType, filename);
        }
        catch (Exception ex)
        {
            return Problem(title: "Export failed", detail: ex.Message, statusCode: 500);
        }
    }
}
```

### 3.5 New audit code

```csharp
// src/CCL.MES.Domain/Audit/AuditAction.cs (+1 const, alphabetical position)
public const string WoExport = "WO_EXPORT";
```

### 3.6 UI — toolbar button trên `/workorders`

Thêm nhóm "Export" cạnh `workorders-view-toggle` (cùng row, navy primary, dropdown 2 option CSV/Excel):

```razor
<div class="workorders-export" role="group" aria-label="Export">
    <a class="rt-btn" href="/api/workorders/export/csv" target="_blank">
        @Loc["workorders.export.csv"]
    </a>
    <a class="rt-btn" href="/api/workorders/export/xlsx" target="_blank">
        @Loc["workorders.export.xlsx"]
    </a>
</div>
```

- `<a target="_blank">` thay button để browser handle file download natively (Content-Disposition).
- KHÔNG cần JS interop / DotNetObjectReference.
- KHÔNG cần loading spinner — request < 500ms với 100 WO.

### Q5–Q9 (Export — đề xuất default, code luôn nếu OK)

- **Q5**: Cột export = 12 cột mirror card view (mục 3.2) — **OK**? Hay anh muốn trim/extend?
- **Q6**: Scope export = Active+Closed combined (mục 3.3) — **OK**? Hay tách 2 file riêng?
- **Q7**: Filename = `WorkOrders_<yyyyMMdd-HHmmss>.<ext>` — **OK**?
- **Q8**: RBAC = `[Authorize]` (any authenticated, match page) — **OK**? Hay strict-er `Roles="Admin,Supervisor,Engineer,Operator"`?
- **Q9**: Audit code mới `WO_EXPORT` (alphabetical sau `WoAdvance`) — **OK**?

---

## 4. Files touched (precise list)

### Code mới (export)
- `src/CCL.MES.Application/WorkOrderExport/WoListColumns.cs` — 12 cột SSoT + ToDisplayCells + ToTypedCells
- `src/CCL.MES.Application/WorkOrderExport/IWorkOrderListExporter.cs` — interface + WoExportContext record
- `src/CCL.MES.Application/WorkOrderExport/CsvWorkOrderListExporter.cs` — RFC 4180 + UTF-8 BOM (port từ CsvSpecListExporter, ~70 LOC)
- `src/CCL.MES.Infrastructure/WorkOrderExport/XlsxWorkOrderListExporter.cs` — ClosedXML (port từ XlsxSpecListExporter, ~80 LOC)
- `src/CCL.MES.Web/Controllers/WorkOrdersExportController.cs` — 2 endpoints + audit emit

### Code edit (history + export wiring)
- `src/CCL.MES.Application/Services/WorkOrderService.cs` — extend `GetDrawerAsync` to populate `HistoryRows` (50 most recent WO_ADVANCE/WO_FLAGS_UPDATE), **mutation methods UNTOUCHED**
- `src/CCL.MES.Application/Services/WorkOrderService.cs` (DTOs) — thêm `WoHistoryRow` record + `WorkOrderDrawerView.HistoryRows` field (additive, default empty list)
- `src/CCL.MES.Web/Components/WorkOrderDrawer.razor` — thêm section "History" giữa QC History và Action; pretty-render Action+Detail per Q4
- `src/CCL.MES.Web/Pages/WorkOrders.razor` — thêm `workorders-export` nhóm `<a>` cạnh view toggle
- `src/CCL.MES.Web/wwwroot/css/site.css` — class `.workorders-export` (cờ nhỏ, ~12 LOC styled như view-toggle)
- `src/CCL.MES.Web/Resources/SharedResource.resx` + `.vi.resx` — keys: `workorders.export.csv`, `workorders.export.xlsx`, `workorders.history.title`, `workorders.history.empty`, `workorders.history.action.advance`, `workorders.history.action.flags_update`, `workorders.history.col.when`, `workorders.history.col.who`, `workorders.history.col.what`
- `src/CCL.MES.Domain/Audit/AuditAction.cs` — `+ WoExport = "WO_EXPORT"`
- `src/CCL.MES.Web/Program.cs` — DI: `+ AddSingleton<CsvWorkOrderListExporter>()` + `+ AddSingleton<XlsxWorkOrderListExporter>()`

### Test (no harness change — render từ entity)
- Build clean (`dotnet build` 0/0)
- Smoke test: `/workorders` 200 → toolbar render Export CSV/Excel → click → file download
- Drawer open → History section render với ≥1 audit row trên WO seed
- Baseline preserve check (sqlite query)

### KHÔNG đụng
- `WorkOrderStateMachine.cs` (state machine)
- `WorkOrderService.AdvanceAsync` / `UpdateFlagsAsync` / Phase 6 mutation methods
- `ShopfloorHub.cs` / `ShopfloorNotifier.cs` (SignalR contract)
- `WoErrorCode` / `WoErrorKeys`
- Migration / EF Core schema
- Sibling projects (Ops Control v1.2, SpecHub, CMES, Old ver — read-only)

---

## 5. Hard constraints checklist (mandatory pass pre-merge)

- [ ] `dotnet build` 0 errors / 0 warnings
- [ ] `git diff main -- src/CCL.MES.Application/Services/WorkOrderService.cs` shows ONLY additive (new HistoryRows population in GetDrawerAsync + new DTO field). Mutation methods byte-identical.
- [ ] `git diff main -- src/CCL.MES.Domain/` shows ONLY `+ WoExport` const. No entity touched.
- [ ] No new EF migration (`ls src/CCL.MES.Infrastructure/Migrations/` unchanged).
- [ ] `.csproj` dep diff = 0.
- [ ] Sibling projects: `git status` shows ZERO file touched outside CCL-MES.
- [ ] Baseline preserved: `sqlite3 ccl_mes.db "SELECT COUNT(*) FROM ProductRevisions, WorkOrders, IqcInspections"` returns 6/1/3.
- [ ] FK ProductRevision↔WO intact (`SELECT ProductRevisionId FROM WorkOrders` non-null on existing row).
- [ ] Responsive (Lesson "Responsive main tab pattern"): toolbar `<div>` chứa Export + ViewToggle wrap correctly trên < 640px container (cùng `.shop-order-scan-wrap` đã có `flex-wrap:wrap`).
- [ ] EN/VI i18n parity (8 new keys × 2 files).
- [ ] Audit emit `WO_EXPORT` 1 row per click verified via `/settings/syslog`.

---

## 6. Verify gates (V1–V10)

- V1: build clean (0/0)
- V2: `/workorders` 200, toolbar Export CSV + Excel render
- V3: click CSV → file `WorkOrders_<ts>.csv` download, open Excel VN locale → ký tự non-ASCII đúng (UTF-8 BOM)
- V4: click Excel → file `.xlsx` open ClosedXML → 12 cột typed (TargetQty/ProducedQty là int, không quoted)
- V5: open drawer 1 WO → section "History" render
- V6: trigger Advance 1 lần → SignalR refresh → drawer "History" section thêm 1 row mới đầu list, pretty-render `Step <from> → <to>`
- V7: trigger Flags update → drawer "History" thêm row pretty-render `Materials: ✓ · …`
- V8: drawer Action section vẫn sticky cuối (CSS check)
- V9: `/settings/syslog` filter Action=`WO_EXPORT` → ≥1 row sau click export
- V10: baseline sqlite query unchanged

---

## 7. Submit + STOP

Plan này nêu 9 câu hỏi (Q1–Q9): 4 cho **History** (Q1–Q4, mandatory chốt), 5 cho **Export** (Q5–Q9, defaults đề xuất, OK thì code luôn). SignalR Q5 từ PR #32b — CLOSED bởi PR #54, document trong section 1 trên.

**Chờ anh duyệt** 9 Q trên. Sau khi chốt → code 1 PR gộp Export + History (cùng surface, cùng test cycle, không tách 2 PR vì History reuse `GetDrawerAsync` extend đã có).
