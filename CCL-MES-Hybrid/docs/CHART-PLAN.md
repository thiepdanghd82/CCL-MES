# CCL-MES — Kế hoạch biểu đồ dashboard (đề xuất, chưa thực thi)

> Nguồn: skill `ui-ux-pro-max` (charts.csv, 25 loại) đối chiếu với **dữ liệu
> thật đang có trong entity** và **6 nguyên tắc CCL iX**. Mọi khuyến nghị màu
> trong charts.csv là hex thô (`#0080FF`, `#FF0000`…) — ĐÃ ĐƯỢC DỊCH sang token
> `--ix-*`. Không đưa hex gốc vào code.
>
> Trạng thái: **đề xuất**. Chưa có dòng code nào được thêm.

---

## 0. Ràng buộc quyết định mọi lựa chọn dưới đây

| Ràng buộc | Bằng chứng | Hệ quả cho biểu đồ |
|---|---|---|
| App là **MAUI Blazor Hybrid** (BlazorWebView), `net10.0-maccatalyst` + `net10.0-windows` | `src/CCL.MES.Hybrid/CCL.MES.Hybrid.csproj:23-27`, `MauiProgram.cs:80` | Không CDN — mọi asset phải bundle vào `wwwroot`. Đổi lại JS interop chạy in-process, không qua SignalR → rẻ. |
| **Chưa có thư viện chart nào** trong cây | grep `chart.js\|apexcharts\|echarts\|plotly\|blazorise\|radzen` → 0 kết quả | Đây là quyết định xanh, không phải migration. |
| Doctrine "số liệu thật" | `MachineDashboard.razor:36-39` — *"honest — no fabricated OEE/speed"* | Không vẽ biểu đồ cho chỉ số mà dữ liệu chưa có. |
| **38/43 work center thiếu `IdealSpeedPcsH`** | `OeePerformance.cs` docstring; `gate-oee-single-source.sh` | OEE performance null ở đa số WO → **cấm gauge OEE** (xem §3). |
| iX #1 không gradient/đổ bóng · #2 một màu nhấn · #3 năm trạng thái · #4 phân cấp bằng độ đậm không bằng màu · #6 dữ liệu là chính | `ix.css:1-30` | Bỏ mọi "gradient" mà charts.csv gợi ý. |
| Hai density (office / shopfloor) | `ix.css`, `wwwroot/js/density.js` | Chart phải đọc lại kích thước khi đổi density. **`density.js` hiện KHÔNG dispatch event** — cần thêm (xem §5). |
| Catalyst scaling: CSS px ≈ points / 0.77 | ghi chú vận hành | Breakpoint chart tính theo CSS px, không theo point cửa sổ. |
| `gate-no-hardcoded-hex.sh` quét `app.css`, `ix.css`, `.razor` — **KHÔNG quét `.js`** | `gate-no-hardcoded-hex.sh:30-70` | Lỗ hổng gate: config chart bằng JS có hex sẽ LỌT. Xem §5. |

---

## 1. Dữ liệu thật nào vẽ được biểu đồ

| Nguồn | Trường | Mở khoá biểu đồ |
|---|---|---|
| `WoQcCheckItem` | `NgReasonCode`, `Status`, `ItemKey` | Pareto lỗi, tỉ lệ pass |
| `CheckItemLibrary` | `DefectCode`, `Severity`, `GroupLabel`, `ProcessLine`, 13 cờ phương pháp (`Flexo`…`Slit`) | Nhóm Pareto, heatmap công đoạn × phương pháp |
| `WoQcCheck` | `QcKind` (IPQC/FQC/OQC), `Judgment`, `InspectedAt` / `ReviewedAt` / `ApprovedAt` | Funnel theo khâu, thời gian chu kỳ duyệt, xu hướng |
| `WorkOrder` | qty good / reject, `MesPhase` | Yield, bullet vs mục tiêu |
| `WoLeg` | phase từng chặng | Bản đồ tiến trình, cycle time theo chặng |
| `AuditLog` | hành động có mốc thời gian | Heatmap hoạt động theo giờ/ca |
| `WoIpqcCheckItem` | `MeasuredValue` (**`string?`**, `MaxLength(128)`), `CheckType` | SPC — **có điều kiện**, xem §3 |
| `WorkCenter` | `IdealSpeedPcsH` (5/43 có giá trị) | ⚠ OEE performance — **chặn**, xem §3 |

---

## 2. Đề xuất — xếp theo giá trị trên chi phí

