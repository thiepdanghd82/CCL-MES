namespace CCL.MES.Hybrid.Client.Qms;

/// <summary>
/// W5 showcard-migration — a lightweight client-side pub/sub so an IQC ticket
/// saved inside a WindowManager-hosted inspection window can tell the (possibly
/// mounted) IqcModule page to refresh its KPI + Data list.
/// </summary>
/// <remarks>
/// Before the migration the inspection form was a child of IqcModule, so a save
/// bubbled up via an <c>EventCallback</c>. Now the form is hosted by the WM
/// (MainLayout owns the window layer), so that direct parent link is gone. This
/// notifier restores it with the smallest possible surface: raise
/// <see cref="Changed"/> on save; IqcModule subscribes while mounted and
/// unsubscribes on dispose. When no page is subscribed the notify is a harmless
/// no-op (the next IqcModule mount loads fresh data anyway). Registered as a
/// singleton (survives page navigation within the session), mirroring how
/// <see cref="Windows.IFloatingWindowStore"/> is wired.
/// </remarks>
public interface IIqcChangeNotifier
{
    /// <summary>Raised after an IQC ticket is created/saved in any window.</summary>
    event Action? Changed;

    /// <summary>Signal that IQC data changed (a ticket was created/saved).</summary>
    void NotifyChanged();
}

/// <inheritdoc />
public sealed class IqcChangeNotifier : IIqcChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
