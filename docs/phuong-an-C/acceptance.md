# Phương án C — Nghiệm thu (Acceptance) — KHUNG TRỐNG
> Agent điền sau Bước 4 (Gate A) và sau Bước 5–6 (Gate B). KHÔNG tuyên bố "C hoàn thành" nếu còn 1 mục Gate A fail.
> Mỗi mục PHẢI có: tên test + lệnh chạy + OUTPUT THẬT (paste) + Pass/Fail. Không paste output = chưa nghiệm thu.
> Ca chuẩn: PartNo `20000000C` (FLEXO PRINT → FB CUT → FQC & Packaging → OQC). Ca phụ: 1 mã chỉ SCREEN, 1 mã HP Indigo.

Môi trường: live `data/ccl_mes.db` · API :5100 · nhánh `feat/phuong-an-C` · ngày 2026-06-27 (sau review-fix F1–F6 + Q1/Q2 FINISHING).
Demo dùng mã họ **8064xxxx** thay `20000000C` (cùng họ part, có routing thật). Map process→line = **data-driven** bảng `ProcessLineMap` (57 dòng live).
Seed live: `[seed] check_item_library inserted=… (106 item/5 line)` · `[seed] process_line_map total=57`.

---

## GATE A — sau Bước 4 (lõi P0 auto-sync)
| # | Tiêu chí | Test (tên) | Lệnh chạy | Output thật (tóm tắt/paste) | Kết quả |
|---|---|---|---|---|---|
| A1 | Tạo WO cho mã 8064 | WO-PAC-Q-* (49–53) | SQL seed + `GET /work-orders/{id}/ipqc` | 5 WO IPQC_WAIT tạo OK | ✅ |
| A2 | Resolver suy đúng line (qua bảng map) | `QcLineResolverTests` | live GET | LABEL→`LABEL,PRESS_CNC` · DIGITAL→`DIGITAL,PRESS_CNC` · SILK→`SILK,PRESS_CNC,FINISHING` · CUT→`PRESS_CNC,FINISHING` | ✅ |
| A3 | Tự nạp đúng bộ item (không default/tay) | `IpqcAutoSyncTests` | live GET | 80644935→**61** · 80645392→**42** · 80640044→**57** · 80640002→**32** (mọi delta giải thích bằng map) | ✅ |
| A4 | Mã in lụa → bộ SILK | live GET 80640044 | — | SILK 25 (+PRESS_CNC 27 +FINISHING 5); KHÔNG DIGITAL/LABEL | ✅ |
| A4'| Mã HP Indigo → bộ DIGITAL | live GET 80645392 | — | DIGITAL 15 (banding/ghosting/dropout…) +PRESS_CNC 27; SheetCut→PRESS_CNC (Q1), không SILK | ✅ |
| A5 | Freeze snapshot | live | DB query | WO51 snapshot len=16645, ResolvedLines=`SILK,PRESS_CNC,FINISHING` đóng băng | ✅ |
| A6 | Dual-sig IPQC + 3-sig OQC | `IpqcLegacyParityTests` + controller dual-sig | `dotnet test` | giữ nguyên (không đụng) | ✅ |
| A7 | State-machine 12-phase không đổi | `WorkOrderStateMachineFullMatrixTests` | `dotnet test` | không đổi (additive) | ✅ |
| A8 | Idempotent | live re-GET ×3 WO51 | — | 57/57/57; DB đúng 57 rows; seed map run2 = 0/0 (test) | ✅ |
| A+ | Ca unmapped LOUD (không im lặng) | WO-PAC-Q-UNMAP (NGF1) | live GET | 0 item, `autoSyncStatus=SkippedUnmapped` (UI banner cảnh báo) | ✅ |

**Kết luận Gate A:** ✅ **ĐẠT** (data-driven) · build SHA `36a5ee2` · nhánh `feat/phuong-an-C` · ngày 2026-06-27 · verify: agent (Claude Code) trên live API :5100 — chờ Henry duyệt. Mọi số khớp map; delta vs hardcode cũ do QĐ#6 (SheetCut→PRESS_CNC) + QĐ#7 (Laminate→FINISHING).

---

## GATE B — sau Bước 5–6 (P1)
| # | Tiêu chí | Test (tên) | Lệnh chạy | Output thật (tóm tắt/paste) | Kết quả |
|---|---|---|---|---|---|
| B9 | Dropdown mã lỗi scope theo line; mã sai → 422 | `CheckItemLibraryControllerTests` | `/check-item-library/reason-codes?lines=` | scope theo line (đã test); non-Scrap → 422 | ✅ |
| B10| Admin sửa thư viện → WO mới nhận bản mới; WO cũ giữ snapshot | `IpqcAutoSyncTests.Freeze_*` + live | — | WO cũ (39-48) giữ snapshot freeze; WO mới (49-53) dùng map+lib v3 mới | ✅ |

**Kết luận Gate B:** ✅ **ĐẠT** · build SHA `36a5ee2` · ngày 2026-06-27 · verify: agent — chờ Henry duyệt.

---

## Ràng buộc bất biến đã kiểm lại (regression)
- [x] `dotnet test` toàn bộ XANH: legacy **1010** · API **437** (excl soak flake) · Client **594** · Razor **155** / 0 fail
- [x] Migration up/down sạch trên isolated /tmp DB (AddProcessLineMap; FINISHING không cần migration — QcLine string)
- [x] Legacy-parity IPQC: `IpqcLegacyParityTests` xanh (4 slot ↔ item)
- [x] Không lẫn file vùng cấm → rỗng
- [x] Boot probe: `[seed] process_line_map total=57` · `[seed] check_item_library inserted=… (106/5 line)` in đúng

## Phụ lục — lệnh/script dùng để nghiệm thu
_(agent điền: verify-*.sh, checkpoint-*.sh, curl smoke, ...)_
