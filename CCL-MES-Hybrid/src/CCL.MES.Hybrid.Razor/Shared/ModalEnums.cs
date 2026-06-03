namespace CCL.MES.Hybrid.Razor.Shared;

/// <summary>
/// P10.5a — size variants exposed on the clean-room <see cref="Modal"/>
/// primitive. Maps to CSS classes <c>modal-sm / modal-md / modal-lg /
/// modal-xl</c> which set <c>max-width</c> + horizontal padding.
/// </summary>
public enum ModalSize
{
    Sm,
    Md,
    Lg,
    Xl,
}

/// <summary>
/// Header tone variants for the <see cref="Modal"/>. Operators read
/// severity at a glance from the header band tint — danger for
/// destructive confirms (purge, trash), warning for state changes that
/// can ripple (supersede), success for transient celebrations, info
/// for everything else.
/// </summary>
public enum ModalSeverity
{
    Info,
    Success,
    Warning,
    Danger,
}
