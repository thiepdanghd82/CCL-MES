# P11 — Multi-Method Routing (Fork-Join DAG) scope proposal (ENRICHED)

> **Status**: draft for Henry approval — mở rộng WO State Contract từ
> single-leg tuyến tính sang routing DAG đa phương pháp (in × cắt,
> combined/separated, tape + assembly).
> **Pattern**: mirrors 7c/7d/7e enriched proposal — mỗi câu hỏi mở
> (Q1..Q11) + Recommended + trade-off + contract-gap note.
> **Supersedes**: bản nháp "P11 2-leg fork-join" trong chat (2026-07-23);
> case mới của Henry (Flexo/LP/HP + tape+assembly) chứng minh phải tổng
> quát hoá thành DAG, không dừng ở 2 leg cứng.
> **Tag plan**: sau khi Henry duyệt Q1..Q11 → mở `feat/p11-routing-domain`.

---

## 0. Vì sao scope phải nở

Bản nháp trước mô hình silkscreen = 2 leg cứng (In ∥ Cắt) → gộp FQC.
Case Henry bổ sung phá vỡ giả định "2 leg":

1. **Nhiều phương pháp in**: Flexo · LP (Letterpress) · HP (Indigo digital)
   · Silkscreen (in lụa).
2. **Nhiều phương pháp cắt/bế**: Flexo inline · Flatbed · Magic line ·
   RDC (rotary die cut) · Power punching · CNC.
3. **In + Cắt có thể GỘP hoặc TÁCH**:
   - Gộp (inline): 1 lượt in-bế → **1 leg** (vd Flexo in+bế).
   - Tách: in xong chuyển máy cắt → **≥2 leg** (vd HP in → cắt RDC/
     Flatbed/Magic line/Flexo).
4. **Silkscreen có nhánh tape + assembly** — KHÔNG phải 2 leg song song
   đơn giản mà là **DAG hội tụ giữa chừng**:
   ```
   In Silkscreen ─────────────► semi-in ─┐
                                          ├─► Assembly (dán tape+semi-in) ─► Cắt final ─► FQC
   Cắt tape (Power punch/Magic/RDC) ──────┘        (Magic line / Power punch / CNC)
   ```

⇒ Mô hình đúng là **routing = DAG các operation** với quan hệ phụ thuộc
(`DependsOn`), không phải fork-join phẳng. "Combined vs separated" trở
thành **thuộc tính phát sinh** của DAG (1 op = 1 leg), không cần cờ riêng.

---

## 1. Taxonomy (đưa vào master data, KHÔNG hardcode)

### 1.1 Phương pháp (Method) → QC line (tái dùng Plan C 5 line)

| Kind | Method | ProcessLine (Plan C) | IPQC items có sẵn |
|---|---|---|---|
| PRINT | Silkscreen (in lụa) | `SILK` | 25 |
| PRINT | HP (Indigo digital) | `DIGITAL` | 15 |
| PRINT | Flexo | `LABEL` | 34 |
| PRINT | LP (Letterpress) | `LABEL` | 34 |
| CUT | Flexo inline / Flatbed / Magic line / RDC / Power punching / CNC | `PRESS_CNC` | 27 |
| TAPE | Cắt tape (Power punching / Magic line / RDC) | `PRESS_CNC` hoặc `FINISHING` | 27 / 5 |
| ASSEMBLY | Dán tape + semi-in | `FINISHING` | 5 |
| PRINT_CUT | In+bế inline (Flexo) | `LABEL` (+ CUT check) | 34 |

> Điểm ăn tiền: `QcLineResolver` + `IpqcLibraryMaterializer` ĐÃ resolve
> routing → QC line qua dữ liệu. Method chỉ cần map sang ProcessLine là
> IPQC mỗi leg **tự động** đúng bộ item — không viết logic IPQC mới.

### 1.2 Topology (phát sinh từ routing DAG)

| # | Tên | DAG | Số leg | Ví dụ |
|---|---|---|---|---|
| **T1** | Combined inline | `[PRINT_CUT]` | 1 | Flexo in+bế 1 lượt |
| **T2** | Print → Cut tách | `[PRINT] → [CUT]` | 2 | HP in → RDC/Flatbed/Magic/Flexo cắt |
| **T3** | Silkscreen tape+assembly | `[PRINT] ┐`<br>`[TAPE]  ┴→ [ASSEMBLY] → [CUT]` | 4 | In lụa ∥ cắt tape → dán → cắt Magic/Power/CNC |

