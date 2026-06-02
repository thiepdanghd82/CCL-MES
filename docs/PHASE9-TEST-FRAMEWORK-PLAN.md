# Phase 9 — Test Framework PLAN

> **Status**: PLAN — chờ duyệt trước khi tạo branch T1.
> **Author**: 02/06/2026, sau khi merge PR #60 (RBAC hardening) và đóng
> task carry-over SpecService PagingHelper (no-op).
> **Goal**: biến các harness script ad-hoc + verify thủ công thành
> regression suite có thể chạy mỗi PR. Khóa lại các preservation
> guarantee rủi ro cao (state machine, RBAC, lifecycle, blob security,
> migration safety) — đó là những thứ Henry và Claude đã verify thủ
> công 5–10 lần qua các sprint Phase 8.

---

## 0. Hard constraints (recap để mỗi PR audit lại)

1. **Test project OUT-OF-PRODUCTION**: solution có 4 prod project
   (Domain / Application / Infrastructure / Web). Thêm 1 project test
   mới `tests/CCL.MES.Tests/` — KHÔNG ai reference vào nó từ runtime,
   chỉ runtime ngược lại (test → prod). Web/Program.cs không đổi.
2. **Isolated DB cho integration**: mỗi test integration boot 1 SQLite
   ở `/tmp/ccl-mes-tests-<guid>/test.db` (theo pattern
   `scripts/VerifyDrawingsUpload`). KHÔNG đụng `data/ccl_mes.db` của
   dev runtime. Bài học A→B→C (sửa lifecycle ngay trên live DB lúc
   PR-L3 dev) đã được nêu trong Lessons Learned.
3. **KHÔNG đổi production code "để test cho dễ"**: nếu cần inject
   (vd `IClock`, hoặc tách 1 helper static cho dễ test) → ghi TRONG
   plan như follow-up ticket, KHÔNG tự refactor ngầm.
4. **Reuse logic harness có sẵn**: VerifyPrB / VerifyBlobStore /
   VerifyDrawingsUpload đã chứa builders + assertion patterns chuẩn —
   T1/T2 port nguyên xi vào xUnit fixture, KHÔNG viết lại từ đầu.
5. **Bảo toàn baseline + vùng cấm READ-ONLY**: test project add vào
   `CCL.MES.sln` nhưng KHÔNG đụng Phase 6 state machine / 4 NPI tab /
   sibling Ops Control v1.2 / SpecHub / Old ver. Test READ surface đó.
6. **Behavior-preserving**: harness logs cũ (PASS/FAIL line format)
   được port thành `Assert.Equal/True/Throws` của xUnit — assertion
   semantics y hệt, không nới lỏng case nào.

---

## 1. Audit harness hiện có

| Harness | LOC | Project ref | Cover gì | Mức tin cậy |
|---|---|---|---|---|
| `scripts/VerifyPrB/Program.cs` | 218 | Application + Infrastructure (SpecExport) | PDF dispatch SILK / FLEXO / GENERIC×3 (INDIGO empty / LETTER+silk rows / DIECUT+flexo cut) — 5 test cases | Cao — pass criterion là PDF byte[] non-empty + no exception; output `/tmp/pr-b-verify/`. Đã chạy thủ công nhiều lần. |
| `scripts/VerifyBlobStore/Program.cs` | 248 | Application + Infrastructure (Storage) | FilesystemBlobStore round-trip + idempotency + 6 security guards (traversal A/B / oversize / extension allowlist / probe-resistance / delete safety) + containment audit — 8 cases | Cao — fail count thành exit code; reproducer chuẩn cho Lesson "6 security guards". |
| `scripts/VerifyDrawingsUpload/Program.cs` | 510 | Application + Infrastructure + Domain (bootstrap MesDbContext) | DrawingsService upload + download + approval chain RBAC — 18 cases: upload large file, SHA round-trip, v2 advance, bad extension rollback, role reject, scoped download, 3-chip approval state machine, reject-comment-required, supersede cascade, dept-mismatch RBAC, admin override, re-decide flip | Cao — đây là harness PR-D-5b/c verbatim; pattern bootstrap EF + InMemoryAuditWriter chuẩn để port. |
| `scripts/BackupRestore/Program.cs` | 165 | Application + Infrastructure | Backup-restore CLI tool — KHÔNG phải test harness, là utility ops dùng tay. | N/A (utility) |
| `scripts/RecoverAdmin/Program.cs` | 147 | Application + Infrastructure | Sys-recovery CLI — KHÔNG phải test harness. | N/A (utility) |

