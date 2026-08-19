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
    /// <summary>feat/iqc-module-tabs — nhóm phiếu (Materials/Chemical/Tools/Other).
    /// Thiếu → server mặc định "Materials" (backward compat form Materials cũ).</summary>
    public string? Group { get; set; }
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
    /// <summary>feat/iqc-module-tabs — nhóm phiếu canonical (server chuẩn hoá).</summary>
    public string Group { get; set; } = "Materials";
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

// ─────────────────────────────────────────────────────────────────────────
// feat/iqc-search-by-desc — tra vật liệu theo MÔ TẢ (đảo chiều: mô tả là ô tìm
// chính, Code IFS là droplist kết quả multi-select). Read-only, QcRead.
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Một dòng kết quả tra vật liệu theo mô tả.
/// <c>CodeIfs</c> = PartNo (chọn nhiều để tạo N phiếu); <c>IfsDescription</c> =
/// PartDescription dòng đại diện.</summary>
public sealed class IqcMaterialSearchItem
{
    public string CodeIfs { get; set; } = "";
    public string? IfsDescription { get; set; }
}

/// <summary>Thân phản hồi <c>GET /api/v2/iqc/search-material</c> — phân trang +
/// cờ desc-quá-ngắn (UI hiện "{đã tick}/{total}").</summary>
public sealed class IqcMaterialSearchResponse
{
    /// <summary>true khi desc dưới ngưỡng ký tự tối thiểu (server KHÔNG query).</summary>
    public bool TooShort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<IqcMaterialSearchItem> Items { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────
// feat/iqc-module-tabs — IQC Data list (phiếu đã lưu) + KPI Dashboard.
// DTO thuần POCO: KHÔNG trả entity IqcInspection ra API (kéo navigation +
// snapshot nội bộ). Read-only, QcRead.
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Một dòng phiếu IQC đã lưu cho tab "IQC Data".</summary>
public sealed class IqcTicketListItem
{
    public long Id { get; set; }
    public string? ReceiptNo { get; set; }
    public string Group { get; set; } = "Materials";
    public string? CodeIfs { get; set; }
    public string? MaterialDescription { get; set; }
    public string? LotBatchNo { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public string? MakerName { get; set; }
    public string? SupplierName { get; set; }
    public string? Inspector { get; set; }
    public DateTime ReceivedDate { get; set; }
    public double Quantity { get; set; }
    public string? Uom { get; set; }
    /// <summary>Pending / Pass / Fail (enum-as-string).</summary>
    public string Result { get; set; } = "Pending";
}

/// <summary>Thân phản hồi <c>GET /api/v2/iqc/tickets</c> — phân trang, lọc group.</summary>
public sealed class IqcTicketListResponse
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<IqcTicketListItem> Items { get; set; } = new();
}

/// <summary>Thân phản hồi <c>GET /api/v2/iqc/dashboard</c> — KPI đếm thật cho
/// tab Dashboard. Placeholder CÓ CẤU TRÚC: số liệu thật, sẽ enrich thêm về sau.</summary>
public sealed class IqcDashboardResponse
{
    public int Total { get; set; }

    // Đếm theo nhóm (4 khoá canonical, luôn có mặt kể cả = 0).
    public int Materials { get; set; }
    public int Chemical { get; set; }
    public int Tools { get; set; }
    public int Other { get; set; }

    // Đếm theo trạng thái kiểm.
    public int Pending { get; set; }
    public int Pass { get; set; }
    public int Fail { get; set; }
}
