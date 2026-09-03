# Feature Specification: Thư viện tiêu chuẩn kiểm tra NVL cho IQC (P12)

**Feature Branch**: `feat/p12-iqc-check-standard-library` (thư mục spec: `012-iqc-check-standard-library`)

**Created**: 2026-09-03 (hồi cứu từ scope proposal 2026-08-28 và 8 commit trên nhánh)

**Status**: Implemented — chờ pha VERIFY (`dotnet test`) trước khi merge

**Input**: User description: "Thư viện tiêu chuẩn kiểm tra NVL cho IQC — P12"

**Nguồn**: `CCL-MES-Hybrid/docs/p12-iqc-library-scope-proposal.md` (đã Henry duyệt, quyết định D1–D4 ngày 2026-08-28) · `docs/RA-SOAT-2026-09-01.md` · Hiến pháp v1.0.0.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mở phiếu IQC là có sẵn bộ hạng mục kiểm ĐÚNG cho nguyên liệu đó (Priority: P1)

Người kiểm IQC mở phiếu cho một lô nguyên liệu về. Hệ thống tra `MotherCode`
của nguyên liệu → số SPEC → danh sách hạng mục kiểm, rồi **đóng băng** nhãn ·
tiêu chuẩn · phương pháp · nhóm (cả VI và EN) vào phiếu. Mục 2 (Ngoại quan)
hiện nhóm NL/NQ, mục 3 (Chức năng) hiện các nhóm còn lại. Hạng mục mới sinh ở
trạng thái **CHƯA KIỂM**, không phải NG.

**Why this priority**: Đây là lý do tồn tại của P12. Trước đó hạng mục IQC là
văn bản tự do, mục 2–5 của phiếu là placeholder — đúng lớp bệnh L61/L63.

**Independent Test**: Tạo phiếu cho một nguyên liệu có spec, đọc
`GET /iqc/tickets/{id}/items`, thấy đúng hạng mục của spec đó với tiêu chuẩn
riêng (kiểm chứng bằng `BD-01` trên ≥3 nguyên liệu có 3 ngưỡng khác nhau).

**Acceptance Scenarios**:

1. **Given** nguyên liệu có `MotherCode` khớp một `IqcMaterialSpec`, **When** mở phiếu, **Then** phiếu có đúng bộ `IqcSpecItem` của spec đó, tiêu chuẩn là của riêng nguyên liệu (không lấy giá trị phổ biến), `SpecNo` được ghi vào bản ghi, `FromDefaultMatrix = false`.
2. **Given** phiếu đã mở, **When** sửa master data (`IqcSpecItem`), **Then** hạng mục đã đóng băng trên phiếu KHÔNG đổi (Nguyên tắc IV).
3. **Given** phiếu vừa mở, **When** chưa ai bấm gì, **Then** mọi hạng mục ở trạng thái `Pass = null` (CHƯA KIỂM), không hạng mục nào là NG.
4. **Given** đổi cờ ngôn ngữ EN/VI, **When** xem phiếu, **Then** nhãn · tiêu chuẩn · phương pháp đổi theo; thiếu bản dịch thì rơi về VI, không bao giờ để ô trống.
5. **Given** nguyên liệu có đủ spec, **When** xem phiếu, **Then** mục 1 có 1 hạng mục (`MT-02`), mục 2 có 7 (NL + NQ), mục 3 có 13 (nhóm còn lại kể cả `KH-01`).

---

### User Story 2 - Nguyên liệu CHƯA có spec vẫn kiểm được bằng ma trận tiêu chuẩn, có đánh dấu (Priority: P1)

590/946 mother code trong MES (62 %) chưa có spec riêng. Với các mã này, phiếu
materialize **13 hạng mục của ma trận tiêu chuẩn** (giá trị phổ biến nhất), và
**phải nói rõ** "tiêu chuẩn mặc định — mã này chưa có spec riêng".

**Why this priority**: Đây là đường chạy ĐA SỐ, không phải ngoại lệ. Không có
cờ phân biệt thì sáu tháng sau không ai biết hồ sơ nào kiểm theo spec thật.

**Independent Test**: Tạo phiếu cho mã không có spec → 13 hạng mục,
`FromDefaultMatrix = true`, `SpecNo = null`, UI hiện dòng cảnh báo mặc định.

**Acceptance Scenarios**:

