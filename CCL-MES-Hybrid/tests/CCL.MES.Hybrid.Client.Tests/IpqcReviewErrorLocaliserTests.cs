using CCL.MES.Hybrid.Client.IpqcReview;
using CCL.MES.Shared.Envelopes;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7d-3 — locks every Vietnamese banner the operator sees on the
/// IPQC + QA Approval dashboards. Mirrors PrepressErrorLocaliserTests /
/// RunningSurfaceErrorLocaliserTests: if a future PR changes a VN
/// string, the locked test fails so the operator's screen can't drift
/// silently.
///
/// Q3 dual-sig has its own dedicated lock test: the
/// <see cref="IpqcReviewErrorLocaliser.Q3SameUserBanner"/> constant
/// drives the QaApprovalDashboard banner + button-tooltip, and BOTH
/// LocaliseApiError("qa.same_user_as_ipqc_submitter") and
/// LocaliseSetError("qa.same_user_as_ipqc_submitter") must produce the
/// SAME message (one server-bounce path + one client-gated path).
/// </summary>
public sealed class IpqcReviewErrorLocaliserTests
{
    // ── LocaliseApiError ───────────────────────────────────────────

    [Theory]
    [InlineData("wo.not_found",                       "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.")]
    [InlineData("wo.invalid_phase",                   "Lệnh không còn ở bước cho phép thao tác này — nạp lại màn hình.")]
    [InlineData("wo.if_match_required",               "Dữ liệu trên màn hình đã cũ — nạp lại rồi làm lại.")]
    [InlineData("wo.idempotency_key_required",        "Yêu cầu thiếu khoá chống trùng — báo IT.")]
    [InlineData("ipqc.invalid_status",                "Trạng thái phải là OK hoặc NG.")]
    [InlineData("ipqc.invalid_reason_code",           "Mã lỗi NG không có trong danh mục — chọn một mã trong danh sách.")]
    [InlineData("ipqc.invalid_ng_note",               "Đánh NG thì bắt buộc ghi mô tả (1–500 ký tự).")]
    [InlineData("ipqc.invalid_judgment",              "Kết luận phải là Cho chạy / Dừng chuyền / Chấp nhận đặc biệt.")]
    [InlineData("ipqc.judgment_inconsistent",         "Đang có hạng mục NG nên không Cho chạy được — chọn Dừng chuyền hoặc Chấp nhận đặc biệt.")]
    // 2026-09-14: câu cũ nói SAI luật — "4 slots" chỉ đúng với WO legacy; WO chạy
    // chế độ hạng mục (data-driven) có thể có 14 hạng mục. Sửa CÂU, không nới luật.
    [InlineData("ipqc.not_ready_for_judgment",        "Còn hạng mục chưa xác nhận OK/NG — chấm hết rồi mới phán định được.")]
    [InlineData("ipqc.invalid_special_accept_reason", "Chấp nhận đặc biệt thì bắt buộc ghi lý do (1–500 ký tự).")]
    [InlineData("qa.invalid_outcome",                 "Quyết định QA phải là Duyệt hoặc Từ chối.")]
    [InlineData("qa.invalid_qa_reason",               "Bắt buộc ghi lý do QA (1–500 ký tự).")]
    public void Locked_VN_banner_for_each_api_error_code(string code, string expected)
    {
        var error = new ApiError { Code = code, MessageEn = "ignored" };
        Assert.Equal(expected, IpqcReviewErrorLocaliser.LocaliseApiError(422, error));
    }

    [Fact]
    public void Q3_same_user_banner_explains_dual_sig_requirement()
    {
        var error = new ApiError { Code = "qa.same_user_as_ipqc_submitter", MessageEn = "ignored" };
        var msg = IpqcReviewErrorLocaliser.LocaliseApiError(422, error);
        // Ý ĐỒ của test giữ nguyên: băng-rôn phải NÊU TÊN luật và nói rõ phải
        // khác ai. Chỉ ngôn ngữ đổi — 14-09 dịch sang tiếng Việt vì người đọc
        // là người đứng máy, không phải lập trình viên.
        Assert.Contains("4-mắt", msg);
        Assert.Contains("KHÁC người đã phán định IPQC", msg);
    }

