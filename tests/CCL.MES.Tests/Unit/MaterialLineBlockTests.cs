using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// D1 (2026-09-18) — khoá LÝ DO một dòng vật tư bị chặn, không chỉ bị-hay-không.
///
/// <para><b>Bug class được chặn ở đây.</b> Trước đó <c>IsLineReady</c> trả một
/// bool, nên cổng <c>/run/start</c> gộp mọi dòng không sẵn sàng vào một câu:
/// "lô không còn được IQC thả". Đo trên live WO-TEST-02: hai dòng chặn máy là
/// <c>Status=Pending</c> — chưa ai kiểm — mà người đứng máy được bảo đi thay lô
/// và xin chấp nhận đặc biệt. Cả hai đều sai việc.</para>
///
/// <para>Test này khoá đúng chỗ đó: <c>NotChecked</c> và <c>LotNotReleased</c>
/// KHÔNG được trộn vào nhau. Ai gộp lại sẽ thấy test đỏ.</para>
/// </summary>
public sealed class MaterialLineBlockTests
{
    private static WoMaterial Line(
        PrepressCheckStatus status, string? ngReasonCode = null) =>
        new()
        {
            BomLineIdx = 1,
            MaterialCode = "30120406",
            Status = status,
            NgReasonCode = ngReasonCode,
        };

    [Theory]
    [InlineData(PrepressCheckStatus.Pending)]
    [InlineData(PrepressCheckStatus.Ng)]
    public void Chua_xac_nhan_o_prepress_thi_bao_NotChecked_khong_phai_loi_lo(
        PrepressCheckStatus status)
    {
        // Lô Released hẳn hoi — nếu vẫn bị quy là lỗi lô thì đó chính là bug cũ.
        var block = MaterialsReadinessRollup.Classify(Line(status), "Released");

        Assert.Equal(MaterialLineBlock.NotChecked, block);
        Assert.NotEqual(MaterialLineBlock.LotNotReleased, block);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Quarantine")]
    [InlineData(null)]          // chưa tra được về lô nào
    public void Da_Ok_nhung_lo_khong_Released_thi_bao_LotNotReleased(string? lotStatus)
    {
        var block = MaterialsReadinessRollup.Classify(
            Line(PrepressCheckStatus.Ok), lotStatus);

        Assert.Equal(MaterialLineBlock.LotNotReleased, block);
    }

    [Fact]
    public void Lo_Released_thi_khong_chan()
    {
        Assert.Equal(MaterialLineBlock.None,
            MaterialsReadinessRollup.Classify(Line(PrepressCheckStatus.Ok), "Released"));
    }

    [Fact]
    public void Special_accept_van_duoc_tinh_la_dat_du_lo_bi_tu_choi()
    {
        // Status=Ok + NgReasonCode khác null = dấu nhận biết Special Accept:
        // đường Ok thường LUÔN xoá NgReasonCode về null.
        var block = MaterialsReadinessRollup.Classify(
            Line(PrepressCheckStatus.Ok, ngReasonCode: "SCRAP-01"), "Rejected");

        Assert.Equal(MaterialLineBlock.None, block);
    }

    [Fact]
    public void Ban_thanh_pham_tu_lam_duoc_mien_cong_lo()
    {
        var block = MaterialsReadinessRollup.Classify(
            Line(PrepressCheckStatus.Ok), lotStatus: null, isInHouse: true);

        Assert.Equal(MaterialLineBlock.None, block);
    }

    /// <summary>
    /// Hợp đồng cũ không đổi: <c>IsLineReady</c> vẫn là "Classify == None".
    /// 5 test LegacyParity khoá bool này, nên nó phải trùng khít.
    /// </summary>
    [Theory]
    [InlineData(PrepressCheckStatus.Pending, "Released", false)]
    [InlineData(PrepressCheckStatus.Ok, "Released", true)]
    [InlineData(PrepressCheckStatus.Ok, "Rejected", false)]
    [InlineData(PrepressCheckStatus.Ng, "Released", false)]
    public void IsLineReady_van_khop_voi_Classify(
        PrepressCheckStatus status, string lotStatus, bool expectedReady)
    {
        var m = Line(status);

        Assert.Equal(expectedReady, MaterialsReadinessRollup.IsLineReady(m, lotStatus));
        Assert.Equal(expectedReady,
            MaterialsReadinessRollup.Classify(m, lotStatus) == MaterialLineBlock.None);
    }
}
