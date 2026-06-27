# Phương án C — Backlog (HOÃN, không làm trong phiên review-fix)

> Phát sinh từ /code-review (8 finding) + đợt fix F1–F6. Hai mục dưới là
> **altitude/maintainability**, đã giảm rủi ro bằng các fix đã land — để lại
> làm riêng, có chủ đích, không nhồi vào đợt này.

## B-#4 — Tổng quát hoá IPQC vào WoQcCheck (gỡ trùng materialize/freeze/ValidateNg)
**Bối cảnh (finding #4):** IPQC dùng cơ chế data-driven SONG SONG nhưng RIÊNG
(`WoIpqcCheck` + `WoIpqcCheckItem` + `ItemsProfileSnapshotJson` + auto-sync trong
`IpqcReviewController`) so với FQC/OQC (`WoQcCheck` + `WoQcCheckItem` +
`ProfileSnapshotJson` + materialize trong `WoQcReviewController`). Logic
lazy-materialise + FREEZE + race-handling bị nhân đôi giữa 2 controller.

**Đề xuất:** trích một service chung (vd `IQcCheckMaterializer`) lo: resolve nguồn
profile → freeze snapshot → tạo item + xử lý UNIQUE-index race. Cả IPQC (nguồn =
routing→ProcessLineMap→CheckItemLibrary) lẫn FQC/OQC (nguồn = Product override →
QcProfileSeed) cắm vào qua 1 strategy. Giảm 2 đường materialise → 1.

**Rủi ro nếu để lâu:** thêm version-stamp/metadata vào snapshot, hay đổi luật
freeze → phải sửa 2 nơi lockstep, dễ phân kỳ giữa IPQC và FQC/OQC.

**Đã giảm nhẹ:** F2 đã chuẩn hoá trạng thái auto-sync (`autoSyncStatus`) một chỗ;
test `IpqcAutoSyncTests` + `IpqcAutoSyncControllerTests` khoá hành vi để refactor an toàn.

## B-#5 — Gộp ValidateNg thành 1 helper dùng chung
**Bối cảnh (finding #5):** validate mã NG (`ValidateNgAsync`) tồn tại ở cả
`IpqcReviewController` và `WoQcReviewController` (logic giống: code phải thuộc
`ReasonCodeKind.Scrap`). Luật NG mới (vd scope theo line ở SERVER) dễ thêm 1 nơi quên nơi kia.

**Đề xuất:** 1 helper `QcNgValidator.Validate(reasonCode, note, scopeLines?)` dùng chung
3 đường ghi (slot legacy, item data-driven, FQC/OQC item).

**Đã giảm nhẹ:** F1 đã chặn slot-PUT khi WO ở mode item → bớt đường ghi IPQC còn
hiệu lực (chỉ item-PUT cho WO data-driven). F5 đã có scope theo line ở tầng đọc
(`/check-item-library/reason-codes?lines=`).

## B-#DR1 — ProcessLineMapSeed là upsert (no sync-delete) → đổi tên/bỏ key để lại orphan row
**Bối cảnh (delta-review 2026-06-27):** `DbSeeder.SeedProcessLineMapAsync` insert/update theo
natural key (MatchType, MatchValue) nhưng KHÔNG xoá row không còn trong seed. Khi Q1 đổi tên
key `R2S`→`R2R`/`R2SC`, row `R2S` cũ thành **orphan** trên DB đã seed.

**Hiện trạng:** vô hại về chức năng (longest-match cho `R2SC` (4 ký tự) thắng `R2S` (3)). Đã
**dọn tay** orphan `R2S` trên live lần này → live map = 57 = canonical seed.

**Cần làm (sau):** bước **prune có bảo vệ** — chỉ xoá row gốc-seed không còn trong
`ProcessLineMapSeed.DefaultEntries()`, **CHỪA** row admin tự thêm (vd cờ `CreatedBy='seed'`
hoặc cột `IsSeedManaged`), HOẶC reconcile (seed = nguồn chân lý cho row seed-origin). KHÔNG
sync-delete mù (sẽ xoá nhầm row admin).

**Rủi ro nếu để lâu:** mỗi lần rename/bỏ key map tương lai cần nhớ dọn tay; quên → orphan
tích tụ (vẫn vô hại nhờ longest-match nhưng map ≠ canonical, khó audit).

---
*Khi làm: tạo nhánh riêng, thêm parity test trước khi refactor (B-#4 đụng freeze +
race — rủi ro cao). Không gộp vào PR review-fix này.*
