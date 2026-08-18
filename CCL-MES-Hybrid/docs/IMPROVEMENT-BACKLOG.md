# CCL-MES — Backlog cải tiến kiến trúc (audit 2026-08-18)

> Mỗi mục dưới đây là một **task chạy được bằng vòng lặp** trong
> [`AGENT-LOOP.md`](./AGENT-LOOP.md): đã gắn sẵn work-class, agent chủ trì,
> skill bắt buộc, và **tiêu chí nghiệm thu đo được**. Không mục nào là "nên
> cân nhắc" — hoặc làm, hoặc đóng lại kèm lý do.
>
> **Đã đóng trong đợt này:** A0 (hệ agent + vòng lặp), D1/D2 (thang token +
> density), E (bộ agent 4 tầng), và 4 gate mới nối vào CI.

---

## Đã xong (đợt 2026-08-18)

| # | Việc | Bằng chứng |
|---|---|---|
| ✅ A0 | Vòng lặp 6 pha + roster 8 agent + 10 skill + router `CLAUDE.md §0` | `AGENT-LOOP.md`, `.claude/agents/`, `.claude/skills/` |
| ✅ D1 | Thang chữ/khoảng cách/bo góc/bóng/motion/focus vào `:root` (no-op) | `app.css`, braces 1993/1993 cân, 0 token vòng |
| ✅ D2 | Hai density `office` / `shopfloor` (tap 44px, font 16px) | `:root[data-density="shopfloor"]` + 4 class tiện ích |
| ✅ D5 | 4 gate mới + runner `gate-all.sh` + job CI `gates` | 8/8 PASS, self-test PASS→FAIL→PASS |
| ✅ — | Lesson L40–L43, mỗi lesson có cơ chế chặn | `LESSONS-LEARNED.md` |
| ✅ D3a | **CCL iX foundation** — 6 nguyên tắc + rail/page-head/tile/pill/toolbar/grid/nút, và lớp tương thích restyle class cũ (toàn app đổi diện mạo, Razor churn tối thiểu) | `ix.css` 520 dòng, 0 hex thô, 0 font-size thô |
| ✅ D2b | Công tắc density trong Settings → Giao diện + rail thu gọn có nhớ | 6 bUnit test, ảnh 2 density |
| ✅ — | Trang tham chiếu thiết kế link CSS thật (không trôi) | `docs/design-system/index.html` |

---

## A1 — Cutover: khai tử legacy Blazor Server

**Vấn đề.** Hai UI song song từ Phase 10 tới nay (đã qua P11). Hai hệ i18n:
`SharedResource[.vi].resx` (legacy) và `TranslationCatalog` 80 partial /
2.171 key (Hybrid). Mỗi tính năng nghiệp vụ mới có nguy cơ làm hai lần.

**Work-class** W3 + W7 · **Agent** `mes-process-architect` → `cmes-implementer`
· **Skill** `cmes-thin-controller`, `cmes-i18n-parity`

**Nghiệm thu**
- [ ] 0 route legacy còn phục vụ; `src/CCL.MES.Web` chỉ còn buildable để rollback
- [ ] `CCL.MES.sln` job CI hoặc bị gỡ, hoặc đổi tên rõ là "frozen baseline"
- [ ] Còn **một** hệ i18n; `.resx` không nhận key mới (thêm vào `gate-i18n-parity`)
- [ ] LOC giảm ≥ 14.000

**STOP-gate.** Ngày cutover phải Henry chốt — có người đang dùng.

---

## A2 — Rút luật nghiệp vụ khỏi controller (L40)

**Vấn đề.** 22 `SaveChangesAsync` trong controller · 20/33 controller chạm
`DbContext` · `WoQcReviewController` 1.460 dòng. Chặn đường mở API cho ERP/máy.

**Work-class** W3 · **Agent** `cmes-implementer` · **Skill** `cmes-thin-controller`

**Thứ tự đề xuất** (nặng → nhẹ, mỗi cái một PR):
`WoQcReview` → `IpqcReview` → `Prepress` → `Routing` → `SemiStock`

**Đã làm — lát 1/n:** `OqcSignaturePolicy` tách khỏi `WoQcReviewController` (L47). Luật 3 chữ ký giờ là hàm thuần + 16 unit test chạy 33 ms. LOC controller gần như không đổi (1502 → 1500) — lát này mua **khả năng kiểm chứng**, không mua số dòng.

