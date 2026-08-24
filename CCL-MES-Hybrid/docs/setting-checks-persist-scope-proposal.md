# SETTING checks — persist scope proposal (ENRICHED)

> Follow-up của PR #211 (OP Setting tách 2 sub-tab In/Cắt + tab-visibility
> theo routing). PR #211 ship UI **attestation-cục-bộ** (per-item OK/NG chỉ
> sống trong RAM component, KHÔNG lưu server) — đúng phạm vi 7c-3, không đụng
> schema. Doc này xin Henry duyệt phần **persist** trước khi chạm
> `Entities/` · `Migrations/` · WO-STATE-CONTRACT · live DB.
>
> Định dạng: mỗi Q có bối cảnh + **Khuyến nghị** + trade-off + contract-gap.
> Không code cho tới khi Henry ký §Sign-off. STOP-gate: đây là W1 (schema) +
> W2 (state contract) + W4 (quality) — 3 vùng đều cần duyệt.

## Scope at a glance

Biến 20 hạng mục kiểm tra setting (10 In + 10 Cắt) từ *tick tạm* thành **bằng
chứng bất biến**: ai xác nhận, lúc nào, OK/NG, lý do NG — để `/setting/done`
có gate server-side (không chỉ client), và audit truy được khi có sự cố chất
lượng ở khâu makeready.

