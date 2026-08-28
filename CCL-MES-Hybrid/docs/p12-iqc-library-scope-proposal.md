# P12 — Thư viện tiêu chuẩn kiểm tra NVL cho IQC (scope proposal)

> **Trạng thái:** chờ Henry duyệt trước khi code.
> **Nguồn dữ liệu:** `IQC_Master_Tieu_chuan_kiem_tra_NVL.xlsx` — tổng hợp
> 19/08/2026 từ **809 file** spec gốc (366 bản R01 + 443 bản R03).
> **Work-class:** W4 (chất lượng) + W1 (schema) + W5 (UI).
> **Skill bắt buộc:** `cmes-audit-emit` · `cmes-migration-abc` · `cmes-i18n-parity`.

---

## 1. Vấn đề

IQC ticket có **5 bước**, chỉ bước 1 được dựng thật:

| # | Nhãn | Trạng thái hôm nay |
|---|---|---|
| 1 | Hồ sơ tài liệu | ✅ bảng chứng thư HSF/SGS |
| 2 | **Ngoại quan** | ❌ placeholder `"<nhãn> — open…"` |
| 3 | **Chức năng** | ❌ placeholder |
| 4 | Mã lỗi & kết luận | ❌ placeholder |
| 5 | Tra cứu lịch sử | ❌ placeholder |

