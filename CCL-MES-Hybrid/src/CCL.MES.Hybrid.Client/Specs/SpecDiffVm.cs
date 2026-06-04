using CCL.MES.Shared.Specs;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// P10.5d — Pure-helper diff engine for the "So sánh với rev trước"
/// feature on the Spec detail page. Takes two <see cref="SpecShowcardVm"/>
/// instances (current + parent rev) and projects a flat list of
/// <see cref="SpecDiffEntry"/> rows the Razor viewer renders as a
/// strikethrough / highlight table.
///
/// Lives in <c>CCL.MES.Hybrid.Client.Specs</c> so xUnit pins the diff
/// rules (added / removed / changed semantics, row-key collision,
/// no-change collapse) without spinning up a Razor renderer.
///
/// Mirrors the same VM-helper-then-Razor-consumer pattern used by
/// <see cref="SpecListInlineEditVm"/> (PR #85): consumers (the
/// <c>SpecDiffViewer</c> component) only render — they do not derive.
/// </summary>
public static class SpecDiffVm
{
    /// <summary>Kind of change for a diff row.</summary>
    public enum ChangeType { Added, Removed, Changed }

    /// <summary>Diff section group — drives the Razor section headers.</summary>
    public enum DiffSection { Identity, PrintParams, Material, Remarks, PrintColors, FlexoPrint, FlexoCutting, FlexoInk }

    /// <summary>One diff row. <see cref="OldValue"/> is the parent rev
    /// value (current state before change); <see cref="NewValue"/> is
    /// what the current rev shows. For <see cref="ChangeType.Added"/>,
    /// OldValue is null; for <see cref="ChangeType.Removed"/>, NewValue
    /// is null.</summary>
    public sealed record SpecDiffEntry(
        DiffSection Section,
        string FieldLabel,
        string? OldValue,
        string? NewValue,
        ChangeType Kind,
        string? RowKey = null);

    /// <summary>Top-level diff result: ordered diff entries + a
    /// pre-computed "is anything different" flag the UI uses to render
    /// the "no changes" empty-state branch.</summary>
    public sealed record SpecDiffResult(
        IReadOnlyList<SpecDiffEntry> Entries,
        string? CurrentRev,
        string? ParentRev)
    {
        public bool HasDiff => Entries.Count > 0;
    }

    /// <summary>
    /// Compute the diff. <paramref name="current"/> is the rev the
    /// operator is viewing; <paramref name="parent"/> is the next-older
    /// entry from <see cref="SpecShowcardVm.Lineage"/>. Pass null parent
    /// → returns an empty diff (caller should not have triggered the
    /// compare toggle without a parent — defensive only).
    /// </summary>
    public static SpecDiffResult Compare(SpecShowcardVm current, SpecShowcardVm? parent)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (parent is null)
            return new SpecDiffResult(Array.Empty<SpecDiffEntry>(),
                current.RevisionCode, null);

        var entries = new List<SpecDiffEntry>();

        // ── Identity / header ───────────────────────────────────────
        AddFieldDiff(entries, DiffSection.Identity, "Tiêu đề", parent.Title, current.Title);
        AddFieldDiff(entries, DiffSection.Identity, "REF NO", parent.RefNo, current.RefNo);
        AddFieldDiff(entries, DiffSection.Identity, "Spec / Inspection level",
            parent.InspectionLevel, current.InspectionLevel);
        AddFieldDiff(entries, DiffSection.Identity, "Process code",
            parent.ProcessCode, current.ProcessCode);

        // ── Print params ────────────────────────────────────────────
        AddFieldDiff(entries, DiffSection.PrintParams, "Printing cavity",
            parent.PrintParams.PrintingCavity?.ToString(),
            current.PrintParams.PrintingCavity?.ToString());
        AddFieldDiff(entries, DiffSection.PrintParams, "Length pitch (mm)",
            FormatNullableDouble(parent.PrintParams.LengthPitchMm),
            FormatNullableDouble(current.PrintParams.LengthPitchMm));
        AddFieldDiff(entries, DiffSection.PrintParams, "Product size",
            FormatProductSize(parent.PrintParams.ProductSizeWmm, parent.PrintParams.ProductSizeHmm),
            FormatProductSize(current.PrintParams.ProductSizeWmm, current.PrintParams.ProductSizeHmm));
        AddFieldDiff(entries, DiffSection.PrintParams, "Adhesive",
            parent.PrintParams.AdhesiveType, current.PrintParams.AdhesiveType);
        AddFieldDiff(entries, DiffSection.PrintParams, "Varnish",
            parent.PrintParams.Varnish, current.PrintParams.Varnish);
        AddFieldDiff(entries, DiffSection.PrintParams, "Lamination",
            parent.PrintParams.Lamination, current.PrintParams.Lamination);

        // ── Material ────────────────────────────────────────────────
        AddFieldDiff(entries, DiffSection.Material, "Substrate type",
            parent.SubstrateType, current.SubstrateType);
        AddFieldDiff(entries, DiffSection.Material, "Material size",
            parent.MaterialExtras.MaterialSize, current.MaterialExtras.MaterialSize);
        AddFieldDiff(entries, DiffSection.Material, "Lamination tape",
            parent.MaterialExtras.LaminationTape, current.MaterialExtras.LaminationTape);

        // ── Remarks ─────────────────────────────────────────────────
        AddFieldDiff(entries, DiffSection.Remarks, "Print remarks",
            parent.RemarksText, current.RemarksText);
        AddFieldDiff(entries, DiffSection.Remarks, "Cut remarks",
            parent.RemarksCutText, current.RemarksCutText);

