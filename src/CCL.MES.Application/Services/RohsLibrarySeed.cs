namespace CCL.MES.Application.Services;

/// <summary>
/// Nhóm <b>E·RoHS &amp; Halogen</b> — 8 chỉ tiêu ppm, chỉ áp cho <b>OQC</b>.
///
/// <para>VÌ SAO NẰM RIÊNG: file master data của Ops (<c>IPQC_Library_CMES_v5.xlsx</c>)
/// mô tả kiểm tra <i>trong chuyền</i> — ngoại quan, kích thước, màu, chức năng.
/// RoHS/Halogen là kiểm tra <i>tuân thủ hoá chất</i> của lô xuất hàng, đo bằng
/// XRF hoặc lấy từ chứng thư phòng lab, và chỉ xuất hiện trên tờ OQC CCL-10-F6.
/// Nó không thuộc về file kia, nhưng vẫn phải là HẠNG MỤC THƯ VIỆN để đi chung
/// một đường materialize với mọi hạng mục khác. Cùng khuôn với
/// <c>SettingLibrarySeed</c>.</para>
///
/// <para><b>KHOÁ GIỮ NGUYÊN</b> (<c>cr_ppm</c> · <c>cl_ppm</c> · …) chứ không đổi
/// sang quy ước <c>ROHS-E1</c>. Tám check OQC đã đóng băng đang mang đúng những
/// khoá này; đổi khoá sẽ biến chúng thành dòng "mồ côi" (straggler) và cắt đứt
/// mạch so sánh giữa lô cũ và lô mới. Danh tính bằng chứng quan trọng hơn sự
/// nhất quán của quy ước đặt tên.</para>
///
/// <para><b>NGƯỠNG KHÔNG ĐƯỢC BỊA.</b> Tám giá trị ppm dưới đây chép nguyên từ
/// <c>QcProfileSeed.OqcProfileJson</c> đang chạy production. Sửa chúng là sửa
/// tiêu chí tuân thủ — phải có văn bản của QA, không phải quyết định của người
/// viết code.</para>
/// </summary>
public static class RohsLibrarySeed
{
    /// <summary>Nhóm hiển thị. Xếp sau A·B·C·D nên tab của nó nằm cuối.</summary>
    public const string Group = "E·RoHS & Halogen";

    /// <summary>
    /// <c>ProcessLine</c> đặc biệt: hạng mục áp cho MỌI dòng sản phẩm.
    ///
    /// <para>Thư viện scope theo dòng sản phẩm (LABEL · SILK). RoHS thì không —
    /// lô nào xuất hàng cũng phải đạt. Nhân đôi 8 dòng cho từng line là nhân đôi
    /// master data, và lần thứ ba thêm line sẽ thành 24 dòng. Thay vào đó
    /// <see cref="QcLineLibrarySelector"/> hiểu <c>ALL</c> là "kèm vào mọi line
    /// đã resolve", khử trùng theo <c>ItemId</c> nên không sinh dòng lặp.</para>
    /// </summary>
    public const string AllLines = "ALL";

    /// <summary>Một chỉ tiêu RoHS. <paramref name="Spec"/> là ngưỡng ppm.</summary>
    public sealed record Row(string ItemId, string Code, string ItemVi, string ItemEn, string Spec, int Sort);

    private const string Method = "XRF / Lab cert";

    public static IReadOnlyList<Row> Items() =>
    [
        new("cr_ppm", "E1", "Cr (Chromium)",  "Cr (Chromium)",  "< 100",   10),
        new("cl_ppm", "E2", "Cl (Chlorine)",  "Cl (Chlorine)",  "< 800",   20),
        new("s_ppm",  "E3", "S (Sulphur)",    "S (Sulphur)",    "< 10000", 30),
        new("cd_ppm", "E4", "Cd (Cadmium)",   "Cd (Cadmium)",   "< 20",    40),
        new("hg_ppm", "E5", "Hg (Mercury)",   "Hg (Mercury)",   "< 100",   50),
        new("pb_ppm", "E6", "Pb (Lead)",      "Pb (Lead)",      "< 100",   60),
        new("sn_ppm", "E7", "Sn (Tin)",       "Sn (Tin)",       "< 800",   70),
        new("sb_ppm", "E8", "Sb (Antimony)",  "Sb (Antimony)",  "< 700",   80),
    ];

    /// <summary>Phương pháp đo — chung cho cả 8, đã là thuật ngữ tiếng Anh nên
    /// không cần bản dịch riêng.</summary>
    public static string MeasureMethod => Method;

    /// <summary>Đây có phải khoá của một chỉ tiêu RoHS không (dùng cho test +
    /// chẩn đoán, tránh rải chuỗi ma thuật khắp nơi).</summary>
    public static bool IsRohsKey(string? itemId) =>
        !string.IsNullOrWhiteSpace(itemId) &&
        Items().Any(r => string.Equals(r.ItemId, itemId.Trim(), StringComparison.OrdinalIgnoreCase));
}