**Còn lại của riêng `WoQcReviewController`:** tách `QcGate` (điều kiện readiness/ngưỡng), đường ảnh (4 endpoint photo ~340 dòng), `summary-report`. Đó mới là phần kéo được ratchet xuống.

**Nợ vị trí:** `OqcSignaturePolicy` đang đặt ở `CCL.MES.Api/Policies/` vì `src/CCL.MES.Domain` là baseline read-only tới khi cutover xong (A1). Sau cutover nên chuyển về Domain.

**Nghiệm thu**
- [ ] Tách `SignaturePolicy` (3 chữ ký, Inspector≠Reviewer≠Approver) ra Domain, unit-test không cần `WebApplicationFactory`
- [ ] `QcGate`, `LegAdvancePolicy`, `SemiStockPolicy` tương tự
- [ ] `gate-thin-controller.sh` BASELINE **22 → ≤10**, số controller >400 dòng **8 → ≤4**
- [ ] Test cũ không sửa mà vẫn xanh (chứng minh hành vi không đổi)

---

## A3 — Observability

**Vấn đề.** `grep` toàn bộ `.csproj`: **không** OpenTelemetry, **không** Serilog,
**không** metrics endpoint. Hệ chạy 3 ca mà mọi sự cố phải điều tra bằng `lsof`.

**Work-class** W8 · **Agent** `mes-integration-architect` · **Skill** `cmes-verify-evidence`

**Nghiệm thu**
- [ ] Log có cấu trúc, mỗi request mang `TraceId` + `WoNo` + `Actor` + `Shift`
- [ ] Trace phủ: `/advance`, mọi endpoint QC, mọi import master data
- [ ] SLO công bố: `advance` p95 < 300ms · 0 WO wedged/tuần · sync gap < 60s
- [ ] Sự cố tiếp theo điều tra được **không cần** SSH vào máy

**STOP-gate.** Chọn exporter (console / file / OTLP collector) là quyết định
hạ tầng — Henry chốt trước khi thêm package.

---

## B1 — Backbone ISA-95

**Vấn đề.** `WorkCenter` là bảng phẳng (`Area` chỉ là `string?`); `Machine`
**không có FK tới WorkCenter**; không có Equipment Class. Hệ quả: KPI không
roll-up theo Area/Line, OEE không benchmark được, không có đường lên Level 4.

**Work-class** W1 · **Agent** `mes-process-architect` · **Skill** `cmes-migration-abc`

**Hình dạng đích** `Site → Area → ProcessLine → WorkCenter → Machine` +
`EquipmentClass`; `WoLeg` / `WoQcCheck` / `ProductionLog` mang `WorkUnitId`.

**Nghiệm thu**
- [ ] Migration **additive**: cột mới nullable, dữ liệu cũ đọc được nguyên vẹn
- [ ] Backfill idempotent, chạy lại lần 2 không đổi gì
- [ ] Một truy vấn duy nhất trả OEE theo Area và theo Equipment Class
- [ ] Phase A/B/C đủ bằng chứng: `.schema` · rowcount · SHA256

---

## B2 — Process Model: quy trình là DỮ LIỆU, không phải code

**Vấn đề — và là khoảng cách lớn nhất so với triết lý Siemens/JustPerform.**
Chỉ thư viện QC đã data-driven. Routing gate, ngưỡng, quy tắc chữ ký, RBAC vẫn
là code + migration ⇒ thêm một luồng sản phẩm mới phải mở PR. CCL-MES hiện là
**ứng dụng được lập trình theo quy trình**, chưa phải **nền tảng cấu hình được quy trình**.

**Work-class** W1 + W2 · **Agent** `mes-process-architect` + `mes-quality-architect`

**Hình dạng đích**
```
ProcessModel (versioned · approve · effective-date · đóng băng vào WO lúc phát hành)
 ├── LegTemplate[]   kind · dependency HARD/SOFT · surface profile
 ├── GateRule[]      điều kiện advance — DỮ LIỆU, không phải switch-case
 ├── SignatureRule[] số chữ ký · ràng buộc ≠ · role được ký
 └── ThresholdSet[]  nối vào chuỗi resolve Product → Profile → Default đã có
```
`WorkOrderStateMachine` chuyển từ hardcode sang **thông dịch model đã đóng băng
trong WO** — tính bất biến của bằng chứng giữ nguyên.

