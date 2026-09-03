---
description: "Task list — P12 thư viện tiêu chuẩn kiểm tra NVL cho IQC (hồi cứu trạng thái thật trên nhánh)"
---

# Tasks: Thư viện tiêu chuẩn kiểm tra NVL cho IQC (P12)

**Input**: Design documents from `/specs/012-iqc-check-standard-library/`

**Prerequisites**: plan.md · spec.md · `CCL-MES-Hybrid/docs/p12-iqc-library-scope-proposal.md`

**Tests**: BẮT BUỘC theo Hiến pháp §I/§II — mỗi hành vi có test ĐỎ khi hoàn nguyên fix.

**Organization**: Theo user story của spec.md. `[x]` = đã có commit trên nhánh `feat/p12-iqc-check-standard-library`; `[ ]` = còn mở.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: chạy song song được (file khác nhau, không phụ thuộc)
- **[Story]**: US1…US5 theo spec.md

## Path Conventions

- Core dùng chung: `src/CCL.MES.{Domain,Application,Infrastructure}` · test `tests/CCL.MES.Tests`
- Hybrid: `CCL-MES-Hybrid/src/CCL.MES.{Api,Shared,Hybrid.Razor}` · test `CCL-MES-Hybrid/tests/*`

---

## Phase 1: Setup

- [x] T001 Scope proposal + đo dữ liệu thật (451 NVL · 460 spec · 21 hạng mục · 5 974 dòng) trong `CCL-MES-Hybrid/docs/p12-iqc-library-scope-proposal.md`
- [x] T002 Đo khoá nối trên live DB (§9): `MotherCode` 352/356 khớp · `MaterialCodeIfs` 0 khớp — chốt D3/D4 với Henry 2026-08-28
- [x] T003 Chuyển `IQC_Master_Tieu_chuan_kiem_tra_NVL.xlsx` sang CSV seed + parser `src/CCL.MES.Application/Services/IqcLibraryCsv.cs` (commit 3574f3e)

---

## Phase 2: Foundational — schema + seed (chặn mọi story)

- [x] T004 Entity `IqcCheckItemLibrary` · `IqcMaterialSpec` · `IqcSpecItem` trong `src/CCL.MES.Domain/Entities/IqcLibrary.cs` (3574f3e)
- [x] T005 DbSet + cấu hình unique `(SpecNo, ItemId, Seq)` trong `src/CCL.MES.Application/IMesDbContext.cs` và `src/CCL.MES.Infrastructure/MesDbContext.cs`
- [x] T006 Migration `20260828091742_AddIqcCheckStandardLibrary` (3 bảng)
- [x] T007 Migration `20260828095900_AddIqcDefaultMatrixColumns` (`InDefaultMatrix` + `Default*` trên hạng mục)
- [x] T008 Migration `20260828100725_AddIqcResultDetailFrozenColumns` (`Pass` nullable + 14 cột đóng băng trên `IqcResultDetail`)
- [x] T009 Seeder idempotent + probe `[seed] iqc_library …` trong `src/CCL.MES.Infrastructure/DbSeeder.cs`; test `tests/CCL.MES.Tests/Integration/IqcLibrarySeederTests.cs` + `Unit/IqcLibraryCsvTests.cs`
- [x] T010 6 hằng audit `IQC_*` trong `src/CCL.MES.Domain/Audit/AuditAction.cs` (nay là 7 — thêm `IQC_COMPLETE`)
- [x] T010b [FR-013] Song ngữ VI+EN: 42 khoá trong `CCL-MES-Hybrid/src/CCL.MES.Hybrid.Client/Localization/TranslationCatalog.IqcModule.cs` (13 khoá lưới hạng mục + 24 khoá soạn spec + 5 khoá chốt phiếu) + cơ chế **rơi về VI khi thiếu EN** ở hàm `Pick()` của `IqcCheckItemDto` / `IqcSpecItemDto` / `IqcLibraryOptionDto` — test `IqcCheckItemGridTests.Doi_sang_EN_thi_nhan_doi_ma_KHOA_tab_van_la_VI` + `IqcSpecEditorTests.Doi_sang_EN_thi_nhan_hang_muc_va_nhom_doi_theo`. (Gate `i18n` chỉ chặn hardcode, KHÔNG kiểm đủ cặp VI/EN — nên phải có task riêng.)
- [x] T011 Xác nhận 3 migration đã strip `type:` / `.HasColumnType(...)` — grep 2026-09-03: 0 hit trên cả 3 file `.cs` (Designer.cs không tính)
- [x] T012 Migration lên live DB — **ĐÃ ÁP 2026-08-28**, Phase A→B→C, rowcount trước=sau, `integrity_check=ok`. Nhật ký đầy đủ: `CCL-MES-Hybrid/docs/p12-migration-log.md`. ⚠ Backup Phase A từng để `/tmp` và đã mất khi `/tmp` bị dọn — mốc gốc mới ở `data/Backup/SQLite/ccl_mes.db.p12-post-migration.20260903-134522` (sha256 `a12cfc1d…`)

