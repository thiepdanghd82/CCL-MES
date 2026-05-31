# Phase 5 — Bước 3: Backend error-string → error-code refactor (KHẢO SÁT + PHƯƠNG ÁN)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Sau khi chọn phương án em sẽ tạo `feat/phase5-error-codes` để triển khai.

---

## 1. Khảo sát hiện trạng

### 1.1 Bản đồ error string (3 tầng × 9 kịch bản lỗi)

| # | Tầng | File:line | Text tiếng Anh hiện tại |
|---|---|---|---|
| 1 | Domain | `WorkOrderStateMachine.cs:39` | `"Work Order is already at the final step."` |
| 2 | Domain | `WorkOrderStateMachine.cs:46` | `"Requires an approved Spec (SpecVersionId) and ready materials (MaterialsReady)."` |
| 3 | Domain | `WorkOrderStateMachine.cs:51` | `"Requires machine setup confirmation (SetupConfirmed)."` |
| 4 | Domain | `WorkOrderStateMachine.cs:56` | `"IPQC has not yet Passed."` |
| 5 | Domain | `WorkOrderStateMachine.cs:64` | `"No production recorded yet (ProducedQty = 0)."` |
| 6 | Domain | `WorkOrderStateMachine.cs:69` | `"FQC has not yet Passed."` |
| 7 | Domain | `WorkOrderStateMachine.cs:74` | `"OQC has not yet Passed or RoHS not met."` |
| 8 | Domain | `WorkOrderStateMachine.cs:76` | `"Invalid step transition."` (catch-all) |
| 9 | Application | `WorkOrderService.cs:61` | `"Work Order not found."` (khi wo null) |

### 1.2 Shape hiện tại (record types)

| File:line | Định nghĩa |
|---|---|
| `WorkOrderStateMachine.cs:5` | `public record TransitionResult(bool Allowed, string? Reason = null);` |
| `Application/Dtos.cs:62` | `public record AdvanceResult(bool Ok, string? Error, string CurrentStep);` |

### 1.3 Dòng chảy lỗi

```
Domain.WorkOrderStateMachine.CanAdvance(wo)
  → TransitionResult { Allowed = false, Reason = "<EN string>" }
    ↓
Application.WorkOrderService.AdvanceAsync(id, user)
  → AdvanceResult { Ok = false, Error = TransitionResult.Reason }      (mapped at line 65)
  → AdvanceResult { Ok = false, Error = "Work Order not found." }       (wo-null branch, line 61)
    ↓ (2 nhánh tiêu thụ)
    ├── Web.Controllers.WorkOrdersController.Advance(id, user)         (line 31-36)
    │     → res.Ok ? Ok(res) : BadRequest(res)
    │     ⇒ JSON wire: { "ok": false, "error": "<EN string>", "currentStep": "..." }
    │     KHÔNG localize, KHÔNG có client API hiện tại đọc.
    │
    └── Web.Pages.WorkOrders.razor.Advance(id)                          (line 125-134)
          → _message = res.Ok
              ? Loc["workorders.msg.advance_ok", res.CurrentStep ?? ""].Value
              : Loc["workorders.msg.advance_fail", res.Error ?? ""].Value
          → workorders.msg.advance_fail i18n key (EN+VI):
              EN: "Cannot advance: {0}"
              VI: "Không thể chuyển: {0}"
          → {0} là res.Error → CHỖ NÀY giữ tiếng Anh dù culture là VI
            (đây là bug cuối cùng phá vỡ tuyên bố "EN 100% Anh / VI 100% Việt" của Phase 4
             — đã ghi nhận trong FINAL-REPORT-2026-05-31.md §6).
```

### 1.4 Comment "Phase 4+ should swap" đã đặt sẵn

- `WorkOrderStateMachine.cs:11-14`: "Reason strings are kept in English because they bubble through the Razor page as the dynamic portion of a localized message ("Cannot advance: <Reason>"). Phase 4+ should swap to an error-code → resource-key map so the dynamic portion also localises."
- `WorkOrderService.cs:56-60`: cùng nội dung, ghi nhận giới hạn.

