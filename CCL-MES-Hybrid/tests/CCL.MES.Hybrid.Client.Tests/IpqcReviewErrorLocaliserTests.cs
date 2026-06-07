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
    [InlineData("wo.not_found",                       "Không tìm thấy WO trên máy chủ.")]
    [InlineData("wo.invalid_phase",                   "WO không ở giai đoạn cho phép hành động này — tải lại trạng thái.")]
    [InlineData("wo.if_match_required",               "Phiên dữ liệu hết hạn — tải lại trạng thái.")]
    [InlineData("wo.idempotency_key_required",        "Yêu cầu thiếu khóa idempotency — báo IT.")]
    [InlineData("ipqc.invalid_status",                "Trạng thái slot phải là OK hoặc NG.")]
    [InlineData("ipqc.invalid_reason_code",           "Mã NG không có trong danh mục — chọn lại từ danh sách.")]
    [InlineData("ipqc.invalid_ng_note",               "Ghi chú NG bắt buộc khi đánh NG (1-500 ký tự).")]
    [InlineData("ipqc.invalid_judgment",              "Phán quyết phải là Go Run / Stop Line / Special Accept.")]
    [InlineData("ipqc.judgment_inconsistent",         "Có slot NG — không được chọn Go Run; chọn Stop Line hoặc Special Accept.")]
    [InlineData("ipqc.not_ready_for_judgment",        "Phải xử lý đủ 4 slot (Material + 3 Print) trước khi phán quyết.")]
    [InlineData("ipqc.invalid_special_accept_reason", "Lý do Special Accept bắt buộc (1-500 ký tự).")]
    [InlineData("qa.invalid_outcome",                 "Kết quả QA phải là Approve hoặc Reject.")]
    [InlineData("qa.invalid_qa_reason",               "Lý do QA bắt buộc (1-500 ký tự).")]
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
        Assert.Contains("dual-sig", msg);
        Assert.Contains("KHÁC người nộp IPQC", msg);
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
    [InlineData("wo.state_conflict",                  "Một thao tác khác đã cập nhật WO này. Đang tải lại trạng thái mới nhất — thử lại.")]
    [InlineData("wo.if_match_required",               "Phiên dữ liệu chưa được tải lại — quét lại WO.")]
    [InlineData("wo.idempotency_key_required",        "Yêu cầu thiếu khóa idempotency — báo IT.")]
    [InlineData("ipqc.judgment_inconsistent",         "Có slot NG — không được chọn Go Run; chọn Stop Line hoặc Special Accept.")]
    [InlineData("ipqc.not_ready_for_judgment",        "Phải xử lý đủ 4 slot (Material + 3 Print) trước khi phán quyết.")]
    [InlineData("ipqc.invalid_special_accept_reason", "Lý do Special Accept bắt buộc (1-500 ký tự).")]
    [InlineData("qa.invalid_qa_reason",               "Lý do QA bắt buộc (1-500 ký tự).")]
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
        Assert.Contains("dual-sig", msg);
        Assert.Contains("Đăng xuất", msg);
        Assert.Contains("tài khoản QC khác", msg);
    }
}
