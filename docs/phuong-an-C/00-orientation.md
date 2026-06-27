# Phương án C — Orientation (Bước 0)

> Nguồn: `build_feasibility_deck.js` (deck 10 slide) + `PhuongAn_ThuVienLoi_AutoSync_QC_v1.pptx/.pdf`
> + thư viện `IPQC_Library_CMES_v1.xlsx/.csv` + đọc code thật (anchors file:line bên dưới).
> Trạng thái: **chờ duyệt** trước khi sang Bước 1. Không có thay đổi code trong Bước 0 (chỉ tài liệu).

---

## 1. Mục tiêu Phương án C
"QC engine đầy đủ theo Routine": một **thư viện lỗi** dùng chung cho IPQC/FQC/OQC, scope theo
**process** (LABEL / SILK / PRESS_CNC) và mã hàng; khi WO đi qua từng phase, hệ thống **tự nạp đúng
bộ check item** (không nhập tay) dựa trên **routing của mã hàng**. Bao trùm cả 3 giai đoạn QC, kể cả
refactor IPQC từ 4-slot cứng → data-driven.

## 2. Cơ chế Auto-sync (đúng theo deck slide 6)
```
WO (PartNo) → đọc RoutingOperations theo PartNo → suy ra tập PROCESS
            → ghép subset thư viện lỗi cho mỗi stage QC → materialize vào IPQC/FQC/OQC check
```
- **Điểm tựa**: FQC & OQC **đã data-driven** qua `ProfileSnapshotJson` → chỉ cần thêm 1 *resolver*
  chọn profile theo routine là auto-sync chạy được **mà không đụng state-machine**.
- **IPQC còn 4-slot cứng** → muốn auto-sync đầy đủ cho IPQC thì phải refactor (Bước 2 — rủi ro cao nhất).
- Ví dụ mã `20000000C` (đã xác minh trong `Data/RoutingOperations 260525-52014.csv`):
  - Op10 `(GALLUS) PRINT` WC=GFL01 (Flexo) · Op21 `(BROTECH) PRINT FL4` WC=BFL04 (Flexo) → **LABEL**
  - Op30 `(FB) CUT` WC=FBL01 · (bản cũ Op40 `(PRESS) CUT` WC=PPSC1) → **PRESS_CNC**
  - Op `TAPPING` WC=MAN3 (Manual) → công đoạn phụ, không sinh bộ QC riêng
  - → resolver kỳ vọng ra `{LABEL, PRESS_CNC, FQC, OQC}` (đúng Gate A mục 2).

## 3. Hiện trạng code (anchors thật)
| Thành phần | File:line | Kiểu lưu | Ý nghĩa cho C |
|---|---|---|---|
| WorkOrder + 12 phase | `src/CCL.MES.Domain/Entities/WorkOrder.cs:47,55` · `StateMachine/MesPhase.cs:27-97` · `StateMachine/WorkOrderStateMachine.cs:180-284` | `MesPhase` string + ma trận 169 cell | Phase trigger QC: IPQC_WAIT/QA_PENDING (IPQC), FQC_PENDING (FQC), OQC_PENDING (OQC) |
| **IPQC 4-slot** | `src/CCL.MES.Domain/Entities/IpqcChecks.cs:25-96` | **HARDCODE** 4 slot (Material/PrintA/PrintB/PrintC) | Bước 2 refactor → data-driven (giữ dual-sig) |
| FQC/OQC | `src/CCL.MES.Domain/Entities/WoQcChecks.cs:32-181` | **DATA-DRIVEN** (`WoQcCheck.ProfileSnapshotJson` + `WoQcCheckItem`) | Khuôn mẫu để IPQC bám theo + đích auto-sync |
| Profile seed | `src/CCL.MES.Application/Services/QcProfileSeed.cs` | JSON FQC 12 mục / OQC 28 mục | Bước 4 thay default toàn cục bằng profile-theo-routine |
| ReasonCode | `src/CCL.MES.Domain/Entities/Spec.cs:378-387` · enum `Enums.cs:65` | 26 mã (Pause/Scrap/Recovery), **dùng chung, chưa scope** | Bước 1 mở rộng + Bước 5 scope theo process/SP |
| Routing/BOM | `src/CCL.MES.Domain/Entities/Npi.cs` (RoutingOperation, ManufacturingStructure, WorkCenter) | CSV import | Input của resolver (Bước 3) |
| Product override | `src/CCL.MES.Domain/Entities/MasterData.cs:18-36` `Product.QcProfileOverride` (string? JSON) | đã có schema, chưa dùng | Tận dụng cho profile per-SP (Bước 4/6) |
| DbSeeder | `src/CCL.MES.Infrastructure/DbSeeder.cs:253-337` | **idempotent per-kind** (HashSet, không global .Any) | Mẫu seed/import cho thư viện (Bước 1) |
| Importer | `tools/import_npi.py` | DELETE+refill 1 transaction, đếm imported/skipped/failed | Mẫu importer thư viện lỗi |
| Controllers QC (Hybrid) | `CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/{Ipqc,WoQcReview,Prepress}Controller.cs` | atomic SaveChanges + If-Match + Idem-Key + audit | Nơi gọi materialize + validate NG |

