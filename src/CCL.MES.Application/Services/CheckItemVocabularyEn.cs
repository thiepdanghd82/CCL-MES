namespace CCL.MES.Application.Services;

/// <summary>
/// Bản EN cho hai cột TỪ VỰNG CÓ KIỂM SOÁT của thư viện hạng mục kiểm:
/// <c>GroupLabel</c> và <c>Method</c>.
///
/// <para>VÌ SAO NẰM TRONG CODE CHỨ KHÔNG PHẢI TRONG FILE NGUỒN: file master
/// data (<c>IPQC_Library_CMES_v5.xlsx</c>) do Ops giữ và chỉ có cột tiếng Việt.
/// Hai trường này KHÔNG phải văn bản tự do — 43 giá trị Method và 10 giá trị
/// GroupLabel dùng đi dùng lại trên 79 hạng mục. Bắt Ops nhập tay bản EN cho
/// từng dòng là mời lỗi chính tả và bản dịch lệch nhau giữa các dòng cùng
/// nghĩa. Để ở đây thì mỗi thuật ngữ có ĐÚNG MỘT bản EN, đi qua code review,
/// và Ops vẫn sửa file nguồn bằng tiếng Việt như cũ.</para>
///
/// <para>Seeder gọi <see cref="Group"/> / <see cref="Method"/> để điền
/// <c>CheckItemLibrary.GroupLabelEn</c> / <c>MethodEn</c> lúc upsert. Từ đó
/// materializer ĐÓNG BĂNG giá trị vào <c>WoIpqcCheckItem</c> — bảng đó mới là
/// thứ UI đọc. Nghĩa là bảng tra ở đây chỉ chạy lúc SEED, không chạy lúc
/// render: đổi một chuỗi ở đây KHÔNG hồi tố hồ sơ QC đã đóng băng.</para>
///
/// <para>Chuỗi VI dùng làm khoá phải khớp TỪNG BYTE với file nguồn, kể cả dấu
/// chấm giữa trong "A·Ngoại quan" và mũi tên "↔". Test
/// <c>CheckItemVocabularyEnTests</c> khoá điều này.</para>
/// </summary>
public static class CheckItemVocabularyEn
{
    /// <summary>Trả bản EN, hoặc <c>null</c> nếu chưa có bản dịch — phía gọi tự
    /// rơi về bản VI. Không bao giờ trả chuỗi rỗng.</summary>
    public static string? Group(string? vi) => Lookup(GroupEn, vi);

    /// <inheritdoc cref="Group"/>
    public static string? Method(string? vi) => Lookup(MethodEn, vi);

    private static string? Lookup(IReadOnlyDictionary<string, string> map, string? vi)
    {
        if (string.IsNullOrWhiteSpace(vi)) return null;
        return map.TryGetValue(vi.Trim(), out var en) ? en : null;
    }

