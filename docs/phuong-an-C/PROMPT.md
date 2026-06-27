# PROMPT — Triển khai PHƯƠNG ÁN C cho CMES
> Thư viện lỗi IPQC/FQC/OQC + Auto-sync vào Work Order theo Routine (đầy đủ 3 giai đoạn).
> Cách dùng: mở Claude Code tại thư mục `CCL-CMES`, dán TOÀN BỘ khối dưới đây làm prompt đầu phiên.
> Nguồn kế hoạch: `PhuongAn_ThuVienLoi_AutoSync_QC_v1.pptx` · `build_feasibility_deck.js`. Phiên bản: v1 · 2026-06-26.

---

```text
# NHIỆM VỤ: Triển khai PHƯƠNG ÁN C — "QC Engine đầy đủ theo Routine" cho CMES

Bạn là kỹ sư phần mềm MES + chuyên gia QMS. Triển khai Phương án C: thư viện lỗi
IPQC/FQC/OQC + auto-sync vào Work Order theo routine, ĐẦY ĐỦ cả 3 giai đoạn QC.

══════════════════════════════════════════════════════════════════════
BƯỚC 0 — ĐỌC KẾ HOẠCH TRƯỚC (BẮT BUỘC, KHÔNG ĐƯỢC BỎ QUA)
══════════════════════════════════════════════════════════════════════
Trước khi viết bất kỳ dòng code nào, ĐỌC và tóm tắt lại các nguồn sau. Code phải
BÁM SÁT đúng cơ chế, ranh giới và thuật ngữ trong kế hoạch — không tự chế hướng khác:

1. Bản kế hoạch (nguồn sự thật):
   - "PhuongAn_ThuVienLoi_AutoSync_QC_v1.pptx"  → đọc bằng:  python -m markitdown PhuongAn_ThuVienLoi_AutoSync_QC_v1.pptx
   - "build_feasibility_deck.js" (text gốc của deck, đầy đủ nội dung audit + 4 phương án + lộ trình)
2. Thư viện lỗi đã xây: "CCL-MES/IPQC_Library_CMES_v2.xlsx" + ".csv" + "build_ipqc_library.py"
   (101 hạng mục / 4 line: LABEL 34 · DIGITAL 15 · SILK 25 · PRESS_CNC 27)
3. Code anchors trong app (đọc thực tế, không đoán):
   - WorkOrder:        CCL-MES/src/CCL.MES.Domain/Entities/WorkOrder.cs
   - IPQC (4 slot):    CCL-MES/src/CCL.MES.Domain/Entities/IpqcChecks.cs
   - FQC/OQC (data-driven): CCL-MES/src/CCL.MES.Domain/Entities/WoQcChecks.cs
   - QcProfileSeed:    CCL-MES/src/CCL.MES.Application/Services/QcProfileSeed.cs
   - ReasonCode:       CCL-MES/src/CCL.MES.Domain/Entities/Spec.cs
   - Routing/BOM:      CCL-MES/src/CCL.MES.Domain/Entities/Npi.cs
   - DbSeeder:         CCL-MES/src/CCL.MES.Infrastructure/DbSeeder.cs
   - Importer CSV:     CCL-MES/tools/import_npi.py
   - Dữ liệu routine:  Data/RoutingOperations 260525-52014.csv  (process: FLEXO/INDIGO/SCREEN/DIECUT; có bước FQC & Packaging, OQC)
   - Quy ước repo:     CCL-MES/CLAUDE.md

Sản phẩm Bước 0: file "CCL-MES/docs/phuong-an-C/00-orientation.md" — tóm tắt
cơ chế auto-sync (WO→Routing→process→thư viện→nạp vào QC check), liệt kê đúng các
ranh giới phải giữ (state-machine 12 phase, dual-sig IPQC/OQC, freeze ProfileSnapshotJson,
seed idempotent). DỪNG và chờ tôi xác nhận orientation đúng trước khi sang Bước 1.

══════════════════════════════════════════════════════════════════════
PHẠM VI PHƯƠNG ÁN C — CHIA THÀNH 7 BƯỚC
══════════════════════════════════════════════════════════════════════
Bước 1 — Mô hình & import THƯ VIỆN LỖI: bảng CheckItem + DefectCode scope theo
         process (LABEL/SILK/PRESS_CNC/…) và theo mã hàng; importer đọc từ
         IPQC_Library_CMES_v1.xlsx/.csv + mở rộng ReasonCode; seed idempotent.
Bước 2 — Refactor IPQC sang DATA-DRIVEN: chuyển WoIpqcCheck 4-slot → danh sách item
         (theo khuôn WoQcCheckItem). GIỮ NGUYÊN dual-sig + judgment + audit; viết
         migration + lớp tương thích (legacy parity) để dữ liệu cũ không vỡ.
Bước 3 — RESOLVER Routine→Process: từ WO.ProductId/PartNo đọc RoutingOperations →
         suy ra tập process + có FQC/OQC hay không → trả về tập profile cho từng stage.
Bước 4 — AUTO-SYNC: khi WO vào phase tương ứng, materialize đúng bộ check item
         (IPQC/FQC/OQC) từ resolver; tôn trọng freeze ProfileSnapshotJson (sửa thư
         viện KHÔNG đổi check đang chạy).
Bước 5 — ReasonCode SCOPE theo process/sản phẩm + cập nhật validate khi ghi NG.
Bước 6 — ADMIN: endpoint + UI quản lý/import thư viện lỗi & profile (CRUD, versioning).
Bước 7 — (tùy chọn) Checkpoint theo TỪNG OPERATION của routine, nếu Bước 0 xác nhận cần.

══════════════════════════════════════════════════════════════════════
QUY TẮC THỰC THI CHO MỖI BƯỚC (1→7)
══════════════════════════════════════════════════════════════════════
Với MỖI bước, làm tuần tự và KHÔNG sang bước sau khi chưa xong "Definition of Done":

A. Code bám sát kế hoạch + quy ước repo hiện có (đặt tên, layer, pattern atomic
   SaveChanges + If-Match + Idem-Key, audit code). Không phá state-machine/dual-sig.
B. Test: thêm unit + integration test; chạy "dotnet test" xanh. Migration phải
   up/down sạch. Seed phải idempotent (chạy 2 lần ra cùng kết quả).
C. LESSON LEARNED (THEO QUY ƯỚC REPO — đọc CLAUDE.md §Pre-flight TRƯỚC):
   APPEND vào "CCL-MES-Hybrid/docs/LESSONS-LEARNED.md" đúng format card 4 cột
   "Triệu chứng | Root cause | Fix | Cơ chế chặn tái phát". BẮT BUỘC điền cột
   "Cơ chế chặn tái phát" = 1 test/rule fail CI khi invariant bị vi phạm — để
   TRỐNG thì PR bị reject (prose rời KHÔNG ship). Nội dung gồm: bối cảnh bước,
   quyết định + lý do, cạm bẫy đã gặp, ranh giới đã giữ, cách verify, file:line.
   (Tùy chọn: bản tóm tắt theo bước ở "CCL-MES/docs/lessons-learned/<NN>-<slug>.md" rồi link sang.)
D. SELF-LEARNING SKILL (THEO QUY ƯỚC REPO): cập nhật playbook
   "CCL-MES-Hybrid/docs/SKILLS.md" (thêm mục Sxx) đóng gói "cách làm" của bước
   — vd "resolver routing→process", "import thư viện lỗi idempotent", "refactor
   IPQC shadow-table + legacy-parity". Mục skill phải có: khi nào dùng, các bước
   chuẩn, code anchor, checklist verify, lỗi thường gặp. Nếu cần skill tái dùng
   dạng Claude skill thì dùng "skill-creator" tạo ".claude/skills/<ten>/SKILL.md"
   và link 2 chiều với SKILLS.md. Có mục liên quan thì CẬP NHẬT, không tạo trùng.
E. Cập nhật "CCL-MES/docs/phuong-an-C/INDEX.md": trạng thái bước + link lesson + link skill.
F. DỪNG, báo cáo (đã làm gì / test / lesson / skill / rủi ro còn lại) và chờ tôi
   duyệt trước khi sang bước kế tiếp.

══════════════════════════════════════════════════════════════════════
GUARDRAILS
══════════════════════════════════════════════════════════════════════
• Không đổi hành vi state-machine 12 phase, dual-sig IPQC/OQC, freeze ProfileSnapshotJson
  trừ khi kế hoạch yêu cầu — nếu buộc phải đổi, nêu rõ và xin xác nhận.
• Mọi seed/import phải idempotent + có audit (theo mẫu import_npi.py / DbSeeder).
• Ưu tiên tận dụng cơ chế đã có (FQC/OQC data-driven, Product.QcProfileOverride,
  ReasonCode) thay vì viết mới song song.
• Không commit/push nếu tôi chưa yêu cầu; tạo nhánh riêng trước khi sửa.
• Mỗi khẳng định "đã chạy/đã xanh" phải kèm output thật.

══════════════════════════════════════════════════════════════════════
ƯU TIÊN & THỜI LƯỢNG TỪNG BƯỚC (P0 = lõi bắt buộc · P1 = quan trọng · P2 = tùy chọn)
══════════════════════════════════════════════════════════════════════
Bước | Nội dung                         | Ưu tiên | Ước lượng | Phụ thuộc
-----|----------------------------------|---------|-----------|----------------
 0   | Orientation (đọc kế hoạch)        | P0      | 0.5 ngày  | —
 1   | Mô hình + import THƯ VIỆN LỖI      | P0      | 3–4 ngày  | 0
 2   | Refactor IPQC → data-driven        | P0      | 5–7 ngày  | 1   ! rủi ro cao nhất
 3   | Resolver Routine → Process         | P0      | 3–4 ngày  | 1
 4   | Auto-sync materialize vào WO check | P0      | 4–5 ngày  | 2,3
 5   | ReasonCode scope theo process/SP   | P1      | 2–3 ngày  | 1
 6   | Admin UI/endpoint quản lý thư viện | P1      | 5–7 ngày  | 1,4
 7   | Checkpoint theo từng Operation     | P2      | 5–8 ngày  | 4 (chỉ làm nếu Bước 0 chốt CẦN)

Quy tắc ưu tiên:
• Hoàn tất trọn vẹn nhóm P0 (1→4) trước; đây là "lõi auto-sync" — sau Bước 4 phải
  CHẠY ĐƯỢC end-to-end tối thiểu (xem nghiệm thu bên dưới) dù chưa có P1/P2.
• P1 (5,6) làm sau khi P0 xanh. P2 (7) chỉ khi tôi xác nhận.
• Ước lượng là MỐC THAM CHIẾU, không phải cam kết. Đầu mỗi bước, báo lại ước lượng
  thực tế + rủi ro; nếu một bước vượt >30% so mốc, DỪNG xin ý kiến thay vì tự kéo dài.
• Bước 2 là điểm rủi ro: BẮT BUỘC làm trên nhánh riêng, có migration up/down + test
  legacy parity xanh trước khi merge; nếu thấy nguy cơ vỡ dữ liệu cũ → dừng, đề xuất
  phương án giữ song song (shadow table) và xin duyệt.

══════════════════════════════════════════════════════════════════════
NGHIỆM THU TỔNG — END-TO-END DEMO (Definition of Done của Phương án C)
══════════════════════════════════════════════════════════════════════
Coi C "xong" khi vượt qua kịch bản demo sau bằng test tự động (integration) + một
lần chạy thật có log/output đính kèm. Dùng mã hàng thật có đủ chặng, ví dụ
PartNo 20000000C (routing: GALLUS/BROTECH PRINT → FB CUT → FQC & Packaging → OQC):

GATE A (sau Bước 4 — lõi P0):
  1. Tạo WO cho PartNo 20000000C, SL bất kỳ.
  2. Resolver suy ra ĐÚNG tập stage/process: {LABEL (in), PRESS_CNC (cắt), FQC, OQC}.
  3. Khi WO vào từng phase, hệ thống TỰ nạp đúng bộ check item từ thư viện (IPQC theo
     process in+cắt; FQC; OQC) — không phải bộ mặc định toàn cục, không nhập tay.
  4. So một mã CHỈ in lụa (SCREEN) → nạp bộ SILK, KHÔNG nạp bộ cắt. (kiểm tính đúng theo routine)
  5. ProfileSnapshotJson đóng băng: sau khi check đã tạo, sửa thư viện KHÔNG đổi check đang chạy.
  6. Dual-sig IPQC & 3 chữ ký OQC vẫn hoạt động đúng (Inspector≠Reviewer≠Approver).
  7. State-machine 12 phase không đổi hành vi; mọi mutation sinh audit row đúng mã.
  8. Seed/import thư viện idempotent: chạy 2 lần → cùng kết quả.

GATE B (sau Bước 5–6 — P1):
  9. Ghi NG ở 1 item → dropdown mã lỗi CHỈ hiện mã hợp lệ theo process/sản phẩm; mã sai bị 422.
 10. Admin sửa/import thư viện qua UI/endpoint → WO mở MỚI nhận thư viện đã cập nhật
     (WO cũ giữ snapshot — không hồi tố).

ĐẦU RA NGHIỆM THU: "CCL-MES/docs/phuong-an-C/acceptance.md" — bảng tick từng mục
GATE A/B kèm: tên test, lệnh chạy, output thật (dotnet test xanh), ảnh/log demo.
KHÔNG tuyên bố "C hoàn thành" nếu còn bất kỳ mục GATE A nào fail.

BẮT ĐẦU TỪ BƯỚC 0. Sau khi tôi duyệt orientation, tiến hành Bước 1.
```
