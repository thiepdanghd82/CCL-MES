using CCL.MES.Api.Auth;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Hãm thử-sai cho ô ký điện tử. Skill <c>cmes-secrets-jwt</c> ghi thẳng: đừng
/// thêm endpoint kiểu login mà không có delay/lockout — brute-force HTTP trên
/// LAN là kịch bản thật, và ở đây động cơ có sẵn: ký duyệt chính lô hàng mình
/// vừa làm hỏng.
///
/// <para>Đồng hồ tiêm được nên khoá/mở khoá kiểm được ngay, không phải chờ 15
/// phút thật.</para>
/// </summary>
public class ReauthThrottleTests
{
    private DateTimeOffset _now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);
    private ReauthThrottle New() => new(() => _now);

    [Fact]
    public void Chua_sai_lan_nao_thi_khong_khoa()
        => Assert.Null(New().LockedFor("qc1"));

    [Fact]
    public void Sai_duoi_nguong_thi_van_cho_thu()
    {
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures - 1; i++)
            Assert.False(t.RegisterFailure("qc1"));
        Assert.Null(t.LockedFor("qc1"));
    }

    [Fact]
    public void Du_so_lan_sai_thi_KHOA()
    {
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures - 1; i++) t.RegisterFailure("qc1");

        Assert.True(t.RegisterFailure("qc1"));          // lần cuối làm khoá
        var left = t.LockedFor("qc1");
        Assert.NotNull(left);
        Assert.True(left!.Value <= ReauthThrottle.LockFor);
    }

    [Fact]
    public void Het_han_khoa_thi_TU_MO()
    {
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures; i++) t.RegisterFailure("qc1");
        Assert.NotNull(t.LockedFor("qc1"));

        _now = _now.Add(ReauthThrottle.LockFor).AddSeconds(1);
        Assert.Null(t.LockedFor("qc1"));                // ← đỏ nếu khoá vĩnh viễn
    }

    [Fact]
    public void Sai_RAI_RAC_ngoai_cua_so_thi_KHONG_cong_don()
    {
        // Gõ nhầm một lần lúc 8h và một lần lúc 11h không được cộng thành
        // chuỗi. Nếu cộng dồn thì người dùng thật bị khoá vì những lần nhầm
        // cách nhau hàng giờ, còn kẻ dò thì vẫn đủ nhanh trong cửa sổ.
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures - 1; i++) t.RegisterFailure("qc1");

        _now = _now.Add(ReauthThrottle.Window).AddMinutes(1);
        Assert.False(t.RegisterFailure("qc1"));         // bắt đầu chuỗi mới
        Assert.Null(t.LockedFor("qc1"));
    }

    [Fact]
    public void Ky_DUNG_thi_xoa_sach_lich_su_sai()
    {
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures - 1; i++) t.RegisterFailure("qc1");

        t.RegisterSuccess("qc1");

        // Sau khi ký đúng, bộ đếm về 0 — lần sai kế tiếp không được làm khoá ngay.
        Assert.False(t.RegisterFailure("qc1"));
        Assert.Null(t.LockedFor("qc1"));
    }

    [Fact]
    public void Khoa_theo_TAI_KHOAN_chu_khong_lan_sang_nguoi_khac()
    {
        // Cả xưởng đi chung một máy và một đường mạng. Khoá theo IP là khoá cả
        // xưởng khi một người gõ sai.
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures; i++) t.RegisterFailure("qc1");

        Assert.NotNull(t.LockedFor("qc1"));
        Assert.Null(t.LockedFor("qc2"));                // ← đỏ nếu khoá toàn cục
    }

    [Fact]
    public void Khong_phan_biet_hoa_thuong()
    {
        var t = New();
        for (var i = 0; i < ReauthThrottle.MaxFailures; i++) t.RegisterFailure("QC1");
        Assert.NotNull(t.LockedFor("qc1"));             // né khoá bằng cách đổi chữ hoa
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ten_rong_thi_khong_no_va_khong_khoa_bua(string? user)
    {
        var t = New();
        Assert.False(t.RegisterFailure(user));
        Assert.Null(t.LockedFor(user));
        t.RegisterSuccess(user);                        // không được ném
    }
}
