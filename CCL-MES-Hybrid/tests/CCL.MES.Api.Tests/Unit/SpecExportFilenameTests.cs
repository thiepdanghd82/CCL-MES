using CCL.MES.Api.Controllers;
using CCL.MES.Application.Services;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// P10.5g — Pure helper coverage for filename + filter-description
/// composition on the server side. Hoisted out of the controller so
/// the unit tests don't need to spin up the integration host just to
/// pin the timestamp shape + sanitisation rules.
/// </summary>
public sealed class SpecExportFilenameTests
{
    private static readonly DateTime Frozen =
        new DateTime(2026, 6, 4, 10, 30, 15, DateTimeKind.Local);

    [Theory]
    [InlineData("csv",  "NpiSpecLibrary_20260604-103015.csv")]
    [InlineData("xlsx", "NpiSpecLibrary_20260604-103015.xlsx")]
    [InlineData("pdf",  "NpiSpecLibrary_20260604-103015.pdf")]
    public void List_filename_carries_prefix_timestamp_and_extension(string ext, string expected)
    {
        Assert.Equal(expected, SpecExportFilename.List(ext, Frozen));
    }

    [Fact]
    public void Sheet_filename_uses_RefNo_when_present()
    {
        var name = SpecExportFilename.SheetPdf("REF-2026-S0042", "SPEC-IGNORED", "B", Frozen);
        Assert.StartsWith("SpecSheet_REF-2026-S0042_RevB_20260604-103015", name);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void Sheet_filename_falls_back_to_specCode_when_RefNo_is_null()
    {
        var name = SpecExportFilename.SheetPdf(null, "SPEC-ABC", "A", Frozen);
        Assert.Contains("SpecSheet_SPEC-ABC_RevA_", name);
    }

    [Fact]
    public void Sheet_filename_sanitises_slashes_spaces_and_special_chars()
    {
        var name = SpecExportFilename.SheetPdf("REF/2026 #042", "X", "Ω", Frozen);
        // Slash → underscore; space → underscore; # → underscore;
        // multiple consecutive underscores collapse to one; Greek omega →
        // underscore (non-ASCII Latin letter rejected per ASCII-safe rule).
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain(" ", name);
        Assert.DoesNotContain("#", name);
        Assert.DoesNotContain("__", name);
    }

    [Fact]
    public void Describe_filter_returns_null_for_default_args()
    {
        Assert.Null(SpecExportFilename.DescribeFilter(null, SpecListView.Active, null));
        Assert.Null(SpecExportFilename.DescribeFilter("", SpecListView.Active, ""));
    }

    [Fact]
    public void Describe_filter_composes_parts_with_separator()
    {
        var desc = SpecExportFilename.DescribeFilter("ARB", SpecListView.Trash, "FLEXO");
        Assert.NotNull(desc);
        Assert.Contains("search=\"ARB\"", desc);
        Assert.Contains("view=Trash", desc);
        Assert.Contains("planner=FLEXO", desc);
        Assert.Contains(" · ", desc);
    }

    [Fact]
    public void Describe_filter_uppercases_planner_token()
    {
        var desc = SpecExportFilename.DescribeFilter(null, SpecListView.Active, "flexo");
        Assert.Contains("planner=FLEXO", desc!);
    }

    [Fact]
    public void Sanitize_token_falls_back_to_spec_when_input_blank()
    {
        Assert.Equal("spec", SpecExportFilename.SanitizeFilenameToken(""));
        Assert.Equal("spec", SpecExportFilename.SanitizeFilenameToken("   "));
        Assert.Equal("spec", SpecExportFilename.SanitizeFilenameToken("///"));
    }

    [Theory]
    [InlineData("REF-A-001", "REF-A-001")]
    [InlineData("REF A 001", "REF_A_001")]
    [InlineData("a.b.c",     "a.b.c")]
    [InlineData("___foo___", "foo")]
    public void Sanitize_token_handles_common_shapes(string input, string expected)
    {
        Assert.Equal(expected, SpecExportFilename.SanitizeFilenameToken(input));
    }
}
