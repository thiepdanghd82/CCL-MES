namespace CCL.MES.Shared.Machines;

/// <summary>P10.8 — one closed work order in the Shop Order History
/// (SpecHub forensic-record parity). Per-WO runtime / setup / pause-count
/// come from real WoRunSession + WoPauseEvent data; OEE is the honest
/// Availability×Performance×Quality (null when the machine has no ideal
/// cycle time or the WO never ran — never fabricated).</summary>
public sealed record ShopOrderHistoryRow
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? ProductCode { get; init; }
    public string? MachineCode { get; init; }
    public string? Process { get; init; }
    public string MesPhase { get; init; } = "";
    public int TargetQty { get; init; }
    public int QtyDone { get; init; }
    public int QtyNg { get; init; }

    /// <summary>(done - ng) / done, as a 0-100 percentage; 0 when done = 0.</summary>
    public int YieldPct { get; init; }

    /// <summary>done / plan, 0-100 (capped at 100).</summary>
    public int ProgressPct { get; init; }

    /// <summary>Availability × Performance × Quality, 0-100; null when not
    /// computable (no run sessions or machine ideal cycle time unset).</summary>
    public int? OeePct { get; init; }

    public long RuntimeSec { get; init; }
    public int SetupSec { get; init; }
    public int PauseCount { get; init; }

    /// <summary>Per-WO pause breakdown by reason (powers the drawer +
    /// the plant-wide "Top Downtime Reasons" roll-up).</summary>
    public IReadOnlyList<ShopOrderAggRow> PauseReasons { get; init; }
        = System.Array.Empty<ShopOrderAggRow>();

    public DateTime? FinishedAt { get; init; }
}

/// <summary>P10.8 — a labelled count bucket for the sidebar roll-ups
/// (top customers / machines / downtime reasons).</summary>
public sealed record ShopOrderAggRow
{
    public string Key { get; init; } = "";
    public string? Label { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// P10.8 — Shop Order History: forensic record of closed work orders
/// (MesPhase SHIPPED or CANCELLED) plus KPI roll-ups + sidebar
/// aggregates, honouring the period / search / status / customer /
/// machine filters. Read-only.
/// </summary>
public sealed record ShopOrderHistoryDto
{
    public int TotalWos { get; init; }
    public int DoneCount { get; init; }
    public int StoppedCount { get; init; }
    public int Output { get; init; }       // sum of QtyDone
    public int Reject { get; init; }       // sum of QtyNg
    public int YieldPct { get; init; }     // (output - reject) / output, 0-100
    public int? AvgOeePct { get; init; }   // mean of rows with a computable OEE

    public IReadOnlyList<ShopOrderHistoryRow> Rows { get; init; }
        = System.Array.Empty<ShopOrderHistoryRow>();

    public IReadOnlyList<ShopOrderAggRow> TopCustomers { get; init; }
        = System.Array.Empty<ShopOrderAggRow>();
    public IReadOnlyList<ShopOrderAggRow> TopMachines { get; init; }
        = System.Array.Empty<ShopOrderAggRow>();
    public IReadOnlyList<ShopOrderAggRow> TopDowntimeReasons { get; init; }
        = System.Array.Empty<ShopOrderAggRow>();

    /// <summary>Distinct customer / machine lists over ALL closed WOs
    /// (stable dropdown options, independent of the current filter).</summary>
    public IReadOnlyList<string> Customers { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> Machines { get; init; } = System.Array.Empty<string>();
}
