using System.Text.RegularExpressions;
using CCL.MES.Application.SpecImport;
using ClosedXML.Excel;
using static CCL.MES.Infrastructure.SpecImport.XlsxParserHelpers;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// Phase 8 PR #31b — Port `parseXlsxFlexo` từ SpecHub HTML:11647-11848.
///
/// Tham chiếu READ-ONLY: `/Volumes/.../SpecHub/spechub-prototype.html`. Cell
/// layout flexo khác hẳn silkscreen:
///   - HEADER R1-R5 (dynamic label-anchored scan — chung silkscreen)
///   - PRODUCT INFO R4 fixed: C1 customer / C4 partNo / C10 partName / C21 version
///     / C25 sizeW / C29 sizeH / C32 diameter
///   - PRINTING ROWS R7+ until "thông tin cắt": C1 process / C3 material /
///     C6 thickness / C9 size / C13 cylinders / C15 pitch / C17 speed /
///     C20 tensionHead / C23 tensionEnd / C26 tensionRoll / C29 plateCavity /
///     C32 tension (12 cols)
///   - CUTTING ROWS after "thông tin cắt" +2: 14 cols (C1 process / C3 lamination
///     / C4 size / C6 cutterLot / C9 cutterName / C13 pcsPerSheet / C14 cuttingCavity
///     / C16 pitch / C17 packing / C19 paperSpeed / C21 cuttingSpeed / C24
///     cuttingPressure / C29 headTension / C33 rollTension)
///   - INK ROWS after "thông tin mực" +2: 10 cols (C1 no / C2 color / C4 inkCode /
///     C7 inkDesc / C15 brand / C17 anilox / C21 plateCode / C24 pressure /
///     C27 uvPower / C31 irPower)
///
/// Caller chọn parser via category-keyed factory ở DI (SpecXlsxParserFactory).
/// </summary>
public class FlexoXlsxParser : ISpecXlsxParser
{
    public ParsedSpecDto Parse(Stream xlsxStream, string forcedCategory)
    {
        using var normalized = XlsxNormalizer.Normalize(xlsxStream);
        using var wb = new XLWorkbook(normalized);
        var ws = wb.Worksheet(1);
        var aoa = WorksheetToAoa(ws);

        var detected = DetectSpecCategory(aoa);
        var category = string.IsNullOrWhiteSpace(forcedCategory) ? detected : forcedCategory;

        var spec = ParseFlexo(aoa);
        spec.Category = category;

        if (!string.Equals(category, "flexo", StringComparison.OrdinalIgnoreCase))
        {
            spec.Warnings.Add(
                $"Planner '{category}' không khớp parser flexo — KHÔNG nên gọi FlexoXlsxParser trực tiếp.");
        }
        if (string.Equals(forcedCategory, "flexo", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(detected, "flexo", StringComparison.OrdinalIgnoreCase))
        {
            spec.Warnings.Add($"File header gợi ý category='{detected}' nhưng operator chọn flexo — dùng lựa chọn operator.");
        }

        return spec;
    }

    private static ParsedSpecDto ParseFlexo(string[][] aoa)
    {
        var spec = new ParsedSpecDto { Category = "flexo" };

        // HEADER — dynamic label-anchored scan (chung silkscreen)
        var refNoLabel = FindValueAfterLabel(aoa, new Regex(@"số tham chiếu|ref\s*no", RegexOptions.IgnoreCase));
        spec.RefNo = !string.IsNullOrEmpty(refNoLabel)
            ? refNoLabel
            : FindCellMatching(aoa, new Regex(@"^CCL-(Silk|Seal|Flexo|Indigo|Letter)-", RegexOptions.IgnoreCase));

        var inspRaw = FindCellMatching(aoa, new Regex(@"^Spec\s+[A-Z]?\d+\w*$", RegexOptions.IgnoreCase));
        if (string.IsNullOrEmpty(inspRaw))
            inspRaw = FindValueAfterLabel(aoa, new Regex(@"mức độ kiểm tra|inspection level", RegexOptions.IgnoreCase));
        spec.InspectionLevel = string.IsNullOrEmpty(inspRaw)
            ? "A"
            : Regex.Replace(inspRaw, @"^Spec\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(spec.InspectionLevel)) spec.InspectionLevel = "A";

        var stamp = FindCellMatching(aoa, new Regex(@"^(approved|draft|in[\s_-]?review)$", RegexOptions.IgnoreCase));
        spec.ApprovalStamp = string.IsNullOrEmpty(stamp) ? "Approved" : stamp;

        var comp = FindCellMatching(aoa, new Regex(@"\brohs\b|\bcompliance\b|\bcompliant\b", RegexOptions.IgnoreCase));
        spec.Compliance = string.IsNullOrEmpty(comp) ? "RoHS Compliance" : comp;

        // PRODUCT INFO — R4 fixed cell layout (flexo template không có dynamic header row)
        spec.Customer        = GetCell(aoa, 4, 1);
        spec.PartNo          = GetCell(aoa, 4, 4);
        spec.PartName        = GetCell(aoa, 4, 10);
        spec.Version         = NullIfEmpty(GetCell(aoa, 4, 21));
        spec.ProductSizeW    = ParseDouble(GetCell(aoa, 4, 25));
        spec.ProductSizeH    = ParseDouble(GetCell(aoa, 4, 29));
        spec.Diameter        = ParseDouble(GetCell(aoa, 4, 32));

        // PRINTING ROWS — R7+ until "thông tin cắt" / "cutting information"
        // (12 cols fixed; SpecHub HTML:11668-11690)
        int printSeq = 0;
        for (int r = 7; r < 30; r++)
        {
            var c1 = GetCell(aoa, r, 1);
            var c1l = c1.ToLowerInvariant();
            if (string.IsNullOrEmpty(c1)) continue;
            if (c1l.Contains("thông tin cắt") || c1l.Contains("cutting information")) break;
            if (c1l.Contains("thông tin") && c1l.Contains("mực")) break;
            spec.FlexoPrintRows.Add(new ParsedFlexoPrintRowDto
            {
                Seq          = ++printSeq,
                Process      = NullIfEmpty(c1),
                Material     = NullIfEmpty(GetCell(aoa, r, 3)),
                Thickness    = NullIfEmpty(GetCell(aoa, r, 6)),
                Size         = NullIfEmpty(GetCell(aoa, r, 9)),
                Cylinders    = NullIfEmpty(GetCell(aoa, r, 13)),
                PitchMm      = NullIfEmpty(GetCell(aoa, r, 15)),
                Speed        = NullIfEmpty(GetCell(aoa, r, 17)),
                TensionHead  = NullIfEmpty(GetCell(aoa, r, 20)),
                TensionEnd   = NullIfEmpty(GetCell(aoa, r, 23)),
                TensionRoll  = NullIfEmpty(GetCell(aoa, r, 26)),
                PlateCavity  = NullIfEmpty(GetCell(aoa, r, 29)),
                Tension      = NullIfEmpty(GetCell(aoa, r, 32)),
            });
        }

        // CUTTING ROWS — find "thông tin cắt" / "cutting information" → +2 → data
        int cutStartRow = -1;
        for (int r = 1; r < 60; r++)
        {
            var c1l = GetCell(aoa, r, 1).ToLowerInvariant();
            if (c1l.Contains("thông tin cắt") || c1l.Contains("cutting information"))
            {
                cutStartRow = r + 2;
                break;
            }
        }
        int cutSeq = 0;
        if (cutStartRow > 0)
        {
            for (int r = cutStartRow; r < 60; r++)
            {
                var c1 = GetCell(aoa, r, 1);
                var c1l = c1.ToLowerInvariant();
                if (string.IsNullOrEmpty(c1)) continue;
                if (c1l.Contains("thông tin") && c1l.Contains("mực")) break;
                if (c1l.Contains("ink information")) break;
                spec.FlexoCuttingRows.Add(new ParsedFlexoCuttingRowDto
                {
                    Seq             = ++cutSeq,
                    Process         = NullIfEmpty(c1),
                    Lamination      = NullIfEmpty(GetCell(aoa, r, 3)),
                    Size            = NullIfEmpty(GetCell(aoa, r, 4)),
                    CutterLot       = NullIfEmpty(GetCell(aoa, r, 6)),
                    CutterName      = NullIfEmpty(GetCell(aoa, r, 9)),
                    PcsPerSheet     = ParseInt(GetCell(aoa, r, 13)),
                    CuttingCavity   = ParseInt(GetCell(aoa, r, 14)),
                    PitchMm         = ParseDouble(GetCell(aoa, r, 16)),
                    Packing         = NullIfEmpty(GetCell(aoa, r, 17)),
                    PaperSpeed      = ParseDouble(GetCell(aoa, r, 19)),
                    CuttingSpeed    = ParseDouble(GetCell(aoa, r, 21)),
                    CuttingPressure = ParseDouble(GetCell(aoa, r, 24)),
                    HeadTension     = ParseDouble(GetCell(aoa, r, 29)),
                    RollTension     = ParseDouble(GetCell(aoa, r, 33)),
                });
            }
        }

        // INK ROWS — find "thông tin mực" / "ink information" → +2 → data
        int inkStartRow = -1;
        for (int r = 1; r < 60; r++)
        {
            var c1l = GetCell(aoa, r, 1).ToLowerInvariant();
            if ((c1l.Contains("thông tin") && c1l.Contains("mực")) || c1l.Contains("ink information"))
            {
                inkStartRow = r + 2;
                break;
            }
        }
        int inkSeq = 0;
        if (inkStartRow > 0)
        {
            for (int r = inkStartRow; r < 60; r++)
            {
                var no = GetCell(aoa, r, 1);
                var c1l = no.ToLowerInvariant();
                if (string.IsNullOrEmpty(no)) continue;
                if (c1l.Contains("ghi ch") || c1l.Contains("remark") || c1l.Contains("lại") || c1l == "rev") break;
                if (!Regex.IsMatch(no, @"^\d")) break;

                if (!int.TryParse(Regex.Match(no, @"\d+").Value, out var seq)) seq = inkSeq + 1;
                spec.FlexoInkRows.Add(new ParsedFlexoInkRowDto
                {
                    Seq             = seq,
                    Color           = NullIfEmpty(GetCell(aoa, r, 2)),
                    InkCode         = NullIfEmpty(GetCell(aoa, r, 4)),
                    InkDescription  = NullIfEmpty(GetCell(aoa, r, 7)),
                    Brand           = NullIfEmpty(GetCell(aoa, r, 15)),
                    Anilox          = NullIfEmpty(GetCell(aoa, r, 17)),
                    PlateCode       = NullIfEmpty(GetCell(aoa, r, 21)),
                    Pressure        = ParseDouble(GetCell(aoa, r, 24)),
                    UvPowerW        = ParseDouble(GetCell(aoa, r, 27)),
                    IrPowerW        = ParseDouble(GetCell(aoa, r, 31)),
                });
                inkSeq++;
            }
        }

        // Remarks (silkscreen format compat — first column scan)
        for (int r = 5; r < 60; r++)
        {
            var c1l = GetCell(aoa, r, 1).ToLowerInvariant();
            if (c1l.Contains("ghi ch") || c1l.Contains("remark"))
            {
                var left = GetCell(aoa, r, 2);
                var right = GetCell(aoa, r, 20);
                spec.Remarks = string.IsNullOrEmpty(right) ? left : $"{left} | {right}";
                spec.RemarksLeft = NullIfEmpty(left);    // PR #31d persist print remarks
                spec.RemarksRight = NullIfEmpty(right);  // PR #31d persist cut remarks
                break;
            }
        }

        // Validations Q9 (mirror silkscreen)
        if (string.IsNullOrWhiteSpace(spec.Customer)) spec.Warnings.Add("Customer field missing — required.");
        if (string.IsNullOrWhiteSpace(spec.PartNo)) spec.Warnings.Add("Part No field missing — required.");
        if (string.IsNullOrWhiteSpace(spec.RefNo))
        {
            spec.RefNo = $"CCL-Flexo-{DateTime.UtcNow:yyyyMMddHHmmss}";
            spec.Warnings.Add($"RefNo missing in xlsx — synthesized as '{spec.RefNo}'.");
        }
        if (spec.FlexoInkRows.Count == 0)
            spec.Warnings.Add("Không parse được ink row nào — kiểm tra layout xlsx có khớp template flexo?");
        if (spec.FlexoCuttingRows.Count == 0)
            spec.Warnings.Add("Không parse được cutting row nào — kiểm tra section 'THÔNG TIN CẮT' có tồn tại?");

        return spec;
    }
}
