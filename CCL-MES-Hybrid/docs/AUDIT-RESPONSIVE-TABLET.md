# Audit responsive — chuẩn bị cho tablet ở xưởng

> Ngày 2026-08-26. Phương pháp: **lái app MAUI thật** (`CCL MES.app`, dữ liệu
> thật, đã đăng nhập), thu cửa sổ qua các bề rộng 1512 → 1024 → 900 → 834 → 768
> → 700pt, chụp và đo từng bước; đối chiếu với phân tích tĩnh `app.css`/`ix.css`.
> Không suy đoán: mọi phát hiện dưới đây đều có ảnh hoặc số đếm kèm theo.

---

## 0. Vì sao audit này cần thiết

Sweep thiết kế trước đó (L56/L57) quét theo **rule CSS**. Nó bắt được token ma,
vùng chạm, và cỡ chữ mù density. Nó **không** bắt được lỗi bố cục ở bề rộng hẹp,
vì lỗi đó chỉ hiện ra khi **render thật ở kích thước thật**. Đây là hai vùng mù
khác nhau, và gate tĩnh không thay được cho việc mở app ra nhìn.

Bối cảnh sản phẩm: app sẽ được cài trên **tablet dùng dưới xưởng**. Tablet công
nghiệp phổ biến 10–12", landscape ~1280×800, **portrait ~800×1280**. Ở portrait
thì bề rộng khả dụng chỉ còn ~800pt — vùng mà audit này cho thấy app đang vỡ.

## 1. Quy mô

| | |
|---|---|
| Màn `.razor` (Pages + Shared) | **105** |
| Thẻ `<table>` trong markup | **42** |
| Tổng luật responsive (`@media` + `@container`) | **40** |
| Trong đó `@media` | **8** (1 print · 1 reduced-motion ⇒ **6 thật sự cho layout**) |
| `container-type` khai báo | **7** |

42 bảng / 40 luật responsive — và phần lớn 40 luật đó nhắm vào **form và lưới
thẻ**, không phải bảng. Độ phủ responsive thực tế cho bảng thấp hơn con số này
nhiều (xem §3).

---

## 2. LỖI A — Thanh chrome toàn cục vỡ dưới ~900pt · **NGHIÊM TRỌNG**

Đây là lỗi nặng nhất vì nó nằm trên `TopBar.razor` — **mọi màn hình đều có**.

### Triệu chứng (đo thật)

| Bề rộng cửa sổ | Hiện tượng |
|---|---|
| 1024pt | bình thường |
| 900pt | bắt đầu chật |
| 834pt | `CCL DESIGN` xuống 2 dòng; nhãn `USER`/`SHIFT` bị cắt |
| **768pt** | `A · 06:00–14:00` xuống **3 dòng**, tràn **xuống dưới** dải navy |
| **700pt** | tràn dọc **+ cắt ngang**: đọc ra `6:00–` / `4:00` (mất số 0 đầu) |

Người vận hành mất luôn nhãn cho biết ba con số đó là gì.

### Root cause (đã truy tới dòng)

```css
.app-topbar {
    display: flex;
    height: 56px;        /* ← px CỨNG, không dùng --ix-header-h (48px) */
    gap: 16px;
    padding: 0 24px;
}
.app-topbar-right { display: flex; align-items: center; gap: 22px; }
.tb-block { line-height: 1.25; }   /* ← không nowrap, không kiểm soát co giãn */
```

Bốn vấn đề chồng nhau:

1. **`height: 56px` cứng** — và nó **mâu thuẫn với `--ix-header-h: 48px`** đang
   dùng cho `.app-nav-brand`. Hai nguồn sự thật cho cùng một chiều cao chrome,
   nên rail và topbar vốn đã không thẳng hàng.
2. **`.tb-block` không có `white-space: nowrap`** ⇒ khi hết chỗ, *giá trị* xuống
   dòng thay vì cả khối bị ẩn/rút gọn.
3. Container cao cố định + `align-items: center` ⇒ nội dung 3 dòng **tràn ra
   ngoài** dải nền, không có `overflow` xử lý.
4. **Không có một luật responsive nào** cho `.app-topbar` — 0 `@media`,
   0 `@container`.

### Phụ: chrome toàn cục chưa token hoá