→ Refactor này đóng đúng 2 TODO comment trên + đóng gap i18n cuối cùng của Phase 4.

### 1.5 Test hiện tại

Khảo sát `find . -name "*.Tests.csproj" -o -name "*Test*.cs"` ở repo CCL-MES: **KHÔNG CÓ unit test project**. Refactor này không phá test (vì không có test). Smoke test bằng Blazor UI + curl API là kênh duy nhất.

---

## 2. Phương án (3 lựa chọn)

### Phương án A — Enum trong Domain + dictionary map ở Web ⭐ đề xuất

**Cách làm**:

1. **Domain** — `src/CCL.MES.Domain/StateMachine/WorkOrderError.cs` (mới):
   ```csharp
   namespace CCL.MES.Domain.StateMachine;
   public enum WoTransitionError
   {
       AlreadyAtFinalStep,
       RequiresSpecAndMaterials,
       RequiresSetupConfirmed,
       IpqcNotPassed,
       NoProductionYet,
       FqcNotPassed,
       OqcOrRohsNotMet,
       InvalidStepTransition,
       WorkOrderNotFound,  // dùng bởi Application (wo-null), Domain không bao giờ emit
   }
   ```

2. **Domain** — đổi shape `TransitionResult`:
   ```csharp
   public record TransitionResult(bool Allowed, WoTransitionError? Error = null);
   ```
   9 callsite trong `CanAdvance` switch chuyển từ string → enum. Pure swap, không đổi logic guard.

3. **Application** — đổi shape `AdvanceResult` trong `Dtos.cs`:
   ```csharp
   public record AdvanceResult(bool Ok, WoTransitionError? ErrorCode, string CurrentStep);
   ```
   `WorkOrderService.cs:61` đổi `"Work Order not found."` → `WoTransitionError.WorkOrderNotFound`. Line 65 đổi `check.Reason` → `check.Error`.

4. **Web** — `src/CCL.MES.Web/Services/WoErrorKeys.cs` (mới):
   ```csharp
   public static class WoErrorKeys
   {
       private static readonly Dictionary<WoTransitionError, string> _map = new()
       {
           [WoTransitionError.AlreadyAtFinalStep]        = "wo.error.already_at_final_step",
           [WoTransitionError.RequiresSpecAndMaterials]  = "wo.error.requires_spec_materials",
           [WoTransitionError.RequiresSetupConfirmed]    = "wo.error.requires_setup_confirmed",
           [WoTransitionError.IpqcNotPassed]             = "wo.error.ipqc_not_passed",
           [WoTransitionError.NoProductionYet]           = "wo.error.no_production_yet",
           [WoTransitionError.FqcNotPassed]              = "wo.error.fqc_not_passed",
           [WoTransitionError.OqcOrRohsNotMet]           = "wo.error.oqc_or_rohs_not_met",
           [WoTransitionError.InvalidStepTransition]     = "wo.error.invalid_transition",
           [WoTransitionError.WorkOrderNotFound]         = "wo.error.wo_not_found",
       };
       public static string KeyFor(WoTransitionError? code) =>
           code is null ? "wo.error.unknown" : _map[code.Value];
   }
   ```

5. **Web** — `Pages/WorkOrders.razor:131` đổi:
   ```csharp
   _message = res.Ok
       ? Loc["workorders.msg.advance_ok", res.CurrentStep ?? ""].Value
       : Loc["workorders.msg.advance_fail",
             res.ErrorCode is null ? "" : Loc[WoErrorKeys.KeyFor(res.ErrorCode)].Value
            ].Value;
   ```
   → khi VI culture: `Loc["wo.error.requires_setup_confirmed"]` trả tiếng Việt → ghép vào `Loc["workorders.msg.advance_fail"]` (cũng VI) → message cuối cùng 100% tiếng Việt.

6. **Web** — `Controllers/WorkOrdersController.cs:31-36`: KHÔNG đụng. Wire format JSON tự động đổi `"error": "..."` → `"errorCode": "RequiresSetupConfirmed"` (enum serialize as name vì có `JsonStringEnumConverter` đã add ở `Program.cs:23`). Không có client API hiện tại đọc → an toàn.

