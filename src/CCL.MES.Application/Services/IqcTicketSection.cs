namespace CCL.MES.Application.Services;

/// <summary>
/// P12 bước 3 — hạng mục kiểm nào thuộc MỤC nào của phiếu IQC (stepper 5 bước).
///
/// <para>Bảng chốt trong scope proposal §4.3:</para>
/// <list type="table">
/// <item><term>1 · Hồ sơ tài liệu</term><description><c>MT-02</c> (HSF/SGS của NCC) — 1 hạng mục</description></item>
/// <item><term>2 · Ngoại quan</term><description>nhóm <c>NL</c> + <c>NQ</c> — 7 hạng mục</description></item>
/// <item><term>3 · Chức năng</term><description>các nhóm còn lại — 12 hạng mục</description></item>
/// <item><term>4 · Mã lỗi &amp; kết luận</term><description>KHÔNG phải danh sách hạng mục</description></item>
/// <item><term>5 · Tra cứu lịch sử</term><description>KHÔNG phải danh sách hạng mục</description></item>
/// </list>
///
/// <para>Mục 4 và 5 là <b>khung nhìn dẫn xuất</b> — một cái ghi mã NG + phán
/// định cuối, một cái tra lô cũ cùng nguyên liệu. Nhồi hạng mục vào đó là nhân
/// đôi đúng lỗi mà L63 vừa gỡ khỏi FQC/OQC (trộn metadata + ô chữ ký vào lưới
/// OK/NG).</para>
///
/// <para>Nhóm <c>MT</c> là nhóm DUY NHẤT bị chẻ: <c>MT-02</c> là hồ sơ giấy nên
/// về mục 1, còn <c>MT-01</c> (RoHS nội bộ) và <c>MT-03</c> (chất cấm) là phép
/// đo thật nên về mục 3. Vì thế luật phải tra theo <b>mã hạng mục trước</b>,
/// nhóm sau — tra theo nhóm không thôi sẽ đẩy cả ba về cùng một mục.</para>
///
/// <para>Đây là chỗ DUY NHẤT quyết định chuyện này. UI chỉ lọc theo số mục mà
/// server đã tính; nếu để UI tự suy thì hai màn sẽ trôi khỏi nhau.</para>
/// </summary>
public static class IqcTicketSection
{
    public const int Documents = 1;
    public const int Visual = 2;
    public const int Functional = 3;

    /// <summary>Hạng mục đi vào mục 1 vì là hồ sơ giấy, không phải phép đo.</summary>
    private const string DocumentItemKey = "MT-02";

    /// <summary>Nhóm thuộc mục 2 (ngoại quan): nhận dạng vật liệu + ngoại quan.</summary>
    private static readonly HashSet<string> VisualGroups =
        new(StringComparer.OrdinalIgnoreCase) { "NL", "NQ" };

    /// <summary>
    /// Mục chứa hạng mục <paramref name="itemKey"/> thuộc nhóm
    /// <paramref name="groupCode"/>.
    /// </summary>
    /// <returns>1, 2 hoặc 3. Hạng mục lạ (thư viện mở rộng về sau) rơi về mục 3
    /// — hiện ra chỗ nào đó còn hơn biến mất khỏi phiếu.</returns>
    public static int Of(string? itemKey, string? groupCode)
    {
        if (string.Equals(itemKey?.Trim(), DocumentItemKey, StringComparison.OrdinalIgnoreCase))
            return Documents;

        var g = groupCode?.Trim();
        return !string.IsNullOrEmpty(g) && VisualGroups.Contains(g) ? Visual : Functional;
    }
}
