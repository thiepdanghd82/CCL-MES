namespace CCL.MES.Shared.Envelopes;

/// <summary>
/// Single shape for non-2xx responses across the new API. Uses an i18n key
/// rather than a literal English message so a Vietnamese client can render
/// the right text — clients hold the SharedResource translation map either
/// inline or by reaching back into the legacy resx via a downloadable
/// snapshot (P10.2).
///
/// Fields:
///   <c>Code</c> is a short stable key (eg. <c>"auth.invalid_credentials"</c>).
///   <c>MessageEn</c> is a fallback English string for ops / logs.
///   <c>Details</c> is an optional bag for field-specific validation errors
///   (key = field path, value = i18n key).
/// </summary>
public sealed record ApiError
{
    public string Code { get; init; } = "error.unknown";
    public string MessageEn { get; init; } = "An error occurred.";
    public Dictionary<string, string>? Details { get; init; }

    public static ApiError Of(string code, string messageEn) =>
        new() { Code = code, MessageEn = messageEn };

    /// <summary>
    /// Bản có <see cref="Details"/> — dùng khi câu tiếng Việt ở client cần
    /// DỮ KIỆN của lỗi (mã vật tư, số dòng, số lô…), không chỉ mã lỗi.
    ///
    /// <para><b>Vì sao không nhét vào <see cref="MessageEn"/>.</b> Client dịch
    /// theo <see cref="Code"/> và VỨT <c>MessageEn</c> đi, nên mọi dữ kiện chỉ
    /// nằm trong chuỗi tiếng Anh là mất trắng trước mắt người đứng máy. Đo
    /// 2026-09-18: server nói rõ dòng nào mã nào, màn hình chỉ hiện một câu
    /// chung chung, người vận hành không biết sờ vào đâu.</para>
    /// </summary>
    public static ApiError Of(string code, string messageEn, Dictionary<string, string> details) =>
        new() { Code = code, MessageEn = messageEn, Details = details };
}