**Verify thủ công khác (trong PR body)**, KHÔNG có harness — chỉ là `curl` matrix + diff:

| Verify (manual) | Sprint / PR | Status sau Phase 9 |
|---|---|---|
| 5 roles × 11 endpoints = 55 cell RBAC matrix | PR #60 (RBAC hardening §6.2 + §6.3) | Cover bởi T1 unit `CanActAs` + T2 integration controller smoke (đề xuất). |
| 9 functional tests Spec Copy + Edit / Revise + Supersede / Trash + Restore + Purge | PR-L1 / L2 / L3 | Cover bởi T2 integration lifecycle. |
| Purge date-boundary 29d KEEP / 30d KEEP / 31d PURGE (3 cycles isolated /tmp) | PR-L3 | Cover bởi T2 integration purge SQL boundary. |
| WO blocker defence-in-depth — purge skip + audit SKIPPED | PR-L3 | Cover bởi T2 integration. |
| Blob cleanup (rev với .pdf file thật → deleted sau cascade) | PR-L3 | Cover bởi T2 integration purge blob path. |
| Spec lifecycle migration A→B (in_memory hot-fix) | PR-L1 → PR-L3 | Cover bởi T2 — mỗi test 1 DB fresh + EF MigrateAsync. |
| NPI CSV import (4 target: WorkCenter / RawMaterial / Routine / Structure) | Phase 7 hạng mục 1 | Cover bởi T2 integration import. |

**Kết luận audit**: 3 harness chuyên dụng (≈976 LOC) đã cover 60-70%
test cần làm. Phase 9 chủ yếu là port + xUnit-ify + thêm ~30% còn lại
(state machine 7+9 cases, lifecycle, RBAC controller smoke, CSV
parser, paging, purge SQL boundary).

---

## 2. CI hiện tại

`.github/workflows/` KHÔNG tồn tại. Không có Azure Pipelines /
GitLab-CI / Jenkinsfile. Project hiện tại chạy bằng `dotnet run` thủ
công + verify trong PR.

**Đề xuất**: Phase 9 ship cùng 1 file `.github/workflows/ci.yml`
chạy `dotnet test` mỗi push + PR. Optional cho T1, mandatory ở T2.
Mac runner (matches dev environment) + ubuntu-latest cũng OK vì cả
2 đều có .NET 10 + SQLite trong runtime. **Nếu Henry không muốn
GitHub Actions** (vd vì repo private quota), có thể defer CI gate
sang skill `dotnet-test-pre-commit` chạy local — báo cáo trong Q.

---

## 3. Solution layout đề xuất

```
CCL-MES/
├── CCL.MES.sln                 ← add 1 project mới ở section Tests
├── src/                        ← KHÔNG đổi (READ-ONLY)
│   ├── CCL.MES.Domain/
│   ├── CCL.MES.Application/
│   ├── CCL.MES.Infrastructure/
│   └── CCL.MES.Web/
├── scripts/                    ← KHÔNG đổi (harness ad-hoc giữ nguyên để ops chạy nhanh)
│   ├── BackupRestore/
│   ├── RecoverAdmin/
│   ├── VerifyBlobStore/
│   ├── VerifyDrawingsUpload/
│   └── VerifyPrB/
└── tests/                      ← MỚI
    └── CCL.MES.Tests/
        ├── CCL.MES.Tests.csproj
        ├── Unit/
        │   ├── WorkOrderStateMachineTests.cs        ← 7 happy + 9 error code paths
        │   ├── WorkOrderStatusBadgeTests.cs         ← 9 SpecHub badge cases
        │   ├── SpecRevisionHelpersTests.cs          ← NextRev + NextAvailableRev + CompareRev
        │   ├── PagingHelperTests.cs                 ← clamp page + clamp size + sequencing
        │   ├── DrawingsService_CanActAsTests.cs     ← 3 chip × {Admin / Engineer×3 dept / Supervisor / others}
        │   ├── BlobStoreSuggestedKeyRegexTests.cs   ← Lesson "regex token classes" lock-in
        │   └── NpiCsvParserTests.cs                 ← RFC-4180 edge cases + missing required
        └── Integration/
            ├── _Support/
            │   ├── IsolatedDbFixture.cs             ← boot /tmp SQLite + Migrate + Seed minimal
            │   ├── InMemoryAuditWriter.cs           ← port từ VerifyDrawingsUpload
            │   └── TestBlobStore.cs                 ← FilesystemBlobStore wired tới /tmp
            ├── BlobStoreIntegrationTests.cs         ← port từ VerifyBlobStore (8 cases)
            ├── DrawingsServiceIntegrationTests.cs   ← port từ VerifyDrawingsUpload (18 cases)
            ├── SpecPdfDispatchTests.cs              ← port từ VerifyPrB (5 cases)
            ├── SpecLifecycleCopyTests.cs            ← Copy + collision NextAvailableRev
            ├── SpecLifecycleReviseTests.cs          ← Revise auto-supersede + ChangeSummary
            ├── SpecLifecycleTrashRestoreTests.cs    ← Trash + WO blocker + Restore
            ├── SpecTrashPurgeServiceTests.cs        ← RunCycleAsync 6-rule contract
            └── NpiImportServiceTests.cs             ← 4 target apply + DELETE+INSERT atomic + backup
```

