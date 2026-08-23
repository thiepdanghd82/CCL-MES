# Đánh giá QMS module QC theo ISO 9001:2015 — CCL-CMES

> **Ngày lập:** 2026-08-21 · **Trạng thái:** DRAFT — chờ Henry duyệt
> **Bản đọc được:** https://claude.ai/code/artifact/d16373ca-9bd8-466f-9fd9-8194d965b9b2
> **Nguồn bằng chứng:** mã nguồn nhánh `main` + bản sao CHỈ-ĐỌC của `data/ccl_mes.db`
> (18,6 MB, 2026-08-21). **Không có dòng dữ liệu nào bị thay đổi.**

---

## 0. Đính chính chuẩn tham chiếu

**Không tồn tại ISO 9001:2005.** Các phiên bản đã phát hành: 1987 · 1994 · 2000 ·
2008 · **2015** (+ `Amd 1:2024` bổ sung yêu cầu xem xét biến đổi khí hậu ở điều
4.1/4.2). Số hiệu ":2005" nhiều khả năng là nhớ nhầm sang **ISO 9000:2005 — Cơ sở
và từ vựng**, tài liệu định nghĩa thuật ngữ chứ không đặt yêu cầu chứng nhận, và
bản thân nó cũng đã bị ISO 9000:2015 thay thế.

Báo cáo này đối chiếu theo **ISO 9001:2015**. Nếu CCL Design Vietnam đang giữ chứng
chỉ theo chuẩn khác, gửi số hiệu chứng chỉ để soát lại phần điều khoản — phần phát
hiện kỹ thuật không đổi.

**Chuẩn ngành cần xác nhận phạm vi áp dụng với QA CCL trước Đợt 1:**

| Chuẩn | Nội dung | Phát hiện liên quan |
|---|---|---|
| ISO 2859-1 | Lấy mẫu theo thuộc tính (AQL), bảng cỡ mẫu + Ac/Re | M5 |
| ISO/IEC 15416 | Chấm điểm chất lượng in mã vạch 1D (grade A–F) | M1, M2 |
| ISO 13655 · CIE ΔE₀₀ | Đo màu phản xạ, sai khác màu | M1, M2 |
| ISO 12647-2/-6 | Kiểm soát quá trình in offset / flexo | M2, m12 |
| ISO 15378 | GMP cho bao bì cấp 1 dược phẩm | M3, M4, M6, m11 |
| IATF 16949 | QMS ô tô — nếu có khách automotive | M1, M5, Đợt 4 |
| 21 CFR Part 11 | Hồ sơ & chữ ký điện tử (khách dược Mỹ) | m11 |

---

## 1. Tóm tắt điều hành

CCL-CMES đã xây xong nửa **KIỂM SOÁT** của ISO 9001 và làm rất tốt: snapshot đóng
băng append-only có SHA-256, vết audit chụp cả vai trò tại thời điểm, luật ba chữ ký
OQC tách vai viết thành hàm thuần có unit test, vòng đời lô NVL có cách ly + hai chữ
ký khi gia hạn. Đó là những thứ phần lớn MES tự xây **không** có.

Nửa **CẢI TIẾN** chưa tồn tại. Hệ ghi lại rất tốt việc *đã kiểm*, nhưng gần như
không ghi lại được *kiểm ra cái gì* và *sau đó làm gì*.

**Kết luận:** ở trạng thái hôm nay CCL-CMES **không đủ để một mình đứng ra chịu một
cuộc audit khách hàng** về điều 8.6, 8.7 và 10.2.

**Tin tốt:** không có phát hiện nào đòi đập đi làm lại. Mô hình đóng băng, khoá
`DefectCode` trong thư viện v5 và cấu trúc snapshot hiện có đã là móng đúng. Cả 15
điểm thiếu đều đóng được bằng cách **thêm**, không phải **sửa** — và dự án đã tự
nhận diện hai trong số đó ở hạng mục C1/C2 của `IMPROVEMENT-BACKLOG.md`.

**Tổng kết:** 6 không phù hợp nặng · 9 không phù hợp nhẹ · 7 điểm mạnh phải giữ.
Đối chiếu 21 điều khoản: **6 đạt · 6 đạt một phần · 8 chưa đạt · 3 ngoài phạm vi hệ thống**.

---

## 2. Phạm vi & phương pháp

Hai nguồn bằng chứng độc lập, **không** dựa vào tài liệu mô tả:

1. **Mã nguồn** — `src/CCL.MES.Domain`, `src/CCL.MES.Application`,
   `CCL-MES-Hybrid/src/CCL.MES.Api`, `CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor`.
   Mọi khẳng định "có / không có" kèm `file:dòng`.
2. **DB vận hành** — `data/ccl_mes.db` sao ra thư mục tạm, chỉ chạy `SELECT`.
   60 bảng nghiệp vụ được đếm và lấy mẫu.

Đọc dữ liệu thật là chủ ý: một module có thể tồn tại đầy đủ trong mã mà chưa từng
được dùng — và với ISO, **một quy trình không có hồ sơ thì không tồn tại**. Nhiều
phát hiện dưới đây chỉ lộ ra khi đếm dòng, không lộ ra khi đọc mã.

**Ngoài phạm vi:** đánh giá nội bộ (9.2), xem xét lãnh đạo (9.3), bối cảnh tổ chức
(4.1–4.4), chính sách & mục tiêu chất lượng (5.2, 6.2).

---

## 3. Bản đồ module QC hiện có

Cột *Vận hành thực* lấy từ số dòng DB, không lấy từ tài liệu.

