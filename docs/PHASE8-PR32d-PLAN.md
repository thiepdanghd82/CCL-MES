# Phase 8 — PR #32d plan: NG tracking + Demo templates (consolidated `/workorders`)

**Status**: DRAFT — chờ duyệt scope NG (a/b) + Demo (a/b) + Q1–Q10 trước khi branch
**Parent**: PR #55 merged — `/workorders` đã có Export + History
**Branch (target)**: `feat/phase8-wo-ng-demo` (1 PR nếu NG=(a) + Demo=(b) hardcoded; **2 PR** nếu NG=(b) CAPTURE)
**Hard freeze**: KHÔNG migration nếu NG=(a); A→B→C SAFE migration nếu NG=(b); KHÔNG sửa OeeService / ProductionLog / WorkOrderStateMachine / mutation Phase 6 — chỉ gọi service có sẵn hoặc layer NG riêng

---

## 1. Khảo sát — NG/reject hôm nay ghi ở đâu

| Surface | Field | Ghi khi nào | Cách đọc hiện tại |
|---|---|---|---|
| `ProductionLog.RejectQty` (int) | `src/CCL.MES.Domain/Entities/Machine.cs:34` | `OeeService.FinishAsync(woId, FinishRunRequest{ GoodQty, RejectQty })` — operator nhập qua nút "Finish (+target)" ở drawer Action section | `ComputeAsync` (Dashboard OEE) cộng `SUM(RejectQty)` để tính `quality = good/(good+reject)`. KHÔNG có UI nào khác đọc field này per-WO |
| `WorkOrder.ProducedQty` | inherits Phase 6 | `FinishAsync` `wo.ProducedQty += req.GoodQty` (CHỈ Good, KHÔNG cộng Reject) | Card + drawer render `ProducedQty / TargetQty` |
| `ReasonCode Kind=Scrap` (PR-D-4) | seeded 4 codes: **SC-COLOR / SC-REG / SC-DIE / SC-BAR** | Chỉ dùng cho `QcCapture.NgReasonCode` (spec-level fail) | **CHƯA dùng cho production NG** — đây là gap khả thi cho (b) |
| Card #32a "Reject" cell (Closed only) | `WorkOrders.razor:202` | — | Hardcoded `<dd class="muted">—</dd>` (placeholder) |
| Active WO card | — | KHÔNG có cột reject | — |
| `WO_CREATE` audit | — | KHÔNG có audit code | `CreateAsync` không emit audit |

**Kết luận**: Reject đã có sẵn trên `ProductionLog.RejectQty` nhưng card đang HARDCODE "—". Sửa = đọc `SUM(ProductionLog.RejectQty) WHERE WorkOrderId=woId` rồi render — KHÔNG cần entity mới.

---

## 2. NG tracking — scope clarification (cần chốt)

### Option (a) — READ-only từ ProductionLog.RejectQty *(ĐỀ XUẤT DEFAULT)*

- Query 1 dòng: `SUM(ProductionLog.RejectQty) WHERE WorkOrderId=woId` trong `ShopOrderListAsync` (single round-trip via `GroupBy(p => p.WorkOrderId)`).
- Thêm field `RejectQty` (int) vào `WorkOrderCardItem` + `WorkOrderDrawerView` (additive DTO).
- Card Closed: thay "—" bằng số reject thực; nếu reject > 0 hiển thị thêm `RejectRate %` (= reject / (produced + reject) * 100, format 1 decimal).
- Card Active: thêm row "Reject" mới (nhỏ, ẩn nếu reject=0 để không nhiễu khi WO chưa Finish).
- Drawer Production section: thêm row "Reject" + Reject Rate.
- KHÔNG mutation · KHÔNG entity · KHÔNG migration · KHÔNG ReasonCode link · KHÔNG đụng OeeService/ProductionLog/state machine.
- **Effort**: S (~80 LOC service + UI + tests).
- **Trade-off**: NG chỉ là aggregate; không track per-event với ReasonCode. Operator muốn log NG có lý do → phải đợi (b) hoặc cộng dồn ở Finish.

### Option (b) — CAPTURE per-event với ReasonCode

- NEW entity `NgEvent`:
  ```csharp
  public class NgEvent : BaseEntity
  {
      public long WorkOrderId { get; set; }
      public WorkOrder? WorkOrder { get; set; }
      public long? MachineId { get; set; }        // optional — derived từ WO.MachineCode
      public string ReasonCode { get; set; } = "";  // natural key → ReasonCode.Code, Kind=Scrap
      public int Qty { get; set; }
      public string? Comment { get; set; }
      public string CapturedBy { get; set; } = "";
      public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
  }
  ```
