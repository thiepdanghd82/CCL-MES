# Nhật ký migration — `AddWoMaterialIpqcWaiver` (D3, 2026-09-25)

Hợp đồng: `P10.7-WO-STATE-CONTRACT.md` §5.8. Quyết định: Henry, tờ trình D3
(`d3a` = ghi về một nguồn sự thật; Reject = gỡ dấu, chặn lại).

## Thay đổi

5 cột **nullable** thêm vào `WoMaterials`: `IpqcWaiverAt` · `IpqcWaiverBy` ·
`IpqcWaiverLotNo` · `IpqcWaiverLotStatus` · `IpqcWaiverReason`.
Additive thuần — không đổi nghĩa cột cũ, không backfill, không đụng bảng khác.

```
$ grep -c 'type: "' 20260925024952_AddWoMaterialIpqcWaiver.cs
0        ← type-affinity đã strip
```

## Phase B — áp lên BẢN SAO DB live (không phải DB rỗng)

Sinh migration trỏ DB cô lập trong scratchpad (`MES_CONNSTR`), không trỏ live.
Áp lên bản sao `sqlite3 .backup` của `data/ccl_mes.db`:

| | Trước | Sau |
| --- | --- | --- |
| `WoMaterials` | 8 | 8 |
| `WorkOrders` | 2 | 2 |
| trigger | 8 | 8 |
| `__EFMigrationsHistory` | 57 | 58 |
| `integrity_check` | ok | ok |
| `foreign_key_check` | 0 | 0 |

```
IpqcWaiverAt|TEXT|0          (notnull = 0 cả 5 cột)
IpqcWaiverBy|TEXT|0
IpqcWaiverLotNo|TEXT|0
IpqcWaiverLotStatus|TEXT|0
IpqcWaiverReason|TEXT|0
```

## Phase A — baseline live (25-09-2026 10:06, Henry duyệt)

API **dừng trước khi áp** (`api-service.sh stop`): cổng 5100 = 0 tiến trình,
`lsof data/ccl_mes.db` = 0.

```
backup : data/Backup/SQLite/ccl_mes.db.before-d3-ipqc-waiver.20260925-100603
sha256 live   34988b390ff24dd08287019fcbabafc5f5a7a2c3a186698c62b64a26ac2da0f4
sha256 backup 3e60d465fa6242d268e20aa7b77125cb4e14a9dda6138951bf2ac1f3edff9a39
```

Hai hash lệch là bình thường — WAL, `.backup` tạo bản đã checkpoint. Đối chiếu
bằng rowcount (cùng luật nhật ký FK-cascade · userperm): live = backup ở mọi dòng
bảng dưới.

## Phase C — áp live

```
Applying migration '20260925024952_AddWoMaterialIpqcWaiver'.
Done.
```

| | Trước | Sau |
| --- | --- | --- |
| `WoMaterials` | 8 | 8 |
| `WorkOrders` | 2 | 2 |
| `WoIpqcMaterialChecks` | 4 | 4 |
| `AuditLogs` | 3646 | 3646 |
| trigger | 8 | 8 |
| `__EFMigrationsHistory` | 57 | 58 |
| `integrity_check` | ok | ok |
| `foreign_key_check` | 0 | 0 |

5 cột mới `notnull = 0`. Migration mới nhất: `20260925024952_AddWoMaterialIpqcWaiver`.

API bật lại bằng Release build của nhánh (`api-service.sh install`): `/health`
200 · `[boot] Database migration check: up-to-date.` · DB path = live.

## Rollback

`Down()` = 5 × `DropColumn`. Trên SQLite EF có thể **rebuild bảng** để drop cột,
và rebuild **xoá trigger** (L38) — nên đường rollback đúng là **restore từ backup
Phase A**, không chạy `Down()`. Undo trong code: `rm` 2 file migration +
`git checkout` snapshot. **Không** `ef migrations remove`.
