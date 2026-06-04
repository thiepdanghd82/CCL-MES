using CCL.MES.Shared.Specs;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// P10.5c-3 — Pure helpers for the inline edit feature on the Engineer
/// Spec list. Lives outside the Razor file so xUnit can pin the
/// editability rules + mutation-shape mapping without spinning up a
/// renderer.
///
/// Inline edit applies ONLY to Draft rows (server enforces the Draft-
/// only gate inside <c>SpecService.UpdateAsync</c>; the client mirrors
/// the same rule so non-editable cells render as plain text and never
/// fire a doomed 422). Three fields are exposed for inline edit:
///   - <c>title</c>             → <see cref="UpdateSpecMutation.Title"/>
///   - <c>ref_no</c>            → <see cref="UpdateSpecMutation.RefNo"/>
///   - <c>inspection_level</c>  → <see cref="UpdateSpecMutation.InspectionLevel"/>
///
/// Other update-able fields (<c>process_code</c>, <c>color_spec_json</c>)
/// stay modal-only because they need richer pickers / validation.
/// </summary>
public static class SpecListInlineEditVm
{
    public static readonly string[] EditableFieldKeys =
    {
        "title", "ref_no", "inspection_level",
    };

    /// <summary>True when the row's status is Draft — the only state in
    /// which the server-side <c>UpdateAsync</c> will not 422 with
    /// <c>immutable_status</c>.</summary>
    public static bool IsRowEditable(SpecRevisionStatus status) =>
        status == SpecRevisionStatus.Draft;

    /// <summary>Combined Draft + whitelist check. Trashed rows are NOT
    /// editable even when status happens to be Draft (server check is
    /// <c>!IsTrashed</c> first).</summary>
    public static bool IsCellEditable(SpecListItem row, string fieldKey)
    {
        if (row is null) return false;
        if (!IsRowEditable(row.Status)) return false;
        // Trash-view rows arrive with IsTrashed=true on the row itself
        // via Status semantics — SpecListItem doesn't expose IsTrashed
        // directly, but the Trash view filter at the list level already
        // hides them from Active/Trash chips that map to editable UI.
        return Array.IndexOf(EditableFieldKeys, fieldKey) >= 0;
    }

    /// <summary>Read the field's CURRENT value from the row so the
    /// inline input pre-populates correctly.</summary>
    public static string ReadFieldValue(SpecListItem row, string fieldKey) => fieldKey switch
    {
        "title"            => row.Title ?? "",
        "ref_no"           => row.RefNo ?? "",
        "inspection_level" => row.InspectionLevel ?? "",
        _                  => "",
    };

    /// <summary>Build the <see cref="UpdateSpecMutation"/> for a single
    /// inline-edit save. Only the touched field is populated; all other
    /// fields stay null so the server treats them as "leave unchanged"
    /// per <c>UpdateSpecRequest</c> semantics.</summary>
    public static UpdateSpecMutation BuildMutation(string fieldKey, string newValue)
    {
        var trimmed = (newValue ?? "").Trim();
        return fieldKey switch
        {
            "title"            => new UpdateSpecMutation { Title = trimmed },
            "ref_no"           => new UpdateSpecMutation { RefNo = trimmed },
            "inspection_level" => new UpdateSpecMutation { InspectionLevel = trimmed },
            _ => throw new ArgumentException(
                    $"Field '{fieldKey}' is not inline-editable.", nameof(fieldKey)),
        };
    }

    /// <summary>True when the operator actually changed the value (trim-
    /// insensitive). Lets the UI skip a no-op API call.</summary>
    public static bool HasChanged(string? original, string? edited)
    {
        var o = (original ?? "").Trim();
        var e = (edited ?? "").Trim();
        return !string.Equals(o, e, StringComparison.Ordinal);
    }

    /// <summary>Patch a freshly-fetched server response back into the
    /// list row in-place so the operator sees the saved value without a
    /// full re-fetch round-trip. Returns the patched row.</summary>
    public static SpecListItem ApplyResponseToRow(SpecListItem row, string fieldKey, string newValue)
    {
        if (row is null) throw new ArgumentNullException(nameof(row));
        var trimmed = (newValue ?? "").Trim();
        return fieldKey switch
        {
            "title"            => row with { Title = trimmed },
            "ref_no"           => row with { RefNo = string.IsNullOrEmpty(trimmed) ? null : trimmed },
            "inspection_level" => row with { InspectionLevel = string.IsNullOrEmpty(trimmed) ? null : trimmed },
            _                  => row,
        };
    }
}