- Migration A→B→C SAFE (đã có pattern từ PR-D-4):
  - **A**: Add `NgEvent` table với FK `WorkOrderId → WorkOrders.Id` ON DELETE CASCADE. Index `(WorkOrderId, CapturedAt DESC)`.
  - **B**: Backfill — KHÔNG cần (table mới, append-only từ giờ).
  - **C**: Verify — count = 0 sau migrate; pre-existing WO không bị ảnh hưởng.
- NEW endpoint `POST /api/workorders/{id}/ng` với role gate `Admin,Supervisor,Operator`. Mutation:
  - Validate: `ReasonCode` tồn tại + `Kind=Scrap` + `Qty > 0` + WO không Cancelled/Closed.
  - Transaction: insert NgEvent + emit audit `NG_CAPTURE` per event. RFC 7807 trả lỗi.
  - KHÔNG đụng `ProductionLog.RejectQty` — NgEvent là layer ĐỘC LẬP.
- NEW modal "Log NG" trong drawer Action section: dropdown ReasonCode (Scrap-kind only, đọc từ DB), qty input, comment textarea.
- Read path: drawer "NG History" section riêng (tách khỏi QC History + History audit), DESC theo `CapturedAt`. Reject total trên card = `SUM(NgEvent.Qty)` thay vì `ProductionLog.RejectQty`.
- **Effort**: M-L (~400 LOC + migration + modal + 2 endpoint tests + role tests).
- **Trade-off**: 2 nguồn truth cho reject:
  - `ProductionLog.RejectQty` (aggregate tại Finish, KHÔNG ReasonCode).
  - `NgEvent` (per-event, có ReasonCode).
  - Phải pick 1 làm "card display source": đề xuất `NgEvent` cho operator-facing card khi (b) shipped, fallback `ProductionLog` nếu NgEvent rỗng (backward-compat).

### ĐỀ XUẤT default: **(a) READ-only**

Lý do:
1. **User explicit**: "ưu tiên READ-only nếu đủ" → match brief.
2. **Phase 6 source of truth giữ nguyên** — `FinishAsync` đã ghi RejectQty đúng cách 1 lần ở cuối run; không cần layer mới.
3. **Demo flow operator**: walk steps 1..5 → Running → Pause/Resume/Finish. RejectQty nhập ở Finish modal (đã có). (a) chỉ surface số đó lên card/drawer.
4. **(b) thực sự cần khi**: operator cần log NG NHIỀU LẦN trong cùng run với REASON CODE độc lập (không chờ Finish). Hôm nay flow không yêu cầu — có thể defer thành PR sau (filed sẵn `MES-3-FIX-48` slot trong backlog nếu duyệt (a)).

Nếu anh chọn (b) → **CẦN tách 2 PR**: #32d-1 (NG CAPTURE, M-L) và #32d-2 (Demo, S-M) vì gộp 1 PR sẽ vượt 800 LOC + 2 migration paths.

---

## 3. Demo templates — scope clarification

### Tình trạng hôm nay

`WorkOrders.razor:569 DemoCodes()` chỉ trả về string list 3 WoNo mới nhất làm scan hint ("Recent: WO-26-3683, …"). KHÔNG phải template/sample WO để click tạo mới. Khi #32a refactor → hint giữ nguyên nhưng không có section "Demo Work Orders".

`DbSeeder` seed 1 WO duy nhất (WO-26-3683) cho Brady Asia BRD-7656-D + 6 ProductRevisions. Không có template entity.

### Option (a) — Defer demo templates

- Giữ `DemoCodes()` hint hiện tại (scan recent WoNo).
- KHÔNG section "Demo Work Orders" mới.
- Phù hợp khi operator chỉ cần thử nghiệm với WO seed sẵn (WO-26-3683) hoặc WO thật do CS team tạo.
- Effort: 0 LOC (no change).

### Option (b) — Hardcoded const templates + click-to-create *(ĐỀ XUẤT DEFAULT)*

- 3 template cards hardcoded trong `WorkOrders.razor` (hoặc `Components/DemoWoTemplates.razor`):
  - **Demo 1**: Brady BRD-7656-D, ACNC3, 5000 pcs (small batch)
  - **Demo 2**: Brady BRD-7656-D, ACNC3, 20000 pcs (full run)
  - **Demo 3**: Brady BRD-7656-D, ACNC4 (other machine), 12000 pcs (machine switch demo)