| Module | Thực thể | Dòng DB | Vận hành thực |
|---|---|---:|---|
| **IQC** | `IqcInspection` / `IqcResultDetail` | 25 / 7 | ⚠️ Mới khởi động — 23/25 phiếu còn Pending |
| **Lô NVL** | `MaterialLot` | 27 | ⚠️ Chưa thông — **27/27 còn Quarantine** |
| **IPQC** | `WoIpqcCheck` / `WoIpqcCheckItem` | 7 / 117 | ✅ Đang chạy |
| **FQC/OQC** | `WoQcCheck` / `WoQcCheckItem` / `WoQcPhoto` | 8 / 83 / **0** | ⚠️ Chạy, chưa dùng ảnh |
| **Thư viện v5** | `CheckItemLibrary` | 59 | ⚠️ Dùng, chưa phân tầng công đoạn |
| **Kế hoạch kiểm theo spec** | `SpecQcWindow` / `QcCriterion` / `SpecQcCapture` | **0 / 0 / 0** | 🔴 Bỏ hoang |
| **Truy xuất** | `WoTraceSnapshot` / `WoTraceIndex` | 17 | ✅ Đang chạy |
| **iCRA / CAPA** | *— không có thực thể —* | — | 🔴 Dữ liệu giả (`QmsMock.Icra`) |
| **Kiểm soát tài liệu** | `ProductRevision` / `Drawing` | 340 | ✅ Đang chạy tốt |
| **Vết audit** | `AuditLog` | 2.521 | ✅ Đang chạy tốt |

### Ba con số đáng chú ý nhất

- **0 / 0 / 0** cho bộ kế hoạch kiểm theo spec. Mã đã có sẵn `SamplePlan`,
  `Frequency`, `QcRejectAction {Rework, Scrap, Escalate, RecordOnly}`,
  `QcCriterionType {Visual, Dimensional, Colorimetric, Functional, Count}` — chỉ chưa
  ai nhập dữ liệu. **Hệ quả: QC chạy theo *dòng sản phẩm*, không theo *yêu cầu khách
  hàng cho mã hàng đó*.**
- **0** ảnh bằng chứng QC trên 83 hạng mục FQC/OQC đã kết luận.
- **27/27** lô NVL còn Quarantine. Nếu giữ nguyên khi go-live thì hoặc IQC chưa vận
  hành, hoặc luật chặn tiêu thụ đang bị đi vòng — cần xác minh, vì đó là điều 8.5.2.

---

## 4. Đối chiếu điều khoản ISO 9001:2015

| Điều | Yêu cầu | Mức | Ghi chú |
|---|---|---|---|
| 7.1.5.1 | Nguồn lực theo dõi & đo lường phù hợp | 🔴 Chưa đạt | Không có đăng ký thiết bị đo |
| 7.1.5.2 | Liên kết chuẩn đo lường — hiệu chuẩn, nhận biết | 🔴 Chưa đạt | M6 |
| 7.2 | Năng lực người thực hiện công việc | 🔴 Chưa đạt | m9 |
| 7.5.2 | Tạo lập & cập nhật thông tin dạng văn bản | ✅ Đạt | `ProductRevision` 5 trạng thái + phả hệ |
| 7.5.3 | Kiểm soát thông tin — bảo vệ khỏi sửa đổi ngoài ý muốn | ✅ Đạt | Snapshot append-only + hash + audit |
| 8.1 | Hoạch định tác nghiệp — tiêu chí chấp nhận | ⚠️ Một phần | m7 |
| 8.3 | Thiết kế & phát triển (NPI) | ✅ Đạt | NPI + duyệt bản vẽ 3 vai |
| 8.4.1 | Đánh giá, lựa chọn, theo dõi nhà cung cấp | 🔴 Chưa đạt | m10 |
| 8.5.1(c) | Theo dõi & đo lường ở giai đoạn thích hợp | ⚠️ Một phần | Có cổng kiểm, không có số đo |
| 8.5.1(e) | Người có năng lực, kể cả trình độ được yêu cầu | ⚠️ Một phần | RBAC có, chứng nhận không |
| 8.5.2 | Nhận biết & truy xuất nguồn gốc | ✅ Đạt | Lô NVL khoá số + snapshot |
| 8.5.6 | Kiểm soát thay đổi | ✅ Đạt | Revision + ChangeSummary + không hồi tố |
| **8.6** | **Thông qua sản phẩm — bằng chứng phù hợp + truy được người thông qua** | ⚠️ Một phần | **Người ký: đạt. Bằng chứng phù hợp: chưa** (M1, M2, M5) |
| **8.7.1** | **Kiểm soát đầu ra không phù hợp** | 🔴 Chưa đạt | M3, M4 |
| **8.7.2** | **Lưu hồ sơ NC: mô tả, hành động, nhượng bộ, người quyết định** | 🔴 Chưa đạt | M4 |
| 9.1.1 | Theo dõi, đo lường, phân tích, đánh giá | ⚠️ Một phần | Có OEE, không có dữ liệu chất lượng phân tích được |
| 9.1.2 | Sự thoả mãn khách hàng | ⬜ Ngoài hệ | m14 |
| 9.1.3 | Phân tích & đánh giá dữ liệu | 🔴 Chưa đạt | M1 + m12 |
| 9.2 · 9.3 | Đánh giá nội bộ · xem xét lãnh đạo | ⬜ Ngoài hệ | Quy trình cấp doanh nghiệp |
| **10.2** | **Sự không phù hợp & hành động khắc phục** | 🔴 Chưa đạt | M3 |
| 10.3 | Cải tiến liên tục | 🔴 Chưa đạt | Không có SPC, không có xu hướng lỗi |

---

## 5. Phát hiện — không phù hợp NẶNG

> "Nặng" = *nếu khách hàng audit dây chuyền nhãn vào tuần sau, đây là những điểm sẽ
> bị ghi nhận thành finding chính thức.*

