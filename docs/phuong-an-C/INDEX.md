# Phương án C — Chỉ mục triển khai (LIVING DOC)

> QC engine đầy đủ theo Routine: thư viện lỗi IPQC/FQC/OQC + auto-sync vào WO.
> Nhánh: `feat/phuong-an-C`. Nguồn kế hoạch: `build_feasibility_deck.js` · `PhuongAn_ThuVienLoi_AutoSync_QC_v1.pptx`.
> Agent CẬP NHẬT file này sau mỗi bước (quy tắc E). Trạng thái: ⬜ Chưa · 🟡 Đang làm · ✅ Xong (test xanh) · ⛔ Bị chặn.

## 1 · Quyết định đã chốt (Decision Log)
| # | Vấn đề | Quyết định | Ngày |
|---|---|---|---|
| 1 | Sửa legacy `Domain/Application/Infrastructure` | ✅ Cho phép — **additive-first** + nhánh riêng + migration up/down + legacy-parity test xanh | 2026-06-26 |
| 2 | Refactor IPQC 4-slot | ✅ **Shadow table**: giữ 4 cột cũ + thêm `WoIpqcCheckItem` + parity test | 2026-06-26 |
| 3 | FQC/OQC có theo điều kiện? | ✅ **Luôn có** (12-phase bắt buộc qua FQC_PENDING/OQC_PENDING); routing chỉ làm giàu item; KHÔNG bỏ cổng hàng xuất | 2026-06-26 |
| 4 | Bảng mới vs mở rộng | ✅ Bảng mới `CheckItemLibrary`; **mở rộng `ReasonCode`** (không tạo DefectCode mới) | 2026-06-26 |
| 5 | Map process → QC line | ✅ **ĐÃ THOẢ** (review-fix F6, 2026-06-27): map là **DỮ LIỆU** qua bảng `ProcessLineMap` (MatchType ProcessCode/WorkCenterPrefix/OpKeyword → QcLine LABEL/DIGITAL/SILK/PRESS_CNC/NONE); resolver tra bảng, KHÔNG còn keyword hardcode. Sửa map = sửa seed. | 2026-06-26 → 2026-06-27 |
| + | Tắt API :5100 + MAUI khi migration | ✅ Tắt trước Bước 1/2 (tránh khóa SQLite) | 2026-06-26 |

