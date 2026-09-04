using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CCL.MES.Application.Services;

/// <summary>Ngưỡng số đã đọc được từ một chuỗi tiêu chuẩn.</summary>
/// <param name="Low">Cận dưới (đã tính từ dung sai). <c>null</c> = không chặn dưới.</param>
/// <param name="Up">Cận trên. <c>null</c> = không chặn trên.</param>
/// <param name="Nominal">Giá trị danh nghĩa nếu chuỗi có nêu.</param>
/// <param name="Unit">Đơn vị nguyên văn (<c>N/25mm</c>, <c>gf/in</c>, <c>mm</c>…).</param>
/// <param name="Label">Nhãn đứng trước nếu có (<c>Face</c>, <c>Adhesive</c>).</param>
/// <param name="TearIsPass">Chuỗi có ghi "or tear" — vật liệu rách cũng coi là đạt.</param>
/// <param name="SourceText">Chuỗi gốc, LUÔN giữ để hiện cho người kiểm.</param>
public readonly record struct IqcSpecLimit(
    double? Low, double? Up, double? Nominal,
    string? Unit, string? Label, bool TearIsPass, string SourceText);

/// <summary>
/// P13 — đọc chuỗi tiêu chuẩn của file master IQC thành ngưỡng số.
///
/// <para>Viết theo <b>937 chuỗi phân biệt</b> đo được trong file master 2026
/// (12.999 lần xuất hiện), không theo tưởng tượng. Mười khuôn thật:</para>
/// <list type="number">
///   <item><c>N/A</c> → không có tiêu chuẩn số</item>
///   <item><c>220</c> — số trần (tiêu chuẩn độ rộng; Roll có sẵn cột Low/Up riêng)</item>
///   <item><c>0.16±0.016</c> · <c>0.082 ±0.0082</c> — dung sai đối xứng</item>
///   <item><c>Face 0.05±0.005</c> — có nhãn đứng trước</item>
///   <item><c>392+0.4-0.2</c> — dung sai lệch</item>
///   <item><c>800 gf/in -160/+240</c> — danh nghĩa + dung sai lệch viết sau đơn vị</item>
///   <item><c>3-6g/25mm</c> · <c>0.4~1.0 kg/inch</c> · <c>1.0-5.5 N/25mm</c> — khoảng</item>
///   <item><c>270 N/m</c> · <c>6N/25mm</c> · <c>122 N/100 mm</c> — trị + đơn vị lực</item>
///   <item><c>≥7N/25mm</c> · <c>&gt;800gf/in</c> · <c>1000↑ gf/in</c> — có toán tử</item>
///   <item><c>420 N/m or tear</c> — đạt ngưỡng HOẶC vật liệu rách</item>
/// </list>
///
/// <para><b>Điểm dễ sai nhất — ngữ nghĩa mặc định của lực bám dính.</b> Chuỗi
/// <c>270 N/m</c> KHÔNG có toán tử, nhưng trong ngành keo dán nó luôn nghĩa là
/// "tối thiểu 270". Đọc thành "đúng bằng 270" thì mọi lô keo tốt hơn tiêu chuẩn
/// đều bị đánh trượt. Vì vậy: có ĐƠN VỊ LỰC ⇒ mặc định là cận DƯỚI. Còn số trần
/// không đơn vị thì KHÔNG đoán — trả về không có ngưỡng, để người chấm.</para>
///
/// <para>Thuần, không I/O. Đọc không được thì trả <c>null</c> chứ KHÔNG đoán
/// bừa: một ngưỡng bịa ra sẽ âm thầm đánh trượt hàng tốt hoặc cho qua hàng
/// hỏng, và không ai biết vì nó trông y hệt ngưỡng thật.</para>
/// </summary>
public static class IqcSpecLimitParser
{
    /// <summary>Đơn vị LỰC ⇒ ngữ nghĩa "tối thiểu". Đơn vị chiều dài (mm) thì
    /// không, vì kích thước là hai phía.</summary>
    private static readonly string[] ForceUnits =
        ["n/25mm", "n/20mm", "n/m", "n/cm", "n/100mm", "n/100 mm", "gf/in", "gf/25mm",
         "kg/inch", "kg/25mm", "g/25mm", "g/in", "n/in", "n/mm"];