### ① Pareto lỗi (ưu tiên cao nhất)
- **charts.csv #2** Compare Categories → Bar Chart (horizontal), sorted descending, có nhãn giá trị.
- **Dữ liệu:** `WoQcCheckItem.NgReasonCode` join `CheckItemLibrary.DefectCode` / `GroupLabel`.
- **Vì sao đứng đầu:** trả lời trực tiếp "sửa cái gì trước" — câu hỏi trung tâm của QMS. Dữ liệu đã có đủ, không cần entity mới.
- **Dịch màu sang iX:** charts.csv nói *"each bar distinct color"* → **BỎ**, vi phạm iX #2/#4. Dùng **một màu** `--ix-accent` cho toàn bộ cột; đường luỹ kế `--ix-ink-mut`; ngưỡng 80% kẻ bằng `--ix-warn-line`. Phân cấp bằng thứ tự + độ dài cột, không bằng màu.
- **Nếu muốn mã hoá `Severity`:** chỉ dùng đúng 5 trạng thái iX (`--ix-alarm-*` / `--ix-warn-*` / `--ix-idle-*`), không tự chế bảng màu thứ 6.
- **Kỹ thuật:** SVG nội tuyến trong Razor (§4 Tier 1).

### ② Bullet chart — yield / tiến độ vs mục tiêu
- **charts.csv #18** Performance vs Target (Compact) → Bullet. a11y ghi *"Excellent — compact with clear values"*.
- **Vì sao thay gauge:** gauge tốn diện tích, đọc kém khi đứng xa. Bullet gọn, hợp shopfloor.
- **Đã có sẵn 80%:** `MachineDashboard.razor:50` đang render `md-kpi-bar` + `md-kpi-bar-fill` — chỉ thiếu **vạch mục tiêu** và **dải ngưỡng** là thành bullet chart đúng nghĩa.
- **Dịch màu:** dải ngưỡng `--ix-alarm-tint` → `--ix-warn-tint` → `--ix-ok-tint`; thanh đo `--ix-accent`; vạch mục tiêu `--ix-ink` 3px (charts.csv nói "black 3px" — `--ix-ink` là bản token hoá).
- **Chi phí thấp nhất trong danh sách.** Nên làm trước ① nếu muốn thắng nhanh.

### ③ Funnel IPQC → FQC → OQC
- **charts.csv #7** Funnel/Flow. Thư viện gợi ý gồm **"Custom SVG"**.
- **Dữ liệu:** `QmsQueueDto` (`IpqcCount`/`FqcCount`/`OqcCount`) đã được `QmsDashboard.razor` gọi sẵn — **không cần API mới**.
- **Dịch màu:** charts.csv nói *"Stages: gradient"* → **BỎ**, vi phạm iX #1. Bề mặt phẳng `--ix-surface`, viền `--ix-line`, % chuyển tiếp ghi bằng chữ.
- **Nâng cấp tự nhiên** của 4 thẻ KPI đang có ở `QmsDashboard.razor:24-45`.

### ④ Heatmap công đoạn × phương pháp
- **charts.csv #5** Heatmap/Intensity.
- **Dữ liệu:** `CheckItemLibrary.ProcessLine` × 13 cờ phương pháp — ma trận đã tồn tại sẵn trong schema, đây là món "cho không".
- **Cảnh báo a11y từ chính charts.csv:** *"⚠ Colorblind: use pattern overlay, provide numerical legend"* → **bắt buộc in số trong ô**, không chỉ tô màu. Điều này trùng khớp iX #4 (phân cấp bằng độ đậm, không bằng màu).
- **Dịch màu:** charts.csv nói "cool blue → hot red" → dùng thang `--ix-accent-tint` → `--ix-alarm-tint` (đơn sắc theo độ đậm), không phải cầu vồng.

### ⑤ Xu hướng NG theo ngày
- **charts.csv #1** Trend Over Time (Line) hoặc **#10** Anomaly Detection (Line with Highlights).
- **Dữ liệu:** `WoQcCheck.InspectedAt` + tỉ lệ NG.
- **Dịch màu:** đường `--ix-accent`; điểm bất thường `--ix-alarm-ink` kèm **marker hình khác** (không chỉ đổi màu) + chú thích chữ, đúng khuyến nghị a11y của charts.csv.
- **Đây là biểu đồ đầu tiên đáng cân nhắc Tier 2** nếu cần zoom/pan nhiều điểm.

---

## 3. KHÔNG khuyến nghị — và lý do