    [Fact]
    public void Unknown_api_code_falls_through_with_status_and_messageEn()
    {
        var error = new ApiError { Code = "novel.code", MessageEn = "some english msg" };
        var msg = IpqcReviewErrorLocaliser.LocaliseApiError(418, error);
        Assert.Contains("418", msg);
        Assert.Contains("novel.code", msg);
        Assert.Contains("some english msg", msg);
    }

    // ── LocaliseSetError ───────────────────────────────────────────

    [Theory]
    [InlineData("wo.state_conflict",                  "Lệnh vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — bấm lại.")]
    [InlineData("wo.if_match_required",               "Data session has not been reloaded — scan the WO again.")]
    [InlineData("wo.idempotency_key_required",        "Yêu cầu thiếu khoá chống trùng — báo IT.")]
    [InlineData("ipqc.judgment_inconsistent",         "Đang có hạng mục NG nên không Cho chạy được — chọn Dừng chuyền hoặc Chấp nhận đặc biệt.")]
    // 2026-09-14: câu cũ nói SAI luật — "4 slots" chỉ đúng với WO legacy; WO chạy
    // chế độ hạng mục (data-driven) có thể có 14 hạng mục. Sửa CÂU, không nới luật.
    [InlineData("ipqc.not_ready_for_judgment",        "Còn hạng mục chưa xác nhận OK/NG — chấm hết rồi mới phán định được.")]
    [InlineData("ipqc.invalid_special_accept_reason", "Chấp nhận đặc biệt thì bắt buộc ghi lý do (1–500 ký tự).")]
    [InlineData("qa.invalid_qa_reason",               "Bắt buộc ghi lý do QA (1–500 ký tự).")]
    [InlineData("http.empty_body",                    "Máy chủ trả về phản hồi rỗng — báo IT.")]
    public void Locked_VN_banner_for_each_in_band_error_code(string code, string expected)
    {
        Assert.Equal(expected, IpqcReviewErrorLocaliser.LocaliseSetError(code));
    }

    [Fact]
    public void SetError_q3_same_user_matches_api_q3_same_user_word_for_word()
    {
        // Server emits qa.same_user_as_ipqc_submitter via both 422 envelope
        // (controller MapError path) and 200 in-band ErrorCode (current
        // domain shape). Both code paths must produce IDENTICAL VN text
        // so operators never see two different wordings for the same
        // policy violation.
        var fromApi = IpqcReviewErrorLocaliser.LocaliseApiError(422,
            new ApiError { Code = "qa.same_user_as_ipqc_submitter", MessageEn = "ignored" });
        var fromSet = IpqcReviewErrorLocaliser.LocaliseSetError("qa.same_user_as_ipqc_submitter");
        Assert.Equal(fromApi, fromSet);
    }

    [Fact]
    public void Unknown_set_code_falls_through_with_code_visible()
    {
        var msg = IpqcReviewErrorLocaliser.LocaliseSetError("novel.code");
        Assert.Contains("novel.code", msg);
    }

    // ── Q3 client-guard banner (constant) ──────────────────────────

    [Fact]
    public void Q3SameUserBanner_constant_is_non_empty_and_explains_dual_sig()
    {
        // Locks the string shown inline in QaApprovalDashboard when the
        // signed-in user matches the IPQC submitter. Deliberately
        // self-contained (operator-actionable) text — explains WHAT +
        // WHY + WHAT TO DO.
        var msg = IpqcReviewErrorLocaliser.Q3SameUserBanner;
        Assert.False(string.IsNullOrWhiteSpace(msg));
        // Ý ĐỒ giữ nguyên (nêu tên luật + nói phải làm gì); 14-09 đổi sang tiếng Việt.
        Assert.Contains("4-mắt", msg);
        Assert.Contains("đăng nhập bằng tài khoản QC khác", msg);
    }
}
