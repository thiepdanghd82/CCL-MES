namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11.5 — loại bán thành phẩm (semi) lưu kho. Semi được sản xuất trước
/// bởi một Semi WO (routing chỉ PRINT/TAPE) rồi nhập kho; assembly của WO
/// sản phẩm tiêu thụ từ kho khi <c>WoLeg.InputSource = FROM_STOCK</c>.
/// </summary>
public enum SemiKind
{
    /// <summary>Bán thành phẩm ĐÃ IN (semi-in) — output leg PRINT.</summary>
    PRINTED_SEMI,
    /// <summary>Tape đã cắt — output leg TAPE.</summary>
    TAPE_SEMI,
}

/// <summary>
/// P11.5 — trạng thái 1 lô semi trong kho.
/// </summary>
public enum SemiLotStatus
{
    /// <summary>Còn khả dụng để reserve/xuất.</summary>
    AVAILABLE,
    /// <summary>Đã tiêu thụ hết (QtyAvailable + QtyReserved = 0).</summary>
    DEPLETED,
    /// <summary>Quá hạn (ExpiryAt &lt; now) — FEFO cảnh báo, không auto-xuất.</summary>
    EXPIRED,
}