## 4. Ranh giới BẮT BUỘC giữ (không đổi trừ khi kế hoạch yêu cầu + xin duyệt)
1. **State-machine 12 phase** — `MesPhase.cs:27-97`, ma trận 169 cell `WorkOrderStateMachine.cs:192-284` (11 RecoveryOnly + RequiresSignoff/Condition). Auto-sync chỉ *materialize check*, **không** thêm/sửa phase hay edge.
2. **Dual-sig IPQC (Q3)** — `IpqcDualSigOptions.cs`: `QaApprovedBy ≠ IpqcSubmittedBy` khi `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on` (default ON, parse typo-safe). Vi phạm → 422 `qa.same_user_as_ipqc_submitter` + audit `WO_QA_APPROVE_DENIED`.
3. **3 chữ ký OQC** — `WoQcSigPolicyOptions.cs`: Inspector≠Reviewer≠Approver (3 cờ độc lập, default ON). Giữ nguyên khi refactor.
4. **Freeze `ProfileSnapshotJson`** — `WoQcChecks.cs:40-51`: snapshot đóng băng lúc tạo check; **sửa thư viện KHÔNG đổi check đang chạy**. Auto-sync phải tôn trọng: chỉ chọn profile *trước khi* materialize; sau đó bất biến.
5. **Seed/import idempotent + audit** — `DbSeeder.cs` per-kind HashSet + `import_npi.py` DELETE+refill. Chạy 2 lần = cùng kết quả.
6. **RowVersion / atomic SaveChanges** — WO giữ `RowVersion` (`WorkOrder.cs:55`); các bảng con (IPQC/QC) **không** có RowVersion, dùng WO-row-touch. Giữ pattern khi thêm bảng/cột.

## 5. Thư viện lỗi v1 (đã xây — đầu vào Bước 1)
`CCL-MES/IPQC_Library_CMES_v1.csv` — **86 item / 3 Line process**: `LABEL` 34 · `PRESS_CNC` 27 · `SILK` 25.
Cột chính: `ItemID, Line(Dòng SP), Group(Nhóm), Code(Mã), Item VI/EN, Acceptance VI/EN, Method·Tool,
Severity, AQL, Sampling, Loại KT, Defect(Mã lỗi), %Pareto, ISO ref`. → Line khớp đúng nhóm process của resolver.

## 6. Resolver Routine → Process (Bước 3) — input thật
`RoutingOperations` **KHÔNG có cột "process"**. Phải suy ra từ `Operation Description` + `Work Centre No/Desc`:
| Tín hiệu trong routing | → Process group | Thư viện |
|---|---|---|
| `PRINT` + WC Flexo/Gallus/Brotech/Letterpress/Indigo/Zebra (GFL/BFL…) | **LABEL** | Line=LABEL |
| `PRINT` lụa: SS Sheet/R2R, SheetCut (Screen) | **SILK** | Line=SILK |
| `CUT`/dập: FB, Power press, RDC, CNC, Laser, Punching (FBL/PPSC…) | **PRESS_CNC** | Line=PRESS_CNC |
| (luôn có ở cuối luồng) | **FQC**, **OQC** | profile FQC/OQC |
*Câu hỏi mở (mục 7-#3): FQC/OQC là operation trong routing hay stage phổ quát.*

## 7. Câu hỏi/Quyết định cần chốt TRƯỚC khi code Bước 1
1. **Xung đột "legacy read-only"**: QC entity, QcProfileSeed, ReasonCode, Routing, DbSeeder đều ở
   `src/CCL.MES.Domain/Application/Infrastructure` — vùng **read-only baseline** theo
   `CCL-MES-Hybrid/README.md`. Phương án C (Bước 1/2/5) **buộc sửa legacy** + migration.
   → Xác nhận: **được phép sửa legacy** (kèm migration up/down + review) cho Plan C? Hay đóng gói trong Hybrid?
2. **IPQC 4-slot là "contract SpecHub §3"** (code-map đánh dấu "không data-driven"). Bước 2 refactor nó.
   → Xác nhận cách: **shadow table** (giữ 4-slot cũ song song bảng item mới + legacy parity) như prompt yêu cầu cho bước rủi ro cao?
3. **FQC/OQC**: luôn có cho mọi WO, hay chỉ khi routing có operation FQC/Packaging/OQC? (quyết định resolver).
4. **Bảng mới CheckItem + DefectCode** (Bước 1) vs **mở rộng** `QcProfileSeed`/`ReasonCode` sẵn có.
   → Khuyến nghị: thêm bảng thư viện (CheckItemLibrary + DefectCode scope process/SP) + giữ ReasonCode làm danh mục mã lỗi; profile materialize từ thư viện. Cần bạn chốt.
5. **Bảng map process** (mục 6): khớp keyword/WorkCenter như trên có đúng thực tế nhà máy không? (Có `ProcessCatalog` 17 code trong DbSeeder — nên dùng làm chuẩn map).

## 8. Mapping 7 bước → file sẽ đụng + rủi ro
| Bước | Đụng chủ yếu | Rủi ro |
|---|---|---|
| 1 Mô hình+import thư viện | Domain (bảng mới) + Infra migration + tools importer + DbSeeder | TB (đụng legacy) |
| 2 Refactor IPQC data-driven | `IpqcChecks.cs` + service + migration + Hybrid IpqcReviewController + tests parity | **CAO** (shadow table, nhánh riêng) |
| 3 Resolver Routine→Process | Application service mới (đọc RoutingOperation) | TB |
| 4 Auto-sync materialize | Hybrid controllers (lazy-materialise) + resolver + freeze | CAO (giữ freeze + state-machine) |
| 5 ReasonCode scope | ReasonCode + validate NG | TB |
| 6 Admin UI/endpoint | Hybrid Api + Razor | TB |
| 7 Checkpoint per-operation | (tùy chọn — chỉ khi chốt CẦN) | — |

## 9. Xác nhận cần từ bạn
Vui lòng duyệt orientation + trả lời 5 câu mục 7 (đặc biệt #1 legacy, #2 shadow-table IPQC, #3 FQC/OQC).
Sau khi duyệt, tôi tiến hành **Bước 1 — Mô hình & import THƯ VIỆN LỖI**.
