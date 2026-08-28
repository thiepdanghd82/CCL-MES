using System.Text;

namespace CCL.MES.Application.Services;

/// <summary>
/// Parser thuần cho ba file CSV master của thư viện IQC. RFC-4180 quote-aware
/// (ô có dấu phẩy hoặc xuống dòng), bỏ BOM, bỏ header, bỏ dòng khoá rỗng.
///
/// <para>Map theo <b>VỊ TRÍ CỘT</b> — đã đối soát với file thật trước khi viết
/// (cảnh báo của skill <c>cmes-defect-library-import</c>: header nhiều dòng làm
/// map theo tên rất dễ lệch). Thứ tự cột được khoá bằng
/// <c>IqcLibraryCsvTests</c>; đổi thứ tự trong file mà quên sửa ở đây thì test
/// ĐỎ, không im lặng nhét sai cột.</para>
/// </summary>
public static class IqcLibraryCsv
{
    public sealed record ItemRow(
        string ItemId, string GroupCode, string GroupLabelVi, string? GroupLabelEn,
        string ItemVi, string? ItemEn, int Sort);

    public sealed record SpecRow(
        string SpecNo, string MaterialCode, string? MaterialCodeIfs,
        string? SupplierName, string? Revision);

    public sealed record SpecItemRow(
        string SpecNo, string ItemId, int Seq,
        string? AcceptanceVi, string? AcceptanceEn,
        string? MethodVi, string? MethodEn,
        string? SourceFrequency, int Sort);

    // ── 21 hạng mục: ItemId · GroupCode · GroupLabelVi · GroupLabelEn · ItemVi · ItemEn · Sort
    public static IReadOnlyList<ItemRow> ParseItems(string csv) =>
        Rows(csv, 7)
            .Where(f => !string.IsNullOrWhiteSpace(f[0]))
            .Select((f, i) => new ItemRow(
                f[0].Trim(), f[1].Trim(), f[2].Trim(), Null(f[3]),
                f[4].Trim(), Null(f[5]), Int(f[6], (i + 1) * 10)))
            .ToList();

    // ── 459 spec: SpecNo · MaterialCode · MaterialCodeIfs · SupplierName · Revision
    public static IReadOnlyList<SpecRow> ParseSpecs(string csv) =>
        Rows(csv, 5)
            .Where(f => !string.IsNullOrWhiteSpace(f[0]) && !string.IsNullOrWhiteSpace(f[1]))
            .Select(f => new SpecRow(
                f[0].Trim(), f[1].Trim(), Null(f[2]), Null(f[3]), Null(f[4])))
            .ToList();

    // ── 5 961 dòng: SpecNo · ItemId · Seq · AcceptanceVi/En · MethodVi/En
    //    · SourceFrequency · Sort
    //
    // Seq phân biệt nhiều tiêu chí cùng mang một mã hạng mục trong cùng spec
    // (12 cặp trong dữ liệu thật). Thiếu nó ⇒ mất 13 tiêu chí kiểm.
    public static IReadOnlyList<SpecItemRow> ParseSpecItems(string csv) =>
        Rows(csv, 9)
            .Where(f => !string.IsNullOrWhiteSpace(f[0]) && !string.IsNullOrWhiteSpace(f[1]))
            .Select((f, i) => new SpecItemRow(
                f[0].Trim(), f[1].Trim(), Int(f[2], 1), Null(f[3]), Null(f[4]),
                Null(f[5]), Null(f[6]), Null(f[7]), Int(f[8], i + 1)))
            .ToList();

    private static string? Null(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static int Int(string s, int fallback) =>
        int.TryParse(s?.Trim(), out var n) ? n : fallback;

    /// <summary>Đọc CSV thành các dòng đã đủ <paramref name="minCols"/> cột;
    /// dòng thiếu cột được đệm rỗng để index không bao giờ văng.</summary>
    private static IEnumerable<string[]> Rows(string csv, int minCols)
    {
        if (string.IsNullOrWhiteSpace(csv)) yield break;
        var first = true;
        foreach (var raw in SplitRecords(csv.TrimStart('﻿')))
        {
            if (first) { first = false; continue; }          // header
            if (raw.Length == 0) continue;
            var f = raw;
            if (f.Length < minCols)
            {
                var padded = new string[minCols];
                Array.Copy(f, padded, f.Length);
                for (var i = f.Length; i < minCols; i++) padded[i] = "";
                f = padded;
            }
            yield return f;
        }
    }

    /// <summary>Tách bản ghi CSV theo RFC-4180: dấu nháy kép bọc ô, <c>""</c> là
    /// một dấu nháy literal, và xuống dòng BÊN TRONG ô không kết thúc bản ghi.</summary>
    private static IEnumerable<string[]> SplitRecords(string csv)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': fields.Add(sb.ToString()); sb.Clear(); break;
                case '\r': break;
                case '\n':
                    fields.Add(sb.ToString()); sb.Clear();
                    yield return fields.ToArray();
                    fields.Clear();
                    break;
                default: sb.Append(c); break;
            }
        }

        if (sb.Length > 0 || fields.Count > 0)
        {
            fields.Add(sb.ToString());
            yield return fields.ToArray();
        }
    }
}