Và hạng mục kiểm của IQC hiện là **văn bản tự do**: `IqcResultDetail.ItemName`
là `string`, không có nguồn master nào. Đây đúng lớp bệnh mà
[L61](LESSONS-LEARNED.md#l61) và [L63](LESSONS-LEARNED.md#l63) vừa gỡ ở
IPQC/FQC/OQC — chỉ là ở IQC nó chưa từng được dựng, nên chưa ai thấy.

## 2. Dữ liệu thật nói gì (đo, không phỏng đoán)

| | |
|---|---|
| Nguyên liệu khác nhau | **451** |
| Số spec (đã khử trùng) | **460** — 1 dòng là template rỗng |
| Hạng mục chuẩn hoá | **21** (gom từ 63 biến thể trong file gốc) |
| Nhóm | **10** — NL · NQ · KT · MT · BD · CU · XS · TL · BO · KH |
| Dòng chi tiết (spec × hạng mục) | **5 974** |

**Ba sự thật quyết định thiết kế:**

**2.1 — Tiêu chuẩn KHÁC NHAU theo từng nguyên liệu.** `BD-01` (độ bám dính)
có **60 tiêu chuẩn khác nhau** trên 451 nguyên liệu; `CU-01` có 11; `NQ-06`
có 10. Nguồn sự thật phải là sheet **`03_Chi tiet`** (khoá `Số SPEC × Mã HM`),
**không phải** `02_Master`. Cột "tiêu chuẩn phổ biến nhất" ở sheet 02 chỉ là
thống kê — dựng từ đó sẽ gán một ngưỡng chung cho mọi vật liệu, và sai đó
**vô hình**: màn hình vẫn đầy chữ, chỉ là chữ sai.

**2.2 — Tần suất là một chiều dữ liệu, không phải ghi chú.**

| Tần suất trong file gốc | Số dòng |
|---|---|
| All lot | 2 231 |
| Lấy mẫu theo AQL GII 0.4 | 1 828 |
| Kiểm mỗi tháng một lần | 1 334 |
| Đo tối đa 5 mẫu/lot | 509 |

**2.3 — Khoá scope là NGUYÊN LIỆU, không phải công đoạn.** IPQC resolve
`routing → QC line` bằng khớp tiền tố (15 luật cho riêng PRESS_CNC). IQC thì
`nguyên liệu → Số SPEC → hạng mục` là **tra thẳng** — đơn giản hơn hẳn, không
cần resolver.

## 3. Quyết định đã chốt (Henry, 2026-08-28)

### D1 — Kiểm **tất cả hạng mục trên MỌI lô NVL về**

Ghi đè tần suất trong file gốc: 1 334 dòng ghi "kiểm 1 lần/tháng" (RoHS XRF,
hồ sơ HSF, peel test) nay **kiểm theo từng lô**.

> ⚠ **Cần QA ghi nhận bằng văn bản.** Đây là thay đổi **tiêu chuẩn kiểm tra**,
> không phải thay đổi phần mềm. File spec gốc do NCC/QA ban hành ghi tần suất
> tháng; hệ thống sẽ yêu cầu chặt hơn. Chặt hơn thì không rủi ro tuân thủ,
> nhưng làm tăng tải kiểm và cần năng lực đo (máy XRF, máy peel) đáp ứng
> **mọi lô** thay vì mỗi tháng một lần.
>
> **Vẫn LƯU cột `SourceFrequency`** nguyên văn từ file gốc. Ghi đè chính sách
> không được xoá dấu vết tiêu chuẩn gốc nói gì — khi có audit, phải trả lời
> được "spec ghi tháng, ta chủ động kiểm từng lô", chứ không phải "không biết
> spec ghi gì".

### D2 — **Bảng riêng, cùng hình dạng** với `CheckItemLibrary`

Không nhồi vào `CheckItemLibrary`. Lý do: khoá scope khác hẳn (nguyên liệu vs
QC line), và tiêu chuẩn nằm ở **dòng chi tiết** chứ không ở dòng master — nhồi
hai mô hình vào một bảng là mời đúng loại nhầm lẫn mà [L61](LESSONS-LEARNED.md#l61)
đã trả giá. Giữ **cùng hình dạng** để tái dùng được khuôn materialize, khuôn
đóng băng, và khuôn UI tab-nhóm vừa dựng cho FQC/OQC.

## 4. Kiến trúc đề xuất

### 4.1 Ba bảng

```
IqcCheckItemLibrary          21 dòng — danh mục hạng mục chuẩn hoá
  ItemId (NL-01 · NQ-02 …)   natural key, unique
  GroupCode / GroupLabel(En) NL · NQ · KT · MT · BD · CU · XS · TL · BO · KH
  ItemVi / ItemEn            hạng mục chuẩn hoá
  Sort · Active

IqcMaterialSpec              460 dòng — nguyên liệu ↔ spec ↔ NCC
  SpecNo (CCL-SPEC-QCxxx)    natural key, unique
  MaterialCode               tên NVL trong List
  MaterialCodeIfs            mã IFS (7000xxxx) — xem §5
  SupplierName · Revision
  Active

IqcSpecItem                  5 974 dòng — TIÊU CHUẨN THEO TỪNG NGUYÊN LIỆU
  SpecNo + ItemId            unique (SpecNo, ItemId)
  AcceptanceVi / AcceptanceEn   ← nguồn sự thật, KHÁC nhau theo vật liệu
  MethodVi / MethodEn           cột "Ghi chú" của file gốc
  SourceFrequency               nguyên văn — xem D1
  Sort
```

### 4.2 Đóng băng vào ticket

Y hệt IPQC/FQC/OQC (Nguyên tắc IV của hiến pháp): lúc tạo ticket, resolve
`RawMaterial → SpecNo → IqcSpecItem[]` rồi **đóng băng** nhãn · tiêu chuẩn ·
phương pháp · nhóm — **cả hai ngôn ngữ** — vào bản ghi ticket. Sửa master data
về sau **không hồi tố** ticket đã ký.

`IqcResultDetail` cần thêm: `ItemKey` · `GroupLabel(En)` · `Label(En)` ·
`Acceptance(En)` · `Method(En)`. Cột `ItemName` (văn bản tự do) **giữ lại** cho
ticket cũ — không xoá dữ liệu lịch sử.

### 4.3 Mục 2–5 lấy hạng mục từ đâu

| Mục | Nhãn thật | Nhóm nguồn | Hạng mục |
|---|---|---|---|
| 1 | Hồ sơ tài liệu | `MT-02` | 1 |
| **2** | **Ngoại quan** | `NL` + `NQ` | **7** |
| **3** | **Chức năng** | `KT` · `BD` · `CU` · `XS` · `TL` · `BO` · `MT-01` · `MT-03` | **12** |
| 4 | Mã lỗi & kết luận | — | — |
| 5 | Tra cứu lịch sử | — | — |

**Mục 4 và 5 KHÔNG phải danh sách hạng mục.** Nhãn thật của chúng là *"Mã lỗi
& kết luận"* và *"Tra cứu lịch sử"* — một cái ghi mã NG + phán định cuối, một
cái tra lô cũ cùng nguyên liệu. Cả hai là **khung nhìn dẫn xuất**. Nhồi hạng
mục vào đó là nhân đôi đúng lỗi mà [L63](LESSONS-LEARNED.md#l63) vừa gỡ khỏi
FQC/OQC (trộn metadata + ô chữ ký vào lưới OK/NG).

`KH-01` ("Hạng mục khác", 3 spec) đi vào mục 3 cùng nhóm KH.

### 4.4 UI

Tái dùng **nguyên** khuôn vừa dựng cho FQC/OQC: tab nhóm một tầng → bảng
`# · ITEM · METHOD · SPEC · VERDICT`, dùng lại bộ class `ipqc-*`, không đẻ CSS
mới. Mục 2 hiện tab của nhóm NL/NQ; mục 3 hiện tab của các nhóm còn lại.

## 5. Chất lượng dữ liệu — kế hoạch import

Sheet `05_Doi chieu & canh bao` có **129 cảnh báo**:

| Loại | Số | Xử lý |
|---|---|---|
| Lệch tên nguyên liệu | 95 | **47 trong đó là file spec ghi THÊM mã IFS** `(70000076)` — **không phải lỗi**, đó là bản đầy đủ hơn. Tách mã IFS ra cột `MaterialCodeIfs` ⇒ có luôn khoá nối sang IFS. 48 dòng còn lại là lệch tên thật, cần Ops đối chiếu. |
| Có trong List, không có file spec | 28 | Gồm tiêu chuẩn **thành phẩm** (BOSE · CABLE WRAP · DESAY · JOHNSON · NETGEAR · SERENA) xếp nhầm vào danh sách NVL. **Loại khỏi import IQC.** |
| Trùng số spec với template rỗng | 4 | Loại. |
| File không đọc được bảng hạng mục | 2 | Loại, ghi log. |

Importer **idempotent, upsert theo natural key, KHÔNG xoá** (DR-1), và **in
probe** `[seed] iqc_library items=21 specs=NNN spec_items=NNNN skipped=NN`.

## 6. Câu hỏi còn mở — cần Ops/QA trả lời

| # | Câu hỏi | Vì sao quan trọng |
|---|---|---|
| ~~Q1~~ | ~~48 dòng lệch tên thật~~ | ✅ **Henry 2026-08-28: giữ nguyên, không cần bổ sung** — xem §10 |
| ~~Q2~~ | ~~Nguyên liệu không có spec thì ticket hiện gì?~~ | ✅ **Henry 2026-08-28: ma trận tiêu chuẩn + CRUD theo mã** — xem §10 |
| ~~Q3~~ | ~~Khoá nối `RawMaterial` ↔ spec là tên hay mã IFS?~~ | ✅ **ĐÃ TRẢ LỜI 2026-08-28 bằng đo thật** — xem §9 |
| Q4 | D1 đã được QA ghi nhận bằng văn bản chưa? | Xem cảnh báo ở §3 |

## 7. Tiêu chí nghiệm thu (đo được)

- [ ] 21 hạng mục · 460 spec · 5 974 dòng chi tiết import được, probe khớp số.
- [ ] Ticket của một nguyên liệu bất kỳ hiện **đúng bộ hạng mục của spec đó**,
      với **tiêu chuẩn riêng của nguyên liệu đó** — kiểm chứng bằng `BD-01`
      trên ≥3 nguyên liệu có 3 tiêu chuẩn khác nhau.
- [ ] Đổi cờ EN/VI ⇒ nhãn · tiêu chuẩn · phương pháp đổi theo; thiếu bản dịch
      thì rơi về VI, **không bao giờ để ô trống**.
- [ ] Sửa master data **không** đổi ticket đã đóng băng.
- [ ] Mục 2 hiện 7 hạng mục, mục 3 hiện 12 — trên nguyên liệu có đủ spec.
- [ ] Test **ĐỎ** khi hoàn nguyên fix (Nguyên tắc I + II của hiến pháp).
- [ ] `gate-all.sh` 19/19; ảnh chụp ở 768px cho PR chạm `.razor`.

## 8. Việc KHÔNG nằm trong scope này

- Mục 4 (mã lỗi & kết luận) và mục 5 (tra cứu lịch sử) — khung nhìn dẫn xuất,
  scope riêng.
- Nhập/sửa master data IQC từ trong app (admin UI) — nay vẫn import từ file.
- Gắn kết quả IQC vào `MaterialLot` genealogy.

---

## 9. ĐÍNH CHÍNH — khoá nối, đo trên live 2026-08-28

Ảnh màn "Materials inspection ticket" của Henry cho thấy `Code IFS` trong MES
có dạng **`300xxxxx`**, kèm cột **`Mother code`**. Đo ngay trên live DB:

| Khoá nối | Khớp |
|---|---|
| `RawMaterials.MotherCode` = `IqcMaterialSpec.MaterialCode` | **352** chính xác · **356** khi bỏ hoa/thường |
| `RawMaterials.PartNo` = `IqcMaterialSpec.MaterialCodeIfs` | **0** |

**Tôi đã sai ở §Q3 và trong chú thích entity.** Mã `7xxxxxxx` trích từ file spec
**không phải** mã IFS của MES — hai hệ đánh số khác hẳn. Chưa biết nó là gì
(mã NCC? hệ cũ?); cột `MaterialCodeIfs` giữ lại nhưng **không dùng để resolve**
cho tới khi Ops xác nhận. Đổi tên cột phải chờ biết tên đúng — đặt sai lần hai
còn tệ hơn.

### Điều này đổi bước 2 thế nào

**Khoá nối: `MotherCode`.** Tra thẳng, không cần resolver mờ.

**Nhưng "không có spec" là ĐA SỐ, không phải ngoại lệ:**

| | |
|---|---|
| Mother code trong MES | **946** |
| Có spec IQC | **356** (38%) |
| **Không có spec** | **590** (62%) |

Mẫu chưa có spec: `TWP5050` · `AM 375 MBH` · `FDO NEW TBF WHITE HF-1*1.5` ·
`Mực trắng trong FD MP Medium`. Phần lớn là mực và màng.

Ở chiều ngược lại, 92/448 spec không khớp mother code nào — tên dạng
`#8616CH-100W` · `114329-DIN 1.4301` · `136860-AM LR 8 uncoating`, tức mã NCC
kèm mô tả. Chuẩn hoá chuỗi đơn giản **không** cứu được; cần Ops đối chiếu.

⇒ **Đường "chưa có tiêu chuẩn" phải là công dân hạng nhất của bước 2**, không
phải một banner dự phòng. Thiết kế lại Q2: ticket cho nguyên liệu chưa có spec
vẫn phải kiểm được (nhập tay như hôm nay) và **nói rõ vì sao trống**, chứ không
hiện màn hình rỗng.

---

## 10. Quyết định bổ sung (Henry, 2026-08-28) — sau khi có §9

### D3 — 590 nguyên liệu chưa có spec: dùng **MA TRẬN TIÊU CHUẨN**

Dữ liệu tự chia đôi rất sắc, không có vùng xám:

| | Hạng mục | Độ phủ |
|---|---|---|
| **Ma trận tiêu chuẩn** | **13** — NQ-06 · NQ-03 · NQ-05 · NL-01 · NQ-01 · NQ-04 · MT-02 · NQ-02 · BD-01 · MT-01 · CU-01 · KT-01 · XS-01 | **92–100%** |
| Riêng theo vật liệu | 8 — KT-02/03/04 · MT-03 · BD-02 · KH-01 · BO-01 · TL-01 | **0–6%** |

Nguyên liệu chưa có spec ⇒ materialize **13 hạng mục** của ma trận, tiêu chuẩn
lấy giá trị phổ biến nhất ở sheet `02_Master`.

> ⚠ **PHẢI ĐÁNH DẤU LÀ MẶC ĐỊNH, KHÔNG PHẢI TIÊU CHUẨN CỦA MÃ ĐÓ.** Đây chính
> là cái bẫy §2.1 cảnh báo: gán một ngưỡng chung cho mọi vật liệu. Với nguyên
> liệu **có** spec thì làm vậy là sai; với nguyên liệu **chưa** có spec thì có
> ma trận vẫn hơn không có gì — nhưng người kiểm phải **nhìn thấy sự khác biệt**.
> Ticket ghi rõ *"tiêu chuẩn mặc định — mã này chưa có spec riêng"*, và bản ghi
> đóng băng mang cờ phân biệt. Không có cờ đó thì sáu tháng sau không ai phân
> biệt được hồ sơ nào kiểm theo spec thật, hồ sơ nào theo mặc định.

### D4 — Thêm / xoá dữ liệu tiêu chuẩn **theo mã**

Đây là bản **hiện thực đầu tiên** của contract `cmes-add-new-inline` (skill ghi
rõ: *"chưa có implementation/gate ở main"*). Theo đúng hình dạng số 2 của nó:
nút **`＋ Thêm hạng mục`** full-width ngay dưới hàng cuối của grid, không modal
toàn màn.

**Phân quyền — do PHẠM VI NHỚ quyết định, không do tiện tay.** Skill chốt sẵn:

| Phạm vi | Ai | Lưu ở đâu |
|---|---|---|
| Ad-hoc cho ticket này | Operator+ | bản ghi ticket |
| **Theo MÃ nguyên liệu** (lô sau tự có) | **Engineer / Supervisor / Admin** | `IqcSpecItem` |

Henry yêu cầu *"theo mã"* ⇒ **Engineer+**, KHÔNG phải Operator. RBAC-by-omission:
client chỉ dựng nút khi đủ quyền, server vẫn 403.

**XOÁ = `Active=false`, KHÔNG xoá vật lý.** Ba lý do: (a) DR-1 — importer
non-deleting, xoá cứng sẽ bị lần seed sau dựng lại, người dùng xoá hai lần rồi
bỏ cuộc; (b) ticket đã đóng băng tham chiếu tới hạng mục đó, xoá cứng làm mồ côi
bằng chứng; (c) audit phải trả lời được *ai xoá, khi nào* — Nguyên tắc IV.

Emit `IQC_SPEC_ITEM_ADDED` / `IQC_SPEC_ITEM_DEACTIVATED` (ai · mã NVL · ItemId).

### Q1 — 92 spec mồ côi: **giữ nguyên**

Henry: không cần bổ sung mother code. Chúng ở lại thư viện, chỉ là không nguyên
liệu nào trong MES resolve tới. Không xoá — có thể là mã đã ngừng dùng, và xoá
đi thì mất lịch sử tiêu chuẩn.
