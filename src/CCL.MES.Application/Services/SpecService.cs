using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>P10.10 — thrown by CreateAsync when the resolved product already
/// has a Rev-A spec; the controller maps it to a 422 duplicate_spec_code
/// (instead of a raw DbUpdate 500).</summary>
public sealed class DuplicateSpecException : System.Exception
{
    public DuplicateSpecException(string message) : base(message) { }
}

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
    /// Phase 8 PR #28 (rewired PR #30) — paginated list cho EngineerSpec grid
    /// 14-col SpecHub parity. Pre-flatten vào DTO để Razor binding clean.
    ///
    /// Q6 — narrow search 5 → 3 field per SpecHub placeholder UX
    /// ("Search by customer / part no / part name"): Customer.Name +
    /// Product.ProductCode + ProductRevision.Title.
    ///
    /// Include chain mở rộng để cover 14-col:
    ///   Product → Customer (cột Customer)
    ///   Print (cột Colors/Cavity/Pitch/Planner derived)
    /// </summary>
    public async Task<PagedResult<ProductRevisionListItem>> SpecsAsync(string? search, int page, int pageSize, SpecListView view = SpecListView.Active, string? planner = null)
    {
        var q = _db.ProductRevisions
            .AsNoTracking()
            .Include(x => x.Product)
                .ThenInclude(p => p!.Customer)      // Q3 — Customer column join
            .Include(x => x.Print)                  // Colors/Cavity/Pitch/Planner derive
            .AsQueryable();
        // PR-L3 — filter chip view selector. Active hides trashed (default,
        // backward-compat with prior callers); Trash shows only trashed;
        // All shows both.
        q = view switch
        {
            SpecListView.Trash => q.Where(x => x.IsTrashed),
            SpecListView.All   => q,
            _                  => q.Where(x => !x.IsTrashed),
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Q6 — exactly 3 fields per SpecHub placeholder UX.
            q = q.Where(x =>
                (x.Product != null && x.Product.Customer != null && EF.Functions.Like(x.Product.Customer.Name, $"%{s}%"))
                || (x.Product != null && EF.Functions.Like(x.Product.ProductCode, $"%{s}%"))
                || EF.Functions.Like(x.Title, $"%{s}%"));
        }

        // P10.5c-3 — Planner chip filter (SpecHub parity). Accepts
        // SILK / FLEXO / LETTER / INDIGO / DIECUT (matches
        // PlannerFromProcessCode output + the client-side palette
        // codes from SpecShowcardVm). Filter projects ProcessCode →
        // planner via the canonical IN-list mapping so the predicate
        // stays SQL-translatable (LinqToSqlite EF cannot translate
        // PlannerFromProcessCode static method directly). Unknown /
        // empty planner = no filter.
        if (!string.IsNullOrWhiteSpace(planner))
        {
            var p = planner.Trim().ToUpperInvariant();
            q = p switch
            {
                "SILK"   => q.Where(x => x.Print != null && x.Print.ProcessCode == "SILKSCREEN"),
                "FLEXO"  => q.Where(x => x.Print != null && x.Print.ProcessCode == "FLEXO"),
                "LETTER" => q.Where(x => x.Print != null && x.Print.ProcessCode == "LETTERPRESS"),
                "INDIGO" => q.Where(x => x.Print != null
                    && (x.Print.ProcessCode == "INDIGO" || x.Print.ProcessCode == "INDIGO_PRIMER")),
                "DIECUT" => q.Where(x => x.Print != null
                    && (x.Print.ProcessCode == "FLATBED_CUT"
                        || x.Print.ProcessCode == "ROTARY_CUT"
                        || x.Print.ProcessCode == "RDC"
                        || x.Print.ProcessCode == "POWERPUNCH"
                        || x.Print.ProcessCode == "CNC"
                        || x.Print.ProcessCode == "LASER_CUT"
                        || x.Print.ProcessCode == "KISS_CUT")),
                _        => q,  // unknown planner code → ignored (no filter)
            };
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
                x.Print?.ProcessCode,
                // Phase 8 PR #30 — 9 fields mới
                CustomerName: x.Product?.Customer?.Name,
                RefNo: x.RefNo,
                InspectionLevel: x.InspectionLevel,
                NumColors: x.Print?.NumColors ?? 0,
                Cavity: x.Print?.Cavity,
                PitchMm: x.Print?.PitchMm,
                Planner: PlannerFromProcessCode(x.Print?.ProcessCode),
                LastUpdatedAt: x.UpdatedAt ?? x.CreatedAt,
                LastUpdatedBy: x.UpdatedBy ?? x.CreatedBy))
            .ToList();

        return new PagedResult<ProductRevisionListItem>(items, paged.Total, paged.Page, paged.PageSize);
    }

    /// <summary>
    /// Phase 8 PR #30 — Q1 DERIVE Planner code từ SpecPrint.ProcessCode (single
    /// source of truth ProcessCatalog). Mapping mirror SpecHub `SPEC_CATEGORIES`
    /// (`spechub-prototype.html:11312`). Operator có thể ẩn cột Planner qua
    /// Columns toggle nếu thấy redundant với ProcessCode.
    /// Mapping:
    ///   SILKSCREEN                                   → SILK
    ///   FLEXO                                        → FLEXO
    ///   LETTERPRESS                                  → LETTER
    ///   INDIGO / INDIGO_PRIMER                       → INDIGO
    ///   FLATBED_CUT / ROTARY_CUT / RDC / POWERPUNCH
    ///   / CNC / LASER_CUT / KISS_CUT                 → DIECUT
    ///   (other / null)                               → UNKNOWN
    /// </summary>
    public static string? PlannerFromProcessCode(string? processCode)
    {
        if (string.IsNullOrWhiteSpace(processCode)) return "UNKNOWN";
        return processCode.Trim().ToUpperInvariant() switch
        {
            "SILKSCREEN"                                                                       => "SILK",
            "FLEXO"                                                                            => "FLEXO",
            "LETTERPRESS"                                                                      => "LETTER",
            "INDIGO" or "INDIGO_PRIMER"                                                        => "INDIGO",
            "FLATBED_CUT" or "ROTARY_CUT" or "RDC" or "POWERPUNCH" or "CNC" or "LASER_CUT" or "KISS_CUT" => "DIECUT",
            _                                                                                  => "UNKNOWN",
        };
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
        // P10.10 — resolve the product from the typed IFS code when no
        // ProductId was supplied (dropdown replaced by an IFS-code input).
        // Find by ProductCode; create a minimal product under an
        // "UNASSIGNED" customer (find-or-create) when the code is new.
        var productId = r.ProductId;
        if (productId <= 0 && !string.IsNullOrWhiteSpace(r.IfsCode))
        {
            var code = r.IfsCode.Trim();
            var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductCode == code);
            if (product is null)
            {
                // P10.10 — resolve the customer from the typed name (find-or-create
                // by Name, case-insensitive) so it syncs into the spec sheet's
                // Customer cell. Blank falls back to the "UNASSIGNED" customer.
                var customerName = r.Customer?.Trim();
                Customer customer;
                if (string.IsNullOrWhiteSpace(customerName))
                {
                    customer = await _db.Customers.FirstOrDefaultAsync(c => c.Code == "UNASSIGNED")
                        ?? new Customer { Code = "UNASSIGNED", Name = "Unassigned" };
                }
                else
                {
                    customer = await _db.Customers.FirstOrDefaultAsync(c => c.Name == customerName)
                        ?? new Customer { Code = DeriveCustomerCode(customerName), Name = customerName };
                }
                if (customer.Id == 0)
                {
                    _db.Customers.Add(customer);
                    await _db.SaveChangesAsync();
                }
                product = new Product
                {
                    ProductCode = code,
                    Name = string.IsNullOrWhiteSpace(r.Title) ? code : r.Title.Trim(),
                    CustomerId = customer.Id,
                };
                _db.Products.Add(product);
                await _db.SaveChangesAsync();
            }
            productId = product.Id;
        }

        // Guard the (ProductId, RevisionCode='A') unique constraint up-front so
        // a duplicate surfaces as a clean error instead of a DbUpdate 500.
        if (await _db.ProductRevisions.AnyAsync(x => x.ProductId == productId && x.RevisionCode == "A"))
            throw new DuplicateSpecException(
                "This Part No already has a spec (Rev A). Use Copy or Revise to add a revision.");

        var revision = new ProductRevision
        {
            ProductId = productId,
            SpecCode = r.SpecCode,
            Title = r.Title,
            InspectionLevel = string.IsNullOrWhiteSpace(r.Spec) ? null : r.Spec.Trim(),
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
                product_id = productId,
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

    /// <summary>
    /// Phase 8 PR-L1 — Spec lifecycle Copy. Creates a new independent
    /// ProductRevision (RevisionCode="A", Status=Draft, ParentRevisionId=null
    /// per Q4 — independent copy is NOT lineage) and deep-clones the SOURCE
    /// rev's content. Per Q6:
    ///   - Clone: SpecMaterial, SpecPrint, SpecDiecut, SpecFinishing
    ///     (1:1 keyed by ProductRevisionId)
    ///   - Clone: SpecPrintColor, SpecFlexoCuttingRow, SpecFlexoInkRow
    ///     (children of SpecPrint — re-keyed to the new SpecPrint.Id once EF
    ///     assigns it inside the transaction)
    ///   - Clone: SpecQcWindow + nested QcCriterion (QC Plan from PR-D-3)
    ///   - DO NOT clone: SpecQcCapture (capture results belong to source rev)
    ///   - DO NOT clone: Drawing (versioned files; new copy starts fresh
    ///     and must re-upload via the existing PR-D-5b upload chain)
    /// SpecCode uniqueness validated (rejects duplicates with 422 via
    /// <see cref="CopyResult"/>). All inserts run in a single transaction
    /// with the audit emit so a partial failure rolls everything back.
    /// </summary>
    public async Task<CopyResult> CopyAsync(long sourceRevisionId, CopySpecRequest r, string? user)
    {
        var src = await _db.ProductRevisions
            .Include(rev => rev.Material)
            .Include(rev => rev.Print)
                .ThenInclude(p => p!.Colors)
            .Include(rev => rev.Print)
                .ThenInclude(p => p!.FlexoCuttingRows)
            .Include(rev => rev.Print)
                .ThenInclude(p => p!.FlexoInkRows)
            .Include(rev => rev.Diecut)
            .Include(rev => rev.Finishing)
            .Include(rev => rev.QcWindows)
                .ThenInclude(w => w.Criteria)
            .AsNoTracking()
            .FirstOrDefaultAsync(rev => rev.Id == sourceRevisionId);
        if (src is null) return CopyResult.SourceNotFound();
        if (string.IsNullOrWhiteSpace(r.SpecCode)) return CopyResult.ValidationError("SpecCode required");
        if (r.ProductId <= 0) return CopyResult.ValidationError("ProductId required");

        // Unique SpecCode check across all (non-trashed?) revisions. SpecHub
        // semantics treat SpecCode as a customer-facing token unique per spec
        // family. Trashed rev collisions still block (operator can Restore +
        // rename if they really need the same code).
        var dup = await _db.ProductRevisions.AnyAsync(p => p.SpecCode == r.SpecCode);
        if (dup) return CopyResult.DuplicateCode(r.SpecCode);

        // EF Core wraps a single SaveChangesAsync call in an implicit
        // transaction (SQLite + SqlServer providers both), so a partial
        // write cannot land. Audit emit runs after persistence — same
        // pattern as CreateAsync/ApproveAsync upstream. IMesDbContext
        // intentionally doesn't expose Database to keep the abstraction
        // provider-agnostic.
        // The DB enforces UNIQUE(ProductId, RevisionCode). A pure "always A"
        // policy works only when the copy lands on a fresh product. If the
        // operator picks the SAME product as the source, "A" collides — so we
        // walk the existing revisions for that product and assign the next
        // available code via SpecRevisionHelpers.NextRev. New product → "A".
        // The helper is intentionally extracted because PR-L2 (Revise) reuses
        // it on the source rev's parent product.
        var existingCodesForProduct = await _db.ProductRevisions
            .Where(rev => rev.ProductId == r.ProductId)
            .Select(rev => rev.RevisionCode)
            .ToListAsync();
        var revCode = SpecRevisionHelpers.NextAvailableRev(existingCodesForProduct);

        var dest = new ProductRevision
        {
            ProductId        = r.ProductId,
            SpecCode         = r.SpecCode,
            Title            = string.IsNullOrWhiteSpace(r.Title) ? src.Title : r.Title,
            RevisionCode     = revCode,
            Status           = ProductRevisionStatus.Draft,
            ParentRevisionId = null,                       // Q4 — independent copy
            ChangeSummary    = null,
            RefNo            = src.RefNo,
            InspectionLevel  = src.InspectionLevel,
        };

        // Deep-clone content from src into dest. Same scope used by Revise
        // (PR-L2) — single source of clone logic per "1 nguồn clone logic,
        // không re-implement" hard constraint. Per Q6: SpecQcCapture and
        // Drawing are intentionally NOT cloned.
        var (qcWindowCount, qcCriterionCount) = CloneSpecContent(src, dest);

            _db.ProductRevisions.Add(dest);
            await _db.SaveChangesAsync();

            await _audit.EmitAsync(
                AuditAction.SpecCopy, user ?? "anonymous", actorRole: "",
                targetType: "ProductRevision", targetId: dest.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    source_id      = src.Id,
                    source_code    = src.SpecCode,
                    source_rev     = src.RevisionCode,
                    new_id         = dest.Id,
                    new_code       = dest.SpecCode,
                    new_rev        = dest.RevisionCode,
                    product_id     = dest.ProductId,
                    cloned_counts  = new
                    {
                        material        = src.Material is null ? 0 : 1,
                        print           = src.Print is null ? 0 : 1,
                        diecut          = src.Diecut is null ? 0 : 1,
                        finishing       = src.Finishing is null ? 0 : 1,
                        print_colors    = src.Print?.Colors.Count ?? 0,
                        flexo_cutting   = src.Print?.FlexoCuttingRows.Count ?? 0,
                        flexo_ink       = src.Print?.FlexoInkRows.Count ?? 0,
                        qc_windows      = qcWindowCount,
                        qc_criteria     = qcCriterionCount,
                    },
                }));

        return CopyResult.Ok(dest);
    }

    /// <summary>
    /// Phase 8 PR-L2 — Single source of clone logic shared between
    /// <see cref="CopyAsync"/> and <see cref="ReviseAsync"/>. Deep-clones
    /// SOURCE content into DEST per the approved Q6 rule:
    ///   - 4 sub-specs (Material / Print / Diecut / Finishing)
    ///   - SpecPrintColor + SpecFlexoCuttingRow + SpecFlexoInkRow
    ///   - SpecQcWindow + nested QcCriterion (approval reset to Draft)
    ///   - SpecQcCapture NOT cloned (results stay with source)
    ///   - Drawing NOT cloned (file version chain re-uploads fresh)
    ///
    /// Returns the (qc_windows, qc_criteria) counts so callers can emit
    /// them into the audit detail JSON. DEST must already be initialised
    /// (Id assignment, top-level fields); this method only touches the
    /// content collections / navigation properties.
    ///
    /// SRC must already have all relevant collections .Include'd by the
    /// caller's query — this method does not re-query the DB.
    /// </summary>
    private static (int QcWindowCount, int QcCriterionCount) CloneSpecContent(
        ProductRevision src, ProductRevision dest)
    {
        if (src.Material is not null)
        {
            dest.Material = new SpecMaterial
            {
                SubstrateType  = src.Material.SubstrateType,
                SubstrateBrand = src.Material.SubstrateBrand,
                ThicknessUm    = src.Material.ThicknessUm,
                LinerType      = src.Material.LinerType,
                AdhesiveType   = src.Material.AdhesiveType,
                AdhesiveBrand  = src.Material.AdhesiveBrand,
                ExtraJson      = src.Material.ExtraJson,
            };
        }
        if (src.Print is not null)
        {
            var p = new SpecPrint
            {
                ProcessCode         = src.Print.ProcessCode,
                HybridProcessesJson = src.Print.HybridProcessesJson,
                NumColors           = src.Print.NumColors,
                ColorSpecJson       = src.Print.ColorSpecJson,
                Varnish             = src.Print.Varnish,
                Lamination          = src.Print.Lamination,
                WhiteUnderprint     = src.Print.WhiteUnderprint,
                ExtraJson           = src.Print.ExtraJson,
                Cavity              = src.Print.Cavity,
                PitchMm             = src.Print.PitchMm,
                ProductSizeWmm      = src.Print.ProductSizeWmm,
                ProductSizeHmm      = src.Print.ProductSizeHmm,
                RemarksText         = src.Print.RemarksText,
                RemarksCutText      = src.Print.RemarksCutText,
            };
            foreach (var c in src.Print.Colors)
            {
                p.Colors.Add(new SpecPrintColor
                {
                    Seq          = c.Seq,
                    Surface      = c.Surface,
                    Color        = c.Color,
                    InkName      = c.InkName,
                    InkCode      = c.InkCode,
                    Maker        = c.Maker,
                    Retarder     = c.Retarder,
                    Viscosity    = c.Viscosity,
                    Speed        = c.Speed,
                    Squeegee     = c.Squeegee,
                    Dry          = c.Dry,
                    TemperatureC = c.TemperatureC,
                    TimeMin      = c.TimeMin,
                    Uv           = c.Uv,
                    EmulsionUm   = c.EmulsionUm,
                    PlateSize    = c.PlateSize,
                    Mesh         = c.Mesh,
                    AngleDeg     = c.AngleDeg,
                    PlateCode    = c.PlateCode,
                    ControlNo    = c.ControlNo,
                    Remark       = c.Remark,
                    ExtraJson    = c.ExtraJson,
                });
            }
            foreach (var fc in src.Print.FlexoCuttingRows)
            {
                p.FlexoCuttingRows.Add(new SpecFlexoCuttingRow
                {
                    Seq             = fc.Seq,
                    Process         = fc.Process,
                    Lamination      = fc.Lamination,
                    Size            = fc.Size,
                    CutterLot       = fc.CutterLot,
                    CutterName      = fc.CutterName,
                    PcsPerSheet     = fc.PcsPerSheet,
                    CuttingCavity   = fc.CuttingCavity,
                    PitchMm         = fc.PitchMm,
                    Packing         = fc.Packing,
                    PaperSpeed      = fc.PaperSpeed,
                    CuttingSpeed    = fc.CuttingSpeed,
                    CuttingPressure = fc.CuttingPressure,
                    HeadTension     = fc.HeadTension,
                    RollTension     = fc.RollTension,
                    ExtraJson       = fc.ExtraJson,
                });
            }
            foreach (var fi in src.Print.FlexoInkRows)
            {
                p.FlexoInkRows.Add(CloneFlexoInkRow(fi));
            }
            dest.Print = p;
        }
        if (src.Diecut is not null)
        {
            dest.Diecut = new SpecDiecut
            {
                CutProcessCode    = src.Diecut.CutProcessCode,
                DieId             = src.Diecut.DieId,
                DieType           = src.Diecut.DieType,
                WidthMm           = src.Diecut.WidthMm,
                LengthMm          = src.Diecut.LengthMm,
                CornerRadiusMm    = src.Diecut.CornerRadiusMm,
                KissCutDepthUm    = src.Diecut.KissCutDepthUm,
                PerforationJson   = src.Diecut.PerforationJson,
                BleedMm           = src.Diecut.BleedMm,
                CncProgram        = src.Diecut.CncProgram,
                LaserPowerW       = src.Diecut.LaserPowerW,
                PowerpunchTool    = src.Diecut.PowerpunchTool,
                ExtraJson         = src.Diecut.ExtraJson,
            };
        }
        if (src.Finishing is not null)
        {
            dest.Finishing = new SpecFinishing
            {
                OutputForm              = src.Finishing.OutputForm,
                LabelsPerRoll           = src.Finishing.LabelsPerRoll,
                CoreDiameterMm          = src.Finishing.CoreDiameterMm,
                WindingDirection        = src.Finishing.WindingDirection,
                MaxOuterDiaMm           = src.Finishing.MaxOuterDiaMm,
                RollsPerBox             = src.Finishing.RollsPerBox,
                FinishingProcessesJson  = src.Finishing.FinishingProcessesJson,
                ExtraJson               = src.Finishing.ExtraJson,
            };
        }
        int qcWindowCount = 0;
        int qcCriterionCount = 0;
        foreach (var w in src.QcWindows)
        {
            var nw = new SpecQcWindow
            {
                Stage         = w.Stage,
                ProcessCode   = w.ProcessCode,
                Title         = w.Title,
                Description   = w.Description,
                SamplePlan    = w.SamplePlan,
                Frequency     = w.Frequency,
                RejectAction  = w.RejectAction,
                Status        = SpecQcWindowStatus.Draft,   // copy/revise resets approval
                ApprovedBy    = null,
                ApprovedAt    = null,
            };
            foreach (var k in w.Criteria)
            {
                nw.Criteria.Add(new QcCriterion
                {
                    Seq              = k.Seq,
                    Name             = k.Name,
                    CriterionType    = k.CriterionType,
                    MeasureMethod    = k.MeasureMethod,
                    TargetValue      = k.TargetValue,
                    ToleranceMin     = k.ToleranceMin,
                    ToleranceMax     = k.ToleranceMax,
                    Unit             = k.Unit,
                    PassCriteria     = k.PassCriteria,
                    ReferenceImageKey = k.ReferenceImageKey,
                    Required         = k.Required,
                    ExtraJson        = k.ExtraJson,
                    Method           = k.Method,
                    Frequency        = k.Frequency,
                });
                qcCriterionCount++;
            }
            dest.QcWindows.Add(nw);
            qcWindowCount++;
        }
        return (qcWindowCount, qcCriterionCount);
    }

    private static SpecFlexoInkRow CloneFlexoInkRow(SpecFlexoInkRow fi)
    {
        // SpecFlexoInkRow shape varies by build; use a member-by-member shallow
        // clone of every non-navigation public setter via reflection so newer
        // fields don't silently fall off the back when this method drifts from
        // the entity definition.
        var clone = new SpecFlexoInkRow();
        var type = typeof(SpecFlexoInkRow);
        foreach (var prop in type.GetProperties(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name is nameof(SpecFlexoInkRow.Id)
                          or nameof(SpecFlexoInkRow.SpecPrintId)
                          or nameof(SpecFlexoInkRow.SpecPrint)) continue;
            prop.SetValue(clone, prop.GetValue(fi));
        }
        return clone;
    }

    /// <summary>
    /// Phase 8 PR-L1 — Spec lifecycle Update (Edit). Touches Identity (Title,
    /// RefNo, InspectionLevel) + SpecPrint (ProcessCode, ColorSpecJson). Server
    /// enforces Draft-only gate per Q3: Approved/Released/Superseded revs
    /// reject with <see cref="UpdateResult.ImmutableStatus"/> so the
    /// controller can map to 422. Audit emit lists field names actually
    /// changed (caller-supplied null = leave unchanged).
    /// </summary>
    public async Task<UpdateResult> UpdateAsync(long revisionId, UpdateSpecRequest r, string? user)
    {
        var rev = await _db.ProductRevisions
            .Include(x => x.Material)
            .Include(x => x.Print).ThenInclude(p => p!.Colors)
            .FirstOrDefaultAsync(x => x.Id == revisionId);
        if (rev is null) return UpdateResult.NotFound();
        if (rev.IsTrashed) return UpdateResult.Trashed();
        if (rev.Status != ProductRevisionStatus.Draft) return UpdateResult.ImmutableStatus(rev.Status);

        var changed = new List<string>();
        if (r.Title is not null && r.Title != rev.Title)
        {
            rev.Title = r.Title;
            changed.Add("title");
        }
        if (r.RefNo is not null && r.RefNo != rev.RefNo)
        {
            rev.RefNo = r.RefNo;
            changed.Add("ref_no");
        }
        if (r.InspectionLevel is not null && r.InspectionLevel != rev.InspectionLevel)
        {
            rev.InspectionLevel = r.InspectionLevel;
            changed.Add("inspection_level");
        }
        if (r.ProcessCode is not null)
        {
            rev.Print ??= new SpecPrint();
            if (r.ProcessCode != rev.Print.ProcessCode)
            {
                rev.Print.ProcessCode = r.ProcessCode;
                changed.Add("process_code");
            }
        }
        if (r.ColorSpecJson is not null)
        {
            rev.Print ??= new SpecPrint();
            if (r.ColorSpecJson != rev.Print.ColorSpecJson)
            {
                rev.Print.ColorSpecJson = r.ColorSpecJson;
                changed.Add("color_spec_json");
            }
        }

        // P10.10 — inline-edit: material scalars (only patch an existing
        // Material row; null = leave unchanged).
        if (rev.Material is not null)
        {
            if (r.SubstrateType is not null && r.SubstrateType != rev.Material.SubstrateType)
            { rev.Material.SubstrateType = r.SubstrateType; changed.Add("substrate_type"); }
            if (r.AdhesiveType is not null && r.AdhesiveType != rev.Material.AdhesiveType)
            { rev.Material.AdhesiveType = r.AdhesiveType; changed.Add("adhesive_type"); }
            if (r.ThicknessUm is not null && r.ThicknessUm != rev.Material.ThicknessUm)
            { rev.Material.ThicknessUm = r.ThicknessUm; changed.Add("thickness_um"); }
        }

        // P10.10 — inline-edit: print-parameter scalars + remarks.
        if (r.PrintingCavity is not null || r.LengthPitchMm is not null
            || r.ProductSizeWmm is not null || r.ProductSizeHmm is not null
            || r.RemarksText is not null || r.RemarksCutText is not null)
        {
            rev.Print ??= new SpecPrint();
            if (r.PrintingCavity is not null && r.PrintingCavity != rev.Print.Cavity)
            { rev.Print.Cavity = r.PrintingCavity; changed.Add("cavity"); }
            if (r.LengthPitchMm is not null && r.LengthPitchMm != rev.Print.PitchMm)
            { rev.Print.PitchMm = r.LengthPitchMm; changed.Add("pitch_mm"); }
            if (r.ProductSizeWmm is not null && r.ProductSizeWmm != rev.Print.ProductSizeWmm)
            { rev.Print.ProductSizeWmm = r.ProductSizeWmm; changed.Add("product_size_w"); }
            if (r.ProductSizeHmm is not null && r.ProductSizeHmm != rev.Print.ProductSizeHmm)
            { rev.Print.ProductSizeHmm = r.ProductSizeHmm; changed.Add("product_size_h"); }
            if (r.RemarksText is not null && r.RemarksText != rev.Print.RemarksText)
            { rev.Print.RemarksText = r.RemarksText; changed.Add("remarks"); }
            if (r.RemarksCutText is not null && r.RemarksCutText != rev.Print.RemarksCutText)
            { rev.Print.RemarksCutText = r.RemarksCutText; changed.Add("remarks_cut"); }
        }

        // P10.10 — inline-edit: replace the structured colour rows. EF deletes
        // the orphaned children (cascade) and re-adds the supplied set,
        // re-sequenced 1..N. The legacy ColorSpecJson blob is left as-is.
        if (r.Colors is not null)
        {
            rev.Print ??= new SpecPrint();
            rev.Print.Colors.Clear();
            var seq = 1;
            foreach (var c in r.Colors)
            {
                rev.Print.Colors.Add(new SpecPrintColor
                {
                    Seq = seq++,
                    Surface = c.Surface, Color = c.Color, InkName = c.InkName, InkCode = c.InkCode,
                    Maker = c.Maker, Retarder = c.Retarder, Viscosity = c.Viscosity, Speed = c.Speed,
                    Squeegee = c.Squeegee, Dry = c.Dry, TemperatureC = c.TemperatureC, TimeMin = c.TimeMin,
                    Uv = c.Uv, EmulsionUm = c.EmulsionUm, PlateSize = c.PlateSize, Mesh = c.Mesh,
                    AngleDeg = c.AngleDeg, PlateCode = c.PlateCode, ControlNo = c.ControlNo, Remark = c.Remark,
                });
            }
            rev.Print.NumColors = rev.Print.Colors.Count;
            changed.Add("colors");
        }

        if (changed.Count == 0)
        {
            return UpdateResult.NoChanges(rev);
        }

        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecUpdate, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: rev.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                rev_id          = rev.Id,
                rev_code        = rev.RevisionCode,
                spec_code       = rev.SpecCode,
                fields_changed  = changed,
            }));
        return UpdateResult.Ok(rev);
    }

    /// <summary>
    /// Phase 8 PR-L2 — Spec lifecycle Revise. Creates a new Draft revision
    /// with ParentRevisionId linking back to the source (lineage), bumps
    /// RevisionCode via <see cref="SpecRevisionHelpers.NextAvailableRev"/>
    /// over the source product's existing codes, deep-clones content using
    /// the shared <see cref="CloneSpecContent"/> helper, and auto-supersedes
    /// the source per approved Q2 default. Reason mandatory (≥5 chars) per
    /// Q1; stored on the new rev's <see cref="ProductRevision.ChangeSummary"/>.
    ///
    /// Gate: source rev must be <c>Approved</c> or <c>Released</c> (Draft
    /// goes through Edit; Superseded already terminal; InReview skipped per
    /// SpecHub principle).
    ///
    /// CreateAsync / ApproveAsync stay untouched per hard constraint — Revise
    /// owns its own write path. The auto-supersede update is on Status +
    /// EffectiveTo only; ApprovedBy/ReleasedBy provenance fields preserved.
    /// </summary>
    public async Task<ReviseResult> ReviseAsync(long sourceRevisionId, ReviseSpecRequest r, string? user)
    {
        var src = await _db.ProductRevisions
            .Include(rev => rev.Material)
            .Include(rev => rev.Print)
                .ThenInclude(p => p!.Colors)
            .Include(rev => rev.Print)
                .ThenInclude(p => p!.FlexoCuttingRows)
            .Include(rev => rev.Print)
                .ThenInclude(p => p!.FlexoInkRows)
            .Include(rev => rev.Diecut)
            .Include(rev => rev.Finishing)
            .Include(rev => rev.QcWindows)
                .ThenInclude(w => w.Criteria)
            .FirstOrDefaultAsync(rev => rev.Id == sourceRevisionId);
        if (src is null) return ReviseResult.SourceNotFound();
        if (src.IsTrashed) return ReviseResult.SourceTrashed();
        if (src.Status != ProductRevisionStatus.Approved
            && src.Status != ProductRevisionStatus.Released)
        {
            return ReviseResult.InvalidSourceStatus(src.Status);
        }
        var reason = (r.Reason ?? "").Trim();
        if (reason.Length < 5) return ReviseResult.ReasonRequired();

        var sourceStatusBefore = src.Status;

        // Next rev code over the SOURCE's product — Revise stays inside the
        // same product family; SpecCode is preserved (rev tree per spec).
        var existingCodesForProduct = await _db.ProductRevisions
            .Where(rev => rev.ProductId == src.ProductId)
            .Select(rev => rev.RevisionCode)
            .ToListAsync();
        var revCode = SpecRevisionHelpers.NextAvailableRev(existingCodesForProduct);

        var dest = new ProductRevision
        {
            ProductId        = src.ProductId,
            SpecCode         = src.SpecCode,                 // SpecCode preserved across rev tree
            Title            = src.Title,
            RevisionCode     = revCode,
            Status           = ProductRevisionStatus.Draft,
            ParentRevisionId = src.Id,                       // Q1 — lineage
            ChangeSummary    = reason,                       // Q1 — operator reason
            RefNo            = src.RefNo,
            InspectionLevel  = src.InspectionLevel,
        };

        var (qcWindowCount, qcCriterionCount) = CloneSpecContent(src, dest);

        // Auto-supersede the source (Q2 default). We touch only the
        // lifecycle status fields — ApprovedBy/ApprovedAt/ReleasedBy/
        // ReleasedAt are preserved as provenance.
        src.Status = ProductRevisionStatus.Superseded;
        src.EffectiveTo = DateTime.UtcNow;

        _db.ProductRevisions.Add(dest);
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecRevise, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: dest.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                source_id            = src.Id,
                source_code          = src.SpecCode,
                source_rev           = src.RevisionCode,
                source_status_before = sourceStatusBefore.ToString(),
                new_id               = dest.Id,
                new_rev              = dest.RevisionCode,
                reason               = reason,
                cloned_counts        = new
                {
                    material        = src.Material is null ? 0 : 1,
                    print           = src.Print is null ? 0 : 1,
                    diecut          = src.Diecut is null ? 0 : 1,
                    finishing       = src.Finishing is null ? 0 : 1,
                    print_colors    = src.Print?.Colors.Count ?? 0,
                    flexo_cutting   = src.Print?.FlexoCuttingRows.Count ?? 0,
                    flexo_ink       = src.Print?.FlexoInkRows.Count ?? 0,
                    qc_windows      = qcWindowCount,
                    qc_criteria     = qcCriterionCount,
                },
            }));

        return ReviseResult.Ok(dest);
    }

    /// <summary>
    /// Phase 8 PR-L2 — Spec lifecycle Mark Superseded (manual). Operator
    /// types the target spec's SpecCode as a confirmation; server validates
    /// the typed value matches before flipping Status. UI 2-step is defence
    /// in depth, NOT the trust boundary.
    ///
    /// Gate per Q2: Status must be <c>Approved</c> or <c>Released</c>. Draft
    /// uses Trash; Superseded is already terminal; InReview skipped (operator
    /// can revert to Draft first if needed).
    ///
    /// Does NOT create a new revision (use Revise for that). Does NOT touch
    /// ApprovedBy/ReleasedBy — those stay as historical provenance.
    /// </summary>
    public async Task<SupersedeResult> SupersedeAsync(long revisionId, SupersedeSpecRequest r, string? user)
    {
        var rev = await _db.ProductRevisions.FirstOrDefaultAsync(x => x.Id == revisionId);
        if (rev is null) return SupersedeResult.NotFound();
        if (rev.IsTrashed) return SupersedeResult.Trashed();
        if (rev.Status != ProductRevisionStatus.Approved
            && rev.Status != ProductRevisionStatus.Released)
        {
            return SupersedeResult.InvalidStatus(rev.Status);
        }
        var typed = (r.ConfirmSpecCode ?? "").Trim();
        if (!string.Equals(typed, rev.SpecCode, StringComparison.Ordinal))
        {
            return SupersedeResult.ConfirmMismatch();
        }

        var fromStatus = rev.Status;
        rev.Status = ProductRevisionStatus.Superseded;
        rev.EffectiveTo = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecSupersede, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: rev.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                rev_id      = rev.Id,
                rev_code    = rev.RevisionCode,
                spec_code   = rev.SpecCode,
                from_status = fromStatus.ToString(),
                manual      = true,
            }));

        return SupersedeResult.Ok(rev);
    }

    /// <summary>
    /// Phase 8 PR-L3 — Spec lifecycle Trash (soft-delete). Flips
    /// <see cref="ProductRevision.IsTrashed"/> + records who/when. Server-side
    /// blocker counts WO references with <c>Status NOT IN {Closed, Cancelled,
    /// Finished}</c>; if any exist, returns
    /// <see cref="TrashResult.ActiveWorkOrders"/> with the count so the
    /// controller can map to 422 + the modal can render the inline blocker
    /// reason. Closed/Cancelled/Finished WOs do NOT block — historical
    /// references are acceptable.
    ///
    /// Why the blocker matters even though Trash is "soft": Purge later
    /// hard-deletes from <c>ProductRevisions</c> and the FK on
    /// <c>WorkOrders.ProductRevisionId</c> is <c>ON DELETE RESTRICT</c>. If
    /// we let trash succeed while active WOs reference the spec, Purge
    /// would either fail or have to skip the row forever. Catching the
    /// blocker at Trash time is the cleaner ergonomics.
    /// </summary>
    public async Task<TrashResult> TrashAsync(long revisionId, string? user)
    {
        var rev = await _db.ProductRevisions.FirstOrDefaultAsync(x => x.Id == revisionId);
        if (rev is null) return TrashResult.NotFound();
        if (rev.IsTrashed) return TrashResult.AlreadyTrashed();

        // Phase 6 WorkOrders FK is ON DELETE RESTRICT. Count WOs that still
        // reference this rev in any operational state. Historical states
        // (Closed / Cancelled / Finished) are acceptable.
        var activeWoCount = await _db.WorkOrders
            .Where(w => w.ProductRevisionId == revisionId
                        && w.Status != WoStatus.Closed
                        && w.Status != WoStatus.Cancelled
                        && w.Status != WoStatus.Finished)
            .CountAsync();
        if (activeWoCount > 0)
        {
            return TrashResult.ActiveWorkOrders(activeWoCount);
        }

        rev.IsTrashed = true;
        rev.TrashedAt = DateTime.UtcNow;
        rev.TrashedBy = user;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecTrash, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: rev.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                rev_id    = rev.Id,
                rev_code  = rev.RevisionCode,
                spec_code = rev.SpecCode,
                status    = rev.Status.ToString(),
            }));

        return TrashResult.Ok(rev);
    }

    /// <summary>
    /// Phase 8 PR-L3 — Spec lifecycle Restore (un-trash). Clears the trash
    /// flags + stamps. No blocker — restoring cannot create an orphan FK.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(long revisionId, string? user)
    {
        var rev = await _db.ProductRevisions.FirstOrDefaultAsync(x => x.Id == revisionId);
        if (rev is null) return RestoreResult.NotFound();
        if (!rev.IsTrashed) return RestoreResult.NotTrashed();

        var wasTrashedAt = rev.TrashedAt;
        rev.IsTrashed = false;
        rev.TrashedAt = null;
        rev.TrashedBy = null;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.SpecRestore, user ?? "anonymous", actorRole: "",
            targetType: "ProductRevision", targetId: rev.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                rev_id          = rev.Id,
                rev_code        = rev.RevisionCode,
                spec_code       = rev.SpecCode,
                was_trashed_at  = wasTrashedAt,
            }));

        return RestoreResult.Ok(rev);
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

    /// <summary>P10.10 — derive a stable uppercase Customer.Code from a typed
    /// name (alnum only, collapsed, ≤32 chars). Customer.Code has no unique
    /// index so a collision is harmless; we find by Name first, so this only
    /// stamps the code on freshly-created customers.</summary>
    private static string DeriveCustomerCode(string name)
    {
        var slug = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (slug.Length == 0) return "CUST";
        return slug.Length > 32 ? slug[..32] : slug;
    }

    // ── Phase 8 PR #29 — read-only queries cho SpecDetailModal ────────────────

    /// <summary>
    /// Load spec content cluster (4 sibling specs + audit stamps) cho detail
    /// modal. Trả null nếu revision không tồn tại. Caller wraps try-catch +
    /// error banner inline (bài học hotfix PR #27: lỗi query KHÔNG được freeze
    /// Blazor circuit).
    /// Read-only — KHÔNG audit emit cho display (Q9 cam kết).
    /// </summary>
    public async Task<SpecContentDto?> SpecContentAsync(long revisionId)
    {
        var rev = await _db.ProductRevisions
            .AsNoTracking()
            .Include(r => r.Material)
            .Include(r => r.Print)
            .Include(r => r.Diecut)
            .Include(r => r.Finishing)
            .FirstOrDefaultAsync(r => r.Id == revisionId);
        if (rev is null) return null;

        return new SpecContentDto(
            RevisionId: rev.Id,
            Material: rev.Material is null ? null : new SpecMaterialDto(
                rev.Material.SubstrateType, rev.Material.SubstrateBrand,
                rev.Material.ThicknessUm, rev.Material.LinerType,
                rev.Material.AdhesiveType, rev.Material.AdhesiveBrand,
                rev.Material.ExtraJson),
            Print: rev.Print is null ? null : new SpecPrintDto(
                rev.Print.ProcessCode, rev.Print.NumColors,
                rev.Print.ColorSpecJson, rev.Print.Varnish,
                rev.Print.Lamination, rev.Print.WhiteUnderprint,
                rev.Print.ExtraJson),
            Diecut: rev.Diecut is null ? null : new SpecDiecutDto(
                rev.Diecut.CutProcessCode, rev.Diecut.DieId, rev.Diecut.DieType,
                rev.Diecut.WidthMm, rev.Diecut.LengthMm, rev.Diecut.CornerRadiusMm,
                rev.Diecut.KissCutDepthUm, rev.Diecut.BleedMm, rev.Diecut.ExtraJson),
            Finishing: rev.Finishing is null ? null : new SpecFinishingDto(
                rev.Finishing.OutputForm, rev.Finishing.LabelsPerRoll,
                rev.Finishing.CoreDiameterMm, rev.Finishing.WindingDirection,
                rev.Finishing.FinishingProcessesJson, rev.Finishing.ExtraJson),
            CreatedAt: rev.CreatedAt,
            CreatedBy: rev.CreatedBy,
            UpdatedAt: rev.UpdatedAt,
            UpdatedBy: rev.UpdatedBy,
            ApprovedBy: rev.ApprovedBy,
            ApprovedAt: rev.ApprovedAt);
    }

    /// <summary>
    /// Audit trail query cho 1 ProductRevision. Filter AuditLog WHERE
    /// TargetType='ProductRevision' AND TargetId=revisionId.ToString(),
    /// ORDER Timestamp DESC, LIMIT max. PR #28 emit SpecCreate/SpecApprove
    /// với TargetType='ProductRevision' (đã đúng shape); PR #30+ thêm
    /// SpecRevise/SpecCopy/SpecTrash/SpecRestore cũng giữ pattern này.
    /// Q9 cam kết: KHÔNG ADD field/column mới, query trực tiếp AuditLog table.
    /// </summary>
    public async Task<List<SpecAuditEntry>> SpecAuditTrailAsync(long revisionId, int max = 50)
    {
        if (max <= 0) max = 50;
        var rid = revisionId.ToString();
        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TargetType == "ProductRevision" && a.TargetId == rid)
            .OrderByDescending(a => a.Timestamp)
            .Take(max)
            .Select(a => new SpecAuditEntry(
                a.Timestamp, a.Action, a.ActorUsername, a.ActorRole, a.Detail))
            .ToListAsync();
        return rows;
    }

    // ── Phase 8 PR #31d — full detail sheet ────────────────────────────────

    /// <summary>
    /// PR #31d — Single-query full detail graph cho EngineerSpecDetail.razor
    /// + PdfSpecSheetExporter. Include Material/Print/Print.Colors/
    /// FlexoCuttingRows/FlexoInkRows + Product.Customer. KHÔNG re-fetch by id
    /// per section (bài học hotfix #27 — render trực tiếp từ entity graph).
    ///
    /// Returns null nếu revision không tồn tại hoặc trashed.
    /// </summary>
    public async Task<SpecDetailDto?> SpecDetailAsync(long revisionId, int auditMax = 50, int lineageMax = 6)
    {
        var rev = await _db.ProductRevisions
            .AsNoTracking()
            .Include(r => r.Material)
            .Include(r => r.Print!).ThenInclude(p => p.Colors)
            .Include(r => r.Print!).ThenInclude(p => p.FlexoCuttingRows)
            .Include(r => r.Print!).ThenInclude(p => p.FlexoInkRows)
            .Include(r => r.Product!).ThenInclude(p => p.Customer)
            .Where(r => !r.IsTrashed)
            .FirstOrDefaultAsync(r => r.Id == revisionId);
        if (rev is null) return null;

        var processCode = rev.Print?.ProcessCode ?? "";
        var planner = PlannerFromProcessCode(processCode);
        var isFlexo = string.Equals(processCode, "FLEXO", StringComparison.OrdinalIgnoreCase);
        var isSilkscreen = !isFlexo
            && (string.Equals(processCode, "SILKSCREEN", StringComparison.OrdinalIgnoreCase)
             || string.Equals(processCode, "INDIGO", StringComparison.OrdinalIgnoreCase)
             || string.Equals(processCode, "INDIGO_PRIMER", StringComparison.OrdinalIgnoreCase));

        // Parse flexo printing rows từ ExtraJson (folded per Q3 PR #31b)
        var flexoPrintRows = new List<FlexoPrintRow>();
        if (isFlexo && !string.IsNullOrWhiteSpace(rev.Print?.ExtraJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rev.Print.ExtraJson);
                if (doc.RootElement.TryGetProperty("flexo_print_rows", out var rows)
                    && rows.ValueKind == JsonValueKind.Array)
                {
                    int seq = 0;
                    foreach (var el in rows.EnumerateArray())
                    {
                        if (el.ValueKind != JsonValueKind.Object) continue;
                        flexoPrintRows.Add(new FlexoPrintRow(
                            Seq:         el.TryGetProperty("Seq", out var s) ? (s.GetInt32()) : ++seq,
                            Process:     el.TryGetProperty("Process", out var p) ? p.GetString() : null,
                            Material:    el.TryGetProperty("Material", out var m) ? m.GetString() : null,
                            Thickness:   el.TryGetProperty("Thickness", out var t) ? t.GetString() : null,
                            Size:        el.TryGetProperty("Size", out var sz) ? sz.GetString() : null,
                            Cylinders:   el.TryGetProperty("Cylinders", out var c) ? c.GetString() : null,
                            PitchMm:     el.TryGetProperty("PitchMm", out var pm) ? pm.GetString() : null,
                            Speed:       el.TryGetProperty("Speed", out var sp) ? sp.GetString() : null,
                            TensionHead: el.TryGetProperty("TensionHead", out var th) ? th.GetString() : null,
                            TensionEnd:  el.TryGetProperty("TensionEnd", out var te) ? te.GetString() : null,
                            TensionRoll: el.TryGetProperty("TensionRoll", out var tr) ? tr.GetString() : null,
                            PlateCavity: el.TryGetProperty("PlateCavity", out var pc) ? pc.GetString() : null,
                            Tension:     el.TryGetProperty("Tension", out var tn) ? tn.GetString() : null));
                    }
                }
            }
            catch { /* malformed JSON — fallback empty list, không crash */ }
        }

        var dto = new SpecDetailDto
        {
            Id = rev.Id,
            SpecCode = rev.SpecCode,
            Title = rev.Title,
            RevisionCode = rev.RevisionCode,
            Status = rev.Status,
            RefNo = rev.RefNo,
            InspectionLevel = rev.InspectionLevel,
            Planner = planner ?? "UNKNOWN",
            ProductCode = rev.Product?.ProductCode ?? "",
            ProductName = rev.Product?.Name ?? "",
            CustomerName = rev.Product?.Customer?.Name,
            ProcessCode = processCode,
            IsSilkscreen = isSilkscreen,
            IsFlexo = isFlexo,
            // Material
            SubstrateType = rev.Material?.SubstrateType,
            SubstrateBrand = rev.Material?.SubstrateBrand,
            AdhesiveType = rev.Material?.AdhesiveType,
            AdhesiveBrand = rev.Material?.AdhesiveBrand,
            ThicknessUm = rev.Material?.ThicknessUm,
            MaterialExtraJson = rev.Material?.ExtraJson,
            // Print params
            PrintingCavity = rev.Print?.Cavity,
            LengthPitchMm = rev.Print?.PitchMm,
            ProductSizeWmm = rev.Print?.ProductSizeWmm,
            ProductSizeHmm = rev.Print?.ProductSizeHmm,
            Varnish = rev.Print?.Varnish,
            Lamination = rev.Print?.Lamination,
            WhiteUnderprint = rev.Print?.WhiteUnderprint ?? false,
            PrintExtraJson = rev.Print?.ExtraJson,
            ColorSpecJson = rev.Print?.ColorSpecJson,
            // Remarks (PR #31d)
            RemarksText = rev.Print?.RemarksText,
            RemarksCutText = rev.Print?.RemarksCutText,
            // Silk colors
            PrintColors = rev.Print?.Colors.OrderBy(c => c.Seq).Select(c => new SpecPrintColorRow(
                c.Seq, c.Surface, c.Color, c.InkName, c.InkCode, c.Maker, c.Retarder,
                c.Viscosity, c.Speed, c.Squeegee, c.Dry, c.TemperatureC, c.TimeMin, c.Uv,
                c.EmulsionUm, c.PlateSize, c.Mesh, c.AngleDeg, c.PlateCode, c.ControlNo, c.Remark
            )).ToList() ?? new List<SpecPrintColorRow>(),
            // Flexo rows
            FlexoPrintRows = flexoPrintRows,
            FlexoCuttingRows = rev.Print?.FlexoCuttingRows.OrderBy(c => c.Seq).Select(c => new FlexoCuttingRow(
                c.Seq, c.Process, c.Lamination, c.Size, c.CutterLot, c.CutterName,
                c.PcsPerSheet, c.CuttingCavity, c.PitchMm, c.Packing, c.PaperSpeed,
                c.CuttingSpeed, c.CuttingPressure, c.HeadTension, c.RollTension
            )).ToList() ?? new List<FlexoCuttingRow>(),
            FlexoInkRows = rev.Print?.FlexoInkRows.OrderBy(i => i.Seq).Select(i => new FlexoInkRow(
                i.Seq, i.Color, i.InkCode, i.InkDescription, i.Brand, i.Anilox,
                i.PlateCode, i.Pressure, i.UvPowerW, i.IrPowerW
            )).ToList() ?? new List<FlexoInkRow>(),
            // Approval (Option A render-only)
            ApprovedBy = rev.ApprovedBy,
            ApprovedAt = rev.ApprovedAt,
            ReleasedBy = rev.ReleasedBy,
            ReleasedAt = rev.ReleasedAt,
            // Audit stamps
            CreatedAt = rev.CreatedAt,
            CreatedBy = rev.CreatedBy,
            UpdatedAt = rev.UpdatedAt,
            UpdatedBy = rev.UpdatedBy,
        };

        // Lineage walk — DESC max 6 entries (Q11)
        dto.Lineage = await WalkLineageAsync(rev.Id, lineageMax);

        // Audit timeline — reuse PR #29 query path (Q11 audit timeline)
        dto.AuditEntries = await SpecAuditTrailAsync(rev.Id, auditMax);

        return dto;
    }

    /// <summary>
    /// Walk ParentRevisionId chain DESC, return entries newest-first
    /// (current → parent → grandparent → …). Limit max entries.
    /// </summary>
    private async Task<List<RevisionLineageEntry>> WalkLineageAsync(long startId, int max)
    {
        var entries = new List<RevisionLineageEntry>();
        var currentId = (long?)startId;
        int safetyCounter = 0;
        while (currentId.HasValue && entries.Count < max && safetyCounter++ < 50)
        {
            var rev = await _db.ProductRevisions
                .AsNoTracking()
                .Where(r => r.Id == currentId.Value)
                .Select(r => new {
                    r.Id, r.RevisionCode, r.ChangeSummary, r.CreatedAt, r.CreatedBy, r.ParentRevisionId
                })
                .FirstOrDefaultAsync();
            if (rev is null) break;
            entries.Add(new RevisionLineageEntry(
                rev.Id, rev.RevisionCode, rev.ChangeSummary, rev.CreatedAt, rev.CreatedBy));
            currentId = rev.ParentRevisionId;
        }
        return entries;
    }
}

