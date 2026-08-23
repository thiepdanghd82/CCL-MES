namespace CCL.MES.Api.Services;

/// <summary>
/// A2 — mã hoá ETag ↔ RowVersion dùng chung. Trước đây <c>Base64</c> + strip
/// <c>W/</c>/dấu nháy (RFC 7232) bị sao chép ở nhiều nơi (executor, RoutingController,
/// WoMutationControllerBase). Gom một chỗ: một nguồn sự thật, một lần sửa.
/// </summary>
public static class EtagCodec
{
    /// <summary>RowVersion (byte[]) → base64; rỗng/null → "".</summary>
    public static string Base64(byte[]? rowVersion)
        => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : "";

    /// <summary>Bóc lớp bọc HTTP ETag: prefix weak <c>W/</c> + dấu nháy (RFC 7232
    /// cho phép client bỏ nháy) → base64 RowVersion thô để so sánh Ordinal.</summary>
    public static string Normalize(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s;
    }
}