**Nghiệm thu**
- [ ] Thêm một luồng sản phẩm mới **không cần PR**, chỉ cấu hình + approve
- [ ] WO cũ vẫn chạy đúng model đã đóng băng khi phát hành (không hồi tố)
- [ ] `WorkOrderStateMachineLegacyParityTests` vẫn xanh

**STOP-gate.** Đụng contract state machine ⇒ sửa `P10.7-WO-STATE-CONTRACT.md`
trước, có chữ ký, rồi mới code.

---

## C1 — Đóng vòng chất lượng: NC → Disposition → CAPA → SPC

**Vấn đề.** Hệ mới dừng ở Pass/Fail. `DefectCode` trong thư viện v5 đã sẵn làm
khoá nhưng chưa có vòng nào dùng nó để cải tiến.

**Work-class** W4 · **Agent** `mes-quality-architect` · **Skill** `cmes-audit-emit`

**Nghiệm thu**
- [ ] `NonConformance` → `Disposition (Rework / Scrap / Use-As-Is)` → `CAPA`
- [ ] Biểu đồ SPC theo `DefectCode` × ProcessLine × thời gian
- [ ] Disposition nào cũng để lại vết audit + không ghi đè bằng chứng đã đóng băng

---

## C2 — Quality Record Pack (một nút)

**Đây là mục duy nhất trong backlog bán được cho khách hàng.** Gộp as-planned
(Spec revision + routing snapshot) và as-built (leg actual + material lot scan +
chữ ký + ảnh QC) thành **một PDF** cho audit khách hàng.

**Work-class** W4 + W5 · **Agent** `mes-quality-architect` + `cmes-shopfloor-ux`
· **Skill** `cmes-spec-print` (đã có luật in native, L39)

**Nghiệm thu**
- [ ] Một WO → một PDF, đủ digital thread, không cần ghép tay
- [ ] Nội dung lấy **hoàn toàn** từ snapshot đã đóng băng (không JOIN live — L29)
- [ ] In được qua `IPrintService` native trên maccatalyst

---

## C3 — Cổng ERP: outbox thay import tay

**Vấn đề.** Master data IFS vào bằng CSV/XLSX thủ công; không adapter, không
outbox, không reconciliation. Và `SyncEnvelope<T>` **đã định nghĩa nhưng chưa
nơi nào dùng** — offline-first mới là ý định, chưa là năng lực.

**Work-class** W8 · **Agent** `mes-integration-architect`

**Nghiệm thu**
- [ ] Outbox ghi cùng transaction nghiệp vụ; worker đẩy + retry + dead-letter
- [ ] Mọi mutation qua ranh giới nhận `Idempotency-Key`; retry không tạo dòng thứ hai
- [ ] Mỗi lần đồng bộ sinh reconciliation report: vào / bỏ qua / lỗi + **vì sao**
- [ ] **Chốt dứt khoát** về `SyncEnvelope`: làm thật, hoặc tuyên bố online-required
      và xoá. Để lửng lơ là tệ nhất.

---

## D6 — Sau audit 20 tab (2026-08-18) — **ĐÃ ĐÓNG TOÀN BỘ**

| # | Việc | Cách đóng |
|---|---|---|
| ✅ D6.1 | 26 loại emoji tượng hình (44 lượt trong markup + 107 chuỗi dịch) | `Icon.razor` — 26 icon line 24×24, nhận `currentColor`. Emoji trong chuỗi C# đổi sang glyph đơn sắc (component không nhúng được vào biểu thức chuỗi). Gate mới chặn icon quay lại chuỗi dịch |
| ✅ D6.2 | `ix-page-head` 0/33 trang | `PageHead.razor` (eyebrow · tiêu đề · phụ đề · hành động · hairline), **25/25 trang** dùng. Vẫn render `<h1 class="page-title">` nên không test nào vỡ |
| ✅ D6.4 | Đồng hồ khổng lồ ở Home | Bỏ. `HomeTests` đổi sang khoá bất biến mới: giờ chỉ hiện MỘT nơi |
| ✅ D6.5 | Lưới Modules lẻ ô | `auto-fit minmax(210px,1fr)` |
| ✅ D6.6 | `PLANNER` vẽ bằng pill màu đặc | Hạ về nhãn trung tính — phân biệt bằng CHỮ, màu để dành cho tình trạng |
| ✅ D6.7 | Zebra ở Specs vs hairline ở NPI | Một cách vẽ bảng: hairline |
| ✅ D6.8 | Viền trái màu ở card máy | Hairline (trạng thái đã có pill ở góc) |
| ✅ D6.9 | Focus ring xanh quanh h1 module QMS | Khử chung cho mọi heading nhận focus khi điều hướng SPA |

