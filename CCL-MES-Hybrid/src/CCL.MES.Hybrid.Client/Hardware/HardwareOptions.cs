namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Feature-flag block bound from configuration section <c>Hardware</c>.
/// Read into the host process at boot via
/// <c>configuration.GetSection("Hardware")</c> + <see cref="HardwareOptions"/>.
///
/// <para>
/// W1 default: <see cref="ScanEnabled"/> is <c>false</c>. The /hardware
/// + /mode routes still mount (so deep-links don't 404) but render
/// "Tính năng phần cứng đang tắt" gates instead of the real scanner
/// UI. NavMenu hides the Settings group entirely when the flag is off
/// — operators don't see a button they can't use.
/// </para>
/// <para>
/// W4 flips <see cref="ScanEnabled"/> to <c>true</c> in
/// <c>appsettings.json</c>. The same JSON path supports an env-var
/// override for station-specific tweaks (e.g. one shop-floor station
/// gets the flag on early for pilot). Pattern matches the
/// <c>CclApi:BaseUrl</c> overrideability shipped in P10.2.
/// </para>
/// </summary>
public sealed class HardwareOptions
{
    /// <summary>
    /// Master switch for the P10.3 hardware surface. When false,
    /// /hardware + /mode show the gate page and NavMenu hides the
    /// Settings group. When true, /hardware enumerates the
    /// stubbed/real services and /mode exposes the kiosk config.
    /// Default <c>false</c>.
    /// </summary>
    public bool ScanEnabled { get; set; } = false;
}
