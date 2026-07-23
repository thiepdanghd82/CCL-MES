# P11-1 Domain — Implementation Breakdown

> **Stack**: P11 — Multi-Method Routing (Fork-Join DAG). PR đầu tiên.
> **Scope**: DOMAIN + MIGRATION ONLY. KHÔNG controller, KHÔNG Razor/UI.
> **Nguồn**: [p11-scope-proposal.md](./p11-scope-proposal.md) (Q2/Q4/Q6/Q10
> đã chốt 2026-07-23) + [P10.7-WO-STATE-CONTRACT.md](./P10.7-WO-STATE-CONTRACT.md).
> **Nguyên tắc vàng**: ADDITIVE. Mọi WO cũ (0 leg) chạy y hệt hôm nay.
> 2140 test hiện tại PHẢI còn xanh.

---

## 0. Định nghĩa "done" của P11-1

1. Build xanh cả solution (legacy + Hybrid).
2. 2140 test cũ còn pass (0 regression) — đặc biệt `LegacyParity*` +
   `WorkOrderStateMachineFullMatrixTests` (auto nở 13→14 = 169→196 cells).
3. Test mới của P11-1 pass (danh sách §8).
4. Migration round-trip trên isolated `/tmp` DB: `update` → `.schema` →
   backfill để WO cũ = 0 leg (không đổi 1 byte dữ liệu WO cũ).
5. `verify-p11-1.sh` self-prep (Rule 6) in output THẬT, exit 0.
6. Gate scripts pass: `gate-no-hardcoded-hex.sh` (không đụng CSS nên
   trivially pass), `audit-state-machine-emits.sh` (chưa có controller
   nên N/A — ghi rõ).

**KHÔNG làm trong P11-1**: endpoint, DTO, Razor, Client wrapper,
`InputSource=FROM_STOCK` runtime (chỉ khai báo enum + để `IN_LINE`),
`SemiLot` (đó là P11.5).

---

## 1. Bảng thay đổi file (SSOT cho agent)

### 1.1 File MỚI

| Path | Nội dung |
|---|---|
| `src/CCL.MES.Domain/Entities/WoLeg.cs` | entity `WoLeg` (§2.1) |
| `src/CCL.MES.Domain/Entities/WoLegDependency.cs` | entity cạnh DAG (§2.2) |
| `src/CCL.MES.Domain/StateMachine/LegPhase.cs` | enum sub-phase leg (§3.1) |
| `src/CCL.MES.Domain/Routing/LegKind.cs` | enum `LegKind` (§3.2) |
| `src/CCL.MES.Domain/Routing/RoutingEnums.cs` | `InputSource` + `DependencyGate` + `SurfaceProfile` (§3.2) |
| `src/CCL.MES.Domain/Routing/RoutingLegResolver.cs` | pure helper: routing ops → leg DAG (§5) |
| `src/CCL.MES.Domain/Routing/RoutingDagValidator.cs` | pure helper: no-cycle + assembly-inputs + terminal-reaches-FQC (§6) |
| `src/CCL.MES.Application/Services/RoutingLegMapSeed.cs` | seed data-driven (mirror `ProcessLineMapSeed`) (§5.2) |
| `src/CCL.MES.Domain/Entities/ProcessLegMap.cs` | entity nạp seed (mirror `ProcessLineMap`) |
| `CCL-MES-Hybrid/scripts/verify-p11-1.sh` | verify belt Rule 6 self-prep |

### 1.2 File SỬA (additive, không phá parity)

| Path | Thay đổi |
|---|---|
| `src/CCL.MES.Domain/StateMachine/MesPhase.cs` | `+ SPLIT = 13` (§3.3) |
| `src/CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs` | `CanonicalFlow += SPLIT`; 2 cell mới trong `ClassifyTransition`; `CheckCondition` case; helper `IsTerminalLeg` (§4) |
| `src/CCL.MES.Domain/StateMachine/WoErrorCode.cs` | `+ LegsNotAllDone`, `+ RoutingUnmapped`, `+ AssemblyInputsMissing`, `+ InvalidRoutingDag` |
| `src/CCL.MES.Domain/Entities/WorkOrder.cs` | `+ List<WoLeg> Legs`, `+ List<WoLegDependency> LegEdges`; đổi default cascade KHÔNG đổi phase khởi tạo |
| `src/CCL.MES.Domain/Audit/AuditAction.cs` | `+ WO_LEG_*` (§7, alphabetical) |
| `src/CCL.MES.Infrastructure/MesDbContext.cs` | `DbSet<WoLeg>`, `DbSet<WoLegDependency>`, `DbSet<ProcessLegMap>` + `OnModelCreating` config (index, conversion, maxlen) + `+ WoLegId?` shadow/FK trên 8 surface entity (§2.3) |
| `src/CCL.MES.Infrastructure/Migrations/` | 1 migration mới (§ generate theo quy trình §4-EF-safety) |
| `src/CCL.MES.Application/Services/DbSeeder.cs` (hoặc tương đương) | `SeedProcessLegMapAsync` upsert idempotent NON-deleting + boot probe `[seed] process_leg_map total=N` |