### M1 — Hệ không lưu giá trị đo; mọi kết quả kiểm chỉ là Ok/NG
**Điều 8.6 · 8.5.1(c) · 9.1.3**

**Bằng chứng.** `PRAGMA table_info` trên DB vận hành: `WoIpqcCheckItems` và
`WoQcCheckItems` chỉ có `ItemKey · Status · NgReasonCode · NgNote · Sort ·
PhotoBlobId`. **Không có cột nào chứa số đo.** `Status` là enum
`{Pending, Ok, Ng}` (`src/CCL.MES.Domain/Enums.cs`). Thực thể cũ `QcResultDetail`
*có* `MeasuredValue` — nhưng `QcInspections` hiện **0 dòng**, nhánh đó đã chết.

**Rủi ro.** Đánh giá viên hỏi "cho tôi xem bằng chứng lô này đạt ΔE ≤ 2" → hệ trả về
một ô tick. Ô tick không phải bằng chứng phù hợp, nó là bằng chứng có người bấm nút.
Dây chuyền: không biến số ⇒ không SPC ⇒ không năng lực quá trình ⇒ không MSA ⇒ mất
nền điều 9.1.3 và 10.3. Với khách IATF 16949 đây là điểm chặn.

---

### M2 — Chuỗi giải ngưỡng đã viết đủ, có test, nhưng CHƯA NỐI vào đường ghi
**Điều 8.5.1(c) · 8.6 · (kéo theo 7.5.2)**

**Bằng chứng.** `grep -rn "QcThresholdResolver"` toàn kho (loại `obj/`, `bin/`, worktree):

- 13 lời gọi — **tất cả** trong `tests/CCL.MES.Tests/Unit/QcThresholdResolverTests.cs`
- 1 tham chiếu `<see cref>` tại `src/CCL.MES.Domain/Entities/MasterData.cs:26`
- 1 định nghĩa lớp tại `src/CCL.MES.Application/Services/QcThresholdResolver.cs:28`
- **0 lời gọi trong mã production**

Cột `Product.QcProfileOverride` *có* được đọc tại
`CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/WoQcMutationControllerBase.cs:214` —
nhưng để chọn **bộ hạng mục nào hiện ra** (qua `QcProfileResolver`), **không phải**
để so sánh ngưỡng.

**Rủi ro.** Đây là dạng finding tệ nhất: *tài liệu mô tả một kiểm soát không tồn
tại*. Nó chuyển finding từ "thiếu kiểm soát" sang "tài liệu không phản ánh thực tế",
đụng luôn 7.5.2. Ngưỡng ΔE ≤ 2 hiện là một dòng chữ, không phải một cái cổng —
người kiểm tự nhìn và tự quyết.

---

### M3 — Vòng không phù hợp chưa tồn tại; iCRA là dữ liệu giả
**Điều 8.7.1 · 10.2 · 10.3**

**Bằng chứng.**
`CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor/Pages/IcraModule.razor:27` lặp trên
`QmsMock.Icra` — danh sách tĩnh khai báo cứng tại
`CCL-MES-Hybrid/src/CCL.MES.Hybrid.Client/Qms/QmsUiModels.cs:46`. Chú thích trong
chính tệp ghi: *"Static mock (QmsMock)"*.

Trong 60 bảng DB: không có `NonConformances`, `Dispositions`, `Capa`. Tìm "capa",
"nonconform", "corrective action" trong `*.cs`: chỉ trúng tài liệu và định nghĩa
agent, không trúng thực thể nào.

**Rủi ro.** 8.7 và 10.2 nằm trong nhóm câu hỏi mở màn của mọi cuộc audit. Một màn
hình hiển thị dữ liệu giả **tệ hơn** không có màn hình: nó tạo ấn tượng có quy trình,
rồi khi xin hồ sơ NC thật thì lộ ra không có gì phía sau — finding về **tính chính
trực của hệ thống**, không chỉ về tính năng thiếu.

**Khuyến nghị tức thời:** hoặc đóng vòng thật, hoặc **gỡ iCRA khỏi menu trước khi
bàn giao**. Không để tồn tại ở dạng hiện nay.

---

### M4 — Nhượng bộ có chữ ký nhưng thiếu 4 yếu tố mà 8.7.2 bắt buộc
**Điều 8.7.1(c)(d) · 8.7.2**

**Bằng chứng.** Hai bản ghi nhượng bộ thật (SPECIAL_ACCEPT → QA duyệt) trong DB:

```
WorkOrderId         = 3
SpecialAcceptReason = "Lô gấp giao trong ngày, ΔE 2.3 chấp nhận được"
IpqcSubmittedBy     = ipqc-test-checkpoint
QaOutcome           = Approve
QaApprovedBy        = qa-test-checkpoint
```

Cơ chế tách vai hoạt động đúng (`WO_QA_APPROVE_DENIED` emit khi vi phạm —
`IpqcReviewController.cs:506`). Nhưng hồ sơ thiếu:

1. Số lượng bị ảnh hưởng
2. Mã lỗi có cấu trúc (ΔE 2.3 nằm trong văn bản tự do)
3. Khách hàng đã được thông báo hay chưa — **8.7.1(c)**
4. Phạm vi & hiệu lực nhượng bộ (một lô? một đơn? tới ngày nào?)

**Rủi ro.** 8.7.2 liệt kê rành mạch bốn thứ phải lưu: mô tả NC, hành động đã thực
hiện, nhượng bộ đã nhận được, và người quyết định. Hệ có "người quyết định" + một câu
văn tự do; ba yếu tố còn lại không truy vấn được, không thống kê được. Với khách dược
theo ISO 15378, thả hàng lệch spec mà không có bằng chứng đã thông báo khách có thể
dẫn tới yêu cầu thu hồi.

