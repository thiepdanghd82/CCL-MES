# Implementation Plan: Thư viện tiêu chuẩn kiểm tra NVL cho IQC (P12)

**Branch**: `feat/p12-iqc-check-standard-library` | **Date**: 2026-09-03 (hồi cứu) | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/012-iqc-check-standard-library/spec.md`

**Trạng thái** (cập nhật 2026-09-03 sau `/speckit-analyze`): Code đã có trên nhánh. Plan này ghi lại kiến trúc ĐÃ chọn và phần còn mở. Pha VERIFY đã qua (4 suite xanh + gate 19/19 + wire thật); 3 migration đã áp live (nhật ký: `CCL-MES-Hybrid/docs/p12-migration-log.md`); vùng cấm đã giải quyết bằng hiến pháp v1.1.0.

## Summary

Thay hạng mục IQC văn bản tự do bằng thư viện 3 bảng (hạng mục chuẩn hoá ·
spec theo nguyên liệu · dòng tiêu chuẩn chi tiết), resolve bằng `MotherCode`,
đóng băng song ngữ vào phiếu lúc mở, lùi về ma trận 13 hạng mục có đánh dấu khi
mã chưa có spec, cho Engineer+ soạn tiêu chuẩn theo mã (xoá mềm, audit), và
chốt phiếu chỉ khi đã kiểm hết. Tái dùng nguyên khuôn materialize/đóng băng/UI
tab-nhóm của FQC/OQC (L60 · L62 · L63).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: EF Core (SQLite mặc định, SQL Server phải provider-agnostic) · Blazor (Web legacy + MAUI Blazor Hybrid) · xUnit · bUnit

**Storage**: SQLite `data/ccl_mes.db` (dev) — 3 migration mới: `20260828091742_AddIqcCheckStandardLibrary` · `20260828095900_AddIqcDefaultMatrixColumns` · `20260828100725_AddIqcResultDetailFrozenColumns`

**Testing**: `tests/CCL.MES.Tests` (Unit + Integration, xUnit) · `CCL-MES-Hybrid/tests/CCL.MES.Api.Tests` · `CCL-MES-Hybrid/tests/CCL.MES.Hybrid.Razor.Tests` (bUnit) · 19 gate tĩnh `CCL-MES-Hybrid/scripts/gate-all.sh`

**Target Platform**: Windows/macOS desktop (MAUI Hybrid, shop-floor offline) + API self-host

**Project Type**: Web/API + Hybrid client, mô hình Clean Architecture (Domain → Application → Infrastructure → Api/Web)

**Performance Goals**: Materialize ≤ 21 hạng mục/phiếu — không có yêu cầu hiệu năng riêng; import 5 974 dòng một lần lúc seed.

**Constraints**: Migration strip `type:` và `.HasColumnType(...)` (provider-agnostic) · controller mỏng (`gate-thin-controller`) · i18n parity EN/VI · không hard-code hex/cỡ chữ · vùng chạm `--d-tap` · ảnh chụp 768 px cho PR chạm `.razor`

**Scale/Scope**: 21 hạng mục · **459** spec · **5 961** dòng chi tiết (sau lọc 1 spec template + 13 dòng của nó; file master gốc 460 / 5 974) · 946 mother code (356 có spec) · 7 bản ghi `IqcResultDetail` cũ phải giữ nguyên

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Nguyên tắc / ràng buộc | Trạng thái | Bằng chứng |
|---|---|---|
| I. Bằng chứng, không phải lời khẳng định | ✅ | Gate 19/19 PASS. `dotnet test` đã chạy 2026-09-03 trên máy có SDK: **legacy 1337 · Api 903 · Client 731 · Razor 533 — 0 fail**. Verify wire thật trên `:5100` cho cả ba đường (materialize · chấm/chốt · soạn spec) — xem PR body. |
| II. Mọi bài học có cơ chế chặn tái phát | ✅ | L61/L63 → hạng mục lấy từ thư viện, khoá bằng `IqcTicketSectionTests` (7/13) và `IqcCheckResolverTests`; L64 → test đường màn hình THẬT gọi endpoint (`IqcModuleTests`). |
| III. Ratchet chỉ đi xuống | ✅ | Không bump BASELINE gate nào trong 8 commit. |
| IV. Dữ liệu sản xuất là BẰNG CHỨNG | ✅ | Đóng băng song ngữ vào `IqcResultDetail` (test `Dong_bang_ca_hai_ngon_ngu_va_nhom`, `Xoa_hang_muc_KHONG_dung_toi_phieu_da_mo`); xoá mềm; 6 audit action mới. |
| V. Bàn tay người đứng máy quyết định UI | ✅ | Tái dùng khuôn `ipqc-*`, `ConfirmToggle`, tab nhóm một tầng; nút `＋ Thêm hạng mục` theo contract `cmes-add-new-inline` hình dạng 2. |
| Stack — migration provider-agnostic | ✅ | grep 2026-09-03: **0 hit** `type:` / `.HasColumnType(` trên cả 3 file migration `.cs` (T011). |
| Controller mỏng | ✅ | `IqcSpecController` (138 dòng) / `IqcController` (+99) gọi `IqcSpecEditService` (383) / `IqcService`; gate `thin-controller` PASS. |
| i18n parity | ✅ | Mọi nhãn/tiêu chuẩn/phương pháp có cặp Vi/En; gate `i18n-parity` PASS. |
| **Vùng cấm** | ✅ **ĐÃ GIẢI QUYẾT** — hiến pháp **v1.1.0** (2026-09-03) thu hẹp vùng cấm về `src/CCL.MES.Web`. | Đo trên `main` 60 commit: `Web` **0** lần đổi · `Domain` 12 · `Application` 17 · `Infrastructure` 14 → câu chữ cũ xếp 8/60 commit vào STOP-gate mà không commit nào chạm thứ luật muốn bảo vệ. P12 chạm `Web` đúng **1 dòng** (`Iqc.razor` hiển thị 3 trạng thái) — vẫn cần ghi nhận trong PR. |
| STOP-gate: migration lên live DB | ✅ ĐÃ ÁP 2026-08-28 | Cả 3 migration đã trên `data/ccl_mes.db` (`__EFMigrationsHistory`). Phase A→B→C + rowcount trước=sau + `integrity_check=ok`: **`CCL-MES-Hybrid/docs/p12-migration-log.md`**. ⚠ Backup Phase A để ở `/tmp` đã mất khi `/tmp` bị dọn — đã chụp mốc gốc mới vào `data/Backup/SQLite/` và ghi lesson. |
| Quy trình 6 pha | ⚠ | ANALYZE → SELECT → EXECUTE → AUDIT → **VERIFY đã qua** (4 suite + gate + wire thật). **LEARN còn mở**: lesson card mới (T044) chưa viết xong. |

## Project Structure

### Documentation (this feature)

```text
specs/012-iqc-check-standard-library/
├── spec.md              # hồi cứu từ p12-iqc-library-scope-proposal.md
├── plan.md              # file này
└── tasks.md             # trạng thái thật của từng việc
```

Tài liệu gốc (không sao chép, tham chiếu): `CCL-MES-Hybrid/docs/p12-iqc-library-scope-proposal.md` (§1–§10, D1–D4, Q1–Q4) · `docs/RA-SOAT-2026-09-01.md` · `CCL-MES-Hybrid/docs/LESSONS-LEARNED.md` (L60–L64).

### Source Code (repository root)

```text
src/CCL.MES.Domain/
├── Entities/IqcLibrary.cs            # IqcCheckItemLibrary · IqcMaterialSpec · IqcSpecItem
├── Entities/Iqc.cs                   # IqcResultDetail: Pass nullable + 14 cột đóng băng
└── Audit/AuditAction.cs              # 6 hằng IQC_* mới

src/CCL.MES.Application/
├── IMesDbContext.cs                  # 3 DbSet mới
├── DependencyInjection.cs
└── Services/
    ├── IqcCheckResolver.cs           # MotherCode → SpecNo → items | ma trận 13
    ├── IqcTicketSection.cs           # chia hạng mục vào mục 1/2/3 theo MÃ
    ├── IqcService.cs                 # materialize lúc mở phiếu · chấm · chốt
    ├── IqcSpecEditService.cs         # soạn theo mã: tạo spec cục bộ · thêm · xoá mềm · restore · RBAC
    ├── IqcLibraryCsv.cs              # parser CSV master
    └── MaterialLotScanService.cs     # trùng lô → 409

src/CCL.MES.Infrastructure/
├── MesDbContext.cs                   # cấu hình 3 bảng, unique (SpecNo,ItemId,Seq)
├── DbSeeder.cs                       # seed idempotent + probe [seed] iqc_library …
└── Migrations/2026082809…/2026082810…  # 3 migration

src/CCL.MES.Web/Pages/QcQa/Iqc.razor  # 1 dòng

CCL-MES-Hybrid/src/
├── CCL.MES.Api/Controllers/IqcController.cs      # GET items · PUT item · POST complete (QcEdit)
├── CCL.MES.Api/Controllers/IqcSpecController.cs  # GET {materialCode} · POST items · DELETE · restore (IqcSpecRead/Write)
├── CCL.MES.Shared/Quality/IqcDtos.cs
└── CCL.MES.Hybrid.Razor/Shared/Iqc/
    ├── IqcCheckItemGrid.razor        # bảng # · ITEM · METHOD · SPEC · VERDICT
    ├── IqcSpecEditor.razor           # ＋ Thêm hạng mục (Engineer+)
    └── MaterialsInspectionForm.razor # mục 2 / mục 3 dùng grid

tests/CCL.MES.Tests/
├── Unit/IqcCheckResolverTests.cs · IqcTicketSectionTests.cs · IqcLibraryCsvTests.cs
└── Integration/IqcTicketMaterializeTests.cs · IqcTicketItemsTests.cs · IqcSpecEditTests.cs · IqcLibrarySeederTests.cs

CCL-MES-Hybrid/tests/
├── CCL.MES.Api.Tests/IqcSpecControllerTests.cs · IqcTicketTests.cs
└── CCL.MES.Hybrid.Razor.Tests/IqcCheckItemGridTests.cs · IqcSpecEditorTests.cs · IqcModuleTests.cs · _Support/RecordingApi.cs
```

**Structure Decision**: Giữ đúng phân tầng hiện có — luật nghiệp vụ ở
`Application/Services`, controller Hybrid API mỏng, UI Hybrid Razor tái dùng
khuôn FQC/OQC. Không tạo project mới.

## Thiết kế chính (đã hiện thực)

1. **Ba bảng riêng, cùng hình dạng `CheckItemLibrary`** (D2) — khoá scope là nguyên liệu, tiêu chuẩn nằm ở dòng chi tiết.
2. **Resolver tra thẳng** `RawMaterials.MotherCode` = `IqcMaterialSpec.MaterialCode` (case-insensitive, trim). `MaterialCodeIfs` KHÔNG dùng (đo live: 0 khớp).
3. **Ma trận tiêu chuẩn** (D3): `IqcCheckItemLibrary.InDefaultMatrix` + `DefaultAcceptance/Method` → 13 hạng mục khi mã chưa có spec; cờ `FromDefaultMatrix` trên bản ghi phiếu.
4. **Đóng băng** (Nguyên tắc IV): 14 cột trên `IqcResultDetail`; `Pass` nullable để có trạng thái CHƯA KIỂM; `ItemName` cũ giữ nguyên.
5. **Placeholder `XXX`**: đánh dấu chưa xác định; cấm ĐẠT, cho KHÔNG ĐẠT.
6. **Soạn theo mã** (D4): spec cục bộ cho mã chưa có spec (không dùng dải `CCL-SPEC-QCxxx`), `Seq` tăng khi trùng ItemId, xoá mềm + restore, policy `IqcSpecWrite` = Engineer+.
7. **Chốt phiếu**: từ chối khi còn `Pass = null`; phiếu cũ không có hạng mục vẫn chốt; kết luận lô suy từ hạng mục.
8. **Seed idempotent** (DR-1): upsert theo natural key, không xoá, không hồi sinh dòng `Active=false`, probe `[seed] iqc_library items= specs= spec_items= skipped=`.
9. **Audit**: `IQC_ITEM_SET · IQC_COMPLETE · IQC_SPEC_CREATED · IQC_SPEC_ITEM_ADDED · IQC_SPEC_ITEM_DEACTIVATED · IQC_SPEC_ITEM_REACTIVATED`.

## Complexity Tracking

> Chỉ điền khi Constitution Check có vi phạm cần biện minh.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| ~~Sửa `src/CCL.MES.{Domain,Application,Infrastructure}`~~ — **KHÔNG CÒN LÀ VI PHẠM** | Henry chọn sửa luật (2026-09-03): hiến pháp **v1.1.0** thu hẹp vùng cấm về `src/CCL.MES.Web`. Ba tầng dùng chung không thể loại khỏi phạm vi khi thêm bảng. | Giữ nguyên câu chữ cũ ⇒ một luật mà 8/60 commit trên `main` vi phạm và không ai dừng lại — luật chết trên thực tế, làm vô hiệu cả năm STOP-gate còn lại. Đây là sửa câu chữ cho khớp ý định (đóng băng app `:5050`), KHÔNG phải nới luật vì bất tiện — xem SYNC IMPACT REPORT đầu hiến pháp. |
| `Pass` từ `bool` → `bool?` trên bảng đang có dữ liệu | Cần trạng thái thứ ba CHƯA KIỂM; mặc định `false` sẽ tuyên bố cả lô NG khi vừa mở phiếu. | Thêm cột `Status` enum riêng ⇒ hai cột cùng nói một chuyện, 7 bản ghi cũ phải backfill; nullable là thay đổi nhỏ nhất, không đụng dữ liệu cũ. |