**Checkpoint**: schema + seed sẵn sàng ✅ · T011 + T012 đã đóng.

---

## Phase 3: User Story 1 — Mở phiếu có sẵn bộ hạng mục đúng (P1) 🎯 MVP

**Goal**: Resolve `MotherCode → SpecNo → IqcSpecItem[]`, đóng băng song ngữ, chia mục 1/2/3.

**Independent Test**: `GET /iqc/tickets/{id}/items` trả đúng hạng mục + tiêu chuẩn riêng của mã.

- [x] T013 [US1] `IqcCheckResolver` trong `src/CCL.MES.Application/Services/IqcCheckResolver.cs` (15424c6) — test `Unit/IqcCheckResolverTests.cs` (15 case: khớp MotherCode · case-insensitive · KHÔNG khớp IFS · tiêu chuẩn RIÊNG · Inactive bị loại …)
- [x] T014 [US1] `IqcTicketSection` chia mục theo MÃ trong `src/CCL.MES.Application/Services/IqcTicketSection.cs` — test `Unit/IqcTicketSectionTests.cs` khoá 1 / 7 / 13, hạng mục lạ rơi về mục 3
- [x] T015 [US1] Materialize + đóng băng lúc mở phiếu trong `src/CCL.MES.Application/Services/IqcService.cs` (3d1fc30) — test `Integration/IqcTicketMaterializeTests.cs` (đường service + đường UI CreateTicket)
- [x] T016 [US1] `GET /iqc/tickets/{id}/items` trong `CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/IqcController.cs` + DTO `CCL.MES.Shared/Quality/IqcDtos.cs`
- [x] T017 [US1] `IqcCheckItemGrid.razor` (bảng `# · ITEM · METHOD · SPEC · VERDICT`, class `ipqc-*`) — test `IqcCheckItemGridTests.cs` (14 fixture)
- [x] T018 [US1] Mục 2 / mục 3 của `MaterialsInspectionForm.razor` dùng grid (99afa6c) — test `IqcModuleTests.cs` khoá đường màn hình THẬT gọi endpoint (L64, d253a4d)
- [ ] T019 [US1] SC-002 — **phần DỮ LIỆU đã đo** trên DB thật 2026-09-03: `BD-01` có **60 tiêu chuẩn KHÁC NHAU** trải trên 449 spec (yêu cầu ≥3), vd `3M 5915P → FTM: 2(72h)` · `3M 897 → FTM: 1( ASTM D3330 )` · `3M 9448A → FTM 1` · `ADS1412 → FTM: 2(24h)`. **CÒN LẠI: ảnh 768 px** mở phiếu của ≥3 mã đó cạnh nhau — cần đăng nhập app.

**Checkpoint**: US1 chạy được độc lập ✅ (T019 = bằng chứng VERIFY)

---

## Phase 4: User Story 2 — Mã chưa có spec dùng ma trận, có đánh dấu (P1)

