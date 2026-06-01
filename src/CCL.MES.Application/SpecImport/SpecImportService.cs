using System.Globalization;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.SpecImport;

/// <summary>
/// Phase 8 PR #31a — orchestrator cho xlsx import flow. Tách parser (pure data
/// extraction) khỏi persistence (Product lookup / ProductRevision + SpecPrint +
/// SpecPrintColor rows + audit emit) cho dễ test + reuse.
///
/// Hai entry point chính:
///   - <see cref="PreviewAsync"/>: parse stream → trả về preview DTO + dup check
///     (KHÔNG ghi DB). Modal show summary + warnings để operator confirm.
///   - <see cref="SaveAsync"/>: ghi DB trong 1 transaction (Q13). Reject nếu RefNo
///     trùng (Q4 — Replace/Upgrade defer PR lifecycle).
///
/// `RefreshSamplesAsync` (PR #31a — Q7) batch-process 4 silkscreen samples
/// bundled tại `wwwroot/Data/Specs/`. Admin-only; idempotent skip RefNo trùng;
/// `?force=1` re-parse + update in-place.
/// </summary>
public class SpecImportService
{
    /// <summary>5 MB max per Q14 — reject xlsx vượt size.</summary>
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ISpecXlsxParserFactory _parserFactory;

    public SpecImportService(IMesDbContext db, IAuditWriter audit, ISpecXlsxParserFactory parserFactory)
    {
        _db = db;
        _audit = audit;
        _parserFactory = parserFactory;
    }

    /// <summary>
    /// Đọc xlsx → parse → trả preview KHÔNG ghi DB. Caller hold pending state;
    /// nếu operator confirm gọi <see cref="SaveAsync"/> với cùng parsed DTO.
    /// </summary>
    public async Task<SpecImportPreviewDto> PreviewAsync(
        Stream xlsxStream, string fileName, long fileSize, string plannerCategory)
    {
        var preview = new SpecImportPreviewDto
        {
            FileName = fileName,
            FileSizeBytes = fileSize,
            PlannerCategory = string.IsNullOrWhiteSpace(plannerCategory) ? "silkscreen" : plannerCategory,
        };

        if (fileSize > MaxFileSizeBytes)
        {
            preview.ParseOk = false;
            preview.ParseError = $"File quá lớn ({fileSize / 1024} KB). Tối đa {MaxFileSizeBytes / 1024} KB.";
            return preview;
        }

        ParsedSpecDto parsed;
        try
        {
            // PR #31b — dispatch parser via factory (silkscreen / flexo / fallback)
            var parser = _parserFactory.GetParser(preview.PlannerCategory);
            parsed = parser.Parse(xlsxStream, preview.PlannerCategory);
        }
        catch (Exception ex)
        {
            preview.ParseOk = false;
            preview.ParseError = $"Parse error: {ex.Message}";
            return preview;
        }

        preview.Parsed = parsed;
        preview.ParseOk = true;
        preview.Warnings.AddRange(parsed.Warnings);

        // Duplicate detection — RefNo match trên non-trashed ProductRevisions.
        if (!string.IsNullOrWhiteSpace(parsed.RefNo))
        {
            var dup = await _db.ProductRevisions
                .AsNoTracking()
                .Where(r => !r.IsTrashed && r.RefNo == parsed.RefNo)
                .Select(r => new { r.Id, r.SpecCode })
                .FirstOrDefaultAsync();
            if (dup is not null)
            {
                preview.DuplicateRefNo = true;
                preview.DuplicateRevisionId = dup.Id;
                preview.DuplicateSpecCode = dup.SpecCode;
                preview.Warnings.Add(
                    $"RefNo '{parsed.RefNo}' đã tồn tại (spec '{dup.SpecCode}', id={dup.Id}). " +
                    $"PR #31a chỉ cho create-new — đổi RefNo trong xlsx hoặc xóa spec cũ trước.");
            }
        }

        return preview;
    }