    private const string Num = @"\d+(?:[.,]\d+)?";

    // MỌI nhóm đều đặt TÊN. .NET đánh số nhóm-có-tên SAU nhóm-không-tên, nên
    // trộn hai kiểu thì Groups[2] trỏ vào toán tử chứ không phải con số — bộ
    // đọc "chạy" nhưng đọc sai, và chỉ lộ khi đối chiếu với 937 chuỗi thật.

    // ≥ 7 N/25mm · > 750gf/in · <= 5 N/m
    private static readonly Regex RxOperator = new(
        $@"^(?<op>[≥≤><]=?)\s*(?<v>{Num})\s*(?<unit>\S.*)?$", RegexOptions.Compiled);

    // 1000↑ gf/in  ·  450↑ gf/in or tear
    private static readonly Regex RxUpArrow = new(
        $@"^(?<v>{Num})\s*(?<unit1>[^\d↑]*?)\s*↑\s*(?<unit2>.*)$", RegexOptions.Compiled);

    // [nhãn] 0.16 ± 0.016 [đơn vị]
    private static readonly Regex RxPlusMinus = new(
        $@"^(?<label>[^\d±]*?)[\s:]*(?<nom>{Num})\s*±\s*(?<tol>{Num})\s*(?<unit>[A-Za-z%µ][^\s]*(?:\s+[A-Za-z%µ][^\s]*)*)?$",
        RegexOptions.Compiled);

    // 392+0.4-0.2   (dung sai lệch, viết liền sau trị)
    private static readonly Regex RxAsymTight = new(
        $@"^(?<label>[^\d+\-]*?)[\s:]*(?<nom>{Num})\s*\+(?<up>{Num})\s*[-−](?<lo>{Num})\s*(?<unit>[A-Za-z%µ][^\s]*(?:\s+[A-Za-z%µ][^\s]*)*)?$",
        RegexOptions.Compiled);

    // 800 gf/in -160/+240   (dung sai lệch viết SAU đơn vị)
    private static readonly Regex RxAsymTail = new(
        $@"^(?<nom>{Num})\s*(?<unit>[A-Za-z/%µ]+(?:\s*[A-Za-z/%µ]+)?)\s*[-−](?<lo>{Num})\s*/\s*\+(?<up>{Num})$",
        RegexOptions.Compiled);

    // 3-6g/25mm · 0.4~1.0 kg/inch · 1.0-5.5 N/25mm
    private static readonly Regex RxRange = new(
        $@"^(?<lo>{Num})\s*[~–—-]\s*(?<up>{Num})\s*(?<unit>[^\d\s].*)?$", RegexOptions.Compiled);

    // 270 N/m · 6N/25mm · 122 N/100 mm
    private static readonly Regex RxValueUnit = new(
        $@"^(?<v>{Num})\s*(?<unit>[A-Za-z][A-Za-z/%µ]*(?:\s*\d*\s*[A-Za-z/%µ]+)*)$",
        RegexOptions.Compiled);

    /// <summary>Số trần, không đơn vị (<c>220</c>).</summary>
    private static readonly Regex RxBareNumber = new($@"^(?<v>{Num})$", RegexOptions.Compiled);

