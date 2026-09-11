using System.Globalization;
using System.Text.RegularExpressions;

namespace CCL.MES.Application.Services;

/// <summary>
/// Đọc cặp ô <c>Target</c> + <c>Tolerance</c> của tab QC Plans thành CẶN SỐ để
/// máy so được (P4, 2026-09-11).
///
/// <para><b>Vì sao cần.</b> <c>QcCriteria</c> có sẵn bốn cột số
/// <c>TargetValue</c> · <c>ToleranceMin</c> · <c>ToleranceMax</c> · <c>Unit</c>,
/// nhưng <c>SpecQcWindowService</c> chỉ ghi CHỮ vào <c>PassCriteria</c> và
/// <c>MeasureMethod</c> rồi để bốn cột số nguyên vẹn (comment "untouched" ở
/// dòng 251). Hệ quả: kỹ sư gõ "20 ± 0,5 mm" mà không gì so được với số người
/// kiểm đo ra — hạng mục đo lường vẫn phải chấm bằng mắt.</para>
///
/// <para><b>Đọc CẢ HAI ô, không chỉ một.</b> UI có hai cột riêng và người ta
/// gõ theo cách tự nhiên: Target "20", Tolerance "±0,5". Đọc riêng từng ô thì
/// "±0,5" vô nghĩa vì thiếu gốc. Ghép lại mới ra 19,5–20,5.</para>
///
/// <para><b>Không đọc được thì NÓI RA.</b> <see cref="Result.Parsed"/> false
/// kèm <see cref="Result.Reason"/> để UI hiện lại cho kỹ sư thấy hệ thống hiểu
/// hay không hiểu. Lưu lặng một ô không đọc được chính là hạng bug mà phiên này
/// gặp nhiều lần: giá trị nằm đúng ô, không ai kiểm, không gì báo.</para>
/// </summary>
public static class QcCriterionLimit
{
    /// <param name="Low">Cận dưới, null = không chặn dưới.</param>
    /// <param name="Up">Cận trên, null = không chặn trên.</param>
    /// <param name="Nominal">Giá trị danh nghĩa nếu suy được.</param>
    /// <param name="Unit">Đơn vị đọc được từ chuỗi, null nếu không ghi.</param>
    /// <param name="Parsed">Có ra được ÍT NHẤT một cận không.</param>
    /// <param name="Reason">Vì sao không đọc được — chỉ có nghĩa khi Parsed=false.</param>
    public readonly record struct Result(
        double? Low, double? Up, double? Nominal, string? Unit, bool Parsed, string? Reason);

    public const string ReasonEmpty      = "qc.limit.empty";
    public const string ReasonNoNumber   = "qc.limit.no_number";
    public const string ReasonAmbiguous  = "qc.limit.ambiguous";

    private static Result Fail(string reason) => new(null, null, null, null, false, reason);

    private const string Num = @"[-+]?\d+(?:[.,]\d+)?";

