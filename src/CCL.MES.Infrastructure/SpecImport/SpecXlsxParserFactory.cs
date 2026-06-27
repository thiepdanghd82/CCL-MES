using CCL.MES.Application.SpecImport;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// Phase 8 PR #31b — Category-keyed parser factory. Dispatch silkscreen vs
/// flexo theo category string.
/// P10.10 — indigo (HP) + letterpress (LP) now have a dedicated parser
/// (<see cref="IndigoLetterpressXlsxParser"/>); only DIECUT still falls back to
/// silkscreen layout.
/// </summary>
public class SpecXlsxParserFactory : ISpecXlsxParserFactory
{
    private readonly SilkscreenXlsxParser _silk;
    private readonly FlexoXlsxParser _flexo;
    private readonly IndigoLetterpressXlsxParser _indigoLetter;

    public SpecXlsxParserFactory(SilkscreenXlsxParser silk, FlexoXlsxParser flexo, IndigoLetterpressXlsxParser indigoLetter)
    {
        _silk = silk;
        _flexo = flexo;
        _indigoLetter = indigoLetter;
    }

    public ISpecXlsxParser GetParser(string category)
        => (category?.Trim().ToLowerInvariant()) switch
        {
            "flexo"       => _flexo,
            "indigo"      => _indigoLetter,  // HP Indigo seal template
            "letterpress" => _indigoLetter,  // LP letterpress template
            _             => _silk,          // silkscreen + diecut fallback
        };

    public bool IsSupported(string category)
        => string.Equals(category, "silkscreen", StringComparison.OrdinalIgnoreCase)
        || string.Equals(category, "flexo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(category, "indigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(category, "letterpress", StringComparison.OrdinalIgnoreCase);
}
