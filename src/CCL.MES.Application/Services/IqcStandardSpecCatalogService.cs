using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Catalog tiêu chuẩn <c>CCL-SPEC-QC*</c> + import folder file Form gốc
/// (header + hạng mục Acceptance/Method). Không đụng schema.
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
    /// Upsert header + hạng mục từ Form. Spec đã Approved giữ MaterialCode;
    /// hạng mục ghi đè Acceptance/Method theo Form (nút Import = nguồn Form).
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

        var existingItems = await _db.IqcSpecItems
            .Where(i => nos.Contains(i.SpecNo))
            .ToDictionaryAsync(
                i => (i.SpecNo.ToUpperInvariant(), i.ItemId.ToUpperInvariant(), i.Seq),
                i => i, ct);

        var now = DateTime.UtcNow;
        var headerTouched = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SpecNo) || string.IsNullOrWhiteSpace(row.MaterialCode))
            {
                result.FilesSkipped++;
                continue;
            }

            IqcMaterialSpec spec;
            if (existing.TryGetValue(row.SpecNo, out var found))
            {
                spec = found;
                var headerChanged = false;
                if (commit)
                {
                    if (!string.IsNullOrWhiteSpace(row.SupplierName)
                        && !string.Equals(spec.SupplierName, row.SupplierName, StringComparison.Ordinal))
                    { spec.SupplierName = Trunc(row.SupplierName, 256); headerChanged = true; }
                    if (!string.IsNullOrWhiteSpace(row.Revision)
                        && string.IsNullOrWhiteSpace(spec.Revision))
                    { spec.Revision = Trunc(row.Revision, 16); headerChanged = true; }
                    if (!string.IsNullOrWhiteSpace(row.MaterialCodeIfs)
                        && string.IsNullOrWhiteSpace(spec.MaterialCodeIfs))
                    { spec.MaterialCodeIfs = Trunc(row.MaterialCodeIfs, 32); headerChanged = true; }
                    if (!string.Equals(spec.ImportSource, ImportSourceTag, StringComparison.Ordinal))
                    { spec.ImportSource = ImportSourceTag; headerChanged = true; }
                    if (headerChanged || row.Items.Count > 0)
                    {
                        spec.UpdatedAt = now;
                        spec.UpdatedBy = actor;
                    }
                }
                else
                {
                    headerChanged =
                        (!string.IsNullOrWhiteSpace(row.SupplierName)
                         && !string.Equals(spec.SupplierName, row.SupplierName, StringComparison.Ordinal))
                        || (!string.IsNullOrWhiteSpace(row.Revision) && string.IsNullOrWhiteSpace(spec.Revision))
                        || (!string.IsNullOrWhiteSpace(row.MaterialCodeIfs) && string.IsNullOrWhiteSpace(spec.MaterialCodeIfs))
                        || !string.Equals(spec.ImportSource, ImportSourceTag, StringComparison.Ordinal);
                }

                if (headerChanged || row.Items.Count > 0)
                {
                    result.Updated++;
                    headerTouched++;
                }
                else
                    result.AlreadyPresent++;
            }
            else
            {
                if (!commit)
                {
                    result.Inserted++;
                    headerTouched++;
                    UpsertItemsDry(row, result);
                    continue;
                }

                spec = new IqcMaterialSpec
                {
                    SpecNo = Trunc(row.SpecNo, 32)!,
                    MaterialCode = Trunc(row.MaterialCode, 256)!,
                    MaterialCodeIfs = Trunc(row.MaterialCodeIfs, 32),
                    SupplierName = Trunc(row.SupplierName, 256),
                    Revision = Trunc(row.Revision, 16),
                    Approval = IqcSpecApproval.PendingQc,
                    ImportSource = ImportSourceTag,
                    Active = true,
                    CreatedAt = now,
                    CreatedBy = actor,
                };
                _db.IqcMaterialSpecs.Add(spec);
                existing[spec.SpecNo] = spec;
                result.Inserted++;
                headerTouched++;
            }

            UpsertItems(row, existingItems, actor, now, commit, result);
        }

        if (commit && (headerTouched > 0 || result.ItemsInserted > 0 || result.ItemsUpdated > 0))
        {
            await _db.SaveChangesAsync(ct);
            await _audit.EmitAsync(
                AuditAction.QcLibraryImport, actor, role,
                targetType: "IqcMaterialSpec",
                targetId: ImportSourceTag,
                detail: $"{{\"kind\":\"iqc_standard_spec_folder\",\"folder\":{JsonEsc(folderPath)},\"seen\":{result.FilesSeen},\"inserted\":{result.Inserted},\"updated\":{result.Updated},\"itemsIns\":{result.ItemsInserted},\"itemsUpd\":{result.ItemsUpdated}}}",
                source: "Api");
        }

        return result;
    }

    private static void UpsertItemsDry(IqcStandardSpecParsedRow row, IqcStandardSpecImportResult result)
    {
        foreach (var _ in row.Items)
            result.ItemsInserted++;
    }

    private void UpsertItems(
        IqcStandardSpecParsedRow row,
        Dictionary<(string, string, int), IqcSpecItem> existingItems,
        string actor, DateTime now, bool commit,
        IqcStandardSpecImportResult result)
    {
        foreach (var it in row.Items)
        {
            if (string.IsNullOrWhiteSpace(it.ItemId)) continue;
            var key = (row.SpecNo.ToUpperInvariant(), it.ItemId.ToUpperInvariant(), it.Seq);
            var acc = Trunc(it.AcceptanceVi, 1024);
            var method = Trunc(it.MethodVi, 512);
            var lim = IqcSpecLimitParser.Parse(acc);

            if (existingItems.TryGetValue(key, out var item))
            {
                if (!commit)
                {
                    if (WouldChangeItem(item, acc, method, lim)) result.ItemsUpdated++;
                    continue;
                }

                if (ApplyItem(item, acc, method, lim))
                {
                    item.UpdatedAt = now;
                    item.UpdatedBy = actor;
                    item.Active = true;
                    result.ItemsUpdated++;
                }
            }
            else
            {
                if (!commit)
                {
                    result.ItemsInserted++;
                    continue;
                }

                item = new IqcSpecItem
                {
                    SpecNo = Trunc(row.SpecNo, 32)!,
                    ItemId = Trunc(it.ItemId, 16)!,
                    Seq = Math.Max(1, it.Seq),
                    Active = true,
                    CreatedAt = now,
                    CreatedBy = actor,
                };
                ApplyItem(item, acc, method, lim);
                _db.IqcSpecItems.Add(item);
                existingItems[key] = item;
                result.ItemsInserted++;
            }
        }
    }

    private static bool WouldChangeItem(IqcSpecItem e, string? acc, string? method, IqcSpecLimit? lim)
    {
        if (!string.Equals(e.AcceptanceVi, acc, StringComparison.Ordinal)) return true;
        if (!string.IsNullOrWhiteSpace(method)
            && !string.Equals(e.MethodVi, method, StringComparison.Ordinal)) return true;
        if (lim is { } L)
        {
            var nominal = L.Nominal ?? IqcSpecLimitParser.ParseBareNominal(acc);
            if (!e.LimitParsed
                || e.LimitLow != L.Low || e.LimitUp != L.Up
                || e.LimitNominal != nominal)
                return true;
        }
        return false;
    }

    private static bool ApplyItem(IqcSpecItem e, string? acc, string? method, IqcSpecLimit? lim)
    {
        var ch = false;
        void S<T>(T cur, T next, Action<T> set)
        {
            if (!EqualityComparer<T>.Default.Equals(cur, next)) { set(next); ch = true; }
        }

        S(e.AcceptanceVi, acc, v => e.AcceptanceVi = v);
        if (!string.IsNullOrWhiteSpace(method))
            S(e.MethodVi, method, v => e.MethodVi = v);

        S(e.LimitLow, lim?.Low, v => e.LimitLow = v);
        S(e.LimitUp, lim?.Up, v => e.LimitUp = v);
        var nominal = lim?.Nominal ?? IqcSpecLimitParser.ParseBareNominal(acc);
        S(e.LimitNominal, nominal, v => e.LimitNominal = v);
        S(e.LimitUnit, lim?.Unit, v => e.LimitUnit = v);
        S(e.LimitLabel, lim?.Label, v => e.LimitLabel = v);
        S(e.TearIsPass, lim?.TearIsPass ?? false, v => e.TearIsPass = v);
        S(e.LimitParsed, lim is not null, v => e.LimitParsed = v);
        return ch;
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

public sealed class IqcStandardSpecParsedItem
{
    public string ItemId { get; init; } = "";
    public int Seq { get; init; } = 1;
    public string? AcceptanceVi { get; init; }
    public string? MethodVi { get; init; }
}

public sealed class IqcStandardSpecParsedRow
{
    public string SpecNo { get; init; } = "";
    public string MaterialCode { get; init; } = "";
    public string? MaterialCodeIfs { get; init; }
    public string? Revision { get; init; }
    public string? SupplierName { get; init; }
    public string? FileName { get; init; }
    public List<IqcStandardSpecParsedItem> Items { get; init; } = new();
}

public sealed class IqcStandardSpecImportResult
{
    public int FilesSeen { get; set; }
    public int FilesSkipped { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int AlreadyPresent { get; set; }
    public int ItemsInserted { get; set; }
    public int ItemsUpdated { get; set; }
    public string? FolderPath { get; set; }
    public List<string> Warnings { get; set; } = new();
}