| ✅ D6.3 | Cột số căn trái ở lưới NPI | Hạ tầng đã có sẵn (`ColClass()` trả `grid-col-num`) nhưng class **chỉ áp cho `<th>`, không cho `<td>`** — đó là toàn bộ nguyên nhân. Nay `<td>` nhận cùng class + `tabular-nums`. Gate mới bắt tĩnh nếu ai tách lại |

**Nợ còn treo (chưa chứng minh được nguyên nhân):** sau khi sửa, cột **QUANTITY**
căn phải đúng nhưng **SCRAP FACTOR vẫn căn trái**, dù cả hai đi qua **cùng một**
biểu thức `ColClass(col.Id)` và cùng một nhánh `switch`. Chưa có giải thích PROVEN
nên KHÔNG vá mò — cần một lượt kiểm DOM thật (không dựng được từ ngoài WebView,
phải mở Web Inspector trên máy) rồi mới sửa. Ghi ở đây thay vì im lặng.

## D3/D4 — Component contract + tách `app.css`

**Work-class** W5 · **Agent** `cmes-shopfloor-ux` · **Skill** `cmes-design-tokens`

**Nghiệm thu**
- [x] ~~Bộ pill trạng thái một cách vẽ duy nhất~~ — CSS xong (`.ix-pill*`)
- [x] ~~`StatusPill` + `PhaseVisual`~~ — nguồn sự thật duy nhất, 5 bậc mang ý nghĩa vận hành; gate hard-fail nếu thêm phase mà quên bảng màu (L46)
- [ ] **Di nốt 6 dashboard còn dùng class cứng** `rs-head-phase rs-phase-*` (`Ipqc`/`Fqc`/`Oqc`/`Setting`/`QaApproval`/`ShippedSummary`) sang `StatusPill`. Đã di 3 chỗ có logic trùng lặp + chỗ nhét hex thô; 6 chỗ còn lại là class tĩnh nên để lại cho một PR riêng, đổi đồng loạt sẽ đổi diện mạo 6 màn cùng lúc
- [ ] **Nhãn tiếng Việt cho 14 phase WO** — hiện hiện token thô (`IPQC_WAIT`) cho người vận hành đọc. Cần người vận hành chốt từ ngữ, không nên để component tự bịa
- [ ] `DataGrid` · `StepTimeline` · `SignaturePad` · `EvidenceCard` thành component chung
- [ ] `app.css` xếp `@layer reset → tokens → primitives → patterns`, còn ≤ **2.000** dòng
- [ ] `gate-design-tokens` BASELINE **527 → ≤200**
- [ ] Mọi surface Operator có screenshot 2 density trong PR

---

## Nợ kỹ thuật đã phát hiện, chưa xếp lịch

| Việc | Ghi chú |
|---|---|
| ~~Soak test `Concurrent_*_N_equals_10` flake~~ — **ĐÃ ĐÓNG, và chẩn đoán ban đầu SAI** | Mục này từng ghi "assert quá chặt cho engine có retry nội bộ, nên tách `Category=Soak`". **Sai.** Đo thật (20 lượt × 10 request) cho thấy wire **tất định tuyệt đối** — luôn 1 winner + 9 × 409 — còn thứ dao động là **số dòng audit**, và chờ 3s không hội tụ ⇒ **mất dữ liệu thật**, không phải test giòn. Nguyên nhân: 7/11 nhánh `catch (DbUpdateConcurrencyException)` trả 409 mà không emit `WO_STATE_CONFLICT`. Đã vá + gate chặn (L45). Full suite Api **504/504**. **Không sửa một assert nào** — test đúng từ đầu. Bài học cho backlog: đừng gắn nhãn "flake" trước khi đo phân bố kết quả. |
| `AngleSharp 1.2.0` NU1902 moderate vulnerability | Cảnh báo restore ở `CCL.MES.Hybrid.Razor.Tests` |
| 3 gate cũ chưa có `--self-test` | `gate-row-actions` · `gate-floating-showcard` · `gate-spec-print` — `gate-all --self-test` báo SKIP minh bạch, nhưng detector hỏng thầm sẽ không ai biết |
