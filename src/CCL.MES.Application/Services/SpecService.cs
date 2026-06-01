using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.SpecDetail;
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
    public async Task<PagedResult<ProductRevisionListItem>> SpecsAsync(string? search, int page, int pageSize)
    {
        var q = _db.ProductRevisions
            .AsNoTracking()
            .Where(x => !x.IsTrashed)               // soft-delete skip
            .Include(x => x.Product)
                .ThenInclude(p => p!.Customer)      // Q3 — Customer column join
            .Include(x => x.Print)                  // Colors/Cavity/Pitch/Planner derive
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Q6 — exactly 3 fields per SpecHub placeholder UX.
            q = q.Where(x =>
                (x.Product != null && x.Product.Customer != null && EF.Functions.Like(x.Product.Customer.Name, $"%{s}%"))
                || (x.Product != null && EF.Functions.Like(x.Product.ProductCode, $"%{s}%"))
                || EF.Functions.Like(x.Title, $"%{s}%"));
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
