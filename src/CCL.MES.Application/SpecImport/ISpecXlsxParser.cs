namespace CCL.MES.Application.SpecImport;

/// <summary>
/// Phase 8 PR #31a — Abstraction để Application layer KHÔNG phụ thuộc ClosedXML.
/// Infrastructure implement via ClosedXML (`SilkscreenXlsxParser`). Future flexo
/// parser PR #31b dùng cùng interface (caller chọn category trước khi gọi).
/// </summary>
public interface ISpecXlsxParser
{
    /// <summary>
    /// Đọc xlsx stream, parse theo planner category.
    /// Caller pass <paramref name="forcedCategory"/> = "silkscreen" cho PR #31a;
    /// "flexo" defer PR #31b; LETTER/INDIGO/DIECUT fallback silkscreen layout
    /// với warning per Q11.
    /// </summary>
    ParsedSpecDto Parse(Stream xlsxStream, string forcedCategory);
}
