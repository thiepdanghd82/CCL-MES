using CCL.MES.Hybrid.Client.Prepress;
using CCL.MES.Shared.Prepress;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests.Prepress;

/// <summary>
/// Coverage for <see cref="MaterialBarcodeMatcher"/> — the pure scan→BOM
/// matcher behind the PREPRESS "Scan materials" flow. Driven by the 3 real
/// scans + the operator's clarification that the Raw Materials master holds
/// 30031701, 30031701-0228 and 30031702 as THREE distinct parts (so the
/// matcher must NOT strip the suffix).
/// </summary>
public class MaterialBarcodeMatcherTests
{
    private static PrepressMaterialRow Row(string code, int idx = 0, string status = "Pending") =>
        new() { BomLineIdx = idx, MaterialCode = code, Status = status };

    // Three live scans (raw payload before the first '/').
    private const string Scan1 = "30030532/50/(LU29) / lukitape #. 1407V1 (245mm x 50M)";
    private const string Scan2 = "30031701-0228/250/(SDT (EA)#1R(K241221)) 228mm x 1000M";
    private const string Scan3 = "30031145/80/(BU'488) / BU'-0112N (215mm x 1000M)";

    [Theory]
    [InlineData(Scan1, "30030532")]
    [InlineData(Scan2, "30031701-0228")]
    [InlineData(Scan3, "30031145")]
    [InlineData("30031145", "30031145")]          // no '/' — whole string is the part
    [InlineData("  30031145/x ", "30031145")]      // outer trim
    public void ExtractPartNo_takes_segment_before_first_slash(string scan, string expected)
    {
        Assert.Equal(expected, MaterialBarcodeMatcher.ExtractPartNo(scan));
    }

    [Fact]
    public void Pure_numeric_part_matches_single_row()
    {
        var bom = new[] { Row("30030532", 0), Row("30031145", 1) };
        var r = MaterialBarcodeMatcher.Match(bom, Scan1);
        Assert.Equal(MaterialMatchOutcome.Single, r.Outcome);
        Assert.Equal(0, r.Row!.BomLineIdx);
        Assert.Equal("30030532", r.PartNo);
    }

    [Fact]
    public void Hyphenated_part_matches_exact_variant_not_base_nor_sibling()
    {
        // Master holds all three as distinct parts.
        var bom = new[] { Row("30031701", 0), Row("30031701-0228", 1), Row("30031702", 2) };
        var r = MaterialBarcodeMatcher.Match(bom, Scan2);
        Assert.Equal(MaterialMatchOutcome.Single, r.Outcome);
        Assert.Equal(1, r.Row!.BomLineIdx);          // the -0228 variant, exactly
        Assert.Equal("30031701-0228", r.Row!.MaterialCode);
    }

    [Fact]
    public void Hyphenated_scan_does_NOT_strip_suffix_to_match_base_part()
    {
        // BOM has the base + sibling but NOT the -0228 variant: must be a miss,
        // never a silent confirm of the wrong (base) part.
        var bom = new[] { Row("30031701", 0), Row("30031702", 1) };
        var r = MaterialBarcodeMatcher.Match(bom, Scan2);
        Assert.Equal(MaterialMatchOutcome.NoMatch, r.Outcome);
        Assert.Null(r.Row);
        Assert.Equal("30031701-0228", r.PartNo);
    }

    [Fact]
    public void Match_is_case_insensitive_and_trims_material_code()
    {
        var bom = new[] { Row(" 30031701-0228 ", 5) };
        var r = MaterialBarcodeMatcher.Match(bom, "30031701-0228/9/(x)");
        Assert.Equal(MaterialMatchOutcome.Single, r.Outcome);
        Assert.Equal(5, r.Row!.BomLineIdx);
    }

    [Fact]
    public void Unknown_part_is_NoMatch()
    {
        var bom = new[] { Row("30030532"), Row("30031145") };
        var r = MaterialBarcodeMatcher.Match(bom, "99999999/1/(z)");
        Assert.Equal(MaterialMatchOutcome.NoMatch, r.Outcome);
        Assert.Equal("99999999", r.PartNo);
    }

    [Fact]
    public void Same_part_on_two_bom_lines_confirms_first_unconfirmed_then_second()
    {
        // Both pending → first scan picks line 0.
        var bothPending = new[] { Row("30031145", 0), Row("30031145", 3) };
        var r1 = MaterialBarcodeMatcher.Match(bothPending, Scan3);
        Assert.Equal(MaterialMatchOutcome.Single, r1.Outcome);
        Assert.Equal(0, r1.Row!.BomLineIdx);

        // Line 0 already OK → next scan picks line 3.
        var firstDone = new[] { Row("30031145", 0, status: "Ok"), Row("30031145", 3) };
        var r2 = MaterialBarcodeMatcher.Match(firstDone, Scan3);
        Assert.Equal(MaterialMatchOutcome.Single, r2.Outcome);
        Assert.Equal(3, r2.Row!.BomLineIdx);
    }

    [Fact]
    public void All_matching_lines_already_ok_is_AllOk()
    {
        var allOk = new[] { Row("30031145", 0, status: "Ok"), Row("30031145", 3, status: "Ok") };
        var r = MaterialBarcodeMatcher.Match(allOk, Scan3);
        Assert.Equal(MaterialMatchOutcome.AllOk, r.Outcome);
    }

    [Fact]
    public void Description_is_the_remainder_after_part_number()
    {
        Assert.Equal("80/(BU'488) / BU'-0112N (215mm x 1000M)",
            MaterialBarcodeMatcher.ExtractDescription(Scan3));
        Assert.Equal(string.Empty, MaterialBarcodeMatcher.ExtractDescription("30031145"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(null)]
    public void Empty_or_slash_only_payload_is_EmptyCode(string? scan)
    {
        var bom = new[] { Row("30030532") };
        var r = MaterialBarcodeMatcher.Match(bom, scan);
        Assert.Equal(MaterialMatchOutcome.EmptyCode, r.Outcome);
    }
}
