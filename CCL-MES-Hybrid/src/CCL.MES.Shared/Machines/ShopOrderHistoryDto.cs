namespace CCL.MES.Shared.Machines;

/// <summary>P10.8 — one closed work order in the Shop Order History.</summary>
public sealed record ShopOrderHistoryRow
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? MachineCode { get; init; }
    public string MesPhase { get; init; } = "";
    public int TargetQty { get; init; }
    public int QtyDone { get; init; }
    public int QtyNg { get; init; }

    /// <summary>(done - ng) / done, as a 0-100 percentage; 0 when done = 0.</summary>
    public int YieldPct { get; init; }

    public DateTime? FinishedAt { get; init; }
}

/// <summary>
/// P10.8 — Shop Order History: forensic record of closed work orders
/// (MesPhase SHIPPED or CANCELLED) plus KPI roll-ups, honouring the
/// period + search filters. Read-only.
/// </summary>
public sealed record ShopOrderHistoryDto
{
    public int TotalWos { get; init; }
    public int Output { get; init; }       // sum of QtyDone
    public int Reject { get; init; }       // sum of QtyNg
    public int YieldPct { get; init; }     // (output - reject) / output, 0-100

    public IReadOnlyList<ShopOrderHistoryRow> Rows { get; init; }
        = Array.Empty<ShopOrderHistoryRow>();
}