- [x] T020 [US2] Ma trận 13 hạng mục trong resolver (`Chua_co_spec_thi_dung_ma_tran_13_hang_muc`, `Hang_muc_ma_tran_mang_co_phan_biet…`, `Spec_ton_tai_nhung_khong_co_dong_chi_tiet_thi_van_lui_ve_ma_tran`)
- [x] T021 [US2] Cờ `FromDefaultMatrix` đóng băng + không đoán bừa khi thiếu MotherCode (`IqcTicketMaterializeTests`: `…CHUA_co_spec…`, `Nguyen_lieu_khong_co_MotherCode_thi_KHONG_doan_bua`, `…ma_KHONG_khop_catalog…`)
- [x] T022 [US2] Đánh dấu tiêu chuẩn placeholder `XXX` (`Tieu_chuan_dang_XXX_bi_danh_dau_chua_xac_dinh`, `Nhan_dien_placeholder`)
- [x] T023 [US2] UI hiện dòng "tiêu chuẩn mặc định — mã này chưa có spec riêng" trong `IqcCheckItemGrid.razor` / `MaterialsInspectionForm.razor`
- [ ] T024 [US2] SC-004 — **phần DỮ LIỆU đã đo** trên DB thật 2026-09-03: ma trận mặc định đúng **13 hạng mục** (`InDefaultMatrix=1`); catalog có **590 mother code distinct chưa có spec** (946 tổng − 356 có spec). Đo qua wire trước đó trên một phiếu thật của mã chưa có spec: 13 hạng mục · `FromDefaultMatrix=1` cả 13 · `SpecNo` NULL cả 13. **CÒN LẠI: ảnh 768 px** — cần đăng nhập app.

---

## Phase 5: User Story 3 — Chấm hạng mục và chốt phiếu (P2)

- [x] T025 [US3] `PUT /iqc/tickets/{id}/items/{itemId}` (QcEdit) + emit `IQC_ITEM_SET`; gỡ về CHƯA KIỂM; cấm ĐẠT khi placeholder — test `Integration/IqcTicketItemsTests.cs`
- [x] T026 [US3] `POST /iqc/tickets/{id}/complete` (QcEdit): từ chối khi còn CHƯA KIỂM, phiếu cũ vẫn chốt, kết luận suy từ hạng mục, emit `IQC_COMPLETE` (fe3d919)
- [x] T027 [US3] Hạng mục của phiếu KHÁC ⇒ 404; vai thiếu quyền ⇒ chặn trước khi chạm DB
- [x] T028 [US3] Trùng lô ⇒ 409 sạch thay vì 500 trong `MaterialLotScanService.cs` (fe3d919) — test `CCL.MES.Api.Tests/IqcTicketTests.cs`
- [x] T029 [US3] `ConfirmToggle` OK/NG trên grid (gate `confirm-toggle`)
- [x] T029b [US3] UI chốt phiếu trong `MaterialsInspectionForm.razor` (fe3d919): thanh tiến độ **"Đã kiểm 7/13"** hiện ở MỌI bước của stepper · nút **Chốt phiếu** khoá tới khi chấm đủ, kèm câu nói rõ còn thiếu mấy mục · phiếu đã chốt ⇒ lưới chuyển **chỉ đọc** · **BỎ nút "Complete ticket" ở chế độ tạo mới** (đổi thành "Lưu & bắt đầu kiểm") — *thay đổi hành vi CÓ CHỦ ĐÍCH* của một affordance đã ship: lúc tạo phiếu thì hạng mục kiểm chưa tồn tại nên "chốt" là vô nghĩa. Test: 5 fixture trong `IqcModuleTests` (`Con_hang_muc_chua_kiem_thi_nut_CHOT_bi_khoa…`, `Cham_HET_thi_nut_CHOT_mo_khoa`, `Bam_CHOT_thi_goi_dung_phieu`, `Thanh_tien_do_hien_o_MOI_buoc…`, `Che_do_TAO_MOI_KHONG_co_nut_chot`)