---

## 2. Entities

### 2.1 `WoLeg`
```csharp
namespace CCL.MES.Domain.Entities;

public class WoLeg : BaseEntity
{
    public long WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public int    Sequence   { get; set; }        // theo OpNo routing; render + suy dep tuyến tính
    public string LegKind     { get; set; } = "";  // PRINT|CUT|TAPE|ASSEMBLY|PRINT_CUT (LegKind enum .ToString())
    public string Method      { get; set; } = "";  // Silkscreen|HP|Flexo|LP|RDC|CNC|PowerPunch|Flatbed|MagicLine
    public string ProcessLine { get; set; } = "";  // SILK|DIGITAL|LABEL|PRESS_CNC|FINISHING (khớp Plan C token)

    public long?  SpecRevisionId { get; set; }      // spec riêng theo method (Q8)
    public string SurfaceProfile { get; set; } = "FULL"; // FULL|LITE (Q5)
    public string InputSource    { get; set; } = "IN_LINE"; // IN_LINE|FROM_STOCK|MIXED — P11 chỉ dùng IN_LINE

    public string LegPhase { get; set; } = "PREPRESS";  // LegPhase enum .ToString()
    public byte[] RowVersion { get; set; } = Array.Empty<byte>(); // concurrency PER-LEG (trigger SQLite)

    public int QtyDoneCached { get; set; }
    public int QtyNgCached   { get; set; }
    public DateTime? LegDoneAt { get; set; }
}
```
- Config: `LegKind/Method/ProcessLine/SurfaceProfile/InputSource/LegPhase`
  `HasMaxLength(16..32)`. Index `(WorkOrderId, Sequence)` unique.
- `RowVersion`: dùng ĐÚNG pattern trigger SQLite của `WorkOrder`
  (migration `AddWorkOrderRowVersionAndMesPhase` là mẫu — copy trigger
  cho bảng `WoLegs`).

### 2.2 `WoLegDependency`
```csharp
public class WoLegDependency : BaseEntity
{
    public long WorkOrderId { get; set; }
    public long LegId { get; set; }            // node phụ thuộc (vd ASSEMBLY)
    public long DependsOnLegId { get; set; }   // node tiên quyết (vd PRINT / TAPE)
    public string DependencyGate { get; set; } = "SOFT"; // SOFT|HARD (Q4)
    public int    RequiredQty { get; set; }    // 0 = "chỉ cần done"; >0 = cần đủ qty (ASSEMBLY)
}
```
- Index unique `(WorkOrderId, LegId, DependsOnLegId)`.
- Seed rule (§5): PRINT/TAPE → không tạo edge chặn (song song); ASSEMBLY
  → edge HARD tới mọi PRINT+TAPE cùng WO, `RequiredQty = WO.TargetQty`.

### 2.3 Cột thêm trên 8 surface entity (additive, nullable)
`WoMaterial · WoPlateCheck · WoCutterCheck · WoRunSession · WoPauseEvent ·
WoQtyEntry · WoIpqcCheck · WoIpqcCheckItem` → mỗi bảng `+ long? WoLegId`
(null = WO 1-leg cũ). Chỉ thêm cột + FK optional; KHÔNG đổi logic đọc/ghi
hiện tại (controllers vẫn set null cho tới P11-2).

---

## 3. Enums

### 3.1 `LegPhase` (sub-machine của 1 leg — subset production của MesPhase)
```
PREPRESS, SETTING, IPQC_WAIT, QA_PENDING, IPQC_APPROVED, RUNNING, PAUSED, LEG_DONE
```
Transition tái dùng `ClassifyTransition` cho subset (không viết matrix
riêng); `LEG_DONE` là terminal của leg.

### 3.2 Routing enums
```csharp
public enum LegKind { PRINT, CUT, TAPE, ASSEMBLY, PRINT_CUT }
public enum InputSource { IN_LINE, FROM_STOCK, MIXED }   // P11: chỉ IN_LINE hoạt động
public enum DependencyGate { SOFT, HARD }
public enum SurfaceProfile { FULL, LITE }
```
Lưu DB dạng string (HasConversion + MaxLength) — nhất quán MesPhase.

### 3.3 `MesPhase += SPLIT`
```csharp
SPLIT = 13,   // WO forked: production đang chạy trên ≥2 leg; join → FQC_PENDING
```
Comment rõ: WO 1-leg KHÔNG bao giờ vào SPLIT (backward-compat). Test
matrix nở tự động vì enumerate `Enum.GetValues<MesPhase>()`.

