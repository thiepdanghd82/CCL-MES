using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.IqcMaster;

/// <summary>
/// Đọc file tiêu chuẩn IQC dạng Form
/// (<c>CCL-SPEC-QC001 - … (R03) - SW-7325F.xlsx</c>):
/// header (SpecNo · Mother · Supplier · Revision) + lưới hạng mục (cột Test items /
/// Testing standards / Note) map sang <c>ItemId</c> thư viện.
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
        @"^(.*?)\((\d{7,})\s*\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Kim chỉ nam Form → ItemId. Kim dài / đặc hiệu đứng trước.</summary>
    private static readonly (string Needle, string ItemId)[] ItemMap =
    {
        ("độ dày", "KT-04"),
        ("chieu day", "KT-04"),
        ("chiều rộng", "KT-03"),
        ("chieu rong", "KT-03"),
        ("chiều dài", "KT-02"),
        ("chieu dai", "KT-02"),
        ("kích thước", "KT-01"),
        ("kich thuoc", "KT-01"),
        ("tem nhãn", "NQ-01"),
        ("tem nhan", "NQ-01"),
        ("màu sắc", "NQ-02"),
        ("mau sac", "NQ-02"),
        ("bụi bẩn", "NQ-03"),
        ("bui ban", "NQ-03"),
        ("vết xước", "NQ-04"),
        ("vet xuoc", "NQ-04"),
        ("lỗi khác", "NQ-05"),
        ("loi khac", "NQ-05"),
        ("đóng gói", "NQ-06"),
        ("dong goi", "NQ-06"),
        ("hsf", "MT-02"),
        ("rohs", "MT-01"),
        ("chất cấm", "MT-03"),
        ("bám dính", "BD-01"),
        ("bam dinh", "BD-01"),
        ("keo của nguyên liệu", "BD-01"),
        ("keo cua nguyen lieu", "BD-01"),
        ("độ cứng", "CU-01"),
        ("do cung", "CU-01"),
        ("xuyên sáng", "XS-01"),
        ("xuyen sang", "XS-01"),
        ("định lượng", "TL-01"),
        ("dinh luong", "TL-01"),
        ("độ bóng", "BO-01"),
        ("do bong", "BO-01"),
        ("kiểm tra vật liệu", "NL-01"),
        ("kiem tra vat lieu", "NL-01"),
        ("nhận dạng", "NL-01"),
    };

    public sealed record FormItem(
        string ItemId,
        int Seq,
        string? AcceptanceVi,
        string? MethodVi,
        string? SourceLabel);

    public sealed record ParsedFile(
        string SpecNo,
        string MaterialCode,
        string? MaterialCodeIfs,
        string? Revision,
        string? SupplierName,
        string FileName,
        string FullPath,
        IReadOnlyList<FormItem> Items);

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
            Path.GetFullPath(pathOrName), Array.Empty<FormItem>());
        return true;
    }

    public static string NormalizeSpecNo(string prefix, string digits)
    {
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return (prefix + digits).ToUpperInvariant();
        return n < 1000
            ? $"CCL-SPEC-QC{n:D3}"
            : $"CCL-SPEC-QC{n}";
    }

    /// <summary>Quét folder: mỗi file Form thật → header + hạng mục.</summary>
    public static IReadOnlyList<ParsedFile> ScanFolder(string folderPath, bool readFormContent = true)
    {
        if (!Directory.Exists(folderPath)) return Array.Empty<ParsedFile>();
        var list = new List<ParsedFile>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*.xlsx"))
        {
            if (!TryParseFileName(path, out var p) || p is null) continue;
            list.Add(readFormContent ? ReadFormContent(p) : p);
        }
        return list.OrderBy(x => x.SpecNo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Đọc sheet Form: header + lưới hạng mục từ hàng 7.</summary>
    public static ParsedFile ReadFormContent(ParsedFile fromName)
    {
        try
        {
            using var wb = new XLWorkbook(fromName.FullPath);
            if (!wb.Worksheets.TryGetWorksheet("Form", out var ws)
                && !wb.Worksheets.TryGetWorksheet("FORM", out ws))
                return fromName;

            var formSpec = Cell(ws, 1, 3);
            var formMat = Cell(ws, 5, 5);
            var supplier = Cell(ws, 5, 14) ?? FindSupplier(ws);

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

            var items = ParseItemGrid(ws);
            return fromName with
            {
                SpecNo = specNo,
                MaterialCode = material,
                SupplierName = supplier ?? fromName.SupplierName,
                Items = items,
            };
        }
        catch
        {
            return fromName;
        }
    }

    /// <summary>Giữ API cũ — chỉ enrich header.</summary>
    public static ParsedFile EnrichFromWorkbook(ParsedFile fromName)
        => ReadFormContent(fromName) with { Items = fromName.Items };

    private static IReadOnlyList<FormItem> ParseItemGrid(IXLWorksheet ws)
    {
        var seqByItem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FormItem>();
        var emptyStreak = 0;

        for (var r = 7; r <= 80; r++)
        {
            var label = FirstLine(Cell(ws, r, 5));
            var standard = FirstLine(Cell(ws, r, 8));
            var note = FirstLine(Cell(ws, r, 12));

            if (string.IsNullOrWhiteSpace(label))
            {
                emptyStreak++;
                if (emptyStreak >= 3 && list.Count > 0) break;
                continue;
            }
            emptyStreak = 0;

            var itemId = MapItemId(label);
            seqByItem.TryGetValue(itemId, out var seq);
            seq++;
            seqByItem[itemId] = seq;

            list.Add(new FormItem(
                itemId,
                seq,
                Trunc(standard, 1024),
                Trunc(note, 512),
                Trunc(label, 256)));
        }

        return list;
    }

    public static string MapItemId(string label)
    {
        var key = label.Trim().ToLowerInvariant();
        foreach (var (needle, id) in ItemMap)
        {
            if (key.Contains(needle, StringComparison.Ordinal))
                return id;
        }
        return "KH-01";
    }

    private static string? FindSupplier(IXLWorksheet ws)
    {
        for (var r = 5; r <= 6; r++)
        for (var c = 10; c <= 14; c++)
        {
            var v = Cell(ws, r, c);
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (v.Contains("Supplier", StringComparison.OrdinalIgnoreCase)
                || v.Contains("nhà cung cấp", StringComparison.OrdinalIgnoreCase)
                || v.Contains("Nha cung cap", StringComparison.OrdinalIgnoreCase)
                || v.Contains("Tên", StringComparison.OrdinalIgnoreCase))
                continue;
            if (v.Length > 3 && v.Length < 200)
                return FirstLine(v);
        }
        return null;
    }

    private static string? FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var line = s.Split('\n', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private static string? Trunc(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }

    private static string? Cell(IXLWorksheet ws, int row, int col)
    {
        var v = ws.Cell(row, col).GetString()?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
