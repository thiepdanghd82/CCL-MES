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

Đã shipped UI-only (attestation-cục-bộ, KHÔNG cần duyệt lại):
- PR #211: `SettingProcessScope` + `HasPrintProcess/HasCutProcess` (tab-visibility);
  2 catalog In/Cắt hard-code trong `SettingDashboard.razor`.
- PR #213 (F1/F2/F3): checkbox **Áp dụng** (N/A loại khỏi gate) · cột **Kết quả**
  (Đạt/NG/N-A) · dropdown **Defect per-item** (~75 mã VI/EN trong component).
- Gate "Hoàn tất" theo tab áp dụng + item áp dụng — **client-side only** (lỗ hổng doc này vá).

Xin duyệt: Q1..Q10 (persist nền) **+ QA..QF (F1–F4 mới)** dưới.

---

## F1–F4 — bốn tính năng bảng xác nhận (yêu cầu Henry 2026, superset của 7g)

| | Tính năng | Đã ship (UI-only #213) | Cần persist (doc này) |
|---|---|---|---|
| F1 | Checkbox "Áp dụng" (N/A) | ✅ per-WO trong RAM | Lưu `Applicable` per-item (QA); tùy chọn nhớ per-product |
| F2 | Cột "Kết quả" (Đạt/NG/N-A) | ✅ dẫn xuất | Không cần cột riêng — dẫn xuất từ persisted status+applicable (QB) |
| F3 | Defect dropdown per-item | ✅ bộ cứng ~75 mã | Lưu `DefectCode` khi NG + catalog per-item chuyển vào master (QC) |
| F4 | **"Add more"** hạng mục | ❌ CHƯA (cần master-data) | Thêm item **theo mã sản phẩm** → LOT kế tự yêu cầu (QD/QE) |

F4 là dimension MỚI hoàn toàn (per-product custom master-data) — không làm được
UI-only vì "nhớ cho LOT sau" đòi persist. Đây là lý do chính doc này phải được ký.

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

**⚠ Phát hiện khi khảo sát**: `CheckItemLibrary` **hiện KHÔNG có cột stage
`Setting`** — chỉ `Ipqc/Fqc/Oqc` (cột P/Q/R). Hướng library-promote vì vậy cần
thêm **1 cột bool `Setting`** (hoặc cột `Stage`) → đây là migration, nằm trong
STOP-gate. Không "miễn phí" như IPQC/FQC/OQC vốn đã có cột.

**Trade-off**: nhiều việc hơn (cột stage + resolver + materializer + importer)
nhưng nhất quán Plan C + admin-editable. Seed hằng nhanh nhưng tạo nguồn
master-data thứ 2 lệch khỏi Plan C — nợ kỹ thuật.
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

## QA — Applicability (F1): lưu per-WO, hay nhớ per-product?

F1 đã ship per-WO trong RAM. Persist thì lưu ở đâu?

**Khuyến nghị**: **cả hai tầng**. Cột `Applicable` (default true) trên
`WoSettingCheckItem` (per-WO — operator bỏ áp dụng cho lệnh này, có audit).
RIÊNG khi **Engineer** đánh dấu "mã này không cần hạng mục X vĩnh viễn" → ghi vào
master per-product (F4 cùng cơ chế) để LOT sau tự bỏ. Operator KHÔNG sửa được default product.

**Trade-off**: 2 tầng linh hoạt (lệnh-này vs mã-này) nhưng cần phân quyền rõ; 1 tầng
per-WO đơn giản nhưng LOT sau phải bỏ-áp-dụng lại thủ công.

**Contract-gap**: rollup guard (Q4) chỉ tính item `Applicable=true`.

## QB — Result (F2): cột riêng hay dẫn xuất?

**Khuyến nghị**: **dẫn xuất** từ (Status + Applicable) — KHÔNG cột nhập riêng,
tránh 2 nguồn sự thật. Đã làm đúng vậy ở #213; persist chỉ cần lưu Status+Applicable.

**Trade-off / Contract-gap**: none.

## QC — Defect (F3): catalog per-item ở đâu?

#213 nhúng ~75 mã cứng trong component. Persist cần catalog per-item có thể sửa.

**Khuyến nghị**: mỗi hạng mục (`CheckItemLibrary` row, stage Setting) mang **danh
sách defect** — bảng con `CheckItemDefectOption` (ItemId → DefectCode + Vi/En + Sort)
HOẶC cột `DefectOptionsJson`. `WoSettingCheckItem.DefectCode` lưu mã đã chọn khi NG.
Buộc chọn defect khi NG (chống free-text, L17).

**Trade-off**: bảng con chuẩn-hoá (query/thống kê defect được) vs JSON (đơn giản, khó
thống kê). Đề xuất **bảng con** vì defect Pareto là báo cáo chất lượng quan trọng.

**QC-mở-rộng — "＋ Thêm mới" defect ngay trong dropdown** (yêu cầu Henry 2026): mỗi
dropdown defect (mọi tab, mọi dòng) có option cuối **`＋ Thêm mới…`** → operator/
engineer bổ sung mã defect chưa có. Đây là **add-new vào `CheckItemDefectOption`**,
cùng luật persist với F4 (QD): Engineer+ thêm per-product (nhớ LOT sau) · Operator
ad-hoc per-WO. Hình dạng + testid theo skill **`cmes-add-new-inline`**. Audit
`WO_SETTING_DEFECT_ADDED`. Vì vậy chọn **bảng con** (không JSON) là bắt buộc —
user thêm defect thì phải insert row, không patch JSON đua ghi.

**Contract-gap**: Ops xác nhận bộ defect per-item seed ban đầu (`setting-ng-reason-codes.md`
§per-item); phần user thêm sau đi qua add-new (QD RBAC).

## QD — "Add more" (F4): ai được thêm + đích lưu?

Yêu cầu: thêm hạng mục tự nhập, **nhớ theo mã sản phẩm** → LOT kế của cùng mã tự yêu cầu.

**Khuyến nghị**: **Engineer / Supervisor / Admin** thêm item **per-product**
(materialize vào mọi WO tương lai của mã đó). Operator: chỉ thêm **ad-hoc per-WO**
(không nhớ sang lô khác) HOẶC chặn hẳn — Henry chốt. Validate: tên + tiêu chuẩn +
process bắt buộc; defect options tùy chọn. Server 403 nếu ngoài whitelist.

**Trade-off**: cho Operator thêm per-WO = linh hoạt hiện trường nhưng loãng master;
chỉ Engineer+ = kỷ luật master-data nhưng chậm. Đề xuất Engineer+ per-product,
Operator per-WO-only.

**Contract-gap**: audit `WO_SETTING_ITEM_ADDED` (ai/mã/hạng mục).

## QE — Nguồn master cho item thêm (F4)

**Khuyến nghị**: tái dùng `CheckItemLibrary` với `ProductCode=<mã>` + stage `Setting`
(cột mới, Q3). Materializer merge **base (ProductCode null) + custom (ProductCode=mã)**
→ bộ item của WO. 1 nguồn master-data, đúng Plan C. Tránh sinh bảng master thứ 2.

**Trade-off / Contract-gap**: phụ thuộc Q3 (cột `Setting` trên `CheckItemLibrary`).

## QF — Advance-guard với applicability

**Khuyến nghị**: gate = mọi item **Applicable=true** trong process áp dụng phải OK
(NG có defect vẫn chặn; N-A bỏ qua; cần ≥1 applicable). Server 422 `setting.incomplete`.
→ vẫn là amendment §3.1 (Q7). Client #213 đã theo luật này; server cần mirror.

**Contract-gap**: gộp vào STOP-gate Q7.

---

## Proposed 4-PR stack (7g-setting-persist)

| PR | Nội dung | Work-class | Skill |
|---|---|---|---|
| 7g-1 | Domain: `WoSettingCheckItem`(+`Applicable`,`DefectCode`) + `CheckItemLibrary`(+`Setting`,+`CheckItemDefectOption`) + `ProcessKind` + migration + backfill + materializer(base+per-product merge, QE) + seed 20 item + defect per-item | W1 | `cmes-migration-abc` |
| 7g-2 | Wire: `SettingChecksController` set-item(status+defect+applicable) + **add-item (F4)** + **add-defect vào `CheckItemDefectOption` (QC-add-new)** per-product (RBAC QD) + `/setting/done` rollup guard (QF/Q7) + audit (Q6 + `WO_SETTING_ITEM_ADDED` + `WO_SETTING_DEFECT_ADDED`) | W3+W4 | `cmes-thin-controller` · `cmes-audit-emit` |
| 7g-3 | UI: `SettingDashboard` đọc/ghi qua API — F1 checkbox · F2 result · F3 defect từ master + **"＋ Thêm mới" trong dropdown** · **F4 "＋ Thêm hạng mục" cuối grid** (cả hai theo skill `cmes-add-new-inline`) · optimistic-revert 409 | W5 | `cmes-design-tokens` · `cmes-add-new-inline` |
| 7g-4 | Test-belt: `verify-setting-persist.sh` + checkpoint(**F4: thêm item → WO mới cùng mã có item**) + purge extend + LESSONS card | W9 | `cmes-verify-evidence` |

## Câu hỏi NGOÀI Q1..Q10 (Henry chốt in/out)

- Setting timer (đã có) có freeze vào bằng chứng khi done không? (đề xuất: có, đã có `SettingDurationSec`).
- Cần chữ ký thứ 2 (supervisor co-sign) cho NG-special-accept ở setting không? (đề xuất: KHÔNG ở 7g — defer).
- Camera capture bằng chứng setting (giống Q6 của 7f)? (đề xuất: defer về 7f blob pattern).

## Sign-off checklist (Henry)

- [ ] Q1 persist: **có / không**
- [ ] Q3 catalog: library-promote (+cột `Setting`) / seed-trung-gian / hard-code-giữ nguyên
- [ ] Q4 + QF + Q7 advance-guard server + amendment §3.1 `SETTING→IPQC_WAIT` requires-condition: **duyệt / không**
- [ ] Q5 NG reason: tái dùng `Scrap` + mã `SET-*` / kind mới / không reason
- [ ] Q9 RBAC set-item gồm Operator: **đúng / sửa**
- [ ] **QA** applicability lưu: per-WO / per-WO + per-product(Engineer)
- [ ] **QC** defect catalog: bảng con `CheckItemDefectOption` / JSON
- [ ] **QC-add-new** "＋ Thêm mới" defect trong dropdown: **duyệt / không** (ai thêm = theo QD RBAC)
- [ ] **QD** "Add more" hạng mục (F4, nút ＋ cuối grid): ai thêm (Engineer+ per-product · Operator per-WO / chặn Operator)
- [ ] Danh mục 20 item + **defect per-item**: Ops xác nhận (`setting-ng-reason-codes.md` §per-item)
- [ ] Cho phép chạy migration lên live DB sau khi §A→C verify: **ký**

> Cho tới khi các ô trên được ký, phần persist + F4 + add-new-defect KHÔNG được code
> (STOP-gate W1/W2 + live DB). Hình dạng add-new đã chốt ở skill
> **`cmes-add-new-inline`**. PR #211 + #213 (UI attestation-cục-bộ: tab-visibility +
> F1/F2/F3) độc lập, đã merge, không phụ thuộc doc này.