- Mỗi card có button "Start Demo" → gọi `WorkOrderService.CreateAsync(new CreateWoRequest { WoNo = "DEMO-<yyyyMMdd-HHmm>", CustomerId=..., ProductId=..., MachineCode=..., TargetQty=..., Uom="pcs" })`. CreateAsync UNCHANGED — chỉ gọi từ controller mới.
- Section "Demo Work Orders" hiển thị TRÊN Active section (hoặc thu gọn dưới một disclosure `<details>` để tránh nhiễu prod). Default: **chỉ hiện khi WO seed `DEMO-*` chưa có** (operator đã tạo demo rồi thì ẩn để không spam).
- New audit code `WO_CREATE` (alphabetical trước `WoExport`). CreateAsync KHÔNG emit audit; controller `DemoWorkOrdersController` emit audit ở callsite — preserves Phase 6 service.
- Effort: S (~150 LOC + 1 controller + i18n).
- **Trade-off**: Hardcoded templates dễ ship nhưng cần update code khi CCL Vietnam có customer/product mới. Acceptable vì đây là DEMO flow, không phải template management feature.

### Option (c) — Entity-driven templates

- New entity `WoTemplate` (Code/Title/CustomerId/ProductId/MachineCode/DefaultTargetQty/Active).
- Library admin UI để CRUD template.
- Effort: M-L (~400 LOC + migration + admin tab).
- **NGOÀI scope #32d** — feature management, không phải demo. Filed làm KIOSK-009 nếu cần.

### ĐỀ XUẤT default: **(b) Hardcoded templates**

Lý do:
1. Đáp ứng yêu cầu "click to start" của user.
2. Reuse `CreateAsync` có sẵn — KHÔNG sửa service, audit ở callsite.
3. 3 templates đủ demo các flow chính (small/full/machine-switch).
4. Hardcoded acceptable vì là DEMO scaffold, không phải prod template management.

---

## 4. Câu hỏi cần chốt (Q1–Q10)

### NG tracking

- **Q1**: NG scope = **(a) READ-only** từ `SUM(ProductionLog.RejectQty)` — **OK**? (Hay anh chọn (b) → tách 2 PR.)
- **Q2**: Card Closed "Reject" cell — render `12,345 pcs` + nếu reject>0 hiển thị `(1.2% rate)` italic gray bên cạnh — **OK**? Hay chỉ số reject không rate?
- **Q3**: Card Active WO — thêm row "Reject" mới (chỉ render khi reject > 0 để không nhiễu WO chưa Finish) — **OK**? Hay luôn render kể cả 0?
- **Q4**: Drawer Production section — thêm row "Reject" + Reject Rate — **OK**? Vị trí: ngay sau row "Produced"?

### Demo templates

- **Q5**: Demo scope = **(b) hardcoded templates** (3 card click-to-create via `CreateAsync`) — **OK**? Hay (a) defer / (c) entity?
- **Q6**: WoNo format cho demo — `DEMO-<yyyyMMdd-HHmm>` (e.g. `DEMO-20260602-1430`) — **OK**? Hay `DEMO-<rand4>` hay numeric counter?
- **Q7**: Vị trí section "Demo Work Orders" — TRÊN section Active, thu gọn `<details open>`, default mở — **OK**? Hay ẩn hoàn toàn nếu đã có WO `DEMO-*`?
- **Q8**: Audit code mới `WO_CREATE` (emit ở `DemoWorkOrdersController`, KHÔNG đụng `CreateAsync` Phase 6) — **OK**? Hay không cần audit demo?
- **Q9**: Demo RBAC — `Roles="Admin,Supervisor"` (giới hạn quản lý) — **OK**? Hay any authenticated cho phép Operator test demo?
- **Q10**: Click-duplicate cùng card — cho phép tạo nhiều WO `DEMO-*` cùng template (timestamp khác) — **OK**? Hay guard "đã có 1 active DEMO-*" thì ẩn button?

---

## 5. Files touched (precise list, áp DEFAULT (a)+(b))

### Code mới (Demo)
- `src/CCL.MES.Web/Components/DemoWoTemplates.razor` — 3 template cards + Start Demo button, mounted trên section Active của `/workorders`
- `src/CCL.MES.Web/Controllers/DemoWorkOrdersController.cs` — `POST /api/workorders/demo/{templateCode}` calls `WorkOrderService.CreateAsync` + emit `WO_CREATE` audit + try-catch