    /// <summary>
    /// Đọc một chuỗi tiêu chuẩn. <c>null</c> = không đọc được thành ngưỡng số
    /// (rỗng · <c>N/A</c> · "Tham khảo báo cáo" · số trần không đơn vị · dạng lạ).
    /// Caller phải để người chấm tay trong mọi trường hợp đó.
    /// </summary>
    public static IqcSpecLimit? Parse(string? raw)
    {
        var src = (raw ?? "").Trim();
        if (src.Length == 0) return null;

        var s = Normalise(src);
        if (s.Length == 0 || IsNoSpec(s)) return null;

        // "or tear" là điều kiện đạt THAY THẾ, tách ra trước khi đọc số.
        var tear = false;
        var m = Regex.Match(s, @"\s*or\s+tear\s*$", RegexOptions.IgnoreCase);
        if (m.Success) { tear = true; s = s[..m.Index].Trim(); }

        return TryOperator(s, tear, src)
            ?? TryUpArrow(s, tear, src)
            ?? TryPlusMinus(s, tear, src)
            ?? TryAsymTight(s, tear, src)
            ?? TryAsymTail(s, tear, src)
            ?? TryRange(s, tear, src)
            ?? TryValueUnit(s, tear, src);
        // RxBareNumber cố tình KHÔNG nằm trong chuỗi này — xem ghi chú lớp.
    }

    /// <summary>Số trần không đơn vị chỉ có nghĩa khi caller BIẾT nó là gì và
    /// cấp dung sai từ nơi khác (Roll có cột Low/Up riêng cho độ rộng). Tách
    /// thành hàm riêng để chỗ gọi phải nói rõ ý định.</summary>
    public static double? ParseBareNominal(string? raw)
    {
        var s = Normalise((raw ?? "").Trim());
        var m = RxBareNumber.Match(s);
        return m.Success && TryNum(m.Groups["v"].Value, out var v) ? v : null;
    }

    // ── từng khuôn ───────────────────────────────────────────────────────

    private static IqcSpecLimit? TryOperator(string s, bool tear, string src)
    {
        var m = RxOperator.Match(s);
        if (!m.Success || !TryNum(m.Groups["v"].Value, out var v)) return null;
        var unit = Clean(m.Groups["unit"].Value);
        var op = m.Groups["op"].Value;
        return op.StartsWith('≥') || op.StartsWith('>')
            ? new IqcSpecLimit(v, null, null, unit, null, tear, src)
            : new IqcSpecLimit(null, v, null, unit, null, tear, src);
    }

    private static IqcSpecLimit? TryUpArrow(string s, bool tear, string src)
    {
        var m = RxUpArrow.Match(s);
        if (!m.Success || !TryNum(m.Groups["v"].Value, out var v)) return null;
        // "↑" = trở lên.
        var u = Clean(m.Groups["unit2"].Value) ?? Clean(m.Groups["unit1"].Value);
        return new IqcSpecLimit(v, null, null, u, null, tear, src);
    }

    private static IqcSpecLimit? TryPlusMinus(string s, bool tear, string src)
    {
        var m = RxPlusMinus.Match(s);
        if (!m.Success) return null;
        if (!TryNum(m.Groups["nom"].Value, out var nom) || !TryNum(m.Groups["tol"].Value, out var tol))
            return null;
        return new IqcSpecLimit(nom - tol, nom + tol, nom,
            Clean(m.Groups["unit"].Value), Clean(m.Groups["label"].Value), tear, src);
    }

    private static IqcSpecLimit? TryAsymTight(string s, bool tear, string src)
    {
        var m = RxAsymTight.Match(s);
        if (!m.Success) return null;
        if (!TryNum(m.Groups["nom"].Value, out var nom)
            || !TryNum(m.Groups["up"].Value, out var up) || !TryNum(m.Groups["lo"].Value, out var lo))
            return null;
        return new IqcSpecLimit(nom - lo, nom + up, nom,
            Clean(m.Groups["unit"].Value), Clean(m.Groups["label"].Value), tear, src);
    }

