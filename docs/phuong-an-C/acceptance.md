# Phương án C — Nghiệm thu (Acceptance) — KHUNG TRỐNG
> Agent điền sau Bước 4 (Gate A) và sau Bước 5–6 (Gate B). KHÔNG tuyên bố "C hoàn thành" nếu còn 1 mục Gate A fail.
> Mỗi mục PHẢI có: tên test + lệnh chạy + OUTPUT THẬT (paste) + Pass/Fail. Không paste output = chưa nghiệm thu.
> Ca chuẩn: PartNo `20000000C` (FLEXO PRINT → FB CUT → FQC & Packaging → OQC). Ca phụ: 1 mã chỉ SCREEN, 1 mã HP Indigo.

Môi trường: `[ctx] DB=<abs-path> + sha8` · build SHA: ____ · nhánh: `feat/phuong-an-C` · ngày: ____

---

## GATE A — sau Bước 4 (lõi P0 auto-sync)
| # | Tiêu chí | Test (tên) | Lệnh chạy | Output thật (tóm tắt/paste) | Kết quả |
|---|---|---|---|---|---|
| A1 | Tạo WO cho PartNo 20000000C | | | | ⬜ |
| A2 | Resolver suy ra đúng {LABEL, PRESS_CNC, FQC, OQC} | | | | ⬜ |
| A3 | Tự nạp đúng bộ item theo từng phase (không mặc định, không nhập tay) | | | | ⬜ |
| A4 | Mã chỉ in lụa (SCREEN) → nạp bộ SILK, KHÔNG nạp bộ cắt | | | | ⬜ |
| A4'| Mã HP Indigo → nạp bộ DIGITAL (gồm item digital-riêng) | | | | ⬜ |
| A5 | Freeze ProfileSnapshotJson (sửa thư viện KHÔNG đổi check đang chạy) | | | | ⬜ |
| A6 | Dual-sig IPQC + 3-sig OQC vẫn đúng | | | | ⬜ |
| A7 | State-machine 12-phase không đổi hành vi; audit row đúng mã | | | | ⬜ |
| A8 | Seed/import idempotent (chạy 2 lần cùng kết quả) | | | | ⬜ |

**Kết luận Gate A:** ⬜ CHƯA ĐẠT · ngày đạt: ____ · người duyệt: ____

---

## GATE B — sau Bước 5–6 (P1)
| # | Tiêu chí | Test (tên) | Lệnh chạy | Output thật (tóm tắt/paste) | Kết quả |
|---|---|---|---|---|---|
| B9 | Dropdown mã lỗi chỉ hiện mã hợp lệ theo process/SP; mã sai → 422 | | | | ⬜ |
| B10| Admin sửa/import → WO MỚI nhận bản mới; WO cũ giữ snapshot (không hồi tố) | | | | ⬜ |

**Kết luận Gate B:** ⬜ CHƯA ĐẠT · ngày đạt: ____ · người duyệt: ____

---

## Ràng buộc bất biến đã kiểm lại (regression)
- [ ] `dotnet test` toàn bộ XANH (paste số liệu: ____ pass / ____ fail)
- [ ] Migration up/down sạch trên isolated /tmp DB (CLAUDE.md §4)
- [ ] Legacy-parity IPQC: 4 slot canonical khớp giữa shadow cũ ↔ item mới
- [ ] Không lẫn file vùng cấm: `git diff --name-only | grep -E "^CMES/|SpecHub|_archive"` → rỗng
- [ ] Boot probe seed in đúng số (vd `[seed] qc_profiles ...`, `[seed] reason_codes ...`)

## Phụ lục — lệnh/script dùng để nghiệm thu
_(agent điền: verify-*.sh, checkpoint-*.sh, curl smoke, ...)_
