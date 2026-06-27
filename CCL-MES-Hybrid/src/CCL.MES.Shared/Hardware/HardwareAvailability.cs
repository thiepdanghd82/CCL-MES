namespace CCL.MES.Shared.Hardware;

/// <summary>
/// Result of an <c>IsAvailableAsync()</c> probe on any hardware service.
/// The caller uses <see cref="IsAvailable"/> to grey out UI buttons +
/// renders <see cref="OperatorMessage"/> directly into the banner when
/// unavailable. <see cref="Reason"/> is the machine-readable code that
/// telemetry / audit-log filters can pivot on without parsing localised
/// strings.
///
/// <para>
/// Pattern carried from P10.2: hardware MUST NOT silently no-op when a
/// device isn't present or a permission isn't granted. Every denial path
/// has an operator-actionable message. The "Mở Settings" deep-link UX
/// belongs to the caller (a page button) — this DTO only carries the
/// reason text. Keeps Shared free of MAUI / platform refs.
/// </para>
/// </summary>
/// <param name="IsAvailable">
/// True if the device can be used right now. False if any of:
/// permission denied, device disconnected, driver missing, feature
/// flag off, busy-with-another-session, OS doesn't expose the surface
/// on this platform. False ALWAYS pairs with non-null
/// <see cref="OperatorMessage"/>.
/// </param>
/// <param name="Reason">
/// Stable machine code identifying the failure class. Known values:
/// <c>"ok"</c> (paired with IsAvailable=true; also used as default),
/// <c>"feature_disabled"</c> (HardwareOptions flag off — W1 default
/// case until W4 flips it),
/// <c>"not_implemented"</c> (stub impl, no real impl shipped yet — e.g.
/// printer + scale in P10.3),
/// <c>"permission_denied"</c> (operator rejected the system camera /
/// HID prompt, or revoked it in Settings),
/// <c>"no_device"</c> (no camera attached / no HID match / no scale
/// on serial port),
/// <c>"busy"</c> (another session is using the device — modal opened
/// twice, two AVCaptureSession instances would clash),
/// <c>"platform_unsupported"</c> (e.g. USB-HID on Catalyst — the
/// inherited iOS API surface doesn't expose HID raw input).
/// New codes added without breaking existing callers since callers
/// switch on the recognised set and fall through to the operator
/// message for unknown codes.
/// </param>
/// <param name="OperatorMessage">
/// Localised Vietnamese text suitable for direct display in a banner
/// or modal — e.g. <c>"Quyền camera chưa được cấp. Vào System Settings
/// → Privacy → Camera → bật CCL MES rồi thử lại."</c>. Null only when
/// <see cref="IsAvailable"/> is true. Callers never fabricate this
/// message from <see cref="Reason"/> — the hardware impl owns the
/// wording so platform-specific phrasing (e.g. "Privacy" on macOS vs
/// "Quyền riêng tư" on iOS Vietnamese) stays close to the source.
/// </param>
public sealed record HardwareAvailability(
    bool IsAvailable,
    string Reason,
    string? OperatorMessage)
{
    public static HardwareAvailability Ok(string? sourceDevice = null) =>
        new(true, "ok", null);

    public static HardwareAvailability FeatureDisabled() =>
        new(false, "feature_disabled",
            "Hardware features are off. Contact your administrator to enable.");

    public static HardwareAvailability NotImplemented(string componentName) =>
        new(false, "not_implemented",
            $"{componentName} is not implemented yet. Coming in a later phase.");

    public static HardwareAvailability PermissionDenied(string componentName) =>
        new(false, "permission_denied",
            $"{componentName} permission not granted. Open System Settings → Privacy and try again.");

    public static HardwareAvailability NoDevice(string componentName) =>
        new(false, "no_device",
            $"No {componentName} found. Check the device connection.");

    public static HardwareAvailability Busy(string componentName) =>
        new(false, "busy",
            $"{componentName} is in use by another session. Close the current session and try again.");

    public static HardwareAvailability PlatformUnsupported(string componentName) =>
        new(false, "platform_unsupported",
            $"{componentName} is not supported on this platform.");
}
