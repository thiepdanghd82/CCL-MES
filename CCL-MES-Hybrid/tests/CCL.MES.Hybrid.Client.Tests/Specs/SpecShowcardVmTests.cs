using CCL.MES.Shared.Specs;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5b — SpecShowcardVm pure-function coverage. Status / planner /
/// product-size / compliance-chip mapping is locked here so any
/// regression surfaces as a test failure rather than a misrendered
/// pill in production.
/// </summary>
public sealed class SpecShowcardVmTests
{
    // ── Status label ────────────────────────────────────────────────

    [Theory]
    [InlineData(SpecRevisionStatus.Draft,      "Bản nháp")]
    [InlineData(SpecRevisionStatus.InReview,   "Đang xét")]
    [InlineData(SpecRevisionStatus.Approved,   "Đã duyệt")]
    [InlineData(SpecRevisionStatus.Released,   "Đã phát hành")]
    [InlineData(SpecRevisionStatus.Superseded, "Đã thay thế")]
    public void StatusLabelVi_covers_5_states(SpecRevisionStatus status, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.StatusLabelVi(status));
    }

    [Theory]
    [InlineData(SpecRevisionStatus.Draft,      "spec-status-draft")]
    [InlineData(SpecRevisionStatus.InReview,   "spec-status-in-review")]
    [InlineData(SpecRevisionStatus.Approved,   "spec-status-approved")]
    [InlineData(SpecRevisionStatus.Released,   "spec-status-released")]
    [InlineData(SpecRevisionStatus.Superseded, "spec-status-superseded")]
    public void StatusCssClassFor_uses_stable_kebab_slugs(SpecRevisionStatus status, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.StatusCssClassFor(status));
    }

    // ── Planner normaliser ─────────────────────────────────────────

    [Theory]
    [InlineData("SILK", null, "SILK")]
    [InlineData("FLEXO", null, "FLEXO")]
    [InlineData("LETTER", null, "LETTER")]
    [InlineData("INDIGO", null, "INDIGO")]
    [InlineData("DIECUT", null, "DIECUT")]
    [InlineData("UNKNOWN", null, "UNKNOWN")]
    [InlineData("silk", null, "SILK")]
    [InlineData("  flexo  ", null, "FLEXO")]
    public void NormalisePlannerCode_passthrough_canonical(string raw, string? pc, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.NormalisePlannerCode(raw, pc));
    }

    [Theory]
    [InlineData(null, "SILKSCREEN", "SILK")]
    [InlineData(null, "FLEXO_GALLUS4C", "FLEXO")]
    [InlineData(null, "LETTERPRESS", "LETTER")]
    [InlineData(null, "INDIGO6800", "INDIGO")]
    [InlineData(null, "DIECUT", "DIECUT")]
    [InlineData(null, "DIE-CUT", "DIECUT")]
    [InlineData(null, "DIE_CUT", "DIECUT")]
    [InlineData("", "Silkscreen", "SILK")]
    [InlineData(null, null, "UNKNOWN")]
    [InlineData(null, "", "UNKNOWN")]
    [InlineData(null, "SOMETHING_ELSE", "UNKNOWN")]
    public void NormalisePlannerCode_derives_from_process_code_when_planner_blank(string? raw, string? pc, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.NormalisePlannerCode(raw, pc));
    }

    [Theory]
    [InlineData("SILK",    "Lụa")]
    [InlineData("FLEXO",   "Flexo")]
    [InlineData("LETTER",  "Letterpress")]
    [InlineData("INDIGO",  "Indigo")]
    [InlineData("DIECUT",  "Bế")]
    [InlineData("UNKNOWN", "Chưa rõ")]
    [InlineData("BOGUS",   "Chưa rõ")]
    public void PlannerLabelVi_covers_palette(string code, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.PlannerLabelVi(code));
    }

    [Theory]
    [InlineData("SILK",    "spec-planner-silk")]
    [InlineData("FLEXO",   "spec-planner-flexo")]
    [InlineData("LETTER",  "spec-planner-letter")]
    [InlineData("INDIGO",  "spec-planner-indigo")]
    [InlineData("DIECUT",  "spec-planner-diecut")]
    [InlineData("UNKNOWN", "spec-planner-unknown")]
    public void PlannerCssClassFor_returns_kebab_slug(string code, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.PlannerCssClassFor(code));
    }

    // ── Product size format ─────────────────────────────────────────

    [Theory]
    [InlineData(null, null, "—")]
    [InlineData(60.0, 30.0, "60.0 × 30.0 mm")]
    [InlineData(60.0, null, "60.0 × — mm")]
    [InlineData(null, 30.0, "— × 30.0 mm")]
    [InlineData(60.5, 30.25, "60.5 × 30.3 mm")]   // rounds to 1 decimal
    public void FormatProductSize_pattern(double? w, double? h, string expected)
    {
        Assert.Equal(expected, SpecShowcardVm.FormatProductSize(w, h));
    }

    // ── Compliance chips ────────────────────────────────────────────

    [Fact]
    public void BuildComplianceChips_returns_3_items_in_stable_order()
    {
        var chips = SpecShowcardVm.BuildComplianceChips("A166");
        Assert.Equal(3, chips.Count);
        Assert.Equal("HSF strict control", chips[0]);
        Assert.Equal("Spec A166", chips[1]);
        Assert.Equal("RoHS Compliance", chips[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildComplianceChips_falls_back_to_Spec_A_when_blank(string? inspection)
    {
        var chips = SpecShowcardVm.BuildComplianceChips(inspection);
        Assert.Equal("Spec A", chips[1]);
    }

    // ── FromListItem flatten ───────────────────────────────────────

    [Fact]
    public void FromListItem_flattens_identity_and_palettes()
    {
        var item = new SpecListItem
        {
            Id = 42,
            SpecCode = "SP-001",
            Title = "Demo",
            RevisionCode = "B",
            Status = SpecRevisionStatus.Approved,
            ProductCode = "PRD-001",
            ProductName = "Product",
            CustomerName = "Brady",
            ProcessCode = "FLEXO_GALLUS4C",
            Planner = null, // blank → derive from ProcessCode
            InspectionLevel = "A166",
        };

        var vm = SpecShowcardVm.FromListItem(item);

        Assert.Equal(42, vm.Id);
        Assert.Equal("SP-001", vm.SpecCode);
        Assert.Equal("B", vm.RevisionCode);
        Assert.Equal(SpecRevisionStatus.Approved, vm.Status);
        Assert.Equal("Đã duyệt", vm.StatusLabel);
        Assert.Equal("spec-status-approved", vm.StatusCssClass);
        Assert.Equal("FLEXO", vm.PlannerCode);
        Assert.Equal("Flexo", vm.PlannerLabel);
        Assert.Equal("spec-planner-flexo", vm.PlannerCssClass);
        Assert.Equal(3, vm.ComplianceChips.Count);
        Assert.Equal("Spec A166", vm.ComplianceChips[1]);
    }

    [Fact]
    public void FromListItem_handles_blank_revision_code_with_default_A()
    {
        var item = new SpecListItem
        {
            Id = 1,
            SpecCode = "SP-001",
            Title = "Demo",
            RevisionCode = "",   // server returned blank
            Status = SpecRevisionStatus.Draft,
            ProductCode = "PRD",
            ProductName = "P",
        };

        var vm = SpecShowcardVm.FromListItem(item);

        Assert.Equal("A", vm.RevisionCode);
    }

    // ── FromDetail flatten ─────────────────────────────────────────

    [Fact]
    public void FromDetail_carries_remarks_and_product_size()
    {
        var detail = new SpecDetailItem
        {
            Id = 7,
            SpecCode = "SP-007",
            Title = "Detail",
            RevisionCode = "C",
            Status = SpecRevisionStatus.Released,
            Planner = "SILK",
            ProductCode = "PRD-007",
            ProductName = "Detail product",
            ProcessCode = "SILKSCREEN",
            IsSilkscreen = true,
            ProductSizeWmm = 60.0,
            ProductSizeHmm = 30.5,
            RemarksText = "Note in print",
            RemarksCutText = "Note in cut",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var vm = SpecShowcardVm.FromDetail(detail);

        Assert.Equal("Đã phát hành", vm.StatusLabel);
        Assert.Equal("SILK", vm.PlannerCode);
        Assert.Equal("60.0 × 30.5 mm", vm.ProductSizeDisplay);
        Assert.Equal("Note in print", vm.RemarksText);
        Assert.Equal("Note in cut", vm.RemarksCutText);
        Assert.True(vm.IsSilkscreen);
        Assert.False(vm.IsFlexo);
        Assert.NotNull(vm.UpdatedAt);
        Assert.NotNull(vm.ApprovedAt);
    }

    [Fact]
    public void FromDetail_uses_default_revision_A_when_blank()
    {
        var detail = new SpecDetailItem
        {
            SpecCode = "X",
            ProductCode = "X",
            ProductName = "X",
            ProcessCode = "X",
            RevisionCode = "",
            Status = SpecRevisionStatus.Draft,
            Planner = "UNKNOWN",
        };

        var vm = SpecShowcardVm.FromDetail(detail);
        Assert.Equal("A", vm.RevisionCode);
    }
}