### Code edit
- `src/CCL.MES.Application/Services/WorkOrderService.cs` — extend `ShopOrderListAsync` + `GetDrawerAsync` to populate `RejectQty` (additive). `CreateAsync` UNCHANGED. Add `WorkOrderCardItem.RejectQty` + `WorkOrderDrawerView.RejectQty` fields.
- `src/CCL.MES.Web/Pages/WorkOrders.razor` — render Reject row in Active card (conditional reject>0) + Closed card replace "—" + mount `<DemoWoTemplates>` above Active section
- `src/CCL.MES.Web/Components/WorkOrderDrawer.razor` — Production section add Reject row + Reject Rate
- `src/CCL.MES.Web/Resources/SharedResource.resx` + `.vi.resx` — 8 new keys: `shop_order.field.reject_rate`, `workorders.demo.section`, `workorders.demo.template.{1,2,3}.title`, `workorders.demo.start_btn`, `workorders.demo.created_fmt`, `workorders.demo.empty_hint`
- `src/CCL.MES.Domain/Audit/AuditAction.cs` — `+ WoCreate = "WO_CREATE"` (alphabetical trước `WoExport`)
- `src/CCL.MES.Web/wwwroot/css/site.css` — `.workorders-demo-section`, `.workorders-demo-card`, `.workorders-demo-start-btn` (mirror `.mes-wo-card` navy)

### KHÔNG đụng
- `OeeService.FinishAsync` / `PauseAsync` / `StartAsync` / `ResumeAsync` (Phase 6)
- `WorkOrderService.AdvanceAsync` / `UpdateFlagsAsync` / `CreateAsync` mutation methods (Phase 6 + #32a/b/c)
- `ProductionLog` entity / `WoStatusHistory` / state machine
- Migration / EF Core schema (NG=(a) → no migration)
- Sibling projects + Spec 6 tab + 4 NPI tab + Machine tab + Ops Control v1.2 / CMES / SpecHub / Old ver

---

## 6. Hard constraints checklist (mandatory pass pre-merge)

- [ ] `dotnet build` 0/0
- [ ] `git diff main -- src/CCL.MES.Application/Services/OeeService.cs` = 0 LOC
- [ ] `git diff main -- src/CCL.MES.Application/Services/WorkOrderService.cs` shows ONLY additive (new RejectQty field + ShopOrderListAsync/GetDrawerAsync extension). `CreateAsync` + `AdvanceAsync` + `UpdateFlagsAsync` byte-identical.
- [ ] No new EF migration (NG=(a)) — `ls src/CCL.MES.Infrastructure/Migrations/` unchanged
- [ ] `.csproj` dep diff = 0
- [ ] Sibling projects: `git status` shows ZERO file touched outside CCL-MES
- [ ] Baseline preserved: ProductRevisions=6, WorkOrders=1, IqcInspections=3
- [ ] FK ProductRevision↔WO intact
- [ ] Responsive (Lesson "Responsive main tab pattern"): Demo section wraps cleanly trên < 640px container
- [ ] EN/VI i18n parity (8 keys × 2 files)
- [ ] Audit emit `WO_CREATE` 1 row per Demo click (verified via `/settings/syslog`)

---

## 7. Verify gates (V1–V10)

- V1: build clean (0/0)
- V2: `/workorders` 200, "Demo Work Orders" section render trên Active
- V3: click "Start Demo" trên template 1 → POST `/api/workorders/demo/1` → 200 → SignalR push → card mới `DEMO-<ts>` xuất hiện trong Active section
- V4: drawer mới WO `DEMO-*` → render đầy đủ 6 section (Header / Production / Materials / QC History / History / Action)
- V5: drawer Production section render Reject row + Reject Rate (= 0 cho demo mới, hiển thị `0 pcs`)
- V6: Trigger demo flow (Advance × N + Finish với GoodQty=4000 + RejectQty=50) → SignalR refresh → card Closed cho WO này thay `—` bằng `50 pcs (1.2% rate)`
- V7: drawer Production Reject = `50 pcs (1.2%)` post-Finish
- V8: `/settings/syslog` filter Action=`WO_CREATE` → ≥1 row sau click Demo
- V9: baseline sqlite query — ProductRevisions=6 / IqcInspections=3 / FK intact; WorkOrders tăng theo số lần click Demo (acceptable, demo flow)
- V10: responsive @ 360/640/1024 — Demo cards stack 1fr ở 540px, side-by-side ở 1024

---

## 8. Submit + STOP

Plan này nêu 10 câu hỏi. Default đề xuất: **NG=(a)** + **Demo=(b)** → 1 PR `feat/phase8-wo-ng-demo`, effort S + S ≈ M (1 ngày).

Nếu anh chốt **NG=(b) CAPTURE** → **tách 2 PR**:
- **#32d-1**: NG CAPTURE (entity + migration + endpoint + modal + drawer NG section + tests) — effort M-L, ship trước.
- **#32d-2**: Demo templates + WO_CREATE audit — effort S, ship sau khi #32d-1 stable.

Nếu anh chốt **Demo=(a) defer hoàn toàn** → 1 PR chỉ NG=(a), effort S (~80 LOC). Closes Work Order epic với NG visibility duy nhất.

**Chờ anh duyệt 10 Q + scope (a/b/c) cho NG + Demo + (nếu cần) chia PR.**