**Test framework chọn xUnit**: lý do (a) project hiện tại đã .NET 10
chuẩn — xUnit chạy `dotnet test` không cần thêm runner config, (b)
xUnit theory + InlineData parametrize sạch hơn NUnit TestCase, (c)
async test default — phù hợp pattern EF Core + Service Layer của
codebase. Alternative NUnit / MSTest cũng hoạt động nhưng không có
lợi gì hơn. Q1 dưới.

**FluentAssertions optional** — đẹp hơn `Assert.Equal` nhưng thêm 1
dep. Default ABSTAIN — dùng `Assert.*` native xUnit để khớp pattern
harness sẵn có. Q2.

---

## 4. Priority — khóa preservation guarantee rủi ro cao trước

Sắp xếp theo **rủi ro nếu broken (P0 → P3)**:

### P0 — data corruption / silent regression nếu lỗi

1. **WorkOrderStateMachine 7 transitions + 9 error codes** —
   `src/CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs` 79 LOC,
   `WoErrorCode.cs` 9 values. Đã verify thủ công 5+ lần qua Phase 5/6.
   Test: 7 happy + 9 deny (mỗi error code 1 case). **T1**.
2. **FilesystemBlobStore 6 security guards** — port `VerifyBlobStore`
   (8 cases). Path traversal A/B + oversize + extension + probe +
   delete + containment audit. **T1 (unit regex + T2 IO integration)**.
3. **SpecTrashPurgeService 6-rule safety contract** — port logic
   verify PR-L3 đã chạy (29d KEEP / 30d KEEP / 31d PURGE + WO blocker
   + blob cleanup + idempotency). **T2**.
4. **DrawingsService.CanActAs RBAC matrix** — đảm bảo Admin override
   + Engineer×dept + Supervisor + reject. Đây là defence-in-depth duy
   nhất cho approval chain. **T1**.

### P1 — lifecycle correctness / cascade

5. **Spec Copy + Revise deep-clone via CloneSpecContent** — Q6 đã chốt
   "4 sub-specs + nested" được clone; QcCapture + Drawing KHÔNG. T2
   khóa bằng integration test → count rows trước/sau. **T2**.
6. **NextAvailableRev collision handling** — Copy/Revise pick A nếu
   product mới; bump B/C nếu cùng product. **T1 (unit)** + **T2
   (collision integration)**.
7. **Spec Trash WO blocker** — TrashAsync với active WO → ActiveWorkOrders
   result. Đã hit empirically PR-L3. **T2**.

### P2 — đầu ra ổn định / format

8. **WorkOrderStatusBadge 9 SpecHub states** — 9 case từ NEW →
   CANCELLED. Pure helper, mock WO + LastQc. **T1**.
9. **PagingHelper clamp + Skip/Take** — page<1 → 1; pageSize<1 hoặc
   >500 → 50. Đã consolidate (xem Lessons Learned). **T1**.
10. **PDF dispatch SILK/FLEXO/GENERIC×3** — port VerifyPrB. PDF
    byte[] non-empty + no exception. **T2**.

### P3 — tooling correctness

11. **NpiCsvParser RFC-4180 + missing required** — Phase 7 csv import
    có 4 target, parser pure. **T1 (unit parser)** + **T2 (apply +
    audit + backup)**.

---

## 5. Đề xuất chia PR (T1 → T2 → T3)

### T1 — Scaffold + core unit tests (≈3 ngày dev + review)