1. **Given** mã chưa có spec, **When** mở phiếu, **Then** phiếu có đúng 13 hạng mục `InDefaultMatrix`, mỗi bản ghi mang cờ `FromDefaultMatrix = true`.
2. **Given** spec tồn tại nhưng không có dòng chi tiết, **When** mở phiếu, **Then** vẫn lùi về ma trận và đánh dấu.
3. **Given** nguyên liệu không có `MotherCode` hoặc không khớp catalog, **When** tạo phiếu, **Then** vẫn tạo được phiếu (nhập tay như trước) nhưng KHÔNG đoán bừa hạng mục.
4. **Given** tiêu chuẩn dạng khuôn mẫu chưa điền (`"FTM: XXX"`, 521/5 961 dòng), **When** hiện trên phiếu, **Then** được đánh dấu "chưa xác định"; **không cho chấm ĐẠT** nhưng vẫn cho chấm KHÔNG ĐẠT.

---

### User Story 3 - Chấm từng hạng mục và chốt phiếu chỉ khi đã kiểm hết (Priority: P2)

Người kiểm chấm ĐẠT / KHÔNG ĐẠT từng hạng mục (có thể gỡ về CHƯA KIỂM khi bấm
nhầm). Phiếu chỉ chốt được khi không còn hạng mục CHƯA KIỂM; kết luận lô suy
ra từ hạng mục (tất cả ĐẠT ⇒ lô ĐẠT).

**Why this priority**: Không có bước này thì thư viện chỉ là danh sách đọc.

**Independent Test**: `PUT /iqc/tickets/{id}/items/{itemId}` rồi
`POST /iqc/tickets/{id}/complete`; còn CHƯA KIỂM ⇒ bị từ chối.

**Acceptance Scenarios**:

1. **Given** hạng mục CHƯA KIỂM, **When** chấm ĐẠT, **Then** lưu đúng và emit `IQC_ITEM_SET` (ai · phiếu · hạng mục).
2. **Given** còn ≥1 hạng mục CHƯA KIỂM, **When** chốt phiếu, **Then** bị từ chối với thông báo rõ.
3. **Given** đã chấm hết, **When** chốt, **Then** emit `IQC_COMPLETE`, kết luận lô suy ra từ hạng mục.
4. **Given** phiếu CŨ (trước P12, không có hạng mục thư viện), **When** chốt, **Then** vẫn chốt được — không phá dữ liệu lịch sử.
5. **Given** hạng mục thuộc phiếu KHÁC, **When** ghi, **Then** 404, không ghi nhầm.
6. **Given** vai không có policy `QcEdit`, **When** ghi/chốt, **Then** 403 trước khi chạm DB.
7. **Given** trùng lô nhập kho, **When** tạo phiếu, **Then** trả 409 sạch, không 500.

---

### User Story 4 - Engineer+ soạn tiêu chuẩn kiểm THEO MÃ nguyên liệu (Priority: P2)

Engineer / Supervisor / Admin thêm hạng mục kiểm cho một mã nguyên liệu (lô sau
tự có), hoặc tắt hạng mục không dùng. Xoá là **xoá mềm** (`Active=false`), bật
lại được. Đây là hiện thực đầu tiên của contract `cmes-add-new-inline`
(nút `＋ Thêm hạng mục` full-width dưới grid, không modal toàn màn).

**Why this priority**: 590 mã chưa có spec cần được bổ sung dần từ trong app,
không chờ import file.

**Independent Test**: `POST /iqc/specs/{materialCode}/items` với vai Engineer
⇒ 201; với QC/Operator ⇒ 403; `DELETE` ⇒ `Active=false`; `restore` ⇒ bật lại.

**Acceptance Scenarios**:

1. **Given** mã chưa có spec, **When** Engineer thêm hạng mục, **Then** tạo spec cục bộ cho mã đó (không dùng không gian tên `CCL-SPEC-QCxxx` của file master), emit `IQC_SPEC_CREATED` + `IQC_SPEC_ITEM_ADDED`.
2. **Given** mã đã có spec, **When** thêm hạng mục, **Then** thêm vào spec đó, không đẻ spec thứ hai; thêm cùng `ItemId` lần hai ⇒ `Seq` tăng.
3. **Given** hạng mục đã xoá mềm, **When** seed/import lần sau chạy, **Then** KHÔNG hồi sinh dòng đã tắt (DR-1 non-deleting nhưng tôn trọng `Active`).
4. **Given** hạng mục bị tắt, **When** xem phiếu đã mở trước đó, **Then** phiếu không đổi.
5. **Given** `ItemId` ngoài thư viện 21 hạng mục hoặc mã nguyên liệu rỗng, **When** thêm, **Then** bị từ chối.
6. **Given** vai QC / Operator, **When** ghi master, **Then** 403; client không dựng nút (RBAC-by-omission), server vẫn chặn.

---

### User Story 5 - Import thư viện từ file master idempotent, có probe (Priority: P3)

Ops import `IQC_Master_Tieu_chuan_kiem_tra_NVL.xlsx` (đã chuyển CSV). File gốc có
460 spec / 5 974 dòng; sau khi lọc 1 spec template rỗng và 13 dòng của nó, vào DB
là **21 hạng mục · 459 spec · 5 961 dòng chi tiết**. Upsert theo natural key,
KHÔNG xoá, in probe `[seed] iqc_library items=NN specs=NNN spec_items=NNNN updated=NN`.