---

## 4. State machine (`WorkOrderStateMachine.cs`)

### 4.1 `CanonicalFlow` — chèn SPLIT sau PREPRESS
`... PREPRESS, SPLIT, SETTING, ...` — GIỮ thứ tự cũ, chỉ chèn SPLIT (chú ý
test render timeline).

### 4.2 `ClassifyTransition` — 2 cell mới
```csharp
(MesPhase.PREPRESS, MesPhase.SPLIT)      => MesTransitionKind.RequiresCondition, // fork
(MesPhase.SPLIT,     MesPhase.FQC_PENDING) => MesTransitionKind.RequiresCondition, // join
```
- Giữ NGUYÊN `(PREPRESS, SETTING)` cho WO 1-leg. WO nhiều leg đi
  `PREPRESS → SPLIT`; WO 1-leg đi `PREPRESS → SETTING` như cũ. Việc chọn
  nhánh nào do controller (P11-2) quyết theo `wo.Legs.Count`.
- `SPLIT → CANCELLED` tự động RecoveryOnly qua rule `to == CANCELLED`.

### 4.3 `CheckCondition` — 2 case
```csharp
// PREPRESS → SPLIT: WO phải có ≥2 leg + DAG hợp lệ.
if (from == MesPhase.PREPRESS && to == MesPhase.SPLIT)
    return wo.Legs.Count >= 2 && RoutingDagValidator.IsValid(wo)
        ? new TransitionResult(true)
        : new TransitionResult(false, WoErrorCode.InvalidRoutingDag);

// SPLIT → FQC_PENDING: mọi leg TERMINAL (không có successor) == LEG_DONE.
if (from == MesPhase.SPLIT && to == MesPhase.FQC_PENDING)
    return wo.Legs.Count > 0
        && wo.Legs.Where(l => IsTerminalLeg(wo, l)).All(l => l.LegPhase == nameof(LegPhase.LEG_DONE))
        ? new TransitionResult(true)
        : new TransitionResult(false, WoErrorCode.LegsNotAllDone);
```

### 4.4 Helper `IsTerminalLeg`
```csharp
// terminal = không leg nào phụ thuộc vào nó (không phải nguồn của edge nào).
public static bool IsTerminalLeg(WorkOrder wo, WoLeg leg) =>
    wo.LegEdges.All(e => e.DependsOnLegId != leg.Id);
```

---

## 5. Routing → Leg DAG (data-driven)

### 5.1 `RoutingLegResolver.Resolve(ops, legMap)` (pure)
Input: `IEnumerable<QcLineResolver.RoutingOp>` (TÁI DÙNG record có sẵn) +
`IReadOnlyList<ProcessLegMapEntry>`.
Output record:
```csharp
public sealed record LegPlan(
    IReadOnlyList<LegNode> Legs,          // (Sequence, LegKind, Method, ProcessLine)
    IReadOnlyList<(int from,int to)> Edges,
    IReadOnlyList<string> Unmapped);      // op không map → caller log loud, KHÔNG đoán
```
Luật:
1. Mỗi op → classify (LegKind, Method, ProcessLine) qua legMap (ưu tiên
   ProcessCode → WorkCenterPrefix dài nhất → OpKeyword — **giống hệt
   `QcLineResolver.Classify`**, tái dùng thuật toán).
2. Gộp op cùng (LegKind, ProcessLine, máy) liền kề → 1 leg.
3. Edge: mặc định tuyến tính theo Sequence GIỮA các leg khác line; RIÊNG
   `ASSEMBLY` → edge HARD từ MỌI leg PRINT + TAPE đứng trước nó.
4. Op không map → `Unmapped` (caller báo `RoutingUnmapped`, chặn tạo WO
   multi-leg, hỏi người duyệt).

### 5.2 `RoutingLegMapSeed` (mirror `ProcessLineMapSeed`)
Tái dùng WC prefix đã có trong `ProcessLineMapSeed` + thêm cột LegKind:

| WC prefix (ví dụ, xác nhận với xưởng) | ProcessLine | LegKind | Method |
|---|---|---|---|
| GFL/BFL/LP | LABEL | PRINT | Flexo/LP |
| IDG | DIGITAL | PRINT | HP |
| ASS/MSS/ARSS/MAGSS/R2R | SILK | PRINT | Silkscreen |
| FBL/RDC/ACNC/CNC/PPSC/R2SC/LASE/PUNC | PRESS_CNC | CUT | Flatbed/RDC/CNC/PowerPunch/… |
| (OpKeyword "TAPE"/"BĂNG KEO") | FINISHING/PRESS_CNC | TAPE | (máy cắt tape) |
| (OpKeyword "ASSEMBLY"/"DÁN"/"LAM.") | FINISHING | ASSEMBLY | — |