Trong riêng khối `.app-topbar`/`.tb-*`: `gap: 16px`, `gap: 22px`,
`padding: 0 24px`, `height: 56px` — đều là px cứng ngoài thang `--sp-*`. Và
toàn app đang dùng **8 giá trị `border-radius`** khác nhau (2·3·4·10·11·12·14·16px)
trong khi thang chỉ có 4 bậc (`--r-sm/md/lg/pill`).

---

## 3. LỖI B — Bảng rộng không có xử lý responsive

Kiểm kê mọi bảng có `min-width` lớn, đối chiếu với luật `@container` thực sự
tác động **lên bảng đó** (không tính luật chỉ đổi form/lưới thẻ cạnh bên):

| Bảng | `min-width` | Có luật cho BẢNG? | Bề mặt |
|---|---|---|---|
| `.prepress-table` | **1400px** | **KHÔNG** — rule 720px chỉ đổi `.prepress-row-form` | **XƯỞNG** ⚠ |
| `.accounts-table` | 1200px | **KHÔNG** — 0 `@container` | Settings |
| `.qclib-grid` | 1180px | **KHÔNG** — rule 900px chỉ đổi `.qclib-form-ticks` | QA / văn phòng |
| `.audit-table` | 1100px | **KHÔNG** — 0 `@container` | Văn phòng |
| `.backup-table` | 900px | **KHÔNG** — 0 `@container` | Settings |
| `.trace-grid` | 1100px | **KHÔNG** — rule 700px chỉ đổi `.trace-kv-grid` | Văn phòng |
| `.trace-prod` | 1100px | **KHÔNG** — như trên | Văn phòng |
| `.trace-tools` / `.trace-items` | 760/640px | dưới ngưỡng tablet ngang | Văn phòng |
| `.semi-table` | 900px | CÓ | Xưởng |

**Nguy hiểm nhất: `.prepress-table` 1400px trên bề mặt xưởng.** Trên tablet
portrait ~800pt, người đứng máy phải **cuộn ngang qua một bảng rộng 1400px** để
xác nhận công đoạn chế bản. Cuộn ngang khi đeo găng, cầm tablet một tay, là cách
chắc chắn để bấm nhầm dòng.

Quy luật rút ra: **màn làm sau (có container query) thì sập card đúng; màn làm
trước thì không**. Đây không phải quyết định thiết kế — đó là **nợ tích luỹ theo
thời gian**, và không có gì chặn nó lớn thêm.

---

## 4. LỖI C — Không có THANG BREAKPOINT

App đang dùng **12 ngưỡng khác nhau**, không theo hệ nào:

```
480 · 520 · 560 · 600 · 640 · 700 · 720 · 900 · 1000 · 1080 · 1081 · 1400
```

- Bốn ngưỡng gần như trùng nhau ở vùng nhỏ: **600 · 640 · 700 · 720**
- **1080 và 1081** — hai ngưỡng cách nhau 1px
- Mỗi màn tự chọn số của mình, không ai dùng chung

Đây **chính xác** là câu chuyện đã xảy ra ba lần rồi trong repo này:
màu trước L37 (hex rải rác) → size trước L41 → typography trước L49
(*"103 cỡ chữ khác nhau cho 527 khai báo"*). Lần này là **breakpoint**.

Hệ quả cụ thể: không ai trả lời được câu "app hỗ trợ tablet nào" vì không có
định nghĩa nào về "tablet" trong code.

---

## 5. LỖI D — Bố cục card lãng phí chiều dọc

Panel MATERIAL sập card đúng ở bề rộng hẹp (cơ chế hoạt động). Nhưng **nội dung
card thì chưa được thiết kế cho dạng card**:

1. **Trùng lặp nguyên văn** — `MATERIAL CODE: 30030328` rồi ngay dưới
   `MATERIAL (SYSTEM): 30030328`. Cùng một chuỗi, hai nhãn, hai dòng.
2. **Ô rỗng vẫn chiếm chỗ đầy đủ** — `SOURCE IQC LOT: —` và
   `ACTUAL AT MACHINE: —` mỗi cái vẫn tốn một cặp nhãn + giá trị (~62px) chỉ để
   nói "không có dữ liệu".
