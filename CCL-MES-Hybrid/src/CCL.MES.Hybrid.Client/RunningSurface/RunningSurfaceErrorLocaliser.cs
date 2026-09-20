using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.RunningSurface;

/// <summary>
/// P10.7c-3 — VN message bank for the SETTING + RUNNING + PAUSED write
/// surface. Mirrors PrepressErrorLocaliser pattern: every operator-facing
/// banner string lives here so the xUnit suite can lock the wording
/// without booting MAUI. Setting/RunningDashboard.razor + the pause/finish
/// modals call these static methods directly.
/// </summary>
public static class RunningSurfaceErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.
    /// Covers the 422 codes the RunningSurfaceController emits + the
    /// shared 428/400/404 codes the prelude raises.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                          => "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.",
            "wo.invalid_phase"                      => "Lệnh không còn ở bước cho phép thao tác này — nạp lại màn hình.",
            "wo.if_match_required"                  => "Dữ liệu trên màn hình đã cũ — nạp lại rồi làm lại.",
            "wo.idempotency_key_required"           => "Yêu cầu thiếu khoá chống trùng — báo IT.",
            "running.setting_not_started"           => "Lệnh chưa vào bước cài đặt nên chưa Hoàn tất được — nạp lại màn hình.",
            "running.invalid_body"                  => "Dữ liệu gửi lên không hợp lệ — báo IT.",
            // Hai mã dưới đây là HAI việc khác nhau — xem MaterialLineBlock.
            // Cả hai đều phải nói DÒNG NÀO, MÃ NÀO: một WO có nhiều dòng vật
            // tư, câu chung chung thì người đứng máy không biết sờ vào đâu.
            "run.material_line_unchecked"           => LineUnchecked(error),
            "run.material_lot_unusable"             => LotUnusable(error),
            "running.invalid_qty_delta"             => "Số lượng phải lớn hơn 0 — muốn trừ bớt thì dùng \"Sửa sản lượng\".",
            "running.invalid_reason_code"           => "Mã lý do không có trong danh mục — chọn một mã trong danh sách.",
            "running.invalid_ng_note"               => "Nhập số lượng NG thì bắt buộc ghi mô tả (1–500 ký tự).",
            "running.invalid_note"                  => "Ghi chú dài quá 500 ký tự — rút ngắn lại.",
            "running.invalid_correction_reason"     => "Bắt buộc ghi lý do sửa (1–500 ký tự).",
            "running.linked_entry_not_found"        => "Không thấy bản ghi cần sửa — nạp lại danh sách.",
            "running.linked_entry_wrong_wo"         => "Bản ghi cần sửa không thuộc lệnh này — chọn một dòng trong danh sách.",
            "running.no_active_session"             => "Máy chưa chạy phiên nào — bấm \"Bắt đầu chạy\" trước đã.",
            "running.no_active_pause"               => "Không có lần dừng nào đang mở — nạp lại màn hình.",
            "running.no_production"                 => "Chưa có sản lượng nào nên chưa kết thúc lệnh được.",
            "setting.incomplete"                    => "Còn hạng mục cài đặt chưa OK — xác nhận hết rồi mới Hoàn tất được.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>
    /// "Dòng 2 · 30032127" — cụm định danh dòng, dựng từ
    /// <see cref="ApiError.Details"/>. Thiếu dữ kiện (server cũ, hoặc lỗi
    /// dựng từ nơi khác) thì trả chuỗi rỗng và câu gọi nó phải vẫn đọc được —
    /// nên mọi chỗ dùng đều nối có điều kiện, không nối thẳng.
    /// </summary>
    private static string LineLabel(ApiError error)
    {
        var d = error.Details;
        if (d is null) return "";
        d.TryGetValue("bom_line_idx", out var idx);
        d.TryGetValue("material_code", out var code);
        var hasIdx = !string.IsNullOrWhiteSpace(idx);
        var hasCode = !string.IsNullOrWhiteSpace(code);
        if (!hasIdx && !hasCode) return "";
        if (!hasCode) return $"dòng {idx}";
        return hasIdx ? $"dòng {idx} · {code}" : code!;
    }

    /// <summary>"(và 2 dòng nữa)" — chỉ hiện khi thật sự có dòng khác.</summary>
    private static string MoreLabel(ApiError error)
    {
        if (error.Details is null) return "";
        if (!error.Details.TryGetValue("more_count", out var raw)) return "";
        return int.TryParse(raw, out var n) && n > 0 ? $" (và {n} dòng nữa)" : "";
    }

    /// <summary>
    /// Dòng vật tư CHƯA ĐƯỢC KIỂM ở Pre-press. Việc phải làm là quét và xác
    /// nhận dòng đó — KHÔNG phải đổi lô, cũng KHÔNG phải xin chấp nhận đặc
    /// biệt. Câu cũ gộp chung với lỗi lô nên chỉ sai việc (đo 2026-09-18,
    /// WO-TEST-02: hai dòng Pending bị báo là "lô không còn được IQC thả").
    /// </summary>
    private static string LineUnchecked(ApiError error)
    {
        var who = LineLabel(error);
        var head = string.IsNullOrEmpty(who)
            ? "Còn dòng vật tư chưa được xác nhận ở bước Chuẩn bị"
            : $"Vật tư {who} chưa được xác nhận ở bước Chuẩn bị";
        return head + MoreLabel(error) + " — xác nhận dòng đó với lô đã được IQC thả, rồi mới chạy máy.";
    }

    /// <summary>
    /// Lô bị IQC đánh trượt SAU khi Pre-press đã gắn. Câu phải nói rõ HAI
    /// đường ra, vì lúc này WO đã qua Pre-press nên người vận hành không tự
    /// sửa dòng vật tư được nữa.
    /// </summary>
    private static string LotUnusable(ApiError error)
    {
        var who = LineLabel(error);
        var lotNo = error.Details is not null
            && error.Details.TryGetValue("lot_no", out var l) && !string.IsNullOrWhiteSpace(l)
            ? $" (lô {l})" : "";
        var head = string.IsNullOrEmpty(who)
            ? "Có lô vật tư không còn được IQC thả"
            : $"Vật tư {who}{lotNo} không còn được IQC thả";
        return head + MoreLabel(error) + " — thay lô, hoặc nhờ kỹ sư chấp nhận đặc biệt, rồi mới chạy máy.";
    }

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.RunningSurface.RunningSurfaceSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Lệnh vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — bấm lại.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khoá chống trùng — báo IT.",
        "http.empty_body"             => "Máy chủ trả về phản hồi rỗng — báo IT.",
        "setting.incomplete"          => "Còn hạng mục cài đặt chưa OK — xác nhận hết rồi mới Hoàn tất được.",
        _                             => $"Unknown error code ({code}).",
    };
}