    /// <summary>
    /// Persist parsed spec → ProductRevision rev "A" + SpecPrint + SpecPrintColor
    /// rows trong transaction. Q4: reject nếu RefNo trùng. Q8: auto-create
    /// Product nếu PartNo không khớp existing ProductCode.
    /// </summary>
    public async Task<SpecImportSaveResult> SaveAsync(
        ParsedSpecDto parsed, string source, string fileName, string? user, bool overwriteRefNo = false)
    {
        if (parsed is null) throw new ArgumentNullException(nameof(parsed));
        if (string.IsNullOrWhiteSpace(parsed.Customer)) throw new InvalidOperationException("Customer field required for save.");
        if (string.IsNullOrWhiteSpace(parsed.PartNo)) throw new InvalidOperationException("Part No field required for save.");

        // Dup re-check (race vs PreviewAsync — operator có thể delay click Save).
        if (!overwriteRefNo && !string.IsNullOrWhiteSpace(parsed.RefNo))
        {
            var dupExists = await _db.ProductRevisions
                .AsNoTracking()
                .AnyAsync(r => !r.IsTrashed && r.RefNo == parsed.RefNo);
            if (dupExists)
                throw new InvalidOperationException(
                    $"RefNo '{parsed.RefNo}' đã tồn tại — không cho create-new.");
        }

        // ⚠ Q13 — transaction wraps Product upsert + ProductRevision + sibling
        // + color rows + audit. Rollback on any throw.
        // Note: IMesDbContext abstraction không expose Database property; cast
        // xuống DbContext qua interface để mở transaction (acceptable vì
        // Application service đã được phép biết EF-shape concrete impl).
        var dbCtx = (DbContext)_db;
        await using var tx = await dbCtx.Database.BeginTransactionAsync();

        try
        {
            // Q8 — Customer lookup-or-create. Product.CustomerId là FK non-
            // nullable nên không thể save Product without resolving Customer trước.
            // Match by Name case-insensitive (xlsx customer column = display name).
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.Name.ToLower() == parsed.Customer.ToLower());
            if (customer is null)
            {
                customer = new Customer
                {
                    Code = parsed.Customer.ToUpperInvariant().Replace(" ", "_"),
                    Name = parsed.Customer,
                };
                _db.Customers.Add(customer);
                await _db.SaveChangesAsync();
            }

            // Q8 — Product lookup-or-create. Scoped to (CustomerId, ProductCode)
            // để 2 customer khác nhau có thể trùng PartNo nội bộ.
            var product = await _db.Products
                .FirstOrDefaultAsync(p => p.ProductCode == parsed.PartNo && p.CustomerId == customer.Id);
            bool createdNewProduct = false;
            if (product is null)
            {
                product = new Product
                {
                    ProductCode = parsed.PartNo,
                    Name = !string.IsNullOrWhiteSpace(parsed.PartName) ? parsed.PartName : parsed.PartNo,
                    CustomerId = customer.Id,
                };
                _db.Products.Add(product);
                await _db.SaveChangesAsync();
                createdNewProduct = true;
            }
            else if (!string.IsNullOrWhiteSpace(parsed.PartName) && string.IsNullOrWhiteSpace(product.Name))
            {
                // Patch Name nếu existing product có Name rỗng (legacy backfill).
                product.Name = parsed.PartName;
                await _db.SaveChangesAsync();
            }

            // Generate SpecCode — semantic gắn liền với SpecHub `spec.code` =
            // partNo. KHÔNG enforce unique cross-product (PartNo có thể chia
            // ranh giới khác); chỉ unique trong (ProductId, RevisionCode).
            var specCode = parsed.PartNo;
            var isFlexo = string.Equals(parsed.Category, "flexo", StringComparison.OrdinalIgnoreCase);
            // PR #31b — flexo NumColors = số ink row; silk = số print row.
            var numColors = isFlexo ? parsed.FlexoInkRows.Count : parsed.PrintRows.Count;
            // Flexo cavity/pitch lấy từ first cuttingRow (tự nhiên ánh xạ);
            // silk dùng PrintingCavity + LengthPitchMm trực tiếp từ R8.
            int? cavity = isFlexo
                ? parsed.FlexoCuttingRows.FirstOrDefault()?.CuttingCavity
                : parsed.PrintingCavity;
            double? pitchMm = isFlexo
                ? parsed.FlexoCuttingRows.FirstOrDefault()?.PitchMm
                : parsed.LengthPitchMm;

            var revision = new ProductRevision
            {
                ProductId = product.Id,
                SpecCode = specCode,
                Title = !string.IsNullOrWhiteSpace(parsed.PartName) ? parsed.PartName : parsed.PartNo,
                RevisionCode = "A",
                Status = ProductRevisionStatus.Draft,
                RefNo = string.IsNullOrWhiteSpace(parsed.RefNo) ? null : parsed.RefNo,
                InspectionLevel = string.IsNullOrWhiteSpace(parsed.InspectionLevel) ? null : parsed.InspectionLevel,
                ChangeSummary = $"Imported from {fileName}",
                Print = new SpecPrint
                {
                    ProcessCode = MapCategoryToProcessCode(parsed.Category),
                    NumColors = numColors,
                    Cavity = cavity,
                    PitchMm = pitchMm,
                    ColorSpecJson = isFlexo ? null : SerializeColorsForLegacy(parsed.PrintRows),
                    // PR #31b — fold flexo printing rows (substrate/cylinder/tension)
                    // vào ExtraJson per Q3. Operator ít cần column-wise query.
                    ExtraJson = isFlexo ? SerializeFlexoPrintRows(parsed.FlexoPrintRows) : null,
                    Colors = isFlexo ? new List<SpecPrintColor>() : parsed.PrintRows.Select(r => new SpecPrintColor
                    {
                        Seq          = r.Seq,
                        Surface      = r.Surface,
                        Color        = r.Color,
                        InkName      = r.InkName,
                        InkCode      = r.InkCode,
                        Maker        = r.Maker,
                        Retarder     = r.Retarder,
                        Viscosity    = r.Viscosity,
                        Speed        = r.Speed,
                        Squeegee     = r.Squeegee,
                        Dry          = r.Dry,
                        TemperatureC = r.TemperatureC,
                        TimeMin      = r.TimeMin,
                        Uv           = r.Uv,
                        EmulsionUm   = r.EmulsionUm,
                        PlateSize    = r.PlateSize,
                        Mesh         = r.Mesh,
                        AngleDeg     = r.AngleDeg,
                        PlateCode    = r.PlateCode,
                        ControlNo    = r.ControlNo,
                        Remark       = r.Remark,
                    }).ToList(),
                    // PR #31b — Flexo cutting + ink child rows.
                    FlexoCuttingRows = isFlexo ? parsed.FlexoCuttingRows.Select(c => new SpecFlexoCuttingRow
                    {
                        Seq             = c.Seq,
                        Process         = c.Process,
                        Lamination      = c.Lamination,
                        Size            = c.Size,
                        CutterLot       = c.CutterLot,
                        CutterName      = c.CutterName,
                        PcsPerSheet     = c.PcsPerSheet,
                        CuttingCavity   = c.CuttingCavity,
                        PitchMm         = c.PitchMm,
                        Packing         = c.Packing,
                        PaperSpeed      = c.PaperSpeed,
                        CuttingSpeed    = c.CuttingSpeed,
                        CuttingPressure = c.CuttingPressure,
                        HeadTension     = c.HeadTension,
                        RollTension     = c.RollTension,
                    }).ToList() : new List<SpecFlexoCuttingRow>(),
                    FlexoInkRows = isFlexo ? parsed.FlexoInkRows.Select(i => new SpecFlexoInkRow
                    {
                        Seq             = i.Seq,
                        Color           = i.Color,
                        InkCode         = i.InkCode,
                        InkDescription  = i.InkDescription,
                        Brand           = i.Brand,
                        Anilox          = i.Anilox,
                        PlateCode       = i.PlateCode,
                        Pressure        = i.Pressure,
                        UvPowerW        = i.UvPowerW,
                        IrPowerW        = i.IrPowerW,
                    }).ToList() : new List<SpecFlexoInkRow>(),
                },
            };

            // Material sibling — populated từ product info fields nếu có giá trị
            if (!string.IsNullOrWhiteSpace(parsed.MaterialType) || !string.IsNullOrWhiteSpace(parsed.MaterialSize))
            {
                revision.Material = new SpecMaterial
                {
                    SubstrateType = NullIfEmpty(parsed.MaterialType),
                    SubstrateBrand = null,
                    ExtraJson = SerializeMaterialExtra(parsed),
                };
            }

            _db.ProductRevisions.Add(revision);
            await _db.SaveChangesAsync();

            await _audit.EmitAsync(
                AuditAction.SpecImport,
                user ?? "anonymous",
                actorRole: "",
                targetType: "ProductRevision",
                targetId: revision.Id.ToString(CultureInfo.InvariantCulture),
                detail: JsonSerializer.Serialize(new
                {
                    spec_code = revision.SpecCode,
                    ref_no = revision.RefNo,
                    title = revision.Title,
                    product_id = revision.ProductId,
                    process_code = revision.Print?.ProcessCode,
                    category = parsed.Category,
                    source,
                    filename = fileName,
                    rows_parsed = isFlexo
                        ? parsed.FlexoInkRows.Count + parsed.FlexoCuttingRows.Count + parsed.FlexoPrintRows.Count
                        : parsed.PrintRows.Count,
                    flexo_ink_rows = isFlexo ? parsed.FlexoInkRows.Count : 0,
                    flexo_cutting_rows = isFlexo ? parsed.FlexoCuttingRows.Count : 0,
                    flexo_print_rows = isFlexo ? parsed.FlexoPrintRows.Count : 0,
                    silk_print_rows = isFlexo ? 0 : parsed.PrintRows.Count,
                    warnings = parsed.Warnings.Take(20).ToArray(),  // cap to ≤4KB
                    created_new_product = createdNewProduct,
                }));

            await tx.CommitAsync();

            return new SpecImportSaveResult
            {
                ProductRevisionId = revision.Id,
                ProductId = product.Id,
                SpecCode = revision.SpecCode,
                RefNo = revision.RefNo ?? "",
                RowsParsed = isFlexo
                    ? parsed.FlexoInkRows.Count + parsed.FlexoCuttingRows.Count + parsed.FlexoPrintRows.Count
                    : parsed.PrintRows.Count,
                CreatedNewProduct = createdNewProduct,
                Warnings = parsed.Warnings.ToList(),
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// PR #31a Q7 — Admin batch sample loader. Đọc 4 file silkscreen sanitized
    /// bundled tại <paramref name="samplesDir"/>; idempotent skip RefNo trùng;
    /// `force=true` re-parse + UPDATE rev existing trong-place.
    /// </summary>
    public async Task<SampleRefreshResult> RefreshSamplesAsync(
        string samplesDir, IEnumerable<(string FileName, string Category)> files, bool force, string? user)
    {
        var result = new SampleRefreshResult();
        foreach (var (fileName, category) in files)
        {
            var fullPath = Path.Combine(samplesDir, fileName);
            if (!File.Exists(fullPath))
            {
                result.Files.Add(new SampleFileResult(fileName, "missing", null, "File not found in samples dir."));
                continue;
            }

            try
            {
                await using var fs = File.OpenRead(fullPath);
                var preview = await PreviewAsync(fs, fileName, fs.Length, category);
                if (!preview.ParseOk || preview.Parsed is null)
                {
                    result.Files.Add(new SampleFileResult(fileName, "parse-error", null, preview.ParseError));
                    continue;
                }

                if (preview.DuplicateRefNo && !force)
                {
                    result.Skipped++;
                    result.Files.Add(new SampleFileResult(fileName, "skipped-dup", preview.Parsed.RefNo, null));
                    continue;
                }

                if (preview.DuplicateRefNo && force)
                {
                    // Force = soft-trash existing + create new. Safer than in-
                    // place mutation cho audit forensic trail.
                    var existing = await _db.ProductRevisions
                        .FirstOrDefaultAsync(r => !r.IsTrashed && r.RefNo == preview.Parsed.RefNo);
                    if (existing is not null)
                    {
                        existing.IsTrashed = true;
                        existing.TrashedAt = DateTime.UtcNow;
                        existing.TrashedBy = user ?? "system";
                        await _db.SaveChangesAsync();
                    }
                }

                var save = await SaveAsync(preview.Parsed, "refresh_samples", fileName, user, overwriteRefNo: force);
                if (preview.DuplicateRefNo && force)
                {
                    result.Updated++;
                    result.Files.Add(new SampleFileResult(fileName, "updated", save.RefNo, null));
                }
                else
                {
                    result.Added++;
                    result.Files.Add(new SampleFileResult(fileName, "added", save.RefNo, null));
                }
            }
            catch (Exception ex)
            {
                result.Files.Add(new SampleFileResult(fileName, "error", null, ex.Message));
            }
        }

        await _audit.EmitAsync(
            AuditAction.SpecRefreshSamples,
            user ?? "anonymous",
            actorRole: "",
            targetType: "ProductRevision",
            targetId: "(batch)",
            detail: JsonSerializer.Serialize(new
            {
                added = result.Added,
                updated = result.Updated,
                skipped = result.Skipped,
                force,
                files = result.Files.Take(20).Select(f => new { filename = f.FileName, status = f.Status, ref_no = f.RefNo }).ToArray(),
            }));

        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string MapCategoryToProcessCode(string category) => category?.ToLowerInvariant() switch
    {
        "silkscreen" => "SILKSCREEN",
        "flexo" => "FLEXO",
        "letterpress" => "LETTERPRESS",
        "indigo" => "INDIGO",
        "diecut" => "FLATBED_CUT",  // mặc định trong DIECUT family; operator có thể chỉnh sau
        _ => "SILKSCREEN",
    };

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// PR #31a — Vẫn maintain ColorSpecJson legacy shape (mirror SpecHub
    /// `printRows[]`) song song với SpecPrintColor entity rows. Caller cũ đọc
    /// JSON vẫn work; PR #33 detail sheet sẽ ưu tiên đọc entity rows.
    /// </summary>
    private static string SerializeColorsForLegacy(List<ParsedPrintColorDto> rows)
    {
        if (rows.Count == 0) return "[]";
        var arr = rows.Select(r => new
        {
            no = r.Seq,
            surface = r.Surface,
            color = r.Color,
            ink_name = r.InkName,
            ink_code = r.InkCode,
            maker = r.Maker,
            retarder = r.Retarder,
            visc = r.Viscosity,
            speed = r.Speed,
            squeegee = r.Squeegee,
            dry = r.Dry,
            temp = r.TemperatureC,
            time_min = r.TimeMin,
            uv = r.Uv,
            emulsion_um = r.EmulsionUm,
            plate_size = r.PlateSize,
            mesh = r.Mesh,
            angle = r.AngleDeg,
            plate_code = r.PlateCode,
            control = r.ControlNo,
            remark = r.Remark,
        });
        return JsonSerializer.Serialize(arr);
    }

    /// <summary>
    /// PR #31b — Persist flexo printing rows (substrate/cylinder/tension —
    /// 12 fields) vào SpecPrint.ExtraJson. Operator ít cần column-wise query
    /// nên KHÔNG tách entity riêng (Q3 đã chốt 2 entity cutting + ink).
    /// </summary>
    private static string? SerializeFlexoPrintRows(List<ParsedFlexoPrintRowDto> rows)
    {
        if (rows.Count == 0) return null;
        var payload = new { flexo_print_rows = rows };
        return JsonSerializer.Serialize(payload);
    }

    private static string? SerializeMaterialExtra(ParsedSpecDto parsed)
    {
        var bag = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(parsed.MaterialSize)) bag["material_size"] = parsed.MaterialSize;
        if (!string.IsNullOrWhiteSpace(parsed.LaminationTape) && parsed.LaminationTape != "— Không có")
            bag["lamination_tape"] = parsed.LaminationTape;
        if (!string.IsNullOrWhiteSpace(parsed.LaminationSize) && parsed.LaminationSize != "—")
            bag["lamination_size"] = parsed.LaminationSize;
        if (!string.IsNullOrWhiteSpace(parsed.LaminationCavity) && parsed.LaminationCavity != "—")
            bag["lamination_cavity"] = parsed.LaminationCavity;
        if (bag.Count == 0) return null;
        return JsonSerializer.Serialize(bag);
    }
}

public class SampleRefreshResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<SampleFileResult> Files { get; set; } = new();
}

public record SampleFileResult(string FileName, string Status, string? RefNo, string? ErrorMessage);
