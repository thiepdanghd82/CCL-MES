using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// D3 (Henry chốt 2026-09-25, contract §5.8) — duyệt sai lệch 4 mắt của IPQC
/// được ghi xuống <see cref="WoMaterial"/> và thoả cổng lô của
/// <c>/run/start</c>, NHƯNG chỉ cho đúng lô + đúng trạng thái lô đã ký.
///
/// <para>Ba lỗ phải bịt: (1) chữ ký không được đi theo sang lô khác; (2) IQC
/// đổi trạng thái lô sau khi ký thì chữ ký không còn nói về lô này nữa;
/// (3) duyệt sai lệch lô không che được việc Pre-press chưa xác nhận dòng.</para>
/// </summary>
public sealed class IpqcWaiverGateTests
{
    private static WoMaterial Waived(
        string lotNo = "LOT-A", string? waivedStatus = "Quarantine",
        PrepressCheckStatus status = PrepressCheckStatus.Ok) =>
        new()
        {
            BomLineIdx = 1,
            MaterialCode = "30120406",
            Status = status,
            LotNo = lotNo,
            IpqcWaiverBy = "eng-a",
            IpqcWaiverAt = new DateTime(2026, 9, 25, 3, 0, 0, DateTimeKind.Utc),
            IpqcWaiverReason = "Lô thay thế đã kiểm",
            IpqcWaiverLotNo = "LOT-A",
            IpqcWaiverLotStatus = waivedStatus,
        };

    [Fact]
    public void Dung_lo_dung_trang_thai_da_ky_thi_khong_chan()
        => Assert.Equal(MaterialLineBlock.None,
            MaterialsReadinessRollup.Classify(Waived(), "Quarantine"));

    [Fact]
    public void Lo_chua_dang_ky_luc_ky_va_van_chua_dang_ky_thi_khong_chan()
        => Assert.Equal(MaterialLineBlock.None,
            MaterialsReadinessRollup.Classify(Waived(waivedStatus: null), null));

    [Fact]
    public void Doi_sang_lo_khac_thi_chu_ky_het_hieu_luc()
        => Assert.Equal(MaterialLineBlock.LotNotReleased,
            MaterialsReadinessRollup.Classify(Waived(lotNo: "LOT-B"), "Quarantine"));

    [Fact]
    public void IQC_doi_trang_thai_lo_sau_khi_ky_thi_chan_lai()
        => Assert.Equal(MaterialLineBlock.LotNotReleased,
            MaterialsReadinessRollup.Classify(Waived(), "Rejected"));

    [Fact]
    public void Lo_duoc_tha_sau_khi_ky_thi_van_qua_theo_duong_Released()
        => Assert.Equal(MaterialLineBlock.None,
            MaterialsReadinessRollup.Classify(Waived(), "Released"));

    [Theory]
    [InlineData(PrepressCheckStatus.Pending)]
    [InlineData(PrepressCheckStatus.Ng)]
    public void Duyet_sai_lech_khong_che_duoc_dong_chua_xac_nhan_o_Prepress(PrepressCheckStatus status)
        => Assert.Equal(MaterialLineBlock.NotChecked,
            MaterialsReadinessRollup.Classify(Waived(status: status), "Quarantine"));

    [Fact]
    public void Khong_co_dau_duyet_thi_hanh_vi_cu_giu_nguyen()
    {
        var m = Waived();
        m.IpqcWaiverAt = null;
        Assert.Equal(MaterialLineBlock.LotNotReleased,
            MaterialsReadinessRollup.Classify(m, "Quarantine"));
    }
}
