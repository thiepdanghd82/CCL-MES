# LL-PAC-02 — IPQC data-driven + resolver + auto-sync (Phương án C · B2-B6)

> Tóm tắt cho người sau. Code: nhánh `feat/phuong-an-C`. Tiền đề: [[LL-PAC-01]] (thư viện B1).

## Bối cảnh & mục tiêu
Biến IPQC từ **4 slot cứng** (Material/PrintA/PrintB/PrintC) → **data-driven N item**
tự nạp theo routing của mã hàng, dùng thư viện B1. Lõi auto-sync:
```
WO → Product.ProductCode → RoutingOperations(PartNo) → QcLineResolver → {LABEL/DIGITAL/SILK/PRESS_CNC}
   → CheckItemLibrary(line, IPQC) → materialize WoIpqcCheckItem + FREEZE snapshot
```

## Quyết định thiết kế (+ lý do)
- **Shadow table `WoIpqcCheckItem`** (quyết định #2) — GIỮ 4 slot cũ nguyên vẹn (legacy
  parity), thêm bảng con + 2 cột `ItemsProfileSnapshotJson`/`ResolvedLines` trên WoIpqcChecks.
  Rollup `IpqcReadinessRollup.Compute(check, items)` ƯU TIÊN items khi có, LÙI 4-slot khi rỗng.
- **Resolver thuần `QcLineResolver`** — suy process line từ **Operation Description + tiền tố
  WorkCenter**, KHÔNG dùng `WorkCenter.Area` (auto-derive trong import sai: Power press PPSC1
  bị gán "SILKSCREEN") và KHÔNG dùng `RoutingType` (toàn "Manufacturing").
- **Auto-sync = lazy materialize** trong `IpqcReviewController.Get` (mirror WoQcReviewController
  FQC/OQC). No-op (giữ legacy) khi không có routing/library → mọi WO cũ + test cũ không đổi.
- **Freeze** `ItemsProfileSnapshotJson` lúc materialize → sửa thư viện KHÔNG hồi tố (GATE B10).
- **B5 scope mã lỗi** = lọc Scrap catalog về DefectCode của line (client từ items + endpoint
  `/check-item-library/reason-codes?lines=`). Giữ ReasonCode dùng chung (quyết định #4), chỉ scope hiển thị.

## Cạm bẫy đã gặp & cách sửa
- **`dotnet ef migrations add --no-build` sinh migration RỖNG** vì assembly Web cũ chưa có entity
  mới → snapshot diff = 0. Sửa: bỏ `--no-build` (hoặc build startup project trước). Luôn kiểm
  `wc -l migration.cs` + `grep -c 'type:'` > 0 trước khi strip.
- **BSD sed (macOS) không hỗ trợ `\|` alternation** → lệnh strip type-affinity multi-line âm thầm
  không match. `type: "TEXT",` đứng riêng dòng (AddColumn multi-line) cần `sed '/^[[:space:]]*type: "TEXT",$/d'`.
- **Soak test flaky KHÔNG phải regression**: `Concurrent_run_qty_add_N_equals_10` fail 4-8 winners.
  Đã CHỨNG MINH bằng git stash chạy baseline 4x → cũng flaky (2 pass/2 fail). Đúng flake
  SQLite-macOS CLAUDE.md ghi (`Category=Soak`, chạy riêng 2-attempt). → **Bài học: nghi regression
  thì stash + chạy baseline nhiều lần trước khi kết luận.**
- **SQL seed WO demo dùng `CurrentStep='OpIpqc'`** (không phải enum hợp lệ) → GET 500
  "Cannot convert 'OpIpqc' to ProcessStepCode". Giá trị đúng: `IpqcApproval`.
- **Hybrid API KHÔNG auto-migrate** — chỉ WARN "DATABASE HAS UNAPPLIED MIGRATIONS" + boot.
  Phải `dotnet ef database update` thủ công (Phase C) HOẶC boot legacy Web (auto-migrate).

## Ranh giới đã giữ (verify)
- State-machine 12 phase: KHÔNG đụng (IpqcLegacyParityTests 6 xanh).
- Dual-sig IPQC + 3-sig OQC: KHÔNG đụng (controller dual-sig giữ nguyên).
- Freeze snapshot: GATE B10 live — WO cũ 61 item trước/sau khi sửa thư viện; WO mới nhận 60.
- Additive: WO không routing/library → legacy 4-slot (toàn bộ test cũ xanh).

## Cơ chế chặn tái phát (test — fail CI nếu vi phạm)
- `tests/CCL.MES.Tests/Unit/QcLineResolverTests.cs` (26) — khóa phân loại = routing THẬT 8064.
- `tests/CCL.MES.Tests/Unit/IpqcDataDrivenTests.cs` — rollup items-aware + parity fallback + materializer.
- `tests/CCL.MES.Tests/Integration/IpqcAutoSyncTests.cs` (5) — end-to-end resolve→materialize→freeze.
- `tests/CCL.MES.Api.Tests/CheckItemLibraryControllerTests.cs` (5) — list/lines/scoped-reason (GATE B9).
- `tests/CCL.MES.Hybrid.Razor.Tests/IpqcDashboardItemsTests.cs` (2) — items-mode UI + item PUT.

## Verify (output thật)
```
GATE A live (API :5100): LABEL 80644935→61 item · DIGITAL 80645392→42 · SILK 80640044→52 · CUT 80640002→61
  re-GET ×3 ổn định 61 (idempotent) · snapshot 17963 ký tự (freeze)
GATE B9: lines=LABEL,PRESS_CNC→24 mã · lines=SILK→14 mã (scope khác nhau)
GATE B10: WO cũ 61→61 sau sửa thư viện · WO mới 60 (no retro)
Suite: legacy 1000 · API 428(excl soak) · Client 594 · Razor 155 — 0 regression
```

## File chạm
- `src/CCL.MES.Application/Services/QcLineResolver.cs` · `IpqcLibraryMaterializer.cs` (new)
- `src/CCL.MES.Domain/Entities/IpqcChecks.cs` (+WoIpqcCheckItem) · `StateMachine/IpqcReadinessRollup.cs` (items overload)
- `src/CCL.MES.Infrastructure/Migrations/*_AddIpqcCheckItems.cs` (new, 0 type)
- `CCL-MES-Hybrid/.../IpqcReviewController.cs` (auto-sync materialize + item PUT)
- `CCL-MES-Hybrid/.../CheckItemLibraryController.cs` (new — B5/B6 read+scope)
- `CCL-MES-Hybrid/.../Shared/IpqcDashboard.razor` (items-mode) · `Pages/QcLibrary.razor` (new)