**Scope**:
- Tạo `tests/CCL.MES.Tests/CCL.MES.Tests.csproj` (xUnit + xunit.runner.visualstudio + Microsoft.NET.Test.Sdk + ref to Domain/Application/Infrastructure).
- Add vào `CCL.MES.sln` ở solution folder mới "tests".
- Unit tests P0–P3 thuần tính (không EF):
  - `WorkOrderStateMachineTests` — 7 happy + 9 deny case (mỗi WoErrorCode 1 test).
  - `WorkOrderStatusBadgeTests` — 9 case từ SpecHub palette.
  - `SpecRevisionHelpersTests` — NextRev (A→B, Z→AA, AZ→BA, ZZ→AAA, null→A) + NextAvailableRev (empty/[A]→B/[A,Z]→AA) + CompareRev.
  - `PagingHelperTests` — clamp page/size + Skip/Take chuẩn.
  - `DrawingsService_CanActAsTests` — 12-15 case bảng RBAC (3 chip × 5 actor profile).
  - `BlobStoreSuggestedKeyRegexTests` — regex SuggestedKeyRx + StoredKeyRx (port từ VerifyBlobStore case 3+4+7 ở dạng regex direct).
  - `NpiCsvParserTests` — UTF-8 BOM, quoted field embedded `""`, CRLF, missing required → MissingRequired.Count>0.
- Optional: `.github/workflows/ci.yml` chạy `dotnet test --no-build --verbosity normal`.

**Ước lượng**: 7-8 test class × ~10-15 case = ~90 test method. Scaffold project ~30 phút. Mỗi test class ~30-45 phút. Tổng ~4-6h dev + review.

**KHÔNG đụng**: prod code, integration EF, lifecycle services.

**Acceptance T1**: `dotnet test` exit 0 + ≥90 case pass. `dotnet build` clean. Baseline + vùng cấm intact (diff prod code = 0 LOC).

---

### T2 — Integration EF + lifecycle + import (≈5 ngày dev + review)

**Scope**:
- `Integration/_Support/IsolatedDbFixture.cs` — pattern chuẩn boot SQLite `/tmp/ccl-mes-tests-<guid>/test.db`, `db.Database.MigrateAsync()`, seed minimal (1 customer + 1 product + 1 rev). Dispose xóa /tmp.
- `Integration/_Support/InMemoryAuditWriter.cs` — port từ VerifyDrawingsUpload (no-op IAuditWriter).
- `BlobStoreIntegrationTests` — port 8 case VerifyBlobStore + containment audit.
- `DrawingsServiceIntegrationTests` — port 18 case VerifyDrawingsUpload (PR-D-5b upload + PR-D-5c approval chain).
- `SpecPdfDispatchTests` — port 5 case VerifyPrB.
- `SpecLifecycleCopyTests` — happy Copy + duplicate SpecCode reject + product collision NextAvailableRev.
- `SpecLifecycleReviseTests` — Revise auto-supersede source + ChangeSummary preserve.
- `SpecLifecycleTrashRestoreTests` — Trash success + Trash blocked bởi active WO + Restore.
- `SpecTrashPurgeServiceTests` — RunCycleAsync với date setup 29d/30d/31d (3 boundary) + WO blocker → SKIP + audit SKIPPED + blob cleanup count.
- `NpiImportServiceTests` — 4 target (WorkCenter/RawMaterial/Routine/Structure), parse → apply → assert OldCount/NewCount + backup file exists + audit emit.

**Ước lượng**: ~8 integration class × 5-10 case = ~60-70 test method. Mỗi case có EF migrate ~50-100ms × 60 = 3-6s chạy toàn suite. Acceptable.

**Acceptance T2**: `dotnet test` exit 0 + tất cả case T1+T2 pass. `dotnet test --filter Category=Integration` riêng chạy <30s. CI workflow gate enforced.

**Follow-up tickets nếu cần refactor (đề xuất, KHÔNG tự làm)**:
- `IClock` injection cho `SpecTrashPurgeService` thay vì `DateTime.UtcNow` — hiện tại test phải set `OPS_SPEC_TRASH_RETENTION_DAYS=1` + insert `TrashedAt = UtcNow.AddDays(-2)` thay vì freeze clock. Trade-off OK.
- `MesDbContext` constructor để integration test KHÔNG cần qua Web project — kiểm xem Infrastructure đã expose chưa.

---

### T3 — E2E Playwright (DEFER hoặc optional, ≈1 tuần)