        // ── Silk print colours (keyed by Seq) ───────────────────────
        DiffRowSet(entries, DiffSection.PrintColors,
            parent.PrintColors, current.PrintColors,
            c => c.Seq.ToString(),
            c => DescribePrintColor(c));

        // ── Flexo print rows (keyed by Seq) ─────────────────────────
        DiffRowSet(entries, DiffSection.FlexoPrint,
            parent.FlexoPrintRows, current.FlexoPrintRows,
            r => r.Seq.ToString(),
            r => DescribeFlexoPrint(r));

        // ── Flexo cutting rows (keyed by Seq) ───────────────────────
        DiffRowSet(entries, DiffSection.FlexoCutting,
            parent.FlexoCuttingRows, current.FlexoCuttingRows,
            r => r.Seq.ToString(),
            r => DescribeFlexoCutting(r));

        // ── Flexo ink rows (keyed by Seq) ───────────────────────────
        DiffRowSet(entries, DiffSection.FlexoInk,
            parent.FlexoInkRows, current.FlexoInkRows,
            r => r.Seq.ToString(),
            r => DescribeFlexoInk(r));

        return new SpecDiffResult(entries, current.RevisionCode, parent.RevisionCode);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static void AddFieldDiff(
        List<SpecDiffEntry> sink, DiffSection section, string label,
        string? oldVal, string? newVal)
    {
        var o = Normalize(oldVal);
        var n = Normalize(newVal);
        if (string.Equals(o, n, StringComparison.Ordinal)) return;
        ChangeType kind;
        if (string.IsNullOrEmpty(o)) kind = ChangeType.Added;
        else if (string.IsNullOrEmpty(n)) kind = ChangeType.Removed;
        else kind = ChangeType.Changed;
        sink.Add(new SpecDiffEntry(section, label, NullIfEmpty(o), NullIfEmpty(n), kind));
    }

    /// <summary>Generic row-set differ. Keys rows by <paramref name="keyOf"/>;
    /// uses <paramref name="describe"/> to project each row to a stable
    /// human string for the OldValue / NewValue cell + Changed
    /// detection.</summary>
    private static void DiffRowSet<T>(
        List<SpecDiffEntry> sink, DiffSection section,
        IReadOnlyList<T> parentRows, IReadOnlyList<T> currentRows,
        Func<T, string> keyOf, Func<T, string> describe)
    {
        var parentByKey = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var r in parentRows) parentByKey[keyOf(r)] = r;
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in currentRows)
        {
            var key = keyOf(r);
            currentKeys.Add(key);
            if (parentByKey.TryGetValue(key, out var p))
            {
                var pDesc = describe(p);
                var cDesc = describe(r);
                if (!string.Equals(pDesc, cDesc, StringComparison.Ordinal))
                {
                    sink.Add(new SpecDiffEntry(
                        section, $"Row #{key}", pDesc, cDesc, ChangeType.Changed, key));
                }
            }
            else
            {
                sink.Add(new SpecDiffEntry(
                    section, $"Row #{key}", null, describe(r), ChangeType.Added, key));
            }
        }
        foreach (var (key, p) in parentByKey)
        {
            if (!currentKeys.Contains(key))
            {
                sink.Add(new SpecDiffEntry(
                    section, $"Row #{key}", describe(p), null, ChangeType.Removed, key));
            }
        }
    }

    private static string? Normalize(string? v) => v?.Trim() ?? "";
    private static string? NullIfEmpty(string? v) => string.IsNullOrEmpty(v) ? null : v;

    private static string FormatNullableDouble(double? v) =>
        v is null ? "" : v.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatProductSize(double? w, double? h)
    {
        if (w is null && h is null) return "";
        var ws = FormatNullableDouble(w);
        var hs = FormatNullableDouble(h);
        return $"{(string.IsNullOrEmpty(ws) ? "—" : ws)} × {(string.IsNullOrEmpty(hs) ? "—" : hs)} mm";
    }

    // ── row description projections (stable Changed-detection) ──────

    private static string DescribePrintColor(SpecPrintColorRow c) =>
        $"surf={c.Surface ?? "-"}, color={c.Color ?? "-"}, ink={c.InkCode ?? "-"}, " +
        $"maker={c.Maker ?? "-"}, mesh={c.Mesh ?? "-"}, plate={c.PlateCode ?? "-"}";

    private static string DescribeFlexoPrint(SpecFlexoPrintRow r) =>
        $"process={r.Process ?? "-"}, material={r.Material ?? "-"}, size={r.Size ?? "-"}, " +
        $"cyl={r.Cylinders ?? "-"}, pitch={r.PitchMm ?? "-"}, speed={r.Speed ?? "-"}";

    private static string DescribeFlexoCutting(SpecFlexoCuttingRow r) =>
        $"process={r.Process ?? "-"}, cutter={r.CutterName ?? "-"}, " +
        $"pcs/sheet={r.PcsPerSheet?.ToString() ?? "-"}, cavity={r.CuttingCavity?.ToString() ?? "-"}, " +
        $"pitch={FormatNullableDouble(r.PitchMm)}";

    private static string DescribeFlexoInk(SpecFlexoInkRow r) =>
        $"color={r.Color ?? "-"}, code={r.InkCode ?? "-"}, brand={r.Brand ?? "-"}, " +
        $"anilox={r.Anilox ?? "-"}, plate={r.PlateCode ?? "-"}";
}
