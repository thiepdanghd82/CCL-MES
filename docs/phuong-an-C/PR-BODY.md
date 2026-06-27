# Phương án C — QC engine data-driven + auto-sync theo Routine

> Nhánh `feat/phuong-an-C` · 9 commit (`7bda3fd..36a5ee2`) · 53 files, +15004/−47.
> **Chưa merge** — chờ duyệt (có thể ultrareview trước).

## Tóm tắt
Biến QC từ checklist cứng → **data-driven, tự nạp đúng bộ hạng mục theo routing của mã hàng**:
khi WO vào phase IPQC, hệ thống đọc RoutingOperations → suy tập **process line** (qua bảng map
DỮ LIỆU) → materialize đúng subset **thư viện hạng mục kiểm** vào check + **đóng băng snapshot**
(sửa thư viện KHÔNG hồi tố WO đang chạy).

### Bước B1–B6 (lõi)
- **B1** Thư viện `CheckItemLibrary` + import idempotent từ CSV (`tools/import_qc_library.py`) + mở rộng `ReasonCode`.
- **B2** IPQC **data-driven shadow table** `WoIpqcCheckItem` (GIỮ 4 slot legacy + parity test); rollup ưu tiên items, lùi 4-slot.
- **B3** `QcLineResolver` (thuần) Routine → process line.
- **B4** Auto-sync lazy-materialize + FREEZE `ItemsProfileSnapshotJson` vào `IpqcReviewController` + UI render N item.
- **B5** Scope mã lỗi NG theo process line (endpoint + dropdown).
- **B6** `CheckItemLibraryController` (list/lines/scoped-reason/process-map) + trang `/qc/library`.

### Review-fix (sau /code-review 8 finding)
- **F1** Chặn slot-PUT legacy khi WO đã materialize items → 422 `ipqc.slot_write_in_item_mode`.
- **F2** Self-heal materialize + cờ `autoSyncStatus` (Materialized/SkippedUnmapped/SkippedNoLibrary/LegacyManual) — KHÔNG im lặng; không đè dữ liệu operator.
- **F4** CSV strict: bỏ hàng lỗi (<19 cột / rỗng field bắt buộc) + log; importer exit non-zero.
- **F5** Auth `NpiRead` cho library/process-map (thay `[Authorize]` trần).
- **F6** Map process→line **DATA-DRIVEN** bảng `ProcessLineMap` (gỡ keyword hardcode; đóng finding #3+#7).

### Q1/Q2 (chốt map theo QC thật) + line thứ 5
- **QĐ#6 (Q1)** SheetCut(SS) là công đoạn CẮT → **PRESS_CNC** (không phải in lụa).
- **QĐ#7 (Q2)** Cán/ép dán/xẻ → line **FINISHING** (5 item FIN-*); KHÔNG dội bộ print lên op cán.
  Thư viện nâng lên **v3 (106 item / 5 line)**. FINISHING = string token → **0 migration**.

## Decision Log (đã chốt)
| # | Quyết định |
|---|---|
| 1 | Cho phép sửa legacy (additive + migration up/down + parity test) |
| 2 | IPQC refactor = **shadow table** (giữ 4 slot + thêm item table) |
| 3 | FQC/OQC luôn có (không bỏ cổng hàng xuất) |
| 4 | Bảng mới `CheckItemLibrary`; mở rộng `ReasonCode` (không tạo DefectCode table) |
| 5 | Map process→line là **DỮ LIỆU** (`ProcessLineMap`), sửa qua seed |
| 6 | **SheetCut(SS) → PRESS_CNC** (cắt, không in lụa) |
| 7 | **Laminate/Slit/Magic → line FINISHING** (5 item), không dội print |

## Nghiệm thu (GATE A/B — live, mã 8064)
| Mã | line | items | giải thích |
|---|---|---|---|
| 80644935 LABEL | LABEL,PRESS_CNC | **61** | flexo + RDC cut |
| 80645392 DIGITAL | DIGITAL,PRESS_CNC | **42** | Indigo + cut (SheetCut→PRESS_CNC, QĐ#6) |
| 80640044 SILK | SILK,PRESS_CNC,FINISHING | **57** | in lụa + cut + cán (QĐ#7) |
| 80640002 CUT | PRESS_CNC,FINISHING | **32** | không op in; FB-cut + laminate |
| (NGF1) unmapped | — | **0** | `autoSyncStatus=SkippedUnmapped` (loud) |

- **GATE A** A1–A8 ✅ (resolver/auto-load/SILK≠DIGITAL/freeze/idempotent/dual-sig+state-machine giữ nguyên).
- **GATE B** B9 (dropdown scope theo line; mã sai→422) ✅ · B10 (WO mới nhận bản mới, WO cũ giữ snapshot) ✅.
- Chi tiết + output: `docs/phuong-an-C/acceptance.md`.

## Test (0 regression)
`legacy 1010 · API 437 (excl soak) · Client 594 · Razor 155`.
**Soak flake** `Concurrent_run_qty_add_N_equals_10` là **pre-existing** (đã chứng minh bằng git-stash chạy baseline 4× cũng flaky — CLAUDE.md L25; `Category=Soak`, chạy riêng 2-attempt). KHÔNG do PR này.

## Migration + backup
- 3 migration (`AddCheckItemLibrary`, `AddIpqcCheckItems`, `AddProcessLineMap`) — 0 type-affinity, up/down sạch, **đã áp live** (Phase C).
- FINISHING (Q2) KHÔNG cần migration (QcLine/ProcessLine lưu string).
- Backup live: `data/Backup/SQLite/ccl_mes.db.before-{ipqcitems,plmap,plmap-seed,q1q2-reseed}.*`.

## Backlog (hoãn — `docs/phuong-an-C/BACKLOG.md`)
- **B-#4** tổng quát hoá IPQC vào WoQcCheck (gỡ trùng materialize/freeze/ValidateNg).
- **B-#5** gộp ValidateNg 1 helper.
- **B-#DR1** ProcessLineMapSeed upsert (no sync-delete) → orphan khi rename key (đã dọn tay R2S; cần prune có bảo vệ).

## Lessons / Skills
`CCL-MES-Hybrid/docs/LESSONS-LEARNED.md` +L25/L26/L27 · `SKILLS.md` +S13 · `docs/lessons-learned/01,02` · skill `.claude/skills/cmes-defect-library-import`.

## Vùng cấm
`git diff --name-only | grep -E "^CMES/|SpecHub|_archive"` → rỗng.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
