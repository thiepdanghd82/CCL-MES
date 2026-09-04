# P13 — Đưa toàn bộ hạng mục kiểm IQC của file master vào app

> Nguồn: `IQC report 2026 (version 1).xlsx` — 9 sheet, 4,5 MB.
> Mọi con số dưới đây **đo được từ file**, không suy đoán. Lệnh đo nằm ở
> `docs/p13-measurements.md`.

## 1. Hiện trạng đo được

### 1.1 Bốn nhóm vật liệu, hạng mục khác nhau hẳn

| Nhóm | Bản ghi 2026 | Ngoại quan (đếm lỗi) | Đo lường | Khác |
|---|---:|---:|---|---|
| Roll — cuộn/băng | 3 711 | 13 | Rộng ×5, Dày ×5 | Keo/độ cứng · RoHS · L-a-b |
| PCS — tấm/miếng | 140 | 9 | Rộng×Dài ×5, Dày ×5 | RoHS · HSF |
| Chem — hoá chất | 528 | 3 (đóng gói) | — | HSF · COA |
| Tool — dụng cụ | 957 | 6 | — | HSF |

Chung mọi nhóm: tem nhãn (ngày nhập / hết hạn), PEFC-FSC + Level, khối NG
(công đoạn phát hiện · tên lỗi · số lượng NG · hình thức claim · trạng thái).

### 1.2 Luật lấy mẫu — KHÔNG phải tra bảng thẳng

Sheet `AQL` cho 15 bậc cỡ lô → cỡ mẫu. Nhưng đối chiếu 5 336 bản ghi thật:

| | tra thẳng | `min(bảng, cỡ lô)` | khác |
|---|---:|---:|---:|
| Roll | 70 % | 27 % | 1 % |
| Chem | 59 % | 39 % | 1 % |
| Tool | 5 % | **93 %** | 0 % |

⇒ Công thức đúng là **`min(bảng(lô), lô)`** — không ai lấy 2 cuộn ra khỏi một
lô 1 cuộn. Nhóm Tool rơi vào nhánh cắt ngọn tới 93 % vì lô dao thường 1–2 cái.
Phần "khác" là QC **cố ý** lấy nhiều hơn (kiểm siết / kiểm 100 %) — ghi đè hợp
lệ, không phải sai sót.

### 1.3 Luật chấp nhận — Ac = 0, và file KHÔNG hề nói ra

Bảng AQL trong file chỉ có cỡ mẫu, **không có cột Ac/Re**. Luật thật nằm trong
hành vi, phải đo mới thấy — 3 715 lô Roll có kết luận rõ ràng:

| | có đếm được lỗi | không lỗi nào |
|---|---:|---:|
| **OK** | **0** | 3 648 |
| **NG** | 67 | **0** |

Không một lô nào có lỗi mà vẫn đạt ⇒ **zero-defect, Ac = 0, Re = 1**, tuyệt
đối. Đây là phát hiện quan trọng nhất của đợt phân tích: nó biến "đánh giá cảm
tính của người kiểm" thành một luật máy chấm được.

### 1.4 Tiêu chuẩn theo mã — sheet `Raw`

1 028 mã mẹ phân biệt. Tỷ lệ khai đủ: rộng 87 % · keo 69 % · dày 68 % ·
phương pháp test 68 % (FTM1 656× · FTM2 421× · ASTM D3330 64× · N/A 175×).

Đối chiếu với app: **356/459** mã đã có spec trùng khớp; **672** mã trong file
mà app chưa có; 92 mã app có mà file không có. Import xong: 459 → **1 131** mã.

### 1.5 Chuỗi tiêu chuẩn có máy đọc được không

937 dạng phân biệt / 12 999 lượt dùng. Sau khi dựng bộ đọc:

| | lượt | % |
|---|---:|---:|
| đọc được thành ngưỡng số | 6 483 | 49 % |
| trị danh nghĩa trần (độ rộng — Roll có cột Low/Up riêng) | 5 695 | 43 % |
| khai rõ không có chuẩn (`N/A`, `Tham khảo báo cáo`) | 598 | 4 % |
| **chưa xử lý được** | 223 | **1 %** |

1 % còn lại là chuỗi thật sự mơ hồ (`3.0±0.3~0.5`, `400/600/150/g/25mm`,
`Mặt 1: … / Mặt 2: …`). Bộ đọc trả `null` cho chúng và **nhường quyền cho
người chấm** — cố đoán sẽ tạo ra ngưỡng bịa, trông y hệt ngưỡng thật.

## 2. Khoảng cách với app hiện tại

App có 21 hạng mục chung, mỗi hạng mục **một** ô `MeasuredValue` + một cờ
`Pass`. Thiếu:

