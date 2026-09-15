using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.IpqcReview;

/// <summary>
/// P10.7d-3 — VN message bank for the IPQC + QA Approval write surface.
/// Mirrors <see cref="CCL.MES.Hybrid.Client.RunningSurface.RunningSurfaceErrorLocaliser"/>
/// pattern so xUnit can lock wording without booting MAUI.
///
/// Covers every <see cref="ApiError.Code"/> the IpqcReviewController
/// emits (7 ipqc.* + 3 qa.* + 1 wo.invalid_phase shared) plus the
/// shared 428 / 400 / 404 envelope codes and the in-band 409
/// state-conflict + http.empty_body codes returned via
/// <see cref="CCL.MES.Shared.IpqcReview.IpqcSetResponse"/>.
/// </summary>
public static class IpqcReviewErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                          => "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.",
            "wo.invalid_phase"                      => "Lệnh không còn ở bước cho phép thao tác này — nạp lại màn hình.",
            "wo.if_match_required"                  => "Dữ liệu trên màn hình đã cũ — nạp lại rồi làm lại.",
            "wo.idempotency_key_required"           => "Yêu cầu thiếu khoá chống trùng — báo IT.",
            "ipqc.invalid_status"                   => "Trạng thái phải là OK hoặc NG.",
            "ipqc.invalid_reason_code"              => "Mã lỗi NG không có trong danh mục — chọn một mã trong danh sách.",
            "ipqc.invalid_ng_note"                  => "Đánh NG thì bắt buộc ghi mô tả (1–500 ký tự).",
            "ipqc.invalid_judgment"                 => "Kết luận phải là Cho chạy / Dừng chuyền / Chấp nhận đặc biệt.",
            "ipqc.judgment_inconsistent"            => "Đang có hạng mục NG nên không Cho chạy được — chọn Dừng chuyền hoặc Chấp nhận đặc biệt.",
            "ipqc.signature_required"               => "Nhập tài khoản và mật khẩu của người đánh giá IPQC để ký duyệt.",
            "ipqc.signature_invalid"                => "Tài khoản hoặc mật khẩu không đúng.",
            // Quyền theo KẾT QUẢ phán định (Thiệp chốt 2026-09-15): ba nút là ba
            // quyết định khác nhau nên ba quyền khác nhau. Nói rõ THIẾU quyền NÀO —
            // "không có quyền" chung chung thì quản trị không biết tick ô nào.
            "ipqc.go_run_forbidden"                 => "Tài khoản này không có quyền Phê duyệt sản xuất nên không bấm Cho chạy được. Nhờ quản trị tick ô \"Phê duyệt sản xuất\" trong Quản lý tài khoản.",
            "ipqc.special_accept_forbidden"         => "Tài khoản này không có quyền Phê duyệt QC nên không đề nghị Chấp nhận đặc biệt được.",
            "ipqc.stop_line_forbidden"              => "Tài khoản này không có quyền Phê duyệt QC nên không bấm Dừng chuyền được.",
            "ipqc.signer_not_allowed"               => "Tài khoản này không có quyền phán định IPQC.",
            "ipqc.signature_locked"                 => "Tài khoản đang tạm khoá do gõ sai nhiều lần. Thử lại sau ít phút.",
            "ipqc.signature_password_not_set"       => "Tài khoản này chưa đặt mật khẩu riêng nên chưa ký được. Đăng nhập một lần để đổi mật khẩu, rồi ký lại.",
            "ipqc.not_ready_for_judgment"           => "Còn hạng mục chưa xác nhận OK/NG — chấm hết rồi mới phán định được.",
            "ipqc.material_divergence_unresolved"    => "Vật tư lệch so với dữ liệu IQC và chưa được kỹ sư phê duyệt — nhập lý do rồi bấm \"Kỹ sư phê duyệt\" ở từng dòng LỆCH, sau đó mới Cho chạy được.",
            "ipqc.invalid_item"                     => "Hạng mục này không thuộc bộ hạng mục IPQC của lệnh — nạp lại màn hình.",
            "ipqc.slot_write_in_item_mode"          => "Lệnh này chấm theo hạng mục, không theo 4 ô cũ — nạp lại màn hình.",
            "leg.not_found"                         => "Không thấy nhánh công đoạn này trên lệnh — nạp lại màn hình.",
            "leg.invalid_phase"                     => "Nhánh công đoạn chưa thể chuyển sang bước đó từ bước hiện tại.",
            "ipqc.invalid_special_accept_reason"    => "Chấp nhận đặc biệt thì bắt buộc ghi lý do (1–500 ký tự).",
            "qa.invalid_outcome"                    => "Quyết định QA phải là Duyệt hoặc Từ chối.",
            "qa.invalid_qa_reason"                  => "Bắt buộc ghi lý do QA (1–500 ký tự).",
            "qa.same_user_as_ipqc_submitter"        => "Người duyệt QA phải KHÁC người đã phán định IPQC — nguyên tắc 4-mắt. Nhờ người khác duyệt.",
            // ── IPQC first-article — MATERIAL (SYSTEM) reconciliation (h-3) ──
            "ipqc.invalid_material_line"            => "Dòng vật tư này không thuộc lệnh — nạp lại bảng.",
            "material.invalid_outcome"              => "Quyết định phê duyệt phải là Duyệt hoặc Từ chối.",
            "material.invalid_reason"               => "Bắt buộc ghi lý do cho quyết định phê duyệt (1–500 ký tự).",
            "material.not_divergent"                => "Dòng vật tư này không lệch nên không cần kỹ sư phê duyệt.",
            "material.same_user_as_confirmer"       => "Kỹ sư ký duyệt phải KHÁC người đã xác nhận dòng vật tư này — nguyên tắc 4-mắt. Nhờ kỹ sư khác ký.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band
    /// <see cref="CCL.MES.Shared.IpqcReview.IpqcSetResponse.ErrorCode"/>
    /// (returned on 200 / 409 / 422) into the operator-facing banner.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"                     => "Lệnh vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — bấm lại.",
        "wo.if_match_required"                  => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required"           => "Yêu cầu thiếu khoá chống trùng — báo IT.",
        "qa.same_user_as_ipqc_submitter"        => "Người duyệt QA phải KHÁC người đã phán định IPQC — nguyên tắc 4-mắt. Nhờ người khác duyệt.",
        "ipqc.judgment_inconsistent"            => "Đang có hạng mục NG nên không Cho chạy được — chọn Dừng chuyền hoặc Chấp nhận đặc biệt.",
        "ipqc.signature_required"               => "Nhập tài khoản và mật khẩu của người đánh giá IPQC để ký duyệt.",
        "ipqc.signature_invalid"                => "Tài khoản hoặc mật khẩu không đúng.",
        // Quyền theo KẾT QUẢ phán định (Thiệp chốt 2026-09-15): ba nút là ba
        // quyết định khác nhau nên ba quyền khác nhau. Nói rõ THIẾU quyền NÀO —
        // "không có quyền" chung chung thì quản trị không biết tick ô nào.
        "ipqc.go_run_forbidden"                 => "Tài khoản này không có quyền Phê duyệt sản xuất nên không bấm Cho chạy được. Nhờ quản trị tick ô \"Phê duyệt sản xuất\" trong Quản lý tài khoản.",
        "ipqc.special_accept_forbidden"         => "Tài khoản này không có quyền Phê duyệt QC nên không đề nghị Chấp nhận đặc biệt được.",
        "ipqc.stop_line_forbidden"              => "Tài khoản này không có quyền Phê duyệt QC nên không bấm Dừng chuyền được.",
        "ipqc.signer_not_allowed"               => "Tài khoản này không có quyền phán định IPQC.",
        "ipqc.signature_locked"                 => "Tài khoản đang tạm khoá do gõ sai nhiều lần. Thử lại sau ít phút.",
        "ipqc.signature_password_not_set"       => "Tài khoản này chưa đặt mật khẩu riêng nên chưa ký được. Đăng nhập một lần để đổi mật khẩu, rồi ký lại.",
        "ipqc.not_ready_for_judgment"           => "Còn hạng mục chưa xác nhận OK/NG — chấm hết rồi mới phán định được.",
            "ipqc.material_divergence_unresolved"    => "Vật tư lệch so với dữ liệu IQC và chưa được kỹ sư phê duyệt — nhập lý do rồi bấm \"Kỹ sư phê duyệt\" ở từng dòng LỆCH, sau đó mới Cho chạy được.",
            "ipqc.invalid_item"                     => "Hạng mục này không thuộc bộ hạng mục IPQC của lệnh — nạp lại màn hình.",
            "ipqc.slot_write_in_item_mode"          => "Lệnh này chấm theo hạng mục, không theo 4 ô cũ — nạp lại màn hình.",
            "leg.not_found"                         => "Không thấy nhánh công đoạn này trên lệnh — nạp lại màn hình.",
            "leg.invalid_phase"                     => "Nhánh công đoạn chưa thể chuyển sang bước đó từ bước hiện tại.",
        "ipqc.invalid_special_accept_reason"    => "Chấp nhận đặc biệt thì bắt buộc ghi lý do (1–500 ký tự).",
        "qa.invalid_qa_reason"                  => "Bắt buộc ghi lý do QA (1–500 ký tự).",
        // ── IPQC first-article — MATERIAL (SYSTEM) reconciliation (h-3) ──
        "ipqc.invalid_material_line"            => "Dòng vật tư này không thuộc lệnh — nạp lại bảng.",
        "material.invalid_outcome"              => "Quyết định phê duyệt phải là Duyệt hoặc Từ chối.",
        "material.invalid_reason"               => "Bắt buộc ghi lý do cho quyết định phê duyệt (1–500 ký tự).",
        "material.not_divergent"                => "Dòng vật tư này không lệch nên không cần kỹ sư phê duyệt.",
        "material.same_user_as_confirmer"       => "Kỹ sư ký duyệt phải KHÁC người đã xác nhận dòng vật tư này — nguyên tắc 4-mắt. Nhờ kỹ sư khác ký.",
        "http.empty_body"                       => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                                       => $"Unknown error code ({code}).",
    };

    /// <summary>Q3 dual-sig client-side guard. Banner shown when the
    /// signed-in user matches the IPQC submitter — the Approve button
    /// is disabled and this string is rendered inline so the operator
    /// understands BEFORE the server bounces them with 422.</summary>
    public const string Q3SameUserBanner =
        "Bạn là người đã phán định IPQC cho lệnh này — nguyên tắc 4-mắt yêu cầu người duyệt QA phải là người khác. " +
        "Nhờ người khác duyệt, hoặc đăng xuất rồi đăng nhập bằng tài khoản QC khác.";
}
