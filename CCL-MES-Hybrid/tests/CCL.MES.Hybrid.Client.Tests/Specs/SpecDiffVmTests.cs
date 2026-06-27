using CCL.MES.Hybrid.Client.Specs;
using CCL.MES.Shared.Specs;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5d — Pure-helper coverage for the "So sánh với rev trước" diff.
/// Pinned in xUnit so the Razor viewer can rely on the field + row diff
/// rules without re-deriving them. All scenarios construct VMs directly
/// (no SpecDetailItem round-trip needed since the diff operates on the
/// VM shape).
/// </summary>
public sealed class SpecDiffVmTests
{
    private static SpecShowcardVm Vm(
        string rev = "B",
        string title = "Title",
        string? refNo = "REF-1",
        string? inspection = "A",
        string processCode = "SILKSCREEN",
        int? cavity = 1,
        double? pitch = 100.0,
        string? substrate = "PET",
        string? remarks = null,
        IReadOnlyList<SpecPrintColorRow>? colors = null,
        IReadOnlyList<SpecFlexoCuttingRow>? cutting = null,
        IReadOnlyList<SpecRevisionLineageEntry>? lineage = null)
        => new()
        {
            Id = 1,
            SpecCode = "SP",
            Title = title,
            RevisionCode = rev,
            Status = SpecRevisionStatus.Draft,
            PlannerCode = "SILK",
            ProcessCode = processCode,
            RefNo = refNo,
            InspectionLevel = inspection,
            ProductCode = "PRD",
            ProductName = "P",
            CreatedAt = DateTime.UtcNow,
            ComplianceChips = SpecShowcardVm.BuildComplianceChips(inspection),
            PrintParams = new SpecShowcardPrintParams
            {
                PrintingCavity = cavity,
                LengthPitchMm = pitch,
                ProductSizeWmm = 60,
                ProductSizeHmm = 30,
            },
            SubstrateType = substrate,
            RemarksText = remarks,
            PrintColors = colors ?? Array.Empty<SpecPrintColorRow>(),
            FlexoCuttingRows = cutting ?? Array.Empty<SpecFlexoCuttingRow>(),
            Lineage = lineage ?? Array.Empty<SpecRevisionLineageEntry>(),
        };

    private static SpecPrintColorRow Color(int seq, string? color = "PMS-300C", string? inkCode = "INK-001", string? mesh = "300T")
        => new(seq, "Front", color, "Ink", inkCode, "Toyo",
            null, null, null, null, null, null, null, null, null,
            "300x400", mesh, null, "PLT-1", null, null);

    private static SpecFlexoCuttingRow Cut(int seq, string? process = "CUT-1", string? cutter = "C-001", int? cavity = 6)
        => new(seq, process, "L-tape", "300x400", "L-001", cutter, 12, cavity, 50.0,
            "B-1", 60.0, 50.0, 5.0, 30.0, 30.0);

    // ── No-diff happy path ──────────────────────────────────────────