---

### M5 — AQL khai báo đủ 59/59 hạng mục nhưng KHÔNG dòng mã nào thi hành
**Điều 8.6 · ISO 2859-1**

**Bằng chứng.** Thư viện lấp đầy 100%: 59/59 có `Aql` và `Sampling`
(vd `"FAI 100% + AQL 0.65"`), 59/59 có `Severity` ba bậc
(**20 Critical · 35 Major · 4 Minor**).

`grep "Aql"` trong mã: chỉ trúng `src/CCL.MES.Infrastructure/DbSeeder.cs:539` (nạp
dữ liệu) và các tệp migration. **Không có bộ tính cỡ mẫu, không có Ac/Re, không có
cột lưu cỡ mẫu thực tế.**

`src/CCL.MES.Domain/StateMachine/FqcReadinessRollup.cs` tự thừa nhận trong chú thích:
*"Pass STILL allowed… operator may flag minor NGs without failing the lot"* — nhưng
không có ngưỡng nào giới hạn số NG đó.

**Lỗi dữ liệu kèm theo:** giá trị lưu là `"0,65"` / `"1,5"` — **dấu phẩy thập phân**,
không parse được bằng invariant culture. Phải chuẩn hoá trước khi dùng.

**Rủi ro.** Người kiểm có thể đánh NG 10 hạng mục Critical và vẫn bấm Pass cho cả lô,
chỉ cần gõ một dòng lý do. Kế hoạch lấy mẫu *được in trên phiếu* nhưng *không ràng
buộc quyết định* — về hình thức là có kiểm soát, về thực chất là không.

---

### M6 — Không có sổ thiết bị đo và lịch hiệu chuẩn
**Điều 7.1.5.1 · 7.1.5.2**

**Bằng chứng.** `grep -i "calibrat|hiệu chuẩn"` toàn bộ `*.cs` + `*.razor`:
**0 kết quả.** `src/CCL.MES.Domain/Entities/Machine.cs` chỉ có `Code`, `Name`,
`Type`, `CurrentState`, `IdealCycleTimeSec` — hoàn toàn là thiết bị sản xuất, không
có khái niệm thiết bị đo. Thư viện có cột `Method` mô tả "phương pháp · dụng cụ kiểm"
dạng văn bản nhưng không nối tới thiết bị cụ thể nào.

**Rủi ro.** 7.1.5.2 yêu cầu thiết bị đo phải được hiệu chuẩn theo chuẩn có liên kết,
nhận biết được trạng thái, và **khi phát hiện thiết bị không phù hợp thì phải xác
định giá trị của các kết quả đo trước đó**.

Vế cuối là vế đắt nhất: nếu quang phổ kế lệch chuẩn, CCL phải trả lời được *"những lô
nào đã được đo bằng thiết bị này kể từ lần hiệu chuẩn đạt gần nhất"*. Hôm nay không
trả lời được — không có liên kết phép-đo ↔ thiết bị nào cả.

---

## 6. Phát hiện — không phù hợp NHẸ

| Mã | Phát hiện | Điều | Bằng chứng |
|---|---|---|---|
| **m7** | **Kế hoạch kiểm theo spec bị bỏ hoang.** `SpecQcWindow` có sẵn cỡ mẫu, tần suất, `QcRejectAction`, 5 kiểu tiêu chí. QC thực tế chạy theo dòng SP, không theo yêu cầu khách hàng của mã hàng | 8.1 · 8.5.1(c) | `SpecQcWindows` 0 · `QcCriteria` 0 · `SpecQcCaptures` 0 |
| **m8** | **Thư viện không phân biệt công đoạn.** 59/59 hạng mục tick đồng thời IPQC+FQC+OQC → cùng một bộ item materialize cho cả ba cổng. Và 0/59 có phạm vi theo mã hàng | 8.1 | `GROUP BY Ipqc,Fqc,Oqc` → một nhóm `(1,1,1)`, n=59 |
| **m9** | **Không có hồ sơ năng lực người kiểm.** Quyền ký chỉ dựa chuỗi `Role`; không có chứng nhận, ngày hiệu lực, giới hạn phạm vi | 7.2 · 8.5.1(e) | `User`: chỉ `Role` + `Department` |
| **m10** | **Không đánh giá nhà cung cấp.** `SupplierName` là chuỗi tự do; không có thực thể, không có điểm, không tái đánh giá | 8.4.1 | `grep "class Supplier"` → 0 kết quả |
| **m11** | **Chữ ký điện tử không xác thực lại tại thời điểm ký.** Duyệt OQC chỉ dựa JWT + policy `QcEdit`; trong khi *xoá bản vẽ* lại bắt nhập username + mật khẩu. Hành động rủi ro cao hơn được bảo vệ yếu hơn | 7.5.3 · 21 CFR 11 | `WoQcReviewController.cs:528` vs `DrawingsApiController.cs:479` |
| **m12** | **Phế ghi số lượng nhưng không ghi nguyên nhân.** `ProductionLog.RejectQty` là `int` trần, không FK tới mã lỗi. 53 `ReasonCode` tồn tại nhưng phế không gắn được | 9.1.3 · 10.3 | `ProductionLog` có `DowntimeReasonId`, **không** có `ScrapReasonId` |
| **m13** | **Chưa có hồ sơ chất lượng xuất một nút.** Snapshot đã đóng băng đủ (17 dòng, 4 phase) nhưng chưa gộp thành PDF cho audit khách hàng | 8.6 · 7.5.3 | Endpoint `summary-report` có, chưa thành gói hồ sơ |
| **m14** | **Không có khiếu nại khách hàng / hàng trả về.** Không có đường vào cho NC nguồn ngoại; vòng cải tiến chỉ nhìn được lỗi nội bộ | 9.1.2 · 10.2 | `grep "complaint\|khiếu nại"` → 0 |
| **m15** | **Ảnh bằng chứng không bắt buộc khi NG.** Hạ tầng đã đủ (SHA-256, 5 MiB cap, kiểm MIME) nhưng chưa có luật bắt buộc | 8.7.2 | `WoQcPhotos` **0 dòng** / 83 hạng mục đã kết luận |

