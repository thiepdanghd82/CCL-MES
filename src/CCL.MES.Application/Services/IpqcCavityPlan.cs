namespace CCL.MES.Application.Services;

/// <summary>
/// Cỡ mẫu first-article của IPQC, giải theo SỐ CAVITY (Thiệp chốt 2026-09-11).
///
/// <para><b>Luật.</b> Lô của IPQC không phải sản lượng lệnh và không liên quan
/// tới IQC: nó là số cavity của MỘT shot in hoặc MỘT shot cắt, và FAI là kiểm
/// đủ 100% số cavity ấy.</para>
///
/// <para><b>Vì sao không dùng bảng AQL theo cỡ lô.</b> Khuôn bế hoặc bản in
/// nhiều cavity sinh lỗi LẶP THEO VỊ TRÍ — cavity số 7 hỏng thì mọi shot về sau
/// đều hỏng đúng con thứ 7. Lấy mẫu ngẫu nhiên theo ISO 2859-1 trên cả lô rất
/// dễ trượt lỗi đó, hoặc rơi trúng mà tưởng lỗi lẻ. Soi đủ mọi cavity thì mỗi
/// vị trí được kiểm đúng một lần, nên lỗi hệ thống lộ ngay ở sản phẩm đầu.</para>
/// </summary>
public static class IpqcCavityPlan
{
    /// <summary>Cavity lấy từ <c>SpecPrints.Cavity</c>.</summary>
    public const string SourcePrint = "Print";
    /// <summary>Cavity lấy từ <c>SpecFlexoCuttingRows.CuttingCavity</c>.</summary>
    public const string SourceCut = "Cut";
    /// <summary>Spec có NHIỀU giá trị cavity cắt khác nhau — đo 2026-09-11:
    /// 59/238 spec rơi vào đây, và cột <c>Process</c> không phải lúc nào cũng
    /// tách được (một spec có "PRESS / Dập" mang cả cavity 5 lẫn 2 tuỳ dao).</summary>
    public const string SourceAmbiguous = "Ambiguous";
    /// <summary>Spec chưa điền cavity — đo được 12% bên in, 7% bên cắt.</summary>
    public const string SourceMissing = "Missing";
    /// <summary>Người kiểm nhập tay tại chuyền.</summary>
    public const string SourceManual = "Manual";
    /// <summary>Hạng mục không thuộc IN lẫn CẮT nên không có cavity để giải.</summary>
    public const string SourceNotApplicable = "N/A";

    /// <summary>Kết quả giải cavity cho MỘT công đoạn.</summary>
    /// <param name="Count">Số cavity, hoặc <c>null</c> khi chưa giải được.</param>
    /// <param name="Source">Một trong các hằng <c>Source*</c> ở trên.</param>
    public readonly record struct Resolution(int? Count, string Source);

    /// <summary>Cavity đã giải cho cả hai công đoạn của một lệnh sản xuất.</summary>
    public readonly record struct Plan(Resolution Print, Resolution Cut);

    /// <summary>Chưa tra được gì (vd WO không có bản spec) — cả hai đều Missing.</summary>
    public static Plan None => new(
        new Resolution(null, SourceMissing),
        new Resolution(null, SourceMissing));

    /// <summary>
    /// Dựng kết quả cho công đoạn IN từ giá trị đọc ở <c>SpecPrints.Cavity</c>.
    /// <c>null</c> hoặc ≤ 0 ⇒ <see cref="SourceMissing"/>. <b>Không bao giờ mặc
    /// định 1</b>: mặc định 1 biến "kiểm đủ cavity" thành "kiểm một con" mà hồ
    /// sơ vẫn trông như đã kiểm đủ.
    /// </summary>
    public static Resolution FromPrint(int? cavity) =>
        cavity is > 0 ? new Resolution(cavity, SourcePrint)
                      : new Resolution(null, SourceMissing);

    /// <summary>
    /// Dựng kết quả cho công đoạn CẮT từ TẬP giá trị <c>CuttingCavity</c> của
    /// spec. Một spec có nhiều dòng cắt (đo được: 179/238 spec cùng một giá
    /// trị, 30 spec hai giá trị, 29 spec ba giá trị).
    ///
    /// <para>Đúng một giá trị dương ⇒ lấy. Nhiều giá trị khác nhau ⇒
    /// <see cref="SourceAmbiguous"/>, KHÔNG chọn hộ: chọn sai thì người kiểm
    /// soi 2 con trong khi khuôn có 5, và hồ sơ ghi là đã kiểm đủ.</para>
    /// </summary>
    public static Resolution FromCut(IEnumerable<int?>? cavities)
    {
        var distinct = (cavities ?? Array.Empty<int?>())
            .Where(c => c is > 0)
            .Select(c => c!.Value)
            .Distinct()
            .ToList();

        return distinct.Count switch
        {
            0 => new Resolution(null, SourceMissing),
            1 => new Resolution(distinct[0], SourceCut),
            _ => new Resolution(null, SourceAmbiguous),
        };
    }

    /// <summary>Kết quả áp cho một hạng mục, chọn theo công đoạn của chính nó.</summary>
    public static Resolution For(Plan plan, string? processLine) =>
        QcProcessKind.Of(processLine) switch
        {
            QcProcessKind.Print => plan.Print,
            QcProcessKind.Cut   => plan.Cut,
            _ => new Resolution(null, SourceNotApplicable),
        };
}
