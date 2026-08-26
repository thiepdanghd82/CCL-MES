---
name: cmes-design-tokens
description: >
  Ngôn ngữ thiết kế CCL-MES — thang chữ, thang khoảng cách, bo góc, đổ bóng,
  chuyển động, focus ring, và HAI density mode (office / shopfloor). Dùng khi
  chạm app.css, thêm màn hình .razor, hoặc chỉnh bất kỳ kích thước nào. Màu đã
  có token từ L37; skill này phủ phần còn lại.
---

# CMES design tokens

**Nền tảng: CCL iX** — `CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor/wwwroot/css/ix.css`
(nạp SAU `app.css`). Sáu nguyên tắc + toàn bộ pattern nằm ở đầu file đó.
**Trang tham chiếu sống:** mở `CCL-MES-Hybrid/docs/design-system/index.html`
trong trình duyệt — nó link CSS THẬT nên không bao giờ trôi khỏi code.
Xem nhanh biến thể: `?density=shopfloor` · `?rail=collapsed`.


**Rule (enforced):** mọi **kích thước** trong `app.css` đi qua token, y như
mọi **màu** đã đi qua token từ L37. Cỡ chữ dùng `var(--fs-*)`, khoảng cách
dùng `var(--sp-*)`, và mọi surface operator phải chạy đúng ở **cả hai density**.

Triệu chứng đã trả giá: 6 commit liên tiếp chỉnh tay một bảng QC
(`0.9rem → 1.08rem`, `nới cột 3.4%`, `clamp/vw`, `table-layout:fixed`…).
Đó không phải lỗi thẩm mỹ — đó là hệ quả của việc không có thang.

## Thang BREAKPOINT (L58) — đặt tên theo THIẾT BỊ, không theo số

```
--bp-phone 480 · --bp-tablet-p 768 · --bp-tablet-l 1024 · --bp-desk 1280 · --bp-wide 1600
```

CSS chưa cho `var()` trong điều kiện `@media`, nên đây là **hợp đồng + gate**:
viết số thật, nhưng chỉ được viết 5 số này. `gate-breakpoint-scale.sh` từ chối
số thứ 6.

**Trước khi thêm một ngưỡng, hỏi: có phải thứ cần sửa là CO LIÊN TỤC không?**
Đo được ở L58: chỉ ẩn theo breakpoint thì luôn còn khe hở giữa hai ngưỡng.
`min-width: 0` + `text-overflow: ellipsis` trên phần co được, `flex: 0 0 auto`
trên nút — đóng được mọi bề rộng mà không cần ngưỡng nào.

⚠ `@container` chỉ khớp khi có tổ tiên khai `container-type`. Trong app này chỉ
`.app-content`, `.trace-win-body`, `.rs-dashboard`, `.prepress-dashboard`,
`.semi-dashboard`, `.qms-page`, `.iqc-insp` là container. Thứ nằm NGOÀI chúng
(vd `TopBar`) phải dùng `@media` — `@container` ở đó **hỏng lặng lẽ**.

## Bảng rộng trên tablet (L58)

Bảng khai `min-width` ≥ 1024px phải có luật responsive cho **chính bảng đó**
(`gate-table-responsive.sh`). Hai khuôn đã có, chọn theo BẢN CHẤT dữ liệu:

- **Sập card** — bảng ít cột, mỗi dòng là một thực thể.
  Khuôn đang chạy: `.ipqc-mat-row` + `[data-label]::before`.
- **Cột dính** — bảng ma trận nhiều cột (lưới tick QC Library). Giữ 1-2 cột
  định danh dính trái, phần còn lại cuộn trong vùng riêng.

Khi sập card, **thiết kế lại NỘI DUNG**, đừng dịch từng ô 1:1: bỏ trường trùng
lặp, và **ô rỗng thì đừng render** (trừ khi sự vắng mặt tự nó là thông tin — khi
đó phải nói rõ lý do). L58 đo được một card dịch 1:1 cao gấp ~3 lần dòng bảng.

