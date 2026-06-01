namespace CCL.MES.Application.SpecExport;

/// <summary>
/// Phase 8 PR #31c — Abstraction cho 3 exporter formats. Application layer
/// định nghĩa interface; Infrastructure impl xlsx + pdf (Word lib deps); CSV
/// impl ngay ở Application (pure .NET).
///
/// Caller (SpecsExportController) gọi từng impl theo format. Mỗi exporter
/// trả về byte[] để controller pipe ra FileStreamResult.
/// </summary>
public interface ISpecListExporter
{
    /// <summary>Format identifier — "csv" / "xlsx" / "pdf".</summary>
    string Format { get; }

    /// <summary>MIME content type cho HTTP response.</summary>
    string ContentType { get; }

    /// <summary>File extension (không dấu chấm).</summary>
    string FileExtension { get; }

    /// <summary>
    /// Build file bytes từ row collection. Caller đã filter/search rows trước
    /// khi gọi → exporter render đúng N rows nhận được.
    /// </summary>
    byte[] Export(IReadOnlyList<ProductRevisionListItem> rows, SpecExportContext context);
}

/// <summary>
/// Context cho exporter — i18n labels + filter description (hiển thị trên
/// header PDF/Excel) + culture (number/date format).
/// </summary>
public record SpecExportContext(
    string Title,
    string? FilterDescription,
    DateTime GeneratedAt,
    string GeneratedBy,
    System.Globalization.CultureInfo Culture);