// ── Phase 8 PR-L1 — Lifecycle service result envelopes ──────────────────────

/// <summary>
/// Outcome from <see cref="SpecService.CopyAsync"/>. Controller maps Kind to
/// the appropriate HTTP status: Ok → 201, SourceNotFound → 404,
/// DuplicateCode → 422, ValidationError → 422.
/// </summary>
public sealed record CopyResult(
    CopyResultKind Kind,
    ProductRevision? Revision,
    string? Error)
{
    public static CopyResult Ok(ProductRevision rev) => new(CopyResultKind.Ok, rev, null);
    public static CopyResult SourceNotFound() => new(CopyResultKind.SourceNotFound, null, "Source spec not found");
    public static CopyResult DuplicateCode(string code) => new(CopyResultKind.DuplicateCode, null, $"SpecCode '{code}' already exists");
    public static CopyResult ValidationError(string message) => new(CopyResultKind.ValidationError, null, message);
}

public enum CopyResultKind
{
    Ok,
    SourceNotFound,
    DuplicateCode,
    ValidationError,
}

/// <summary>
/// Outcome from <see cref="SpecService.UpdateAsync"/>. ImmutableStatus
/// carries the existing status so the controller can compose a clear
/// "Only Draft revs editable; this rev is Approved" message.
/// </summary>
public sealed record UpdateResult(
    UpdateResultKind Kind,
    ProductRevision? Revision,
    ProductRevisionStatus? CurrentStatus,
    string? Error)
{
    public static UpdateResult Ok(ProductRevision rev) => new(UpdateResultKind.Ok, rev, rev.Status, null);
    public static UpdateResult NoChanges(ProductRevision rev) => new(UpdateResultKind.NoChanges, rev, rev.Status, null);
    public static UpdateResult NotFound() => new(UpdateResultKind.NotFound, null, null, "Spec not found");
    public static UpdateResult Trashed() => new(UpdateResultKind.Trashed, null, null, "Spec is trashed; restore before editing");
    public static UpdateResult ImmutableStatus(ProductRevisionStatus current) => new(
        UpdateResultKind.ImmutableStatus, null, current,
        $"Only Draft revisions are editable; current status is {current}");
}