> TAPE vs ASSEMBLY phân biệt bằng **OpKeyword** (dán/assembly → ASSEMBLY;
> cắt tape → TAPE). Ma trận keyword cần Henry/Ops xác nhận trước seed.

Seed upsert idempotent NON-deleting (DR-1) + boot probe.

---

## 6. `RoutingDagValidator` (pure, fail-closed)
`IsValid(WorkOrder wo)` → true khi:
1. **No cycle** (topological sort thành công trên `wo.LegEdges`).
2. **Assembly inputs**: mọi leg `LegKind==ASSEMBLY` có ≥1 dep PRINT + ≥1
   dep TAPE (hoặc — khi P11.5 — `InputSource!=IN_LINE`).
3. **Terminal reaches FQC**: ≥1 terminal leg; không leg mồ côi.
4. Trả `(bool, WoErrorCode?)` để caller emit chính xác (`InvalidRoutingDag`
   / `AssemblyInputsMissing`).
Có unit test riêng cho từng luật (§8).

---

## 7. Audit actions (alphabetical, thêm vào `AuditAction.cs`)
`WO_LEG_CREATED`, `WO_LEG_DONE`, `WO_LEG_PHASE_ADVANCED`, `WO_LEG_REWORK`
(reject leg → PREPRESS), `WO_SPLIT_FORKED`, `WO_SPLIT_JOINED`.
(Emit thực tế ở P11-2; hằng số khai báo sẵn ở P11-1 để test parity ổn định.)

---

## 8. Test plan (bổ sung, không sửa test cũ)

| Test | Khẳng định |
|---|---|
| `WorkOrderStateMachineLegacyParityTests` (chạy lại) | 0 thay đổi hành vi WO 1-leg |
| `WorkOrderStateMachineFullMatrixTests` (auto nở) | 196 cells; SPLIT cells đúng kind |
| `MesPhaseSplitTransitionTests` (mới) | PREPRESS→SPLIT (≥2 leg + DAG valid), SPLIT→FQC_PENDING (all terminal LEG_DONE), thiếu → LegsNotAllDone |
| `RoutingLegResolverTests` (mới) | T1 (1 leg PRINT_CUT), T2 (PRINT→CUT), T3 (PRINT∥TAPE→ASSEMBLY→CUT) + Unmapped |
| `RoutingDagValidatorTests` (mới) | cycle→false, assembly thiếu input→false, terminal mồ côi→false, T3 hợp lệ→true |
| `WoLegRowVersionTests` (mới) | trigger SQLite bump RowVersion per-leg |
| Migration round-trip (verify script) | `.schema WoLegs/WoLegDependencies/ProcessLegMap` đúng; WO cũ backfill 0 leg |

---

## 9. Migration — quy trình BẮT BUỘC (EF Core safety §4)

1. **Phase A**: `cp data/ccl_mes.db /tmp/ccl_mes.db.before-p11-1.<ts>` +
   `shasum -a 256` + rowcount baseline (WorkOrders).
2. **Phase B — generate trên ISOLATED /tmp DB** (KHÔNG trỏ live):
   ```bash
   cp src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs /tmp/snap-pre-p11-1.cs
   rm -f /tmp/p11-1-design.db
   MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/p11-1-design.db" \
     dotnet ef migrations add AddRoutingLegDag \
     -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations --no-build
   ```
3. **Type-affinity strip** (§4.5): xoá `type: "TEXT|INTEGER|REAL"` +
   `.HasColumnType(...)` trong migration mới (giữ cổng SQL Server).
4. **Backfill trong `Up()`**: KHÔNG đụng WorkOrders (WO cũ = 0 leg → không
   INSERT gì). Chỉ tạo bảng + index + trigger RowVersion cho `WoLegs`.
5. **Apply + verify** trên `/tmp/p11-1-design.db`, `.schema`.
6. **UNDO nếu cần**: `rm` file migration + `cp` snapshot cũ — **TUYỆT ĐỐI
   KHÔNG** `dotnet ef migrations remove` (revert live DB).
7. **Phase C**: chỉ áp live khi Henry duyệt; verify SHA + rowcount +
   `__EFMigrationsHistory`.

---

## 10. Backward-compat guarantee (checklist reviewer)
- [ ] WO không có leg: `PREPRESS → SETTING` vẫn allow (không ép SPLIT).
- [ ] `ProjectToLegacy(SPLIT)` = `ProcessStepCode.PrePressCheck` (hoặc
      cùng slot production) — legacy Razor không vỡ.
- [ ] 8 surface bảng: `WoLegId` nullable, default null, không có FK
      NOT NULL.
- [ ] Không xoá/sửa enum value cũ (chỉ append SPLIT=13).
- [ ] `LegacyParity*` + 2140 test xanh.
