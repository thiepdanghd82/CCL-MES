namespace CCL.MES.Application.Services;

/// <summary>
/// Một hạng mục IPQC thuộc công đoạn IN hay CẮT — phép phân loại TẦNG-1.
///
/// <para><b>Vì sao cần ở tầng này.</b> Cỡ mẫu FAI lấy theo số cavity, mà cavity
/// của in và của cắt là HAI con số khác nhau và nằm ở hai bảng khác nhau
/// (<c>SpecPrints.Cavity</c> · <c>SpecFlexoCuttingRows.CuttingCavity</c>). Muốn
/// giải đúng nguồn thì server phải biết hạng mục đang đứng ở công đoạn nào.</para>
///
/// <para><b>Bản sao thứ hai nằm ở UI</b> —
/// <c>IpqcDashboard.ProcessForItem</c> dùng đúng hai danh sách này để chia chip
/// công đoạn. Hai nơi phải KHỚP: lệch một line thì cavity giải theo bảng này
/// trong khi người vận hành nhìn thấy chip của bảng kia, và không ai phát hiện
/// vì cả hai đều hiện ra số. <c>gate-qc-process-kind.sh</c> canh đúng chuyện đó.</para>
/// </summary>
public static class QcProcessKind
{
    public const string Print = "Print";
    public const string Cut   = "Cut";
    public const string Other = "Other";

    /// <summary>Line đi về công đoạn IN. Giữ nguyên thứ tự + chữ hoa để gate
    /// so khớp được với danh sách trong <c>IpqcDashboard.razor</c>.</summary>
    public static readonly IReadOnlyList<string> PrintLines = ["LABEL", "DIGITAL", "SILK"];

    /// <summary>Line đi về công đoạn CẮT.</summary>
    public static readonly IReadOnlyList<string> CutLines = ["PRESS_CNC", "FINISHING"];

    /// <summary>
    /// Phân loại một <c>ProcessLine</c> đã resolve. Không nhận ra thì trả
    /// <see cref="Other"/> — KHÔNG đoán về IN, vì đoán sai chiều nào cũng dẫn
    /// tới lấy cavity của công đoạn khác và cỡ mẫu sẽ sai mà trông vẫn hợp lý.
    /// </summary>
    public static string Of(string? processLine)
    {
        var l = (processLine ?? "").Trim().ToUpperInvariant();
        if (l.Length == 0) return Other;
        if (PrintLines.Contains(l)) return Print;
        if (CutLines.Contains(l)) return Cut;
        return Other;
    }
}
