namespace CCL.MES.Shared.WorkOrders;

/// <summary>
/// P10.7 landing — one active work-order card for the Work Orders scan
/// surface (SpecHub "Active Work Orders" parity). Read-only summary the
/// operator taps to open the WO.
/// </summary>
public sealed record ActiveWorkOrderCard
{
    public string WoNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string ProductName { get; init; } = "";
    public string? MachineCode { get; init; }
    public string MesPhase { get; init; } = "";
    public int TargetQty { get; init; }
    public int QtyDone { get; init; }
}
