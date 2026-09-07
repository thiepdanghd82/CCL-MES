namespace CCL.MES.Api.Auth;

/// <summary>
/// Preflight cho <see cref="JwtOptions.SigningKey"/>. Độ dài ≥32 byte
/// không đủ — placeholder trong appsettings.json dài 73 byte và từng lọt
/// cửa (kiểm định 2026-09-07 R2). Ai đọc repo tự ký được token Admin.
/// </summary>
public static class JwtSigningKeyGuard
{
    /// <summary>Các mẫu khoá đã lộ trong git / sentinel class default.
    /// Khớp → cấm boot (trừ môi trường Test).</summary>
    public static bool IsForbiddenDevKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;
        if (key.Contains("REPLACE-IN-PROD", StringComparison.OrdinalIgnoreCase))
            return true;
        if (key.StartsWith("dev-only-do-not-use", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static void EnsureSafeForBoot(string? key, string environmentName)
    {
        if (string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase))
            return;

        var bytes = System.Text.Encoding.UTF8.GetByteCount(key ?? "");
        if (bytes < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes of UTF-8 for HS256. "
                + "Set Jwt__SigningKey in env or appsettings.Development.local.json.");
        }

        if (IsForbiddenDevKey(key))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is a committed placeholder / known-dev sentinel "
                + "(REPLACE-IN-PROD or dev-only-…). Refusing to boot — anyone with "
                + "the repo could mint Admin tokens. Set a private key via "
                + "Jwt__SigningKey or appsettings.Development.local.json "
                + "(gitignored) / appsettings.Production.json.");
        }
    }
}