- **T1** = đúng luồng tuyến tính hôm nay (0 row `WoLeg`) → backward-compat.
- **T2/T3** = multi-leg; WO đứng ở phase umbrella `SPLIT`; join → FQC.

---

## 2. Mô hình dữ liệu đề xuất

### 2.1 `WoLeg` (node của routing DAG)

```csharp
public class WoLeg : BaseEntity
{
    public long WorkOrderId { get; set; }
    public int  Sequence { get; set; }             // thứ tự OpNo, để render + suy dep tuyến tính
    public string LegKind { get; set; } = "";      // PRINT | CUT | TAPE | ASSEMBLY | PRINT_CUT
    public string Method  { get; set; } = "";       // Silkscreen | HP | Flexo | RDC | CNC | ...
    public string ProcessLine { get; set; } = "";   // SILK | DIGITAL | LABEL | PRESS_CNC | FINISHING

    public long? SpecRevisionId { get; set; }       // spec riêng theo method (In vs Dập)
    public string SurfaceProfile { get; set; } = "FULL"; // FULL | LITE (xem Q5)

    public string LegPhase { get; set; } = "PREPRESS";   // sub-machine tái dùng phase production
    public byte[] RowVersion { get; set; } = Array.Empty<byte>(); // concurrency PER-LEG
    public int QtyDoneCached { get; set; }
    public int QtyNgCached { get; set; }
    public DateTime? LegDoneAt { get; set; }
}

public class WoLegDependency : BaseEntity   // cạnh DAG (assembly cần print + tape)
{
    public long WorkOrderId { get; set; }
    public long LegId { get; set; }          // node phụ thuộc
    public long DependsOnLegId { get; set; } // node tiên quyết
}
```

Các surface hiện có nhận thêm **1 cột nullable `WoLegId`** (null = WO 1-leg
cũ, giữ nguyên hành vi):

| Bảng | Thay đổi |
|---|---|
| `WoPrepressMaterial / WoPlateCheck / WoCutterCheck` | `+ WoLegId?` |
| `WoRunSession / WoQtyEntry / WoPauseEvent` | `+ WoLegId?` |
| `WoIpqcCheck` (+ item mode Plan C) | `+ WoLegId?` |

### 2.2 State machine — fork + join

**Leg sub-phase** (tái dùng token production của `MesPhase`, thêm terminal
`LEG_DONE`):
```
PREPRESS → SETTING → IPQC_WAIT → (QA_PENDING) → IPQC_APPROVED → RUNNING ↔ PAUSED → LEG_DONE
```
→ tái dùng nguyên `ClassifyTransition` cho subset này.

**WO-level**: thêm đúng **1 phase umbrella** `SPLIT` (enum 13→14; test
matrix tự nở vì đã `Enum.GetValues<MesPhase>()`, y hệt lần 12→13 thêm
`SHIPPED`):
```
NEW → PREPRESS → SPLIT ──(join)──► FQC_PENDING → OQC_PENDING → SHIPPED
```

Join = `RequiresCondition`, cắm vào `CheckCondition`:
```csharp
// SPLIT → FQC_PENDING: mọi leg TERMINAL (không có successor) == LEG_DONE
if (from == MesPhase.SPLIT && to == MesPhase.FQC_PENDING)
    return wo.Legs.Count > 0
        && wo.Legs.Where(l => IsTerminalNode(wo, l)).All(l => l.LegPhase == "LEG_DONE")
        ? new TransitionResult(true)
        : new TransitionResult(false, WoErrorCode.LegsNotAllDone);
```
Server cascade khi leg cuối `LEG_DONE` (single SaveChanges, pattern 7c-2).

Assembly join giữa chừng: op `ASSEMBLY` có `DependsOn = {print, tape}`.
Gate xem Q4.

---

## 3. Câu hỏi mở cần Henry duyệt

