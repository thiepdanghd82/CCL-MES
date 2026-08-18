---
name: cmes-i18n-parity
description: >
  Luật đa ngữ EN/VI của CCL-MES — mọi chuỗi hiển thị đi qua TranslationCatalog
  (Hybrid) hoặc SharedResource.resx (legacy), không hardcode trong .razor.
  Dùng kèm MỌI work-class có thêm chuỗi hiển thị. i18n là thuế của mọi task
  chạm UI, không phải một task riêng.
---

# CMES i18n parity

**Rule (enforced):** không có chuỗi hiển thị nào được hardcode trong `.razor`.
Mọi chuỗi vào catalog với **đủ cả VI và EN** ngay trong cùng commit.

## Hai hệ — biết mình đang ở hệ nào

| Ứng dụng | Hệ | File |
|---|---|---|
| **Hybrid (hiện hành)** | `TranslationCatalog` in-code | `CCL.MES.Hybrid.Client/Localization/TranslationCatalog.*.cs` |
| Legacy Web (đang chờ cutover) | `IStringLocalizer<SharedResource>` | `CCL.MES.Web/Resources/SharedResource[.vi].resx` |

Code mới ⇒ **luôn** hệ Hybrid. Không thêm key mới vào `.resx` nữa.

## Cách thêm chuỗi

```csharp
// TranslationCatalog.<Surface>.cs — một partial mỗi surface
public sealed partial class TranslationCatalog
{
    private void RegisterLegs()
    {
        //     key                        vi                        en
        Add("legs.title",              "Công đoạn song song",     "Parallel legs");
        Add("legs.gate.blocked",       "Chờ công đoạn trước",     "Waiting upstream");
    }
}
```

Rồi đăng ký `RegisterLegs()` trong constructor `TranslationCatalog.cs`.

## Luật đặt key

- **lower.dotted**, so khớp **ordinal, phân biệt hoa thường**.
- Namespace theo surface: `nav.*` · `wo.*` · `ipqc.*` · `legs.*` · `spec.*` ·
  `settings.*` · `qms.*`.
- Key **duy nhất toàn hệ**. Trùng key ⇒ `Dictionary.Add` **throw lúc khởi động**
  → app chết ngay khi mở. Gate bắt tĩnh trước khi tới đó.
- Không nhét biến vào key (`$"wo.status.{code}"`) trừ khi mọi giá trị `code`
  đều đã có key — nếu không sẽ thiếu chuỗi im lặng ở runtime.

## Chuỗi động từ server

Server trả **error code**, không trả câu tiếng Việt. `WoErrorCode` →
`WoErrorKeys` → i18n key → catalog. Đây là lý do guard trả enum (xem
`cmes-state-contract`).

## Checklist

- [ ] Chuỗi mới có **cả** VI và EN, không để rỗng, không để trùng nhau máy móc
- [ ] Key mới không trùng key đã có
- [ ] Không còn chuỗi tiếng Việt/Anh nằm trần trong markup `.razor`
- [ ] `aria-label`, `title`, `placeholder` **cũng** phải qua catalog
- [ ] Chuỗi dài (EN thường dài hơn VI ~20%) không phá layout ở density hẹp
- [ ] `bash CCL-MES-Hybrid/scripts/gate-i18n-parity.sh` xanh

## Do NOT

- `<h2>Danh sách lệnh sản xuất</h2>` — dù "chỉ là màn hình nội bộ".
- Dịch bằng cách nối chuỗi (`t("a") + " " + t("b")`) — trật tự từ khác nhau
  giữa hai ngôn ngữ.
- Thêm key vào `.resx` cho tính năng mới.