> **Review-fix (2026-06-27)** — /code-review 8 finding: FIX **F1** (chặn slot-PUT khi WO ở mode item, 422) ·
> **F2** (self-heal materialize + `autoSyncStatus` không im lặng) · **F4** (CSV strict: skip bad row + exit
> non-zero) · **F5** (auth `NpiRead` cho library/process-map) · **F6** (map data-driven `ProcessLineMap` →
> đóng **#3** + **#7**). HOÃN **B-#4/B-#5** ([BACKLOG.md](BACKLOG.md)).

## 2 · Ranh giới BẤT BIẾN (đã định vị file:line ở Bước 0)
- [ ] 12-phase state-machine (169-cell) — không đổi hành vi
- [ ] Dual-sig IPQC (Q3) — Inspector ≠ QA approver
- [ ] 3-sig OQC — Inspector ≠ Reviewer ≠ Approver
- [ ] Freeze `ProfileSnapshotJson` — sửa thư viện KHÔNG đổi check đang chạy
- [ ] Seed/import idempotent (per-kind) — chạy 2 lần cùng kết quả
- [ ] RowVersion / atomic SaveChanges (If-Match + Idem-Key) — không nới lỏng
- [ ] EF migration theo CLAUDE.md §4 (isolated /tmp DB, KHÔNG `ef migrations remove`, type-affinity strip)

## 3 · Map Process → QC Line — bảng `ProcessLineMap` (DỮ LIỆU, sửa qua seed `ProcessLineMapSeed`; xem `/api/v2/qc/library/process-map`)
| QC Line | Process / Work Centre | Ghi chú |
|---|---|---|
| **LABEL** (in nhãn) | Flexo (Gallus/Brotech) · Letterpress | In có khuôn |
| **DIGITAL** (in số) | **HP Indigo 6800** · Zebra *(thermal/variable — xác nhận)* | In KHÔNG khuôn. **Đã có 15 mục trong `IPQC_Library_CMES_v2`** (DGT-*): banding/sọc · dropout/vạch trắng · ghosting/bóng ma · trôi màu ΔE lot-to-lot · bám ElectroInk · seri biến đổi không trùng |
| **SILK** (in lụa) | SS (Sheet/R2R/Auto) · SheetCut (SS) | |
| **PRESS_CNC** (dập/cắt) | FB · Power press · RDC · CNC · Laser · Punching · Drill | |
| *(LABEL appearance)* | Laminate · Slit · Magic (ép dán/xẻ) | Dùng bộ appearance LABEL — giữ item bong/phồng A7 |
| *(không sinh IPQC item)* | Pre-press · Ink Mixing · Oven/UV drying · Manual · AOI | Đánh dấu rõ; AOI là kiểm tự động riêng |
| *(unmapped)* | — | Code lạ → log `unmapped process <code>` + hỏi người duyệt, KHÔNG đoán |

## 4 · Tiến độ 7 bước
| Bước | Nội dung | Ưu tiên | Trạng thái | Nhánh/PR | Test | Lesson | Skill |
|---|---|---|---|---|---|---|---|
| 0 | Orientation (đọc kế hoạch) | P0 | ✅ duyệt — [00-orientation.md](00-orientation.md) | feat/phuong-an-C | — | — | — |
| 1 | Mô hình + import THƯ VIỆN LỖI | P0 | ✅ | feat/phuong-an-C | 1000✓ | [LL-01](../lessons-learned/01-check-item-library-import.md) | [cmes-defect-library-import](../../.claude/skills/cmes-defect-library-import/SKILL.md) |
| 2 | Refactor IPQC → data-driven *(shadow table)* | P0 ⚠ | ✅ | feat/phuong-an-C | +26 unit | [LL-02](../lessons-learned/02-ipqc-data-driven-autosync.md) | LL-02 |
| 3 | Resolver Routine → Process | P0 | ✅ | feat/phuong-an-C | +26 unit | LL-02 | LL-02 |
| 4 | Auto-sync materialize vào WO check (+UI items) | P0 | ✅ | feat/phuong-an-C | +5 int +2 bUnit | LL-02 | LL-02 |
| 5 | ReasonCode scope theo process/line | P1 | ✅ | feat/phuong-an-C | (trong B6 tests) | LL-02 | LL-02 |
| 6 | Admin endpoint + trang xem thư viện | P1 | ✅ | feat/phuong-an-C | +5 API | LL-02 | LL-02 |
| 7 | Checkpoint theo từng Operation | P2 | ⬜ **bỏ** (không cần — process-line scoping đã đủ) | — | — | — | — |

> **GATE A ✅ (live API :5100, 4 mã 8064):** LABEL 80644935→61 item · DIGITAL 80645392→42 ·
> SILK 80640044→52 · CUT 80640002→61. Idempotent re-GET, freeze snapshot, dual-sig + state-machine giữ nguyên.
> **GATE B ✅:** B9 dropdown scope (LABEL,PRESS_CNC=24 mã ≠ SILK=14 mã) · B10 no-retro (WO cũ 61→61, WO mới 60).

## 5 · Nghiệm thu (chi tiết ở [acceptance.md](acceptance.md) — tạo sau Bước 4)
**GATE A — sau Bước 4 (lõi P0):** *(dùng mã 8064xxxx thay 20000000C — cùng họ part)*
- [x] A1 Tạo WO cho mã 8064 (39-42: LABEL/DIGITAL/SILK/CUT)
- [x] A2 Resolver suy đúng line (LABEL,PRESS_CNC / DIGITAL,PRESS_CNC / SILK,PRESS_CNC) + FQC/OQC flags
- [x] A3 Tự nạp đúng bộ item theo line (không mặc định, không nhập tay) — 61/42/52/61
- [x] A4 Mã in lụa → nạp bộ SILK (25) + PRESS_CNC; không lẫn DIGITAL
- [x] A4' Mã HP Indigo → nạp bộ DIGITAL (15, gồm banding/ghosting…)
- [x] A5 Freeze snapshot đúng (17963 ký tự, không hồi tố)
- [x] A6 Dual-sig IPQC + 3-sig OQC giữ nguyên (parity tests, không đụng controller)
- [x] A7 State-machine không đổi (IpqcLegacyParityTests 6 xanh)
- [x] A8 Idempotent (re-GET ×3 ổn định 61; DB đúng 61 rows)

**GATE B — sau Bước 5–6 (P1):**
- [x] B9 Dropdown scope theo line (LABEL,PRESS_CNC=24 ≠ SILK=14); mã non-Scrap → 422 (sẵn có)
- [x] B10 Admin sửa thư viện → WO mới nhận bản mới (60); WO cũ giữ snapshot (61, không hồi tố)

## 6 · Lesson Learned & Skills — THEO QUY ƯỚC REPO (CLAUDE.md §Pre-flight)
> KHÔNG để prose rời. Mỗi lesson PHẢI có cột `Cơ chế chặn tái phát` (test/rule fail CI), nếu trống → PR reject.
- **Lesson**: append vào [`CCL-MES-Hybrid/docs/LESSONS-LEARNED.md`](../../CCL-MES-Hybrid/docs/LESSONS-LEARNED.md) (format `Triệu chứng | Root cause | Fix | Cơ chế chặn`). Bản tóm tắt theo bước có thể để `docs/lessons-learned/NN-slug.md` rồi link sang.
- **Skill**: cập nhật [`CCL-MES-Hybrid/docs/SKILLS.md`](../../CCL-MES-Hybrid/docs/SKILLS.md) (playbook) — ưu tiên hơn tạo skill rời; nếu tạo skill tái dùng thì đặt ở `.claude/skills/<ten>/SKILL.md` và link 2 chiều.

| Bước | Lesson (link) | Skill (link) |
|---|---|---|
| 1 | [LL-01 check-item-library-import](../lessons-learned/01-check-item-library-import.md) + [L25](../../CCL-MES-Hybrid/docs/LESSONS-LEARNED.md#l25) | [cmes-defect-library-import](../../.claude/skills/cmes-defect-library-import/SKILL.md) |
| 2 | [LL-02 ipqc-data-driven-autosync](../lessons-learned/02-ipqc-data-driven-autosync.md) + [L25](../../CCL-MES-Hybrid/docs/LESSONS-LEARNED.md#l25) | [S13](../../CCL-MES-Hybrid/docs/SKILLS.md#s13) |
| 3 | [LL-02](../lessons-learned/02-ipqc-data-driven-autosync.md) | [S13](../../CCL-MES-Hybrid/docs/SKILLS.md#s13) |
| 4 | [LL-02](../lessons-learned/02-ipqc-data-driven-autosync.md) | [S13](../../CCL-MES-Hybrid/docs/SKILLS.md#s13) |
| 5 | [LL-02](../lessons-learned/02-ipqc-data-driven-autosync.md) | [S13](../../CCL-MES-Hybrid/docs/SKILLS.md#s13) |
| 6 | [LL-02](../lessons-learned/02-ipqc-data-driven-autosync.md) | [S13](../../CCL-MES-Hybrid/docs/SKILLS.md#s13) |

### Quy tắc mỗi bước (A–F)
A. Code bám kế hoạch + quy ước repo · B. Test xanh + migration up/down (isolated /tmp DB) + seed idempotent ·
C. Lesson learned (có cơ chế chặn) · D. Skill (SKILLS.md / skill-creator) · E. Cập nhật INDEX này · F. DỪNG, báo cáo, chờ duyệt.
