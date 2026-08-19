using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// feat/iqc-module-tabs — <see cref="IqcGroup"/> whitelist + normalize.
/// Locks the additive contract: 4 canonical values, case-insensitive match,
/// empty/unknown → Materials (backward compat for the legacy Materials form
/// that does not declare a group).
/// </summary>
public sealed class IqcGroupTests
{
    [Theory]
    [InlineData("Materials", "Materials")]
    [InlineData("Chemical", "Chemical")]
    [InlineData("Tools", "Tools")]
    [InlineData("Other", "Other")]
    [InlineData("chemical", "Chemical")]      // case-insensitive → canonical
    [InlineData("  TOOLS ", "Tools")]         // trimmed + canonical
    public void Normalize_returns_canonical_for_valid(string raw, string expected)
        => Assert.Equal(expected, IqcGroup.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nonsense")]
    [InlineData("Material")]                   // near-miss, not an exact member
    public void Normalize_falls_back_to_materials(string? raw)
        => Assert.Equal(IqcGroup.Materials, IqcGroup.Normalize(raw));

    [Theory]
    [InlineData("Materials", true)]
    [InlineData("chemical", true)]
    [InlineData("Nonsense", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsValid_only_accepts_known_members(string? raw, bool expected)
        => Assert.Equal(expected, IqcGroup.IsValid(raw));

    [Fact]
    public void All_lists_exactly_four_canonical_groups()
    {
        Assert.Equal(new[] { "Materials", "Chemical", "Tools", "Other" }, IqcGroup.All);
    }

    [Fact]
    public void New_inspection_defaults_group_to_materials()
    {
        // Additive default at the entity level → legacy code paths that build
        // an IqcInspection without setting Group still land on Materials.
        Assert.Equal(IqcGroup.Materials, new IqcInspection().Group);
    }
}
