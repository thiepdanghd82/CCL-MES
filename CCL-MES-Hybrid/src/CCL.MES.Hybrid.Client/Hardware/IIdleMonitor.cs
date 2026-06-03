namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Watches for operator-idle on the device and fires
/// <see cref="IdleThresholdReached"/> after the configured number of
/// minutes (read from <see cref="IDeviceModeService.IdleMinutes"/>) of
/// no input. Used by Kiosk mode to navigate to /lock — see plan §4.3.
///
/// <para>
/// W1 ships a stub impl that never fires (timer never starts) so the
/// DI graph + page wiring is in place but no behaviour change ships
/// alongside W1's flag-OFF UI. W4 ships the real timer + lock page.
/// </para>
/// </summary>
public interface IIdleMonitor
{
    /// <summary>
    /// Called from a document-level input observer (touch / mouse /
    /// keydown) to reset the idle timer. The
    /// <c>MacCatalystKeyboardFix</c> JS already runs a document-level
    /// keydown handler — W4 hooks <c>NotifyActivity</c> into the same
    /// observer to avoid a second listener.
    /// </summary>
    void NotifyActivity();

    /// <summary>
    /// Fires once when the idle threshold elapses without a
    /// <see cref="NotifyActivity"/> call. Kiosk-mode subscriber
    /// navigates to /lock. Re-arms only after the next
    /// <see cref="NotifyActivity"/>.
    /// </summary>
    event Action? IdleThresholdReached;

    /// <summary>
    /// Starts the timer. Called when the device transitions to Kiosk
    /// mode. <see cref="Stop"/> aborts a pending fire. W1 stub no-ops
    /// both.
    /// </summary>
    void Start();

    void Stop();
}