    // "±0,5 mm" · "+/- 0.5" · "+-0,5" — CHỈ dung sai, gốc nằm ở ô Target.
    private static readonly Regex TolOnly = new(
        $@"^\s*(?:±|\+\/-|\+-)\s*(?<tol>{Num})\s*(?<unit>[A-Za-zµ%°]+)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "20 +/- 0.5 mm" — dạng đầy đủ nhưng dùng +/- thay vì ±, rất hay gặp vì bàn phím.
    private static readonly Regex NomPlusMinusAscii = new(
        $@"^\s*(?<nom>{Num})\s*(?:\+\/-|\+-)\s*(?<tol>{Num})\s*(?<unit>[A-Za-zµ%°]+)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "min 18" · "max 22" · "tối thiểu 18" — một phía, bằng CHỮ.
    private static readonly Regex OneSidedWord = new(
        $@"^\s*(?<kw>min|max|tối\s*thiểu|tối\s*đa|không\s*quá|ít\s*nhất)\s*[:=]?\s*(?<v>{Num})\s*(?<unit>[A-Za-zµ%°]+)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Số theo kiểu vi-VN: dấu phẩy LÀ dấu thập phân.</summary>
    private static double? Num1(string? s) =>
        double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>
    /// Đọc cặp Target + Tolerance. Thứ tự thử, dừng ở cái đầu tiên ăn:
    /// <list type="number">
    ///   <item>Tolerance tự nó đã đủ (vd "20 ± 0,5 mm") ⇒ dùng luôn, bỏ qua Target.</item>
    ///   <item>Tolerance chỉ có dung sai (vd "±0,5") ⇒ lấy gốc từ Target.</item>
    ///   <item>Tolerance một phía bằng chữ (vd "max 22").</item>
    ///   <item>Target tự nó đã đủ (kỹ sư gõ hết vào một ô).</item>
    /// </list>
    /// </summary>
    public static Result Resolve(string? target, string? tolerance)
    {
        var tol = (tolerance ?? "").Trim();
        var tgt = (target ?? "").Trim();
        if (tol.Length == 0 && tgt.Length == 0) return Fail(ReasonEmpty);

        // ① Tolerance đã là một biểu thức đầy đủ — bộ đọc IQC đã kham dạng
        //    "n ± t [đơn vị]", "a – b", "≥ n", "≤ n".
        if (tol.Length > 0 && IqcSpecLimitParser.Parse(tol) is { } full)
            return new Result(full.Low, full.Up, full.Nominal, full.Unit, true, null);

        // ② "+/-" kiểu bàn phím, có gốc đứng trước.
        if (NomPlusMinusAscii.Match(tol) is { Success: true } m2
            && Num1(m2.Groups["nom"].Value) is { } n2 && Num1(m2.Groups["tol"].Value) is { } t2)
            return new Result(n2 - Math.Abs(t2), n2 + Math.Abs(t2), n2,
                NullIfBlank(m2.Groups["unit"].Value), true, null);

        // ③ CHỈ dung sai — gốc phải lấy từ ô Target. Không có gốc thì chịu:
        //    "±0,5" một mình không nói lên khoảng nào cả.
        if (TolOnly.Match(tol) is { Success: true } m3 && Num1(m3.Groups["tol"].Value) is { } t3)
        {
            var nom = NominalFrom(tgt);
            if (nom is null) return Fail(ReasonNoNumber);
            return new Result(nom - Math.Abs(t3), nom + Math.Abs(t3), nom,
                NullIfBlank(m3.Groups["unit"].Value) ?? UnitFrom(tgt), true, null);
        }

        // ④ Một phía bằng chữ.
        if (OneSidedWord.Match(tol) is { Success: true } m4 && Num1(m4.Groups["v"].Value) is { } v4)
        {
            var kw = m4.Groups["kw"].Value.ToLowerInvariant().Replace(" ", "");
            var isMin = kw is "min" or "tốithiểu" or "ítnhất";
            return new Result(isMin ? v4 : null, isMin ? null : v4, null,
                NullIfBlank(m4.Groups["unit"].Value), true, null);
        }

        // ⑤ Kỹ sư gõ hết vào ô Target, bỏ trống Tolerance.
        if (tgt.Length > 0 && IqcSpecLimitParser.Parse(tgt) is { } fromTarget)
            return new Result(fromTarget.Low, fromTarget.Up, fromTarget.Nominal,
                fromTarget.Unit, true, null);

        return Fail(ReasonNoNumber);
    }

    /// <summary>Gốc từ ô Target: một số trần ("20" · "20 mm"). Nhiều số thì
    /// KHÔNG đoán — "20x10" không nói được đang ràng buộc chiều nào.</summary>
    private static double? NominalFrom(string target)
    {
        var nums = Regex.Matches(target, Num).Select(x => Num1(x.Value)).Where(x => x is not null).ToList();
        return nums.Count == 1 ? nums[0] : null;
    }

    private static string? UnitFrom(string target)
    {
        var m = Regex.Match(target, $@"{Num}\s*(?<unit>[A-Za-zµ%°]+)");
        return m.Success ? NullIfBlank(m.Groups["unit"].Value) : null;
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
