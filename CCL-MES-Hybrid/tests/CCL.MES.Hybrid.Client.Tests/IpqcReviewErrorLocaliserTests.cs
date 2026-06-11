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
    [InlineData("wo.not_found",                       "WO not found on the server.")]
    [InlineData("wo.invalid_phase",                   "WO is not in a phase that allows this action — reload the state.")]
    [InlineData("wo.if_match_required",               "Data session expired — reload the state.")]
    [InlineData("wo.idempotency_key_required",        "Request is missing the idempotency key — contact IT.")]
    [InlineData("ipqc.invalid_status",                "Slot status must be OK or NG.")]
    [InlineData("ipqc.invalid_reason_code",           "NG reason code is not in the catalog — choose one from the list.")]
    [InlineData("ipqc.invalid_ng_note",               "An NG note is required when marking NG (1-500 characters).")]
    [InlineData("ipqc.invalid_judgment",              "Judgment must be Go Run / Stop Line / Special Accept.")]
    [InlineData("ipqc.judgment_inconsistent",         "There is an NG slot — Go Run is not allowed; choose Stop Line or Special Accept.")]
    [InlineData("ipqc.not_ready_for_judgment",        "All 4 slots (Material + 3 Print) must be processed before judgment.")]
    [InlineData("ipqc.invalid_special_accept_reason", "A Special Accept reason is required (1-500 characters).")]
    [InlineData("qa.invalid_outcome",                 "QA outcome must be Approve or Reject.")]
    [InlineData("qa.invalid_qa_reason",               "A QA reason is required (1-500 characters).")]
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
        Assert.Contains("DIFFERENT from the IPQC submitter", msg);
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
    [InlineData("wo.state_conflict",                  "Another operation has already updated this WO. Reloading the latest state — try again.")]
    [InlineData("wo.if_match_required",               "Data session has not been reloaded — scan the WO again.")]
    [InlineData("wo.idempotency_key_required",        "Request is missing the idempotency key — contact IT.")]
    [InlineData("ipqc.judgment_inconsistent",         "There is an NG slot — Go Run is not allowed; choose Stop Line or Special Accept.")]
    [InlineData("ipqc.not_ready_for_judgment",        "All 4 slots (Material + 3 Print) must be processed before judgment.")]
    [InlineData("ipqc.invalid_special_accept_reason", "A Special Accept reason is required (1-500 characters).")]
    [InlineData("qa.invalid_qa_reason",               "A QA reason is required (1-500 characters).")]
    [InlineData("http.empty_body",                    "The server returned an empty response — contact IT.")]
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
        Assert.Contains("Sign out", msg);
        Assert.Contains("different QC account", msg);
    }
}