**Independent Test**: chạy seeder hai lần ⇒ số dòng không đổi, probe khớp.

**Acceptance Scenarios**:

1. **Given** DB trống, **When** seed, **Then** **21 hạng mục · 459 spec · 5 961 dòng** chi tiết; `SourceFrequency` giữ nguyên văn.
2. **Given** đã seed, **When** seed lại, **Then** idempotent — không nhân đôi, không xoá.
3. **Given** 28 tiêu chuẩn thành phẩm xếp nhầm, 4 spec trùng template rỗng, 2 file không đọc được, **When** import, **Then** bị loại — khoá trong `IqcLibraryCsvTests`. (Probe boot hiện KHÔNG in số bị loại; đưa `skipped` vào probe nằm ở `IMPROVEMENT-BACKLOG.md`.)
4. **Given** 92 spec mồ côi (không khớp mother code nào), **When** import, **Then** vẫn giữ trong thư viện (Q1 — Henry: giữ nguyên).

---

### Edge Cases

- `MaterialCodeIfs` (`7xxxxxxx` trích từ file spec) KHÔNG phải mã IFS của MES (`300xxxxx`) — khớp 0/…; cột giữ lại nhưng **không dùng để resolve** cho tới khi Ops xác nhận nó là gì.
- Khớp `MotherCode` không phân biệt hoa/thường và đã trim (352 chính xác → 356 khi bỏ hoa/thường).
- Dòng `Active=false` trong thư viện bị loại khi materialize.
- Thư viện rỗng ⇒ trả rỗng, không lỗi.
- Bản ghi `IqcResultDetail` cũ (7 dòng, `ItemName` tự do, `Pass` bool) giữ nguyên; cột mới đều nullable.
- Xoá mềm hai lần không báo lỗi.

## Thuật ngữ

- **"Mục" = "bước" trên stepper = `Section` trong mã.** Tài liệu dùng "mục" (khớp
  cách QA đọc form giấy); UI và i18n hiển thị "bước"; kiểu dữ liệu tên `Section`.
  Ba tên, một khái niệm.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Hệ thống PHẢI lưu thư viện 3 bảng riêng, cùng hình dạng với `CheckItemLibrary` nhưng KHÔNG nhồi chung (D2): `IqcCheckItemLibrary` (**21**) · `IqcMaterialSpec` (**459**) · `IqcSpecItem` (**5 961**). Đây là số SAU khi lọc: file master có 460 spec / 5 974 dòng, trừ 1 spec template rỗng và 13 dòng chi tiết của nó (§5 scope proposal).
- **FR-002**: Nguồn sự thật cho tiêu chuẩn là dòng chi tiết `IqcSpecItem` (SpecNo × ItemId × Seq), KHÔNG phải cột "phổ biến nhất" của master.
- **FR-003**: Khoá nối nguyên liệu ↔ spec là `RawMaterials.MotherCode = IqcMaterialSpec.MaterialCode`, tra thẳng, không resolver mờ.
- **FR-004**: Khi mở phiếu, hệ thống PHẢI đóng băng vào `IqcResultDetail`: `ItemKey · Seq · SpecNo · GroupCode · GroupLabelVi/En · LabelVi/En · AcceptanceVi/En · MethodVi/En · SourceFrequency · FromDefaultMatrix`.
- **FR-005**: Kiểm TẤT CẢ hạng mục trên MỌI lô (D1); `SourceFrequency` chỉ lưu tra cứu, KHÔNG điều khiển hành vi.
- **FR-006**: Mã chưa có spec ⇒ materialize 13 hạng mục `InDefaultMatrix`, cờ `FromDefaultMatrix = true`, UI nói rõ là mặc định (D3).
- **FR-007**: `Pass` là nullable: null = CHƯA KIỂM, true = đạt, false = không đạt.
- **FR-008**: Tiêu chuẩn placeholder (`XXX`) ⇒ đánh dấu chưa xác định; cấm chấm ĐẠT, cho chấm KHÔNG ĐẠT.
- **FR-009**: Chốt phiếu chỉ khi không còn CHƯA KIỂM; phiếu cũ không có hạng mục vẫn chốt được.
- **FR-010**: Soạn tiêu chuẩn theo mã cần policy `IqcSpecWrite` (Engineer+); đọc cần `IqcSpecRead`; ghi/chốt phiếu cần `QcEdit`. Client ẩn nút khi thiếu quyền, server vẫn 403.
- **FR-011**: Xoá = `Active=false`; có `restore`; seed sau không hồi sinh dòng đã tắt.
- **FR-012**: Emit audit: `IQC_ITEM_SET` · `IQC_COMPLETE` · `IQC_SPEC_CREATED` · `IQC_SPEC_ITEM_ADDED` · `IQC_SPEC_ITEM_DEACTIVATED` · `IQC_SPEC_ITEM_REACTIVATED` (skill `cmes-audit-emit`).
- **FR-013**: Song ngữ VI/EN đầy đủ, thiếu EN rơi về VI (skill `cmes-i18n-parity`).
- **FR-014**: Mục 1 = `MT-02`; mục 2 = nhóm NL + NQ; mục 3 = nhóm còn lại + `MT-01` + `MT-03` + `KH`; nhóm MT chia theo MÃ, không theo nhóm. Hạng mục lạ rơi về mục 3, không biến mất.
- **FR-015**: Mục 4 (mã lỗi & kết luận) và mục 5 (tra cứu lịch sử) KHÔNG phải danh sách hạng mục — ngoài scope.
- **FR-016**: Migration theo skill `cmes-migration-abc`; cột thêm vào bảng cũ đều nullable, không xoá `ItemName`.
- **FR-017**: UI tái dùng nguyên khuôn FQC/OQC: tab nhóm một tầng → bảng `# · ITEM · METHOD · SPEC · VERDICT`, class `ipqc-*`, không đẻ CSS mới ngoài phần tối thiểu.