7. **Resources** — thêm 9 key + (option) 1 key fallback `wo.error.unknown` vào EN + VI resx.

**Ưu**:
- Domain language-free (chỉ enum, không string UX).
- Compiler safety: thêm enum mới → switch của `WoErrorKeys.KeyFor` phải thêm map → CS8509 (nếu dùng exhaustive switch) hoặc runtime KeyNotFoundException → catch ngay sprint sau.
- Đóng 2 TODO comment + đóng gap "EN/VI 100% coverage" cuối cùng của Phase 4.
- Single source of truth (enum) → audit log sau này (Phase 5+) có thể stamp code chứ không phải free-form string.

**Nhược / rủi ro**:
- **BREAKING wire format** trên `POST /api/workorders/{id}/advance`:
  - Trước: `{ "ok": false, "error": "Requires machine setup confirmation (SetupConfirmed).", "currentStep": "OpSetting" }`
  - Sau: `{ "ok": false, "errorCode": "RequiresSetupConfirmed", "currentStep": "OpSetting" }`
- Hiện tại **KHÔNG có client API consumer** (chỉ Blazor UI gọi `WorkOrderService` trực tiếp, không qua REST), nhưng nếu sau này có kiosk/native client sẽ phải hiểu enum.
- Field rename `Error` → `ErrorCode`: ngầm hàm ý "code, không phải free-form". Nếu sợ break ngoài dự kiến → giữ tên `Error` nhưng kiểu enum (anti-pattern, em không khuyến nghị).

**Độ phức tạp**: ⭐⭐ (2/5)
**LOC ước tính**: ~120 (Domain +30, Application +5, Web service +25, WorkOrders.razor +5, 2 file .resx × 9 key × ~3 dòng = +54)

---

### Phương án B — Const string code (vd `"WO_REQUIRES_SETUP_CONFIRMED"`)

**Cách làm**:
- Domain: `public static class WoErrorCodes { public const string AlreadyAtFinalStep = "WO_ALREADY_AT_FINAL_STEP"; ... }`
- `TransitionResult.Reason` → `Code` (vẫn string).
- `AdvanceResult.Error` → string code (vd `"WO_REQUIRES_SETUP_CONFIRMED"`).
- Web side: cùng dictionary `code → resource key` nhưng key là string thay vì enum.

**Ưu**:
- Tự-tài-liệu-hoá khi đọc JSON wire format (`"errorCode": "WO_REQUIRES_SETUP_CONFIRMED"` đọc hiểu ngay vs `"errorCode": "RequiresSetupConfirmed"` enum cũng dễ đọc).
- Linh hoạt nếu tương lai cần mã hoá ngoài C# (vd thêm field `wo_status_history.error_code` text).

**Nhược / rủi ro**:
- **Không có compiler safety**: thêm code mới chỗ này, quên map chỗ kia → KeyNotFoundException runtime.
- String literal magic — typo dễ lọt qua compile.
- Domain vẫn chứa "string" — tinh thần "Domain language-free" mất phần nào (dù không phải UX string).

**Độ phức tạp**: ⭐⭐ (2/5)
**LOC**: tương tự A.

---

### Phương án C — Dual-field (giữ string + thêm code)

**Cách làm**:
- Giữ `AdvanceResult.Error` (English) cho forensic + log.
- Thêm `AdvanceResult.ErrorCode` (enum) cho UI localize.
- `TransitionResult` tương tự: `(bool Allowed, string? Reason = null, WoTransitionError? Code = null)`.

**Ưu**:
- Backward compatible 100% (không break JSON wire format).
- Operator/log đọc English dễ trên server log; UI dùng code để localize.

**Nhược / rủi ro**:
- 2 field nói cùng 1 thứ → drift theo thời gian (sau 6 tháng quên cập nhật 1 trong 2 khi thêm scenario).
- Domain vẫn hardcode English (comment "Phase 4+ should swap" không gỡ được).
- Nhân đôi work mỗi khi thêm/sửa lỗi.

**Độ phức tạp**: ⭐⭐⭐ (3/5)
**LOC**: ~150 (cao hơn A vì cả string + code).