### Q1 — Nguồn routing DAG lấy từ đâu?
**Context**: cần biết mỗi mã hàng có mấy op, method gì, gộp/tách.
**Recommended**: **hybrid data-driven** — derive từ IFS routing ops (mà
`QcLineResolver` ĐÃ đọc) qua bảng map mới `RoutingLegMap` (chị em với
`ProcessLineMap`): mỗi op → (LegKind, Method, ProcessLine); dependency
mặc định tuyến tính theo OpNo, có luật fork/join riêng cho ASSEMBLY.
Fallback `RoutingTemplate` master data khi mã hàng chưa có routing IFS.
**Trade-off**: (+) không nhập tay per-product, đúng triết lý "map là DỮ
LIỆU" Plan C. (−) suy dependency từ OpNo cho case fork (in ∥ tape) cần
luật tường minh → xem Q2.

### Q2 — Mô hình fork = DAG có cạnh phụ thuộc, hay flat parallel + join FQC?
**✅ CHỐT (Henry 2026-07-23)**: **DAG (`WoLegDependency`)**. T3 có join
GIỮA CHỪNG (assembly cần print+tape) — flat-parallel không biểu diễn được.
Cần `IsTerminalNode` + DAG validation (no-cycle) lúc tạo WO.

### Q3 — "Combined vs separated" cần cờ riêng không?
**Recommended**: **KHÔNG** — phát sinh từ routing: 1 op `PRINT_CUT` = 1
leg (T1); op PRINT + op CUT tách = 2 leg (T2). Cờ thừa dễ lệch dữ liệu.

### Q4 — Gate phụ thuộc (cut-after-print, assembly) hard hay soft?
**✅ CHỐT (Henry 2026-07-23)**:
- **PRINT ∥ TAPE = SOFT** (chạy song song) — cả hai là công đoạn **semi**,
  độc lập, không chặn nhau.
- **ASSEMBLY = HARD** — KHÔNG dán được nếu semi **chưa chạy xong** HOẶC
  **thiếu số lượng**. Gate = (mọi input semi ở trạng thái done) AND
  (tổng qty semi khả dụng ≥ qty assembly yêu cầu).
- Bổ sung: case **semi chạy trước + keep stock** (sản xuất semi theo lệnh
  riêng, nhập kho, đến khi chạy sản phẩm mới xuất ra) → giải pháp riêng
  ở **§3B — Decoupling point**.

Cột `DependencyGate = SOFT|HARD` trên `WoLegDependency`; seed:
PRINT/TAPE→SOFT, ASSEMBLY→HARD (kèm điều kiện qty).

### Q5 — Surface profile mỗi leg?
**Recommended**: `FULL` (prepress→setting→ipqc→running) cho PRINT/CUT/
PRINT_CUT; `LITE` (running/qty + IPQC optional, không setting timer) cho
TAPE/ASSEMBLY. Cấu hình theo LegKind trong `RoutingLegMap`.
**Trade-off**: LITE tránh bắt công nhân dán tape phải qua setting/IPQC
vô nghĩa; vẫn ghi qty + NG.

### Q6 — Method → ProcessLine mapping (bảng §1.1) có đúng thực tế xưởng?
**✅ CHỐT (Henry 2026-07-23)** — QC có **2 grain khác nhau**:

- **IPQC = theo KHU VỰC/máy (per-leg)**. Mỗi công đoạn/máy có bộ IPQC
  riêng: Label riêng, Silkscreen riêng, và từng khu vực cắt (CNC /
  Power punching / Magic line…) có IPQC riêng. → driven bởi `leg.ProcessLine`
  qua Plan C `IpqcLibraryMaterializer`, đúng như thiết kế. IPQC KHÔNG gộp.
- **FQC = theo HỌ SẢN PHẨM (WO-level, sau join)**, 2 profile:
  - `FQC_LABEL` — cho họ Label.
  - `FQC_SILKSCREEN` — cho họ Silkscreen; **CNC + Silkscreen + Power
    punching dùng CHUNG** một FQC profile này.

⇒ Bổ sung khái niệm **`FqcProfileKey` chọn theo product family** (không
theo leg). Xem Q6b. Bảng §1.1 (IPQC/method→line) giữ nguyên, chỉ làm rõ
cột "IPQC items" là **grain per-leg**, còn FQC là cột riêng ở Q6b.

