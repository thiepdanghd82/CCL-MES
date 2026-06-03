# Phase 9 — Audit log Export + Retention PLAN

> **Status**: Export = code-ready (ship this PR). Retention = PLAN — chờ
> Henry chốt 1 trong 3 option (a/b/c) trước khi đụng phần xóa/archive.
> **Author**: 02/06/2026 sau khi merge #65 (purge WO-retain fix).
> **Reference**: PR #33 exporter pattern
> (`src/CCL.MES.Application/SpecExport/` + `Infrastructure/SpecExport/`).

---

## 0. Vấn đề

`AuditLogs` table append-only từ Phase 6 Bước 5. Mỗi mutation prod
emit 1 row (login/login fail / spec lifecycle / WO advance / NPI
import / drawing decide / TOTP enroll…). 2 vấn đề **độc lập**:

| # | Vấn đề | Giải pháp dự kiến |
|---|---|---|
| 1 | Operator **xem** + **lấy ra** audit log để incident review / compliance audit / external SIEM ingestion. Hiện tại `Pages/Settings/Logs.razor` chỉ hiển thị grid 50-row pagination — KHÔNG có nút export. | **Export feature** — CSV + XLSX. Code-ready. |
| 2 | Table tăng vô hạn. Sau 12 tháng @ ~500 row/day = ~180k row; vài năm = vài triệu row. Query tốc độ giảm; backup file lớn dần. | **Retention strategy** — 3 option dưới. |

