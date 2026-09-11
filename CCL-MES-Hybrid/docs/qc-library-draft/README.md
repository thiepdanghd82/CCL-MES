# Dự thảo thư viện hạng mục kiểm IPQC — P3

Trạng thái: **DỰ THẢO, chờ bên chất lượng soát và ký.** Chưa nạp vào DB live
(đã kiểm: `CheckItemLibraries` vẫn 87 dòng, 0 dòng `PNC-`).

| File | Nội dung |
|---|---|
| `press-cnc-ipqc-draft.csv` | 21 hạng mục PRESS_CNC, 33 cột đúng thứ tự sheet `IPQC_FQC_OQC_MAP` |
| `press-cnc-ipqc-draft.xlsx` | cùng nội dung, dạng xlsx — parser thật đã đọc được 21/21 |

## Phạm vi: CHỈ có PRESS_CNC. Vì sao không có FINISHING và DIGITAL

P3 ban đầu là "soạn thư viện cho PRESS_CNC · FINISHING · DIGITAL". Sau khi đo,
tôi chỉ soạn PRESS_CNC — và đó là quyết định có lý do, không phải làm dở.

**PRESS_CNC có nền để soạn.** Sheet `DEFECT_CODES` của `IPQC_Library_CMES_v5.xlsx`
mang **14 mã lỗi PRESS_CNC rút từ 5.229 lượt NG có thật**, kèm số lượt và luỹ kế
phần trăm — tức nhà máy đã biết khâu cắt hỏng theo những kiểu nào và kiểu nào
nhiều. Thêm nữa, thư viện đã có sẵn 10 hạng mục `SET-CU-*` (bước Setting) dùng
đúng từ vựng của khâu cắt: khuôn/dao, độ sâu cắt, canh cắt theo hình in, bóc
lưới thải, layout con/tờ.

**FINISHING và DIGITAL không có gì cả.** Đo được:

| | mã lỗi trong DEFECT_CODES | lịch sử NG | từ vựng Setting |
|---|---|---|---|
| PRESS_CNC | **14** (5.229 lượt) | có | 10 mục `SET-CU-*` |
| FINISHING | **0** | không | không |
| DIGITAL | **0** | không | không |

Soạn một bộ tiêu chí đầy đủ cho hai dòng này từ kiến thức quy trình chung, không
có một lượt NG nào của chính nhà máy làm chứng, chính là thứ tôi đã cảnh báo
trong tờ trình: **spec soạn cho có thì tệ hơn không có spec, vì nó mang chữ ký
của người duyệt.** Tôi không làm việc đó.

Đường đúng cho hai dòng ấy: bên chất lượng gom danh sách dạng lỗi thực tế ở khâu
cán màng / xẻ cuộn và khâu in digital (kể cả bằng tay, vài ca), rồi tôi dựng
thư viện từ danh sách đó theo đúng cách vừa làm cho PRESS_CNC.

## PRESS_CNC — cách dự thảo được dựng

**Nguyên tắc: mọi mã lỗi có thật đều phải có ít nhất một hạng mục bắt được.**
Danh mục kiểm mà không bắt được lỗi vẫn xảy ra thì chỉ là thủ tục.

| Mã lỗi | Lượt NG | % | Hạng mục bắt |
|---|---|---|---|
| CRACK | 853 | 16,3% | PNC-A1 |
| OTHER | 716 | 13,7% | (mã gom — PNC-A12 dùng) |
| DENT | 663 | 12,7% | PNC-A2 |
| BURR | 563 | 10,8% | PNC-A3 |
| CONVEX | 541 | 10,3% | PNC-A4 · PNC-D2 |
| FRAY | 510 | 9,8% | PNC-A5 |
| FULLCUT | 412 | 7,9% | PNC-A6 · PNC-B3 · PNC-D1 |
| PAINTCRK | 382 | 7,3% | PNC-A7 |
| SCRATCH | 275 | 5,3% | PNC-A8 |
| PRESSDEV | 136 | 2,6% | PNC-B2 |
| GLUE | 114 | 2,2% | PNC-A9 |
| OIL | 57 | 1,1% | PNC-A10 |
| DIRTY | 7 | 0,1% | PNC-A11 |
| SIZE | — | — | PNC-B1 · B4 · B5 · B6 · B7 |

**14/14 mã được phủ, 0 mã thiếu.**

Cấu trúc theo đúng khuôn LABEL/SILK đang chạy: nhóm `A·Ngoại quan` (12) ·
`B·Kích thước` (7) · `D·Chức năng` (2). **Không có `C·Màu sắc`** — khâu cắt
không kiểm màu.

Mức và AQL theo đúng quy ước đang dùng: `◆ Critical` → AQL 0,65 · `● Major` →
1,5 · `○ Minor` → 4. Phân bố: 5 Critical · 14 Major · 2 Minor.

Ba hạng mục dùng cờ tick-box để thu hẹp phạm vi thay vì áp cho mọi máy:
`PNC-B4` chỉ khi có khoan (`Drill hole`), `PNC-B5` chỉ khi có đục lỗ
(`Punch hole`), `PNC-D1` chỉ với sản phẩm có lớp đế.

## Đã kiểm chứng — không chỉ là file đẹp

1. **Parser thật đọc được**: `QcLibraryV5Parser` đọc 21/21 hạng mục, đúng
   `ProcessLine=PRESS_CNC`, `Ipqc=true` 21/21, có Acceptance 21/21, có AQL 21/21.
2. **Chạy thật trên bản sao DB live**: nạp 21 hạng mục rồi mở IPQC cho
   `WO-TEST-02` (máy ACNC3 → PRESS_CNC). Kết quả: **21 hạng mục riêng
   (`PNC-*`), 0 hạng mục mượn (`LBL-*`)**. `QcLineLibrarySelector` tự ưu tiên
   `ProcessLine` khớp nên đường lùi theo cờ `SheetCut` bị thay thế đúng thiết kế.

## Bên chất lượng cần quyết trước khi nạp

1. **Dung sai SỐ cho 3 hạng mục đo** — `PNC-B1` (kích thước tổng thể),
   `PNC-B3` (độ sâu cắt), `PNC-B4` (đường kính lỗ). Hiện để dạng "trong dung sai
   bản vẽ"; máy chưa chấm được cho tới khi có cận trên/dưới (đó là P4).
2. **Mức nặng-nhẹ có đúng không.** Tôi gán theo hậu quả, có tham chiếu tần suất
   NG — nhưng người đứng chuyền mới biết lỗi nào thực sự chặn hàng.
3. **Ba hạng mục này có áp ở FQC/OQC không?** Dự thảo để `IPQC=●`,
   `FQC`/`OQC` trống. Thư viện LABEL hiện bật cả ba. Đây là quyết định nghiệp vụ.
4. **`PNC-A12` (bóc lưới thải)** hiện gán mã lỗi `OTHER` vì Pareto chưa có mã
   riêng. Nếu khâu cắt gặp lỗi này thường xuyên thì nên mở một mã riêng.

## Nạp thế nào khi đã duyệt

Dán 21 dòng vào sheet `IPQC_FQC_OQC_MAP` của `IPQC_Library_CMES_v5.xlsx` (cùng
thứ tự cột), rồi chạy seeder — nó upsert theo `ItemId` và **không xoá** dòng cũ
(DR-1). Sau khi PRESS_CNC có thư viện riêng, cân nhắc gỡ đường lùi theo cờ trong
`QcLineLibrarySelector.FlagFallback` để không còn hai nguồn cho cùng một dòng.