---

## Phase 6: User Story 4 — Engineer+ soạn tiêu chuẩn theo mã (P2)

- [x] T030 [US4] `IqcSpecEditService` trong `src/CCL.MES.Application/Services/IqcSpecEditService.cs` (7548df0): spec cục bộ · thêm (Seq tăng) · xoá mềm · restore · RBAC — test `Integration/IqcSpecEditTests.cs` (19 case)
- [x] T031 [US4] `IqcSpecController` (`GET {materialCode}` · `POST items` · `DELETE items/{id}` · `POST restore`) policy `IqcSpecRead` / `IqcSpecWrite` — test `CCL.MES.Api.Tests/IqcSpecControllerTests.cs` (10 fixture)
- [x] T032 [US4] Seed không hồi sinh dòng `Active=false` (`Dong_da_XOA_MEM_khong_bi_lan_seed_ke_tiep_hoi_sinh`)
- [x] T033 [US4] `IqcSpecEditor.razor` — nút `＋ Thêm hạng mục` full-width theo contract `cmes-add-new-inline`; ẩn khi thiếu quyền — test `IqcSpecEditorTests.cs` (17 fixture)
- [x] T034 [US4] Emit `IQC_SPEC_CREATED` · `IQC_SPEC_ITEM_ADDED` · `IQC_SPEC_ITEM_DEACTIVATED` · `IQC_SPEC_ITEM_REACTIVATED`
- [ ] T035 [US4] Cập nhật skill `cmes-add-new-inline` (`.claude/skills/`): ghi nhận P12 là implementation/gate đầu tiên ở main — sau khi merge

---

## Phase 7: User Story 5 — Import thư viện (P3)

- [x] T036 [US5] Loại 28 tiêu chuẩn thành phẩm · 4 spec trùng template · 2 file lỗi, ghi `skipped` (`IqcLibraryCsvTests`)
- [x] T037 [US5] Giữ `SourceFrequency` nguyên văn (D1) và 92 spec mồ côi (Q1)
- [x] T038 [US5] Probe boot đo trên DB thật 2026-09-03: `[seed] iqc_library items=0 specs=0 spec_items=0 updated=0` (lần chạy thứ N — **0/0/0/0 chính là bằng chứng idempotent**; lần seed đầu đã nạp 21/459/5961, khoá trong `IqcLibrarySeederTests`). Trường thứ tư là `updated=`, KHÔNG phải `skipped=` — SC-001 đã sửa theo.

---

## Phase 8: VERIFY + LEARN (pha 5–6 của vòng lặp — CHƯA LÀM)

- [x] T039 `dotnet test` **4** test project trên máy có SDK, 2026-09-03: `tests/CCL.MES.Tests` **1337** · `CCL.MES.Api.Tests` **903** · `CCL.MES.Hybrid.Client.Tests` **731** · `CCL.MES.Hybrid.Razor.Tests` **533** — tổng **3504 passed, 0 failed**
- [x] T040 `bash CCL-MES-Hybrid/scripts/gate-all.sh` 2026-09-03 → **PASS=19 FAIL=0 SKIP=0**. (Một lần FAIL giữa chừng ở gate `tokens` do `font-weight: 600` viết thẳng trong CSS mới — đã sửa sang `var(--fw-semibold)`.)
- [x] T041 SC-006 — đo ĐỎ/XANH cho `IqcCheckResolver` 2026-09-03: hoàn nguyên đúng bất biến chính (`FromSpec` lấy `DefaultAcceptance*` của thư viện thay vì dòng chi tiết theo mã) ⇒ **4 test ĐỎ**: `Co_spec_thi_lay_tieu_chuan_RIENG_chu_khong_lay_gia_tri_chung` · `Mo_ticket_cho_ma_CO_spec_thi_dung_tieu_chuan_RIENG` · `Duong_UI_CreateTicket_CO_spec_thi_dung_hang_muc_RIENG` · `Nhieu_tieu_chi_cung_ma_deu_duoc_giu`. Khôi phục ⇒ **31/31 XANH**. Ba chỗ khác đã đo trước đó:
  - seeder hồi sinh dòng `Active=false`: hoàn nguyên 1 dòng `DbSeeder` ⇒ 1 test ĐỎ, khôi phục ⇒ 24/24 XANH
  - L64 (form phải THẬT SỰ gọi endpoint): chặn nhánh nạp ⇒ 1 test ĐỎ, khôi phục ⇒ 34/34 XANH
  - trùng lô 409: test ĐỎ với đúng `SqliteException UNIQUE constraint failed` trước fix, XANH sau
