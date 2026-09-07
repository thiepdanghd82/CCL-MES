using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Nạp sổ lịch sử IQC từ Excel → <see cref="IqcInspection"/> (+ chi tiết
/// Roll/PCS khi <c>enrichDetails</c>). Idempotent theo ReceiptNo; chi tiết
/// idempotent theo ItemKey đã gắn nguồn <c>xls-ledger</c>.
/// </summary>
public sealed class IqcHistoryLedgerImportService
{
    public const string DetailSourceTag = "xls-ledger";

    private readonly IMesDbContext _db;
    public IqcHistoryLedgerImportService(IMesDbContext db) => _db = db;

    public async Task<IqcHistoryLedgerImportResult> ImportAsync(
        IReadOnlyList<IqcHistoryLedgerRow> rows, string actor, bool commit,
        bool enrichDetails = false, bool repairDetails = false,
        IReadOnlyList<(int PrimaryExcelRow, int AbsorbedExcelRow)>? absorbedPcsPairs = null,
        CancellationToken ct = default)
    {
        var read = rows.Count;
        var skipJudgment = 0;
        var skipPcs = 0;
        var inserted = 0;
        var already = 0;
        var detailsUpserted = 0;

        var candidates = new List<(IqcHistoryLedgerRow Row, string Receipt, QcResult Result, string Group, IqcMaterialCategory Cat)>();
        foreach (var row in rows)
        {
            if (string.Equals(row.Sheet, "PCS", StringComparison.OrdinalIgnoreCase)
                && row.Stt is null)
            {
                skipPcs++;
                continue;
            }

            var result = ParseJudgment(row.FinalJudgment);
            if (result is null || row.InspectedAt == DateTime.MinValue)
            {
                skipJudgment++;
                continue;
            }

            var sheet = NormalizeSheet(row.Sheet);
            var receipt = $"XLS-{sheet.ToUpperInvariant()}-{row.ExcelRow:D5}";
            var (group, cat) = MapGroup(sheet);
            candidates.Add((row, receipt, result.Value, group, cat));
        }

        var receipts = candidates.Select(c => c.Receipt).ToList();
        var existingMap = await _db.IqcInspections
            .Where(x => x.ReceiptNo != null && receipts.Contains(x.ReceiptNo))
            .ToDictionaryAsync(x => x.ReceiptNo!, StringComparer.OrdinalIgnoreCase, ct);

        // Code IFS → RawMaterials: gắn FK để tab Documents tìm được mã mẹ / thư mục HSF.
        var codeIfs = candidates
            .Select(c => c.Row.CodeIfs)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rawByPart = codeIfs.Count == 0
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            : await _db.RawMaterials.AsNoTracking()
                .Where(m => codeIfs.Contains(m.PartNo))
                .GroupBy(m => m.PartNo)
                .Select(g => new { PartNo = g.Key, Id = g.Min(m => m.Id) })
                .ToDictionaryAsync(x => x.PartNo, x => x.Id, StringComparer.OrdinalIgnoreCase, ct);

        // Batch: phiếu nào đã có chi tiết xls-ledger — tránh N+1 Include.
        HashSet<long> alreadyEnriched = [];
        if (enrichDetails && existingMap.Count > 0)
        {
            var ids = existingMap.Values.Select(x => x.Id).ToList();
            var tagged = await _db.IqcResultDetails.AsNoTracking()
                .Where(d => ids.Contains(d.IqcInspectionId)
                    && (d.CreatedBy == DetailSourceTag || d.MethodVi == DetailSourceTag))
                .Select(d => d.IqcInspectionId)
                .Distinct()
                .ToListAsync(ct);
            alreadyEnriched = tagged.ToHashSet();
        }

        // --repair-dims: gỡ chi tiết xls-ledger cũ rồi ghi lại (KT-02/KT-03 đúng rộng×dài).
        if (commit && enrichDetails && repairDetails && existingMap.Count > 0)
        {
            var repairReceipts = candidates
                .Where(c => IsRollOrPcs(c.Row.Sheet) && c.Row.Checks is not null)
                .Select(c => c.Receipt)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var repairIds = existingMap
                .Where(kv => repairReceipts.Contains(kv.Key))
                .Select(kv => kv.Value.Id)
                .ToList();
            await StripLedgerDetailsAsync(repairIds, ct);
            foreach (var id in repairIds)
                alreadyEnriched.Remove(id);
        }

        var measurePending = new List<(IqcResultDetail Detail, IReadOnlyList<double?> Samples)>();
        // Tracked parents that need BuildDetails after we know Id (new inserts).
        var newWithDetails = new List<(IqcInspection Insp, IqcHistoryLedgerRow Row)>();
        var linkedRaw = 0;

        // Dòng PCS đã gộp — gỡ KT sai trên phiếu orphan rồi gắn lại cùng chi tiết primary
        // (vd XLS-PCS-00129 mở vẫn thấy rộng+dài như 00128).
        if (commit && enrichDetails && repairDetails && absorbedPcsPairs is { Count: > 0 })
        {
            var byPrimary = candidates
                .Where(c => c.Row.Sheet.Equals("PCS", StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => c.Row.ExcelRow)
                .ToDictionary(g => g.Key, g => g.First().Row);
            var mirrorByOrphanReceipt = new Dictionary<string, IqcHistoryLedgerRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var (primaryRow, absorbedRow) in absorbedPcsPairs)
            {
                if (!byPrimary.TryGetValue(primaryRow, out var primaryLedger)) continue;
                mirrorByOrphanReceipt[$"XLS-PCS-{absorbedRow:D5}"] = primaryLedger;
            }

            if (mirrorByOrphanReceipt.Count > 0)
            {
                var orphanReceipts = mirrorByOrphanReceipt.Keys.ToList();
                var orphans = await _db.IqcInspections
                    .Where(x => x.ReceiptNo != null && orphanReceipts.Contains(x.ReceiptNo))
                    .ToListAsync(ct);
                if (orphans.Count > 0)
                {
                    await StripLedgerDetailsAsync(orphans.Select(o => o.Id).ToList(), ct);
                    foreach (var orphan in orphans)
                    {
                        if (orphan.ReceiptNo is null) continue;
                        if (!mirrorByOrphanReceipt.TryGetValue(orphan.ReceiptNo, out var ledger)) continue;
                        if (ledger.Checks is null) continue;
                        measurePending.AddRange(BuildDetails(orphan, ledger));
                        detailsUpserted++;
                    }
                }
            }
        }

        foreach (var (row, receipt, result, group, cat) in candidates)
        {
            if (existingMap.TryGetValue(receipt, out var found))
            {
                already++;
                if (commit && found.RawMaterialId is null
                    && !string.IsNullOrWhiteSpace(row.CodeIfs)
                    && rawByPart.TryGetValue(row.CodeIfs.Trim(), out var linkId))
                {
                    found.RawMaterialId = linkId;
                    linkedRaw++;
                }
                if (commit && enrichDetails && row.Checks is not null && IsRollOrPcs(row.Sheet)
                    && !alreadyEnriched.Contains(found.Id))
                {
                    // Attach details onto tracked entity already in context.
                    measurePending.AddRange(BuildDetails(found, row));
                    if (found.SampleSize == 0 && row.Checks.VisualSampleQty is int sq)
                        found.SampleSize = sq;
                    detailsUpserted++;
                    alreadyEnriched.Add(found.Id);
                }
                continue;
            }

            inserted++;
            if (!commit) continue;

            var part = FirstNonEmpty(row.CodeIfs, row.MotherCode, row.MaterialName) ?? "UNKNOWN";
            var qty = row.Quantity;
            var uom = row.Uom;
            if (qty <= 0) { qty = 1; uom ??= "ea"; }

            long? rawId = null;
            if (!string.IsNullOrWhiteSpace(row.CodeIfs)
                && rawByPart.TryGetValue(row.CodeIfs.Trim(), out var rid))
                rawId = rid;

            var insp = new IqcInspection
            {
                Group = group,
                MaterialCategory = cat,
                PartNo = Trunc(part, 64) ?? "UNKNOWN",
                CodeIfs = Trunc(row.CodeIfs, 64),
                RawMaterialId = rawId,
                BatchNumber = Trunc(row.PoNumber, 64) ?? "",
                LotNumber = Trunc(row.PoNumber, 64),
                ReceiptNo = receipt,
                MaterialDescription = Trunc(row.MaterialName, 256),
                SupplierName = Trunc(row.SupplierName, 256),
                InspectorId = Trunc(row.Inspector, 64),
                ReceivedDate = row.InspectedAt.Date,
                Quantity = qty,
                UomQty = Trunc(uom, 16),
                Result = result,
                ApprovedBy = Trunc(row.Inspector ?? actor, 64),
                ApprovedAt = row.InspectedAt.Date,
                SampleSize = row.Checks?.VisualSampleQty ?? 0,
            };
            _db.IqcInspections.Add(insp);
            existingMap[receipt] = insp;

            if (enrichDetails && row.Checks is not null && IsRollOrPcs(row.Sheet))
                newWithDetails.Add((insp, row));
        }

        foreach (var (insp, row) in newWithDetails)
        {
            measurePending.AddRange(BuildDetails(insp, row));
            detailsUpserted++;
        }

        if (commit && (inserted > 0 || detailsUpserted > 0 || linkedRaw > 0))
        {
            await _db.SaveChangesAsync(ct);
            AttachMeasurements(measurePending);
            if (measurePending.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        return new IqcHistoryLedgerImportResult(
            read, skipJudgment, skipPcs, inserted, already, detailsUpserted);
    }

    private static bool IsRollOrPcs(string sheet) =>
        sheet.Equals("Roll", StringComparison.OrdinalIgnoreCase)
        || sheet.Equals("PCS", StringComparison.OrdinalIgnoreCase);

    private async Task StripLedgerDetailsAsync(IReadOnlyList<long> inspectionIds, CancellationToken ct)
    {
        if (inspectionIds.Count == 0) return;
        const int chunkSize = 400;
        for (var offset = 0; offset < inspectionIds.Count; offset += chunkSize)
        {
            var slice = inspectionIds.Skip(offset).Take(chunkSize).ToList();
            var details = await _db.IqcResultDetails
                .Where(d => slice.Contains(d.IqcInspectionId)
                    && (d.CreatedBy == DetailSourceTag || d.MethodVi == DetailSourceTag))
                .ToListAsync(ct);
            if (details.Count == 0) continue;
            var detailIds = details.Select(d => d.Id).ToList();
            for (var dOff = 0; dOff < detailIds.Count; dOff += chunkSize)
            {
                var dSlice = detailIds.Skip(dOff).Take(chunkSize).ToList();
                var measures = await _db.IqcResultMeasurements
                    .Where(m => dSlice.Contains(m.IqcResultDetailId))
                    .ToListAsync(ct);
                _db.IqcResultMeasurements.RemoveRange(measures);
            }
            _db.IqcResultDetails.RemoveRange(details);
            await _db.SaveChangesAsync(ct);
        }
    }

    private void AttachMeasurements(
        List<(IqcResultDetail Detail, IReadOnlyList<double?> Samples)> pending)
    {
        foreach (var (detail, samples) in pending)
        {
            if (detail.Id <= 0) continue;
            for (var i = 0; i < detail.MeasureCount; i++)
            {
                _db.IqcResultMeasurements.Add(new IqcResultMeasurement
                {
                    IqcResultDetailId = detail.Id,
                    Seq = i + 1,
                    Value = i < samples.Count ? samples[i] : null,
                    CreatedBy = DetailSourceTag,
                });
            }
        }
    }

    private static List<(IqcResultDetail Detail, IReadOnlyList<double?> Samples)> BuildDetails(
        IqcInspection insp, IqcHistoryLedgerRow row)
    {
        var c = row.Checks!;
        var isRoll = row.Sheet.Equals("Roll", StringComparison.OrdinalIgnoreCase);
        var pending = new List<(IqcResultDetail, IReadOnlyList<double?>)>();

        // Packaging
        AddVerdict(insp, "NQ-01", "NQ", "Ngoại quan", "External inspection",
            "Tem nhãn", "Labels / marking",
            c.PackagingPass ?? true,
            measured: c.WarehouseInDate,
            acceptanceVi: c.ExpiryText is null ? null : $"HSD: {c.ExpiryText}");

        if (!string.IsNullOrWhiteSpace(c.ExpiryText) || !string.IsNullOrWhiteSpace(c.Pefc)
            || !string.IsNullOrWhiteSpace(c.PackagingSpec) || c.PackagingPass is not null)
        {
            var pkgNote = JoinParts(
                c.PackagingSpec is null ? null : $"Quy cách: {c.PackagingSpec}",
                c.ExpiryText is null ? null : $"HSD: {c.ExpiryText}",
                c.Pefc is null ? null : $"PEFC/FSC: {c.Pefc}",
                c.PefcLevel is null ? null : $"Level: {c.PefcLevel}");
            AddVerdict(insp, "NQ-06", "NQ", "Ngoại quan", "External inspection",
                "Điều kiện đóng gói", "Packaging condition",
                c.PackagingPass, measured: pkgNote);
        }

        // Visual defects — keep rows that have a count OR when overall is set keep zeros
        var anyCount = c.VisualDefects.Any(d => d.Count is not null);
        foreach (var d in c.VisualDefects)
        {
            if (d.Count is null && !anyCount) continue;
            if (d.Count is null) continue; // only materialise counted cells
            AddDefect(insp, d.ItemKey, d.Count, c.VisualPass);
        }

        if (c.VisualPass is not null && !anyCount)
        {
            AddVerdict(insp, isRoll ? "RD-13" : "PD-09", "NQ", "Ngoại quan", "External inspection",
                "Lỗi khác / tổng ngoại quan", "Other / visual overall",
                c.VisualPass);
        }

        // Dimension — width (KT-03)
        if (c.WidthSamples.Any(v => v.HasValue)
            || c.WidthPass is not null || c.WidthNominal is not null
            || (c.WidthSampleTexts?.Any(t => !string.IsNullOrWhiteSpace(t)) ?? false))
        {
            pending.Add(AddMeasure(insp, "KT-03", "KT", "Kích thước", "Size",
                "Độ rộng", "Width",
                measureCount: 5,
                samples: Pad5(c.WidthSamples),
                sampleTexts: c.WidthSampleTexts,
                low: c.WidthLow, up: c.WidthUp,
                unit: "mm",
                limitLabel: c.WidthNominal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                acceptanceVi: c.WidthNominal is null ? null : $"TC {c.WidthNominal} mm",
                pass: c.WidthPass));
        }

        // Dimension — length (KT-02): PCS dòng 2 hoặc nửa phải "290x301"
        if ((c.LengthSamples?.Any(v => v.HasValue) ?? false)
            || c.LengthPass is not null || c.LengthNominal is not null
            || (c.LengthSampleTexts?.Any(t => !string.IsNullOrWhiteSpace(t)) ?? false)
            || !string.IsNullOrWhiteSpace(c.LengthSpec))
        {
            pending.Add(AddMeasure(insp, "KT-02", "KT", "Kích thước", "Size",
                "Chiều dài", "Length",
                measureCount: 5,
                samples: Pad5(c.LengthSamples ?? Array.Empty<double?>()),
                sampleTexts: c.LengthSampleTexts,
                low: c.LengthLow, up: c.LengthUp,
                unit: "mm",
                limitLabel: c.LengthSpec
                    ?? c.LengthNominal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                acceptanceVi: c.LengthSpec
                    ?? (c.LengthNominal is null ? null : $"TC {c.LengthNominal} mm"),
                pass: c.LengthPass));
        }

        // Dimension — thickness
        if (c.ThicknessSamples.Any(v => v.HasValue) || c.ThicknessPass is not null
            || !string.IsNullOrWhiteSpace(c.ThicknessSpec))
        {
            pending.Add(AddMeasure(insp, "KT-04", "KT", "Kích thước", "Size",
                "Độ dày", "Thickness",
                measureCount: 5,
                samples: Pad5(c.ThicknessSamples),
                sampleTexts: null,
                low: null, up: null,
                unit: "mm",
                limitLabel: c.ThicknessSpec,
                acceptanceVi: c.ThicknessSpec,
                pass: c.ThicknessPass));
        }

        // Functional — Adhesive + Hardness
        if (!string.IsNullOrWhiteSpace(c.FuncSpec) || c.FuncPass is not null)
        {
            var hardness = LooksLikeHardness(c.FuncSpec);
            AddVerdict(insp, "BD-01", "BD", "Độ bám dính", "Adhesion",
                "Độ bám dính keo (Adhesive)", "Adhesive / peel",
                hardness ? null : c.FuncPass,
                measured: hardness ? null : c.FuncSpec,
                acceptanceVi: hardness ? null : c.FuncSpec);
            AddVerdict(insp, "CU-01", "CU", "Độ cứng bút chì", "Pencil hardness",
                "Độ cứng (Hardness)", "Hardness",
                hardness ? c.FuncPass : null,
                measured: hardness ? c.FuncSpec : null,
                acceptanceVi: hardness ? c.FuncSpec : null);
        }

        // Lab L-a-b
        if ((c.LabSheets?.Any(v => v.HasValue) ?? false) || c.LabPass is not null
            || !string.IsNullOrWhiteSpace(c.LabSpec))
        {
            pending.Add(AddMeasure(insp, "LB-01", "LB", "Lab L-a-b", "Lab L-a-b",
                "L-a-b", "L-a-b",
                measureCount: 5,
                samples: Pad5(c.LabSheets ?? Array.Empty<double?>()),
                sampleTexts: null,
                low: null, up: null,
                unit: null,
                limitLabel: c.LabSpec,
                acceptanceVi: c.LabSpec,
                pass: c.LabPass));
        }

        return pending;
    }

    private static bool LooksLikeHardness(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var s = spec.Trim().ToUpperInvariant();
        if (s.Contains("N/", StringComparison.Ordinal) || s.Contains("N /", StringComparison.Ordinal))
            return false;
        if (s.Contains("KEO", StringComparison.Ordinal) || s.Contains("PEEL", StringComparison.Ordinal)
            || s.Contains("ADHES", StringComparison.Ordinal))
            return false;
        return s.Contains("CỨNG", StringComparison.Ordinal) || s.Contains("CUNG", StringComparison.Ordinal)
               || s.Contains("HARD", StringComparison.Ordinal) || s.Contains("PENCIL", StringComparison.Ordinal)
               || RegexHardnessGrade(s);
    }

    private static bool RegexHardnessGrade(string s)
        => System.Text.RegularExpressions.Regex.IsMatch(s, @"\b([0-9]?H|[0-9]?B|HB|F)\b");

    private static IReadOnlyList<double?> Pad5(IReadOnlyList<double?> src)
    {
        var a = new double?[5];
        for (var i = 0; i < 5; i++)
            a[i] = i < src.Count ? src[i] : null;
        return a;
    }

    private static void AddDefect(IqcInspection insp, string key, int? count, bool? visualOverall)
    {
        var pass = count is null ? visualOverall : count == 0;
        if (count is null && visualOverall is null) return;
        var d = BaseDetail(key, "NQ", "Ngoại quan", "External inspection",
            LabelOfDefect(key), LabelOfDefectEn(key), IqcCheckKind.DefectCount, 0);
        d.DefectCount = count;
        d.Pass = pass;
        d.Qty = count ?? 0;
        insp.Details.Add(d);
    }

    private static void AddVerdict(
        IqcInspection insp, string key, string group, string gVi, string gEn,
        string labelVi, string labelEn, bool? pass,
        string? measured = null, string? acceptanceVi = null)
    {
        var d = BaseDetail(key, group, gVi, gEn, labelVi, labelEn, IqcCheckKind.Verdict, 0);
        d.Pass = pass;
        d.MeasuredValue = Trunc(measured, 256);
        d.AcceptanceVi = Trunc(acceptanceVi, 1024);
        d.AcceptanceEn = Trunc(acceptanceVi, 1024);
        insp.Details.Add(d);
    }

    private static (IqcResultDetail Detail, IReadOnlyList<double?> Samples) AddMeasure(
        IqcInspection insp, string key, string group, string gVi, string gEn,
        string labelVi, string labelEn, int measureCount,
        IReadOnlyList<double?> samples, IReadOnlyList<string?>? sampleTexts,
        double? low, double? up, string? unit, string? limitLabel,
        string? acceptanceVi, bool? pass)
    {
        var d = BaseDetail(key, group, gVi, gEn, labelVi, labelEn, IqcCheckKind.Measure, measureCount);
        d.LimitLow = low;
        d.LimitUp = up;
        d.LimitUnit = Trunc(unit, 32);
        d.LimitLabel = Trunc(limitLabel, 64);
        d.AcceptanceVi = Trunc(acceptanceVi, 1024);
        d.AcceptanceEn = Trunc(acceptanceVi, 1024);
        d.Pass = pass;
        if (sampleTexts is { Count: > 0 } && sampleTexts.Any(t => !string.IsNullOrWhiteSpace(t)))
            d.MeasuredValue = Trunc(string.Join(" | ", sampleTexts.Where(t => !string.IsNullOrWhiteSpace(t))!), 256);

        insp.Details.Add(d);
        return (d, samples);
    }

    private static IqcResultDetail BaseDetail(
        string key, string group, string gVi, string gEn,
        string labelVi, string labelEn, IqcCheckKind kind, int measureCount)
        => new()
        {
            ItemKey = key,
            GroupCode = group,
            GroupLabelVi = gVi,
            GroupLabelEn = gEn,
            LabelVi = labelVi,
            LabelEn = labelEn,
            ItemName = labelVi,
            Kind = kind,
            MeasureCount = measureCount,
            MethodVi = DetailSourceTag,
            MethodEn = DetailSourceTag,
            CreatedBy = DetailSourceTag,
            CreatedAt = DateTime.UtcNow,
        };

    private static string LabelOfDefect(string key) => key switch
    {
        "RD-01" => "Nhăn / Hằn", "RD-02" => "Xô, lỏng", "RD-03" => "Tràn keo",
        "RD-04" => "Loang", "RD-05" => "Xước", "RD-06" => "Biến dạng",
        "RD-07" => "Màu sắc", "RD-08" => "Dị vật", "RD-09" => "Bẩn",
        "RD-10" => "Rỗ / Thủng", "RD-11" => "Lệch", "RD-12" => "Bavia",
        "RD-13" => "Lỗi khác",
        "PD-01" => "Nhăn", "PD-02" => "Hằn", "PD-03" => "Loang",
        "PD-04" => "Xước", "PD-05" => "Màu sắc", "PD-06" => "Dị vật",
        "PD-07" => "Bẩn", "PD-08" => "Biến dạng", "PD-09" => "Bavia",
        _ => key,
    };

    private static string LabelOfDefectEn(string key) => key switch
    {
        "RD-01" => "Wrinkle / dent", "RD-02" => "Shifted / loose", "RD-03" => "Adhesive bleed",
        "RD-04" => "Blotch", "RD-05" => "Scratch", "RD-06" => "Deformation",
        "RD-07" => "Colour", "RD-08" => "Foreign matter", "RD-09" => "Dirt",
        "RD-10" => "Pinhole", "RD-11" => "Misalignment", "RD-12" => "Burr",
        "RD-13" => "Other defect",
        "PD-01" => "Wrinkle", "PD-02" => "Dent", "PD-03" => "Blotch",
        "PD-04" => "Scratch", "PD-05" => "Colour", "PD-06" => "Foreign matter",
        "PD-07" => "Dirt", "PD-08" => "Deformation", "PD-09" => "Burr",
        _ => key,
    };

    private static string? JoinParts(params string?[] parts)
        => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static QcResult? ParseJudgment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        if (s is "OK" or "PASS" or "ĐẠT" or "DAT") return QcResult.Pass;
        if (s is "NG" or "FAIL" or "N.G" or "KHÔNG ĐẠT" or "KHONG DAT") return QcResult.Fail;
        return null;
    }

    private static string NormalizeSheet(string sheet) => sheet.Trim() switch
    {
        var s when s.Equals("Roll", StringComparison.OrdinalIgnoreCase) => "Roll",
        var s when s.Equals("PCS", StringComparison.OrdinalIgnoreCase) => "PCS",
        var s when s.Equals("Chem", StringComparison.OrdinalIgnoreCase) => "Chem",
        var s when s.Equals("Tool", StringComparison.OrdinalIgnoreCase) => "Tool",
        _ => sheet.Trim(),
    };

    private static (string Group, IqcMaterialCategory Cat) MapGroup(string sheet) => sheet switch
    {
        "Roll" => (IqcGroup.Materials, IqcMaterialCategory.Roll),
        "PCS" => (IqcGroup.Materials, IqcMaterialCategory.Pcs),
        "Chem" => (IqcGroup.Chemical, IqcMaterialCategory.Chem),
        "Tool" => (IqcGroup.Tools, IqcMaterialCategory.Tool),
        _ => (IqcGroup.Materials, IqcMaterialCategory.Any),
    };

    private static string? FirstNonEmpty(params string?[] vals)
    {
        foreach (var v in vals)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    private static string? Trunc(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
