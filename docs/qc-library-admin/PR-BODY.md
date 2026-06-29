# QC Library Admin — import Excel + inline add/edit (master data)

Turns `/qc/library` (CheckItemLibrary — 106 item / 5 line, shipped read-only in
v0.10.8) into an admin surface: import from `.xlsx`, and add / edit / hide rows
directly in the grid. Base = `main` (p10.8 / #124 / #125 already merged).

## Phạm vi

- **Import `.xlsx`** (`POST /api/v2/check-item-library/import`) — ClosedXML
  (dep sẵn, không thêm package vuln). **Một service validate dùng chung với CSV**:
  `CheckItemLibraryImporter` (Application) chứa `UpsertAsync` (idempotent theo
  `ItemId` + mở rộng ReasonCode) — `DbSeeder` boot-seed delegate vào nó, nên
  CSV-seed và UI-import đi CHUNG một đường upsert (không divergence). `.xlsx` và
  `.csv` cùng map về một `ParseResult`. Endpoint trả `{parsed, inserted, updated,
  skipped, errors[]}`; chỉ nhận `.xlsx` (type khác → 422); size cap 5 MB.
- **Template** (`GET .../template`) — xuất file mẫu 19 cột (header + 1 dòng mẫu).
- **Thêm / sửa inline** (`POST` / `PUT {id}`) — form trực tiếp trên trang;
  `PUT` mang `RowVersion` (If-Match) → **409 khi stale** (optimistic-concurrency).
- **Soft-delete** (`PATCH {id}/active`) là chính (ẩn khỏi list, `?includeInactive=true`
  để xem); **hard-delete** (`DELETE {id}`) chỉ Admin.
- **Audit** mọi mutation: `CHECK_ITEM_LIBRARY_ADD/EDIT/DEACTIVATE/DELETE/IMPORT`.
- **UI** `QcLibrary.razor`: toolbar Import/Template/Thêm dòng/“Hiện dòng đã ẩn”,
  banner summary (inserted/updated/skipped + lỗi từng dòng), form add/edit, nút
  Sửa / Ẩn-Hiện mỗi dòng. QC = chỉ đọc. Responsive (Design Rules S9).

## Ràng buộc bất biến đã giữ — đều có TEST chặn

- **Freeze** — import/sửa thư viện KHÔNG hồi tố WO đã materialize (snapshot
  `WoIpqcCheck.ItemsProfileSnapshotJson` + `Items` giữ nguyên).
  → `CheckItemLibraryImporterTests.Import_does_not_retro_change_a_materialized_WO_snapshot`.
- **Idempotent** — re-import cùng file = 0 net change.
  → `Import_inserts_then_reimport_is_idempotent`.
- **Validate** — ProcessLine strict (∈ LABEL/DIGITAL/SILK/PRESS_CNC/FINISHING),
  Severity/Group lenient (giữ tương thích dữ liệu v3 “◆ Critical”/“A·…”); dòng
  thiếu cột/required → skipped + errors, KHÔNG seed im lặng.
  → `Import_rejects_invalid_processline_without_silent_seed`,
  `Xlsx_parse_skips_row_missing_required_field`, `Import_rejects_non_xlsx_with_422`.
- **Concurrency** — `RowVersion` string app-managed + `IsConcurrencyToken()`
  (SQLite không có rowversion native, không cần trigger) → 409.
  → `Edit_happy_then_stale_returns_409`.
- **Policy** — write = class `NpiRead` AND method `Roles=Admin,Supervisor,Engineer`
  (QC read-only, operator 403); hard-delete Admin-only.
  → `Operator_cannot_write_add`, `Engineer_can_add`,
  `SoftDelete_hides_from_default_list_but_visible_with_includeInactive`,
  `Add_then_duplicate_returns_422`, `Import_valid_xlsx_inserts_rows`.

## Migration

`20260629090223_AddCheckItemLibraryRowVersion` — additive TEXT column,
type-affinity đã strip (§4.5). Round-trip empty→up→down→up sạch;
`has-pending-model-changes` = no changes (ModelSnapshot khớp).

## Test (tất cả XANH)

`CCL.MES.Tests` **1014** · `CCL.MES.Api.Tests` **448** · `Hybrid.Client.Tests`
**595** · `Hybrid.Razor.Tests` **155** — 0 fail. Mới: importer **4/4** +
CheckItemLibrary controller **16/16**. Build legacy + Hybrid (API/Client/Razor) ✅.

## Docs

`LESSONS-LEARNED.md` **L28** (cơ chế chặn = các test trên) ·
`SKILLS.md` **S14** (master-data admin: one upsert · RowVersion app-managed · freeze guard).

## Known / follow-up

- **Ảnh UI** (import summary + edit form) bổ sung ở phiên MAUI — build maccatalyst
  đang kẹt toolchain (Xcode 26.6 vs .NET MacCatalyst SDK 26.5), **không phải lỗi
  code**; UI đã compile + wire đúng, hợp đồng phủ bởi controller tests.

> Mở để review — KHÔNG merge cho tới khi reviewer xác nhận.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
