using CCL.MES.Application.Services;

namespace CCL.MES.Application.WorkOrderExport;

/// <summary>
/// Phase 8 PR #32c — Abstraction for WO list export formats. Mirror of
/// <c>ISpecListExporter</c> from PR #31c. Application layer defines
/// interface + CSV impl; Infrastructure ships XLSX (ClosedXML).
///
/// Each exporter receives the already-split Active + Closed lists and a
/// context object (title + culture + emit metadata) and returns the
/// full file bytes. Controller pipes bytes to <c>FileContentResult</c>.
/// </summary>
public interface IWorkOrderListExporter
{
    string Format { get; }
    string ContentType { get; }
    string FileExtension { get; }

    byte[] Export(
        IReadOnlyList<WorkOrderCardItem> active,
        IReadOnlyList<WorkOrderCardItem> closed,
        WoExportContext context);
}

public record WoExportContext(
    string Title,
    DateTime GeneratedAt,
    string GeneratedBy,
    System.Globalization.CultureInfo Culture);
