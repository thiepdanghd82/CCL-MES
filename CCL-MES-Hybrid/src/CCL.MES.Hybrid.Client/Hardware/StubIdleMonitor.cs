namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// W1 stub for <see cref="IIdleMonitor"/>. Timer never starts so
/// <see cref="IdleThresholdReached"/> never fires. Wired into DI so
/// Kiosk-mode subscribers compile + run with no behaviour change in
/// W1; W4 host registration overrides with a real
/// <c>MauiIdleMonitor</c> that listens for OS-level last-input-time
/// (UIApplication on Catalyst, GetLastInputInfo on Windows).
/// </summary>
public sealed class StubIdleMonitor : IIdleMonitor
{
    public event Action? IdleThresholdReached;

    public void NotifyActivity()
    {
        // Stubbed — no timer running.
    }

    public void Start()
    {
        // No-op.
    }

    public void Stop()
    {
        // No-op.
    }

    // Suppress unused-event warning — real impl will raise this.
    internal void Raise() => IdleThresholdReached?.Invoke();
}