---

## 7. Điểm mạnh phải giữ

Mọi phương án cải tiến phải **không** làm hỏng bảy điểm này — đặc biệt tính bất biến
của bằng chứng.

| Cơ chế | Vì sao là điểm mạnh theo ISO | Điều |
|---|---|---|
| **Snapshot đóng băng append-only** — `WoTraceSnapshot` có SHA-256, version tăng dần, không bao giờ upsert | Sửa master data hôm nay không đổi hồ sơ lô đã xuất tháng trước | 7.5.3 |
| **Đóng băng bộ hạng mục vào phiếu** — `ProfileSnapshotJson` / `ItemsProfileSnapshotJson` | Đánh giá viên thấy đúng tiêu chí đã áp dụng *tại thời điểm đó*, không phải tiêu chí hôm nay | 8.6 · 8.5.6 |
| **Ba chữ ký OQC tách vai** — `OqcSignaturePolicy`, hàm thuần, so khớp không phân biệt hoa thường | Một người không thể tự đẩy lô hàng ra khỏi nhà máy. Luật thuần ⇒ kiểm soát *chứng minh được* bằng unit test | 8.6 |
| **Vết audit chụp vai trò tại thời điểm** — `AuditLog.ActorRole` | Đổi vai trò về sau không viết lại lịch sử | 7.5.3 |
| **Vòng đời lô NVL có cách ly** — Quarantine → Released/Rejected/Expired, 2 chữ ký khi gia hạn | Cách ly vật lý mô hình hoá bằng trạng thái; quyết định rủi ro cao hơn đòi hai vai khác nhau | 8.5.2 · 8.7.1 |
| **Kiểm soát bản sửa đổi sản phẩm** — 5 trạng thái, ngày hiệu lực, phả hệ, ChangeSummary | Kiểm soát tài liệu đúng chuẩn; 340 dòng thật cho thấy đang được dùng | 7.5.2 · 8.5.6 |
| **Cổng CI 8 lớp + ratchet** — `gate-all.sh` | Kỷ luật này khiến các cải tiến bên dưới *khả thi*; cơ chế chặn tái phát đã sẵn để gắn luật chất lượng mới | 4.4 |

---

## 8. Ba phương án cải tiến

### PA-1 · Mua ngoài (module QMS của ERP)
- **Được:** quy trình đã được audit nhiều nơi, không tốn công dựng, nhà cung cấp
  chịu trách nhiệm cập nhật theo chuẩn.
- **Mất:** nhập hai lần; **đứt liên kết bằng chứng** (NC ở hệ này, snapshot đóng băng
  ở hệ kia, ghép tay khi audit); license định kỳ; tích hợp ngược chính là C3 chưa làm.

### PA-2 · Làm hết trong CMES
- **Được:** một nguồn sự thật; NC gắn thẳng vào lô/WO/snapshot/ảnh.
- **Mất:** phạm vi phình rất nhanh; đánh giá nội bộ + xem xét lãnh đạo là quy trình
  *tổ chức*, ép vào phần mềm sản xuất sẽ tạo module không ai dùng; đẩy rủi ro sang go-live.

### PA-3 · Lai — CMES giữ bằng chứng cấp lô, QMS doanh nghiệp giữ quy trình
Ranh giới đặt ở **đơn vị bằng chứng**: cái gì gắn với một lô / một WO / một phép đo
thì ở CMES; cái gì là quy trình cấp tổ chức thì ở ngoài, nối bằng mã tham chiếu hai chiều.

- **Trong CMES:** giá trị đo · so ngưỡng bằng máy · engine AQL · hồ sơ NC ·
  disposition · nhượng bộ đầy đủ · sổ thiết bị đo · SPC theo mã lỗi · Quality Record Pack.
- **Ngoài CMES:** CAPA cấp hệ thống (8D) · đánh giá nội bộ · xem xét lãnh đạo ·
  khiếu nại khách hàng · đánh giá nhà cung cấp — CMES chỉ giữ *mã tham chiếu* +
  *dữ liệu đầu vào*.

### Chấm điểm (1–5, cao là tốt)

| Tiêu chí | PA-1 | PA-2 | PA-3 |
|---|---:|---:|---:|
| Giữ được tính bất biến của bằng chứng | 2 | 5 | 5 |
| Đóng được finding nặng trước go-live | 2 | 2 | 4 |
| Chi phí & công sức | 2 | 1 | 4 |
| Rủi ro với mốc go-live | 3 | 1 | 4 |
| Gánh nặng vận hành cho người kiểm | 1 | 4 | 4 |
| **Tổng** | **10** | **13** | **21** |

### Khuyến nghị: **PA-3**

Lý do quyết định không phải điểm số mà là một nguyên tắc: **bằng chứng phải nằm cùng
chỗ với dữ liệu sinh ra nó.** Giá trị đo, mã lỗi, ảnh, chữ ký và snapshot đóng băng
phải ở chung một giao dịch — tách ra hai hệ là tự tạo khe hở mà đánh giá viên sẽ tìm
đúng vào đó.

