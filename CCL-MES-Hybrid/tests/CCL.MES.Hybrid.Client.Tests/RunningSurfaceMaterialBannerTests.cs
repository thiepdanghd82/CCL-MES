using System.Collections.Generic;
using CCL.MES.Hybrid.Client.RunningSurface;
using CCL.MES.Shared.Envelopes;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// D1 (2026-09-18) — khoá câu tiếng Việt của HAI mã lỗi vật tư ở cổng
/// <c>/run/start</c>.
///
/// <para><b>Vì sao cần test riêng.</b> Câu cũ gộp hai nguyên nhân và bỏ mất dữ
/// kiện: người đứng máy thấy "Có lô vật tư không còn được IQC thả" mà không
/// biết dòng nào, mã nào, và trong thực tế lỗi lại là "chưa ai kiểm dòng đó".
/// Hai thứ phải đúng cùng lúc: ĐÚNG VIỆC (thay lô ≠ xác nhận dòng) và ĐỦ DỮ
/// KIỆN (dòng + mã). Test khoá cả hai.</para>
/// </summary>
public sealed class RunningSurfaceMaterialBannerTests
{
    private static ApiError Err(string code, params (string K, string V)[] details)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in details) d[k] = v;
        return new ApiError { Code = code, MessageEn = "ignored english", Details = d };
    }

    [Fact]
    public void Dong_chua_kiem_thi_bao_di_XAC_NHAN_khong_bao_di_thay_lo()
    {
        var msg = RunningSurfaceErrorLocaliser.LocaliseApiError(422, Err(
            "run.material_line_unchecked",
            ("bom_line_idx", "1"), ("material_code", "30120406"), ("more_count", "0")));

        Assert.Contains("dòng 1", msg);
        Assert.Contains("30120406", msg);
        Assert.Contains("chưa được xác nhận", msg);
        // Chỉ sai việc là bug gốc — câu này KHÔNG được bảo đi thay lô.
        Assert.DoesNotContain("thay lô", msg);
    }

    [Fact]
    public void Lo_khong_Released_thi_van_neu_hai_duong_ra_va_noi_ro_lo_nao()
    {
        var msg = RunningSurfaceErrorLocaliser.LocaliseApiError(422, Err(
            "run.material_lot_unusable",
            ("bom_line_idx", "2"), ("material_code", "30032127"),
            ("lot_no", "1398244"), ("lot_status", "Rejected"), ("more_count", "0")));

        Assert.Contains("dòng 2", msg);
        Assert.Contains("30032127", msg);
        Assert.Contains("1398244", msg);
        Assert.Contains("thay lô", msg);
        Assert.Contains("chấp nhận đặc biệt", msg);
    }

    [Fact]
    public void Nhieu_dong_hong_thi_noi_con_bao_nhieu_dong_nua()
    {
        var msg = RunningSurfaceErrorLocaliser.LocaliseApiError(422, Err(
            "run.material_line_unchecked",
            ("bom_line_idx", "1"), ("material_code", "30120406"), ("more_count", "2")));

        Assert.Contains("và 2 dòng nữa", msg);
    }

    [Fact]
    public void Mot_dong_duy_nhat_thi_KHONG_bia_them_dong_nua()
    {
        var msg = RunningSurfaceErrorLocaliser.LocaliseApiError(422, Err(
            "run.material_lot_unusable",
            ("bom_line_idx", "0"), ("material_code", "30032128"), ("more_count", "0")));

        Assert.DoesNotContain("dòng nữa", msg);
    }

    /// <summary>
    /// Server cũ (hoặc lỗi dựng từ nơi khác) không gửi Details — câu vẫn phải
    /// đọc được, không được ra "dòng  · " hay ném NullReference.
    /// </summary>
    [Theory]
    [InlineData("run.material_line_unchecked")]
    [InlineData("run.material_lot_unusable")]
    public void Thieu_Details_thi_van_ra_cau_hoan_chinh(string code)
    {
        var msg = RunningSurfaceErrorLocaliser.LocaliseApiError(
            422, new ApiError { Code = code, MessageEn = "ignored" });

        Assert.DoesNotContain("HTTP 422", msg);   // không rơi xuống nhánh fallback
        Assert.DoesNotContain("dòng  ", msg);
        Assert.EndsWith("chạy máy.", msg);
    }
}