    private static IqcSpecLimit? TryAsymTail(string s, bool tear, string src)
    {
        var m = RxAsymTail.Match(s);
        if (!m.Success) return null;
        if (!TryNum(m.Groups["nom"].Value, out var nom)
            || !TryNum(m.Groups["lo"].Value, out var lo) || !TryNum(m.Groups["up"].Value, out var up))
            return null;
        return new IqcSpecLimit(nom - lo, nom + up, nom,
            Clean(m.Groups["unit"].Value), null, tear, src);
    }

    private static IqcSpecLimit? TryRange(string s, bool tear, string src)
    {
        var m = RxRange.Match(s);
        if (!m.Success) return null;
        if (!TryNum(m.Groups["lo"].Value, out var lo) || !TryNum(m.Groups["up"].Value, out var up))
            return null;
        // "6-3" là lỗi gõ, không phải khoảng ngược — từ chối chứ đừng tự đảo.
        if (lo > up) return null;
        return new IqcSpecLimit(lo, up, null, Clean(m.Groups["unit"].Value), null, tear, src);
    }

    private static IqcSpecLimit? TryValueUnit(string s, bool tear, string src)
    {
        var m = RxValueUnit.Match(s);
        if (!m.Success || !TryNum(m.Groups["v"].Value, out var v)) return null;
        var unit = Clean(m.Groups["unit"].Value);

        // ĐIỂM MẤU CHỐT: chỉ đơn vị LỰC mới được hiểu ngầm là "tối thiểu".
        // Đơn vị khác (mm…) không đoán — trả null để người chấm.
        return IsForceUnit(unit)
            ? new IqcSpecLimit(v, null, null, unit, null, tear, src)
            : null;
    }

    // ── phụ trợ ──────────────────────────────────────────────────────────

    private static bool IsForceUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Replace(" ", "").ToLowerInvariant();
        return ForceUnits.Any(f => u == f.Replace(" ", ""));
    }

    /// <summary>Chuẩn hoá bề mặt: gộp khoảng trắng, đưa các biến thể dấu về một
    /// dạng, và đổi dấu phẩy thập phân kiểu VN (<c>0,3 ± 0,03</c>) thành dấu
    /// chấm — file master do người Việt gõ nên cả hai kiểu cùng tồn tại.</summary>
    private static string Normalise(string s)
    {
        // NFKC gom mọi biến thể toàn rộng / ký tự tương thích CJK về ASCII
        // trong MỘT bước: Ｎ→N, ㎜→mm, ＞→>, ／→/ … File master gõ trên nhiều
        // bàn phím (VN, JP, CN) nên các ký tự này lẫn khắp nơi. Bảng thay thủ
        // công luôn sót một ký tự và chỉ lộ khi gặp file thật.
        s = s.Normalize(NormalizationForm.FormKC);

        // NFKC KHÔNG gom mấy ký hiệu sau (chúng là ký tự toán học riêng, không
        // phải biến thể tương thích) nên phải tự quy đổi.
        s = s.Replace('\u00A0', ' ')
             .Replace("≧", "≥").Replace("≦", "≤")
             .Replace("−", "-").Replace("–", "-").Replace("—", "-");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        // Dấu phẩy CHỈ là dấu thập phân khi kẹp giữa hai chữ số.
        s = Regex.Replace(s, @"(?<=\d),(?=\d)", ".");
        return s.ToLowerInvariant() is "n/a" or "na" ? "n/a" : s;
    }

    /// <summary>Các cách viết "không có tiêu chuẩn số" gặp trong file master.</summary>
    private static bool IsNoSpec(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        return t is "n/a" or "na" or "-" or "--" or "x"
            || t.StartsWith("tham khảo", StringComparison.Ordinal)
            || t.StartsWith("theo ", StringComparison.Ordinal);
    }

    private static string? Clean(string? v)
    {
        var s = (v ?? "").Trim(' ', ':', '(', ')');
        return s.Length == 0 ? null : s;
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Replace(",", "."), NumberStyles.Float,
            CultureInfo.InvariantCulture, out v);
}