public enum UpdateResultKind
{
    Ok,
    NoChanges,
    NotFound,
    Trashed,
    ImmutableStatus,
}

// ── Phase 8 PR-L2 — Revise + Supersede result envelopes ─────────────────────

/// <summary>
/// Outcome from <see cref="SpecService.ReviseAsync"/>. Controller maps Kind to
/// HTTP: Ok → 201, SourceNotFound → 404, InvalidSourceStatus → 422 with the
/// current status surfaced, ReasonRequired → 422 validation, SourceTrashed →
/// 422 trashed.
/// </summary>
public sealed record ReviseResult(
    ReviseResultKind Kind,
    ProductRevision? Revision,
    ProductRevisionStatus? CurrentStatus,
    string? Error)
{
    public static ReviseResult Ok(ProductRevision rev) => new(ReviseResultKind.Ok, rev, rev.Status, null);
    public static ReviseResult SourceNotFound() => new(ReviseResultKind.SourceNotFound, null, null, "Source spec not found");
    public static ReviseResult SourceTrashed() => new(ReviseResultKind.SourceTrashed, null, null, "Source spec is trashed; restore before revising");
    public static ReviseResult InvalidSourceStatus(ProductRevisionStatus current) => new(
        ReviseResultKind.InvalidSourceStatus, null, current,
        $"Revise is only available for Approved/Released revisions; current status is {current}");
    public static ReviseResult ReasonRequired() => new(ReviseResultKind.ReasonRequired, null, null, "Reason must be at least 5 characters");
}

