namespace CCL.MES.Application.Services;

/// <summary>
/// P13 — bảng cỡ mẫu IQC theo cỡ lô (sheet <c>AQL</c> của file master
/// "IQC report 2026"), 15 bậc, kiểu ISO 2859-1 một lần lấy mẫu.
///
/// <para><b>Quy tắc thật KHÔNG phải là tra bảng thẳng.</b> Đo trên 5.336 bản
/// ghi có thật của file master 2026:</para>
/// <list type="bullet">
///   <item>Roll  : tra thẳng 70% · <c>min(bảng, cỡ lô)</c> 27% · khác 1%</item>
///   <item>Chem  : tra thẳng 59% · <c>min(bảng, cỡ lô)</c> 39% · khác 1%</item>
///   <item>Tool  : tra thẳng  5% · <c>min(bảng, cỡ lô)</c> 93% · khác 0%</item>
/// </list>
/// <para>Nghĩa là công thức đúng là <c>min(bảng(lô), lô)</c> — không ai lấy 2
/// cuộn ra khỏi một lô 1 cuộn. Riêng nhóm Tool 93% rơi vào nhánh cắt ngọn vì lô
/// dao/dụng cụ thường chỉ 1–2 cái. Phần "khác" còn lại là QC CỐ Ý lấy nhiều hơn
/// (kiểm siết, hoặc kiểm 100%) — đó là ghi đè hợp lệ, không phải sai sót, nên
/// hàm này chỉ ĐỀ XUẤT.</para>
///
/// <para>Thuần, không trạng thái, không I/O ⇒ khoá được bằng test mà không cần
/// DB. Bảng là hằng số biên dịch: đây là chuẩn lấy mẫu đã ký, không phải cấu
/// hình để ai đó sửa nóng.</para>
/// </summary>
public static class IqcSamplingTable
{
    /// <summary>Một bậc: cỡ lô từ <paramref name="Lo"/> đến <paramref name="Hi"/>
    /// thì lấy <paramref name="SampleSize"/> mẫu.</summary>
    public readonly record struct Tier(int Level, long Lo, long Hi, int SampleSize);

    /// <summary>15 bậc, chép nguyên từ sheet AQL. Bậc 15 là "500.001 trở lên".</summary>
    public static readonly IReadOnlyList<Tier> Tiers =
    [
        new( 1,       1,           8,    2),
        new( 2,       9,          15,    3),
        new( 3,      16,          25,    5),
        new( 4,      26,          50,    8),
        new( 5,      51,          90,   13),
        new( 6,      91,         150,   20),
        new( 7,     151,         280,   32),
        new( 8,     281,         500,   50),
        new( 9,     501,       1_200,   80),
        new(10,   1_201,       3_200,  125),
        new(11,   3_201,      10_000,  200),
        new(12,  10_001,      35_000,  315),
        new(13,  35_001,     150_000,  500),
        new(14, 150_001,     500_000,  800),
        new(15, 500_001, long.MaxValue, 1250),
    ];

    /// <summary>Bậc ứng với cỡ lô. <c>null</c> khi lô ≤ 0 — lô rỗng không có
    /// bậc nào, và trả bậc 1 cho nó là bịa ra một con số.</summary>
    public static Tier? TierFor(long lotQty)
    {
        if (lotQty <= 0) return null;
        foreach (var t in Tiers)
            if (lotQty >= t.Lo && lotQty <= t.Hi) return t;
        return null;
    }

    /// <summary>
    /// Cỡ mẫu ĐỀ XUẤT = <c>min(bảng(lô), lô)</c>. Trả 0 khi lô ≤ 0.
    ///
    /// <para>Đây là ĐỀ XUẤT, không phải mệnh lệnh: QC được đổi, nhưng mọi thay
    /// đổi phải kèm lý do (Henry chốt 2026-09-04) — luật đó nằm ở service ghi,
    /// không nằm ở đây, vì hàm này phải thuần để test và để tái dùng.</para>
    /// </summary>
    public static int Suggest(long lotQty)
    {
        var t = TierFor(lotQty);
        if (t is null) return 0;
        // Cắt ngọn: không lấy nhiều mẫu hơn số đơn vị đang có.
        return (int)Math.Min(t.Value.SampleSize, lotQty);
    }

    /// <summary>Cỡ mẫu người dùng nhập có phải là NỚI LỎNG so với đề xuất
    /// không. Lấy nhiều hơn là siết chặt — luôn an toàn; lấy ít hơn mới là
    /// giảm mức bảo đảm và cần soi kỹ.</summary>
    public static bool IsRelaxed(long lotQty, int actualSampleSize)
        => actualSampleSize < Suggest(lotQty);
}
