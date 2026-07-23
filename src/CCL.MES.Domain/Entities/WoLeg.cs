namespace CCL.MES.Domain.Entities;

/// <summary>
/// P11-1 — một node của routing DAG đa phương pháp (fork-join). Một
/// <see cref="WorkOrder"/> có 0 leg (WO 1-leg tuyến tính legacy /
/// combined-inline T1) hoặc ≥2 leg (T2/T3, WO ở <c>MesPhase.SPLIT</c>).
///
/// Mỗi leg mang state-machine con riêng (<c>LegPhase</c>), spec riêng
/// theo phương pháp, và <see cref="RowVersion"/> RIÊNG để 2+ công nhân
/// (in / cắt tape / assembly / cắt) thao tác đồng thời không đụng
/// optimistic-lock của nhau. IPQC của leg driven bởi
/// <see cref="ProcessLine"/> (Plan C) — mỗi khu vực/máy 1 bộ item.
///
/// P11-1 chỉ khai báo + migration; controller/UI ở P11-2/P11-3. Các
/// field production (QtyDoneCached…) mirror pattern denormalise của
/// <see cref="WorkOrder"/>.
/// </summary>
public class WoLeg : BaseEntity
{
    public long WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>Thứ tự hiển thị + suy dependency tuyến tính (theo OpNo routing).</summary>
    public int Sequence { get; set; }

    /// <summary><c>LegKind</c>.ToString() — PRINT|CUT|TAPE|ASSEMBLY|PRINT_CUT.</summary>
    public string LegKind { get; set; } = "";

    /// <summary>Phương pháp/máy: Silkscreen|HP|Flexo|LP|RDC|CNC|PowerPunch|Flatbed|MagicLine…</summary>
    public string Method { get; set; } = "";

    /// <summary>QC line token Plan C: SILK|DIGITAL|LABEL|PRESS_CNC|FINISHING.
    /// Driver cho IPQC library materialize (per-leg, per-khu-vực).</summary>
    public string ProcessLine { get; set; } = "";

    /// <summary>Spec riêng của leg theo phương pháp (In vs Dập). Nullable —
    /// bind ở P11-2 khi tạo WO từ routing.</summary>
    public long? SpecRevisionId { get; set; }

    /// <summary><c>SurfaceProfile</c>.ToString() — FULL|LITE (Q5).</summary>
    public string SurfaceProfile { get; set; } = "FULL";

    /// <summary><c>InputSource</c>.ToString() — IN_LINE|FROM_STOCK|MIXED.
    /// P11-1 chỉ IN_LINE hoạt động; FROM_STOCK/MIXED land ở P11.5.</summary>
    public string InputSource { get; set; } = "IN_LINE";

    /// <summary><c>LegPhase</c>.ToString() — PREPRESS…RUNNING/PAUSED→LEG_DONE.</summary>
    public string LegPhase { get; set; } = "PREPRESS";

    /// <summary>Optimistic-concurrency token PER-LEG. SQLite trigger sinh
    /// randomblob(8) mỗi UPDATE/INSERT khi app không tự set (migration).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Denormalise SUM qty ledger của leg (ledger vẫn authoritative).</summary>
    public int QtyDoneCached { get; set; }
    public int QtyNgCached { get; set; }

    /// <summary>Stamp khi leg đạt LEG_DONE (join gate đọc cột này).</summary>
    public DateTime? LegDoneAt { get; set; }
}

/// <summary>
/// P11-1 — một cạnh phụ thuộc của routing DAG: <see cref="LegId"/> chỉ
/// chạy được (theo <see cref="DependencyGate"/>) sau khi
/// <see cref="DependsOnLegId"/> hoàn tất. Ví dụ ASSEMBLY dep HARD vào
/// PRINT + TAPE; CUT dep SOFT vào PRINT.
/// </summary>
public class WoLegDependency : BaseEntity
{
    public long WorkOrderId { get; set; }

    /// <summary>Node phụ thuộc (successor) — vd ASSEMBLY.</summary>
    public long LegId { get; set; }

    /// <summary>Node tiên quyết (predecessor) — vd PRINT / TAPE.</summary>
    public long DependsOnLegId { get; set; }

    /// <summary><c>DependencyGate</c>.ToString() — SOFT (cảnh báo) | HARD (chặn).</summary>
    public string DependencyGate { get; set; } = "SOFT";

    /// <summary>0 = chỉ cần predecessor done; &gt;0 = cần đủ qty (ASSEMBLY
    /// dùng WO.TargetQty).</summary>
    public int RequiredQty { get; set; }
}
