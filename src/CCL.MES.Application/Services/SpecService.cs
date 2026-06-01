using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 8 PR #28 — REWRITTEN sau Spec → ProductRevision clean rewrite.
///
/// Public surface giữ nguyên signature cho EngineerSpec.razor + CreateSpecModal
/// đỡ phải cascade rộng:
///   - <see cref="SpecsAsync(string?, int, int)"/> trả về <see cref="PagedResult{T}"/>
///     của <see cref="ProductRevisionListItem"/> thay vì <c>Spec</c> entity.
///   - <see cref="CreateAsync"/> nhận <see cref="CreateSpecRequest"/> (DTO unchanged)
///     và lưu thành 1 <see cref="ProductRevision"/> (rev "A") + 1 <see cref="SpecPrint"/>
///     sibling với parameters folded vào ColorSpecJson.
///   - <see cref="ApproveAsync"/> chuyển vào <see cref="ProductRevision"/>.
///
/// RBAC NpiSpecRead unchanged. Audit codes SpecCreate + SpecApprove unchanged
/// (target id giờ là ProductRevision.Id thay vì SpecVersion.Id).
/// Mutation Revise/Copy/Trash/Restore + Drawing CRUD ship PR #30/#31/#32.
/// </summary>
public class SpecService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    public SpecService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Phase 8 PR #28 — paginated list cho EngineerSpec grid. Pre-flatten vào
    /// DTO để Razor binding clean + KHÔNG kéo full graph qua serializer.
    /// Search 5 field: SpecCode / Title / Product code / Product name / ProcessCode.
    /// </summary>
    public async Task<PagedResult<ProductRevisionListItem>> SpecsAsync(string? search, int page, int pageSize)
    {
        var q = _db.ProductRevisions
            .AsNoTracking()
            .Where(x => !x.IsTrashed)               // soft-delete skip (Trash UI ở PR #30)
            .Include(x => x.Product)
            .Include(x => x.Print)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => EF.Functions.Like(x.SpecCode, $"%{s}%")
                || EF.Functions.Like(x.Title, $"%{s}%")
                || (x.Product != null && EF.Functions.Like(x.Product.ProductCode, $"%{s}%"))
                || (x.Product != null && EF.Functions.Like(x.Product.Name, $"%{s}%"))
                || (x.Print != null && x.Print.ProcessCode != null && EF.Functions.Like(x.Print.ProcessCode, $"%{s}%")));
        }

        var ordered = q.OrderByDescending(x => x.Id);
        var paged = await PagingHelper.PageAsync(ordered, page, pageSize);

        var items = paged.Items
            .Select(x => new ProductRevisionListItem(
                x.Id,
                x.SpecCode,
                x.Title,
                x.RevisionCode,
                x.Status,
                x.EffectiveFrom,
                x.ApprovedBy,
                x.Product?.ProductCode ?? "",
                x.Product?.Name ?? "",
                x.Print?.ProcessCode))
            .ToList();

        return new PagedResult<ProductRevisionListItem>(items, paged.Total, paged.Page, paged.PageSize);
    }

    /// <summary>Phase 7 hạng mục 4 — Product dropdown cho CreateSpecModal.</summary>
    public Task<List<ProductDropdownItem>> ProductsForDropdownAsync() =>
        _db.Products
            .AsNoTracking()
            .OrderBy(p => p.ProductCode)
            .Select(p => new ProductDropdownItem(p.Id, p.ProductCode, p.Name))
            .ToListAsync();

    /// <summary>
    /// Phase 8 PR #28 — Create new spec → ProductRevision rev "A" + SpecPrint
    /// sibling. Parameters folded vào SpecPrint.ColorSpecJson (mirror DbSeeder
    /// shape). PR #29 sẽ thay form thành full editor 4 sibling tab.
    /// </summary>
    public async Task<ProductRevision> CreateAsync(CreateSpecRequest r, string? user)
    {
        var revision = new ProductRevision
        {
            ProductId = r.ProductId,
            SpecCode = r.SpecCode,
            Title = r.Title,
            RevisionCode = "A",
            Status = ProductRevisionStatus.Draft,
            Print = new SpecPrint
            {
                ProcessCode = string.IsNullOrWhiteSpace(r.ProcessCode) ? "SILKSCREEN" : r.ProcessCode,
                NumColors = 0,
                ColorSpecJson = SerializeParams(r.Parameters)
            }
        };
        _db.ProductRevisions.Add(revision);
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecCreate, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: revision.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                spec_code = r.SpecCode,
                title = r.Title,
                product_id = r.ProductId,
                revision_code = revision.RevisionCode,
                process_code = revision.Print?.ProcessCode,
                param_count = r.Parameters.Count,
            }));
        return revision;
    }

    /// <summary>Approve hiện rev → Status=Approved + ApprovedBy/At + EffectiveFrom=now.</summary>
    public async Task<ProductRevision?> ApproveAsync(long revisionId, string? user)
    {
        var rev = await _db.ProductRevisions.FirstOrDefaultAsync(x => x.Id == revisionId);
        if (rev is null) return null;
        rev.Status = ProductRevisionStatus.Approved;
        rev.ApprovedBy = user;
        rev.ApprovedAt = DateTime.UtcNow;
        rev.EffectiveFrom ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecApprove, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: rev.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                spec_code = rev.SpecCode,
                revision_code = rev.RevisionCode,
                product_id = rev.ProductId,
            }));
        return rev;
    }

    private static string SerializeParams(List<SpecParamDto> parameters)
    {
        if (parameters.Count == 0) return "[]";
        var arr = parameters.Select(p => new
        {
            param_name = p.ParamName,
            nominal = p.Nominal,
            tol_min = p.TolMin,
            tol_max = p.TolMax,
            uom = p.Uom,
            is_critical = p.IsCritical
        });
        return JsonSerializer.Serialize(arr);
    }
}
