namespace CCL.MES.Application.SpecImport;

/// <summary>
/// Phase 8 PR #31a — Abstraction để Application layer KHÔNG phụ thuộc ClosedXML.
/// Infrastructure implement via ClosedXML.
///
/// PR #31b — 2 impl đăng ký: silkscreen + flexo. Caller (SpecImportService)
/// dispatch via <see cref="ISpecXlsxParserFactory"/> theo category. LETTER/INDIGO/
/// DIECUT chưa có dedicated parser (Q11) — fallback silkscreen + warning.
/// </summary>
public interface ISpecXlsxParser
{
    /// <summary>
    /// Đọc xlsx stream, parse theo planner category.
    /// Caller pass <paramref name="forcedCategory"/> để override auto-detect
    /// nếu operator chọn planner khác hơn header xlsx gợi ý.
    /// </summary>
    ParsedSpecDto Parse(Stream xlsxStream, string forcedCategory);
}

/// <summary>
/// PR #31b — Category-keyed factory để SpecImportService dispatch parser
/// đúng theo planner. Khi LETTER/INDIGO/DIECUT có dedicated parser sau này
/// chỉ cần register thêm impl + add case ở factory.
/// </summary>
public interface ISpecXlsxParserFactory
{
    /// <summary>
    /// Trả parser khớp category. Categories chưa support fallback silkscreen
    /// per Q11; caller có thể check trước bằng <see cref="IsSupported"/>.
    /// </summary>
    ISpecXlsxParser GetParser(string category);

    /// <summary>True nếu category có dedicated parser (silkscreen / flexo PR #31b).</summary>
    bool IsSupported(string category);
}