    [Fact]
    public void Compare_returns_empty_when_VMs_identical()
    {
        var current = Vm();
        var parent = Vm();
        var result = SpecDiffVm.Compare(current, parent);
        Assert.Equal("B", result.CurrentRev);
        Assert.Equal("B", result.ParentRev);
        Assert.False(result.HasDiff);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Compare_returns_empty_when_parent_null()
    {
        var current = Vm();
        var result = SpecDiffVm.Compare(current, null);
        Assert.False(result.HasDiff);
        Assert.Null(result.ParentRev);
    }

    // ── Identity field diffs ────────────────────────────────────────

    [Fact]
    public void Compare_detects_title_change()
    {
        var current = Vm(title: "New Title");
        var parent = Vm(title: "Old Title");
        var result = SpecDiffVm.Compare(current, parent);
        Assert.Single(result.Entries);
        var entry = result.Entries[0];
        Assert.Equal(SpecDiffVm.DiffSection.Identity, entry.Section);
        Assert.Equal("Title", entry.FieldLabel);
        Assert.Equal("Old Title", entry.OldValue);
        Assert.Equal("New Title", entry.NewValue);
        Assert.Equal(SpecDiffVm.ChangeType.Changed, entry.Kind);
    }

    [Fact]
    public void Compare_detects_added_when_parent_field_blank()
    {
        var current = Vm(refNo: "NEW-REF");
        var parent = Vm(refNo: null);
        var result = SpecDiffVm.Compare(current, parent);
        var entry = result.Entries.Single(e => e.FieldLabel == "REF NO");
        Assert.Equal(SpecDiffVm.ChangeType.Added, entry.Kind);
        Assert.Null(entry.OldValue);
        Assert.Equal("NEW-REF", entry.NewValue);
    }

    [Fact]
    public void Compare_detects_removed_when_current_field_blank()
    {
        var current = Vm(refNo: null);
        var parent = Vm(refNo: "OLD-REF");
        var result = SpecDiffVm.Compare(current, parent);
        var entry = result.Entries.Single(e => e.FieldLabel == "REF NO");
        Assert.Equal(SpecDiffVm.ChangeType.Removed, entry.Kind);
        Assert.Equal("OLD-REF", entry.OldValue);
        Assert.Null(entry.NewValue);
    }

    [Fact]
    public void Compare_trim_insensitive_match()
    {
        // Whitespace-only delta should NOT register a diff per the
        // trim-insensitive normalisation rules in AddFieldDiff.
        var current = Vm(title: "  Same  ");
        var parent = Vm(title: "Same");
        var result = SpecDiffVm.Compare(current, parent);
        Assert.False(result.HasDiff);
    }

    // ── Print param diffs ───────────────────────────────────────────

    [Fact]
    public void Compare_detects_cavity_and_pitch_changes()
    {
        var current = Vm(cavity: 4, pitch: 120.0);
        var parent = Vm(cavity: 2, pitch: 100.0);
        var result = SpecDiffVm.Compare(current, parent);
        var cavityEntry = result.Entries.Single(e => e.FieldLabel == "Printing cavity");
        Assert.Equal("2", cavityEntry.OldValue);
        Assert.Equal("4", cavityEntry.NewValue);
        var pitchEntry = result.Entries.Single(e => e.FieldLabel == "Length pitch (mm)");
        Assert.Equal("100", pitchEntry.OldValue);
        Assert.Equal("120", pitchEntry.NewValue);
    }

    // ── Row-set diffs (silk colours) ────────────────────────────────

    [Fact]
    public void Compare_detects_added_silk_colour_row()
    {
        var current = Vm(colors: new[] { Color(1), Color(2), Color(3) });
        var parent = Vm(colors: new[] { Color(1), Color(2) });
        var result = SpecDiffVm.Compare(current, parent);
        var added = result.Entries.Single(e =>
            e.Section == SpecDiffVm.DiffSection.PrintColors && e.Kind == SpecDiffVm.ChangeType.Added);
        Assert.Equal("3", added.RowKey);
        Assert.Null(added.OldValue);
        Assert.NotNull(added.NewValue);
    }

    [Fact]
    public void Compare_detects_removed_silk_colour_row()
    {
        var current = Vm(colors: new[] { Color(1) });
        var parent = Vm(colors: new[] { Color(1), Color(2) });
        var result = SpecDiffVm.Compare(current, parent);
        var removed = result.Entries.Single(e =>
            e.Section == SpecDiffVm.DiffSection.PrintColors && e.Kind == SpecDiffVm.ChangeType.Removed);
        Assert.Equal("2", removed.RowKey);
        Assert.NotNull(removed.OldValue);
        Assert.Null(removed.NewValue);
    }

    [Fact]
    public void Compare_detects_changed_silk_colour_row()
    {
        var current = Vm(colors: new[] { Color(1, inkCode: "INK-NEW") });
        var parent = Vm(colors: new[] { Color(1, inkCode: "INK-OLD") });
        var result = SpecDiffVm.Compare(current, parent);
        var changed = result.Entries.Single(e =>
            e.Section == SpecDiffVm.DiffSection.PrintColors && e.Kind == SpecDiffVm.ChangeType.Changed);
        Assert.Equal("1", changed.RowKey);
        Assert.Contains("INK-OLD", changed.OldValue);
        Assert.Contains("INK-NEW", changed.NewValue);
    }

    [Fact]
    public void Compare_no_change_when_colour_row_unchanged()
    {
        var current = Vm(colors: new[] { Color(1) });
        var parent = Vm(colors: new[] { Color(1) });
        var result = SpecDiffVm.Compare(current, parent);
        Assert.DoesNotContain(result.Entries, e => e.Section == SpecDiffVm.DiffSection.PrintColors);
    }

    // ── Row-set diffs (flexo cutting) ───────────────────────────────

    [Fact]
    public void Compare_detects_flexo_cutting_changes_independently()
    {
        var current = Vm(cutting: new[] { Cut(1, cutter: "C-NEW") });
        var parent = Vm(cutting: new[] { Cut(1, cutter: "C-OLD") });
        var result = SpecDiffVm.Compare(current, parent);
        var changed = result.Entries.Single(e =>
            e.Section == SpecDiffVm.DiffSection.FlexoCutting && e.Kind == SpecDiffVm.ChangeType.Changed);
        Assert.Equal("1", changed.RowKey);
    }

    // ── Combined multi-section ──────────────────────────────────────

    [Fact]
    public void Compare_groups_entries_in_a_single_pass()
    {
        var current = Vm(
            title: "T-new",
            cavity: 5,
            substrate: "PVC",
            remarks: "New remark",
            colors: new[] { Color(1, inkCode: "INK-NEW"), Color(2) });
        var parent = Vm(
            title: "T-old",
            cavity: 1,
            substrate: "PET",
            remarks: null,
            colors: new[] { Color(1, inkCode: "INK-OLD") });

        var result = SpecDiffVm.Compare(current, parent);

        Assert.True(result.HasDiff);
        Assert.Contains(result.Entries, e => e.FieldLabel == "Title");
        Assert.Contains(result.Entries, e => e.FieldLabel == "Printing cavity");
        Assert.Contains(result.Entries, e => e.FieldLabel == "Substrate type");
        Assert.Contains(result.Entries, e => e.FieldLabel == "Print remarks" && e.Kind == SpecDiffVm.ChangeType.Added);
        Assert.Equal(2, result.Entries.Count(e => e.Section == SpecDiffVm.DiffSection.PrintColors));
    }
}