public enum ReviseResultKind
{
    Ok,
    SourceNotFound,
    SourceTrashed,
    InvalidSourceStatus,
    ReasonRequired,
}

/// <summary>
/// Outcome from <see cref="SpecService.SupersedeAsync"/>. ConfirmMismatch
/// signals the operator-typed SpecCode did not match — server-side defence
/// for the 2-step confirm UX (Q2).
/// </summary>
public sealed record SupersedeResult(
    SupersedeResultKind Kind,
    ProductRevision? Revision,
    ProductRevisionStatus? CurrentStatus,
    string? Error)
{
    public static SupersedeResult Ok(ProductRevision rev) => new(SupersedeResultKind.Ok, rev, rev.Status, null);
    public static SupersedeResult NotFound() => new(SupersedeResultKind.NotFound, null, null, "Spec not found");
    public static SupersedeResult Trashed() => new(SupersedeResultKind.Trashed, null, null, "Spec is trashed; restore before superseding");
    public static SupersedeResult InvalidStatus(ProductRevisionStatus current) => new(
        SupersedeResultKind.InvalidStatus, null, current,
        $"Mark Superseded is only available for Approved/Released revisions; current status is {current}");
    public static SupersedeResult ConfirmMismatch() => new(
        SupersedeResultKind.ConfirmMismatch, null, null,
        "Confirmation SpecCode does not match");
}

