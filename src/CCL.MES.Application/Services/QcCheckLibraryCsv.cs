using System.Text;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phương án C — Bước 1. Một dòng thư viện hạng mục kiểm đọc từ
/// <c>IPQC_Library_CMES_v2.csv</c> (19 cột). Thuần dữ liệu — không EF.
/// </summary>
public sealed class QcCheckLibraryRow
{
    public string ItemId { get; init; } = "";
    public string ProcessLine { get; init; } = "";
    public string GroupLabel { get; init; } = "";
    public string Code { get; init; } = "";
    public string ItemVi { get; init; } = "";
    public string ItemEn { get; init; } = "";
    public string AcceptanceVi { get; init; } = "";
    public string AcceptanceEn { get; init; } = "";
    public string? Method { get; init; }
    public string? Severity { get; init; }
    public string? Aql { get; init; }
    public string? Sampling { get; init; }
    public string? CheckType { get; init; }
    public string? DefectCode { get; init; }
    public string? ParetoPct { get; init; }
    public string? ShortForm { get; init; }
    public string? IsoRef { get; init; }
    public string? AppliesWhen { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// Parser RFC-4180 tối thiểu cho thư viện hạng mục kiểm. Hỗ trợ ô có dấu phẩy /
/// xuống dòng trong ngoặc kép + escape <c>""</c> (header thư viện có ô đa dòng
/// song ngữ). Map theo VỊ TRÍ cột 0..18 (đã đối soát file v2). Bỏ header + dòng
/// trống / ItemId rỗng.
/// </summary>
public static class QcCheckLibraryCsv
{
    public static IReadOnlyList<QcCheckLibraryRow> Parse(string text)
    {
        var records = ParseRecords(text);
        var rows = new List<QcCheckLibraryRow>();
        for (int i = 1; i < records.Count; i++)   // bỏ header (record 0)
        {
            var f = records[i];
            string G(int idx) => idx < f.Count ? f[idx].Trim() : "";
            var itemId = G(0);
            if (itemId.Length == 0) continue;
            rows.Add(new QcCheckLibraryRow
            {
                ItemId = itemId,
                ProcessLine = G(1),
                GroupLabel = G(2),
                Code = G(3),
                ItemVi = G(4),
                ItemEn = G(5),
                AcceptanceVi = G(6),
                AcceptanceEn = G(7),
                Method = NullIfEmpty(G(8)),
                Severity = NullIfEmpty(G(9)),
                Aql = NullIfEmpty(G(10)),
                Sampling = NullIfEmpty(G(11)),
                CheckType = NullIfEmpty(G(12)),
                DefectCode = NullIfEmpty(G(13)),
                ParetoPct = NullIfEmpty(G(14)),
                ShortForm = NullIfEmpty(G(15)),
                IsoRef = NullIfEmpty(G(16)),
                AppliesWhen = NullIfEmpty(G(17)),
                Note = NullIfEmpty(G(18)),
            });
        }
        return rows;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static List<List<string>> ParseRecords(string text)
    {
        // Bỏ BOM nếu có.
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var recs = new List<List<string>>();
        var cur = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false, sawContent = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; sawContent = true; break;
                    case ',': cur.Add(sb.ToString()); sb.Clear(); sawContent = true; break;
                    case '\r': break;   // CRLF → handled at \n
                    case '\n':
                        cur.Add(sb.ToString()); sb.Clear();
                        recs.Add(cur); cur = new List<string>(); sawContent = false;
                        break;
                    default: sb.Append(c); sawContent = true; break;
                }
            }
        }
        if (sb.Length > 0 || cur.Count > 0 || sawContent)
        {
            cur.Add(sb.ToString());
            recs.Add(cur);
        }
        return recs;
    }
}
