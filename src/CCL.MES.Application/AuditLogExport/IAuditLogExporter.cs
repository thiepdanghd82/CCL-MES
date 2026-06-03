using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.AuditLogExport;

/// <summary>
/// Phase 9 audit-export — list exporter abstraction parallel to
/// <see cref="CCL.MES.Application.SpecExport.ISpecListExporter"/>.
/// Application layer defines the interface; CSV impl lives here (pure
/// .NET), XLSX impl in Infrastructure (ClosedXML reuse PR #31a).
///
/// <para>
/// Caller (AuditLogExportController) pulls filtered rows from
/// <c>AuditLogService.ListForExportAsync</c> and dispatches by format.
/// </para>
/// </summary>
public interface IAuditLogExporter
{
    /// <summary>Format identifier — "csv" / "xlsx".</summary>
    string Format { get; }

    /// <summary>MIME content type for the HTTP response.</summary>
    string ContentType { get; }

    /// <summary>File extension (no leading dot).</summary>
    string FileExtension { get; }

    /// <summary>
    /// Build file bytes from a row collection. The caller already
    /// applied filters; the exporter renders exactly N rows received.
    /// </summary>
    byte[] Export(IReadOnlyList<AuditLog> rows, AuditLogExportContext context);
}
