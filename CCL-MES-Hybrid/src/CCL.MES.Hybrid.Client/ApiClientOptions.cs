namespace CCL.MES.Hybrid.Client;

/// <summary>
/// Bound from configuration section <c>CclApi</c>. The MAUI shell
/// supplies these via <c>appsettings.json</c> packaged with the app —
/// operators can override the BaseUrl per-station so a single binary
/// works against multiple plants.
/// </summary>
public sealed class ApiClientOptions
{
    /// <summary>Absolute base URL of the CCL.MES.Api host, e.g.
    /// <c>http://10.0.5.12:5100</c> on the factory LAN. Must end without
    /// a trailing slash (HttpClient handles the join).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5100";

    /// <summary>Per-request timeout. Default 30 s — long enough to wait
    /// out a brief LAN hiccup without hanging the UI thread.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional device identifier sent on login. The MAUI shell
    /// fills this with a stable per-install id so audit rows can
    /// distinguish operators sharing one user account across stations.</summary>
    public string? DeviceId { get; set; }
}
