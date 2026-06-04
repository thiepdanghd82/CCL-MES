using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Tests.Localization;

/// <summary>
/// P10.6b — serialise/parse contract owned by the client lib so the
/// in-memory + MAUI Preferences impls cannot drift on what a
/// persisted string looks like.
///
/// The "by NAME not by int" rule (covered by guards 3 + 4) is what
/// keeps an operator's preference safe when a future PR adds a
/// third language and re-orders the enum values.
/// </summary>
public sealed class LanguageCodeNamesTests
{
    [Theory]
    [InlineData(LanguageCode.Vietnamese, "Vietnamese")]
    [InlineData(LanguageCode.English, "English")]
    public void ToPreferenceString_returns_enum_name(LanguageCode code, string expected)
    {
        Assert.Equal(expected, LanguageCodeNames.ToPreferenceString(code));
    }

    [Theory]
    [InlineData("Vietnamese", LanguageCode.Vietnamese)]
    [InlineData("English", LanguageCode.English)]
    [InlineData("english", LanguageCode.English)]   // case-insensitive
    [InlineData("VIETNAMESE", LanguageCode.Vietnamese)]
    public void FromPreferenceString_parses_known_names(string raw, LanguageCode expected)
    {
        Assert.Equal(expected, LanguageCodeNames.FromPreferenceString(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Klingon")]            // unknown
    [InlineData("0")]                  // int form must NOT round-trip
    [InlineData("1")]
    public void FromPreferenceString_falls_back_to_Vietnamese_on_garbage(string? raw)
    {
        Assert.Equal(LanguageCode.Vietnamese, LanguageCodeNames.FromPreferenceString(raw));
    }

    [Fact]
    public void Round_trip_through_string_form_preserves_value()
    {
        foreach (LanguageCode code in Enum.GetValues<LanguageCode>())
        {
            var raw = LanguageCodeNames.ToPreferenceString(code);
            var back = LanguageCodeNames.FromPreferenceString(raw);
            Assert.Equal(code, back);
        }
    }

    [Theory]
    [InlineData(LanguageCode.Vietnamese, "Tiếng Việt")]
    [InlineData(LanguageCode.English, "English")]
    public void LabelVi_returns_endonym(LanguageCode code, string expected)
    {
        Assert.Equal(expected, LanguageCodeNames.LabelVi(code));
    }
}
