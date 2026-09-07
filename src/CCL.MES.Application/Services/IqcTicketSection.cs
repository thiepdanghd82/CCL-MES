namespace CCL.MES.Application.Services;

/// <summary>
/// Mục nào của stepper phiếu IQC chứa hạng mục nào.
///
/// <list type="table">
/// <item><term>1 · Hồ sơ tài liệu</term><description><c>MT-02</c></description></item>
/// <item><term>2 · Điều kiện đóng gói</term><description><c>NQ-01</c> (tem) · <c>NQ-06</c> (đóng gói)</description></item>
/// <item><term>3 · Ngoại quan</term><description><c>NL</c> + <c>NQ</c> còn lại (kể cả RD/PD)</description></item>
/// <item><term>4 · Kiểm tra kích thước</term><description>nhóm <c>KT</c></description></item>
/// <item><term>5 · Chức năng / Lab</term><description>các nhóm còn lại (+ <c>LB</c>)</description></item>
/// </list>
/// Mục Defect / History trên UI không chứa hạng mục thư viện.
/// </summary>
public static class IqcTicketSection
{
    public const int Documents = 1;
    public const int Packaging = 2;
    public const int Visual = 3;
    public const int Dimension = 4;
    public const int Functional = 5;

    private const string DocumentItemKey = "MT-02";

    private static readonly HashSet<string> PackagingKeys =
        new(StringComparer.OrdinalIgnoreCase) { "NQ-01", "NQ-06" };

    private static readonly HashSet<string> VisualGroups =
        new(StringComparer.OrdinalIgnoreCase) { "NL", "NQ" };

    private static readonly HashSet<string> DimensionGroups =
        new(StringComparer.OrdinalIgnoreCase) { "KT" };

    /// <returns>1…5. Hạng mục lạ → Functional (hiện ra chỗ nào đó còn hơn biến mất).</returns>
    public static int Of(string? itemKey, string? groupCode)
    {
        var key = itemKey?.Trim();
        if (string.Equals(key, DocumentItemKey, StringComparison.OrdinalIgnoreCase))
            return Documents;

        if (!string.IsNullOrEmpty(key) && PackagingKeys.Contains(key))
            return Packaging;

        var g = groupCode?.Trim();
        if (!string.IsNullOrEmpty(g) && DimensionGroups.Contains(g))
            return Dimension;

        if (!string.IsNullOrEmpty(g) && VisualGroups.Contains(g))
            return Visual;

        return Functional;
    }
}
