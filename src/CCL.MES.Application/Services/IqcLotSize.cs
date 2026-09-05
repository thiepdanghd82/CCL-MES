namespace CCL.MES.Application.Services;

/// <summary>
/// P13 bước 4 — cỡ lô cho AQL là <b>SỐ ĐƠN VỊ ĐẾM ĐƯỢC</b>, không phải con số
/// trong ô "số lượng".
///
/// <para><b>Vì sao phải tách ra.</b> Bảng AQL đếm đơn vị: "lô 91–150 thì lấy 20
/// mẫu" nghĩa là 20 <i>cái</i>. Đưa thẳng <c>Quantity</c> vào bảng thì một lô
/// 5.000 m² (3 cuộn) rơi vào bậc 11 và app đòi lấy 200 mẫu từ 3 cuộn. Không ai
/// làm được, và người kiểm sẽ học cách bỏ qua con số app đề xuất — mất luôn giá
/// trị của việc đề xuất.</para>
///
/// <para><b>Đo trên live 2026-09-05, 26 phiếu:</b></para>
/// <list type="bullet">
///   <item><c>rolls</c>/<c>Roll</c> 22 phiếu, số lượng 4–10 ⇒ ĐẾM ĐƯỢC</item>
///   <item><c>pcs</c> 1 phiếu, 100 ⇒ ĐẾM ĐƯỢC</item>
///   <item><c>m</c> 1 · <c>kg</c> 1 · <c>L</c> 1 ⇒ liên tục, KHÔNG đếm được</item>
/// </list>
/// <para>Bằng chứng luật này đúng: phiếu <c>pcs 100</c> có cỡ mẫu người ghi tay
/// là <b>20</b> — trùng đúng bậc 6 của bảng (91–150 ⇒ 20). Hai phiếu
/// <c>kg 250,5</c> và <c>L 50</c> ghi 10 và 5, KHÔNG theo bảng — vì kg với lít
/// không phải số đơn vị, và người kiểm đã tự quyết. App không được giả vờ biết
/// hơn họ ở đúng chỗ đó.</para>
///
/// <para>Không nhận ra đơn vị ⇒ trả <c>null</c> ⇒ KHÔNG đề xuất cỡ mẫu, và cũng
/// KHÔNG đòi lý do khi người kiểm tự điền: đòi giải trình cho một sai lệch so
/// với con số app chưa từng đưa ra là vô nghĩa.</para>
///
/// <para>Thuần, không I/O.</para>
/// </summary>
public static class IqcLotSize
{
    /// <summary>Đơn vị gọi tên một VẬT RỜI đếm được.</summary>
    private static readonly HashSet<string> Countable = new(StringComparer.OrdinalIgnoreCase)
    {
        "pcs", "pc", "piece", "pieces", "ea", "each", "unit", "units",
        "roll", "rolls", "sheet", "sheets", "set", "sets",
        "box", "boxes", "carton", "cartons", "pallet", "pallets",
        "bag", "bags", "can", "cans", "tin", "tins", "drum", "drums",
        "cuon", "cuộn", "tam", "tấm", "cai", "cái", "chiec", "chiếc",
        "thung", "thùng", "lon", "hop", "hộp",
    };

    /// <summary>Đơn vị đo LIÊN TỤC — chiều dài, diện tích, khối lượng, thể tích.
    /// Liệt kê tường minh chứ không suy ngược từ <see cref="Countable"/>: gặp
    /// đơn vị lạ thì phải trả "không biết", không được đoán là liên tục.</summary>
    private static readonly HashSet<string> Continuous = new(StringComparer.OrdinalIgnoreCase)
    {
        "m", "mm", "cm", "km", "mt", "meter", "metre", "meters", "metres",
        "m2", "m²", "sqm", "m3", "m³",
        "kg", "g", "mg", "ton", "tonne", "lb",
        "l", "lit", "liter", "litre", "liters", "litres", "ml", "cc",
        "yard", "yd", "ft", "feet", "inch", "in",
    };

    /// <summary>Đơn vị này có đếm được không. <c>null</c> = KHÔNG NHẬN RA —
    /// khác hẳn "biết là liên tục".</summary>
    public static bool? IsCountable(string? uom)
    {
        var u = (uom ?? "").Trim().Replace(" ", "");
        if (u.Length == 0) return null;
        if (Countable.Contains(u)) return true;
        if (Continuous.Contains(u)) return false;
        return null;
    }

    /// <summary>
    /// Cỡ lô dùng cho bảng AQL. <c>null</c> khi đơn vị không đếm được hoặc
    /// không nhận ra.
    /// </summary>
    /// <param name="quantity">Số lượng nhận, nguyên văn trên phiếu.</param>
    /// <param name="uom">Đơn vị của <paramref name="quantity"/>.</param>
    public static long? For(double quantity, string? uom)
    {
        if (IsCountable(uom) != true) return null;
        if (double.IsNaN(quantity) || double.IsInfinity(quantity)) return null;
        if (quantity < 1) return null;   // "0,5 cuộn" không phải một cỡ lô

        // Làm tròn XUỐNG: 10,7 cuộn là 10 cuộn nguyên cộng một phần cuộn, và
        // bậc AQL phải tính trên số cuộn thật lấy ra kiểm được.
        var n = Math.Floor(quantity);
        return n > long.MaxValue ? null : (long)n;
    }

    /// <summary>
    /// Cỡ mẫu ĐỀ XUẤT cho phiếu. <c>null</c> khi không suy được cỡ lô — lúc đó
    /// người kiểm tự điền và KHÔNG bị đòi lý do.
    /// </summary>
    public static int? SuggestSampleSize(double quantity, string? uom)
    {
        var lot = For(quantity, uom);
        if (lot is null) return null;
        var s = IqcSamplingTable.Suggest(lot.Value);
        return s > 0 ? s : null;
    }

    /// <summary>
    /// Cỡ mẫu người nhập có cần kèm lý do không (Henry chốt 2026-09-04: MỌI
    /// thay đổi so với đề xuất đều phải ghi lý do — cả nới lẫn siết, vì lấy
    /// nhiều hơn cũng là tốn công của xưởng và cần biết vì sao).
    /// </summary>
    /// <param name="suggested">Đề xuất; <c>null</c> = app không đề xuất được.</param>
    /// <param name="actual">Cỡ mẫu người nhập.</param>
    public static bool NeedsReason(int? suggested, int actual)
        => suggested is { } s && actual != s;
}