**Cái mất phải chấp nhận:** CAPA cấp hệ thống, đánh giá nội bộ và xem xét lãnh đạo
**không** vào CMES giai đoạn này. Cần ghi rõ ranh giới này trong sổ tay chất lượng —
nếu không đánh giá viên sẽ đi tìm chúng trong CMES và ghi là thiếu.

---

## 9. Kế hoạch triển khai 5 đợt

Thứ tự các đợt **là** thứ tự phụ thuộc kỹ thuật: không có giá trị đo (Đợt 1) thì
không có SPC (Đợt 4); không có hồ sơ NC (Đợt 2) thì không có CAPA (Đợt 4).

### ĐỢT 0 — Dọn điểm gây hiểu nhầm · 1–2 tuần · **làm ngay**
`W4 · W5` · `mes-quality-architect` · skill `cmes-audit-emit`

Không thêm năng lực, chỉ loại bỏ những thứ khiến hệ *trông như* có kiểm soát:

- [ ] Gỡ màn hình **iCRA** khỏi menu, hoặc gắn nhãn "chưa triển khai" rõ ràng — **M3**
- [ ] Chuẩn hoá dữ liệu AQL `"0,65"` → `0.65` dạng số — **M5**
- [ ] Phân tầng lại cờ công đoạn trong thư viện: hạng mục nào thật sự thuộc
      IPQC / FQC / OQC — **m8**
- [ ] Bắt buộc ảnh khi đánh NG (hạ tầng đã có, chỉ thêm luật) — **m15**
- [ ] Xác minh vì sao 27/27 lô còn Quarantine và 23/25 phiếu IQC còn Pending

### ĐỢT 1 — Bằng chứng đo được · 4–6 tuần
`W1 · W4` · `mes-quality-architect` + `cmes-implementer` · skill `cmes-migration-abc`

Đóng **M1** và **M2** — hai finding gốc mà bốn finding khác phụ thuộc vào.

- [ ] Thêm vào `WoIpqcCheckItem` + `WoQcCheckItem`: `MeasuredValue` (double?), `Uom`,
      `LowerLimit` / `UpperLimit` / `Target` — **ba giới hạn phải ĐÓNG BĂNG vào dòng**
      lúc materialize, cùng cơ chế với `ProfileSnapshotJson`
- [ ] Nối `QcThresholdResolver` vào đường ghi thật: **server** so sánh và quyết định
      Ok/NG. **Cấm ghi `Status = Ok` khi giá trị đo nằm ngoài giới hạn**
- [ ] Thêm `CheckType` vào hợp đồng: hạng mục `Measure` bắt buộc có số; `Visual` giữ Ok/NG
- [ ] Migration additive thuần — WO đang chạy không bị ảnh hưởng

**Nghiệm thu:** mở một WO thật, nhập ΔE = 2.4 vào hạng mục ngưỡng 2.0 → hệ tự đặt NG,
từ chối Pass, **dán được output thật** của lệnh kiểm chứng.

### ĐỢT 2 — Đóng vòng không phù hợp · 6–8 tuần
`W1 · W2 · W4` · `mes-quality-architect` + `mes-process-architect` · skill `cmes-state-contract`

Đóng **M3** và **M4**. Đây là hạng mục **C1** đã nằm sẵn trong `IMPROVEMENT-BACKLOG.md`.

- [ ] `NonConformance` — nguồn (IQC/IPQC/FQC/OQC/COMPLAINT), `DefectCode` (khoá đã sẵn
      trong thư viện v5), số lượng ảnh hưởng, mức nghiêm trọng, lô/WO/leg liên quan
- [ ] `Disposition` — `{Rework, Scrap, UseAsIs, Return, Regrade}`, người quyết định,
      lý do, số lượng theo từng hướng. Enum `QcRejectAction` đã có sẵn làm điểm khởi đầu
- [ ] **Nâng cấp nhượng bộ** — bổ sung 4 trường mà 8.7.2 đòi: số lượng, mã lỗi có cấu
      trúc, cờ đã-thông-báo-khách + tham chiếu, phạm vi & hiệu lực
- [ ] **Cách ly bằng trạng thái** — WO/lô có NC mở không được advance cho tới khi có
      disposition. Luật đặt trong domain policy, **không** trong controller
- [ ] Mọi disposition emit audit row; **không** ghi đè snapshot đã đóng băng

**Nghiệm thu:** một lô NG đi trọn đường NC → disposition → đóng, và truy vấn được
"tất cả NC mở theo mã lỗi trong tháng". Snapshot cũ không đổi một byte.

### ĐỢT 3 — Thi hành AQL & Quality Record Pack · 5–6 tuần
`W4 · W5` · `mes-quality-architect` + `cmes-shopfloor-ux` · skill `cmes-spec-print`

Đóng **M5** và **m13**. Hạng mục sau chính là **C2** — mục duy nhất trong backlog
*bán được cho khách hàng*.

- [ ] **Engine lấy mẫu ISO 2859-1**: cỡ lô → chữ cái mã cỡ mẫu → cỡ mẫu → Ac/Re,
      theo từng bậc nghiêm trọng (Critical 0.65 · Major 1.5 · Minor 4.0 — **chốt với QA**)
- [ ] Lưu **cỡ mẫu thực tế** + **số khuyết tật đếm được theo bậc** vào phiếu —
      hôm nay hai số này không tồn tại ở đâu cả
- [ ] Sửa `WoQcReadinessRollup`: Pass chỉ khi số khuyết tật ≤ Ac của bậc tương ứng.
      Vượt Ac → chỉ còn Reject hoặc nhượng bộ có hồ sơ đầy đủ theo Đợt 2
