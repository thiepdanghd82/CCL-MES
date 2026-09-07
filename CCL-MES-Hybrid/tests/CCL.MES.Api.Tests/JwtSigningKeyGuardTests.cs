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
}