**Đề xuất DEFER** — sau khi T1 + T2 đã shield được 80% surface, E2E
Playwright thêm 20% còn lại (UI flow: Engineer Spec Copy modal →
form submit → grid refresh, WorkOrder drawer Advance button). Chi
phí cao (boot Web project + headless browser + auth seed) so với
benefit (đã có manual screenshot UI verify mỗi PR).

**Nếu cần**: T3 scope riêng — `tests/CCL.MES.E2E/` với Microsoft.Playwright.NUnit (hoặc xUnit harness). Scope tối thiểu: 5 happy path (login → spec list → spec copy → spec trash → drawing upload). Estimate 1 tuần.

**Khuyến nghị**: KHÔNG ship T3 cùng Phase 9. Đợi 2-3 sprint sau khi T1+T2 đã ổn định + thực sự có UI regression chưa cover thì mới mở T3.

---

## 6. Risk + tradeoff

| Risk | Mitigation |
|---|---|
| Integration test slow → developer skip `dotnet test` | xUnit `[Trait("Category", "Integration")]` filter; T1 unit chạy <1s, T2 integration 3-10s. Skill tester có thể chạy `--filter "Category!=Integration"` local. |
| /tmp DB không clean → /tmp filled | IsolatedDbFixture implement `IDisposable.Dispose()` xóa /tmp. Mỗi test class fresh fixture (xUnit `IClassFixture<T>`). |
| Test phụ thuộc thứ tự (Order dependence) | xUnit mặc định parallel ở class level, sequential ở method level. Mỗi test class = 1 fixture → no cross-test pollution. |
| Migration schema thay đổi → test break | Khi schema mới, mỗi test fresh `MigrateAsync()` → tự pick up. Đó là behavior mong muốn. |
| Prod code "for testability" refactor lan ra | T1+T2 KHÔNG refactor prod. Nếu chạm 1 dòng prod, ghi rõ trong PR description + lý do; thông thường defer sang follow-up ticket. |

---

## 7. Câu hỏi cần Henry chốt (Q1..Q12)

### Q1 — Test framework
- **A (default)**: xUnit + Microsoft.NET.Test.Sdk + xunit.runner.visualstudio.
- B: NUnit 4.
- C: MSTest.

→ A — xUnit hợp pattern async + theory + đã chuẩn .NET 10.

### Q2 — Assertion lib
- **A (default)**: `Xunit.Assert` native.
- B: thêm FluentAssertions (đẹp + verbose pass/fail message).

→ A — khớp harness pattern, ít dep, đỡ License license noise.

### Q3 — DB cho integration
- **A (default)**: SQLite trên `/tmp/ccl-mes-tests-<guid>/test.db` (port từ VerifyDrawingsUpload).
- B: `Microsoft.EntityFrameworkCore.InMemory` (faster, nhưng provider khác → test không bắt được SQLite-specific bug).
- C: SQLite in-memory `Filename=:memory:` (chỉ sống trong connection).

→ A — đúng provider prod, bắt được Lesson 27 (SqlServer.dot-extension), Lesson 30 (Cascade + RESTRICT).

### Q4 — Tests folder name
- **A (default)**: `tests/CCL.MES.Tests/` (số nhiều, hợp .NET convention).
- B: `test/CCL.MES.Tests/`.

→ A.

### Q5 — Test class layout
- **A (default)**: Unit/ + Integration/ subfolder (như mục 3).
- B: Flat layout, dùng `[Trait]` phân loại.

→ A — easier để filter `dotnet test --filter FullyQualifiedName~Unit`.

### Q6 — CI gate
- **A**: Ship CI workflow trong T1 (GitHub Actions `dotnet test` mỗi push + PR).
- **B (default)**: T1 không CI; T2 ship CI gate cùng integration.
- C: Không CI, document `dotnet test` trong CLAUDE.md, skip-gate.

→ B — T1 unit ổn định trước, T2 mới gate integration vì cần `.github` setup repo-level. Nếu Henry không xài GitHub Actions → C.

### Q7 — Coverage report
- **A (default)**: Coverlet collect coverage + report ở local; KHÔNG enforce threshold.
- B: Enforce ≥80% line coverage trong CI.
- C: Skip coverage hoàn toàn.

→ A — coverage là metric; gate fail/pass theo coverage ép developer test rác. Soft target 70% line coverage 6 tháng sau.