- [x] T042 STOP-gate vùng cấm — **Henry chọn sửa luật 2026-09-03**: hiến pháp **v1.1.0** thu hẹp vùng cấm về `src/CCL.MES.Web`; `CLAUDE.md` §0 sửa theo. Căn cứ đo được: 60 commit trên `main` — `Web` 0 lần đổi, ba tầng dùng chung 43 lần. P12 không còn là vi phạm.
- [ ] T043 Q4: QA ghi nhận bằng văn bản quyết định D1 (kiểm mọi lô, ghi đè tần suất tháng) — ngoài phần mềm, nhưng là điều kiện đưa vào sản xuất
- [x] T044 Lesson card: **L65** (bằng chứng Phase A không được để ở `/tmp`) · **L66** (khoá nối phải ĐO — `MaterialCodeIfs` 7xxxxxxx ≠ `PartNo` 300xxxxx, khớp 0 dòng) · **L67** (bản ghi bằng chứng thiếu một chiều thì nói dối im lặng — `Pass` bool nuốt "chưa kiểm"; bộ mặc định không mang cờ nguồn gốc). Cả ba có cột "Cơ chế chặn tái phát" trỏ test cụ thể + đã vào Index.
- [~] T045 ~~Quyết định `.claude/skills/liteparse` · `markitdown`~~ — **CHUYỂN khỏi P12**: việc dọn kho công cụ, không liên quan tiêu chuẩn kiểm NVL. Đã chuyển sang `CCL-MES-Hybrid/docs/IMPROVEMENT-BACKLOG.md`.
- [ ] T046 Mở PR `feat/p12-iqc-check-standard-library → main` với ảnh 768 px, số test thật, probe seed, migration step trong Henry-action

---

## Dependencies & Execution Order

- Phase 2 chặn mọi story (đã xong).
- US1 → US2 (cùng resolver) → US3 (cần hạng mục trên phiếu). US4 và US5 độc lập với US3, cùng phụ thuộc Phase 2.
- Phase 8 chặn merge: T039 · T040 · T042 · T046 bắt buộc; T041 · T043 · T044 nên làm trước merge.

## Implementation Status

*Cập nhật 2026-09-03 sau `/speckit-analyze`.*

| Nhóm | Xong | Mở |
|---|---|---|
| Setup + Foundation | 13 | 0 |
| US1–US5 | 24 | 3 (T019, T024, T035) |
| VERIFY + LEARN | 5 | 2 (T043, T046) |
| **Tổng** | **42** | **5** (+1 chuyển sang backlog) |

**Không còn hạng mục CODE nào.** Năm việc còn lại:

| Task | Cần gì | Ai làm được |
|---|---|---|
| T019 · T024 | ảnh 768 px (phần dữ liệu ĐÃ đo, ghi ngay trong task) | **Henry** — cần đăng nhập app |
| T035 | cập nhật skill `cmes-add-new-inline` ghi nhận P12 là implementation đầu tiên | **sau khi merge** (giờ ghi là sai: main chưa có) |
| T043 | QA ghi nhận D1 (kiểm mọi lô) bằng văn bản | **ngoài phần mềm** |
| T046 | PR #243 đã OPEN — còn thiếu ảnh 768 px + số test thật trong body | **Henry** duyệt body |
