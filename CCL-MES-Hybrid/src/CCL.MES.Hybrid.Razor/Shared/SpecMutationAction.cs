namespace CCL.MES.Hybrid.Razor.Shared;

/// <summary>
/// P10.5c-1 — Enum exchanged between <see cref="SpecContextMenu"/> and
/// the list page so the page can route the operator to the detail
/// surface with the right mutation modal pre-armed.
/// </summary>
public enum SpecMutationAction
{
    Edit,
    Copy,
    Revise,
    Supersede,
}
