# Nhật ký migration — nối IPQC vào QcCriteria

Migration: `20260911071237_AddIpqcProductLimitsLink`
Ngày: 2026-09-11 · Trạng thái: **Phase A + B + C XONG** — áp live 2026-09-11

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

## Phase C — ĐÃ ÁP LIVE (2026-09-11)

**Thẩm quyền:** STOP-gate §0 được **Thiệp miễn tường minh** sau khi đọc đánh giá
rủi ro. Không phải agent tự vượt cổng.

Thứ tự: xác nhận baseline chưa trôi → **dừng API** → áp → thu bằng chứng →
build lại + khởi động API → chạy thật → dọn dữ liệu thử.

### Bằng chứng

| | trước | sau |
|---|---|---|
| cột `WoIpqcCheckItems` | 30 | **35** |
| cột `QcCriteria` | 20 | **21** |
| 6 cột mới | — | **notnull=0** cả sáu |
| index `WoIpqcCheckItems` | 2 | **2** |
| index `QcCriteria` | 1 | **1** |
| `integrity_check` | ok | **ok** |
| `foreign_key_check` | 0 | **0** |

Rowcount — **không dòng nào đổi** trên 8 bảng (WorkOrders 2 · WoIpqcCheckItems 14
· QcCriteria 0 · SpecQcWindows 0 · CheckItemLibraries 87 · SpecPrints 330 ·
IqcInspections 5.334 · MaterialLots 4.850).

`__EFMigrationsHistory` mới nhất: `20260911071237_AddIpqcProductLimitsLink`.

### Pha 5 VERIFY — chạy thật trên DB LIVE

Soạn một kế hoạch QC cho revision 154 (stage `IpqcCut`, `Approved`), một tiêu
chí `LibraryItemKey = LBL-B1`, 19,5–20,5 mm. Dựng lại hạng mục IPQC cho
`WO-TEST-02` qua API thật:

| hạng mục | công đoạn | cận dưới | cận trên | đơn vị | từ kế hoạch |
|---|---|---|---|---|---|
| LBL-A3 | PRESS_CNC | — | — | — | — |
| **LBL-B1** | PRESS_CNC | **19,5** | **20,5** | **mm** | **#1** |

`LBL-B1` có tiêu chí ⇒ nhận ngưỡng kèm nguồn. `LBL-A3` không có ⇒ không bị đụng.

### Dọn dẹp

Kế hoạch QC thử **chèn bằng SQL trực tiếp** nên đã xoá hẳn — đúng thứ phiên làm
việc này đang chống. Ngưỡng đã đóng băng trên hạng mục cũng xoá theo, vì nó là
dư của phép thử chứ không phải hồ sơ thật. Về đúng baseline:
`QcCriteria` 0 · `SpecQcWindows` 0 · 0 dòng mang ngưỡng · `integrity_check` = ok.

> **Quan sát đáng ghi:** xoá một `SpecQcWindow` KHÔNG xoá ngưỡng đã đóng băng
> trên hồ sơ — đó là **đúng thiết kế** (hồ sơ giữ ngưỡng đã áp). Nhưng
> `LimitSourceWindowId` khi ấy trỏ vào một kế hoạch không còn tồn tại. Cùng họ
> với vấn đề khoá ngoại đang chờ Henry duyệt.

### Đường lùi (vẫn còn hiệu lực)

```bash
cp data/Backup/SQLite/ccl_mes.db.before-ipqc-qccriteria.20260911-141122 data/ccl_mes.db
rm -f src/CCL.MES.Infrastructure/Migrations/*AddIpqcProductLimitsLink*
cp /tmp/snapshot-pre-qccriteria.cs \
   src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
```
