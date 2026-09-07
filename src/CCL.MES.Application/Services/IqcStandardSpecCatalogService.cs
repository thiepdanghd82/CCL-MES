using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Catalog tiêu chuẩn <c>CCL-SPEC-QC*</c> + import folder file Form gốc.
/// Không đụng schema — upsert header; hạng mục giữ seed P12 nếu đã có.
/// </summary>
public sealed class IqcStandardSpecCatalogService
{
    public const string DefaultEnvDir = "MES_IQC_STANDARD_SPEC_DIR";
    public const string ImportSourceTag = "iqc-std-folder-2026";

    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public IqcStandardSpecCatalogService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IqcStandardSpecCatalogPage> ListAsync(
        string? q, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var query = _db.IqcMaterialSpecs.AsNoTracking()
            .Where(s => s.SpecNo.StartsWith("CCL-SPEC-QC"));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(s =>
                s.SpecNo.Contains(t)
                || s.MaterialCode.Contains(t)
                || (s.SupplierName != null && s.SupplierName.Contains(t))
                || (s.MaterialCodeIfs != null && s.MaterialCodeIfs.Contains(t)));
        }

        var total = await query.CountAsync(ct);
        var specs = await query
            .OrderBy(s => s.SpecNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.SpecNo,
                s.MaterialCode,
                s.MaterialCodeIfs,
                s.SupplierName,
                s.Revision,
                Approval = s.Approval.ToString(),
                s.ImportSource,
                s.Active,
            })
            .ToListAsync(ct);

        var nos = specs.Select(s => s.SpecNo).ToList();
        var counts = await _db.IqcSpecItems.AsNoTracking()
            .Where(i => nos.Contains(i.SpecNo) && i.Active)
            .GroupBy(i => i.SpecNo)
            .Select(g => new { SpecNo = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.SpecNo, x => x.N, ct);

        return new IqcStandardSpecCatalogPage
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = specs.Select(s => new IqcStandardSpecCatalogRow
            {
                SpecNo = s.SpecNo,
                MaterialCode = s.MaterialCode,
                MaterialCodeIfs = s.MaterialCodeIfs,
                SupplierName = s.SupplierName,
                Revision = s.Revision,
                Approval = s.Approval,
                ImportSource = s.ImportSource,
                Active = s.Active,
                ItemCount = counts.TryGetValue(s.SpecNo, out var n) ? n : 0,
            }).ToList(),
        };
    }

    /// <summary>
    /// Upsert header từ danh sách file đã parse (Infrastructure reader).
    /// Spec đã Approved giữ MaterialCode; chỉ bổ sung Supplier/Revision trống.
    /// Spec mới → PendingQc + ImportSource folder.
    /// </summary>
    public async Task<IqcStandardSpecImportResult> ImportParsedAsync(
        IReadOnlyList<IqcStandardSpecParsedRow> rows,
        string actor, string role, string? folderPath,
        bool commit, CancellationToken ct = default)
    {
        var result = new IqcStandardSpecImportResult
        {
            FilesSeen = rows.Count,
            FolderPath = folderPath,
        };
        if (rows.Count == 0) return result;

        var nos = rows.Select(r => r.SpecNo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await _db.IqcMaterialSpecs
            .Where(s => nos.Contains(s.SpecNo))
            .ToDictionaryAsync(s => s.SpecNo, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SpecNo) || string.IsNullOrWhiteSpace(row.MaterialCode))
            {
                result.FilesSkipped++;
                continue;
            }

            if (existing.TryGetValue(row.SpecNo, out var spec))
            {
                var wouldTouch =
                    (string.IsNullOrWhiteSpace(spec.SupplierName) && !string.IsNullOrWhiteSpace(row.SupplierName))
                    || (string.IsNullOrWhiteSpace(spec.Revision) && !string.IsNullOrWhiteSpace(row.Revision))
                    || (string.IsNullOrWhiteSpace(spec.MaterialCodeIfs) && !string.IsNullOrWhiteSpace(row.MaterialCodeIfs));

                if (!wouldTouch)
                {
                    result.AlreadyPresent++;
                    continue;
                }

                if (!commit)
                {
                    result.Updated++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(spec.SupplierName) && !string.IsNullOrWhiteSpace(row.SupplierName))
                    spec.SupplierName = Trunc(row.SupplierName, 256);
                if (string.IsNullOrWhiteSpace(spec.Revision) && !string.IsNullOrWhiteSpace(row.Revision))
                    spec.Revision = Trunc(row.Revision, 16);
                if (string.IsNullOrWhiteSpace(spec.MaterialCodeIfs) && !string.IsNullOrWhiteSpace(row.MaterialCodeIfs))
                    spec.MaterialCodeIfs = Trunc(row.MaterialCodeIfs, 32);
                spec.UpdatedAt = DateTime.UtcNow;
                result.Updated++;
                continue;
            }

            if (!commit)
            {
                result.Inserted++;
                continue;
            }

            var neu = new IqcMaterialSpec
            {
                SpecNo = Trunc(row.SpecNo, 32)!,
                MaterialCode = Trunc(row.MaterialCode, 256)!,
                MaterialCodeIfs = Trunc(row.MaterialCodeIfs, 32),
                SupplierName = Trunc(row.SupplierName, 256),
                Revision = Trunc(row.Revision, 16),
                Approval = IqcSpecApproval.PendingQc,
                ImportSource = ImportSourceTag,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actor,
            };
            _db.IqcMaterialSpecs.Add(neu);
            existing[neu.SpecNo] = neu;
            result.Inserted++;
        }

        if (commit && (result.Inserted > 0 || result.Updated > 0))
        {
            await _db.SaveChangesAsync(ct);
            await _audit.EmitAsync(
                AuditAction.QcLibraryImport, actor, role,
                targetType: "IqcMaterialSpec",
                targetId: ImportSourceTag,
                detail: $"{{\"kind\":\"iqc_standard_spec_folder\",\"folder\":{JsonEsc(folderPath)},\"seen\":{result.FilesSeen},\"inserted\":{result.Inserted},\"updated\":{result.Updated}}}",
                source: "Api");
        }

        return result;
    }

    private static string? Trunc(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }

    private static string JsonEsc(string? s)
    {
        if (s is null) return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

public sealed class IqcStandardSpecCatalogPage
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<IqcStandardSpecCatalogRow> Items { get; init; } = new();
}

public sealed class IqcStandardSpecCatalogRow
{
    public string SpecNo { get; init; } = "";
    public string MaterialCode { get; init; } = "";
    public string? MaterialCodeIfs { get; init; }
    public string? SupplierName { get; init; }
    public string? Revision { get; init; }
    public string Approval { get; init; } = "";
    public string? ImportSource { get; init; }
    public bool Active { get; init; }
    public int ItemCount { get; init; }
}

public sealed class IqcStandardSpecParsedRow
{
    public string SpecNo { get; init; } = "";
    public string MaterialCode { get; init; } = "";
    public string? MaterialCodeIfs { get; init; }
    public string? Revision { get; init; }
    public string? SupplierName { get; init; }
    public string? FileName { get; init; }
}

public sealed class IqcStandardSpecImportResult
{
    public int FilesSeen { get; set; }
    public int FilesSkipped { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int AlreadyPresent { get; set; }
    public string? FolderPath { get; set; }
    public List<string> Warnings { get; set; } = new();
}
