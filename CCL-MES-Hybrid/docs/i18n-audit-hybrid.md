# i18n Audit — CCL-MES Hybrid (read-only)

> Auditor read-only. Mọi số liệu đếm thật bằng grep/python trên
> `CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor` (**84 file `.razor`**: 28 `Pages/` +
> 53 `Shared/` + 3 shell) + `CCL.MES.Hybrid.Client`. Không sửa code.
>
> **Xác nhận cấu trúc:** Hybrid.Razor có **0 file `.resx`**, **0 `IStringLocalizer`**
> → chuỗi UI hardcode thẳng trong `.razor`. Legacy Web vẫn giữ
> `../src/CCL.MES.Web/Resources/SharedResource{,.vi}.resx` làm nguồn key tham chiếu.

---

## 0. Phương pháp đếm (để tái lập)

- Trích **chuỗi visible**: text giữa `>...<`, và attr `placeholder|title|aria-label|alt`.
  Bỏ: class CSS, `data-testid`, biểu thức `@...` thuần, số/ký hiệu, code C# trong `@code`.
- Phân loại mỗi chuỗi: **VI** nếu chứa dấu tiếng Việt (bảng diacritic đầy đủ) · **EN**
  nếu chỉ chữ ASCII a–z · **NEUTRAL** nếu <2 chữ cái (số/ký hiệu).
- **Caveat (khai báo minh bạch):** token kỹ thuật ASCII (vd `PRINTED_SEMI`, `WO`,
  `FEFO`, `IPQC`, `BOM`) bị đếm là **EN** → %EN là *chặn trên*. Nhưng các màn
  SpecHub (SpecShowcardFull EN=131, MachineDashboard EN=70) là **văn xuôi tiếng
  Anh thật**, nên kết luận "phần lớn EN" không đổi chiều dù trừ token.

---

## 1. Inventory chuỗi hardcode

### Tổng hợp toàn app (grounded)

| Metric | Giá trị |
|---|---|
| File `.razor` | 84 (79 có chuỗi visible) |
| Chuỗi **EN** | **1436** |
| Chuỗi **VI** | **113** |
| Chuỗi NEUTRAL | 245 |
| **%EN / %VI** (trên EN+VI=1549) | **92.7% / 7.3%** |
| File MIXED (có cả EN & VI) | 12 |

→ **Định lượng "phần lớn tiếng Anh": ~93% chuỗi visible là EN.** (Ngược với comment
trong `LanguageCode.cs` "every Razor page hard-codes Vietnamese" — đúng ở thời điểm
P10.6b, nhưng đợt port SpecHub + P10.8/P10.10 "EN pass" đã lật ngược tỷ lệ.)

### Top file EN-nặng (văn xuôi Anh thật)

| File | EN | VI | Nguồn |
|---|---|---|---|
| Shared/SpecShowcardFull.razor | 131 | 0 | SpecHub-port |
| Pages/MachineDashboard.razor | 70 | 0 | P10.8 |
| Pages/ShopOrderHistory.razor | 61 | 0 | SpecHub-port |
| Shared/SpecShowcardEdit.razor | 59 | 0 | SpecHub-port |
| Pages/WorkOrders.razor | 47 | 0 | ops (đã EN-pass) |
| Pages/QcHistory.razor | 46 | 0 | P10.9 |
| Pages/SettingsAccounts.razor | 43 | 0 | Settings |
| Pages/SpecDetailPage.razor | 38 | 0 | SpecHub-port |
| Pages/NpiWorkCenters.razor | 37 | 0 | NPI |
| Pages/Home.razor | 30 | 0 | SpecHub-port (KPI) |

### Top 12 file "lẫn nặng" (MIXED — cả EN lẫn VI trong cùng màn)

| File | EN | VI | Ghi chú |
|---|---|---|---|
| Shared/SemiStockDashboard.razor | 13 | 51 | VI labels + token EN (`PRINTED_SEMI`…) — P11.5 |
| Pages/QcLibrary.razor | 6 | 11 | P10.9 lẫn |
| Shared/SpecDrawingsTab.razor | 9 | 5 | SpecHub-port + chú thích VI |
| Shared/IpqcDashboard.razor | 31 | 4 | ops VI + nhãn slot EN |
| Shared/LegsDashboard.razor | 4 | 16 | P11 routing (VI) + phase code EN |
| Pages/Login.razor | 14 | 4 | shell EN + vài chuỗi VI |
| Shared/FloatingWindow.razor | 12 | 3 | chrome EN + tooltip VI ("Phóng to…") |
| Shared/Modal.razor | 3 | 4 | primitive |
| Shared/TraceabilityDetailDialog.razor | 43 | 1 | gần như EN |
| Shared/NavMenu.razor | 21 | 1 | nav EN + 1 mục VI ("Kho bán thành phẩm") |
| Pages/QualityTraceability.razor | 19 | 1 | EN + 1 VI |
| Shared/GridSearchBox.razor | 1 | 2 | nhỏ |