---

## 3. Đề xuất

**Chọn Phương án A** (Enum trong Domain + dictionary map ở Web).

Lý do:
1. **Domain language-free**: enum thuần — không string UX. Đúng nguyên tắc Clean Architecture.
2. **Compiler safety**: thêm enum mới sẽ trigger lỗi compile nếu quên map (nếu dùng exhaustive pattern matching khi build dictionary).
3. **Đóng đúng TODO** đã đánh dấu ở `WorkOrderStateMachine.cs:11-14` + `WorkOrderService.cs:56-60`.
4. **Đóng gap i18n cuối cùng** của Phase 4 — Phase 4 đã claim "100% EN / 100% VI" nhưng dynamic error portion vẫn EN; sau Bước 3 thì claim đó mới đúng tuyệt đối.
5. **Wire format break có thể chấp nhận**: hiện tại KHÔNG có client API consumer (Blazor UI gọi service trực tiếp). Nếu tương lai có client → enum-as-string vẫn dễ đọc; thêm `Description` attribute cho mỗi enum value để document thêm khi cần.

Phương án B làm fallback nếu anh muốn keep-the-string-shape (đỡ surprise). Phương án C em **không khuyến nghị** vì drift risk.

### Branch base — đề xuất stack

| Approach | Pros | Cons |
|---|---|---|
| **Stack** trên `feat/phase5-hub-auth` (PR #5) | `WorkOrders.razor` đã bị Bước 2 sửa lines 94-109; Bước 3 sửa line 131 — stack tránh merge conflict | Cần merge PR #5 trước PR #6 |
| Branch từ `main` (PR #4 đã merge) | 2 PR độc lập, merge theo thứ tự nào cũng được | Có conflict `WorkOrders.razor` khi merge cái sau (chỉ vài dòng, dễ giải quyết) |

**Đề xuất stack** — đỡ phải giải quyết conflict thủ công, audit trail rõ thứ tự.

---

## 4. Rủi ro chi tiết

| Hạng mục | Rủi ro | Mức | Giảm thiểu |
|---|---|---|---|
| Vỡ luồng Advance/guard hiện tại | Chỉ đổi cơ chế trả lỗi, **không đổi logic guard** | **THẤP** | Smoke test 7 transition × 2 nhánh (allowed + blocked) — cùng đường code cũ |
| Vỡ test/i18n coverage Phase 4 | **CẢI THIỆN** thay vì vỡ: gap cuối cùng (dynamic error portion EN giữa VI message) được đóng | **TÍCH CỰC** | Verify VI culture: gây 1 guard fail → kỳ vọng message 100% tiếng Việt |
| Đụng DB | **Không** — chỉ đổi C# type, không migration, không seed | — | — |
| Wire format BREAKING trên `/api/workorders/{id}/advance` | JSON đổi `"error"` → `"errorCode"`; giá trị đổi free-form text → enum name | **THẤP** (no current consumer) | Document ở PR; nếu Phase 6+ có kiosk consumer thì stable từ giờ |
| `[FromQuery] string? user` không đụng nhưng tính Persistence: `wo_status_history.Action = "Advance"` | Không đụng — chỉ stamp action name, không stamp error | — | — |
| Localize chỉ Web tier, server log sẽ thấy enum name | DevOps đọc log thấy `"RequiresSetupConfirmed"` thay vì English đầy đủ | **THẤP** | Enum name self-describing; nếu muốn English-in-log thì thêm `Description` attribute + helper khi log |
| `Loc["wo.error.unknown"]` fallback khi enum value mới chưa map | Hiển thị key thô | **THẤP** | Phương án A dùng exhaustive switch hoặc Dictionary với guard — KeyNotFoundException ném ra ngay sprint dev, không stale |

**Không** đụng DB; không đụng migration; không đụng auth/RBAC; không đụng SignalR.

---

## 5. i18n keys cần thêm (EN + VI)

### EN (`SharedResource.resx`)

```xml
<data name="wo.error.already_at_final_step" xml:space="preserve">
  <value>Work Order is already at the final step.</value></data>
<data name="wo.error.requires_spec_materials" xml:space="preserve">
  <value>Requires an approved Spec and ready materials.</value></data>
<data name="wo.error.requires_setup_confirmed" xml:space="preserve">
  <value>Requires machine setup confirmation.</value></data>
<data name="wo.error.ipqc_not_passed" xml:space="preserve">
  <value>IPQC has not yet Passed.</value></data>
<data name="wo.error.no_production_yet" xml:space="preserve">
  <value>No production recorded yet (ProducedQty = 0).</value></data>
<data name="wo.error.fqc_not_passed" xml:space="preserve">
  <value>FQC has not yet Passed.</value></data>
<data name="wo.error.oqc_or_rohs_not_met" xml:space="preserve">
  <value>OQC has not yet Passed or RoHS not met.</value></data>
<data name="wo.error.invalid_transition" xml:space="preserve">
  <value>Invalid step transition.</value></data>
<data name="wo.error.wo_not_found" xml:space="preserve">
  <value>Work Order not found.</value></data>
<data name="wo.error.unknown" xml:space="preserve">
  <value>Unknown error.</value></data>
```

### VI (`SharedResource.vi.resx`)

```xml
<data name="wo.error.already_at_final_step" xml:space="preserve">
  <value>Work Order đã ở bước cuối.</value></data>
<data name="wo.error.requires_spec_materials" xml:space="preserve">
  <value>Cần Spec đã duyệt và vật tư sẵn sàng.</value></data>
<data name="wo.error.requires_setup_confirmed" xml:space="preserve">
  <value>Cần xác nhận setup máy.</value></data>
<data name="wo.error.ipqc_not_passed" xml:space="preserve">
  <value>IPQC chưa Pass.</value></data>
<data name="wo.error.no_production_yet" xml:space="preserve">
  <value>Chưa ghi nhận sản lượng (ProducedQty = 0).</value></data>
<data name="wo.error.fqc_not_passed" xml:space="preserve">
  <value>FQC chưa Pass.</value></data>
<data name="wo.error.oqc_or_rohs_not_met" xml:space="preserve">
  <value>OQC chưa Pass hoặc RoHS không đạt.</value></data>
<data name="wo.error.invalid_transition" xml:space="preserve">
  <value>Chuyển bước không hợp lệ.</value></data>
<data name="wo.error.wo_not_found" xml:space="preserve">
  <value>Không tìm thấy Work Order.</value></data>
<data name="wo.error.unknown" xml:space="preserve">
  <value>Lỗi không xác định.</value></data>
```

**10 key × 2 file = 20 entries**. Field tên (`SpecVersionId`, `MaterialsReady`, `SetupConfirmed`, `ProducedQty`) bỏ khỏi text user-facing để dễ dịch + đỡ rườm rà; chi tiết kỹ thuật vẫn có trong source code (tên field tự diễn giải).

---

## 6. Files dự kiến đụng

| File | Hành động | Ước LOC |
|---|---|---|
| `src/CCL.MES.Domain/StateMachine/WorkOrderError.cs` *(mới)* | enum 9 value | ~12 |
| `src/CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs` | `TransitionResult` đổi `string? Reason` → `WoTransitionError? Error`; 9 callsite trong switch | ~10 sửa, +0 thêm |
| `src/CCL.MES.Application/Dtos.cs:62` | `AdvanceResult` đổi `string? Error` → `WoTransitionError? ErrorCode` | ~1 |
| `src/CCL.MES.Application/Services/WorkOrderService.cs:61,65` | Đổi `"Work Order not found."` + `check.Reason` → enum | ~4 |
| `src/CCL.MES.Web/Services/WoErrorKeys.cs` *(mới)* | Dictionary map enum → resource key | ~25 |
| `src/CCL.MES.Web/Pages/WorkOrders.razor:131` | Đổi nội suy `res.Error` → `Loc[WoErrorKeys.KeyFor(res.ErrorCode)]` | ~3 |
| `src/CCL.MES.Web/Resources/SharedResource.resx` | +10 key `wo.error.*` | ~30 |
| `src/CCL.MES.Web/Resources/SharedResource.vi.resx` | +10 key `wo.error.*` | ~30 |
| `WorkOrderStateMachine.cs:11-14` | Gỡ comment "Phase 4+ should swap" + thay bằng "Phase 5 — error-code emitted; see WoErrorKeys" | ~3 |
| `WorkOrderService.cs:56-60` | Cùng pattern | ~3 |

**Tổng**: ~120 LOC, 10 files. Không đụng DB, không đụng auth, không đụng SignalR, không đụng RBAC, không đụng Resources cho các tab khác.

---

## 7. Kế hoạch test + DoD

### Smoke test (manual)

| # | Bước | Kỳ vọng |
|---|---|---|
| 1 | `dotnet build` | 0 warning, 0 error |
| 2 | EN culture + admin: Mở `/workorders`, advance từ PrePressCheck KHI CHƯA mở khoá (chưa set MaterialsReady) | `_message`: **"Cannot advance: Requires an approved Spec and ready materials."** |
| 3 | VI culture + admin: cùng kịch bản | `_message`: **"Không thể chuyển: Cần Spec đã duyệt và vật tư sẵn sàng."** (toàn bộ tiếng Việt, KHÔNG còn EN) |
| 4 | EN: Advance khi Spec OK + materials OK + setup OK + IPQC chưa pass | `_message`: "Cannot advance: IPQC has not yet Passed." |
| 5 | VI: cùng kịch bản | `_message`: "Không thể chuyển: IPQC chưa Pass." |
| 6 | API curl: `POST /api/workorders/{id}/advance` với WO id không tồn tại | JSON: `{"ok": false, "errorCode": "WorkOrderNotFound", "currentStep": "-"}` (HTTP 400) |
| 7 | API curl: ID tồn tại nhưng chưa đủ guard | JSON: `{"ok": false, "errorCode": "RequiresSpecAndMaterials", "currentStep": "PrePressCheck"}` |
| 8 | Happy path: Mở khoá → IPQC pass → Advance | `_message`: "Advanced to step: ReadyToRun" (EN) hoặc "Đã chuyển sang bước: ReadyToRun" (VI) — KHÔNG ĐỔI |
| 9 | NPI rows + Users | 43 / 2127 / 38441 / 20530 / 2 — không đổi |
| 10 | Forbidden dirs | Ops Control v1.2, CMES, Old ver, SpecHub không bị đụng |

### DoD

- [ ] 9 string EN hardcoded trong `WorkOrderStateMachine.cs` + 1 string trong `WorkOrderService.cs` được thay bằng enum.
- [ ] Comment "Phase 4+ should swap" gỡ khỏi 2 file (thay bằng "Phase 5 — error-code emitted; see WoErrorKeys").
- [ ] 10 i18n key `wo.error.*` ở cả EN + VI resx.
- [ ] `dotnet build` clean.
- [ ] Smoke 2+3+4+5 chứng minh VI culture 100% tiếng Việt cho dynamic error portion.
- [ ] Smoke 6+7 chứng minh API wire format đổi đúng kỳ vọng.
- [ ] Smoke 8 chứng minh happy path không đổi.
- [ ] Data integrity: 43/2127/38441/20530/2 unchanged.
- [ ] PR `feat/phase5-error-codes` base = `feat/phase5-hub-auth` (stack) — note rõ trong PR description.
- [ ] STOP, báo cáo, chờ duyệt PR #6.

---

## 8. Câu hỏi cho em duyệt

1. **Chọn phương án nào?** (Đề xuất: A — enum trong Domain)
2. **Branch base**: stack lên `feat/phase5-hub-auth` (đề xuất, đỡ conflict `WorkOrders.razor`) hay branch từ `main` (PR #4 merged, PR #5 chưa)?
3. **Tên enum**: `WoTransitionError` (cụ thể) hay `WoErrorCode` (rộng hơn, mở chỗ cho lỗi không phải transition về sau)?
4. **Resource key prefix**: `wo.error.*` (đề xuất, gọn) hay `workorders.error.*` (đồng bộ với `workorders.msg.*`, `workorders.col.*`)?

Sau khi em duyệt 4 mục em tạo branch + code + commit + push + PR + STOP.
