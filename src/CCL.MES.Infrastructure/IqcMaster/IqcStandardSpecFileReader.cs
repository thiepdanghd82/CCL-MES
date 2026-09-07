using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.IqcMaster;

/// <summary>
/// Đọc một file tiêu chuẩn IQC dạng Form
/// (<c>CCL-SPEC-QC001 - … (R03) - SW-7325F.xlsx</c>).
/// Chỉ lấy header (SpecNo · Mother · Supplier · Revision) — hạng mục chi tiết
/// đã nằm trong seed CSV P12; không parse lại lưới Form.
/// </summary>
public static class IqcStandardSpecFileReader
{
    public const string ImportSourceTag = "iqc-std-folder-2026";

    private static readonly Regex SpecNoRx = new(
        @"^(CCL-SPEC-QC)(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RevisionRx = new(
        @"\((R\d{2})\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IfsInParensRx = new(
        @"^(.*?)\((\d{7,})\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record ParsedFile(
        string SpecNo,
        string MaterialCode,
        string? MaterialCodeIfs,
        string? Revision,
        string? SupplierName,
        string FileName,
        string FullPath);

    /// <summary>Parse tên file. Bỏ template QC00X / Copy / List.</summary>
    public static bool TryParseFileName(string pathOrName, out ParsedFile? parsed)
    {
        parsed = null;
        var fileName = Path.GetFileName(pathOrName);
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Contains("QC00X", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Contains("Copy", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.StartsWith("List", StringComparison.OrdinalIgnoreCase)) return false;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var m = SpecNoRx.Match(stem);
        if (!m.Success) return false;

        var specNo = NormalizeSpecNo(m.Groups[1].Value, m.Groups[2].Value);
        string? revision = null;
        var revM = RevisionRx.Match(stem);
        if (revM.Success) revision = revM.Groups[1].Value.ToUpperInvariant();

        var parts = stem.Split(" - ", StringSplitOptions.None);
        var rawMother = parts.Length >= 2 ? parts[^1].Trim() : "";
        if (string.IsNullOrWhiteSpace(rawMother) || rawMother.Equals("X", StringComparison.OrdinalIgnoreCase))
            return false;

        string material = rawMother;
        string? ifs = null;
        var ifsM = IfsInParensRx.Match(rawMother);
        if (ifsM.Success)
        {
            material = ifsM.Groups[1].Value.Trim();
            ifs = ifsM.Groups[2].Value;
        }

        parsed = new ParsedFile(specNo, material, ifs, revision, null, fileName,
            Path.GetFullPath(pathOrName));
        return true;
    }

    public static string NormalizeSpecNo(string prefix, string digits)
    {
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return (prefix + digits).ToUpperInvariant();
        // Catalog sống dùng QC001…QC809 (3 chữ số). QC0192 → QC192.
        return n < 1000
            ? $"CCL-SPEC-QC{n:D3}"
            : $"CCL-SPEC-QC{n}";
    }

    public static IReadOnlyList<ParsedFile> ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return Array.Empty<ParsedFile>();
        var list = new List<ParsedFile>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*.xlsx"))
        {
            if (!TryParseFileName(path, out var p) || p is null) continue;
            // Chỉ parse tên file khi quét hàng loạt — mở 400+ workbook Form
            // làm import chậm hàng phút. EnrichFromWorkbook dùng khi cần NCC.
            list.Add(p);
        }
        return list.OrderBy(x => x.SpecNo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Đọc sheet Form: xác nhận SpecNo · Supplier · Material nếu trống.</summary>
    public static ParsedFile EnrichFromWorkbook(ParsedFile fromName)
    {
        try
        {
            using var wb = new XLWorkbook(fromName.FullPath);
            if (!wb.Worksheets.TryGetWorksheet("Form", out var ws)
                && !wb.Worksheets.TryGetWorksheet("FORM", out ws))
                return fromName;

            var formSpec = Cell(ws, 1, 3);
            var formMat = Cell(ws, 5, 5);
            string? supplier = null;
            // Hàng 5: nhãn NCC thường ở cột 11, giá trị cạnh / dưới — quét hàng 5–6.
            for (var r = 5; r <= 6; r++)
            for (var c = 10; c <= 14; c++)
            {
                var v = Cell(ws, r, c);
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (v.Contains("Supplier", StringComparison.OrdinalIgnoreCase)
                    || v.Contains("nhà cung cấp", StringComparison.OrdinalIgnoreCase)
                    || v.Contains("Nha cung cap", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (v.Length > 3 && v.Length < 200
                    && !v.Contains("Tên", StringComparison.OrdinalIgnoreCase))
                {
                    supplier = v;
                    break;
                }
            }

            var material = fromName.MaterialCode;
            if (!string.IsNullOrWhiteSpace(formMat)
                && formMat.Length < 200
                && !formMat.Contains('\n'))
                material = formMat.Trim();

            var specNo = fromName.SpecNo;
            if (!string.IsNullOrWhiteSpace(formSpec) && SpecNoRx.IsMatch(formSpec.Trim()))
            {
                var m = SpecNoRx.Match(formSpec.Trim());
                specNo = NormalizeSpecNo(m.Groups[1].Value, m.Groups[2].Value);
            }

            return fromName with
            {
                SpecNo = specNo,
                MaterialCode = material,
                SupplierName = supplier ?? fromName.SupplierName,
            };
        }
        catch
        {
            return fromName;
        }
    }

    private static string? Cell(IXLWorksheet ws, int row, int col)
    {
        var v = ws.Cell(row, col).GetString()?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
