namespace CCL.MES.Domain.Entities;

/// <summary>
/// MUTABLE index row — exactly one per Work Order — that drives the real-time
/// Traceability LIST. Separate from the immutable <see cref="WoTraceSnapshot"/>
/// (which the detail dialog reads): the index carries only lightweight
/// metadata and is upserted whenever a WO is scanned/found, changes MesPhase,
/// or freezes a phase. Updating the index NEVER touches a stored snapshot, so
/// the immutability guarantee holds while the list stays live.
/// </summary>
public class WoTraceIndex : BaseEntity
{
    public long WoId { get; set; }          // unique
    public string WoNo { get; set; } = "";  // unique — list search key

    public string? ProductCode { get; set; }
    public string ProductName { get; set; } = "";
    public string? Customer { get; set; }

    public string CurrentMesPhase { get; set; } = "";

    public DateTime FirstScannedAtUtc { get; set; }
    public DateTime LastScannedAtUtc { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }

    // Which phases already have ≥1 immutable snapshot (drives the list chips).
    public bool ProductFrozen { get; set; }
    public bool IpqcFrozen { get; set; }
    public bool FqcFrozen { get; set; }
    public bool OqcFrozen { get; set; }

    public DateTime? LatestFrozenAtUtc { get; set; }
}
