# Nhật ký migration — nối IPQC vào QcCriteria

Migration: `20260911071237_AddIpqcProductLimitsLink`
Ngày: 2026-09-11 · Trạng thái: **Phase A + B XONG — Phase C CHƯA ÁP (STOP-gate §0)**

## Vì sao

Dung sai của hạng mục đo là giá trị **theo sản phẩm**: nhãn 20mm và nhãn 200mm
cùng hạng mục "kích thước tổng thể" nhưng khác dung sai. Thư viện hạng mục kiểm
khoá theo **dòng sản xuất** nên không thể mang con số ấy.

Con số sống ở `QcCriteria` (khoá theo `ProductRevision × Stage`) — tầng đã có
sẵn, có trình soạn `SpecQcPlansTab` trong app mới. Nhưng **không có đường nối**
giữa `QcCriterion` và hạng mục thư viện: `Name` là chữ kỹ sư tự gõ.

## Thay đổi schema — additive, 6 cột nullable

| Bảng | Cột | Ý nghĩa |
|---|---|---|
| `QcCriteria` | `LibraryItemKey` | đường nối → `CheckItemLibrary.ItemId` |
| `WoIpqcCheckItems` | `LimitLow` · `LimitUp` · `LimitNominal` | ngưỡng đã áp |
| | `LimitUnit` | đơn vị — thiếu nó thì con số vô nghĩa |
| | `LimitSourceWindowId` | truy ngược về đúng kế hoạch QC nào |

Không đổi nghĩa cột cũ. Không index mới. Không trigger. Không rebuild bảng.

## Vì sao nối bằng CỘT, không khớp theo tên

`QcCriterion.Name` là chữ tự do. "Kích thước tổng thể" và "Kích thước tổng thể
(W×L)" không khớp nhau, và sai một chữ là **mất dung sai mà không ai báo**. Cột
tường minh thì trượt là thấy ngay.

## Luật khớp — phải đủ HAI vế

1. cùng `LibraryItemKey` (không phân biệt hoa thường)
2. stage của kế hoạch hợp công đoạn của hạng mục
   (`IpqcPrint`↔IN · `IpqcCut`↔CẮT; `Fqc`/`Oqc` KHÔNG áp)

Thiếu vế 2 thì kế hoạch khâu IN bơm ngưỡng sang hạng mục khâu CẮT — cùng tên
hạng mục, khác dung sai, và **vẫn ra số** nên không ai nhìn thấy.

**Hai tiêu chí cùng trỏ một hạng mục ⇒ KHÔNG lấy cái nào.** Đó là mâu thuẫn
trong chính spec; chọn bừa là giấu nó đi rồi đóng dấu vào hồ sơ đã ký.

**Chỉ lấy kế hoạch `Approved`.** `Draft` là thứ kỹ sư đang sửa dở.

## Phase A — baseline

```
backup : data/Backup/SQLite/ccl_mes.db.before-ipqc-qccriteria.20260911-141122
```
Đối chiếu nội dung live ↔ backup: WorkOrders 2 · WoIpqcCheckItems 14 ·
QcCriteria 0 · SpecQcWindows 0 · CheckItemLibraries 87 — khớp cả 5.
`integrity_check` = ok. Snapshot code: `/tmp/snapshot-pre-qccriteria.cs`.

## Phase B — DB cô lập `/tmp/qccrit-design.db`

Sinh với `MES_CONNSTR` trỏ `/tmp`; đọc nội dung trước khi áp; strip
type-affinity 6 → 0; áp thành công; cả 6 cột `notnull=0`.

## Phase C — CHƯA CHẠY

**Lưu ý về giá trị thực tế:** `SpecQcWindows` và `QcCriteria` đang **0 dòng**.
Áp migration này KHÔNG làm gì thay đổi trên màn hình cho tới khi bên chất lượng
soạn kế hoạch QC cho ít nhất một sản phẩm. Nó dựng sẵn đường ống, không tự sinh
dữ liệu.

**Đường lùi:**
```bash
rm -f src/CCL.MES.Infrastructure/Migrations/*AddIpqcProductLimitsLink*
cp /tmp/snapshot-pre-qccriteria.cs \
   src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
# nếu đã áp live: restore từ data/Backup/SQLite/ccl_mes.db.before-ipqc-qccriteria.20260911-141122
```