    /// <summary>11 nhóm hạng mục — 10 từ thư viện v5 + "Mẫu đầu tiên" chỉ
    /// xuất hiện trong SettingLibrarySeed (khâu SETTING).</summary>
    public static readonly IReadOnlyDictionary<string, string> GroupEn =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A·Ngoại quan"] = "A·Appearance",
        ["B·Kích thước"] = "B·Dimensions",
        ["C·Màu sắc"] = "C·Colour",
        ["Cân chỉnh"] = "Alignment",
        ["Cân chỉnh máy"] = "Machine set-up",
        ["Dao & khuôn"] = "Cutters & dies",
        ["D·Chức năng"] = "D·Function",
        ["Khuôn & vật tư"] = "Dies & materials",
        ["Mẫu đầu & an toàn"] = "First article & safety",
        ["Mẫu đầu tiên"] = "First article",
        ["E·RoHS & Halogen"] = "E·RoHS & Halogen",
        ["Mẫu đầu & thải"] = "First article & waste",
    };

    /// <summary>43 phương pháp · dụng cụ kiểm.</summary>
    public static readonly IReadOnlyDictionary<string, string> MethodEn =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Băng keo chuẩn, bóc 1 lần"] = "Standard tape, single peel",
        ["Băng keo chuẩn, bóc 1 lần dứt khoát"] = "Standard tape, one sharp peel",
        ["Dao rạch ô + băng keo"] = "Cross-hatch cutter + tape",
        ["Dao rạch ô + băng keo, bóc & soi"] = "Cross-hatch cutter + tape, peel & inspect",
        ["Dán thử + lực kế (nếu spec)"] = "Trial bond + force gauge (if specified)",
        ["Máy quét/verifier"] = "Scanner / barcode verifier",
        ["Máy/bút chà + bông tẩm cồn"] = "Rub tester or rub pen + alcohol swab",
        ["RCA / eraser test"] = "RCA / eraser test",
        ["RCA tester / eraser test"] = "RCA tester / eraser test",
        ["Soi buồng tối / đèn UV"] = "Darkroom / UV lamp inspection",
        ["Soi mắt"] = "Visual inspection",
        ["Soi mắt + gập nhẹ"] = "Visual inspection + light flex",
        ["Soi mắt + kính lúp; vs mẫu"] = "Visual inspection + loupe; vs sample",
        ["Soi mắt + sờ"] = "Visual inspection + touch",
        ["Soi mắt + sờ mép"] = "Visual inspection + edge touch",
        ["Soi mắt + sờ; vs mẫu"] = "Visual inspection + touch; vs sample",
        ["Soi mắt + tách thử"] = "Visual inspection + peel trial",
        ["Soi mắt + đèn"] = "Visual inspection + lamp",
        ["Soi mắt + đèn nền"] = "Visual inspection + backlight",
        ["Soi mắt + đối chiếu"] = "Visual inspection + comparison",
        ["Soi mắt + đối chiếu mẫu/spec; kính lúp"] = "Visual inspection + compare to sample/spec; loupe",
        ["Soi mắt + đối chiếu mẫu; kính lúp"] = "Visual inspection + compare to sample; loupe",
        ["Soi mắt dưới đèn chuẩn"] = "Visual inspection under standard light",
        ["Soi mắt nghiêng"] = "Visual inspection at an angle",
        ["Soi mắt nghiêng + đèn"] = "Visual inspection at an angle + lamp",
        ["Soi mắt nghiêng; vs mẫu"] = "Visual inspection at an angle; vs sample",
        ["Soi mắt nhiều vị trí"] = "Visual inspection at several positions",
        ["Soi mắt; densitometer (nếu có)"] = "Visual inspection; densitometer (if available)",
        ["Soi sáng xuyên / đo (nếu spec)"] = "Transmitted-light inspection / measure (if specified)",
        ["Thước + kính lúp đo"] = "Rule + measuring loupe",
        ["Thước + kính lúp; đối chiếu"] = "Rule + loupe; comparison",
        ["Thước + đếm; TẮT chế độ mắt đọc khi kiểm"] = "Rule + count; SWITCH OFF sensor-eye mode while inspecting",
        ["Thước + đối chiếu dưỡng"] = "Rule + compare to template",
        ["Thước + đối chiếu layout"] = "Rule + compare to layout",
        ["Thước cặp + đối chiếu layout"] = "Calliper + compare to layout",
        ["Thước cặp/CMM (KHÔNG thước lá cho dung sai nhỏ)"] = "Calliper/CMM (NOT a steel rule for tight tolerances)",
        ["Đèn chuẩn + máy đo màu (nếu có)"] = "Standard light + colorimeter (if available)",
        ["Đèn chuẩn D50/D65 + máy đo màu (nếu có)"] = "D50/D65 standard light + colorimeter (if available)",
        ["Đếm + đối chiếu"] = "Count + comparison",
        ["Đối chiếu mẫu lưu lô trước"] = "Compare to the retained sample of the previous lot",
        ["Đối chiếu mẫu/dưỡng; cắt thử dao"] = "Compare to sample/template; trial cut",
        ["Đối chiếu spec + đếm lớp"] = "Compare to spec + count layers",
        ["Đối chiếu tem mực ↔ spec"] = "Compare the ink label ↔ spec",
    };
}
