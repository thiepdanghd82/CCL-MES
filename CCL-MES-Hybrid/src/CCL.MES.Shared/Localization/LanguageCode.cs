namespace CCL.MES.Shared.Localization;

/// <summary>
/// P10.6b — operator-selectable UI language. Two values today
/// matching the SpecHub baseline; the resx infrastructure that
/// actually swaps strings ships in a follow-up PR (Q12 i18n inline VN
/// is the current state — every Razor page hard-codes Vietnamese).
///
/// Persisted as the enum NAME (not the int) so a future re-ordering
/// of the values doesn't silently flip an operator's preference.
/// Stored under MAUI Preferences key
/// <c>cclmes.hybrid.language.v1</c>.
/// </summary>
public enum LanguageCode
{
    /// <summary>Tiếng Việt — the default + only fully-translated
    /// language as of P10.6b.</summary>
    Vietnamese = 0,

    /// <summary>English — persisted as an operator preference now;
    /// actual UI string switching ships when the resx layer lands.
    /// The Appearance page surfaces this with a "Bản dịch tiếng Anh
    /// sẽ áp dụng sau khi cập nhật" disclaimer so operators know
    /// their choice is recorded but the swap is not yet live.</summary>
    English = 1,
}