Đã shipped (PR #211, KHÔNG cần duyệt lại):
- `SettingProcessScope` + `HasPrintProcess/HasCutProcess` trên `RunningSurfaceView`.
- 2 catalog In/Cắt hard-code trong `SettingDashboard.razor` (`_printItems`/`_cutItems`).
- Gate "Hoàn tất" theo tab áp dụng — **client-side only** (đây là lỗ hổng doc này vá).

Xin duyệt (Q1..Q10 dưới).

---

## Q1 — Persist OK/NG per-item, hay giữ attestation-cục-bộ?

Hiện `/setting/done` chỉ cần client tick đủ; server KHÔNG biết từng hạng mục.
Một operator (hoặc client bị bug/bypass) có thể advance mà chưa thật sự kiểm.
IPQC/FQC/OQC đều đã persist per-item (`WoIpqcCheckItem`, `WoQcCheckItems`).

**Khuyến nghị**: **PERSIST**. SETTING là gốc chất lượng (sai makeready → hỏng cả
lô); để nó là khâu DUY NHẤT không có bằng chứng là bất đối xứng nguy hiểm.

**Trade-off**: +1 bảng + migration + backfill; nhưng đóng lỗ hổng "advance mà
chưa kiểm" và cho phép RCA khâu setting. Không persist = rẻ nhưng khâu makeready
mãi mù về bằng chứng.

**Contract-gap**: SpecHub CCL-10-F… không nói rõ setting-check phải freeze —
nhưng tinh thần "mọi mutation emit + bằng chứng bất biến" của dự án nghiêng persist.

---

## Q2 — Hình dạng entity: per-item row (shadow table) hay fixed slots?

IPQC dùng cả 2: 4 slot cứng (legacy) + `WoIpqcCheckItem` shadow (Plan C).
Setting có 20 item/2 process, số item có thể đổi theo catalog.

**Khuyến nghị**: **per-item row** `WoSettingCheckItem` — KHÔNG fixed slot.
Khoá `(WorkOrderId, ProcessKind, ItemKey)` unique; `ProcessKind ∈ {Print, Cut}`;
`Status ∈ {Pending, Ok, Ng}`; `NgReasonCode?` + `NgNote?`; `ConfirmedBy?` +
`ConfirmedAt?` + `RowVersion`. Mirror `WoIpqcCheckItem`.

**Trade-off**: per-item mềm dẻo (đổi catalog không cần migration cột) nhưng cần
materialize rows. Fixed slot cứng nhắc, không hợp 2×N item.

**Contract-gap**: none — thuần additive table.

---

## Q3 — Nguồn catalog: promote vào `CheckItemLibrary` (Plan C), hay seed hằng?

PR #211 hard-code 2 catalog trong Razor. Plan C đã có `CheckItemLibrary` +
`QcLineResolver` + `IpqcLibraryMaterializer` cho IPQC/FQC/OQC.

**Khuyến nghị**: **promote vào `CheckItemLibrary`** với 2 line mới
`PRINT_SETTING` + `CUT_SETTING` (hoặc `Stage=Setting` cột phân biệt) →
`SettingLibraryMaterializer` frozen snapshot như IPQC. Admin sửa catalog không
cần deploy; đồng bộ 1 nguồn master-data.

**Trade-off**: nhiều việc hơn (resolver + materializer + importer CSV) nhưng
nhất quán Plan C + admin-editable. Seed hằng nhanh nhưng tạo nguồn master-data
thứ 2 lệch khỏi Plan C — nợ kỹ thuật.
**Phương án trung gian** (đề xuất nếu muốn nhỏ): seed 20 item vào
`CheckItemLibrary` bằng DbSeeder (idempotent, non-deleting DR-1), tái dùng
materializer pattern, KHÔNG viết importer CSV riêng ở bước này.

**Contract-gap**: cần Ops chốt bộ item chuẩn (giống `IPQC_Library_CMES_v3.csv`).

---

## Q4 — Advance-guard server-side cho `/setting/done`?

Hiện gate all-OK là **client-only**. Nếu persist (Q1), server nên tự kiểm.

**Khuyến nghị**: **YES** — `/setting/done` tính rollup từ `WoSettingCheckItem`
của các ProcessKind áp dụng (theo `HasPrint/HasCut`); thiếu OK → **422
`setting.incomplete`** + audit `WO_SETTING_DONE_DENIED`. Defense-in-depth mirror
IPQC judgment guard (L15 dual-side check).

**Trade-off**: +1 query rollup mỗi done; nhưng biến gate thành thật (không bypass
được bằng client). Bỏ qua = client vẫn là "nguồn sự thật" mong manh.

**Contract-gap**: **đây là điểm chạm WO-STATE-CONTRACT** — xem Q7.

---

## Q5 — NG ở setting: bắt buộc reason code + note như IPQC?

PR #211 NG chỉ đổi màu đỏ + chặn advance, không lý do. Persist thì NG cần truy vết.

**Khuyến nghị**: **YES** — NG mở sub-form: picker mã lỗi + note 1–500 ký tự,
validate mã thuộc catalog (chống free-text, L17). Nguồn mã: `ReasonCodeKind`.
Câu hỏi phụ: dùng `Scrap` sẵn có hay thêm `ReasonCodeKind.SettingNg`?
→ đề xuất **tái dùng `Scrap`** + thêm vài mã `SET-*` (SET-PLATE-MISMOUNT,
SET-INK-VISC, SET-DIE-DEPTH…), tránh sinh kind mới.

**Trade-off**: NG có lý do = RCA được; nhưng thêm thao tác cho operator. Không
reason = nhanh nhưng NG vô nghĩa khi truy vết.

**Contract-gap**: cần Ops cung cấp danh mục mã lỗi setting.

---

## Q6 — Audit codes: mirror 7d/7e naming?

**Khuyến nghị**: thêm 3 code alphabetical: `WO_SETTING_ITEM_SET`,
`WO_SETTING_DONE` (nếu chưa có), `WO_SETTING_DONE_DENIED`. Detail JSON tuyệt
đối không chứa password/token (§6). Emit qua `IAuditWriter`.

**Trade-off**: none — thuần additive const.

**Contract-gap**: none.

---

## Q7 — State-contract: `/setting/done` từ "unconditional" thành "requires-condition"?

Hiện `SETTING → IPQC_WAIT` là transition không điều kiện. Q4 thêm tiền điều kiện
(mọi item áp dụng = OK). **Đây là STOP-gate #2** (đụng WO-STATE-CONTRACT).

**Khuyến nghị**: sửa §3.1 ô `SETTING → IPQC_WAIT` từ `allowed` → `requires-condition`
(all applicable setting items Ok), giống ô Q6 PAUSED→FQC của 7c. Additive: WO cũ
đã ở IPQC_WAIT không bị ảnh hưởng (one-way projection); WO đang SETTING cần
backfill rows (Q8).

**Trade-off**: đúng luật additive (không dịch giá trị cũ); nhưng phải cập nhật
`P10.7-WO-STATE-CONTRACT.md` + matrix test. Bỏ qua = gate client-only (Q4 rỗng nghĩa).

**Contract-gap**: **Henry phải duyệt amendment §3.1 trước khi code.**

---

## Q8 — Migration + backfill

**Khuyến nghị**: migration `AddWoSettingCheckItem` — 1 bảng + unique index
`(WorkOrderId, ProcessKind, ItemKey)` + **idempotent backfill INSERT** cho WO
đang ở SETTING (materialize rows Pending theo catalog + `HasPrint/HasCut`).
Type-affinity strip (§4.5). Đi **Phase A→B→C** (§4.4): backup + isolated /tmp DB
+ verify SHA/rowcount. TUYỆT ĐỐI không `ef migrations remove`/`add` trỏ live (§4.1–4.2).

**Trade-off**: backfill giữ WO đang chạy không kẹt; bỏ backfill → WO SETTING cũ
thiếu rows → advance-guard chặn oan.

**Contract-gap**: **STOP-gate #3** (chạy migration lên live DB) — chỉ sau khi Henry ký.

---

## Q9 — RBAC: ai được set setting-check?

**Khuyến nghị**: `SettingSubmit` = **Operator | Admin | Supervisor | Engineer**
(operator đứng máy là người kiểm setting). Server 403 nếu ngoài whitelist;
RBAC-by-omission ở client. Mirror §5.5.0 pattern.

**Trade-off**: none đáng kể.

**Contract-gap**: xác nhận Operator ĐƯỢC quyền (khác IPQC = QC-only).

---

## Q10 — Gắn với tab-visibility đã ship

Persist phải tôn trọng `HasPrintProcess/HasCutProcess` (PR #211): chỉ
materialize + chỉ require ProcessKind áp dụng. WO print-only → chỉ 10 row Print,
rollup bỏ qua Cut.

**Khuyến nghị**: materializer nhận `(hasPrint, hasCut)` từ `SettingProcessScope`
(tái dùng, không tính lại). Rollup guard (Q4) cũng theo scope này.

**Trade-off**: none — tái dùng code đã có.

**Contract-gap**: none.

---

## Proposed 4-PR stack (7g-setting-persist)

| PR | Nội dung | Work-class | Skill |
|---|---|---|---|
| 7g-1 | Domain: `WoSettingCheckItem` + `ProcessKind` + migration + backfill + `SettingLibraryMaterializer` (+ seed 20 item Q3 trung gian) | W1 | `cmes-migration-abc` |
| 7g-2 | Wire: `SettingChecksController` set-item (atomic 7c-2 pattern) + `/setting/done` rollup guard (Q4/Q7) + audit (Q6) | W3+W4 | `cmes-thin-controller` · `cmes-audit-emit` |
| 7g-3 | UI: `SettingDashboard` đọc/ghi qua API (thay `_status` RAM) + NG sub-form reason picker (Q5) + optimistic-revert 409 | W5 | `cmes-design-tokens` |
| 7g-4 | Test-belt: `verify-setting-persist.sh` + checkpoint + purge extend + LESSONS card | W9 | `cmes-verify-evidence` |

## Câu hỏi NGOÀI Q1..Q10 (Henry chốt in/out)

- Setting timer (đã có) có freeze vào bằng chứng khi done không? (đề xuất: có, đã có `SettingDurationSec`).
- Cần chữ ký thứ 2 (supervisor co-sign) cho NG-special-accept ở setting không? (đề xuất: KHÔNG ở 7g — defer).
- Camera capture bằng chứng setting (giống Q6 của 7f)? (đề xuất: defer về 7f blob pattern).

## Sign-off checklist (Henry)

- [ ] Q1 persist: **có / không**
- [ ] Q3 catalog: library-promote / seed-trung-gian / hard-code-giữ nguyên
- [ ] Q4 + Q7 advance-guard server + amendment §3.1 `SETTING→IPQC_WAIT` requires-condition: **duyệt / không**
- [ ] Q5 NG reason: tái dùng `Scrap` + mã `SET-*` / kind mới / không reason
- [ ] Q9 RBAC gồm Operator: **đúng / sửa**
- [ ] Danh mục 20 item + mã lỗi setting: Ops cung cấp CSV
- [ ] Cho phép chạy migration lên live DB sau khi §A→C verify: **ký**

> Cho tới khi các ô trên được ký, phần persist KHÔNG được code (STOP-gate
> W1/W2 + live DB). PR #211 (UI attestation-cục-bộ + tab-visibility) độc lập,
> merge được trước, không phụ thuộc doc này.
