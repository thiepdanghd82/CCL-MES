using CCL.MES.Api.Policies;
using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Luật chuỗi 3 chữ ký OQC — luật an toàn chất lượng nghiêm ngặt nhất của hệ:
/// nó tồn tại để MỘT người không thể tự mình đẩy một lô hàng ra khỏi nhà máy.
///
/// <para>Trước khi tách ra khỏi <c>WoQcReviewController</c>, muốn kiểm luật này
/// phải dựng <c>WebApplicationFactory</c> + DB + auth cho từng tổ hợp — đắt tới
/// mức các nhánh biên (cờ tắt, chữ ký thiếu, khác kiểu chữ) không được phủ.
/// Ở dạng hàm thuần, toàn bộ ma trận chạy trong vài mili-giây.</para>
/// </summary>
public sealed class OqcSignaturePolicyTests
{
    private static WoQcSigPolicyOptions AllOn() => new();   // mặc định 3 cờ đều bật (L20)
    private static WoQcSigPolicyOptions AllOff() => new()
    {
        OqcRequireDistinctReviewer = false,
        OqcRequireDistinctApprover = false,
        OqcRequireApproverDistinctFromInspector = false,
    };

    // ── THỨ TỰ ───────────────────────────────────────────────────────────

    [Fact]
    public void Reviewer_cannot_sign_before_inspector()
    {
        var v = OqcSignaturePolicy.CheckOrder(OqcSignatureStep.Review, inspectedBy: null, reviewedBy: null);
        Assert.False(v.Allowed);
        Assert.Equal(OqcSignaturePolicy.OutOfOrder, v.ErrorCode);
        Assert.Contains("Inspector must sign before Reviewer", v.Message);
        Assert.Null(v.DenyReason);   // sai thứ tự ≠ vi phạm tách vai → KHÔNG emit *_DENIED
    }

    [Fact]
    public void Approver_cannot_sign_before_inspector()
    {
        var v = OqcSignaturePolicy.CheckOrder(OqcSignatureStep.Approve, null, "reviewer");
        Assert.False(v.Allowed);
        Assert.Contains("Inspector must sign before Approver", v.Message);
    }

    [Fact]
    public void Approver_cannot_sign_before_reviewer()
    {
        var v = OqcSignaturePolicy.CheckOrder(OqcSignatureStep.Approve, "inspector", reviewedBy: null);
        Assert.False(v.Allowed);
        Assert.Contains("Reviewer must sign before Approver", v.Message);
    }

    [Fact]
    public void Order_is_satisfied_once_the_predecessors_signed()
    {
        Assert.True(OqcSignaturePolicy.CheckOrder(OqcSignatureStep.Review, "insp", null).Allowed);
        Assert.True(OqcSignaturePolicy.CheckOrder(OqcSignatureStep.Approve, "insp", "rev").Allowed);
    }

    // ── TÁCH VAI ─────────────────────────────────────────────────────────

