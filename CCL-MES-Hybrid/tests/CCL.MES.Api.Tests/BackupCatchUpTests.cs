using CCL.MES.Api.Services;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Chạy bù backup khi cửa sổ hằng ngày bị lỡ.
///
/// <para>Sự cố 2026-09-08: máy chạy hệ này là MacBook và nó NGỦ qua đêm.
/// Scheduler dùng <c>Task.Delay</c> nên cửa sổ 02:00 trôi mất im lặng —
/// snapshot 07/09 có 6 file, 08/09 có <b>0</b>, <c>pmset</c> xác nhận máy ngủ
/// xuyên 02:00. Gate chỉ đo tuổi &lt; 48h nên vẫn báo PASS.</para>
///
/// <para>Không sửa được bằng launchd <c>StartCalendarInterval</c>: TCC của
/// macOS chặn job launchd đọc <c>~/Documents</c> (đã thử, exit 126
/// "Operation not permitted"), nên logic chạy bù phải nằm trong tiến trình API.</para>
/// </summary>
public sealed class BackupCatchUpTests
{
    private static readonly TimeSpan Ict = TimeSpan.FromHours(7);
    private static DateTimeOffset At(int y, int m, int d, int h, int min = 0)
        => new(y, m, d, h, min, 0, Ict);

    [Fact]
    public void Chua_toi_gio_hen_thi_KHONG_chay_bu()
    {
        // 01:00 < 02:00 — chưa tới lượt, dù cả tuần chưa có bản nào.
        Assert.False(BackupSchedulerService.IsWindowMissed(
            At(2026, 9, 8, 1), hour: 2, Array.Empty<DateTimeOffset>()));
    }

    [Fact]
    public void Qua_gio_hen_ma_chua_co_ban_nao_hom_nay_thi_CHAY_BU()
    {
        // Đúng kịch bản sáng 08/09: bản gần nhất là hôm qua, máy vừa ngủ dậy.
        Assert.True(BackupSchedulerService.IsWindowMissed(
            At(2026, 9, 8, 7, 46), hour: 2,
            new[] { At(2026, 9, 7, 2, 12) }));
    }

    [Fact]
    public void Hom_nay_da_co_ban_thi_KHONG_chup_thua()
    {
        Assert.False(BackupSchedulerService.IsWindowMissed(
            At(2026, 9, 8, 23), hour: 2,
            new[] { At(2026, 9, 7, 2, 12), At(2026, 9, 8, 2, 3) }));
    }

    [Fact]
    public void Ban_chup_luc_02h00_ICT_van_tinh_la_CUA_HOM_NAY()
    {
        // ĐÂY LÀ CÁI BẪY. Tên file đóng dấu bằng DateTime.UtcNow, nên bản chụp
        // lúc 02:00 ICT ngày 09/09 mang tên "...snapshot-20260908-190000".
        // So theo NGÀY TRONG TÊN sẽ kết luận "hôm nay chưa có" và chụp thừa một
        // bản MỖI NGÀY, âm thầm. Hàm này chỉ so mốc thời gian đã quy về ICT.
        var chupLuc2hIct = At(2026, 9, 9, 2, 0);
        Assert.Equal(20260908, int.Parse(
            chupLuc2hIct.UtcDateTime.ToString("yyyyMMdd")));   // tên file mang ngày 08
        Assert.Equal(9, chupLuc2hIct.Day);                     // nhưng ICT là ngày 09

        Assert.False(BackupSchedulerService.IsWindowMissed(
            At(2026, 9, 9, 2, 5), hour: 2, new[] { chupLuc2hIct }));
    }

    [Fact]
    public void Ban_chup_cua_ngay_khac_KHONG_tinh_la_hom_nay()
    {
        Assert.True(BackupSchedulerService.IsWindowMissed(
            At(2026, 9, 8, 2, 30), hour: 2,
            new[] { At(2026, 9, 7, 23, 59), At(2026, 9, 9, 0, 1) }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(23)]
    public void Dung_gio_hen_thi_da_tinh_la_toi_luot(int hour)
    {
        Assert.True(BackupSchedulerService.IsWindowMissed(
            At(2026, 9, 8, hour), hour, Array.Empty<DateTimeOffset>()));
    }
}
