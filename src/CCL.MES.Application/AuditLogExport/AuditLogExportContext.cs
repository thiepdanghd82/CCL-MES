using System.Globalization;

namespace CCL.MES.Application.AuditLogExport;

/// <summary>
/// Phase 9 audit-export — exporter context. Mirrors
/// <see cref="CCL.MES.Application.SpecExport.SpecExportContext"/>.
/// Carries i18n labels for the title + filter description and the
/// generation timestamp / actor for the audit header banner.
/// </summary>
public record AuditLogExportContext(
    string Title,
    string? FilterDescription,
    DateTime GeneratedAt,
    string GeneratedBy,
    CultureInfo Culture);
