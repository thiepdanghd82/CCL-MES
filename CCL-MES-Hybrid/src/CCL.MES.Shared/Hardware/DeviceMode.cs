namespace CCL.MES.Shared.Hardware;

/// <summary>
/// Per-device operational mode. Set on <c>/mode</c> page, persisted
/// in local <c>Preferences</c> (NOT the server — see P10.3 plan §5
/// for the local-vs-central decision). Mode is a property of the
/// installed app on this hardware, not a property of the operator.
/// </summary>
public enum DeviceMode
{
    /// <summary>
    /// Default. Full sidebar visible, all permitted routes available,
    /// no idle handling, no lock screen. Used at office desks, NPI
    /// stations, supervisor screens. Selected on first install and
    /// stays unless an admin explicitly switches to Kiosk.
    /// </summary>
    Interactive = 0,

    /// <summary>
    /// Single-purpose workstation tied to one workflow. Sidebar
    /// auto-hides after first nav; idle → lock screen (NOT full
    /// logout — see plan §4.3); requires device passcode on
    /// admin actions. Used on shop-floor stations, kiosk-style.
    /// </summary>
    Kiosk = 1,

    /// <summary>
    /// Scanner-only headless station — no UI chrome, scans posted
    /// to API via background handler. RESERVED for P10.4+. In P10.3
    /// the enum value exists for forward-compat; setting it from
    /// <c>/mode</c> is gated off until the headless host ships.
    /// </summary>
    Headless = 2,
}