### Key Entities

- **IqcCheckItemLibrary**: 21 hạng mục chuẩn hoá; `ItemId` (natural key) · `GroupCode` · `GroupLabelVi/En` · `ItemVi/En` · `InDefaultMatrix` · `DefaultAcceptanceVi/En` · `DefaultMethodVi/En` · `Sort` · `Active`.
- **IqcMaterialSpec**: nguyên liệu ↔ spec ↔ NCC; `SpecNo` (natural key) · `MaterialCode` (= MotherCode) · `MaterialCodeIfs` (giữ, chưa dùng) · `SupplierName` · `Revision` · `Active`.
- **IqcSpecItem**: tiêu chuẩn theo từng nguyên liệu; `(SpecNo, ItemId, Seq)` · `AcceptanceVi/En` · `MethodVi/En` · `SourceFrequency` · `Sort` · `Active`.
- **IqcResultDetail** (mở rộng): bản ghi đóng băng trên phiếu — xem FR-004; `Pass` nullable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Probe boot in đúng `[seed] iqc_library items=21 specs=459 spec_items=5961 updated=0`; chạy lần hai ra **cùng con số với `updated=0`** (chứng minh idempotent). Trường thứ tư là `updated=`, KHÔNG phải `skipped=` — số bị loại lúc parse nằm ở `IqcLibraryCsvTests`, không ở probe.
- **SC-002**: `BD-01` trên ≥3 nguyên liệu khác nhau hiện 3 tiêu chuẩn khác nhau trên phiếu.
- **SC-003**: Trên **THƯ VIỆN 21 hạng mục**: mục 1 = 1 · mục 2 = 7 · mục 3 = 13 (khoá trong `IqcTicketSectionTests`). Trên **MỘT PHIẾU cụ thể**, số hạng mục mỗi mục phụ thuộc spec của mã đó — vd `336-H1a` (spec `CCL-SPEC-QC229`, 13 hạng mục) cho **1 / 7 / 5**. Hai con số này đếm hai thứ khác nhau; đừng mở phiếu ra đếm rồi tưởng hỏng.
- **SC-004**: Mã chưa có spec: đúng 13 hạng mục, 100 % mang cờ `FromDefaultMatrix`.
- **SC-005**: Sửa master sau khi mở phiếu ⇒ 0 thay đổi trên bản ghi đóng băng.
- **SC-006**: Mỗi test bảo vệ hành vi trên phải ĐỎ khi hoàn nguyên fix (Nguyên tắc I + II).
- **SC-007**: `gate-all.sh` **19/19 PASS** và cả **4 test project xanh, 0 fail**. (Tiêu chí là ĐÍCH; trạng thái từng lần chạy ghi ở `tasks.md` T039/T040, không ghi vào đây.)
- **SC-008**: Ảnh chụp 768 px cho mọi PR chạm `.razor`.

## Assumptions

- D1 (kiểm mọi lô, ghi đè tần suất tháng) là thay đổi TIÊU CHUẨN KIỂM — Q4: QA cần ghi nhận bằng văn bản; phần mềm chỉ lưu vết `SourceFrequency`.
- Dữ liệu master import từ file; admin UI nhập/sửa toàn bộ master ngoài scope (chỉ có soạn theo mã — US4).
- Gắn kết quả IQC vào `MaterialLot` genealogy ngoài scope.
- 48 dòng lệch tên thật và 92 spec mồ côi: giữ nguyên, không bổ sung (Henry 2026-08-28).