3. Kết quả: một vật tư ≈ **197pt** chiều cao. Năm vật tư ≈ **985pt** — **vượt
   quá một màn hình tablet 884pt**, chỉ để xác nhận 5 dòng.

Ở dạng bảng, cùng nội dung đó chiếm ~72px/dòng. Việc sập card làm chiều cao
**tăng gần 3 lần** vì nó dịch từng ô 1:1 thay vì thiết kế lại thông tin.

---

## 5b. LỖI E — Khung chiếm gần một nửa màn tablet

Đo trên harness dựng đúng `.app-shell`:

| viewport | rail trái | cột tra cứu phải | **chrome tổng** | còn lại |
|---|---|---|---|---|
| 1280pt | 256px (20%) | 320px (25%) | **45%** | 55% |
| 1024pt | 256px (25%) | 320px (31%) | **56%** | 44% |
| 768pt | 256px (33%) | 320px (42%) | **75%** | 25% |

Cả hai đều là **cột cố định không bao giờ nhường**: rail khai
`grid-template-columns: var(--ix-rail-w) 1fr`, cột phải khai
`minmax(0,1fr) 320px`.

Rail có sẵn cơ chế thu gọn (`data-rail="collapsed"`, 256→64px) — cột phải thì
không có gì. Ba panel trong đó (Spec Quick Ref · BOM Summary · Audit Trail) là
tài liệu **TRA CỨU**: cần khi cần, không cần thường trực.

**Đã sửa:** thêm tay nắm thu gọn, sao đúng khuôn rail — người dùng **chủ động**
đóng/mở (iX #5: khung là hằng số, app không tự đóng dưới chân người đang thao
tác). Ở `--bp-tablet-p` cột xếp xuống dưới và tay nắm tự ẩn vì lúc đó không còn
hai cột để đổi.

**Còn nợ:** trạng thái đóng/mở chưa được lưu — mở lại màn là về mặc định. Rail
trái lưu qua `localStorage` (`js/density.js` là khuôn); cột phải nên theo.

---

## 6. PHƯƠNG ÁN CẢI TIẾN

### Đợt 1 — nền tảng (làm trước, mọi thứ sau dựa vào nó)

**1.1 Thang breakpoint.** Đặt vào `:root` cùng chỗ với các thang khác, đặt tên
theo THIẾT BỊ chứ không theo số:

```css
--bp-phone:    480px;   /* điện thoại, quét cầm tay */
--bp-tablet-p: 768px;   /* tablet DỌC — bề mặt xưởng chính */
--bp-tablet-l: 1024px;  /* tablet NGANG */
--bp-desk:     1280px;  /* màn bàn làm việc */
--bp-wide:     1600px;  /* màn rộng / màn treo tường */
```

> CSS chưa cho dùng `var()` trong điều kiện `@media`/`@container`. Nên thang này
> là **hợp đồng bằng văn bản + gate**, không phải cơ chế runtime: gate cho phép
> đúng 5 con số này và từ chối số thứ 6. Cách này vẫn diệt được vấn đề gốc
> (mỗi màn tự chế một ngưỡng), giống hệt cách L41 diệt cỡ chữ tự chế.

**1.2 Gộp 12 ngưỡng về 5.** Ánh xạ: 480→480 · 520/560/600/640→768 ·
700/720→768 · 900/1000→1024 · 1080/1081→1280 · 1400→1600. Đây là thay đổi cơ
học, đo trước/sau từng màn bị ảnh hưởng.

**1.3 Sửa `.app-topbar`** — rủi ro thấp, lợi ích cao nhất vì nó là chrome toàn cục:
- `height: 56px` → `min-height: var(--ix-header-h)` (một nguồn sự thật, hết lệch
  với rail), bỏ chiều cao cố định để nội dung không bị cắt.
- `.tb-block { white-space: nowrap; }` — giá trị không xuống dòng.
- Dưới `--bp-tablet-l`: ẩn nhãn `.tb-k`, giữ giá trị (`USER`/`SHIFT`/`TIME` là
  thừa khi đã quen); dưới `--bp-tablet-p`: gộp `SHIFT`+`TIME` vào một khối, hoặc
  đẩy `USER` vào menu người dùng.
- `gap`/`padding` qua `--sp-*`.