**NavMenu là ví dụ điển hình lẫn ngay 1 dòng:** 21 mục EN ("Home", "Machine
Dashboard", "Work Orders — Scan"…) cạnh **1 mục VI "Kho bán thành phẩm"** (P11.5 mới).

---

## 2. Bản đồ nguồn gốc (vì sao lẫn)

Phân nhóm 84 file theo header comment / tên (bằng chứng đọc file):

| Nhóm | #file | Thiên hướng | Vì sao |
|---|---|---|---|
| **SpecHub-port** | 18 | **EN** | Port nguyên từ SpecHub (EN gốc), chưa dịch. Spec* + Home/ShopOrderHistory/TopBar/GridSearchBox… |
| **P10.8/10.9/10.10** | 8 | **EN** | Machine Dashboard/QcHistory/QMS/Home KPI — comment "P10.10 EN pass" cố tình ship EN |
| **Settings** | 9 | **EN** | SettingsAccounts/Backup/AuditLog/Appearance… viết EN (kể cả trang Appearance) |
| **P10.7/P11 dashboard vận hành** | 23 | **VI** | Prepress/Ipqc/Oqc/Fqc/Running/Setting/Legs/SemiStock — thao tác operator xưởng → VI |
| **Shell/other** | 26 | lẫn | Login/Modal/FloatingWindow/NavMenu/Npi* — nền tảng, lẫn theo thời điểm viết |

**Kết luận nguồn gốc:** hai "trường phái" chồng nhau — (1) đợt port SpecHub +
P10.8/10.10 giữ **EN**, (2) đợt dashboard vận hành P10.7/P11 viết **VI** cho operator.
Không có lớp i18n trung gian → mỗi PR chọn ngôn ngữ theo tác giả → **lẫn cố hữu**.

---

## 3. Đường đứt của picker ngôn ngữ (no-op — XÁC NHẬN)

**Trace `ILanguageService.Set` → `Changed` → subscriber:**

- `InMemoryLanguageService.Set()` (`Localization/ILanguageService.cs`): chỉ persist
  `_current` + bắn `Changed` khi giá trị đổi. **Không** đụng chuỗi UI.
- **Subscriber của `Changed`:** grep toàn repo → **duy nhất** `Pages/SettingsAppearance.razor:113`
  (`Language.Changed += OnLanguageChanged`). Handler chỉ set `_current` + `StateHasChanged`
  để **highlight lại radio card của chính trang đó** — KHÔNG re-render app, KHÔNG tra chuỗi.
- **Không có bảng tra chuỗi nào** (0 resx, 0 IStringLocalizer, không dict key→text
  cho UI chung). `LanguageCodeNames.LabelEn == LabelVi` (comment tự nhận "identical
  by accident").
- **Comment code tự thú nhận no-op** (`ILanguageService.cs`): *"Pure persistence layer
  this PR: actual string swapping requires the resx infrastructure deferred… only the
  Vietnamese strings exist today"* và *"Future resx-backed translation service **will**
  subscribe"* (thì tương lai → hiện chưa có).

→ **Picker = no-op cho ngôn ngữ UI. XÁC NHẬN giả thuyết.** Chọn "English" chỉ ghi
Preferences + hiện banner, chuỗi giữ nguyên (thực tế phần lớn đang EN sẵn nên đổi
sang EN lại càng "không thấy gì").

**Disclaimer banner:** CÓ — `SettingsAppearance.razor:71` hiện khi chọn English:
*"The English translation will apply after the app is updated. Your selection has been
saved."* (Bản thân banner + cả trang Appearance đều bằng **tiếng Anh** — trớ trêu cho
trang cài đặt ngôn ngữ.)

**Cờ ngôn ngữ trên TopBar:** **KHÔNG có** (grep `TopBar.razor` → 0). Khác legacy Web
(§8 có cờ topbar/login). Picker chỉ tới được qua `/settings/appearance`.

---

## 4. Hai phương án fix + khuyến nghị

### (A) resx + `IStringLocalizer` + `CultureInfo` (chuẩn .NET, mirror Web §8)
- **Ưu:** chuẩn ngành; tái dùng key từ `SharedResource.resx` legacy; format số/ngày/tiền
  theo culture "miễn phí"; tooling dịch quen thuộc.
- **Nhược:** nặng. WebView Hybrid không auto re-render khi đổi `CultureInfo` giữa
  phiên → vẫn phải tự bắn re-render toàn app (đúng vấn đề `Changed` hiện tại). Phải
  bọc mọi chuỗi `@Localizer["key"]` — sửa ~toàn bộ 79 file. Culture plumbing +
  `RootComponents` re-render trên MAUI BlazorWebView có gờ kỹ thuật.

### (B) Translation service nhẹ, dict-based
- Service `Current`-aware (đã có `LanguageService.Current` + event `Changed`); tra
  `T("key")` theo dict `{key → {vi, en}}`; component subscribe `Changed` →
  `StateHasChanged`. **Khớp đúng hạ tầng `Changed` đã tồn tại** (chỉ cần thêm
  subscriber thật + bảng chuỗi), không cần culture plumbing.
- **Ưu:** đòn bẩy nhỏ nhất để "bật" picker; đổi ngôn ngữ **live** trong phiên; dễ test
  (thuần .NET, không CultureInfo global). **Nhược:** tự lo format số/ngày nếu sau này cần;
  không dùng lại tooling resx.

### 👉 KHUYẾN NGHỊ: **(B) trước, để ngỏ đường lên (A)**
Lý do: (1) event `Changed` + `Current` **đã có sẵn** — (B) chỉ "nối dây" phần còn
thiếu (subscriber + dict), đúng như comment code dự tính. (2) App xưởng hầu như không
cần format tiền/ngày theo culture (số lô, qty là số thuần) → chưa cần (A). (3) Đổi
ngôn ngữ **live** (không cần restart) hợp operator hơn. Chỉ nâng lên (A) khi phát sinh
nhu cầu format culture-aware (báo cáo tiền tệ/ngày địa phương). Thiết kế dict key
**mirror namespace `SharedResource.resx`** để sau này migrate sang (A) không mất key.

---

## 5. Kế hoạch migrate theo ưu tiên (chỉ liệt kê)

Thứ tự theo tần suất operator gặp. Ước lượng key = số chuỗi visible unique/nhóm.

| # | Nhóm | #file | Ước key | Rủi ro chính |
|---|---|---|---|---|
| 1 | **TopBar + NavMenu + MainLayout** | 3 | ~30 | NavMenu đang lẫn (21 EN + 1 VI); thêm cờ TopBar (hiện chưa có) |
| 2 | **Login + shell** (Login/Modal/FloatingWindow/Connectivity) | ~6 | ~40 | chuỗi trong `@code` (tooltip, aria); Modal là primitive dùng chung |
| 3 | **WorkOrders + dashboard vận hành** (P10.7/P11: Prepress/Ipqc/Oqc/Fqc/Running/Setting/Legs/SemiStock) | 23 | ~350 | **`*ErrorLocaliser` wording bị xUnit lock** (7 file) — KHÔNG đổi qua i18n; chuỗi ghép động (đếm ngược hạn, "còn N ngày"); enum/token (`PRINTED_SEMI`) giữ nguyên |
| 4 | **Settings** (9 file) | 9 | ~180 | Appearance page tự tham chiếu ngôn ngữ; nhiều help-text dài |
| 5 | **SpecHub-port** (Spec* + Home + ShopOrderHistory) | 18 | ~450 | Khối EN lớn nhất (SpecShowcardFull 131); nhiều bảng/nhãn kỹ thuật; `SpecShowcard*` trong allow-list gate |
| 6 | **NPI + QMS + QcLibrary + còn lại** | ~25 | ~200 | grid headers, import modal |

**Tổng ước lượng key unique ≈ 800–1000** (từ ~1549 chuỗi visible, trừ trùng lặp).

### ⚠️ KHÔNG được đổi (giữ nguyên khi migrate)
- **`data-testid`** — bUnit dựa vào; đổi = vỡ test thầm lặng.
- **7 `*ErrorLocaliser.cs`** (SemiStock/Routing/Ipqc/WoQc/RunningSurface/Prepress/WorkOrder)
  — wording VI **bị xUnit lock**; đây là bank lỗi riêng, KHÔNG gộp vào i18n picker
  (chúng luôn VI theo thiết kế). Nếu muốn EN cho lỗi → việc riêng, không đụng test hiện có.
- **Chuỗi audit/log kỹ thuật** (`AuditAction` codes, detail JSON) — nghiệp vụ, không phải UI.
- **Token/enum hiển thị** (`PRINTED_SEMI`, `IPQC_APPROVED`, `WO`, `FEFO`, `BOM`) — mã
  kỹ thuật, giữ nguyên (chỉ dịch nhãn bao quanh).
- **Class CSS / route / key preference** (`cclmes.hybrid.language.v1`).

---

## Phụ lục — lệnh tái lập
```
# đếm EN/VI/mixed: python heuristic (mục 0) trên src/CCL.MES.Hybrid.Razor/**/*.razor
# subscriber Changed: grep -rn "Changed +=" src → chỉ SettingsAppearance.razor
# resx vắng mặt: find src/CCL.MES.Hybrid.Razor -name '*.resx' → 0
# IStringLocalizer vắng mặt: grep -rl IStringLocalizer src/CCL.MES.Hybrid.Razor → 0
```

---

## 6. Kết quả migrate (Pha 2 — option B, ĐÃ TRIỂN KHAI)

Đã chọn **phương án (B)** (§4): dict translator `ITranslator.T(key)` live theo
`ILanguageService.Current`, component `@inherits LocalizedComponentBase` (hoặc
`@inject ITranslator` cho component IAsyncDisposable/JS-interop) subscribe
`Changed` → re-render. Picker Settings/TopBar giờ **đổi ngôn ngữ LIVE, không
cần restart** (khắc phục "đường đứt" §3).

### Các batch (mỗi batch 1 commit, chưa merge — chờ review từng batch)

| Batch | Surface | Files | Keys | Commit |
|---|---|---|---|---|
| 2A | translator + picker live + TopBar flag + NavMenu | — | (hạ tầng) | `2ee16e8` |
| 2B | Login + shell (FloatingWindow/ConnectivityBanner) | 3 | 29 | `13a855d` |
| 2C | WorkOrders + 20 dashboard vận hành P10.7/P11 | 20 | 540 | `1233f75` |
| 2D | Settings pages | 8 | 241 | `cbf742d` |
| 2E | SpecHub-port (Home/ShopOrders/Spec*) | 17 | 616 | `53d3c70` |
| 2F | NPI/QMS/monitoring + spec modals + misc | 24 | 506 | `8ec6be8` |

**Catalog: ~1974 key**, mỗi key có **VI + EN non-empty** (test parity
`TranslationCatalogTests.Every_key_has_both_languages_non_empty` enforce mọi
batch). Verify sau mỗi batch: **Razor.Tests 216/216 · Client.Tests 629/629 · 3
gate PASS**. Test live-switch (`NavMenuI18nTests` + `SemiStockDashboard.
Language_flip_re_renders_dashboard_live`) chứng minh picker tác dụng ngay.

### %EN/%VI sau migrate

Đo lại bằng heuristic §0 (đếm chuỗi visible KHÔNG bọc `@T`):
**EN 1436 → ~132 · VI 113 → ~6** (giảm ~91% chuỗi hardcode). ~132 "EN" còn lại
KHÔNG phải UI chưa dịch mà là **false-positive + cố ý giữ**:
- **C# trong `@code`** heuristic thô bắt nhầm (generic `List<T>`, so sánh `a < b`,
  lambda `=> x`, fallback log strings). Self-audit chặt từng batch (loại `@code`)
  đã xác nhận **0 chuỗi UI hiển thị còn sót**.
