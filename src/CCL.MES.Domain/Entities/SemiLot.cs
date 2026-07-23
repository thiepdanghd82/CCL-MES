namespace CCL.MES.Domain.Entities;

/// <summary>
/// P11.5 — một lô bán thành phẩm (semi) trong KHO decoupling point.
/// Sản xuất trước bởi một Semi WO (routing chỉ PRINT/TAPE, không
/// ASSEMBLY/CUT) → post lot khi các leg semi <c>LEG_DONE</c> + qua
/// semi-QC. Assembly của WO sản phẩm (<c>WoLeg.InputSource=FROM_STOCK</c>)
/// scan nhãn lot (barcode <see cref="LotNo"/>) → reserve → consume.
///
/// Qty tách 3: <see cref="QtyProduced"/> (bất biến) =
/// <see cref="QtyAvailable"/> (còn trong kho) + <see cref="QtyReserved"/>
/// (đã giữ cho WO) + đã consume (rời kho). <see cref="RowVersion"/>
/// optimistic-lock để 2 assembly reserve cùng lô không over-sell —
/// pattern L38 (IsConcurrencyToken + ValueGeneratedNever + trigger).
/// </summary>
public class SemiLot : BaseEntity
{
    /// <summary>Barcode nhãn lot — natural key khi scan tại assembly.</summary>
    public string LotNo { get; set; } = "";

    /// <summary><c>SemiKind</c>.ToString() — PRINTED_SEMI | TAPE_SEMI.</summary>
    public string SemiKind { get; set; } = "";

    /// <summary>Spec/mã hàng của semi (khớp WO sản phẩm khi reserve).</summary>
    public long? SpecRevisionId { get; set; }

    /// <summary>Semi WO đã sản xuất ra lô (genealogy / truy xuất nguồn gốc).</summary>
    public long SourceWorkOrderId { get; set; }

    public int QtyProduced { get; set; }
    public int QtyAvailable { get; set; }
    public int QtyReserved { get; set; }

    /// <summary><c>SemiLotStatus</c>.ToString() — AVAILABLE | DEPLETED | EXPIRED.</summary>
    public string Status { get; set; } = "AVAILABLE";

    /// <summary>Hạn sử dụng (keo/mực semi xuống cấp) — FEFO ordering + cảnh báo (Q12).</summary>
    public DateTime? ExpiryAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// P11.5 — liên kết assembly leg ↔ lô semi tiêu thụ (nhiều lô cho 1
/// assembly = MIXED; 1 lô cho nhiều WO). Reserve tăng
/// <see cref="QtyReserved"/>; consume (assembly done) chuyển reserved →
/// <see cref="QtyConsumed"/>.
/// </summary>
public class SemiAllocation : BaseEntity
{
    public long WorkOrderId { get; set; }
    public long AssemblyLegId { get; set; }
    public long SemiLotId { get; set; }
    public int QtyReserved { get; set; }
    public int QtyConsumed { get; set; }
}
