---
name: cmes-add-new-inline
description: >
  Luật cho MỌI affordance "thêm mới ngay tại chỗ" trong CCL-MES Hybrid — thêm
  một OPTION vào dropdown (vd defect), hoặc thêm một HÀNG vào grid check-item
  (vd hạng mục setting). Một pattern dùng chung: "＋ Thêm mới…" cuối dropdown +
  nút "＋ Thêm hạng mục" dưới hàng cuối. Dùng khi bất kỳ dropdown/grid nào cần
  cho người dùng bổ sung dữ liệu mà hệ thống phải NHỚ cho lần sau.
---

# CMES add-new-inline (design contract)

> **Trạng thái:** contract cho hạng mục 7g (F4 + add-new-defect). Chưa có
> implementation/gate ở main — phần "nhớ cho LOT sau" cần persist (STOP-gate
> schema). Skill này chốt HÌNH DẠNG để lúc 7g được Henry ký là dựng đúng một
> kiểu, và mọi dropdown/grid tương lai theo cùng khuôn.

**Rule:** một chỗ cho người dùng "thêm mới" đi qua **một trong hai hình dạng
chuẩn**, KHÔNG bịa flow riêng mỗi màn:

1. **Add-new trong dropdown** — mục cuối cùng của `<select>` luôn là
   **`＋ Thêm mới…`** (`option value="__add_new__"`). Chọn nó → mở **form nhỏ
   inline** ngay dưới ô (tên/label + field bắt buộc), KHÔNG nhảy trang.
   Áp dụng cho MỌI tab, MỌI hàng có dropdown (vd defect ở Print & Cut).
2. **Add-row cuối grid** — nút **`＋ Thêm hạng mục`** đặt ngay **dưới hàng cuối**
   của grid check-item, full-width, style phụ (`op-btn op-btn-ghost`/secondary).
   Bấm → chèn 1 hàng nhập liệu (hoặc mở form nhỏ), lưu → hàng thật.

## Vì sao (không phải modal nặng / không phải trang riêng)

Người đứng máy đang giữa luồng kiểm; thêm mới phải ở **đúng ngữ cảnh dòng đó**,
2 chạm là xong. Modal toàn màn cắt luồng; trang admin riêng thì operator không
với tới. Tiền lệ: Polaris ComboBox "Add new", GitHub label picker "Create",
Linear inline create. (Form transactional lớn vẫn `<Modal>`/`FloatingWindow`
theo L34 — đây chỉ là add nhanh 1–3 field.)

## Ngữ nghĩa persist (điểm CỐT LÕI — quyết định RBAC)

"Thêm mới" phải trả lời: **nhớ cho ai, tới bao giờ?**

| Phạm vi nhớ | Ai được thêm | Lưu ở đâu |
|---|---|---|
| **Ad-hoc cho lệnh này** (không nhớ LOT sau) | Operator+ | trên bản ghi WO (vd `WoSettingCheckItem` ad-hoc) |
| **Theo MÃ sản phẩm** (LOT kế tự có) | Engineer / Supervisor / Admin | master per-product (`CheckItemLibrary ProductCode=<mã>` / `CheckItemDefectOption`) |

- Mặc định đề xuất: **Engineer+ thêm per-product**, **Operator chỉ ad-hoc per-WO**.
- **RBAC-by-omission**: client chỉ dựng affordance khi user đủ quyền; server vẫn 403.
- Validate: tên/label bắt buộc; chống trùng mã; chống free-text rỗng (tinh thần L17).
- Audit: emit `*_ITEM_ADDED` / `*_DEFECT_ADDED` (ai · mã · giá trị) — [[cmes-audit-emit]].

## Ràng buộc cứng (giống mọi UI Hybrid)

- Style ở global `app.css` (host maccatalyst bỏ scoped `.razor.css` —
  [[hybrid-app-no-scoped-css]]); token semantic + `--d-tap`/`--d-font`/`--sp-*`,
  KHÔNG hardcode hex/px (L37/L41).
- **Rule 4**: `<button>`/`<input>`/`<select>` thuần, KHÔNG `<InputText>`/`<EditForm>`.
- **WKWebView `<select>`**: dropdown add-new dùng được `<select>` (khớp picker
  defect/NG hiện tại) NHƯNG nếu thấy freeze sau change trên maccatalyst thì đổi
  sang chip-picker (MachineDashboard pattern) — xem [[wkwebview-native-select-freeze]].
- **i18n** VI+EN qua `TranslationCatalog` (`*.addnew`, `*.addrow`) — L42.
- **Không đổi hợp đồng** khi chỉ là lớp trình bày; thêm endpoint add đi theo
  atomic pattern 7c-2 (If-Match/Idem-Key/single SaveChanges) + controller mỏng
  ([[cmes-thin-controller]]).

## Enforce (khi 7g dựng thật)

- Gate `scripts/gate-add-new-inline.sh` (ratchet): dropdown/grid check-item mới
  PHẢI có option `__add_new__` / nút add-row theo testid chuẩn
  (`{prefix}-addnew`, `{prefix}-addrow`); có `--self-test`; nối `gate-all.sh`.
- Test: mỗi surface có fixture "chọn ＋ Thêm mới → form hiện" + "add-row → hàng mới".
- Lesson: append LESSONS-LEARNED khi ship, kèm cơ chế chặn.

## Testid chuẩn

- Dropdown add-new option: `data-testid="{prefix}-defect-addnew"` (value `__add_new__`).
- Form add-new inline: `{prefix}-addnew-form` · field `{prefix}-addnew-name` · lưu `{prefix}-addnew-save` · huỷ `{prefix}-addnew-cancel`.
- Add-row cuối grid: `{tab}-addrow` (vd `setting-print-addrow`).