    [Fact]
    public void Inspector_cannot_also_review()
    {
        var v = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Review, "qc01", null, actor: "qc01", AllOn());
        Assert.False(v.Allowed);
        Assert.Equal(OqcSignaturePolicy.SameAsInspector, v.ErrorCode);
        Assert.Equal("same_user_as_inspector", v.DenyReason);   // ⇒ controller emit *_DENIED
    }

    [Fact]
    public void Reviewer_cannot_also_approve()
    {
        var v = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Approve, "insp", "qc02", actor: "qc02", AllOn());
        Assert.False(v.Allowed);
        Assert.Equal(OqcSignaturePolicy.SameAsReviewer, v.ErrorCode);
        Assert.Equal("same_user_as_reviewer", v.DenyReason);
    }

    [Fact]
    public void Inspector_cannot_also_approve()
    {
        var v = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Approve, "qc01", "rev", actor: "qc01", AllOn());
        Assert.False(v.Allowed);
        Assert.Equal(OqcSignaturePolicy.SameAsInspector, v.ErrorCode);
    }

    [Fact]
    public void One_person_can_never_carry_the_whole_chain_with_default_flags()
    {
        // Đây là câu hỏi thật sự cần trả lời: một người có tự đẩy lô hàng ra
        // khỏi nhà máy được không? Với cấu hình mặc định — không, chặn ở bước 2.
        var opt = AllOn();
        Assert.False(OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Review, "solo", null, "solo", opt).Allowed);
        Assert.False(OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Approve, "solo", "solo", "solo", opt).Allowed);
    }

    [Theory]
    [InlineData("QC01", "qc01")]
    [InlineData("qc01", "QC01")]
    [InlineData("Qc01", "qC01")]
    public void Same_person_typing_a_different_case_is_still_the_same_person(string signed, string actor)
    {
        // Nếu so khớp phân biệt hoa thường thì luật tách vai vượt qua được chỉ
        // bằng cách gõ khác kiểu chữ — đúng loại lỗ hổng không ai nghĩ tới.
        var v = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Review, signed, null, actor, AllOn());
        Assert.False(v.Allowed);
    }

    [Fact]
    public void Distinct_people_pass_every_step()
    {
        var opt = AllOn();
        Assert.True(OqcSignaturePolicy.CheckDistinct(OqcSignatureStep.Review, "a", null, "b", opt).Allowed);
        Assert.True(OqcSignaturePolicy.CheckDistinct(OqcSignatureStep.Approve, "a", "b", "c", opt).Allowed);
    }

    // ── CỜ TẮT ───────────────────────────────────────────────────────────

    [Fact]
    public void Flags_off_allow_the_same_person_through_the_whole_chain()
    {
        // Nhà máy nhỏ có thể tắt cờ. Test này khoá HÀNH VI của cấu hình đó —
        // để nếu ai đổi mặc định thành "luôn chặn" thì thấy ngay là đang đổi
        // hợp đồng cấu hình, không phải sửa bug.
        var opt = AllOff();
        Assert.True(OqcSignaturePolicy.CheckDistinct(OqcSignatureStep.Review, "solo", null, "solo", opt).Allowed);
        Assert.True(OqcSignaturePolicy.CheckDistinct(OqcSignatureStep.Approve, "solo", "solo", "solo", opt).Allowed);
    }

    [Fact]
    public void Each_flag_gates_only_its_own_invariant()
    {
        var onlyReviewer = new WoQcSigPolicyOptions
        {
            OqcRequireDistinctReviewer = true,
            OqcRequireDistinctApprover = false,
            OqcRequireApproverDistinctFromInspector = false,
        };
        Assert.False(OqcSignaturePolicy.CheckDistinct(OqcSignatureStep.Review, "x", null, "x", onlyReviewer).Allowed);
        Assert.True(OqcSignaturePolicy.CheckDistinct(OqcSignatureStep.Approve, "x", "x", "x", onlyReviewer).Allowed);
    }

    [Fact]
    public void Approver_checks_reviewer_before_inspector_so_the_error_code_is_stable()
    {
        // Khi actor trùng CẢ reviewer lẫn inspector, mã lỗi phải là
        // same_user_as_reviewer — giữ đúng thứ tự kiểm của bản trong controller,
        // nếu không client đang bắt mã cũ sẽ hiển thị sai thông báo.
        var v = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Approve, "same", "same", "same", AllOn());
        Assert.Equal(OqcSignaturePolicy.SameAsReviewer, v.ErrorCode);
    }

    [Fact]
    public void Empty_signature_never_counts_as_a_match()
    {
        // Chuỗi rỗng/null không được coi là "trùng người" — nếu không, actor có
        // tên rỗng sẽ bị chặn nhầm ở bước chưa ai ký.
        Assert.True(OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Review, inspectedBy: null, null, actor: "", AllOn()).Allowed);
        Assert.True(OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Review, inspectedBy: "", null, actor: "", AllOn()).Allowed);
    }
}
