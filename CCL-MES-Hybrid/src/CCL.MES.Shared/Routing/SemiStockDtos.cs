namespace CCL.MES.Shared.Routing;

/// <summary>P11.5-2 — body post 1 lô semi vào kho (Semi WO done).</summary>
public sealed class PostSemiLotRequest
{
    public string LotNo { get; set; } = "";
    public string SemiKind { get; set; } = "";      // PRINTED_SEMI | TAPE_SEMI
    public long? SpecRevisionId { get; set; }
    public long SourceWorkOrderId { get; set; }
    public int Qty { get; set; }
    public DateTime? ExpiryAt { get; set; }
}

/// <summary>P11.5-2 — 1 lô trong kho bán thành phẩm (view).</summary>
public sealed class SemiLotRow
{
    public long Id { get; set; }
    public string LotNo { get; set; } = "";
    public string SemiKind { get; set; } = "";
    public long? SpecRevisionId { get; set; }
    public long SourceWorkOrderId { get; set; }
    public int QtyProduced { get; set; }
    public int QtyAvailable { get; set; }
    public int QtyReserved { get; set; }
    public string Status { get; set; } = "";
    public DateTime? ExpiryAt { get; set; }
    public bool Expiring { get; set; }   // ExpiryAt gần / quá hạn — UI cảnh báo FEFO
}

/// <summary>P11.5-2 — kho bán thành phẩm (list + tổng).</summary>
public sealed class SemiStockView
{
    public List<SemiLotRow> Lots { get; set; } = new();
    public int TotalAvailable { get; set; }
    public int TotalReserved { get; set; }
}

/// <summary>P11.5-2 — body reserve semi cho assembly leg. LotNo trống →
/// FEFO auto; có LotNo → scan lô cụ thể.</summary>
public sealed class ReserveSemiRequest
{
    public int Qty { get; set; }
    public string? LotNo { get; set; }
    public string SemiKind { get; set; } = "";   // PRINTED_SEMI | TAPE_SEMI
}

/// <summary>P11.5-2 — kết quả post/reserve/consume.</summary>
public sealed class SemiSetResponse
{
    public bool Ok { get; set; }
    public string? ErrorCode { get; set; }
    public long? SemiLotId { get; set; }
    public int Allocated { get; set; }      // reserve: tổng đã giữ
    public int Shortfall { get; set; }      // reserve: thiếu (nếu >0 → từ chối)
    public List<long> LotIds { get; set; } = new();  // các lô đã đụng
}