- [ ] **Quality Record Pack** — một WO → một PDF: spec revision, snapshot routing,
      lô NVL đã quét, toàn bộ giá trị đo, chữ ký, ảnh, NC + disposition. Nội dung lấy
      **hoàn toàn** từ snapshot đóng băng, **không JOIN dữ liệu sống** (L29)

**Nghiệm thu:** lô 50.000 nhãn, AQL 1.5, cỡ mẫu do hệ tính → đếm 3 khuyết tật Major →
hệ chặn Pass và nêu đúng Ac. Và: một WO → một PDF, không ghép tay.

### ĐỢT 4 — Thiết bị đo, năng lực & cải tiến · 6–8 tuần
`W1 · W4 · W6` · `mes-quality-architect` · skill `cmes-rbac-matrix`

Đóng **M6**, **m9**, **m12**; mở đường cho 9.1.3 / 10.3.

- [ ] **Sổ thiết bị đo** — mã, loại, độ phân giải, chu kỳ hiệu chuẩn, ngày hiệu chuẩn
      gần nhất, ngày đến hạn, chứng chỉ. Mỗi giá trị đo ghi kèm `MeasuringDeviceId`.
      **Chặn ký khi thiết bị quá hạn**, và truy vấn ngược được "những lô nào đã đo bằng
      thiết bị này kể từ lần hiệu chuẩn đạt gần nhất" — vế đắt nhất của 7.1.5.2
- [ ] **Hồ sơ năng lực** — chứng nhận theo công đoạn + theo khách hàng, có ngày hiệu
      lực; hết hạn thì mất quyền ký, không phải mất quyền đăng nhập
- [ ] **Nguyên nhân phế** — thêm `ScrapReasonId` vào `ProductionLog`, nối 53 `ReasonCode`
- [ ] **SPC** — biểu đồ p/u theo `DefectCode` × ProcessLine × thời gian; X̄-R cho các
      hạng mục đo được (ΔE, kích thước die-cut); Pareto lỗi theo tháng
- [ ] **Móc nối CAPA** — CMES **không** chứa 8D; nó mở NC, gắn mã CAPA từ QMS doanh
      nghiệp, cung cấp số liệu hiệu lực (lỗi cùng mã có tái diễn sau ngày đóng CAPA không)

**Nghiệm thu:** ký OQC bị chặn khi quang phổ kế quá hạn hiệu chuẩn; Pareto lỗi 30 ngày
dựng được từ dữ liệu thật, không phải mock.

---

## 10. KPI & tiêu chí nghiệm thu

Nguyên tắc lấy từ kỷ luật của dự án: **không có output thật thì chưa xong.** Mỗi chỉ
số phải đo được bằng một câu truy vấn.

| Chỉ số | Hôm nay | Sau Đợt 3 | Cách đo |
|---|---:|---:|---|
| Hạng mục `Measure` có giá trị đo | 0% | 100% | `MeasuredValue IS NOT NULL` |
| Quyết định Ok/NG do máy tính | 0% | 100% | Audit detail có `threshold_applied` |
| Phiếu QC có cỡ mẫu thực tế | 0% | 100% | `SampleSizeActual IS NOT NULL` |
| Hạng mục NG có ảnh bằng chứng | n/a | 100% | `Status='Ng' AND PhotoBlobId IS NULL` → phải = 0 |
| NC có disposition trong 24h | — | ≥ 95% | `ClosedAt − OpenedAt` |
| Nhượng bộ đủ 4 trường 8.7.2 | 0% | 100% | Ràng buộc `CHECK` ở **schema**, không chỉ UI |
| Thời gian dựng hồ sơ audit 1 WO | ghép tay | < 30 giây | Một nút → một PDF |
| Giá trị đo gắn thiết bị còn hạn | 0% | 100% | Sau Đợt 4 — join sổ thiết bị |

---

## 11. Rủi ro & STOP-gate

### Rủi ro triển khai

- **Gánh nặng nhập liệu ở xưởng.** Bắt nhập số đo cho 34 hạng mục × 3 công đoạn là
  cách chắc chắn để người kiểm bịa số. *Giảm thiểu:* làm **m8 trước Đợt 1**, và chỉ
  bắt buộc số đo ở `CheckType='Measure'` — theo dữ liệu hiện tại phần lớn là `Visual`.
- **Sửa hợp đồng dữ liệu sau khi có dữ liệu thật.** Càng để lâu càng đắt. Ba giới hạn
  phải đóng băng vào dòng ngay từ Đợt 1, không thêm sau.
- **Số AQL sai bậc.** Bậc AQL là quyết định **thương mại**, không phải kỹ thuật — sai
  bậc thì hoặc chặn oan hàng tốt, hoặc thả hàng lỗi. **Phải có chữ ký QA trước khi code Đợt 3.**
- **Phạm vi phình sang QMS doanh nghiệp.** Ranh giới PA-3 phải ghi vào sổ tay chất
  lượng, không chỉ ghi trong báo cáo này.

### STOP-gate — dừng và hỏi Henry

- Bất kỳ thay đổi nào khiến `WoTraceSnapshot` bị ghi đè hoặc cập nhật tại chỗ —
  **đây là lằn ranh không được vượt**
- Thêm trạng thái WO mới cho luồng NC/disposition → sửa `P10.7-WO-STATE-CONTRACT.md`
  và có chữ ký **trước**, rồi mới code
- Chạy migration lên DB live
- Chốt bậc AQL mà chưa có xác nhận của QA CCL
- Đụng `src/CCL.MES.*` khi baseline còn ở chế độ chỉ-đọc

---

## Phụ lục A — Bằng chứng SQL

Chạy trên bản sao CHỈ-ĐỌC của `data/ccl_mes.db`. Toàn bộ số liệu tái tạo được từ đây.