### Đợt 2 — bảng xưởng (theo thứ tự rủi ro cho người đeo găng)

**2.1 `.prepress-table`** — ưu tiên 1, bề mặt xưởng, 1400px. Áp khuôn sập-card
đã có sẵn và đã chứng minh chạy tốt ở `.ipqc-mat-row` (dùng `data-label::before`).
**Không phát minh khuôn mới** — sao chép khuôn đang chạy.

**2.2 `.qclib-grid`** — 1180px, 86 mục. Ở đây sập card là sai (bảng ma trận
tick nhiều cột); phương án đúng là **cột dính + ẩn cột phụ theo breakpoint**:
giữ `ItemId` + `Nội dung` dính trái, các cột phương pháp cuộn ngang trong vùng
riêng. Đây là pattern "frozen column", khác với sập card.

**2.3 `.audit-table` · `.accounts-table` · `.backup-table`** — bề mặt văn phòng,
ưu tiên thấp. Cuộn ngang ở đây chấp nhận được; chỉ cần đảm bảo có chỉ báo cuộn.

### Đợt 3 — thiết kế lại NỘI DUNG card (không chỉ bố cục)

**3.1 Bỏ trùng lặp:** `MATERIAL CODE` và dòng đậm trong `MATERIAL (SYSTEM)` là
cùng một dữ liệu. Ở dạng card chỉ hiện một lần.

**3.2 Ẩn ô rỗng:** `SOURCE IQC LOT: —` và `ACTUAL AT MACHINE: —` không nên
render khi rỗng. Quy tắc chung cho MỌI card: **ô không có dữ liệu thì không
chiếm chỗ** — trừ khi sự vắng mặt đó tự nó là thông tin (khi đó phải nói rõ lý
do, theo đúng tinh thần `OeePerformance.UnavailableReason` của Đợt 1 C3).

**3.3 Mục tiêu đo được:** một vật tư ≤ **120pt** ở dạng card (hiện ~197pt) ⇒ 5
vật tư vừa một màn tablet.

### Đợt 4 — chặn tái phát

Xem §7.

---

## 7. CƠ CHẾ CHẶN TÁI PHÁT

Ba lớp, theo đúng mô hình repo đang dùng:

**7.1 Gate `gate-breakpoint-scale.sh`** — mọi `@media`/`@container` chỉ được
dùng 5 giá trị trong thang. Ratchet đi xuống từ 12 ngưỡng hiện tại. Đây là bản
sao trực tiếp của cách L41/L49 diệt cỡ chữ tự chế.

**7.2 Gate `gate-table-responsive.sh`** — bảng khai `min-width` ≥ `--bp-tablet-l`
(1024px) **phải** có một luật responsive nhắm vào chính nó. Ratchet: **6** vi phạm
hiện tại (đếm bằng chính gate, không chép tay — luật L57).

**7.3 Bài học + skill** — L58 trong `LESSONS-LEARNED.md`, và một mục bắt buộc
trong skill `cmes-design-tokens`: **màn hình mới phải được chụp ở
`--bp-tablet-p` (768pt) trước khi PR**, không chỉ ở hai density.

> Vì sao cần cả (7.3): gate tĩnh **không** bắt được lỗi bố cục — lỗi topbar ở §2
> không gate nào phát hiện được, phải mở app ra ở 768pt mới thấy. Gate chặn được
> nợ MỚI; ảnh chụp mới chứng minh màn hình dùng được.

---

## 8. Điều audit này CHƯA phủ

Nói rõ để người sau không tưởng là đã xong:

- Mới lái **4 màn** trong 105: Work Orders — Scan, IPQC — In-process, Machine
  Dashboard, QC Library. Kết luận về các bảng khác dựa trên **phân tích tĩnh**,
  chưa mở từng cái ra nhìn.
- Chưa test **portrait thật** (800×1280) — mới test bề rộng hẹp ở cửa sổ ngang.
- Chưa test với **`--ui-scale` > 1**, mà đó là tình huống rất thật ở xưởng
  (người lớn tuổi phóng to chữ). Bề rộng hẹp + scale lớn sẽ vỡ sớm hơn nhiều.
- Chưa test **cảm ứng thật** — chuột và ngón tay đeo găng không giống nhau.
