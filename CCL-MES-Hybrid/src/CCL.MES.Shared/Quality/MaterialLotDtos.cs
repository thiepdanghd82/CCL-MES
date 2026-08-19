namespace CCL.MES.Shared.Quality;

/// <summary>A1 — body quét lô lên một dòng BOM.
/// <c>POST /work-orders/{id}/materials/{bomLineIdx}/consume</c>.</summary>
public sealed class ConsumeMaterialLotRequest
{
    public string LotNo { get; set; } = "";
    public double QtyUsed { get; set; }

    /// <summary>Nhánh routing khi WO đã fork. Bỏ trống cho WO 1-leg (hôm nay
    /// <c>WoLegs</c> có 0 dòng).</summary>
    public long? LegId { get; set; }
}

/// <summary>A1 — body nhập lô vào kho lúc làm IQC (Đ2: ExpiryAt nhập tay).</summary>
public sealed class CreateMaterialLotRequest
{
    public string LotNo { get; set; } = "";
    public string PartNo { get; set; } = "";
    public double QtyReceived { get; set; }
    public string? Uom { get; set; }
    public DateTime? ExpiryAt { get; set; }
    public long? IqcInspectionId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierLotNo { get; set; }
}

/// <summary>A1 — body đổi trạng thái lô (QC / Supervisor / Admin).</summary>
public sealed class SetMaterialLotStatusRequest
{
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
}

/// <summary>A1 — body gia hạn lô hết hạn. Người duyệt phải KHÁC người kiểm
/// lại (Đ3) — server kiểm, không tin client.</summary>
public sealed class ExtendMaterialLotExpiryRequest
{
    public DateTime? ExpiryExtendedTo { get; set; }
    public string? Reason { get; set; }
}

/// <summary>A1 — body đảo một lần tiêu thụ (Supervisor, Đ3).</summary>
public sealed class ReverseConsumptionRequest
{
    public string? Reason { get; set; }
}

/// <summary>A1 — kết quả chung của mọi thao tác mạch lô.</summary>
public sealed class MaterialLotSetResponse
{
    public bool Ok { get; set; }
    public string? ErrorCode { get; set; }

    /// <summary>Grace period: ghi thành công nhưng ĐÁNG LẼ bị chặn. Đây là con
    /// số để đo "lật cờ sẽ chặn bao nhiêu ca" trước khi chặn thật.</summary>
    public string? Warning { get; set; }

    /// <summary>Trạng thái cờ <c>Mes:MaterialLot:EnforceReleased</c> lúc xử lý.</summary>
    public bool Enforced { get; set; }

    public long? ConsumptionId { get; set; }
    public long? MaterialLotId { get; set; }
    public string? LotNo { get; set; }
    public string? LotStatus { get; set; }
    public double QtyAvailableAfter { get; set; }
}

/// <summary>A1 — một LẦN QUÉT trong bảng truy xuất (Đ4: từng lần, không gộp).</summary>
public sealed class MaterialGenealogyRowDto
{
    public long ConsumptionId { get; set; }
    public int BomLineIdx { get; set; }
    public string MaterialCode { get; set; } = "";
    public long? LegId { get; set; }
    public long MaterialLotId { get; set; }
    public string LotNo { get; set; } = "";
    public string PartNo { get; set; } = "";
    public string LotStatus { get; set; } = "";
    public DateTime? ExpiryAt { get; set; }
    public string? SupplierName { get; set; }
    public long? IqcInspectionId { get; set; }
    public int IqcFailCount { get; set; }
    public string? IqcFailCodes { get; set; }
    public double QtyUsed { get; set; }

    /// <summary>SUM(QtyUsed) theo (dòng BOM, lô), bỏ dòng đã đảo — Đ4 hệ quả 3.
    /// Màn hình hiển thị TỪNG LẦN QUÉT nhưng con số tiêu hao lấy từ đây.</summary>
    public double QtyUsedTotalForLot { get; set; }

    public string? Uom { get; set; }
    public string ScannedBy { get; set; } = "";
    public DateTime ScannedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
}

/// <summary>A1 — bảng truy xuất mạch lô của một WO.</summary>
public sealed class MaterialGenealogyView
{
    public long WoId { get; set; }
    public List<MaterialGenealogyRowDto> Rows { get; set; } = new();
}
