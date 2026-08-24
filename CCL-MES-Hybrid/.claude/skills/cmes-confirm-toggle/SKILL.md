---
name: cmes-confirm-toggle
description: >
  Luật cho MỌI control "xác nhận OK/NG" trong CCL-MES Hybrid — bắt buộc dùng
  component dùng chung Shared/ConfirmToggle.razor, cấm vẽ tay cặp
  op-btn-success + op-btn-danger. Dùng khi thêm/sửa bất kỳ surface QC nào có
  ô OK/NG (Prepress vật tư/bản kẽm/dao chặt, IPQC, FQC, OQC, và surface mới).
---

# CMES confirm-toggle (L52)

**Rule (enforced):** một thao tác xác nhận **OK / NG** trên item / slot / row
đi qua **`Shared/ConfirmToggle.razor`** — một segmented toggle 2 ô (OK trái /
NG phải, viền bo, tô màu theo trạng thái). **KHÔNG** vẽ tay
`op-btn op-btn-success` + `op-btn op-btn-danger` cạnh nhau cho mục đích OK/NG.

Triệu chứng đã trả giá: cùng cụm nút OK/NG bị vẽ lại ở 6+ surface, mỗi chỗ
một kiểu label ("Record OK" / "Ghi OK" / "Đạt") + spacing + tap-size. Sửa
tone hay vùng chạm phải sờ vào từng chỗ. Một component gom label + token màu
+ tap-target + a11y về **một nơi**.

## Vì sao segmented toggle (không phải 2 chip rời)

Khớp ảnh target: một khối viền bo, ô OK chọn → nền xanh, ô NG chọn → nền đỏ,
chưa chọn → neutral. Gọn 1 dòng (mật độ cao cho table Prepress), trạng thái
Pending/OK/NG đọc được ngay, và dùng lại được cho cả layout **card** (IPQC/
FQC/OQC) lẫn **table** (Prepress). Tiền lệ: Carbon ContentSwitcher, Polaris
segmented ButtonGroup, Siemens iX toggle.

## Dùng thế nào

```razor
<ConfirmToggle Status="@item.Status"          @* "Pending" | "Ok" | "Ng" *@
               Disabled="@_busy"
               OnOk="@(() => OnSetOkAsync(item))"
               OnNg="@(() => OpenNgForm(item.ItemKey))"
               TestIdPrefix="@($"fqc-item-{item.ItemKey}")" />
```

Props:

| Prop | Bắt buộc | Ý nghĩa |
|---|---|---|
| `Status` | ✅ | `"Pending"`/`"Ok"`/`"Ng"` → class `is-pending/is-ok/is-ng` + `aria-pressed` |
| `Disabled` | | Khoá cả 2 ô (vd request đang bay) |
| `OkDisabled` / `NgDisabled` | | Khoá riêng từng ô (vd NG tắt khi danh mục mã lỗi trống) |
| `NgTitle` | | Tooltip ô NG (vd lý do bị khoá) |
| `OkLabel` / `NgLabel` | | Ghi đè nhãn (mặc định `confirm.ok`="OK" / `confirm.ng`="NG") |
| `OnOk` / `OnNg` | | `EventCallback` — set-OK / arm-NG (mở sub-form) |
| `TestIdPrefix` | | Sinh testid: `{prefix}-confirm` (wrapper), `{prefix}-ok`, `{prefix}-ng` |

## Ranh giới — cái gì KHÔNG thuộc toggle

- **NG sub-form** (picker mã lỗi + note) và nút **Save-NG / Cancel**: giữ
  NGUYÊN ở parent, render **dưới** toggle. Toggle chỉ phát intent `OnNg`
  (arm), parent quyết định phần còn lại.
- **Special Accept**: là action phụ (RBAC Admin/Supervisor/Engineer). Giữ nút
  `op-btn-special` **riêng**, KHÔNG nhét vào toggle. RBAC-by-omission như cũ.
- **Judgment** (Go Run / Stop Line / Pass / Reject): là quyết định **phase**,
  KHÁC ngữ nghĩa OK/NG. Giữ `op-btn ipqc-judgment-btn`, gate bỏ qua đúng vì
  chúng mang class `ipqc-judgment-btn`.

## Ràng buộc cứng

- **Style ở `app.css`** block `/* confirm-toggle */` — host maccatalyst chỉ
  nạp global `app.css`, `.razor.css` scoped là code chết ([[hybrid-app-no-scoped-css]]).
- **Token**: OK=`--ok-ink`, NG=`--ng-ink` (bản đậm → trắng đạt WCAG AA ở cả 2
  density), neutral=`--c-*`; tap=`var(--d-tap)`, font=`var(--d-font)`,
  spacing=`var(--sp-*)`. KHÔNG hardcode hex/px (L37 + L41).
- **Rule 4**: chỉ `<button>` + `@onclick`. KHÔNG `<InputText>`/`<EditForm>`.
- **i18n**: nhãn qua `TranslationCatalog` (`confirm.*`), đủ VI + EN (L42).
- **Không đổi hợp đồng**: chỉ đổi lớp trình bày; `EventCallback`/DTO giữ nguyên.

## Enforce

- Gate: `scripts/gate-confirm-toggle.sh` (ratchet, baseline 0) — đếm cụm
  `op-btn-success` + `op-btn-danger` "trần" (không `ipqc-judgment-btn`) trong
  `Shared/*.razor`. Có `--self-test`. Đã nối vào `scripts/gate-all.sh`.
- Test: `tests/CCL.MES.Hybrid.Razor.Tests/ConfirmToggleTests.cs` — 3 trạng
  thái + callback + disable matrix + fixture Prepress.
- Lesson: LESSONS-LEARNED.md **L52**.
