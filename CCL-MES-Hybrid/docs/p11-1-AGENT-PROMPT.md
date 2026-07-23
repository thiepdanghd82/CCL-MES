# PROMPT giao agent — PR P11-1 Domain (Multi-Method Routing DAG)

> Copy toàn bộ khối dưới đây giao cho agent (khuyến nghị: **Backend
> Architect** hoặc **Senior Developer**). Prompt tự chứa; agent PHẢI đọc
> pre-flight trước khi code.

---

## VAI TRÒ
Bạn là kỹ sư backend .NET 10 / EF Core / SQLite làm việc trên CCL-MES.
Nhiệm vụ: implement **PR P11-1 Domain** — nền tảng routing DAG đa phương
pháp (fork-join) cho Work Order. CHỈ domain + migration. KHÔNG controller,
KHÔNG Razor/UI (đó là P11-2/P11-3).

## PRE-FLIGHT (BẮT BUỘC đọc trước khi viết code, đúng thứ tự)
1. `CLAUDE.md` (mục 4 EF Core safety, mục 10 phase history, UI rules).
2. `CCL-MES-Hybrid/docs/LESSONS-LEARNED.md` — toàn bộ, đặc biệt EF SQLite.
3. `CCL-MES-Hybrid/docs/SKILLS.md` — S12 (checkpoint), verify-script rule.
4. `CCL-MES-Hybrid/docs/STACKED-PR-CHECKLIST.md` — Rule 6 verify self-prep.
5. `CCL-MES-Hybrid/docs/P10.7-WO-STATE-CONTRACT.md` — §3.1 transition grid.
6. **`CCL-MES-Hybrid/docs/p11-scope-proposal.md`** — Q2/Q4/Q6/Q10 đã chốt.
7. **`CCL-MES-Hybrid/docs/p11-1-BREAKDOWN.md`** — SSOT: bảng file §1,
   entity §2, enum §3, state machine §4, resolver §5, validator §6,
   audit §7, test §8, migration §9, backward-compat §10.

Trước khi sửa file, ĐỌC các file lõi để bám đúng style thật:
`src/CCL.MES.Domain/StateMachine/{MesPhase,WorkOrderStateMachine,WoErrorCode}.cs`,
`src/CCL.MES.Domain/Entities/WorkOrder.cs`,
`src/CCL.MES.Application/Services/{QcLineResolver,ProcessLineMapSeed}.cs`,
`src/CCL.MES.Domain/Entities/ProcessLineMap.cs`,
`src/CCL.MES.Infrastructure/MesDbContext.cs`, và migration mẫu
`*AddWorkOrderRowVersionAndMesPhase*` (để copy trigger RowVersion SQLite).

## HARD CONSTRAINTS (vi phạm = reject PR)
1. **ADDITIVE ONLY**. Không xoá/đổi enum value cũ; chỉ append `MesPhase.SPLIT=13`.
   Mọi WO 1-leg (0 row `WoLeg`) chạy y hệt hôm nay.
2. **2140 test cũ PHẢI xanh** — nhất là `WorkOrderStateMachineLegacyParityTests`
   + `WorkOrderStateMachineFullMatrixTests` (tự nở 169→196 cells, KHÔNG sửa
   theory, chỉ cập nhật expected count nếu test có assert cứng số cell).
3. **EF Core safety §4 — TUYỆT ĐỐI**:
   - KHÔNG `dotnet ef migrations remove` / `database update` trỏ live DB.
   - Generate migration trên isolated `/tmp/p11-1-design.db` (lệnh §9 breakdown).
   - Type-affinity strip (§4.5): xoá `type:"TEXT|INTEGER|REAL"` +
     `.HasColumnType(...)` khỏi migration mới.
   - Backup Phase A trước, verify SHA + rowcount Phase C. KHÔNG áp live khi
     chưa được duyệt — dừng ở generate + verify trên `/tmp`.
4. **Backfill KHÔNG đụng dữ liệu WO cũ** — migration chỉ tạo bảng mới +
   cột nullable + index + trigger. WO cũ = 0 leg.
