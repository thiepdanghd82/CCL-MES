using CCL.MES.Api.Auth;

namespace CCL.MES.Api.Tests;

/// <summary>R2 — placeholder JWT trong repo không được lọt preflight.</summary>
public sealed class JwtSigningKeyGuardTests
{
    [Theory]
    [InlineData("REPLACE-IN-PROD-Jwt__SigningKey-must-be-at-least-32-bytes-of-utf8-content")]
    [InlineData("dev-only-do-not-use-in-prod-32-bytes-min!!")]
    [InlineData("")]
    [InlineData(null)]
    public void Forbidden_dev_keys_are_detected(string? key)
        => Assert.True(JwtSigningKeyGuard.IsForbiddenDevKey(key));

    [Fact]
    public void Random_prod_key_is_allowed()
        => Assert.False(JwtSigningKeyGuard.IsForbiddenDevKey(
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))));

    [Fact]
    public void Test_environment_skips_forbidden_check()
    {
        // MesApiFactory / CI inject key riêng; Test không được chết vì
        // appsettings.json vẫn chứa placeholder khi bind trước UseSetting.
        var ex = Record.Exception(() =>
            JwtSigningKeyGuard.EnsureSafeForBoot(
                "REPLACE-IN-PROD-Jwt__SigningKey-must-be-at-least-32-bytes-of-utf8-content",
                "Test"));
        Assert.Null(ex);
    }

    [Fact]
    public void Development_rejects_placeholder()
    {
        Assert.Throws<InvalidOperationException>(() =>
            JwtSigningKeyGuard.EnsureSafeForBoot(
                "REPLACE-IN-PROD-Jwt__SigningKey-must-be-at-least-32-bytes-of-utf8-content",
                "Development"));
    }

    // ── Hồi quy sự cố 2026-09-07 ────────────────────────────────────────
    // API chết lúc boot và không ai biết cho tới khi người dùng thấy
    // "login fail". Khoá thật ĐÃ nằm ở appsettings.Development.local.json,
    // nhưng lệnh vận hành không đặt ASPNETCORE_ENVIRONMENT nên .NET chạy
    // Production và nạp một file KHÁC. Thông báo lỗi cũ lại gợi ý đúng cái
    // file đã tồn tại — người đọc tưởng mình làm đúng rồi và đi tìm chỗ khác.

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void Thong_bao_loi_phai_neu_DICH_DANH_moi_truong_dang_chay(string env)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtSigningKeyGuard.EnsureSafeForBoot(
                "REPLACE-IN-PROD-Jwt__SigningKey-must-be-at-least-32-bytes-of-utf8-content",
                env));

        // Phải nói môi trường nào, và chỉ đúng file của MÔI TRƯỜNG ĐÓ.
        Assert.Contains(env, ex.Message);
        Assert.Contains($"appsettings.{env}.local.json", ex.Message);
        // Và phải cảnh báo rằng file của môi trường khác KHÔNG được nạp —
        // đây chính là chỗ người vận hành mắc kẹt lần trước.
        Assert.Contains("KHÔNG được nạp", ex.Message);
    }

    [Fact]
    public void Khong_duoc_goi_y_file_cua_moi_truong_KHAC()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtSigningKeyGuard.EnsureSafeForBoot("REPLACE-IN-PROD-…-32-bytes-of-utf8-content-x", "Production"));

        // Thông báo cũ nêu "appsettings.Development.local.json" ngay cả khi
        // đang chạy Production. Đó là dòng đã tốn hàng giờ để lần ra.
        Assert.DoesNotContain("appsettings.Development.local.json", ex.Message);
    }
}
