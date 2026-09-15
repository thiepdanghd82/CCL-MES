using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Prepress;

/// <summary>
/// P10.7b-3 — VN message bank for the PREPRESS row-write surface.
/// Mirrors WorkOrderErrorLocaliser pattern: every operator-facing
/// banner string lives here so the xUnit suite can lock the wording
/// without booting MAUI. PrepressDashboard.razor + the 3 child
/// components call these static methods directly.
/// </summary>
public static class PrepressErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.
    /// Covers the 422 codes the PrepressController emits + the shared
    /// 428/400 codes the prelude raises.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                      => "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.",
            "wo.material_row_not_found"         => "Không thấy dòng vật tư này — nạp lại danh sách.",
            "wo.invalid_phase"                  => "Lệnh không còn ở bước chuẩn bị nên không ghi kiểm được — nạp lại màn hình.",
            "wo.if_match_required"              => "Dữ liệu trên màn hình đã cũ — nạp lại danh sách rồi làm lại.",
            "wo.idempotency_key_required"       => "Yêu cầu thiếu khoá chống trùng — báo IT.",
            "prepress.invalid_status"           => "Trạng thái không hợp lệ — chỉ nhận Chờ / OK / NG.",
            "prepress.invalid_reason_code"      => "Mã lỗi NG không có trong danh mục — chọn một mã trong danh sách.",
            "prepress.invalid_ng_note"          => "Đánh NG thì bắt buộc ghi mô tả (1–500 ký tự).",
            "prepress.special_accept_forbidden" => "Chỉ kỹ sư hoặc quản đốc mới chấp nhận đặc biệt vật tư được — nhờ người có quyền.",
            // Nói người vận hành phải LÀM GÌ, không nói luật bị vi phạm. Câu
            // "lot_required" trần thì họ đọc xong vẫn không biết gõ vào đâu.
            "prepress.lot_required"             => "Nhập số lô in trên cuộn trước khi xác nhận OK cho dòng này.",
            "prepress.part_scan_required"       => "Quét nhãn vật tư trước khi xác nhận OK cho dòng này.",
            "prepress.part_scan_mismatch"       => "Mã vừa quét không khớp dòng vật tư này — đang cầm nhầm vật tư.",
            "prepress.lot_not_released"         => "Lô này chưa qua IQC — lấy lô đã được thả, hoặc nhờ kỹ sư chấp nhận đặc biệt.",

            // Mã lỗi LÔ — trước đây chỉ với tới được ở đường tiêu thụ nên chưa
            // ai dịch. Từ khi gắn nhãn lô cũng chặn thật khi SAI VẬT TƯ, chúng
            // hiện ngay trên màn Pre-press. Không có bốn dòng này thì người vận
            // hành nhận nguyên chuỗi "HTTP 422 · lot.part_mismatch · …".
            "lot.part_mismatch"                 => "Lô này thuộc vật tư khác — đối chiếu nhãn cuộn với dòng vật tư.",
            "lot.rejected"                      => "Lô này đã bị IQC loại — KHÔNG được đưa lên máy. Lấy lô khác thay.",
            "lot.not_released"                  => "Lô này IQC chưa thả — chờ kết luận của IQC.",
            "lot.expired"                       => "Lô này đã quá hạn — nhờ QC kiểm lại, hoặc dùng lô khác.",
            "lot.not_found"                     => "Hệ thống không có số lô này — kiểm lại số, hoặc nhờ kho khai báo ở IQC.",
            _                                   => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.Prepress.PrepressSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Danh sách vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — ghi lại.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khoá chống trùng — báo IT.",
        "http.empty_body"             => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                             => $"Unknown error code ({code}).",
    };

    // ── Scan materials (PREPRESS) — client-side match outcomes ──────────
    // These are NOT server errors: the barcode is matched against the WO's
    // BOM in-app (MaterialBarcodeMatcher) before any PUT. Wording lives here
    // so the scan flow stays testable without booting MAUI.

    /// <summary>Banner for a non-success scan match (NoMatch / Multiple / EmptyCode).</summary>
    public static string ScanOutcomeMessage(MaterialMatchOutcome outcome, string partNo) => outcome switch
    {
        MaterialMatchOutcome.NoMatch   => $"Part {partNo} is not in this WO's BOM — record by hand if correct.",
        MaterialMatchOutcome.AllOk     => $"All BOM lines for {partNo} are already OK.",
        MaterialMatchOutcome.EmptyCode => "Could not read a part number from the scan — try again.",
        _                              => "",
    };

    /// <summary>Banner when the scanned material row was just recorded OK.</summary>
    public static string ScanRecordedOk(string materialCode) => $"✓ {materialCode} recorded OK.";

    /// <summary>Banner when the scanned material row was already OK.</summary>
    public static string ScanAlreadyOk(string materialCode) => $"{materialCode} is already OK.";

    /// <summary>
    /// Banner khi mã gõ tay KHÔNG khớp mã của chính dòng đó.
    ///
    /// <para>Câu cũ bảo "use Special Accept to record OK" — nay SAI: server
    /// chặn sai mã ở CẢ đường Special Accept (<c>prepress.part_scan_mismatch</c>),
    /// vì nhân nhượng là chấp nhận một LÔ chưa đủ điều kiện, không phải giấy
    /// phép ghi nhầm mã vào hồ sơ truy xuất. Chỉ đường duy nhất đi tiếp là gõ
    /// đúng mã của dòng, hoặc gõ vào ĐÚNG dòng mang mã đó.</para>
    ///
    /// <para>Câu chữ chỉ sai hướng còn tệ hơn không có câu nào: người vận hành
    /// bấm Special Accept, lại bị chặn, và không hiểu vì sao.</para>
    /// </summary>
    public static string ScanMismatch(string typedPart, string lineCode) =>
        $"Typed {typedPart} ≠ line {lineCode} — check you are holding the right material, "
        + "or enter it on the line that carries that code.";
}