### Q6b — FQC profile chọn theo product family thế nào?
**Recommended**: thêm `Product.FqcFamily` (`LABEL` | `SILKSCREEN`) →
resolve `FqcProfileKey` qua chuỗi 3 cấp có sẵn (WO snapshot → Product
override → default theo family). FQC vẫn WO-level sau khi join, KHÔNG
đổi contract FQC/OQC/SHIPPED. OQC giữ nguyên.
**Trade-off**: (+) tái dùng nguyên `FqcReadinessRollup` + Qc profile
chain 7e. (−) cần seed 2 FQC profile + cột family trên Product.

### Q7 — WO phase umbrella: thêm 1 `SPLIT`?
**Recommended**: **YES, +1 `SPLIT`**. Matrix 13→14, test tự nở. WO 1-leg
(T1) KHÔNG dùng SPLIT → 100% backward-compat.
**Trade-off**: (−) +1 enum + DeferredPhaseInfo entry. (+) cô lập độ phức
tạp multi-leg khỏi 13-state cũ.

### Q8 — Spec binding: mỗi leg 1 `SpecRevisionId` theo method?
**Recommended**: **YES** — phản ánh đúng 2 thư mục file thực tế
(`FG Silk_Spec In/80640386.xlsx` cho leg PRINT vs `FG Silk_Spec Dập/
Press_80640386.xlsx` cho leg CUT). Leg picker load đúng spec khi CN chọn.

### Q9 — Scan picker hiển thị leg thế nào?
**Recommended**: hiện **tất cả** leg của WO + phase từng leg; leg bị
dependency SOFT chưa xong → vẫn bấm được (banner cảnh báo); HARD chưa
xong → disable + tooltip. Tận dụng L21 `OnPhaseChanged` auto-route.

### Q10 — NG rework: reject 1 leg quay lại đâu?
**✅ CHỐT (Henry 2026-07-23)** — theo đề xuất: IPQC/NG của 1 leg reject →
**chỉ leg đó** về PREPRESS (không kéo cả WO). WO ở `SPLIT` tới khi leg đó
re-pass. FQC reject (sau join, WO-level) giữ contract hiện hành.

### Q11 — Đặt tên/tag: `P11` (mảng mới) hay `P10.8x`?
**Recommended**: **`P11`** — đủ lớn (routing engine + DAG + N-leg surface),
tách khỏi chuỗi quality 7x. Stack 4 PR (§4).

---

## 3B — Giải pháp Semi keep-stock (Decoupling point)

**Bài toán Henry nêu**: bình thường semi (in + cắt tape) chạy in-line
trong cùng WO (T3). Nhưng có lúc **semi được sản xuất trước theo lệnh
riêng, nhập kho bán thành phẩm (SFG), giữ tồn**; đến khi chạy sản phẩm
mới **xuất kho ra để assembly**. Lúc đó assembly KHÔNG có sibling leg
trong cùng WO — phải lấy semi từ **kho**.

### 3B.1 Nguyên lý — điểm tách (decoupling point) tại ASSEMBLY

ASSEMBLY leg có thuộc tính **`InputSource`** cho từng input semi:

| InputSource | Nghĩa | Gate HARD khi |
|---|---|---|
| `IN_LINE` | Semi chạy trong cùng WO (T3 chuẩn) | sibling leg `LEG_DONE` + qty đủ |
| `FROM_STOCK` | Semi xuất từ kho SFG | lot semi đã reserve, qty đủ |
| `MIXED` | Một phần in-line + một phần kho | tổng (in-line done + reserved) ≥ yêu cầu |

⇒ Gate HARD của assembly được **tổng quát hoá** thành *"đủ input"* — nguồn
input là sibling leg **hoặc** lot kho. Một công thức, hai kịch bản.

### 3B.2 Model — kho bán thành phẩm `SemiLot`

```csharp
public class SemiLot : BaseEntity
{
    public string LotNo { get; set; } = "";        // barcode nhãn lot
    public string SemiKind { get; set; } = "";       // PRINTED_SEMI | TAPE_SEMI
    public long?  SpecRevisionId { get; set; }       // spec semi (khớp mã hàng)
    public long   SourceWorkOrderId { get; set; }    // Semi WO đã sản xuất ra lot (genealogy)
    public int    QtyProduced { get; set; }
    public int    QtyAvailable { get; set; }         // còn trong kho
    public int    QtyReserved { get; set; }          // đã giữ cho WO sản phẩm
    public string Status { get; set; } = "AVAILABLE"; // AVAILABLE | DEPLETED | EXPIRED
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiryAt { get; set; }
}

public class SemiAllocation : BaseEntity  // liên kết assembly leg ↔ lot tiêu thụ
{
    public long WorkOrderId { get; set; }
    public long AssemblyLegId { get; set; }
    public long SemiLotId { get; set; }
    public int  QtyReserved { get; set; }
    public int  QtyConsumed { get; set; }
}
```

