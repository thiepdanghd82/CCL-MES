using Microsoft.AspNetCore.Components;

namespace CCL.MES.Hybrid.Razor.Shared;

/// <summary>
/// One entry in a <see cref="RowContextMenu"/>. A row action (Copy / Edit /
/// Delete / …) — or a <see cref="IsDivider"/> separator. RBAC is expressed by
/// the CALLER: omit an item the user can't perform, or set <see cref="Disabled"/>
/// (the server still enforces the real 403). The menu invokes
/// <see cref="OnClick"/> then closes itself.
/// </summary>
public sealed record ContextMenuItem
{
    public string Label { get; init; } = "";
    public string? Icon { get; init; }
    public bool Danger { get; init; }
    public bool Disabled { get; init; }
    public EventCallback OnClick { get; init; }

    /// <summary>A non-interactive separator line (Label/OnClick ignored).</summary>
    public bool IsDivider { get; init; }

    public static ContextMenuItem Divider { get; } = new() { IsDivider = true };
}
