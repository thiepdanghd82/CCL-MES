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


## ~~i18n — bảng FQC/OQC vẫn khoá một ngôn ngữ (nợ còn lại của L60)~~ ✅ ĐÃ XONG

| | |
|---|---|
| **Work-class** | W4 (chất lượng) + W5 (UI) |
| **Agent** | `mes-quality-architect` ra thiết kế → `cmes-implementer` |
| **Skill** | `cmes-i18n-parity` + `cmes-audit-emit` |
| **Kết quả** | Xong 2026-08-28 — xem [L62](LESSONS-LEARNED.md#l62). Hoá ra bệnh KHÁC dự đoán: không phải sai ngôn ngữ mà KHÔNG CÓ nhãn nào (UI render thẳng ItemKey). Đã nối nhãn từ snapshot ra DTO + thêm bản EN đóng băng tại điểm thắt ResolveSnapshot. |
| **Bối cảnh** | [L60](LESSONS-LEARNED.md#l60) đã sửa xong đường IPQC: nhãn hạng mục · nhóm · METHOD · SPEC nay đóng băng CẢ HAI ngôn ngữ trong `WoIpqcCheckItems`. FQC/OQC **chưa**, và hỏng theo kiểu khác: `WoQcCheckItems` **không có cột nhãn nào cả** (chỉ `ItemKey` + `Status` + trường NG) — nhãn đến từ `WoQcChecks.ItemsProfileSnapshotJson`. Nên fix của IPQC không áp thẳng sang được. |
| **Việc phải làm** | 1. Xác định UI FQC/OQC lấy chuỗi hiển thị từ đâu (JSON snapshot hay `QcProfileSeed` lúc render). 2. Nếu từ JSON: bổ sung khoá EN vào shape snapshot + đọc theo ngôn ngữ, đường lùi VI. 3. Nếu render-time từ seed: đó là bệnh NGƯỢC lại (không đóng băng gì) — cần quyết định có đóng băng không, vì hồ sơ FQC/OQC cũng có chữ ký. 4. Tái dùng `CheckItemVocabularyEn` (thư viện dùng chung cho cả 3 stage qua cờ `Ipqc`/`Fqc`/`Oqc`). |
| **Tiêu chí nghiệm thu** | Test kiểu `IpqcDashboardLanguageTests` cho `FqcDashboard` + `OqcDashboard`: bấm EN thì bảng đổi chữ; thiếu bản dịch thì rơi về VI, không để ô trống; đổi ngôn ngữ giữa chừng không văng tab/section đang mở. Test phải ĐỎ khi hoàn nguyên fix. |
| **Ghi chú** | 59 dòng `PNC-*` (PRESS_CNC) trong `WoIpqcCheckItems` cũng không có bản EN vì **không còn nguồn trong thư viện v5** (v5 chỉ có LABEL · SILK). Đây là câu hỏi cho Ops — thư viện v5 có cố ý bỏ PRESS_CNC không, hay thiếu? Không phải lỗi code. |


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
- [x] ~~Tách `SignaturePolicy` (3 chữ ký) + policy thuần~~ — `OqcSignaturePolicy` +
      `IpqcJudgmentPolicy` + `RunningSurfacePolicy` + `PrepressPolicy` +
      `RoutingPolicy` (unit-test không cần `WebApplicationFactory`)
- [x] ~~`gate-thin` BASELINE **22 → ≤10**~~ — **ĐÓNG: 22 → 9** (2026-08-23)
- [ ] số controller >400 dòng **8 → ≤4** — **8 → 6** (còn RunningSurface 597 ·
      Prepress 584; xuống <400 cần gom-nén 8 endpoint-handler concurrency —
      pure-LOC, churn lớn, HOÃN: không mua thêm save/L45/verifiability)
- [x] ~~Test cũ không sửa mà vẫn xanh~~ — 846 Api test 0 fail mọi lát (byte-identical + soak)

### A2 — đã đóng đợt 2026-08-23 (15 PR #191–#205)

Kiến trúc WO-mutation **hợp nhất hoàn toàn** — mọi đường commit/conflict/prelude
qua MỘT chỗ test + gate:

| Thành phần (Api/Services · Api/Policies) | Vai trò |
|---|---|
| `WoMutationExecutor` | save + L45 conflict (`SaveAndResolveAsync` / `ResolveWoConflictAsync` / `PrecheckStaleAsync`) — dùng bởi RunningSurface · Prepress · WoQc · Ipqc · WoQcPhoto · AdminWorkOrders · **/advance** |
| base `WoMutationControllerBase.PreludeAsync` | prelude concurrency (onConflict nhận `WorkOrder`) — 1 nơi cho mọi surface (Prepress giữ inline vì 428-message + phase-guard riêng) |
| `WoQcCheckMaterializer` · `IpqcCheckMaterializer` | GET lazy-materialise + auto-sync (SaveChanges rời controller) |
| `SemiLotMutationService` | commit Semi-Stock (domain SemiLot, RowVersion L38) |
| `EtagCodec` | ETag↔RowVersion dùng chung |
| 5 `*Policy` | luật thuần (parse/validate/gate) unit-test được |

**Enforcement theo code:** `gate-audit-emit` (C) L45 quét CẢ `Controllers/` +
`Services/` + nhận diện delegation executor (negative-proof); `gate-thin`
BASELINE_SAVE **9**, BASELINE_FAT **6**; 12 gate đều có `--self-test` (SKIP 0).

**Còn lại (đều hoãn có lý do):** fat<400 cho RunningSurface/Prepress (pure-LOC
grind); `WoQcPhoto` item-insert + `CheckItemLibrary`/`Auth` save (domain khác,
save đã ≤10 nên không cần). `*Policy`/service đặt tạm ở `Api/` — chuyển về
Domain/Application sau cutover A1.

---

## ~~A4 — Tách vai Engineer làm hai: kỹ sư SẢN XUẤT và kỹ sư CHẤT LƯỢNG~~ ✅ ĐÃ XONG 2026-09-14

**Thiệp chốt 2026-09-14.** Xưởng có hai ngạch kỹ sư khác nhau, và luồng khác nhau:

| Luồng | Ai xác nhận |
|---|---|
| Sản xuất (SETTING · RUNNING · prepress) | **kỹ sư sản xuất** |
| IQC · IPQC · OQC · FQC | **kỹ sư chất lượng** |

Ký duyệt waiver vật tư lệch: **cả hai vai đều ký được**, miễn KHÁC người đã
xác nhận dòng (luật 4-mắt giữ nguyên).

**Vấn đề.** Hôm nay chỉ có MỘT vai `Engineer` gánh cả hai ngạch. Đo 14-09:
44 chỗ nhắc tới vai này, trải 18 file, 15 policy, 10 `AuthorizeView`. Hệ quả
cụ thể: trong luồng IPQC kỹ sư chất lượng vừa xác nhận dòng vật tư vừa ký
waiver được ⇒ luật 4-mắt chặn mỗi ngày, và lối thoát rẻ nhất là tắt cờ
`OPS_IPQC_REQUIRE_DISTINCT_MATERIAL_WAIVER` — mất luôn 4-mắt thật.

`Users.Role` là **TEXT** nên thêm vai KHÔNG cần migration.

**Work-class** W6 · **Agent** `cmes-implementer` · **Skill** `cmes-rbac-matrix` (+ `cmes-audit-emit`)

**Nghiệm thu**
- [ ] `UserRole` có `EngineerProduction` + `EngineerQuality`; `Engineer` cũ giữ
      lại làm bí danh đọc-được cho dữ liệu cũ, KHÔNG cấp mới
- [ ] `IpqcSubmit` (xác nhận dòng vật tư · chấm hạng mục IPQC) = Admin · QC · **EngineerQuality**
- [ ] `EngineerWaive` (ký waiver) = Admin · Supervisor · **EngineerProduction** · **EngineerQuality**
- [ ] `IpqcSignaturePolicy.WaiverSignerRoleAllowed` khớp `EngineerWaive` — có
      gate hoặc test khoá hai danh sách này không lệch nhau (cùng bệnh với L83)
- [ ] Người dùng `Engineer` đang tồn tại được gán lại vai tường minh, không
      để hệ tự đoán
- [ ] 10 `AuthorizeView` rà lại từng cái: cái nào là việc SẢN XUẤT, cái nào là
      việc CHẤT LƯỢNG — không thay máy móc `Engineer` → cả hai vai
- [ ] Test 4-mắt hiện có vẫn xanh, và thêm ca "kỹ sư chất lượng xác nhận rồi
      kỹ sư sản xuất ký" = ĐƯỢC

**Đã đóng.** 10 test `A4EngineerSplitRbacTests` (mỗi surface: 1 vai được phép +
≥1 vai bị chặn) · 2 test khoá hai danh sách vai không lệch nhau · bài học **L95**.

**Phát sinh khi làm, đã xử lý trong cùng đợt:**
- Cổng duyệt bản vẽ đã phân ngạch bằng `Role="Engineer" + Department` (npi ·
  production · qc) từ trước. Đã hoà hai mô hình thay vì đè lên nhau.
- `QcSpecController` — soạn kế hoạch QC chỉ có `[Authorize]` trần ⇒ Operator
  cũng ghi được. Đã thêm policy `QcPlanWrite`.

**Còn nợ (đo 2026-09-14):** 33 endpoint ghi khác cũng không có policy tường
minh. Phần lớn chính đáng (đăng nhập · tự đổi mật khẩu · heartbeat thiết bị ·
thao tác vận hành trên chuyền) nhưng CHƯA ai rà từng cái và ghi lại lý do.
Xem mục A5.

**Bẫy đã biết.** Đây là RBAC nên mọi thay đổi mặc định vai là STOP-gate
(`cmes-secrets-jwt`). Và đừng thay `Engineer` → `EngineerProduction,EngineerQuality`
bằng find/replace: một nửa số chỗ ấy là quyền ĐỌC spec (cả hai vai nên có),
nửa còn lại là quyền KÝ (phải phân biệt). Thay mù là cấp quyền ký cho người
không nên có.

---

## ~~A5 — Rà 33 endpoint ghi không có policy tường minh~~ ✅ ĐÃ XONG 2026-09-15

**Vấn đề.** Luật vàng #1 của skill `cmes-rbac-matrix`: mọi endpoint mutation
phải có `[Authorize(Policy=...)]` tường minh; dựa vào `FallbackPolicy` nghĩa là
"ai đăng nhập cũng ghi được". Đo 2026-09-14 khi làm A4: **33 endpoint** như vậy
(sau khi A4 đã đóng 2 cái nặng nhất).

Phần lớn có lý do chính đáng — `AuthController` (đăng nhập/refresh/logout),
`SettingsController` (tự đổi mật khẩu, tự sửa hồ sơ), `DevicesController`
(heartbeat/scan-log), và các thao tác vận hành trên chuyền mà mọi vai đứng máy
đều làm. Nhưng **chưa ai rà từng cái và ghi lại lý do**, nên không phân biệt
được "cố ý mở" với "quên gác".

**Đã đóng.** Đo lại cho đúng: 32 endpoint, chia BA nhóm chứ không một —
(1) cố ý mở · (2) đã gác ở tầng service · (3) thật sự bỏ ngỏ. Kết quả:
18 → policy `ShopFloorWrite`, 14 → dấu `// RBAC-OPEN: <lý do>`, 2 → `QcPlanWrite`
(đã làm ở A4). Gate 27 `gate-endpoint-policy.sh` ratchet baseline **0**.
13 test nhắm vào rủi ro thật là SIẾT NHẦM. Bài học **L96**.

**Work-class** W6 · **Agent** `cmes-implementer` · **Skill** `cmes-rbac-matrix`

**Nghiệm thu**
- [ ] Mỗi endpoint trong danh sách: hoặc gắn policy, hoặc có chú thích một dòng
      nói RÕ vì sao cố ý để mở
- [ ] Gate mới: endpoint ghi không policy và không chú thích miễn trừ ⇒ đỏ
      (ratchet, baseline = số còn lại sau đợt rà)
- [ ] Mỗi endpoint được gắn policy có test 1 vai được phép + 1 vai bị chặn

---

## A3 — Observability — **MÔ TẢ CŨ SAI, đã đo lại 17-09-2026**

> ⚠ Mục này từng ghi "không OpenTelemetry, không Serilog, không metrics endpoint"
> rồi kết luận hệ chưa có gì. Đúng về GÓI, sai về NĂNG LỰC: `Observability/` đã có
> `MesLogScope` (`trace_id`·`actor`), `MesRequestContext` (`wo_no`·`work_center`),
> `MesTelemetry` (ActivitySource + 3 counter), middleware đã nối, `AddJsonConsole`
> `IncludeScopes=true`. Chạy thật ra `api_request … -> 401 in 1.4ms` kèm scope.
>
> **Khoảng trống thật, đã đóng 17-09:** log nằm ở `/tmp` — macOS dọn, nên sự cố
> sau lần khởi động lại là không còn gì để điều tra (L65 tái xuất). Nay ở
> `data/Logs/`, xoay vòng theo ngày, gate 30 canh. Xem **L103**.
>
> **Còn lại của A3:** counter đã có nhưng **không có đường đọc** (endpoint /
> exporter) — đó mới là phần cần Henry, đang nằm trong tờ trình STOP-gate.
> Trường `ca` cố ý CHƯA thêm: `MesRequestContext` đã ghi rõ nó chờ `ShiftCalendar`
> data-driven, không bê logic hardcode 06/14/22 từ `TopBar.razor` sang.

### Mô tả gốc (giữ lại để đối chiếu)

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

## PR #127 — đóng, viết lại phần còn thiếu trên nền v5 (2026-08-19)

PR "QC Library Admin" mở 29/06 bị bỏ lại phía sau **122 commit**, trong đó có
#143 re-model toàn bộ QC Library sang v5. Merge khô cho **8 file xung đột**, và
4 file đầu chính là 4 file đã bị viết lại — xung đột về **ý**, không phải về chữ.

Đối chiếu cho thấy hầu hết tính năng của #127 **đã có trên main** (import ·
add · sửa inline · xoá · copy · lines · reason-codes), main còn có thêm `export`.
Chỉ **2 mục thiếu thật**, nay đã viết lại trên nền v5:

| Mục | Trạng thái |
|---|---|
| `GET /template` — file mẫu nhập liệu | ✅ sinh từ **cùng hằng header** với `export` ⇒ mẫu không thể lệch cột; kèm 1 dòng ví dụ vì quy ước tick `●`/`·` không tự hiển nhiên |
| Bật/tắt hạng mục (`Active`) | ✅ `PATCH /{itemId}/active` + mục menu **Ngưng dùng / Dùng lại**. Master data thì "ngưng dùng" mới đúng: WO cũ và snapshot QC đã đóng băng còn tham chiếu tới hạng mục đó |

**Nợ vị trí:** `CheckLibraryAdminService` đặt ở `CCL.MES.Api/Services/` vì
`CCL.MES.Application` là baseline read-only. Sau cutover nên chuyển xuống.
Cùng loại nợ với `OqcSignaturePolicy` (L47).

Nhánh `feat/qc-library-admin` giữ nguyên, không xoá.

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

**Nợ SCRAP-FACTOR-căn-trái — ĐÃ RCA TĨNH 2026-08-23: KHÔNG có defect ở source.**
Truy code `NpiStructures.razor`: `ColClass("scrap_factor")` = `grid-col-num`
(dòng 193, **CÙNG nhánh switch** với `qty_assembly`); `<td>` cả hai cột nhận class
qua **cùng** `ColClass(col.Id)` (dòng 53); CSS `.grid-col-num { text-align:right }`
định nghĩa **giống hệt** ở ix.css:994 + app.css:2039 (không rule scrap-specific đè).
⇒ Hai cột **byte-identical** về class + CSS → **không thể có bug code** khiến chỉ
SCRAP FACTOR lệch. Symptom cũ (nếu còn) chỉ có thể là **runtime/cache** (build cũ
trước fix `<td>`-grid-col-num, hoặc WebView cache) — KHÔNG phải mã. Đã rebuild app
2026-08-23; Henry verify 1 lần trên bản mới: nếu đã căn phải ⇒ đóng hẳn; nếu còn
lệch ⇒ mở Web Inspector chụp computed-style của đúng `<td>` đó (chỉ khi đó mới có
defect thật để truy). **Không vá mò vì hiện KHÔNG có gì để vá.**

## D3/D4 — Component contract + tách `app.css`

**Work-class** W5 · **Agent** `cmes-shopfloor-ux` · **Skill** `cmes-design-tokens`

**Nghiệm thu**
- [x] ~~Bộ pill trạng thái một cách vẽ duy nhất~~ — CSS xong (`.ix-pill*`)
- [x] ~~`StatusPill` + `PhaseVisual`~~ — nguồn sự thật duy nhất, 5 bậc mang ý nghĩa vận hành; gate hard-fail nếu thêm phase mà quên bảng màu (L46)
- [ ] **Di nốt 6 dashboard còn dùng class cứng** `rs-head-phase rs-phase-*` (`Ipqc`/`Fqc`/`Oqc`/`Setting`/`QaApproval`/`ShippedSummary`) sang `StatusPill`. Đã di 3 chỗ có logic trùng lặp + chỗ nhét hex thô; 6 chỗ còn lại là class tĩnh nên để lại cho một PR riêng, đổi đồng loạt sẽ đổi diện mạo 6 màn cùng lúc
- [x] ~~**Nhãn tiếng Việt cho 14 phase WO**~~ — **XONG 2026-09-16**, và chẩn đoán trong mục này SAI: từ ngữ KHÔNG thiếu. 15 key `legs.phase.*` đủ VI/EN đã chốt từ đợt A2 (Henry duyệt) và `StatusPill` đọc đúng; thiếu là ĐƯỜNG DÂY — 8 surface in thẳng `@_view.MesPhase` không qua `StatusPill`, cộng một bảng viết tắt tiếng Anh khai cứng (`PRE-PRESS`·`QA`·`READY`) trong `WorkOrders.razor`. Fix: `PhaseText()` ở `LocalizedComponentBase` + thanh 7 bước giữ key (`wo.step.*`). Chặn tái phát: gate 28 `gate-phase-label.sh` (ratchet 0, có self-test) + `PhaseLabelCoverageTests` duyệt `Enum.GetValues<MesPhase>()`. Bài học **L102**
- [ ] `DataGrid` · `StepTimeline` · `SignaturePad` · `EvidenceCard` thành component chung
- [ ] `app.css` xếp `@layer reset → tokens → primitives → patterns`, còn ≤ **2.000** dòng
- [ ] `gate-design-tokens` BASELINE **527 → ≤200**
- [ ] Mọi surface Operator có screenshot 2 density trong PR

---

## ~~FK-SWEEP — sáu bảng nữa còn thiếu khoá ngoại về `WorkOrders`~~ ✅ ĐÃ ĐÓNG 2026-09-16

**Bối cảnh.** Đợt `AddWorkOrderChildForeignKeys` (`c4b54e3`) đóng 5 bảng, nhưng
phát hiện ra đó không phải toàn bộ. Quét TOÀN DB — mọi bảng có cột `WoId` /
`WorkOrderId` — thay vì quét theo danh sách trong `purge-applied.sql` (quét theo
script là quét theo trí nhớ của người viết script).

**Đo được: 19 bảng trỏ về WO, 6 còn thiếu FK, 0 dòng mồ côi.**

| Bảng | Cột | Dòng | Ghi chú |
|---|---|---|---|
| `WoMaterials` | `WorkOrderId` | **8** | bảng DUY NHẤT còn dữ liệu sống; có FK sang bảng khác nhưng KHÔNG sang `WorkOrders` |
| `WoQcChecks` | `WorkOrderId` | 0 | làm đứt chuỗi của 2 bảng cháu |
| `WoRunSessions` | `WoId` | 0 | |
| `WoQtyEntries` | `WoId` | 0 | |
| `WoPauseEvents` | `WoId` | 0 | |
| `SemiAllocations` | `WorkOrderId` | 0 | |

**Ba chuỗi xoá đang ĐỨT** (bảng cháu không có cột WO, treo vào bảng cha):

| Chuỗi | Tình trạng |
|---|---|
| `WoIpqcCheckItems` → `WoIpqcChecks` → WO | **liền mạch** — đợt `c4b54e3` vừa nối xong đoạn trên, đây là lợi ích phụ chưa ai tính |
| `WoQcCheckItems` → `WoQcChecks` → WO | có FK lên cha (CASCADE) nhưng cha **không** có FK về WO ⇒ **đứt ở ngọn** |
| `WoQcPhotos` → `WoQcCheckItems` → `WoQcChecks` → WO | **ĐÍNH CHÍNH:** ảnh treo vào `WoQcCheckItemId`, KHÔNG phải `WoQcCheckId` — chuỗi thật dài **bốn tầng**, không phải ba như dòng này ghi lúc đầu. `WoQcPhotos` không có FK nào cả ⇒ đứt hai đoạn |
| `SemiAllocations` → WO | không FK ⇒ đứt |

**Điểm sáng:** không bảng nào trong 6 bảng đó có trigger ⇒ **bẫy L38 không dính**
(rebuild sẽ không làm mất trigger nào). 8 trigger của DB nằm ở `MaterialLots` ·
`SemiLots` · `WoLegs` · `WorkOrders`, đều ngoài phạm vi.

**Work-class** W1 · **Agent** `mes-process-architect` · **Skill** `cmes-migration-abc`

### Hai câu phải hỏi trước khi làm, KHÔNG tự quyết

1. **`WoQcPhotos` là bằng chứng hay dữ liệu thao tác?** Đó là ẢNH QC đính vào
   phiếu kiểm. Nếu là bằng chứng thì nó cùng loại với `WoTraceSnapshots` và phải
   `RESTRICT`, không phải cascade. Hiện nó không có FK nào nên xoá WO là để lại
   ảnh mồ côi im lặng — tệ cả hai đường.
2. **`purge-applied.sql` đang xoá `SemiLots` — nhiều khả năng đó là SAI.**
   `SemiLot` là **kho bán thành phẩm**, và chính comment trong entity ghi "1 lô
   cho nhiều WO". Xoá một WO mà xoá luôn lô bán thành phẩm là xoá tồn kho của
   những WO khác. `SemiLots` không có cột WO nên KHÔNG nên có FK về WO — cần rà
   lại chính cái script purge, không phải thêm FK.

**Đã đóng** bằng migration `UnifyQcEvidenceForeignKeys` (`20260916072554`, áp live).
Thiệp chốt 16-09: **ảnh QC là bằng chứng ⇒ Restrict**, và thống nhất luật cho cả
`WoIpqcChecks` (đổi Cascade → Restrict, sửa lại thứ vừa áp buổi sáng).

**Luật thống nhất:** hồ sơ QC **có chữ ký** → `RESTRICT` · dữ liệu **thao tác** → `CASCADE`.
Ba bảng Restrict: `WoTraceSnapshots` · `WoIpqcChecks` · `WoQcChecks`, cộng
`WoQcPhotos` → `WoQcCheckItems`. Hệ quả cố ý: WO đã qua QC thì **không xoá được**,
phải `CANCELLED` — đúng quy trình đã chốt.

**Nghiệm thu**
- [x] 19/19 bảng có cột `WoId`/`WorkOrderId` đều có FK — **0 bảng còn thiếu**
- [x] Chuỗi đứt đã nối; kiểm bằng **xoá thật** trên DB cô lập, 4 ca đều đúng
- [x] ~~Gate mới: bảng có cột `WoId`/`WorkOrderId` mà không có FK ⇒ đỏ~~ — **XONG**: gate 29
      `gate-wo-child-fk.sh`, đọc `MesDbContextModelSnapshot.cs` (chạy được ở CI, không cần DB).
      Gác CẢ HAI luật: thiếu FK ⇒ đỏ, và bằng chứng QC mà không `Restrict` ⇒ đỏ.
      Chứng minh bằng ca THẬT: chĩa vào snapshot trước hai migration hôm nay ⇒ bắt đúng **11 bảng**.
- [ ] Câu hỏi `purge-applied.sql` xoá `SemiLots` — **CHƯA có kết luận**, cần Henry

## Nợ kỹ thuật đã phát hiện, chưa xếp lịch

| Việc | Ghi chú |
|---|---|
| ~~Soak test `Concurrent_*_N_equals_10` flake~~ — **ĐÃ ĐÓNG, và chẩn đoán ban đầu SAI** | Mục này từng ghi "assert quá chặt cho engine có retry nội bộ, nên tách `Category=Soak`". **Sai.** Đo thật (20 lượt × 10 request) cho thấy wire **tất định tuyệt đối** — luôn 1 winner + 9 × 409 — còn thứ dao động là **số dòng audit**, và chờ 3s không hội tụ ⇒ **mất dữ liệu thật**, không phải test giòn. Nguyên nhân: 7/11 nhánh `catch (DbUpdateConcurrencyException)` trả 409 mà không emit `WO_STATE_CONFLICT`. Đã vá + gate chặn (L45). Full suite Api **504/504**. **Không sửa một assert nào** — test đúng từ đầu. Bài học cho backlog: đừng gắn nhãn "flake" trước khi đo phân bố kết quả. |
| `AngleSharp 1.2.0` NU1902 moderate — **đã điều tra 2026-08-23, HOÃN có lý do** | Transitive qua `bunit 1.39.5` (chỉ dùng cho test — parse DOM component của chính ta, KHÔNG ship/không nhận input ngoài ⇒ rủi ro thực tế ~0). Pin trực tiếp `AngleSharp 1.7.1` (bản vá) **vỡ bunit**: `MissingMethodException IHtmlCollection.get_Item(Int32)` (API bị xoá sau 1.2.x; vá không backport về 1.2.x). Fix thật = **nâng bunit lên 2.x (breaking, đổi test API)** — disproportionate cho một advisory test-only. Để lại tới khi có đợt nâng bunit riêng; đừng pin AngleSharp lẻ. |
| ~~3 gate cũ chưa có `--self-test`~~ — **ĐÃ ĐÓNG (2026-08-23)** | `gate-row-actions` · `gate-floating-showcard` · `gate-spec-print` nay đều có khối `--self-test`: inject một vi phạm tổng hợp (Actions-column / inline `role="dialog"` / `table-layout:fixed` trên `.spec-print-table-full`) rồi khẳng định **chính detection code** bắt được. Regex/khối awk tách ra biến `RX` / hàm `check_fixed_layout` dùng chung để self-test không drift khỏi detector thật. `gate-all --self-test` nay **SKIP=0** (trước 3). Negative proof: bẻ detector ⇒ self-test in FAILED + exit 1. |
| ~~`.claude/skills/liteparse` · `markitdown` chưa quyết commit hay `.gitignore`~~ — **ĐÃ QUYẾT 2026-09-16: commit** | Chuyển từ `tasks.md` của P12 sang đây 2026-09-03 (`/speckit-analyze` U2): việc dọn kho công cụ, không liên quan tiêu chuẩn kiểm NVL. Task list của một feature phải đóng lại được khi feature xong. **Chốt:** cả hai là skill CHỈ có tài liệu + script helper — phần mềm thật (LiteParse Apache-2.0 · MarkItDown MIT) không bundle, chỉ trỏ cài từ PyPI/npm. Nằm cùng chỗ 14 skill `cmes-*` đã tracked nên máy khác clone về là có. |
| ~~`gate-qc-process-kind.sh` chưa có `--self-test`~~ — **ĐÃ ĐÓNG 2026-09-16** | Đo sáng 16-09: `gate-all --self-test` ra `SKIP=1` vì gate này. Mục "3 gate cũ chưa có self-test" đã đóng 08-23 với lời hứa `SKIP=0`; gate qc-process-kind thêm vào 09-11 không kèm self-test nên lời hứa đó âm thầm hỏng. **Nay `SKIP=0` trở lại.** Phép so tách thành hàm `compare_all()` để self-test chạy ĐÚNG bộ rút và ĐÚNG phép so của lượt thật (chỉ trỏ `$SRV`/`$UI` sang bản sao), kiểm hai chiều: bản nguyên vẹn phải XANH, bản tiêm một line chỉ-có-ở-UI (`FOIL`) phải ĐỎ. Negative proof: bẻ `ui_lines` cho trả về chính danh sách server ⇒ self-test in FAILED + exit 1; khôi phục ⇒ sha khớp `25a5179c` ⇒ xanh lại. |
| `IqcDocumentViewerTests.Zoom_va_xoay_doi_trang_thai_hien_thi` chớp tắt — **đã điều tra 17-09, KHÔNG tái hiện được** | **Phân bố đo được: 2 đỏ / 36 lượt ≈ 5,6%.** Hôm qua 1/6 (cây sạch) + 1/10 (cây có thay đổi); hôm nay **0/20** gồm 12 lượt máy rảnh + 4 đối chứng + 4 dưới tải. **Hai giả thuyết đã BÁC BỎ bằng thí nghiệm, không phải bằng suy đoán:** ① *quá tải làm vượt timeout* — ép 20 tiến trình chiếm CPU trên máy 10 lõi, vẫn 4/4 xanh; ② *trạng thái dùng chung* — component chỉ có một `static` là hàm format thuần, fixture ghi vào thư mục GUID riêng mỗi lần gọi, test chạy song song nhưng không chia sẻ gì. **Điều đã biết về cấu trúc:** đường tới element được chờ có **I/O đĩa thật** (`File.WriteAllBytes` trong test → `File.ReadAllBytesAsync` trong component → `JS.InvokeAsync`), và **mọi `WaitForElement` trong lớp test này đều không đặt timeout** ⇒ dùng mặc định bUnit. Đó là cơ chế *hợp lý* nhưng **chưa chứng minh được**. **CHƯA có câu lỗi thật** — lần đỏ hôm qua không ai bắt được message, và đó là dữ kiện thiếu quan trọng nhất. **Và cẩn thận khi có nó:** 17-09, lúc làm nhãn `ProcessStepCode`, một test khác đỏ với đúng câu `WaitForFailedException: "The assertion did not pass within the timeout period… This can happen on highly utilized or slower hardware"` — nhưng nguyên nhân KHÔNG phải quá tải chút nào: assertion đơn giản là **không bao giờ đúng** vì tôi vừa đổi nhãn. bUnit dùng CÙNG MỘT câu lỗi cho cả 'hết thời gian' và 'điều kiện không bao giờ thành'. Nên nếu lần sau flake này đỏ với câu đó, **đừng đọc nó là timeout** — nó không phân biệt được hai thứ. **CỐ Ý KHÔNG SỬA:** nới timeout bây giờ là nới assert theo phỏng đoán, đúng cái bẫy mục "soak flake" ở dưới đã trả giá. Lần sau ai gặp đỏ, việc đầu tiên là **dán nguyên văn `Error Message`** vào đây. |
| Băng-rôn "đã chuyển bước" in `CurrentStep` legacy | `WorkOrders.razor` hiện `ProcessStepCode` (`OpSetting`·`Fqc`·`Oqc`) — từ vựng KHÁC 15 nhãn `legs.phase.*` nên `PhaseText` trả nguyên token, cố ý không dịch bừa. Cần bảng nhãn riêng cho `ProcessStepCode`, hoặc bỏ hẳn khi cutover A1 xong. Ghi nhận khi làm L102. |
| Probe seed IQC chưa nói số dòng bị LOẠI | `[seed] iqc_library …` in `items/specs/spec_items/updated`. Số bị loại lúc parse (28 tiêu chuẩn thành phẩm · 4 spec trùng template · 2 file không đọc được bảng) chỉ có trong `IqcLibraryCsvTests`, không hiện lúc boot. Thêm trường `Skipped` vào `IqcLibrarySeedResult` sẽ giúp Ops thấy ngay "master mới loại bao nhiêu dòng" mà không phải chạy test. Nhỏ, additive, không đụng schema. Ghi nhận từ `/speckit-analyze` G2. |
| `ShippedSummaryDashboard` chưa ai nhìn màn thật — **đã thử 21-09, BỊ CHẶN bởi hai thứ, không phải bởi code hỏng** | Màn này ra đời trong `df45f87` (breakdown công đoạn từ `WoPhaseSpan`) và tới giờ chỉ có bUnit test; CI không nhìn màn hình. **Chặn ①: không có đường tắt tới SHIPPED.** Ô duy nhất vào là `(OQC_PENDING, SHIPPED) => RequiresSignoff` (`WorkOrderStateMachine.cs:287`) — chuỗi 3 chữ ký OQC; `from is SHIPPED or CANCELLED` thì `Blocked` (`:211`). Endpoint admin `force-phase` KHÔNG cứu được: nó chỉ mở 11 ô `RecoveryOnly` của §3.1, ô vào SHIPPED không nằm trong đó, và không có surface UI nào gọi nó (grep Razor = 0 kết quả) nên muốn gọi phải có JWT. ⇒ muốn WO shipped thật thì phải đi hết vòng QC bằng tài khoản QC + Supervisor thật. **Chặn ②: app nối cứng cổng.** `CCL.MES.Hybrid/appsettings.json` đặt `BaseUrl = http://127.0.0.1:5100` và nằm trong bundle; màn Settings → Kết nối chỉ **hiển thị** địa chỉ (`<code>@Opts.Value.BaseUrl</code>`), không sửa được. ⇒ muốn app đọc một DB khác thì API trỏ DB đó phải CHIẾM cổng 5100, tức phải dừng dịch vụ launchd `com.ccl.mes.api` (có `KeepAlive`, `kill` là nó dựng lại — phải `launchctl bootout`). **Đã dựng được tới đâu:** bản sao DB cô lập + WO SHIPPED tổng hợp (6 span gồm `VisitNo=2` cho rework, OQC đủ 3 chữ ký) chạy ngon trên API tạm cổng 5101 — `summary-report` trả **401** (endpoint sống, chỉ thiếu token). Không vào được app vì đúng chặn ②. Đã dọn sạch: API tạm tắt, bản sao xoá, `data/ccl_mes.db` không mất byte nào. **Hai bẫy đo được trên đường, ghi để lần sau đỡ mất giờ:** `ASPNETCORE_URLS` **thua** khoá `"Urls"` trong `appsettings.json` ⇒ phải truyền `--urls` trên dòng lệnh, không thì nó vẫn bind 5100 và ném `AddressInUseException`; và `strings` của Xcode **không có cờ `-el`** ⇒ phép thử "DLL có chứa chuỗi UTF-16 này không" bằng `strings -el` trả rỗng vì LỖI CỜ chứ không phải vì không có — so byte `key.encode('utf-16-le')` mới đúng (cùng họ với bẫy L104-③). **Đường rẻ nhất khi làm:** đợi một WO đi hết vòng thật rồi chụp — không phải dựng lại hạ tầng gì. `RunningDashboard` của cùng PR **đã chụp xong 2 density 21-09**. |