### 3B.3 Luồng

**Sản xuất semi (make-to-stock)** — một **Semi WO** = WO có routing chỉ
gồm PRINT (+TAPE), KHÔNG có ASSEMBLY/CUT:
1. Semi WO chạy các leg → `LEG_DONE` + qua **semi-QC (IPQC per-leg)**.
2. Khi WO semi hoàn tất → **post `SemiLot`** (`QtyProduced=QtyAvailable`),
   in **nhãn lot (barcode)**, `Status=AVAILABLE`.

**Tiêu thụ (khi chạy sản phẩm)** — finished WO có ASSEMBLY leg
`InputSource=FROM_STOCK`:
1. Công nhân **scan nhãn lot** semi → **tái dùng `MaterialBarcodeMatcher`
   (PR #124)** để khớp lot ↔ BOM assembly.
2. Hệ thống **reserve** lot: `QtyAvailable → QtyReserved` +
   ghi `SemiAllocation`. Gate HARD assembly = tổng reserved ≥ yêu cầu.
3. Assembly `LEG_DONE` → `reserved → consumed`; lot hết →
   `Status=DEPLETED`.

### 3B.4 Tái dùng hạ tầng — KHÔNG viết gate mới

- **Semi lot khi FROM_STOCK = một dòng BOM (`WoMaterial`) của assembly
  leg** → tái dùng `MaterialsReadinessRollup` + prepress material-scan +
  `MaterialBarcodeMatcher`. Assembly HARD gate = material readiness rollup.
- **Truy xuất nguồn gốc (genealogy)**: `SemiLot.SourceWorkOrderId` +
  `SemiAllocation` → link finished WO ↔ semi WO ↔ lot. Audit đầy đủ
  (`SEMI_LOT_POST` / `SEMI_LOT_RESERVE` / `SEMI_LOT_CONSUME`).
- **FEFO/FIFO**: gợi ý lot theo `CreatedAt`; cảnh báo `ExpiryAt`.

### 3B.5 Phân kỳ (quan trọng — tránh phình P11 core)

- **P11 core**: làm sẵn abstraction `InputSource` trên ASSEMBLY leg nhưng
  **chỉ enable `IN_LINE`** (T1/T2/T3 in-line). Assembly HARD gate =
  sibling legs.
- **P11.5 — Semi-Stock (decoupling)**: `SemiLot` + `SemiAllocation` +
  Semi WO posting + `FROM_STOCK`/`MIXED` + nhãn lot barcode + genealogy
  + màn hình **Kho bán thành phẩm** (tồn, reserve, hết hạn). Đây là
  **surface kho riêng**, tách stack để không kéo P11 core.

> Quyết định cần Henry: P11.5 làm **ngay sau** P11 core, hay để backlog?
> Và semi có cần **hạn sử dụng (shelf-life)** không (ảnh hưởng FEFO +
> cảnh báo)? → Q12.

### Q12 — Semi-Stock: ưu tiên + shelf-life?
**Recommended**: P11 core trước (in-line), **P11.5 ngay sau** vì Henry đã
có nghiệp vụ keep-stock thật. Semi CÓ shelf-life (keo/mực bán thành phẩm
xuống cấp) → thêm `ExpiryAt` + cảnh báo FEFO ngay từ P11.5.

---

## 4. Lộ trình PR (stacked, đúng lệ dự án)

| PR | Nội dung | Cơ chế chặn tái phát |
|---|---|---|
| **P11-1 Domain** | `WoLeg` + `WoLegDependency` + phase `SPLIT` + LegPhase machine + cột `WoLegId?` ×6 bảng + `RoutingLegMap` seed + migration (backfill WO cũ = 0 leg) + DAG validation (no-cycle) | LegParity + matrix 13→14 auto-nở + LegacyParity (T1 không đổi) + `RoutingLegResolverTests` (T1/T2/T3) |
| **P11-2 Wire** | Endpoints leg-scoped (`{legId}`) + `GET /legs` + join cascade + spec binding/leg + Plan C materialize theo ProcessLine + dependency gate SOFT/HARD | Soak N=10 per-leg RowVersion + audit `WO_LEG_*` wire-mirror + `checkpoint-p11-2.sh` S12 |
| **P11-3 UI** | Scan leg picker (N node) + dashboard theo LegKind (FULL/LITE) + assembly gate banner + DAG timeline view + L21 auto-route | bUnit T1/T2/T3 fixtures + Design Rules S9 responsive |
| **P11-4 Test belt** | `verify-p11.sh` (fork → N leg độc lập → assembly join → cắt final → FQC) + `checkpoint-p11-final.sh` (đi hết T3 trên 1 WO) + purge | Rule 6 self-prep + refuse-run guard (thiếu RoutingLegMap seed → bail) |

**Stack P11.5 — Semi-Stock decoupling** (sau khi P11 core tag, xem Q12):

| PR | Nội dung | Cơ chế chặn tái phát |
|---|---|---|
| **P11.5-1 Domain** | `SemiLot` + `SemiAllocation` + `InputSource` enable `FROM_STOCK/MIXED` + Semi WO posting + migration | genealogy parity + FEFO ordering test |
| **P11.5-2 Wire** | endpoint post-lot / reserve / consume + scan lot (MaterialBarcodeMatcher) + assembly gate FROM_STOCK | soak reserve N=10 (không oversell lot) + audit `SEMI_LOT_*` |
| **P11.5-3 UI** | Màn hình **Kho bán thành phẩm** (tồn/reserve/hết hạn) + scan lot ở assembly + cảnh báo FEFO/expiry | bUnit + Design Rules S9 |

---

## 5. Điểm rủi ro / cần theo dõi

1. **DAG cycle / mồ côi**: validate lúc tạo WO — mọi non-terminal leg
   phải có đúng ≥1 path tới FQC; ASSEMBLY phải có ≥1 PRINT + ≥1 TAPE
   dependency.
2. **Concurrency per-leg**: RowVersion trên `WoLeg` — 3-4 CN thao tác
   4 node đồng thời không đụng lock nhau (soak test theo leg).
3. **Backfill an toàn**: migration KHÔNG sinh leg cho WO cũ → mọi WO
   lịch sử vẫn T1 tuyến tính (EF Core safety §4, isolated /tmp DB test).
4. **Method mồ côi**: op có method chưa map trong `RoutingLegMap` →
   `UNMAPPED`, log loud + hỏi người duyệt (đúng pattern QcLineResolver),
   KHÔNG đoán.

---

## 6. Trạng thái quyết định

**✅ Đã chốt (Henry 2026-07-23)**: Q2 (DAG), Q4 (PRINT/TAPE soft ∥,
ASSEMBLY hard + qty), Q6 (IPQC per-khu-vực · FQC per-family: Label riêng,
Silkscreen chung CNC/Silk/Power-punch), Q10 (rework leg-level).
→ **Đủ khoá data model + migration P11-1. Có thể bắt đầu.**

**⏳ Còn mở (chốt song song khi P11-1 đang code)**:
- Q1 nguồn routing (IFS derive vs template) · Q3 (confirm no combined-flag)
  · Q5 surface FULL/LITE · Q6b FQC family resolve · Q7 phase `SPLIT`
  · Q8 spec/leg · Q9 picker · Q11 tên tag.
- **Q12 (Semi-Stock)**: P11.5 làm ngay sau P11 core hay backlog? +
  shelf-life semi? → *Recommended: P11.5 ngay sau, có `ExpiryAt` + FEFO.*

**Ghi chú kiến trúc (không cần duyệt, chỉ để nhất quán)**:
- IPQC = per-leg (Plan C `ProcessLine`); FQC = per-WO sau join, profile
  chọn theo `Product.FqcFamily`. Hai grain tách bạch — xem Q6/Q6b.
- Assembly HARD gate ở P11 core = sibling legs (`IN_LINE`); mở rộng sang
  `FROM_STOCK` khi P11.5 land — cùng một công thức "đủ input" (§3B.1).
