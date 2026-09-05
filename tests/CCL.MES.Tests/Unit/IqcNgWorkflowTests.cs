using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P13 bước 5 — vòng đời claim NCC. Năm trạng thái ĐO ĐƯỢC từ 169 dòng thật của
/// sheet <c>NG Material</c> 2026, không phải bịa: chưa claim 23 · đã báo chưa
/// hồi 8 · chờ bù 6 · đã xử lý 123 · khép không đòi được 2.
/// </summary>
public sealed class IqcNgWorkflowTests
{
    [Theory]
    [InlineData(IqcNgStatus.Open, IqcNgStatus.Claimed)]
    [InlineData(IqcNgStatus.Open, IqcNgStatus.ClosedNoClaim)]
    [InlineData(IqcNgStatus.Claimed, IqcNgStatus.SupplierConfirmed)]
    [InlineData(IqcNgStatus.Claimed, IqcNgStatus.Settled)]
    [InlineData(IqcNgStatus.SupplierConfirmed, IqcNgStatus.Settled)]
    public void Duong_di_hop_le(IqcNgStatus from, IqcNgStatus to)
        => Assert.True(IqcNgWorkflow.CanTransition(from, to));

    [Fact]
    public void NCC_bu_thang_KHONG_can_qua_buoc_xac_nhan()
    {
        // 84/169 vụ đi thẳng Claimed -> Settled. Ép qua SupplierConfirmed là
        // bắt QC bấm một nút mô tả việc không xảy ra.
        Assert.True(IqcNgWorkflow.CanTransition(IqcNgStatus.Claimed, IqcNgStatus.Settled));
    }

    [Theory]
    [InlineData(IqcNgStatus.Open, IqcNgStatus.Settled)]
    [InlineData(IqcNgStatus.Open, IqcNgStatus.SupplierConfirmed)]
    [InlineData(IqcNgStatus.Settled, IqcNgStatus.Claimed)]
    [InlineData(IqcNgStatus.ClosedNoClaim, IqcNgStatus.Claimed)]
    [InlineData(IqcNgStatus.Settled, IqcNgStatus.ClosedNoClaim)]
    public void Duong_di_KHONG_hop_le(IqcNgStatus from, IqcNgStatus to)
        => Assert.False(IqcNgWorkflow.CanTransition(from, to));

    [Fact]
    public void Chuyen_sang_CHINH_no_khong_phai_mot_buoc()
    {
        foreach (IqcNgStatus s in Enum.GetValues<IqcNgStatus>())
            Assert.False(IqcNgWorkflow.CanTransition(s, s));
    }

    [Fact]
    public void Hai_trang_thai_cuoi_la_DIEM_DUNG()
    {
        Assert.True(IqcNgWorkflow.IsTerminal(IqcNgStatus.Settled));
        Assert.True(IqcNgWorkflow.IsTerminal(IqcNgStatus.ClosedNoClaim));
        Assert.False(IqcNgWorkflow.IsTerminal(IqcNgStatus.Open));
        foreach (IqcNgStatus to in Enum.GetValues<IqcNgStatus>())
        {
            Assert.False(IqcNgWorkflow.CanTransition(IqcNgStatus.Settled, to));
            Assert.False(IqcNgWorkflow.CanTransition(IqcNgStatus.ClosedNoClaim, to));
        }
    }

    // ── khép vụ ──────────────────────────────────────────────────────────

    [Fact]
    public void Khep_vu_phai_noi_RO_hinh_thuc_den_bu()
    {
        // "Đã xử lý" mà không biết bù hàng hay trừ tiền thì kế toán không đối
        // chiếu được.
        var r = new IqcNgRecord { ClaimedAt = new DateTime(2026, 3, 1) };
        Assert.Equal("iqc.ng.settlement_required",
            IqcNgWorkflow.ValidateSettle(r, IqcClaimSettlement.None));
        Assert.Null(IqcNgWorkflow.ValidateSettle(r, IqcClaimSettlement.Replacement));
    }

    [Fact]
    public void KHONG_khep_duoc_mot_vu_chua_tung_gui_claim()
    {
        // Đo được: 0/169 vụ đã xử lý xong mà thiếu ngày claim. Khép một vụ chưa
        // ai báo NCC nghĩa là ghi rằng họ đã đền cho việc họ chưa biết.
        var r = new IqcNgRecord { ClaimedAt = null };
        Assert.Equal("iqc.ng.claim_required_before_settle",
            IqcNgWorkflow.ValidateSettle(r, IqcClaimSettlement.Replacement));
    }

    // ── bản ghi mới ──────────────────────────────────────────────────────

    private static IqcNgRecord Good() => new()
    {
        DetectedAt = new DateTime(2026, 3, 1),
        DefectName = "Xước",
        NgAreaM2 = 12.5,
        PartNo = "30030146",
    };

    [Fact]
    public void Ban_ghi_moi_du_thong_tin_thi_qua()
        => Assert.Null(IqcNgWorkflow.ValidateNew(Good()));

    [Fact]
    public void Thieu_ngay_phat_hien_bi_tu_choi()
    {
        var r = Good(); r.DetectedAt = default;
        Assert.Equal("iqc.ng.detected_at_required", IqcNgWorkflow.ValidateNew(r));
    }

    [Fact]
    public void Thieu_ten_loi_bi_tu_choi_tru_khi_da_co_ma_loi()
    {
        var r = Good(); r.DefectName = null;
        Assert.Equal("iqc.ng.defect_required", IqcNgWorkflow.ValidateNew(r));
        r.DefectCode = "NG-XUOC";
        Assert.Null(IqcNgWorkflow.ValidateNew(r));
    }

    [Fact]
    public void Phai_co_IT_NHAT_MOT_don_vi_so_luong()
    {
        // Kho đếm cuộn, NCC tính m², sản xuất tính mét. Ép cả ba là bắt người
        // ghi bịa hai con số họ không đo.
        var r = Good(); r.NgAreaM2 = null;
        Assert.Equal("iqc.ng.quantity_required", IqcNgWorkflow.ValidateNew(r));

        r.NgRolls = 2;   Assert.Null(IqcNgWorkflow.ValidateNew(r));
        r.NgRolls = null; r.NgQty = 50; Assert.Null(IqcNgWorkflow.ValidateNew(r));
    }

    [Fact]
    public void So_luong_khong_duoc_bang_0_hay_am()
    {
        var r = Good(); r.NgAreaM2 = 0;
        Assert.Equal("iqc.ng.quantity_must_be_positive", IqcNgWorkflow.ValidateNew(r));
        r.NgAreaM2 = 12.5; r.NgRolls = -1;
        Assert.Equal("iqc.ng.quantity_must_be_positive", IqcNgWorkflow.ValidateNew(r));
    }

    [Fact]
    public void Phai_noi_duoc_la_vat_lieu_NAO()
    {
        // Một vụ NG không nói được của vật liệu nào thì không đòi ai được, và
        // cũng không vào được báo cáo theo NCC.
        var r = Good(); r.PartNo = null;
        Assert.Equal("iqc.ng.material_required", IqcNgWorkflow.ValidateNew(r));

        // Nối bằng phiếu IQC cũng đủ — vụ phát hiện lúc kiểm nhập.
        r.IqcInspectionId = 7; Assert.Null(IqcNgWorkflow.ValidateNew(r));
    }
}
