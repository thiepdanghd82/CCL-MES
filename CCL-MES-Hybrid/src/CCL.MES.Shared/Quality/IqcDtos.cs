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
    // feat/iqc-materials-line-table — bảng line-items (Part No/Mother code/Part
    // description/Width) mỗi Code IFS đã tick. Additive; nullable.
    public string? MotherCode { get; set; }
    public double? WidthMm { get; set; }
    public string? PartDescription { get; set; }
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

// ── P12 bước 3 — hạng mục kiểm đã đóng băng trên phiếu ───────────────────

/// <summary>
/// Một hạng mục kiểm ĐÃ ĐÓNG BĂNG vào phiếu IQC lúc mở phiếu.
///
/// <para>Nhãn/tiêu chuẩn/phương pháp mang <b>cả hai ngôn ngữ</b>: sửa thư viện
/// về sau KHÔNG hồi tố phiếu đã mở, và đổi cờ EN/VI không cần gọi lại server.</para>
/// </summary>
public sealed class IqcCheckItemDto
{
    public long Id { get; set; }
    public string? ItemKey { get; set; }
    public int Seq { get; set; } = 1;

    /// <summary>Mục của stepper chứa hạng mục này (1 hồ sơ · 2 ngoại quan ·
    /// 3 chức năng). Server tính — UI KHÔNG tự suy.</summary>
    public int Section { get; set; } = 3;

    public string? GroupCode { get; set; }
    public string? GroupLabelVi { get; set; }
    public string? GroupLabelEn { get; set; }
    public string? LabelVi { get; set; }
    public string? LabelEn { get; set; }
    public string? AcceptanceVi { get; set; }
    public string? AcceptanceEn { get; set; }
    public string? MethodVi { get; set; }
    public string? MethodEn { get; set; }

    /// <summary>Tần suất ghi trong spec gốc, giữ nguyên văn. Chính sách hiện tại
    /// là kiểm MỌI lô (D1) — trường này để trả lời auditor "spec ghi gì".</summary>
    public string? SourceFrequency { get; set; }

    /// <summary>Bộ hạng mục đến từ ma trận mặc định chứ không phải spec riêng.</summary>
    public bool FromDefaultMatrix { get; set; }

    /// <summary>Tiêu chuẩn gốc còn placeholder <c>XXX</c> — hiện ra nhưng KHÔNG
    /// được bắt người kiểm ký "đạt/không đạt so với XXX".</summary>
    public bool AcceptanceUnspecified { get; set; }

    /// <summary><c>null</c> = CHƯA KIỂM (khác hẳn NG).</summary>
    public bool? Pass { get; set; }
    public string? MeasuredValue { get; set; }
    public string? DefectCode { get; set; }

    /// <summary>Nhãn theo ngôn ngữ đang bật. Thiếu EN → bản VI; thiếu cả hai →
    /// <see cref="ItemKey"/>, để ô không bao giờ trống. Đây là chỗ DUY NHẤT
    /// quyết định chuyện đó; UI không tự viết lại <c>??</c>.</summary>
    public string LabelFor(bool english) => Pick(LabelEn, LabelVi, english) ?? ItemKey ?? "";

    /// <inheritdoc cref="LabelFor"/>
    public string? GroupLabelFor(bool english) => Pick(GroupLabelEn, GroupLabelVi, english);

    /// <inheritdoc cref="LabelFor"/>
    public string? AcceptanceFor(bool english) => Pick(AcceptanceEn, AcceptanceVi, english);

    /// <inheritdoc cref="LabelFor"/>
    public string? MethodFor(bool english) => Pick(MethodEn, MethodVi, english);

    private static string? Pick(string? en, string? vi, bool english)
        => english && !string.IsNullOrWhiteSpace(en) ? en : (string.IsNullOrWhiteSpace(vi) ? null : vi);
}

/// <summary>Thân phản hồi <c>GET /api/v2/iqc/tickets/{id}/items</c>.</summary>
public sealed class IqcTicketItemsResponse
{
    public long TicketId { get; set; }

    /// <summary>Spec đã khớp, hoặc <c>null</c> khi phiếu dùng ma trận mặc định.</summary>
    public string? SpecNo { get; set; }

    /// <summary>Cả bộ đến từ ma trận mặc định — UI hiện băng nhắc để sáu tháng
    /// sau còn phân biệt được hồ sơ nào kiểm theo spec thật.</summary>
    public bool FromDefaultMatrix { get; set; }

    public List<IqcCheckItemDto> Items { get; set; } = new();
}

