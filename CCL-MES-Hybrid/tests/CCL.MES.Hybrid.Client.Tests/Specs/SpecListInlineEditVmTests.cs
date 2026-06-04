using CCL.MES.Hybrid.Client.Specs;
using CCL.MES.Shared.Specs;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5c-3 — pure-helper coverage for the inline-edit feature on
/// the Engineer Spec list. Pinned in unit tests so the Razor page
/// can rely on the editability + mutation-shape rules without
/// re-deriving them.
/// </summary>
public sealed class SpecListInlineEditVmTests
{
    private static SpecListItem MakeRow(SpecRevisionStatus status,
        string title = "Foo", string? refNo = "REF-1", string? inspection = "A")
        => new()
        {
            Id = 1,
            SpecCode = "SP-1",
            Title = title,
            RevisionCode = "A",
            Status = status,
            ProductCode = "PRD-1",
            ProductName = "Prod",
            RefNo = refNo,
            InspectionLevel = inspection,
        };

    [Theory]
    [InlineData(SpecRevisionStatus.Draft, true)]
    [InlineData(SpecRevisionStatus.InReview, false)]
    [InlineData(SpecRevisionStatus.Approved, false)]
    [InlineData(SpecRevisionStatus.Released, false)]
    [InlineData(SpecRevisionStatus.Superseded, false)]
    public void IsRowEditable_only_true_for_Draft(SpecRevisionStatus status, bool expected)
    {
        Assert.Equal(expected, SpecListInlineEditVm.IsRowEditable(status));
    }

    [Theory]
    [InlineData("title", true)]
    [InlineData("ref_no", true)]
    [InlineData("inspection_level", true)]
    [InlineData("process_code", false)]   // modal-only
    [InlineData("color_spec_json", false)] // modal-only
    [InlineData("rev", false)]            // not editable
    [InlineData("status", false)]
    [InlineData("", false)]
    public void IsCellEditable_whitelists_three_fields(string fieldKey, bool expected)
    {
        var row = MakeRow(SpecRevisionStatus.Draft);
        Assert.Equal(expected, SpecListInlineEditVm.IsCellEditable(row, fieldKey));
    }

    [Fact]
    public void IsCellEditable_false_for_non_Draft_even_on_whitelisted_field()
    {
        var row = MakeRow(SpecRevisionStatus.Approved);
        Assert.False(SpecListInlineEditVm.IsCellEditable(row, "title"));
        Assert.False(SpecListInlineEditVm.IsCellEditable(row, "ref_no"));
        Assert.False(SpecListInlineEditVm.IsCellEditable(row, "inspection_level"));
    }

    [Fact]
    public void IsCellEditable_handles_null_row_gracefully()
    {
        Assert.False(SpecListInlineEditVm.IsCellEditable(null!, "title"));
    }

    [Fact]
    public void ReadFieldValue_returns_blank_when_underlying_is_null()
    {
        var row = MakeRow(SpecRevisionStatus.Draft, title: "T", refNo: null, inspection: null);
        Assert.Equal("T", SpecListInlineEditVm.ReadFieldValue(row, "title"));
        Assert.Equal("", SpecListInlineEditVm.ReadFieldValue(row, "ref_no"));
        Assert.Equal("", SpecListInlineEditVm.ReadFieldValue(row, "inspection_level"));
        Assert.Equal("", SpecListInlineEditVm.ReadFieldValue(row, "unknown_key"));
    }

    [Fact]
    public void BuildMutation_only_populates_the_touched_field()
    {
        var mut = SpecListInlineEditVm.BuildMutation("title", "  New Title  ");
        Assert.Equal("New Title", mut.Title);  // trim applied
        Assert.Null(mut.RefNo);
        Assert.Null(mut.InspectionLevel);
        Assert.Null(mut.ProcessCode);
        Assert.Null(mut.ColorSpecJson);
    }

    [Fact]
    public void BuildMutation_supports_each_whitelisted_key()
    {
        var t = SpecListInlineEditVm.BuildMutation("title", "T");
        var r = SpecListInlineEditVm.BuildMutation("ref_no", "R");
        var i = SpecListInlineEditVm.BuildMutation("inspection_level", "I");
        Assert.Equal("T", t.Title);
        Assert.Equal("R", r.RefNo);
        Assert.Equal("I", i.InspectionLevel);
    }

    [Fact]
    public void BuildMutation_throws_on_unknown_field()
    {
        Assert.Throws<ArgumentException>(() =>
            SpecListInlineEditVm.BuildMutation("process_code", "X"));
    }

    [Theory]
    [InlineData("Foo", "Foo", false)]
    [InlineData("Foo", "Bar", true)]
    [InlineData("Foo", "  Foo  ", false)]   // trim-insensitive
    [InlineData(null, "", false)]
    [InlineData("", null, false)]
    [InlineData(null, "X", true)]
    public void HasChanged_trim_insensitive(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, SpecListInlineEditVm.HasChanged(a, b));
    }

    [Fact]
    public void ApplyResponseToRow_patches_title_only()
    {
        var row = MakeRow(SpecRevisionStatus.Draft, title: "Old", refNo: "R-1", inspection: "A");
        var patched = SpecListInlineEditVm.ApplyResponseToRow(row, "title", "New");
        Assert.Equal("New", patched.Title);
        Assert.Equal("R-1", patched.RefNo);
        Assert.Equal("A", patched.InspectionLevel);
        // The original row is left untouched (record `with` returns a new instance).
        Assert.Equal("Old", row.Title);
    }

    [Fact]
    public void ApplyResponseToRow_normalises_empty_to_null_for_optional_fields()
    {
        var row = MakeRow(SpecRevisionStatus.Draft, refNo: "R-1", inspection: "A");
        var clearedRef = SpecListInlineEditVm.ApplyResponseToRow(row, "ref_no", "");
        Assert.Null(clearedRef.RefNo);
        var clearedInsp = SpecListInlineEditVm.ApplyResponseToRow(row, "inspection_level", "  ");
        Assert.Null(clearedInsp.InspectionLevel);
    }

    [Fact]
    public void ApplyResponseToRow_unknown_field_is_a_noop()
    {
        var row = MakeRow(SpecRevisionStatus.Draft);
        var patched = SpecListInlineEditVm.ApplyResponseToRow(row, "process_code", "X");
        Assert.Same(row, patched); // record-with would clone; unknown key returns the same row instance
    }
}
