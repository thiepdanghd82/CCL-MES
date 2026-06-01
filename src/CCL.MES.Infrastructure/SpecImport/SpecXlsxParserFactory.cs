using CCL.MES.Application.SpecImport;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// Phase 8 PR #31b — Category-keyed parser factory. Dispatch silkscreen vs
/// flexo theo category string; default fallback silkscreen per Q11
/// (LETTER/INDIGO/DIECUT chưa có dedicated parser — caller surface warning).
/// </summary>
public class SpecXlsxParserFactory : ISpecXlsxParserFactory
{
    private readonly SilkscreenXlsxParser _silk;
    private readonly FlexoXlsxParser _flexo;

    public SpecXlsxParserFactory(SilkscreenXlsxParser silk, FlexoXlsxParser flexo)
    {
        _silk = silk;
        _flexo = flexo;
    }

    public ISpecXlsxParser GetParser(string category)
        => (category?.Trim().ToLowerInvariant()) switch
        {
            "flexo" => _flexo,
            _       => _silk,  // silkscreen + letterpress/indigo/diecut fallback
        };

    public bool IsSupported(string category)
        => string.Equals(category, "silkscreen", StringComparison.OrdinalIgnoreCase)
        || string.Equals(category, "flexo", StringComparison.OrdinalIgnoreCase);
}