/// <summary>Body <c>PUT /api/v2/iqc/tickets/{id}/items/{itemId}</c> — ghi phán
/// định một hạng mục. <c>Pass=null</c> đưa hạng mục về CHƯA KIỂM (bấm nhầm).</summary>
public sealed class SetIqcItemBody
{
    public bool? Pass { get; set; }
    public string? MeasuredValue { get; set; }
    public string? DefectCode { get; set; }
}

// ── P12 bước 2b — soạn tiêu chuẩn theo mã nguyên liệu ────────────────────

/// <summary>Một dòng tiêu chuẩn của mã nguyên liệu (màn soạn).</summary>
public sealed class IqcSpecItemDto
{
    public long Id { get; set; }
    public string ItemId { get; set; } = "";
    public int Seq { get; set; }
    public string? GroupCode { get; set; }
    public string? GroupLabelVi { get; set; }
    public string? GroupLabelEn { get; set; }
    public string? LabelVi { get; set; }
    public string? LabelEn { get; set; }
    public string? AcceptanceVi { get; set; }
    public string? AcceptanceEn { get; set; }
    public string? MethodVi { get; set; }
    public string? MethodEn { get; set; }
    public string? SourceFrequency { get; set; }

    /// <summary><c>false</c> = đã xoá mềm; vẫn giữ để truy vết và khôi phục.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Dòng đến từ file master — sửa ở đây thì lần import sau ghi đè.</summary>
    public bool FromMasterFile { get; set; }

    public string LabelFor(bool english) => Pick(LabelEn, LabelVi, english) ?? ItemId;
    public string? GroupLabelFor(bool english) => Pick(GroupLabelEn, GroupLabelVi, english);
    public string? AcceptanceFor(bool english) => Pick(AcceptanceEn, AcceptanceVi, english);
    public string? MethodFor(bool english) => Pick(MethodEn, MethodVi, english);

    private static string? Pick(string? en, string? vi, bool english)
        => english && !string.IsNullOrWhiteSpace(en) ? en : (string.IsNullOrWhiteSpace(vi) ? null : vi);
}

/// <summary>Một hạng mục thư viện để chọn khi thêm dòng tiêu chuẩn.</summary>
public sealed class IqcLibraryOptionDto
{
    public string ItemId { get; set; } = "";
    public string? GroupCode { get; set; }
    public string? GroupLabelVi { get; set; }
    public string? GroupLabelEn { get; set; }
    public string? ItemVi { get; set; }
    public string? ItemEn { get; set; }
    public string? DefaultAcceptanceVi { get; set; }
    public string? DefaultAcceptanceEn { get; set; }
    public string? DefaultMethodVi { get; set; }
    public string? DefaultMethodEn { get; set; }

    public string LabelFor(bool english) =>
        (english && !string.IsNullOrWhiteSpace(ItemEn) ? ItemEn : ItemVi) ?? ItemId;
}

/// <summary>Thân phản hồi <c>GET /api/v2/iqc/specs/{materialCode}</c>.</summary>
public sealed class IqcSpecEditResponse
{
    public string MaterialCode { get; set; } = "";

    /// <summary><c>null</c> = mã chưa có tiêu chuẩn riêng (1 trong 590) — đang
    /// kiểm theo ma trận mặc định.</summary>
    public string? SpecNo { get; set; }
    public bool SpecActive { get; set; }

    /// <summary>Spec do người dùng soạn trong app, không phải từ file master.</summary>
    public bool IsLocalSpec { get; set; }

    public List<IqcSpecItemDto> Items { get; set; } = new();
    public List<IqcLibraryOptionDto> Library { get; set; } = new();
}

/// <summary>Body <c>POST /api/v2/iqc/specs/items</c>. Mã nguyên liệu đi trong
/// BODY, không phải URL — 623/946 mã có dấu cách, 56 có <c>/</c>.</summary>
public sealed class AddIqcSpecItemBody
{
    public string? MaterialCode { get; set; }
    public string? ItemId { get; set; }
    public string? AcceptanceVi { get; set; }
    public string? AcceptanceEn { get; set; }
    public string? MethodVi { get; set; }
    public string? MethodEn { get; set; }
    public string? SourceFrequency { get; set; }
}


/// <summary>Thân phản hồi <c>POST /api/v2/iqc/tickets/{id}/complete</c>.</summary>
public sealed class CompleteIqcResponse
{
    /// <summary>Pending / Pass / Fail.</summary>
    public string Result { get; set; } = "Pending";
    public int Total { get; set; }

    /// <summary>Số hạng mục CHƯA kiểm — &gt;0 nghĩa là chưa chốt được.</summary>
    public int Pending { get; set; }
    public int Failed { get; set; }
}
