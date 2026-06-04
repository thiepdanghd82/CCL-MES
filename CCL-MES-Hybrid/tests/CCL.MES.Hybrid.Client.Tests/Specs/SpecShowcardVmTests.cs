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

    // ── P10.5d — FromDetailFull flatten ─────────────────────────────

    [Fact]
    public void FromDetailFull_carries_silk_print_colors_and_print_params()
    {
        var detail = new SpecDetailItem
        {
            Id = 11,
            SpecCode = "SP-011",
            Title = "Full silk",
            RevisionCode = "B",
            Status = SpecRevisionStatus.Approved,
            Planner = "SILK",
            ProductCode = "PRD-011",
            ProductName = "Full silk product",
            ProcessCode = "SILKSCREEN",
            IsSilkscreen = true,
            PrintingCavity = 3,
            LengthPitchMm = 485.0,
            ProductSizeWmm = 442,
            ProductSizeHmm = 78.5,
            AdhesiveType = "AC-100",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PrintColors = new()
            {
                new SpecPrintColorRow(1, "Front", "PMS-300C", "Ink-1", "INK-001", "Toyo",
                    "R-3", 28.0, 60.0, "B", "N", 65.0, 5, "—", 4.0,
                    "300x400", "300T", 22.5, "PLT-001", 1, "—"),
                new SpecPrintColorRow(2, "Front", "White", "Ink-2", "INK-002", "Seiko",
                    null, 30.0, 60.0, "A", "U", null, null, "—", null,
                    "300x400", "200T", null, "PLT-002", null, "—"),
            },
        };

        var vm = SpecShowcardVm.FromDetailFull(detail);

        Assert.Equal(2, vm.PrintColors.Count);
        Assert.Equal("PMS-300C", vm.PrintColors[0].Color);
        Assert.Equal("INK-002", vm.PrintColors[1].InkCode);
        Assert.Equal(3, vm.PrintParams.PrintingCavity);
        Assert.Equal(485.0, vm.PrintParams.LengthPitchMm);
        Assert.Equal("AC-100", vm.PrintParams.AdhesiveType);
        Assert.Empty(vm.FlexoPrintRows);
    }

    [Fact]
    public void FromDetailFull_carries_flexo_three_subtables()
    {
        var detail = new SpecDetailItem
        {
            Id = 12,
            SpecCode = "SP-012",
            Title = "Full flexo",
            RevisionCode = "A",
            Status = SpecRevisionStatus.Draft,
            Planner = "FLEXO",
            ProductCode = "PRD-012",
            ProductName = "Full flexo product",
            ProcessCode = "FLEXO",
            IsFlexo = true,
            CreatedAt = DateTime.UtcNow,
            FlexoPrintRows = new()
            {
                new SpecFlexoPrintRow(1, "PRINT-1", "PET", "0.1mm", "300x400", "10/12", "200", "60", "30", "30", "30", "10/0", "30"),
            },
            FlexoCuttingRows = new()
            {
                new SpecFlexoCuttingRow(1, "CUT-1", "L-tape", "300x400", "L-001", "C-001", 12, 6, 50.0, "B-1", 60.0, 50.0, 5.0, 30.0, 30.0),
            },
            FlexoInkRows = new()
            {
                new SpecFlexoInkRow(1, "C", "INK-Cyan", "Cyan ink", "Toyo", "300lpi", "PLT-1", 5.0, 100.0, 50.0),
                new SpecFlexoInkRow(2, "M", "INK-Magenta", "Magenta ink", "Toyo", "300lpi", "PLT-2", 5.0, 100.0, 50.0),
            },
        };

        var vm = SpecShowcardVm.FromDetailFull(detail);

        Assert.Single(vm.FlexoPrintRows);
        Assert.Single(vm.FlexoCuttingRows);
        Assert.Equal(2, vm.FlexoInkRows.Count);
        Assert.Equal("PRINT-1", vm.FlexoPrintRows[0].Process);
        Assert.Empty(vm.PrintColors);
    }

    [Fact]
    public void FromDetailFull_walks_lineage_and_audit()
    {
        var lineage = new List<SpecRevisionLineageEntry>
        {
            new(13, "C", "Current rev (CR-003)", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "engineer"),
            new(12, "B", "Previous rev (CR-002)", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "engineer"),
            new(11, "A", "Initial", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "engineer"),
        };
        var audit = new List<SpecAuditEntry>
        {
            new(new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc), "SPEC_CREATE", "engineer", "Engineer", null),
            new(new DateTime(2026, 3, 2, 1, 0, 0, DateTimeKind.Utc), "SPEC_APPROVE", "leader", "Engineer", null),
        };
        var detail = new SpecDetailItem
        {
            Id = 13,
            SpecCode = "SP-013",
            Title = "Lineage walk",
            RevisionCode = "C",
            Status = SpecRevisionStatus.Draft,
            Planner = "SILK",
            ProductCode = "PRD-013",
            ProductName = "P",
            ProcessCode = "SILKSCREEN",
            IsSilkscreen = true,
            Lineage = lineage,
            AuditEntries = audit,
            CreatedAt = DateTime.UtcNow,
        };

        var vm = SpecShowcardVm.FromDetailFull(detail);

        Assert.Equal(3, vm.Lineage.Count);
        Assert.Equal("C", vm.Lineage[0].RevisionCode);
        Assert.Equal("B", vm.Lineage[1].RevisionCode);
        Assert.Equal("A", vm.Lineage[2].RevisionCode);
        Assert.True(vm.HasParentRev, "Lineage with 3 entries should report HasParentRev=true.");

        Assert.Equal(2, vm.AuditEntries.Count);
        Assert.Equal("SPEC_CREATE", vm.AuditEntries[0].Action);
    }

    [Fact]
    public void FromDetailFull_no_parent_rev_when_lineage_has_one_entry()
    {
        var detail = new SpecDetailItem
        {
            SpecCode = "X",
            ProductCode = "X",
            ProductName = "X",
            ProcessCode = "X",
            RevisionCode = "A",
            Status = SpecRevisionStatus.Draft,
            Planner = "SILK",
            CreatedAt = DateTime.UtcNow,
            Lineage = new() { new(1, "A", null, DateTime.UtcNow, "x") },
        };
        var vm = SpecShowcardVm.FromDetailFull(detail);
        Assert.False(vm.HasParentRev);
    }

    [Fact]
    public void ParseMaterialExtras_handles_well_formed_json()
    {
        const string json = """{"material_size":"300x400","lamination_tape":"L-tape","lamination_size":"300x400","lamination_cavity":"6"}""";
        var extras = SpecShowcardVm.ParseMaterialExtras(json);
        Assert.Equal("300x400", extras.MaterialSize);
        Assert.Equal("L-tape", extras.LaminationTape);
        Assert.Equal("300x400", extras.LaminationSize);
        Assert.Equal("6", extras.LaminationCavity);
    }

    [Fact]
    public void ParseMaterialExtras_returns_empty_on_null_or_malformed()
    {
        var fromNull = SpecShowcardVm.ParseMaterialExtras(null);
        Assert.Null(fromNull.MaterialSize);
        Assert.Null(fromNull.LaminationTape);

        var fromGarbage = SpecShowcardVm.ParseMaterialExtras("{not-json");
        Assert.Null(fromGarbage.MaterialSize);

        var fromNonObject = SpecShowcardVm.ParseMaterialExtras("\"a string\"");
        Assert.Null(fromNonObject.MaterialSize);
    }

    [Fact]
    public void ParseMaterialExtras_handles_partial_json()
    {
        const string json = """{"material_size":"100"}""";
        var extras = SpecShowcardVm.ParseMaterialExtras(json);
        Assert.Equal("100", extras.MaterialSize);
        Assert.Null(extras.LaminationTape);
    }
}