## Thang (định nghĩa ở `:root` trong `app.css`)

```css
/* Chữ — 7 bậc, KHÔNG có bậc trung gian tự chế */
--fs-xs:.75rem  --fs-sm:.8125rem  --fs-md:.875rem  --fs-base:1rem
--fs-lg:1.125rem  --fs-xl:1.375rem  --fs-2xl:1.75rem

/* Khoảng cách — lưới 4px */
--sp-1:4px  --sp-2:8px  --sp-3:12px  --sp-4:16px  --sp-5:24px  --sp-6:32px  --sp-7:48px

/* Bo góc · đổ bóng · chuyển động · focus */
--r-sm:4px  --r-md:8px  --r-lg:12px  --r-pill:999px
--el-1  --el-2  --el-3
--mo-fast:120ms  --mo-base:200ms  --mo-slow:320ms  --ease-std:cubic-bezier(.2,0,0,1)
--focus-ring: 0 0 0 3px var(--brand-bg), 0 0 0 1px var(--brand)
```

## Hai density — đây là phần quan trọng nhất

Cùng một component, hai bộ số. Chuyển bằng `data-density` trên `<html>`.

| Token | `office` (mặc định) | `shopfloor` |
|---|---|---|
| `--d-font` | `var(--fs-md)` 14px | `var(--fs-base)` 16px |
| `--d-row-h` | 32px | 56px |
| `--d-control-h` | 28px | 44px |
| `--d-tap` | 28px | **44px** |
| `--d-gap` | `var(--sp-2)` | `var(--sp-4)` |
| `--d-pad-x` | `var(--sp-3)` | `var(--sp-4)` |

`office` = kỹ sư/QA ngồi bàn, màn rộng, chuột, cần nhiều dòng trên một màn.
`shopfloor` = người đứng máy, **đeo găng**, nhìn xa hơn, ánh sáng xưởng,
chạm bằng ngón — mọi vùng chạm ≥ 44px, cỡ chữ ≥ 16px, tương phản cao hơn.

```css
/* Dùng như thế này — không viết số trực tiếp */
.wo-row { height: var(--d-row-h); font-size: var(--d-font); padding-inline: var(--d-pad-x); }
.wo-btn { min-height: var(--d-tap); min-width: var(--d-tap); }
```

Màn hình nào Operator chạm được ⇒ **bắt buộc** screenshot cả hai density
trong PR (xem `cmes-verify-evidence`).

## Thứ tự ưu tiên khi cần một giá trị

1. Có token phù hợp ⇒ dùng token.
2. Không có ⇒ hỏi: đây là **bậc mới của thang** hay **một ngoại lệ**?
   - Bậc mới ⇒ thêm vào `:root`, đặt tên theo hệ, dùng ≥2 nơi.
   - Ngoại lệ ⇒ viết `/* one-off: <lý do> */` ngay dòng đó và bump BASELINE.
3. **Không bao giờ** chọn cách "chỉnh 0.9 lên 1.08 cho vừa mắt".

## Ở đâu viết cái gì

| Việc | File |
|---|---|
| Thêm/đổi token (thang, density, trạng thái) | `ix.css` §`:root` |
| Pattern dùng lại (tile, pill, toolbar, grid, nút) | `ix.css` §3–§8 |
| Đổi diện mạo class markup CŨ (không sửa Razor) | `ix.css` §10 lớp tương thích |
| Style riêng của MỘT component | `.razor.css` scoped |
| Print-CSS | **luôn** `app.css` global (L39 — scoped chết trên maccatalyst) |

⚠ **Không đụng** `.spec-*-table-full` / `.spec-print-*` — L39 quản, on-screen
phải == bản in.

## Bố cục app.css

`app.css` hiện 7.3k dòng một khối. Code mới xếp theo `@layer`:
`reset → tokens → primitives → patterns → pages`. Rule của một component cụ
thể nên nằm ở `.razor.css` scoped, **trừ** print-CSS (L39: `@media print`
scoped chết trên maccatalyst → phải để global).

