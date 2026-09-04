# P13 — cách đo lại mọi con số trong scope proposal

> Mọi khẳng định trong `p13-scope-proposal.md` đều đo được lại bằng các lệnh
> dưới đây. Nếu file master đổi, chạy lại và **sửa cả con số lẫn kết luận** —
> đừng để tài liệu nói một đằng, dữ liệu một nẻo.

File nguồn (KHÔNG nằm trong repo — dữ liệu vận hành):
`~/Documents/0. WORK DATA/IQC Data/Copy of IQC report 2026 (version 1).xlsx`

## Vị trí cột (0-based) — đã đối chiếu tay

| Sheet | dòng tiêu đề | cỡ lô | số lượng kiểm | đánh giá ngoại quan |
|---|---:|---:|---:|---:|
| Roll | 2 | 11 `Qty roll` | 18 | 32 |
| PCS | 1 | 10 `Số lượng về` | 15 | 25 |
| Chem | 2 | 10 `Tin/Box` | 11 | — |
| Tool | 2 | 9 `Số lượng Về` | 10 | — |
| Raw | 2 | — | — | — |

`Raw`: 2 `IFS` · 3 `Mother code` · 6 `Phương pháp test` · 7 `Tiêu chuẩn keo` ·
8 `Tiêu chuẩn dày` · 9 `Tiêu chuẩn rộng`

## Đo 1 — luật cỡ mẫu là `min(bảng, lô)`

```python
# so cột "số lượng kiểm" với aql(lô) và với min(aql(lô), lô)
# kỳ vọng: Roll 70/27/1 · Chem 59/39/1 · Tool 5/93/0  (%)
```

## Đo 2 — luật chấp nhận Ac = 0

```python
# Roll: cộng 13 ô đếm lỗi (cột 19..31), đối chiếu với cột 32 (Đánh giá)
# kỳ vọng: OK&có-lỗi = 0 · NG&không-lỗi = 0
```
Đây là con số quan trọng nhất. Nếu lần đo sau ra **khác 0**, luật Ac=0 đã
không còn đúng và `IqcAcceptance.JudgeDefectCounts` phải sửa theo.

## Đo 3 — độ phủ bộ đọc tiêu chuẩn

Gom chuỗi từ 7 cột tiêu chuẩn (`Raw` 7/8/9 · `Roll` 34/52 · `PCS` 27/34),
chạy qua `IqcSpecLimitParser`, phân về 4 rổ.
Kỳ vọng (2026-09-04): **49 % / 43 % / 4 % / 1 %**.

Rổ "chưa xử lý" là **ratchet**: được phép giảm, không được phép tăng.

## Đo 4 — độ trùng mã với app

```sql
SELECT MaterialCode FROM IqcMaterialSpecs WHERE Active=1;   -- 459
```
so với `Mother code` phân biệt của `Raw` (1 028).
Kỳ vọng: trùng **356** · chỉ có ở Excel **672** · chỉ có ở app **92**.
