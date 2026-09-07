using System.Globalization;

namespace CCL.MES.Application.Services;

/// <summary>
/// Ngày nhập kho đóng băng trên phiếu (ô Excel "ngày nhập kho", hiện nằm ở
/// <c>MeasuredValue</c> của hạng mục <c>NQ-01</c>) và hạn dùng suy ra từ nó.
///
/// <para>Ledger có HAI dạng ô: Roll ghi đủ <c>dd/MM/yyyy</c>, PCS ghi rút gọn
/// <c>d-MMM</c> (thiếu năm) — dạng rút gọn lấy năm của ngày về, vì hai ngày này
/// trên cùng một dòng ledger. Ô không đọc được ⇒ <c>null</c>, KHÔNG đoán.</para>
///
/// <para>Hạn dùng: ledger ghi "HSD: 1 Năm từ ngày nhập kho" ⇒ nhập kho + 365
/// ngày. Một hằng số, một chỗ.</para>
/// </summary>
public static class IqcWarehouseInDate
{
    /// <summary>Hạn dùng mặc định của nguyên liệu tính từ ngày nhập kho.</summary>
    public const int ShelfLifeDays = 365;

    private static readonly string[] FullFormats =
    {
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "yyyy-MM-dd", "d-MMM-yyyy", "dd-MMM-yyyy",
    };

    // PCS ledger: "5-Jan" — thiếu năm, mượn năm của ngày về.
    private static readonly string[] DayMonthFormats = { "d-MMM", "dd-MMM", "d/M", "dd/MM" };

    /// <summary>
    /// Đọc ô ngày nhập kho. <paramref name="receivedDate"/> chỉ dùng để bù năm
    /// cho dạng thiếu năm — không dùng làm giá trị thay thế.
    /// </summary>
    public static DateTime? Parse(string? raw, DateTime receivedDate)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        if (DateTime.TryParseExact(s, FullFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var full))
            return Plausible(full.Date, receivedDate) ? full.Date : null;

        if (DateTime.TryParseExact(s, DayMonthFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dayMonth))
        {
            // Ô sát cuối năm có thể lệch năm so với ngày về (31-Dec kiểm 02-Jan);
            // chọn năm cho khoảng cách tới ngày về nhỏ nhất.
            var candidates = new[] { receivedDate.Year - 1, receivedDate.Year, receivedDate.Year + 1 };
            DateTime? best = null;
            foreach (var y in candidates)
            {
                if (dayMonth.Month == 2 && dayMonth.Day == 29 && !DateTime.IsLeapYear(y)) continue;
                var candidate = new DateTime(y, dayMonth.Month, dayMonth.Day);
                if (best is null
                    || Math.Abs((candidate - receivedDate.Date).TotalDays)
                       < Math.Abs((best.Value - receivedDate.Date).TotalDays))
                    best = candidate;
            }
            return best;
        }

        return null;
    }

    /// <summary>
    /// Ngày nhập kho phải nằm quanh ngày về — vật tư kiểm lúc nhận hàng. Ô Excel
    /// trống bị quy về mốc serial 0 ra "01/01/1900"; nhận nó là ngày thật thì
    /// lưới hiện một loạt "quá hạn 45 nghìn ngày" màu đỏ, hoàn toàn là rác.
    /// </summary>
    private static bool Plausible(DateTime candidate, DateTime receivedDate)
    {
        var delta = (candidate - receivedDate.Date).TotalDays;
        return delta is >= -MaxDaysBeforeReceived and <= MaxDaysAfterReceived;
    }

    private const int MaxDaysBeforeReceived = 730;
    private const int MaxDaysAfterReceived = 365;

    /// <summary>Hạn dùng = ngày nhập kho + <see cref="ShelfLifeDays"/>.</summary>
    public static DateTime? Expiry(DateTime? warehouseIn)
        => warehouseIn?.AddDays(ShelfLifeDays);
}
