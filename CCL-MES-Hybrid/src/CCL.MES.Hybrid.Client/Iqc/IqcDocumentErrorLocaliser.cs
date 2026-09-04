using CCL.MES.Hybrid.Client;

namespace CCL.MES.Hybrid.Client.Iqc;

/// <summary>
/// P12 bước 4 — dịch mã lỗi của <c>IqcDocumentController</c> thành câu người
/// vận hành đọc được.
///
/// <para>Khác các localiser cũ (Routing / SemiStock / Prepress) vốn hardcode
/// tiếng Việt trong file: ở đây nhận vào hàm tra <c>T</c> nên chuỗi nằm trong
/// <c>TranslationCatalog</c> và có ĐỦ VI + EN. Module IQC đã đi theo
/// <c>LocalizedComponentBase</c>, để một bảng chữ tiếng Việt cứng lọt vào giữa
/// là tự tay đục một lỗ thủng trong parity.</para>
///
/// <para>Static + thuần ⇒ xUnit khoá được wording mà không cần dựng MAUI.</para>
/// </summary>
public static class IqcDocumentErrorLocaliser
{
    /// <summary>Map <c>ApiError.Code</c> → khoá i18n. Trả <c>null</c> cho mã lạ
    /// để caller tự dựng chuỗi chẩn đoán có kèm HTTP status.</summary>
    public static string? KeyFor(string? code) => code switch
    {
        "iqc.doc_edit_forbidden"      => "iqc.doc.err.forbidden",
        "iqc.doc_not_found"           => "iqc.doc.err.notfound",
        "iqc.doc_file_missing"        => "iqc.doc.nofile.hint",

        // Ba trường bắt buộc gộp về MỘT câu: người dùng nhìn thấy cả ba ô cùng
        // lúc, tách thành ba câu chỉ khiến họ sửa một ô rồi lại bị chặn tiếp.
        "iqc.doc_number_required"     => "iqc.doc.err.required",
        "iqc.doc_issue_required"      => "iqc.doc.err.required",
        "iqc.doc_expiry_required"     => "iqc.doc.err.required",
        "iqc.doc_expiry_before_issue" => "iqc.doc.err.expiry",

        "iqc.doc_type_duplicate"      => "iqc.doc.err.duplicate",
        "iqc.doc_type_required"       => "iqc.doc.err.badtype",
        "iqc.doc_type_too_long"       => "iqc.doc.err.badtype",
        "iqc.doc_number_too_long"     => "iqc.doc.err.badtype",
        "iqc.doc_material_required"   => "iqc.doc.err.nomaterial",

        "iqc.doc_too_large"           => "iqc.doc.err.toolarge",
        "iqc.doc_file_rejected"       => "iqc.doc.err.toolarge",
        "iqc.doc_empty_upload"        => "iqc.doc.err.emptyfile",
        _                             => null,
    };

    /// <summary>Câu hoàn chỉnh cho banner. <paramref name="t"/> là hàm tra
    /// i18n của component gọi tới.</summary>
    public static string Localise(ApiException ex, Func<string, string> t)
    {
        var key = KeyFor(ex.ApiError.Code);
        if (key is not null) return t(key);

        // Mã chưa ai khai: hiện nguyên trạng CÓ status + code. Nuốt nó thành
        // "có lỗi xảy ra" là cách chắc chắn để không ai debug được tại xưởng.
        return $"HTTP {ex.StatusCode} · {ex.ApiError.Code} · {ex.ApiError.MessageEn}";
    }
}
