namespace CCL.MES.Application.Services;

/// <summary>
/// P13 bước 3 — MỘT dòng của sheet <c>Raw</c> trong file master IQC, sau khi đã
/// đọc khỏi Excel. Thuần dữ liệu, không phụ thuộc ClosedXML ⇒ tầng Application
/// và test không phải kéo theo thư viện đọc Excel.
/// </summary>
/// <param name="MotherCode">Mã mẹ — KHOÁ NỐI duy nhất đã ĐO được với app
/// (<c>IqcMaterialSpec.MaterialCode</c>, 356/459 trùng). KHÔNG dùng cột IFS:
/// bài học L66, hai hệ đánh số khớp 0 dòng.</param>
/// <param name="CodeIfs">Chỉ để tra cứu và ghi vào phiếu, KHÔNG dùng để nối.</param>
public readonly record struct IqcMasterRow(
    string MotherCode,
    string? CodeIfs,
    string? MaterialName,
    string? SupplierName,
    string? TestMethod,
    string? AdhesionSpec,
    string? ThicknessSpec,
    string? WidthSpec);

/// <summary>
/// P13 bước 3 — quy đổi ba cột tiêu chuẩn của sheet <c>Raw</c> về hạng mục
/// trong thư viện IQC đang chạy.
///
/// <para>Ba mã đích là hạng mục CÓ THẬT trong 21 hạng mục hiện có, không phải
/// mã bịa: <c>BD-01</c> "Độ bám dính keo (Peel test)" · <c>KT-04</c> "Độ dày" ·
/// <c>KT-03</c> "Chiều rộng". Nghĩa là import KHÔNG đẻ ra thư viện thứ hai —
/// nó rót tiêu chuẩn vào đúng khung đã có.</para>
/// </summary>
public static class IqcMasterItemMap
{
    /// <summary>Độ bám dính keo — cột "Tiêu chuẩn keo".</summary>
    public const string Adhesion = "BD-01";

    /// <summary>Độ dày — cột "Tiêu chuẩn dày".</summary>
    public const string Thickness = "KT-04";

    /// <summary>Chiều rộng — cột "Tiêu chuẩn rộng".</summary>
    public const string Width = "KT-03";

    /// <summary>Nguồn nhập, ghi vào <c>IqcMaterialSpec.ImportSource</c> để lần
    /// import sau biết dòng nào là của mình.</summary>
    public const string Source = "iqc-report-2026";

    /// <summary>Tiền tố mã spec sinh ra cho mã mẹ CHƯA có spec. Cố ý khác cả
    /// <c>CCL-SPEC-QC###</c> (file master QC cũ) lẫn <c>MES-SPEC-</c> (người
    /// tạo trong app) — đụng dải số của nhau là lần import sau ghi đè mất công
    /// người khác soạn.</summary>
    public const string SpecNoPrefix = "IQC26-";

    /// <summary>Ba cặp (mã hạng mục, chuỗi tiêu chuẩn) của một dòng, BỎ QUA ô
    /// trống. Trả theo thứ tự cố định để lần import sau sinh cùng một
    /// <c>Seq</c> — đảo thứ tự là mọi dòng bị coi như đã đổi.</summary>
    public static IEnumerable<(string ItemId, string Spec)> ItemsOf(IqcMasterRow r)
    {
        if (Has(r.AdhesionSpec)) yield return (Adhesion, r.AdhesionSpec!.Trim());
        if (Has(r.ThicknessSpec)) yield return (Thickness, r.ThicknessSpec!.Trim());
        if (Has(r.WidthSpec)) yield return (Width, r.WidthSpec!.Trim());
    }

    /// <summary>Ô có nội dung đáng nhập không. <c>0</c> bị loại vì trong file
    /// master nó là ô Excel bỏ trống bị công thức điền, không phải "tiêu chuẩn
    /// bằng 0" — nhập vào sẽ thành ngưỡng giả cho 2.320 dòng.</summary>
    private static bool Has(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0 || t == "-" || t == "--" || t == "0") return false;

        // "N/A" KHÔNG phải tiêu chuẩn chưa khai — nó là lời khai rằng vật liệu
        // này KHÔNG CÓ tiêu chuẩn ấy (mực in thì không có phép thử bóc keo).
        // Dựng hạng mục cho nó là bắt người kiểm mỗi lô phải bấm qua 128 dòng
        // ghi "N/A", và làm phiếu dài ra mà không thêm một thông tin nào.
        var u = t.ToUpperInvariant();
        return u != "N/A" && u != "NA";
    }

    /// <summary>Mã spec cho một mã mẹ chưa có spec. Băm ổn định để chạy lại
    /// import ra CÙNG mã, không phụ thuộc thứ tự dòng trong file.</summary>
    public static string SpecNoFor(string motherCode)
    {
        var key = (motherCode ?? "").Trim().ToUpperInvariant();
        // FNV-1a 32-bit: ngắn, ổn định giữa các phiên bản .NET (string.GetHashCode
        // thì KHÔNG — nó randomize theo tiến trình, chạy lại ra mã khác).
        uint h = 2166136261;
        foreach (var c in key) { h ^= c; h *= 16777619; }
        return SpecNoPrefix + h.ToString("X8");
    }
}
