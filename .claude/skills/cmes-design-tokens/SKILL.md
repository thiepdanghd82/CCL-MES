---
name: cmes-design-tokens
description: >
  Ngôn ngữ thiết kế CCL-MES — thang chữ, thang khoảng cách, bo góc, đổ bóng,
  chuyển động, focus ring, và HAI density mode (office / shopfloor). Dùng khi
  chạm app.css, thêm màn hình .razor, hoặc chỉnh bất kỳ kích thước nào. Màu đã
  có token từ L37; skill này phủ phần còn lại.
---

# CMES design tokens

**Rule (enforced):** mọi **kích thước** trong `app.css` đi qua token, y như
mọi **màu** đã đi qua token từ L37. Cỡ chữ dùng `var(--fs-*)`, khoảng cách
dùng `var(--sp-*)`, và mọi surface operator phải chạy đúng ở **cả hai density**.

Triệu chứng đã trả giá: 6 commit liên tiếp chỉnh tay một bảng QC
(`0.9rem → 1.08rem`, `nới cột 3.4%`, `clamp/vw`, `table-layout:fixed`…).
Đó không phải lỗi thẩm mỹ — đó là hệ quả của việc không có thang.

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
- [ ] Tương phản chữ/nền ≥ AA (shopfloor nhắm AA+)
- [ ] `:focus-visible` dùng `var(--focus-ring)`, không `outline: none` trần
- [ ] `bash CCL-MES-Hybrid/scripts/gate-design-tokens.sh` không tăng ratchet
- [ ] `bash CCL-MES-Hybrid/scripts/gate-no-hardcoded-hex.sh` xanh (L37)

## Do NOT

- `clamp()`/`vw` để "tự co cho vừa" thay cho việc chọn đúng bậc thang —
  `clamp` là công cụ cho fluid type có chủ đích, không phải cách né thang.
- `!important` để đè token.
- Đặt cỡ chữ theo cảm giác trên đúng một cỡ màn hình đang mở.