5. `WoLeg.RowVersion` dùng trigger SQLite y hệt `WorkOrder` (copy từ
   migration mẫu) — concurrency per-leg.
6. Resolver + validator là **pure helper** (không I/O, không DbContext) →
   unit-test được, tái dùng thuật toán `QcLineResolver.Classify`.
7. Op không map → `Unmapped` + `WoErrorCode.RoutingUnmapped`, **KHÔNG đoán**
   (đúng pattern QcLineResolver — log loud, hỏi người duyệt).
8. Seed `ProcessLegMap` upsert idempotent **NON-deleting** (DR-1) + boot
   probe `[seed] process_leg_map total=N`.

## DELIVERABLES (theo §1 breakdown — bám đúng path)
- 10 file mới (§1.1): WoLeg, WoLegDependency, LegPhase, LegKind,
  RoutingEnums, RoutingLegResolver, RoutingDagValidator, RoutingLegMapSeed,
  ProcessLegMap, verify-p11-1.sh.
- 8 nhóm file sửa (§1.2): MesPhase(+SPLIT), WorkOrderStateMachine(2 cell +
  CheckCondition + IsTerminalLeg), WoErrorCode(+4), WorkOrder(+Legs/+LegEdges),
  AuditAction(+WO_LEG_*), MesDbContext(3 DbSet + config + WoLegId? ×8),
  1 migration, DbSeeder(+SeedProcessLegMapAsync).
- Test mới (§8): MesPhaseSplitTransition, RoutingLegResolver (T1/T2/T3 +
  Unmapped), RoutingDagValidator (4 luật), WoLegRowVersion.

## Q6 — grain QC (đã chốt, để làm đúng ngay từ domain)
- IPQC = per-leg (driven `leg.ProcessLine` — Plan C sẽ dùng ở P11-2, ở
  P11-1 chỉ cần field ProcessLine đúng trên WoLeg).
- FQC = per product family (WO-level). P11-1 KHÔNG động FQC; chỉ ghi chú.

## QUY TRÌNH LÀM VIỆC
1. Tạo branch: `feat/p11-routing-domain` (base `main`).
2. Implement theo §1→§7 breakdown. Chạy `dotnet build` sau mỗi cụm.
3. Viết test §8, chạy `dotnet test` — PHẢI xanh cả bộ cũ + mới.
4. Generate migration theo §9 (isolated /tmp), strip type-affinity, verify
   `.schema` trên /tmp DB. **DỪNG trước khi áp live** — báo Henry.
5. Viết `verify-p11-1.sh` (Rule 6 self-prep: Down test DB copy về baseline
   trước, build + 4 test suite + migration round-trip) — chạy, **paste
   output THẬT** vào PR body (không mô tả "chắc là pass").
6. Mở PR `--base main`, body gồm: checklist §10 backward-compat (tick
   thật), output verify script, số test trước/sau (2140 → 2140+N).

## STOP-GATES (dừng + hỏi Henry, KHÔNG tự quyết)
- Nếu bất kỳ `LegacyParity*` fail → DỪNG, RCA proven (không "most likely"),
  báo trước khi sửa.
- Trước khi áp migration lên **live** `data/ccl_mes.db` → DỪNG, chờ duyệt.
- Nếu ma trận keyword TAPE-vs-ASSEMBLY (§5.2) chưa rõ với routing thật →
  DỪNG, hỏi; seed tối thiểu T1/T2/T3 mẫu + đánh dấu cần Ops xác nhận.
- Phát hiện phải sửa test cũ để pass → DỪNG (dấu hiệu phá parity).

## OUTPUT CUỐI
1 PR `feat/p11-routing-domain` + báo cáo ngắn: file đã tạo/sửa, kết quả
`dotnet test` (số pass/fail thật), output `verify-p11-1.sh`, migration
đã generate + verify trên /tmp (chưa áp live), danh sách STOP-gate đã gặp
(nếu có). KHÔNG merge — chờ Henry review.