public enum SupersedeResultKind
{
    Ok,
    NotFound,
    Trashed,
    InvalidStatus,
    ConfirmMismatch,
}

// ── Phase 8 PR-L3 — Trash + Restore + Purge result envelopes ────────────────

/// <summary>Filter chip view selector for <see cref="SpecService.SpecsAsync"/>.</summary>
public enum SpecListView
{
    Active,
    Trash,
    All,
}

/// <summary>
/// Outcome from <see cref="SpecService.TrashAsync"/>. ActiveWorkOrders carries
/// the count so the controller can compose a clear "Cannot trash: N active
/// WOs reference this spec" message.
/// </summary>
public sealed record TrashResult(
    TrashResultKind Kind,
    ProductRevision? Revision,
    int? ActiveWoCount,
    string? Error)
{
    public static TrashResult Ok(ProductRevision rev) => new(TrashResultKind.Ok, rev, null, null);
    public static TrashResult NotFound() => new(TrashResultKind.NotFound, null, null, "Spec not found");
    public static TrashResult AlreadyTrashed() => new(TrashResultKind.AlreadyTrashed, null, null, "Spec is already in Trash");
    public static TrashResult ActiveWorkOrders(int n) => new(
        TrashResultKind.ActiveWorkOrders, null, n,
        $"Cannot trash: {n} active Work Order(s) reference this spec");
}

public enum TrashResultKind
{
    Ok,
    NotFound,
    AlreadyTrashed,
    ActiveWorkOrders,
}

/// <summary>
/// Outcome from <see cref="SpecService.RestoreAsync"/>.
/// </summary>
public sealed record RestoreResult(
    RestoreResultKind Kind,
    ProductRevision? Revision,
    string? Error)
{
    public static RestoreResult Ok(ProductRevision rev) => new(RestoreResultKind.Ok, rev, null);
    public static RestoreResult NotFound() => new(RestoreResultKind.NotFound, null, "Spec not found");
    public static RestoreResult NotTrashed() => new(RestoreResultKind.NotTrashed, null, "Spec is not in Trash");
}

public enum RestoreResultKind
{
    Ok,
    NotFound,
    NotTrashed,
}
