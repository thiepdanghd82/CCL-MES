namespace CCL.MES.Shared.Quality;

// ─────────────────────────────────────────────────────────────────────────
// feat/iqc-ticket — hợp đồng HTTP cho tạo phiếu IQC. Tách khỏi DTO tầng
// Application (CreateIqcTicketRequest ở IqcService) vì đây là wire contract:
// đổi record nội bộ không được âm thầm đổi thân request/response client đọc.
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Body tạo phiếu IQC (<c>POST /api/v2/iqc</c>). RECEIPT + Inspector
/// + mô tả do server sinh/resolve — client KHÔNG khai.</summary>
public sealed class CreateIqcTicketBody
{
    public string CodeIfs { get; set; } = "";
    public string LotBatchNo { get; set; } = "";
    public DateTime? ManufactureDate { get; set; }
    public string? MakerName { get; set; }
    public string? SupplierName { get; set; }
    public double Quantity { get; set; }
    public string? Uom { get; set; }
    public int? SampleSize { get; set; }
    public DateTime? ExpiryAt { get; set; }
}

/// <summary>Thân phản hồi tạo phiếu — 201 khi thành công.</summary>
public sealed class CreateIqcTicketResponse
{
    public string ReceiptNo { get; set; } = "";
    public long IqcInspectionId { get; set; }
    public long? MaterialLotId { get; set; }
    public string? MaterialDescription { get; set; }
    public string? IfsDescription { get; set; }

    /// <summary>matched / ambiguous / unmatched (quyết định #2/#3).</summary>
    public string MatchStatus { get; set; } = "unmatched";
    public string? LotStatus { get; set; }
}

/// <summary>Thân phản hồi resolve Code IFS (UI auto-fill trước submit).</summary>
public sealed class ResolveIqcCodeResponse
{
    public string MatchStatus { get; set; } = "unmatched";
    public string? PartNo { get; set; }
    public string? MaterialDescription { get; set; }
    public string? IfsDescription { get; set; }
    public string? SupplierName { get; set; }
}
