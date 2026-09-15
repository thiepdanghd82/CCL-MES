# Nhật ký migration — `AddUserPermissions` (2026-09-15)

> Bắt buộc theo skill `cmes-migration-abc`: số liệu Phase A → B → C phải nằm
> trong một file **commit được**. File `.db` không vào git; nhật ký thì có.

## STOP-gate

Thêm cột vào DB thật là **STOP-gate** theo `CLAUDE.md` §0. Thiệp yêu cầu tính
năng "bảng phân quyền cho tab Quản lý tài khoản, tích được và lưu lại" và chốt
rõ 8 cột là **quyền riêng từng người** — tức bắt buộc có chỗ lưu. **Thiệp cho
phép tường minh**; không phải agent tự vượt cổng.

## Thiết kế — vì sao migration này KHÔNG đổi hành vi của ai

8 cột **nullable**, `NULL` = *"theo vai trò"*. Mọi dòng cũ đều NULL nên vẫn suy
quyền từ vai đúng như trước; cờ chỉ có nghĩa khi admin tick tường minh. Đó là
lý do có thể áp lên DB đang chạy mà không cần cửa sổ dừng máy.

**8 cột tường minh chứ không một số nguyên bitmask.** Hồ sơ phân quyền phải
đọc được bằng SQL khi khách audit hỏi *"tháng 3 ai được duyệt QC"*. Bitmask
tiết kiệm vài byte và trả giá bằng việc không ai tra nổi.

## Phase A — baseline

```
backup : data/Backup/SQLite/ccl_mes.db.before-userperm.20260915-114410
sha256 live   4bf77f10646ca45ce2e517656ea5bc42b0cd14af02df23fda87d671162672093
sha256 backup 7c2fdb48ca8b9a83938186332dce5c9b3ccd7ccc5a7a6f583137b86fdd91c94f
```

Hai hash **lệch nhau là bình thường** — SQLite ở chế độ WAL, `.backup` tạo bản
đã checkpoint. Đối chiếu bằng **nội dung và rowcount**, không bằng hash (L-WAL).

```
Users        7
WorkOrders   2
AuditLogs    3490
Migrations   53
integrity_check    ok
foreign_key_check  0 dòng lỗi
snapshot mã        /tmp/snapshot-pre-userperm.cs
```

## Phase B — sinh + kiểm trên DB CÔ LẬP

```
MES_CONNSTR="Data Source=/tmp/userperm-design.db"   ← KHÔNG chạm live
20260915044444_AddUserPermissions.cs
type-affinity: 8 → 0        (§4.5 — giữ cổng SQL Server provider-agnostic)
```

Đã **đọc nội dung migration trước khi áp**: thuần `AddColumn` ×8, `nullable: true`,
không `AlterColumn`, không đụng dữ liệu, không rebuild bảng (nên không drop
trigger RowVersion — bẫy L38).

Áp lên `/tmp/userperm-design.db` → 8 cột INTEGER, nullable đúng.

## Phase C — áp live + bằng chứng

API **dừng trước khi áp** để không ai ghi giữa chừng, bật lại sau (HTTP 401 =
sống, đúng vì API bắt buộc đăng nhập).

```
cột Users        13 → 21     (+8 Perm*)
Users            7     → 7        (khớp baseline)
WorkOrders       2     → 2        (khớp)
AuditLogs        3490  → 3490     (khớp)
Migrations       53    → 54       (+1, đúng như mong đợi)
integrity_check        ok
foreign_key_check      0 dòng lỗi
migration mới nhất     20260915044444_AddUserPermissions
```

**7/7 dòng có cả 8 cột = NULL** ⇒ không tài khoản nào bị đổi quyền.

## Đường lùi

`rm` file migration + `git checkout` snapshot. **KHÔNG** dùng
`dotnet ef migrations remove` — lệnh đó connect DB thật và chạy `Down()`, đã
DROP TABLE AuditLogs một lần ngày 2026-05-31.

Migration này thuần bổ sung và mọi giá trị đều NULL, nên bỏ cột đi cũng không
mất dữ liệu nghiệp vụ nào.
