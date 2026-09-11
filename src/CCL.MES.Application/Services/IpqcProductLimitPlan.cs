using CCL.MES.Domain;

namespace CCL.MES.Application.Services;

/// <summary>
/// Ngưỡng số THEO SẢN PHẨM, tra từ kế hoạch QC (<c>SpecQcWindows</c> +
/// <c>QcCriteria</c>) để đóng băng lên hạng mục IPQC của một lệnh sản xuất
/// (Thiệp chốt 2026-09-11).
///
/// <para><b>Vì sao ngưỡng không nằm ở thư viện.</b> Thư viện hạng mục kiểm là
/// theo DÒNG SẢN XUẤT: nhãn 20mm và nhãn 200mm dùng chung hạng mục "kích thước
/// tổng thể" nhưng dung sai khác nhau. Một con số ở thư viện sẽ sai cho gần hết
/// mặt hàng. Con số ấy sống ở <c>QcCriteria</c>, khoá theo
/// <c>ProductRevision × Stage</c>.</para>
///
/// <para><b>Nối bằng CỘT, không khớp theo tên.</b> <c>QcCriterion.Name</c> là
/// chữ kỹ sư tự gõ — "Kích thước tổng thể" và "Kích thước tổng thể (W×L)" không
/// khớp nhau, và sai một chữ là mất dung sai mà không ai báo. Đường nối là
/// <c>QcCriterion.LibraryItemKey</c> trỏ thẳng vào <c>ItemId</c> của thư viện.</para>
///
/// <para>Lớp thuần: nhận dữ liệu đã nạp, không I/O — khoá được bằng test không
/// cần DB.</para>
/// </summary>
public static class IpqcProductLimitPlan
{
    /// <summary>Một tiêu chí đã nạp, rút gọn còn đúng thứ cần để đóng băng.</summary>
    /// <param name="LibraryItemKey">Hạng mục thư viện mà tiêu chí làm rõ.</param>
    /// <param name="WindowId">Kế hoạch QC cấp ngưỡng — để truy ngược hồ sơ.</param>
    /// <param name="Stage">Kế hoạch thuộc công đoạn nào (IpqcPrint / IpqcCut).</param>
    public readonly record struct Criterion(
        string? LibraryItemKey, long WindowId, QcStage Stage,
        double? Low, double? Up, double? Nominal, string? Unit);

    /// <summary>Ngưỡng áp cho MỘT hạng mục, kèm nguồn.</summary>
    public readonly record struct Limit(
        double? Low, double? Up, double? Nominal, string? Unit, long? SourceWindowId)
    {
        /// <summary>Có ràng buộc số nào không — quyết định máy chấm được hay không.</summary>
        public bool HasBound => Low is not null || Up is not null;
    }

    /// <summary>Không có ngưỡng nào áp được.</summary>
    public static Limit None => new(null, null, null, null, null);

    /// <summary>
    /// Công đoạn của kế hoạch QC ↔ công đoạn của hạng mục.
    /// <c>IpqcPrint</c> ↔ <see cref="QcProcessKind.Print"/>,
    /// <c>IpqcCut</c> ↔ <see cref="QcProcessKind.Cut"/>. Hai stage còn lại
    /// (<c>Fqc</c> · <c>Oqc</c>) KHÔNG áp cho IPQC.
    /// </summary>
    public static bool StageMatches(QcStage stage, string? processLine) =>
        (stage, QcProcessKind.Of(processLine)) switch
        {
            (QcStage.IpqcPrint, QcProcessKind.Print) => true,
            (QcStage.IpqcCut,   QcProcessKind.Cut)   => true,
            _ => false,
        };

    /// <summary>
    /// Ngưỡng cho một hạng mục, tìm trong tập tiêu chí đã nạp.
    ///
    /// <para>Điều kiện khớp, phải đủ CẢ HAI: cùng <c>LibraryItemKey</c> (không
    /// phân biệt hoa thường) VÀ stage của kế hoạch hợp với công đoạn của hạng
    /// mục. Thiếu vế stage thì tiêu chí của khâu IN sẽ bơm ngưỡng sang hạng mục
    /// khâu CẮT — cùng tên hạng mục nhưng khác dung sai.</para>
    ///
    /// <para><b>Nhiều tiêu chí cùng khớp ⇒ KHÔNG lấy cái nào.</b> Hai kế hoạch
    /// cùng trỏ về một hạng mục với hai dung sai khác nhau là mâu thuẫn trong
    /// chính spec; chọn bừa một cái là giấu mâu thuẫn ấy đi và đóng dấu nó vào
    /// hồ sơ đã ký.</para>
    /// </summary>
    public static Limit For(
        IReadOnlyList<Criterion>? criteria, string? itemKey, string? processLine)
    {
        if (criteria is null || criteria.Count == 0) return None;
        if (string.IsNullOrWhiteSpace(itemKey)) return None;

        var hits = criteria.Where(c =>
                !string.IsNullOrWhiteSpace(c.LibraryItemKey)
                && string.Equals(c.LibraryItemKey!.Trim(), itemKey.Trim(),
                                 StringComparison.OrdinalIgnoreCase)
                && StageMatches(c.Stage, processLine)
                && (c.Low is not null || c.Up is not null))
            .ToList();

        if (hits.Count != 1) return None;

        var h = hits[0];
        return new Limit(h.Low, h.Up, h.Nominal, h.Unit, h.WindowId);
    }
}