### Q8 — Harness script cũ giữ hay xóa?
- **A (default)**: Giữ nguyên scripts/Verify* trong solution (ops chạy nhanh khi debug); T2 integration là port chứ KHÔNG thay thế.
- B: Xóa scripts/Verify* sau khi T2 merge — single source of truth là tests/.

→ A — harness chạy `dotnet run --project scripts/VerifyBlobStore` không cần test SDK; ops/dev sometimes muốn quick reproduce.

### Q9 — SpecTrashPurgeService test approach
- **A (default)**: Set `OPS_SPEC_TRASH_RETENTION_DAYS=1` env + seed `TrashedAt = UtcNow.AddDays(-2)/-1/0` cho 3 boundary case (31d/30d/29d effective).
- B: Refactor service inject `IClock`, test freeze clock.

→ A — không đụng prod code. Trade-off chấp nhận được vì test set env một lần ở fixture ctor.

### Q10 — Test data seed strategy
- **A (default)**: Mỗi `IClassFixture` seed minimal (1 customer + 1 product + 1 rev). Helper method `SeedRevision(int n)` cho test cần nhiều rev.
- B: Test data builder pattern (Fluent API) — verbose hơn nhưng đọc dễ.

→ A — đủ minimal.

### Q11 — IAuditWriter trong integration
- **A (default)**: `InMemoryAuditWriter` capture vào `List<AuditRow>` để test assert (port từ VerifyDrawingsUpload + thêm capture).
- B: Real `AuditWriter` → live DB.

→ A — test cần assert audit event emit (vd Spec_PURGE SKIPPED case).

### Q12 — Test E2E Playwright
- **A (default)**: DEFER — mở T3 sau 2-3 sprint khi T1+T2 stable.
- B: Ship cùng T2.
- C: Không bao giờ E2E — chỉ unit + integration.

→ A — chi phí lớn so với marginal coverage. C cũng OK nếu Henry không muốn maintain UI test.

---

## 8. Out of scope Phase 9

- Performance benchmark (BenchmarkDotNet) — defer.
- Mutation testing (Stryker.NET) — defer.
- Load test API (NBomber / k6) — defer.
- UI snapshot test Blazor — defer.
- Migration roll-back test (rollback giả lập down migration) — defer, prod KHÔNG roll back migration mà restore từ backup.

---

## 9. Acceptance Phase 9 hoàn tất

- [ ] T1 merged: `tests/CCL.MES.Tests/` + ≥90 unit test pass.
- [ ] T2 merged: ≥60 integration test pass, /tmp DB cleanup OK, `dotnet test` chạy <30s toàn suite, CI workflow xanh.
- [ ] T3 status (defer | ship | not-applicable) ghi rõ.
- [ ] Mọi harness `scripts/Verify*` vẫn `dotnet run` OK (backward compat).
- [ ] CLAUDE.md cập nhật 1 paragraph "Test Framework v1" + grep command `dotnet test` reference.
- [ ] Lessons learned: 1 entry mới ghi pattern `IsolatedDbFixture` + `/tmp` isolation.

---

## 10. Cấu trúc commit T1 (preview)

```
feat(tests): Phase 9 T1 — xUnit scaffold + 7 unit test class

* tests/CCL.MES.Tests/CCL.MES.Tests.csproj (new, .NET 10 + xUnit)
* CCL.MES.sln — add project tới solution folder "tests"
* tests/CCL.MES.Tests/Unit/WorkOrderStateMachineTests.cs (16 case)
* tests/CCL.MES.Tests/Unit/WorkOrderStatusBadgeTests.cs (9 case)
* tests/CCL.MES.Tests/Unit/SpecRevisionHelpersTests.cs (12 case)
* tests/CCL.MES.Tests/Unit/PagingHelperTests.cs (8 case)
* tests/CCL.MES.Tests/Unit/DrawingsService_CanActAsTests.cs (15 case)
* tests/CCL.MES.Tests/Unit/BlobStoreSuggestedKeyRegexTests.cs (10 case)
* tests/CCL.MES.Tests/Unit/NpiCsvParserTests.cs (8 case)
* (KHÔNG đổi production code)
* (KHÔNG ship CI workflow trong T1 — sẽ ship cùng T2)
```

---

*Plan author: Claude. STOP — chờ Henry duyệt Q1..Q12 + cấu trúc + chia PR. Sau khi duyệt sẽ tạo branch `tests/phase9-t1-scaffold` base `main`.*