| Biểu đồ | Vì sao loại |
|---|---|
| **Gauge OEE** (charts.csv #8) | 38/43 work center không có `IdealSpeedPcsH`; 19/27 WO không có chỉ số. Gauge hiển thị "—" ở đa số WO **tệ hơn** con số + lý do đang có. Đây là nợ **master data**, không phải nợ biểu đồ. Sửa `IdealSpeedPcsH` trước, biểu đồ sau. |
| **Pie / Donut** (#3) | charts.csv tự giới hạn 6 lát; số mã lỗi vượt xa. Pareto trả lời cùng câu hỏi tốt hơn. |
| **Streaming area chart** (#23) | charts.csv tự cảnh báo *"⚠ flashing elements — provide pause button"*. Sai cho panel chạy liên tục 8 tiếng trước mặt công nhân. |
| **Radar / Spider** (#14) | a11y "moderate", giới hạn 5-8 trục; không có tập dữ liệu nào ở đây hợp tự nhiên. |
| **SPC / Box plot** (#17) — **hoãn, không loại** | `WoIpqcCheckItem.MeasuredValue` là **`string?`**, không phải số. Muốn SPC thật thì phải chuẩn hoá sang kiểu số + đơn vị + USL/LSL trước. Vẽ SPC trên chuỗi text tự do là bịa số liệu — trái doctrine ở `MachineDashboard.razor:36`. |

---

## 4. Quyết định thư viện

### Tier 1 — SVG nội tuyến trong Razor, KHÔNG thư viện *(mặc định)*
Áp cho: **bullet ②, Pareto ①, funnel ③, heatmap ④, sparkline.**

Lý do:
1. **Token-native.** `var(--ix-*)` dùng thẳng được trong thuộc tính SVG qua CSS → tự động đúng cả hai density, tự động theo retheme L37.
2. **Gate nhìn thấy.** Nằm trong `.razor` → `gate-no-hardcoded-hex.sh` quét được. Config JS thì không (§5).
3. **In được.** App có `IPrintService` riêng vì `window.print()` là no-op trong BlazorWebView; SVG nội tuyến in thẳng, canvas thì không.
4. **Bundle 0 KB**, đúng iX #6 "chrome tối thiểu".
5. charts.csv **tự liệt kê "Custom SVG"** làm lựa chọn hợp lệ cho bullet (#18), gauge (#8), funnel (#7).

### Tier 2 — Chart.js, chỉ khi thật cần
Áp cho: **⑤ xu hướng nhiều điểm có zoom/pan**, và chỉ khi Tier 1 đã tỏ ra không đủ.

- ~60 KB gzip, canvas, MIT, **không phụ thuộc**, bundle vào `wwwroot/js/`.
- Loại ApexCharts (~500 KB), ECharts (~1 MB) — quá nặng cho một app desktop offline chỉ cần vài biểu đồ.
- Loại Recharts (chỉ React), Blazorise/Radzen (kéo theo cả design system riêng, đụng độ trực diện với iX).

**Không bao giờ dùng CDN** — app chạy offline ở xưởng.

---

## 5. Hai việc phải làm TRƯỚC khi có biểu đồ đầu tiên

### 5a. Vá lỗ hổng gate hex cho `.js` *(chỉ khi chọn Tier 2)*
`gate-no-hardcoded-hex.sh` quét `app.css` + `ix.css` + `.razor`, **không quét `wwwroot/js/`**. Một config Chart.js chứa `borderColor: '#1d4ed8'` sẽ **pass gate mà vẫn phá doctrine token**.

Hai đường xử lý, chọn một:
- **Ưu tiên:** đọc token lúc chạy, không hardcode —
  `getComputedStyle(document.documentElement).getPropertyValue('--ix-accent')`
  rồi truyền vào config. Tự động đúng khi retheme.
- **Hoặc:** mở rộng gate quét `wwwroot/js/*.js` với baseline ratchet như `.razor`.

### 5b. Cho `density.js` phát sự kiện
`density.js` chỉ set `document.documentElement.dataset.density` rồi thôi — **không `dispatchEvent`**. SVG Tier 1 tự co theo CSS nên không sao, nhưng **chart canvas Tier 2 sẽ không vẽ lại** khi người dùng đổi office ↔ shopfloor.

Thêm vào `apply()`:
```js
window.dispatchEvent(new CustomEvent('ccl:density', { detail: v }));
```
rồi chart Tier 2 lắng nghe và gọi `resize()`.

---

## 6. Thứ tự thực thi đề xuất

| # | Việc | Work-class | Skill bắt buộc | Agent | Nghiệm thu |
|---|---|---|---|---|---|
| 1 | Bullet ② — thêm vạch mục tiêu + dải ngưỡng vào `md-kpi-bar` | W5 (UI) | `cmes-design-tokens` + `cmes-i18n-parity` | `cmes-shopfloor-ux` | `gate-all.sh` PASS; ảnh chụp 2 density |
| 2 | Funnel ③ — nâng cấp 4 thẻ KPI `QmsDashboard` | W5 | `cmes-design-tokens` + `cmes-i18n-parity` | `cmes-shopfloor-ux` | Không API mới; `gate-all.sh` PASS |
| 3 | Pareto ① — cần endpoint gộp `NgReasonCode` | W3 (API) + W5 | `cmes-thin-controller`, `cmes-design-tokens`, `cmes-i18n-parity` | `cmes-implementer` → `cmes-shopfloor-ux` | `gate-thin-controller.sh` PASS |
| 4 | Heatmap ④ | W5 | như trên | `cmes-shopfloor-ux` | Có số trong ô, không chỉ màu |
| 5 | Xu hướng ⑤ (cân nhắc Tier 2) | W5 | như trên + §5a, §5b | `cmes-shopfloor-ux` | Đổi density → chart vẽ lại đúng |

**STOP-gate:** mọi chuỗi hiển thị mới (nhãn trục, chú thích, tooltip) phải đi qua
`TranslationCatalog` EN/VI — `gate-i18n-parity.sh`. Đây là thuế bắt buộc của mọi
task chạm UI.