- **Cố ý giữ**: token/enum/code hiển thị raw (`PRINTED_SEMI`, `IPQC_APPROVED`,
  phase codes, tên cột CSV `Parent Part No`), tên printer-tech (Silkscreen/Flexo/
  Indigo/Letterpress), brand ("CCL Design", company name), format (CSV/Excel/PDF),
  và **`Modal.razor aria-label="Close"`** (a11y-only; migrate buộc `ITranslator`
  vào ~22 file test render `<Modal>` — không tương xứng cho 1 aria).

### Do-not-touch — giữ nguyên (verify đối kháng từng file, 0 vi phạm)

`data-testid` · 7 `*ErrorLocaliser` (wording VI locked, KHÔNG gộp vào translator)
· chuỗi audit/log kỹ thuật · token/enum · class CSS/route/preference key ·
cấu trúc `Modal`/`FloatingWindow`/`RowContextMenu` (L34/L35).

### Nâng cấp lên (A) resx khi nào

Chuyển sang `IStringLocalizer`/resx (§4 phương án A) khi phát sinh nhu cầu
**format culture-aware** (số/ngày/tiền theo locale) — chưa cần cho MES xưởng
(qty/lot là số thuần). Key đã đặt theo namespace mirror `SharedResource.resx`
nên migrate B→A không mất key.

### Quy trình đã dùng (Workflow đa-agent)

Mỗi batch: **workflow N agent song song** (migrate 1 file + tự viết catalog
partial + verify đối kháng "chỉ đổi display/lifecycle đúng/không đụng do-not-
touch") → main-loop assemble serial (wire `Register*` vào ctor + helper
`TestI18n.AddI18n()` + DI cho test class) → **workflow fix-assertion song song**
(cập nhật assertion literal → VI-default byte-exact) → verify xanh → commit.