1. Bảng AQL và luật cỡ mẫu — **chưa có gì**
2. Đo **lặp ×5** cho một hạng mục — mô hình hiện tại chỉ 1 ô
3. Ô **đếm lỗi** — hiện chỉ có đạt/không đạt
4. **Giới hạn số** để tự chấm — hiện tiêu chuẩn là văn bản thuần
5. Tách **4 nhóm** vật liệu — hiện một thư viện phẳng
6. Khối **NG / claim** — chưa có
7. **PEFC-FSC + Level** — chưa có

## 3. Ba quyết định Henry đã chốt (2026-09-04)

| Vấn đề | Quyết định |
|---|---|
| Máy tự chấm | **Ràng buộc.** Đổi kết luận phải ghi lý do; bản ghi giữ CẢ HAI (máy nói gì · người đổi thành gì · ai đổi) |
| Ghi đè cỡ mẫu | **Mọi thay đổi** đều phải ghi lý do |
| Import 1 028 mã | Import hết, gắn cờ **chờ QC duyệt**; phiếu dùng mã chưa duyệt hiện băng nhắc, **không chặn sản xuất** |

## 4. Kế hoạch — 6 bước

| Bước | Nội dung | Trạng thái |
|---|---|---|
| **13-1** | Bộ máy thuần: bảng AQL · bộ đọc tiêu chuẩn · luật chấp nhận | ✅ **XONG** — 71 test |
| 13-2 | Domain + migration: `Category`/`Kind` cho thư viện · bảng đo lặp · ô đếm lỗi · giới hạn số · cờ duyệt spec | ⏳ |
| 13-3 | Importer sheet `Raw` (1 028 mã) + `AQL`, idempotent, dry-run mặc định | ⏳ |
| 13-4 | Service ghi: đề xuất cỡ mẫu · tự chấm · ghi đè-kèm-lý-do · audit | ⏳ |
| 13-5 | Khối NG / claim (4 hình thức claim × 5 trạng thái) | ⏳ |
| 13-6 | UI 4 nhóm: lưới đếm lỗi · lưới đo ×5 · băng cảnh báo spec chưa duyệt | ⏳ |

## 5. Việc bước 13-1 đã giao

- `IqcSamplingTable` — 15 bậc, `Suggest(lô) = min(bảng, lô)`, `IsRelaxed`
- `IqcSpecLimitParser` — 10 khuôn chuỗi thật, NFKC cho ký tự toàn rộng CJK
- `IqcAcceptance` — Ac=0 · kiểm khoảng · `Combine` ba trạng thái
- 71 test, mọi chuỗi và con số lấy từ file thật

## 6. Điểm nghiệp vụ cần Henry xác nhận

1. **Bảng AQL thiếu Ac/Re.** Thực tế đang chạy Ac=0. Nếu muốn nới cho nhóm ít
   rủi ro (vd Tool) thì phải khai tường minh, không để ngầm.
2. **Ngưỡng "or tear"** hiện hiểu là: vật liệu rách ⇒ đạt bất kể trị đo. Cần QA
   ký xác nhận vì nó bỏ qua ngưỡng số.
3. **Đo ×5 là cố định** bất kể cỡ lô — khác với cỡ mẫu ngoại quan vốn theo lô.
   Xác nhận đây là chủ ý.
4. **65 dạng chuỗi (1 %) không đọc được** — nên sửa lại trong file master cho
   thống nhất, thay vì bắt bộ đọc chiều theo mọi cách gõ.

---

## 7. Nhật ký migration P13 bước 2

`20260904075826_AddIqcP13SamplingAndLimits` — 26 cột + 1 bảng
(`IqcResultMeasurements`), toàn bộ additive.

| Phase | Kết quả |
|---|---|
| A | backup `data/Backup/SQLite/ccl_mes.db.before-p13-2.20260904T080909Z` · sha `3f69ee92…` · 48 migration · integrity ok |
| B | trên **bản sao thật** của live — enum về đúng `Any/Verdict` + `Approved`, rowcount giữ nguyên, type-affinity 0 |
| C | sha → `e8eba9ab…` · **48 → 49** migration · mọi rowcount khớp Phase A · integrity ok |

Kèm theo (Henry duyệt cùng lúc): xoá **3 dòng `WoQcCheckItems` mồ côi** có từ
2026-06-07 (`ItemKey=appearance`, `Status=Ok`, không mã lỗi NG — rác sót lại
khi dọn dữ liệu test P10.7e, cha đã bị xoá mà con thì không).
`foreign_key_check` **3 → 0**. Nội dung đầy đủ của ba dòng đã chép ra
`p13-migration-evidence/orphan-cleanup-20260904T080909Z.txt` trước khi xoá.

> Câu lệnh xoá dùng `WHERE WoQcCheckId NOT IN (SELECT Id FROM WoQcChecks)`
> chứ KHÔNG dùng `Id IN (1,2,3)`: nếu chạy lại trên một DB khác, điều kiện
> theo Id sẽ xoá nhầm ba dòng hoàn toàn khác.