Export là tách rời và đã code-ready (reuse pattern PR #31c). Retention
là quyết định compliance + cần Henry chốt vì xóa audit có thể vi phạm
luật/audit-trail requirement nhà máy.

---

## 1. Export — scope (CODE LUÔN trong PR này)

### 1.1 Files mới

```
src/CCL.MES.Application/AuditLogExport/
├── IAuditLogExporter.cs            ← interface (parallel ISpecListExporter)
├── AuditLogExportContext.cs        ← context record
└── CsvAuditLogExporter.cs          ← pure .NET, UTF-8 BOM + RFC 4180

src/CCL.MES.Infrastructure/AuditLogExport/
└── XlsxAuditLogExporter.cs         ← ClosedXML reuse PR #31a

src/CCL.MES.Web/Controllers/
└── AuditLogExportController.cs     ← path-segment csv/xlsx + AdminOnly
```

### 1.2 DI đăng ký (Infrastructure/DependencyInjection.cs)

```csharp
services.AddSingleton<CsvAuditLogExporter>();
services.AddSingleton<XlsxAuditLogExporter>();
services.AddSingleton<IAuditLogExporter>(sp => sp.GetRequiredService<CsvAuditLogExporter>());
services.AddSingleton<IAuditLogExporter>(sp => sp.GetRequiredService<XlsxAuditLogExporter>());
```

### 1.3 AuditLogService bổ sung

`AuditLogService.ListAsync` đã có pagination + filter. Thêm
`ListForExportAsync(search, action, actor, from, to)` — same filter
shape nhưng KHÔNG paginate (return `IReadOnlyList<AuditLog>`). Hard
cap 100k row safe-guard để không OOM trên prod box với 5 năm data
(operator filter trước nếu range lớn).

### 1.4 Controller

```
GET /api/audit-log/export/csv?search=&action=&actor=&from=&to=
GET /api/audit-log/export/xlsx?search=&action=&actor=&from=&to=
```

- `[ApiController]` + `[Route("api/audit-log/export")]`
- `[Authorize(Roles = "Admin")]` — sensitive data (login fail attempts,
  IP, target ids) → AdminOnly **trừ khi Henry override** (Q1).
- Path-segment `csv` / `xlsx` (NOT `.csv` — bài học #33).
- Audit emit `AUDIT_EXPORT` after each call: `{ format, filters, rows, filename, content_length }`.
  Đây là exception "audit-the-audit-export" — admin lấy audit log ra là
  hành động cần ghi lại (chống admin tampering trail).
- Filename pattern: `AuditLog_<yyyyMMdd-HHmmss>.<ext>`

### 1.5 UI — Logs.razor

- Thêm 2 button: `[CSV] [XLSX]` cạnh search button.
- Click → window.open URL với current filter query string (browser
  download). KHÔNG dùng JS interop heavy — just `<a href>` đơn giản
  hoặc `NavigationManager.NavigateTo(..., forceLoad: true)`.
- i18n keys mới (EN+VI):
  - `settings.syslog.export_csv` — "Export CSV" / "Xuất CSV"
  - `settings.syslog.export_xlsx` — "Export Excel" / "Xuất Excel"

### 1.6 Domain — AuditAction code mới

```csharp
// docs/PHASE9-AUDIT-RETENTION-PLAN.md §1.6 — audit-the-audit-export.
// detail JSON: { format, search, action_filter, actor_filter, from, to,
//                rows, filename, content_length }
public const string AuditExport = "AUDIT_EXPORT";
```

### 1.7 Test coverage

- `tests/CCL.MES.Tests/Integration/AuditLogExportTests.cs`:
  - Seed N audit rows → export CSV → assert N+1 lines (header + N) +
    UTF-8 BOM + RFC 4180 escape on detail JSON với embedded comma/quote
  - Export XLSX → load lại ClosedXML, assert worksheet + N+1 rows
  - Filter by action → only matching rows in export
  - Filter by date range → only matching rows in export
  - Empty result → header-only CSV + 0-row XLSX
  - Audit emit `AUDIT_EXPORT` row visible in InMemoryAuditWriter

Ước lượng: ~8-10 case integration ~200 LOC test code.

### 1.8 Cột exporter (8 col từ AuditLog entity)

| # | Header | AuditLog field | Notes |
|---|---|---|---|
| 1 | Timestamp UTC | `Timestamp` | ISO 8601 `yyyy-MM-ddTHH:mm:ss.fffZ` |
| 2 | Actor | `ActorUsername` |  |
| 3 | Role | `ActorRole` |  |
| 4 | Action | `Action` | const từ AuditAction |
| 5 | Target type | `TargetType` | null → empty |
| 6 | Target id | `TargetId` | null → empty |
| 7 | Detail (JSON) | `Detail` | full JSON string; CSV escape sẽ wrap quotes |
| 8 | IP | `IpAddress` | null → empty |
| 9 | Source | `Source` | Web / Console / Hub |

### 1.9 Ước lượng Export

- ~250 LOC code (CSV + XLSX + controller + service ext)
- ~30 LOC UI (Logs.razor button + i18n)
- ~200 LOC test
- ~2-3h dev + verify

---

## 2. Retention — 3 option (CẦN HENRY CHỐT)

### Option (a) — Export-only, KHÔNG auto-delete *(default em đề xuất)*

- Cung cấp export feature §1 + optional **scheduled archive task**
  (HostedService ghi `AuditLog_<yyyymmdd>.csv.gz` ra
  `<DATA_DIR>/Backup/AuditLogs/` mỗi tuần, KHÔNG xóa khỏi DB).
- **Audit row KHÔNG bao giờ tự động xóa** — không có HostedService
  giống SpecTrashPurge.
- Operator dọn dẹp DB → manual: ops chạy `DELETE FROM AuditLogs WHERE
  Timestamp < ?` thủ công sau khi đã backup, ký nhận.

**Pros**
- Compliance an toàn nhất — không có code path nào tự xóa audit.
- Match prod data law VN — nhà máy thường giữ trail vài năm theo
  ISO 9001 / TS 16949 hoặc theo BCP của hãng mẹ.
- ZERO migration, ZERO schema change.
- Code đơn giản (cycle scheduler optional).

**Cons**
- Bảng `AuditLogs` lớn dần (~180k/year ước tính). Sau 5 năm ~1M row →
  SQLite chậm queries không index, SQL Server OK.
- Backup file (`.db.bak.snapshot-*`) cũng lớn dần theo. Pruning audit
  rows từ live DB sẽ KHÔNG xảy ra → backup tăng ~5MB/năm trên SQLite.

**Mitigation cho cons**:
- Index trên `(Timestamp DESC)` + `(ActorUsername)` + `(Action)` (Phase
  6 đã có index `ActorUsername` + `Action`; **thêm `Timestamp DESC`**).
- UI filter mặc định 30 ngày recent — operator query thường không
  scan full table.

### Option (b) — Archive-then-prune

- HostedService chạy hàng tuần/tháng: rows > N tháng (vd 24)
  → ghi ra file CSV/JSONL archive `<DATA_DIR>/Backup/AuditLogs/Archive_<yyyyMM>.jsonl.gz`
  → verify file SHA256 → mới DELETE từ AuditLogs table.
- Archive file giữ permanent (operator backup riêng).
- DB chỉ giữ "active window" gần đây.

**Pros**
- DB nhỏ, query nhanh.
- Vẫn giữ full audit trail (trong file).

**Cons**
- Compliance phải verify rằng audit trail trên file = legally equivalent
  với DB row. Một số quy định/customer audit (vd MS audit, BMW PPAP)
  yêu cầu "system of record" KHÔNG tách trail ra file ngoài.
- Phức tạp: cần A→B→C SAFE protocol (backup → archive write → verify
  SHA → only-then DELETE). Operator phải tin archive ghi được.
- Implementation effort cao hơn (a): ~400-600 LOC + integration test.
- Cần migration nếu add `ArchiveBatchId` column lên AuditLog cho
  forensic re-link sau này (KHÔNG nếu chỉ delete hard).

**Khi nào chọn (b)**: nếu DB size đo được thực tế bị stuck (> 5GB) +
backup time/restore time vượt RTO + compliance team confirm OK với
external archive file.

### Option (c) — Hard-delete > retention (HostedService giống SpecTrashPurge)

- Cycle 24h: `DELETE FROM AuditLogs WHERE Timestamp < UtcNow.AddDays(-N)`.
- Retention env `OPS_AUDIT_RETENTION_DAYS` (vd 365).

**Pros**
- DB compact, predictable size.
- Implementation tối thiểu (~150 LOC + test).

**Cons** — Đây là option em **KHÔNG khuyên** trừ khi:
- Henry/legal confirm rằng quy định cho phép xóa cứng audit trail.
- Operator hiểu rằng login fail, role change, NPI import history > N
  ngày sẽ KHÔNG còn trên hệ thống.
- Có off-box archive đảm bảo (vd Splunk / Elastic / SIEM nhận log
  realtime). Hiện CMES chưa có SIEM integration.

### So sánh nhanh

| Tiêu chí | (a) Export-only | (b) Archive-then-prune | (c) Hard-delete |
|---|---|---|---|
| Compliance an toàn | ✅ Cao | ⚠ Trung (file != DB) | ❌ Thấp |
| DB size dài hạn | ⚠ Lớn dần | ✅ Active window | ✅ Compact |
| Code effort | XS (vài h) | M (1-2 ngày) | S (~half ngày) |
| Migration cần? | ❌ KHÔNG | Có thể (ArchiveBatchId) | ❌ KHÔNG |
| A→B→C SAFE risk | ❌ N/A | ⚠ Critical (archive write trước delete) | ⚠ Trung (live delete) |
| Phù hợp ISO 9001/IATF 16949 | ✅ Match | ⚠ Tùy customer | ❌ KHÔNG match |

### Đề xuất

**Default (a)** — ship Export §1 trong PR này; KHÔNG đụng phần xóa.
Nếu sau 12 tháng (D-0 + 1 year) DB size hoặc query latency thực sự
problem → mở Phase 10 cho (b). (c) chỉ nếu Henry có legal opinion
cho phép.

---

## 3. Q1..Qn

### Q1 — Export RBAC
- **A (default)**: `AdminOnly`. Audit trail là sensitive (chứa login
  fail attempts, IP, target ids); admin là role duy nhất "compliance
  officer surrogate".
- B: `Admin + Supervisor` — Supervisor có context vận hành nên cũng
  cần audit khi điều tra incident.

→ **A**. Supervisor có thể request admin export khi cần; tránh
audit log leak.

### Q2 — Format export ship-ready
- **A (default)**: CSV + XLSX (2 format). PDF defer (PDF audit log
  ít dùng — operator thường feed CSV vào Excel/SIEM).
- B: chỉ CSV.
- C: CSV + XLSX + PDF (đầy đủ như Spec export).

→ A. PDF audit log không có use case rõ — defer.

### Q3 — Hard cap row count cho export
- **A (default)**: 100k row safe-guard. Vượt → 400 với message
  "Refine date range (max 100k rows). Current range matches N rows."
- B: 1M row.
- C: KHÔNG cap — admin biết mình làm gì.

→ A. 100k row XLSX là ~50MB; vượt thì gen file ~1GB không health.
SIEM integration thường stream chứ không full export.

### Q4 — Audit-the-audit-export
- **A (default)**: emit `AUDIT_EXPORT` mỗi lần admin export. Detail
  ghi filter + rows + filename + content_length.
- B: KHÔNG emit — audit log không cần tự-audit (recursion).

→ A. Admin tampering trail (admin xóa audit row rồi export một bản
"sạch") cần chống → emit AUDIT_EXPORT để trail vẫn còn dấu vết.

### Q5 — Retention default cho prod
- **A (default — đề xuất)**: Option (a) export-only, KHÔNG auto-delete.
- B: Option (b) archive-then-prune sau 24 tháng.
- C: Option (c) hard-delete sau 12 tháng.

→ **CHỜ HENRY CHỐT** — đây là quyết định compliance.

### Q6 — Nếu chọn (a), có ship scheduled archive HostedService không?
- A: KHÔNG. Manual export đủ; operator tự download định kỳ.
- **B (default nếu chọn (a))**: CÓ — weekly cron viết
  `<DATA_DIR>/Backup/AuditLogs/AuditLog_<yyyyMMdd>.csv.gz`,
  giữ 12 file (3 tháng) rồi rotate. KHÔNG xóa DB row.

→ **B nếu (a)**. Nhẹ (~150 LOC) và ops không phải nhớ thủ công.

### Q7 — Nếu chọn (b) hoặc (c), retention N (ngày)?
- (b): 24 tháng (730 ngày) default.
- (c): 12 tháng (365 ngày) default.

→ Chờ option chốt trước.

### Q8 — Nếu chọn (b), archive format?
- A: JSONL gz — line-delimited, dễ stream + grep.
- B: CSV gz — Excel-friendly.
- C: Parquet — analytics-friendly nhưng cần dep mới.

→ A nếu chọn (b) — nhỏ + grep-friendly.

---

## 4. Out of scope

- SIEM integration (Splunk / Elastic forwarder) — Phase 11+.
- Audit log signing (HMAC chain để chống admin tampering) — chỉ làm
  khi customer yêu cầu cụ thể.
- Realtime audit broadcast (SignalR push tới external monitor) —
  không phải requirement hiện tại.

---

## 5. Acceptance Phase 9 audit work

- [ ] PR này merge: Export feature §1 ship + plan §2 chốt option.
- [ ] Plan §2 retention option chốt qua review (a/b/c) — ghi vào CLAUDE.md.
- [ ] Nếu (b) hoặc (c) → mở PR Phase 9.B "Retention HostedService"
  base main, **A→B→C SAFE protocol** (backup → write archive → verify
  SHA → only then delete) cho (b); seed-date 29/30/31/N boundary test
  cho (c). Predicate KHÔNG extract — test qua EF query thật (Henry's
  Option 2 from PR #65 lesson).
- [ ] Lessons learned: pattern "audit-the-audit-export" ghi vào
  `LESSONS_LEARNED.md` sau merge.

---

*Plan author: Claude. Export §1 ship cùng PR này. Retention §2 STOP
chờ Henry chốt Q5 + Q7 trước khi code phần xóa/archive.*