```sql
-- M1 · không có cột giá trị đo
PRAGMA table_info(WoIpqcCheckItems);
PRAGMA table_info(WoQcCheckItems);

-- m7 · kế hoạch kiểm theo spec bị bỏ hoang
SELECT (SELECT COUNT(*) FROM SpecQcWindows)  AS windows,
       (SELECT COUNT(*) FROM QcCriteria)     AS criteria,
       (SELECT COUNT(*) FROM SpecQcCaptures) AS captures;
-- → 0 | 0 | 0

-- m8 · thư viện không phân biệt công đoạn
SELECT Ipqc, Fqc, Oqc, COUNT(*) n FROM CheckItemLibraries GROUP BY Ipqc, Fqc, Oqc;
-- → 1 | 1 | 1 | 59   (một nhóm duy nhất)

-- M5 · AQL lấp đầy 100% nhưng lưu dạng chuỗi dấu phẩy
SELECT ItemId, Severity, Aql, Sampling, CheckType FROM CheckItemLibraries LIMIT 8;
-- → "0,65" · "FAI 100% + AQL 0.65" · Visual

-- M4 · hồ sơ nhượng bộ thực tế
SELECT WorkOrderId, SpecialAcceptReason, IpqcSubmittedBy,
       QaOutcome, QaReason, QaApprovedBy
FROM WoIpqcChecks WHERE Judgment = 'SpecialAccept';

-- m15 · ảnh bằng chứng
SELECT COUNT(*) FROM WoQcPhotos;   -- → 0
SELECT Status, COUNT(*) FROM WoQcCheckItems GROUP BY Status;

-- Lô NVL còn cách ly
SELECT Status, COUNT(*) FROM MaterialLots GROUP BY Status;   -- → Quarantine | 27

-- Phiếu IQC chưa kết luận
SELECT Result, COUNT(*) FROM IqcInspections GROUP BY Result;
-- → Pending 23 | Pass 1 | Fail 1
```

Kiểm chứng **M2**:

```bash
grep -rn "QcThresholdResolver" --include="*.cs" . | grep -v "/obj/" | grep -v "/bin/"
# 13 lời gọi — tất cả trong QcThresholdResolverTests.cs
# 1 tham chiếu <see cref> trong chú thích XML
# 1 định nghĩa lớp
# 0 lời gọi trong mã production
```

---

## Phụ lục B — Schema đề xuất

Hình dạng đích, **không** phải mã cuối cùng. Thiết kế chi tiết là việc của
`mes-quality-architect` ở pha ANALYZE của từng đợt.

```
// ── Đợt 1 · bổ sung vào CẢ HAI bảng hạng mục ───────────────────
WoIpqcCheckItem / WoQcCheckItem
  + MeasuredValue   double?
  + Uom             string?
  + LowerLimit      double?   // ĐÓNG BĂNG lúc materialize
  + UpperLimit      double?   // KHÔNG đọc lại từ master data
  + Target          double?
  + CheckType       string    // Visual | Measure | Functional
  + MeasuredBy      string?
  + MeasuredAt      DateTime?

// ── Đợt 2 · vòng không phù hợp ─────────────────────────────────
NonConformance
  Source            // IQC | IPQC | FQC | OQC | COMPLAINT
  DefectCode        // khoá đã sẵn trong thư viện v5
  Severity          // Critical | Major | Minor
  QtyAffected · Uom
  WorkOrderId? · WoLegId? · MaterialLotId? · CheckItemId?
  DetectedBy · DetectedAt · Description
  Status            // Open | Dispositioned | Closed

Disposition
  NonConformanceId
  Action            // Rework | Scrap | UseAsIs | Return | Regrade
  QtyByAction · DecidedBy · DecidedAt · Reason
  // khi Action = UseAsIs → BẮT BUỘC 4 trường 8.7.2:
  CustomerNotified      bool
  CustomerRef           string?
  ConcessionScope       string     // lô | đơn hàng | tới ngày…
  ConcessionValidUntil  DateTime?

// ── Đợt 3 · lấy mẫu ────────────────────────────────────────────
WoQcCheck
  + LotSize · SampleSizeActual
  + InspectionLevel · AqlBySeverity
  + DefectsFound_Critical / _Major / _Minor
  + AcceptNumber · RejectNumber      // tính từ ISO 2859-1

// ── Đợt 4 · thiết bị đo & năng lực ─────────────────────────────
MeasuringDevice
  Code · Name · Type · Resolution · Uom
  CalibrationIntervalDays
  LastCalibratedAt · NextDueAt · CertificateRef
  Status            // Active | OutOfCalibration | Retired

InspectorQualification
  UserId · Stage · ProcessLine · CustomerCode?
  CertifiedAt · ValidUntil · CertifiedBy
```

> **Một nguyên tắc cho toàn bộ schema trên:** mọi giới hạn, ngưỡng, bậc AQL và cỡ mẫu
> **phải được đóng băng vào dòng dữ liệu tại thời điểm tạo phiếu**, đúng cách mà
> `ProfileSnapshotJson` đang làm. Không đọc lại từ master data khi hiển thị hồ sơ cũ.
> Đây là thứ phân biệt một hệ chịu được audit với một hệ chỉ đẹp trên dashboard — dự
> án đã làm đúng ở phần bộ hạng mục; phần ngưỡng chỉ cần đi theo cùng khuôn.

---

*Các số liệu vận hành phản ánh trạng thái DB tại thời điểm truy vấn và sẽ thay đổi khi
hệ chạy thật; các phát hiện về mã nguồn cần soát lại sau mỗi đợt. Bậc AQL, danh mục
thiết bị đo và ranh giới với QMS doanh nghiệp cần xác nhận của QA CCL Design Vietnam
trước khi đưa vào thực thi.*
