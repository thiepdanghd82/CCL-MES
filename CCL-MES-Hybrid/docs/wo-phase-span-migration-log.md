# Nhật ký migration — `AddWoPhaseSpan`

**Áp live:** 2026-09-20 09:42 UTC · **Nhật ký viết:** 2026-09-21 (T+1)

> Bắt buộc theo skill `cmes-migration-abc`: số liệu Phase A → B → C phải nằm
> trong một file **commit được**. File `.db` không vào git; nhật ký thì có.

## ⚠ Nhật ký này viết BÙ — đọc mục này trước

Commit `df45f87` áp migration lên `data/ccl_mes.db` **mà không kèm nhật ký**.
Năm đợt schema trước đó (`fk-cascade`, `userperm`, `ipqc-qccriteria`,
`ipqc-p1p2`, `p12`) đều có file này; đợt này sót. Phát hiện 21-09 lúc rà lại
trạng thái repo.

Hệ quả phải nói thẳng, vì nó quyết định file này đáng tin tới đâu:

| Bằng chứng | Trạng thái |
|---|---|
| Phase A — backup **có chủ đích** ngay trước khi chạm DB | ❌ không có. Thay bằng snapshot tự động (xem dưới) |
| Phase A — sha256 đo **tại thời điểm** trước migration | ❌ mất vĩnh viễn. Hash dưới đây đo ngày 21-09 |
| Phase B — DB cô lập `/tmp/*-design.db` | ❌ macOS đã dọn. Không có bản ghi nào của lệnh sinh migration |
| Phase B — `.schema` trên DB cô lập | ❌ không tái dựng được |
| Phase C — schema · index · FK · rowcount · `__EFMigrationsHistory` | ✅ đo lại được, và đã đo (số liệu dưới) |

**Không suy ra được** từ hiện trạng: migration có thật sự được sinh trên DB cô
lập hay đã trỏ thẳng vào live. Cả hai đường đều để lại đúng một schema như
nhau. Nếu ai đó cần biết chắc, câu trả lời trung thực là **không biết** — đó
chính là cái giá của việc không ghi nhật ký cùng lúc.

## Phase A — dựng lại từ snapshot tự động

Không có backup `before-phasespan` nào trong `data/Backup/SQLite/`:

```
$ ls data/Backup/SQLite/ | grep -iE "phase|span"
(không dòng nào)
```

Cứu được nhờ **backup định kỳ**, tình cờ rơi đúng 1 giờ 6 phút trước migration:

```
data/Backup/SQLite/ccl_mes.db.bak.snapshot-20260920-083653   (08:36:53 UTC)
migration 20260920094216_AddWoPhaseSpan                       (09:42:16 UTC)
```

Chứng minh đó thật sự là ảnh **trước** migration, không phải phỏng đoán theo
tên file:

```
$ sqlite3 <snapshot> "select count(*) from sqlite_master
                      where type='table' and name='WoPhaseSpans';"
0

$ sqlite3 <snapshot> "select MigrationId from __EFMigrationsHistory
                      order by MigrationId desc limit 2;"
20260916072554_UnifyQcEvidenceForeignKeys
20260916063206_AddWorkOrderChildForeignKeys
```

Bảng chưa tồn tại và migration cuối là đợt FK 16-09 ⇒ đúng baseline cần tìm.

```
sha256 snapshot  0ba9bc877276ab12b55dec7002972d8c73ad64a3ee1f2441cd2827f64b16fceb
sha256 live      7edc02880c8041526742fe3092362cec82e8540abb86228a235a4171f8fe6b9b   (đo 21-09)
```

Hai hash lệch là **đương nhiên** ở đây — không chỉ vì WAL như các đợt trước,
mà vì live đã chạy tiếp 1 ngày sau migration. Hash live ở trên chỉ có nghĩa
"ảnh lúc viết nhật ký", **không** dùng để đối chiếu trước/sau được.

## Phase B — không tái dựng được; đây là cái kiểm được bây giờ

DB cô lập đã mất. Nhưng chính file migration thì commit được và đọc được:

```
$ cat src/CCL.MES.Infrastructure/Migrations/20260920094216_AddWoPhaseSpan.cs
Up()   : 1 × CreateTable + 5 × CreateIndex
Down() : 1 × DropTable
INSERT / migrationBuilder.Sql : 0

$ grep -c 'type: "' 20260920094216_AddWoPhaseSpan.cs
0        ← type-affinity đã strip, cổng SQL Server còn provider-agnostic
```

Ba thứ trong checklist `cmes-migration-abc` **xác minh được từ file**, và cả ba đạt:

- [x] Type-affinity đã strip — 0 chỗ
- [x] Additive: bảng MỚI, không đổi nghĩa cột cũ, không đụng bảng nào khác
- [x] Rollback rẻ: `Down()` = `DROP TABLE`, không có dòng nào phải dịch ngược

Thứ thay thế được cho "chạy thử trên DB cô lập" là **10 test dựng bảng từ
migration thật** (`CCL.MES.Api.Tests/WoPhaseSpanTests.cs`), trong đó có đúng
bất biến mà index ép:

```
A_wo_can_never_have_two_open_spans_at_once
Rework_loop_keeps_every_visit_separately
Phases_that_already_have_a_time_source_are_not_duplicated (Theory)
Actor_is_system_when_the_write_path_stamped_nothing
Same_actor_twice_in_a_row_is_still_attributed_not_downgraded_to_system
Span_stores_canonical_mes_phase_not_the_legacy_step_name
```

Nó **không** thay thế Phase B (test chạy trên DB in-memory/temp của chính nó,
không chứng minh migration áp sạch lên một DB có sẵn dữ liệu). Ghi ở đây để
người đọc biết ranh giới, không phải để lấp chỗ trống.

## Phase C — đo lại trên live (21-09-2026)

### Schema sau

```
$ sqlite3 data/ccl_mes.db ".schema WoPhaseSpans"
CREATE TABLE "WoPhaseSpans" (
    "Id"        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "WoId"      INTEGER NOT NULL,
    "Phase"     TEXT    NOT NULL,
    "VisitNo"   INTEGER NOT NULL,
    "StartedAt" TEXT    NOT NULL,
    "EndedAt"   TEXT    NULL,
    "StartedBy" TEXT    NOT NULL,
    "EndedBy"   TEXT    NULL,
    "WoLegId"   INTEGER NULL,
    "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NULL,
    "UpdatedAt" TEXT NULL,     "UpdatedBy" TEXT NULL,
    CONSTRAINT "FK_WoPhaseSpans_WorkOrders_WoId"
        FOREIGN KEY ("WoId") REFERENCES "WorkOrders" ("Id") ON DELETE CASCADE
);
CREATE INDEX        "IX_WoPhaseSpans_WoId_Phase"     ON "WoPhaseSpans" ("WoId","Phase");
CREATE INDEX        "IX_WoPhaseSpans_WoId_StartedAt" ON "WoPhaseSpans" ("WoId","StartedAt");
CREATE INDEX        "IX_WoPhaseSpans_WoLegId"        ON "WoPhaseSpans" ("WoLegId");
CREATE UNIQUE INDEX "UX_WoPhaseSpans_OpenPerLeg"     ON "WoPhaseSpans" ("WoId","WoLegId")
       WHERE "EndedAt" IS NULL AND "WoLegId" IS NOT NULL;
CREATE UNIQUE INDEX "UX_WoPhaseSpans_OpenPerWo"      ON "WoPhaseSpans" ("WoId")
       WHERE "EndedAt" IS NULL AND "WoLegId" IS NULL;
```

Khớp §5.7 của `P10.7-WO-STATE-CONTRACT.md`: **CASCADE** (dữ liệu thao tác,
không phải bằng chứng QC có chữ ký — gate 29), và **hai** partial unique index
tách riêng vì SQLite coi NULL là phân biệt.

```
$ sqlite3 data/ccl_mes.db "select * from pragma_foreign_key_list('WoPhaseSpans');"
0|0|WorkOrders|WoId|Id|NO ACTION|CASCADE|NONE
```

### Rowcount trước → sau

```
                       trước    sau
WorkOrders                 2      2
WoLegs                     0      0
WoRunSessions              0      2   ← vận hành sau migration, KHÔNG phải migration
WoPauseEvents              0      1   ← vận hành sau migration
AuditLogs               3601   3626   ← +25, vận hành sau migration
__EFMigrationsHistory     56     57   ← +1, đúng một migration
WoPhaseSpans         (chưa có)     4   ← bảng mới

integrity_check    ok
foreign_key_check  0 dòng lỗi
```

**Khác các đợt trước, cột "sau" ở đây KHÔNG chứng minh "không suy suyển".**
Nó đo ở T+1, nên mọi delta đều lẫn hoạt động vận hành bình thường (chạy máy,
ghi audit). Thứ nhật ký này khẳng định được là hẹp hơn: `__EFMigrationsHistory`
+1 đúng một dòng, và không bảng nào **mất** dòng.

### Không backfill — kiểm được trên dữ liệu thật

`Up()` có 0 dòng INSERT, nên WO đi qua các công đoạn **trước** 20-09 không có
span. Đúng như vậy trên live:

```
Id  WoNo         MesPhase   span
58  WO-TEST-01   PREPRESS   4
59  WO-TEST-02   RUNNING    0   ← qua PREPRESS/IPQC ngày 18-09, trước migration
```

WO 59 trống là **đúng thiết kế**, không phải mất dữ liệu.

## Quan sát trên live — một câu hỏi còn mở

Bốn span của WO-TEST-01 đều là `PREPRESS`, `VisitNo` chạy tới **4** — tức WO bị
trả về Pre-press 3 lần. Đó chính là chỉ số rework mà bảng này sinh ra để đo, và
nó hoạt động.

```
Id  WoId  Phase     VisitNo  StartedAt                   EndedAt                     StartedBy  EndedBy
 1    58  PREPRESS        1  2026-09-20 10:26:10.611637  2026-09-20 10:26:29.519429  system     system
 2    58  PREPRESS        2  2026-09-20 10:26:29.577508  2026-09-20 10:33:08.430553  system     admin
 3    58  PREPRESS        3  2026-09-20 10:33:08.577278  2026-09-20 12:20:19.773635  admin      admin
 4    58  PREPRESS        4  2026-09-20 12:20:19.922993  (đang mở)                   admin
```

**Quy kết actor đổi giữa chừng: `system` → `admin`, đúng mốc 10:33.** Audit của
cùng WO trong cửa sổ đó cho thấy actor luôn là `admin`, kể cả ở những lần span
ghi `system`:

```
3755  WO_ADVANCE    admin  WorkOrder 58  10:26:00
3756  SYS_RECOVERY  admin  WorkOrder 58  10:26:10   ← span 1 mở, StartedBy=system
3757  WO_ADVANCE    admin  WorkOrder 58  10:26:29   ← span 1 đóng, EndedBy=system
3758  SYS_RECOVERY  admin  WorkOrder 58  10:26:29   ← span 2 mở, StartedBy=system
3759  LOGIN_OK      admin  User 1        10:33:07
3760  WO_ADVANCE    admin  WorkOrder 58  10:33:08   ← span 2 đóng, EndedBy=admin
3761  SYS_RECOVERY  admin  WorkOrder 58  10:33:08   ← span 3 mở, StartedBy=admin
```

Giả thuyết hợp lý: binary chạy trước 10:33 chưa mang fix
`WorkOrderService.AdvanceAsync` (đường **duy nhất** đổi `MesPhase` mà không
chạm `UpdatedAt`/`UpdatedBy` — sửa trong cùng PR), `LOGIN_OK` 10:33:07 là dấu
hiệu app vừa khởi động lại với bản mới.

**Chưa chứng minh được, và không được viết như đã chứng minh.** Lệnh chứng minh
phải là giờ khởi động tiến trình so với giờ commit (bẫy L7), nhưng:

```
$ ls data/Logs/
ccl-api-5100.20260917.log     ← vẫn đang được ghi ngày 21-09
$ grep -c "2026-09-20" data/Logs/*.log
0
```

Log **không có trường thời gian trên mỗi dòng** và tên file không cuộn theo
ngày, nên không định vị được lần khởi động nào. Đây là đúng thứ A3
(observability) nói tới, đo được ngay tại đây.

Hệ quả thực tế nếu giả thuyết đúng: 2 span đầu quy kết sai người (`system` thay
vì `admin`) và **không sửa được** — không backfill được thứ không ai ghi lại.
4 dòng, trên WO test. Chấp nhận.

## Rollback

```
DROP TABLE WoPhaseSpans;
DELETE FROM __EFMigrationsHistory WHERE MigrationId='20260920094216_AddWoPhaseSpan';
rm src/CCL.MES.Infrastructure/Migrations/20260920094216_AddWoPhaseSpan*.cs
git checkout <sha trước> -- src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
```

**TUYỆT ĐỐI KHÔNG** `dotnet ef migrations remove` — nó connect live và chạy
`Down()` thật (sự cố 31-05 đã DROP TABLE AuditLogs).

Mất 4 dòng span của WO test, không mất gì khác: bảng không có dữ liệu nào khác
trỏ vào nó, và `Up()` chưa bao giờ sửa bảng nào sẵn có.

## Còn phải làm

- [ ] **Lần sau nhật ký đi cùng PR schema, không viết bù.** Ba loại bằng chứng
      ở bảng đầu file mất vĩnh viễn chỉ vì trễ một ngày.
- [ ] Định vị đường ghi phát `system` (xem mục quan sát) — cần log có mốc thời
      gian trước, tức chạm A3.
- [ ] Screenshot 2 density cho `RunningDashboard` + `ShippedSummaryDashboard` —
      thay đổi UI của cùng PR này chưa ai nhìn màn thật.