## Checklist

- [ ] 0 `font-size:` literal mới — dùng `var(--fs-*)` hoặc `var(--d-font)`
- [ ] 0 `px` literal mới cho padding/margin/gap — dùng `var(--sp-*)`
- [ ] Surface operator: mọi nút/ô chạm `min-height: var(--d-tap)`
- [ ] Thử ở `data-density="shopfloor"`, không vỡ layout, không cắt chữ
- [ ] **Chụp màn ở 768px (`--bp-tablet-p`)** — BẮT BUỘC với mọi PR chạm
      `.razor`/`app.css`. App sẽ chạy trên tablet dưới xưởng; tablet DỌC chỉ còn
      ~800px bề rộng. Đây là ảnh THỨ BA, không thay cho hai ảnh density.
      Vì sao bắt buộc: **không gate nào bắt được lỗi bố cục** — nó chỉ tồn tại
      khi render ở kích thước thật. L58: thanh chrome toàn cục tràn+cắt ở 768px
      suốt nhiều tháng trong khi mọi gate đều xanh.
- [ ] Tương phản chữ/nền ≥ AA (shopfloor nhắm AA+)
- [ ] `:focus-visible` dùng `var(--focus-ring)`, không `outline: none` trần
- [ ] `bash CCL-MES-Hybrid/scripts/gate-design-tokens.sh` không tăng ratchet
- [ ] `bash CCL-MES-Hybrid/scripts/gate-no-hardcoded-hex.sh` xanh (L37)
- [ ] `bash CCL-MES-Hybrid/scripts/gate-token-defined.sh` xanh (L56) — mọi
      `var()` trỏ token CÓ THẬT. Hard-fail ở 0, không có baseline để bump.
      `var(--chưa-định-nghĩa)` hỏng ở computed-value time, KHÔNG im lặng:
      màu chữ tụt về inherit, nền/viền tụt về trong suốt. Đã làm một nút vô hình.
- [ ] `bash CCL-MES-Hybrid/scripts/gate-tap-target.sh` xanh (L57a) — vùng chạm
      qua `--d-tap`, không px cứng. Sàn px đi KÈM token là hợp lệ
      (`width: var(--d-tap); min-width: 20px`).
- [ ] `bash CCL-MES-Hybrid/scripts/gate-viewport-sizing.sh` xanh (L57b) — cỡ chữ
      và khoảng cách KHÔNG lái bằng `vw/vh`. Cần fluid thật ⇒ dùng `cqi`.

## Do NOT

- `clamp()`/`vw` để "tự co cho vừa" thay cho việc chọn đúng bậc thang —
  `clamp` là công cụ cho fluid type có chủ đích, không phải cách né thang.
  **Nay có gate**: `gate-viewport-sizing.sh` hard-fail ở 0. `vw` mù CẢ density
  LẪN `--ui-scale` ⇒ màn hình dùng nó tự rút khỏi hệ density. Fluid thật thì
  dùng đơn vị container-query (`cqi`), nó co theo container chứ không theo cửa sổ.
- Viết số px thẳng cho vùng chạm. **Nay có gate**: `gate-tap-target.sh`.
- Dùng một `var(--token)` mà chưa định nghĩa token đó. **Nay có gate**:
  `gate-token-defined.sh`.
- `!important` để đè token.
- Đặt cỡ chữ theo cảm giác trên đúng một cỡ màn hình đang mở.
- Tự chế một ngưỡng breakpoint mới. **Nay có gate**: `gate-breakpoint-scale.sh`.
- Thêm bảng `min-width` ≥1024px mà không kèm luật responsive cho nó.
  **Nay có gate**: `gate-table-responsive.sh`.
- Dùng `@container` cho thứ nằm ngoài một `container-type` — nó không khớp và
  **không báo lỗi**.
