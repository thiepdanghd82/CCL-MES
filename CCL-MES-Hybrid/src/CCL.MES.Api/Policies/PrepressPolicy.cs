using CCL.MES.Domain;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Kết quả parse trạng thái một hàng PREPRESS. <see cref="ErrorCode"/> khác null
/// nghĩa là body không hợp lệ (thiếu / sai giá trị) — controller trả 422 với
/// <see cref="ErrorCode"/> + <see cref="ErrorMessage"/>. Khi hợp lệ,
/// <see cref="Status"/> mang Pending / Ok / Ng.
/// </summary>
public sealed record PrepressStatusParse
{
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public PrepressCheckStatus Status { get; init; }

    public bool IsValid => ErrorCode is null;

    public static PrepressStatusParse Ok(PrepressCheckStatus status) => new() { Status = status };
    public static PrepressStatusParse Fail(string code, string message) =>
        new() { ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// Luật KIỂM GIÁ TRỊ body PREPRESS (status + NG format) — tách khỏi
/// <c>PrepressController</c> theo mẫu <see cref="IpqcJudgmentPolicy"/> /
/// <see cref="RunningSurfacePolicy"/> để kiểm được bằng unit test thuần, không
/// dựng web host (L47). Cả hai hàm dùng lại ở 3 endpoint (materials / plate /
/// cutter) nên tách còn khử trùng lặp, không chỉ mua khả năng kiểm chứng.
///
/// <para><b>Thuần — không I/O.</b> <see cref="ParseStatus"/> parse chuỗi →
/// enum; <see cref="ValidateNgFormat"/> kiểm status→reason→note. Việc tra danh
/// mục ReasonCode(Kind=Scrap) là I/O DB nên Ở LẠI controller SAU khi format hợp
/// lệ — đúng thứ tự cũ trong <c>ValidateNgAsync</c>.</para>
///
/// <para><b>Byte-identical.</b> Mã lỗi + message + thứ tự giữ nguyên hệt bản
/// inline (test wire/integration cũ không sửa mà vẫn xanh là bằng chứng).</para>
///
/// <para><b>Ghi chú phạm vi.</b> Nơi đúng là Domain, nhưng
/// <c>src/CCL.MES.Domain</c> baseline read-only tới khi cutover xong — đặt tạm
/// ở <c>Api/Policies/</c> cạnh các policy A2 khác.</para>
/// </summary>
public static class PrepressPolicy
{
    public const string InvalidStatus     = "prepress.invalid_status";
    public const string InvalidReasonCode = "prepress.invalid_reason_code";
    public const string InvalidNgNote     = "prepress.invalid_ng_note";

    /// <summary>
    /// Parse chuỗi status → Pending / Ok / Ng (tolerant case-insensitive).
    /// Null/blank → invalid_status ("required"); parse-fail → invalid_status
    /// dùng CHUỖI GỐC (raw) trong message. Byte-identical với bản controller.
    /// </summary>
    public static PrepressStatusParse ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return PrepressStatusParse.Fail(InvalidStatus,
                "Status is required (one of: Pending / Ok / Ng).");

        if (!Enum.TryParse<PrepressCheckStatus>(raw, ignoreCase: true, out var v))
            return PrepressStatusParse.Fail(InvalidStatus,
                $"Status '{raw}' is not one of Pending / Ok / Ng.");

        return PrepressStatusParse.Ok(v);
    }

    /// <summary>
    /// Kiểm ĐỊNH DẠNG NG. Không phải Ng → luôn hợp lệ (null), controller bỏ qua
    /// cả lần tra danh mục. Là Ng: mã lý do bắt buộc; ghi chú 1–500 ký tự.
    /// Trả (ErrorCode, Message) khi vi phạm, null khi hợp lệ. Thứ tự
    /// status→reason→note giữ nguyên; lần tra ReasonCodes(Kind=Scrap) do
    /// controller làm SAU khi format hợp lệ.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateNgFormat(
        PrepressCheckStatus status, string? ngReasonCode, string? ngNote)
    {
        if (status != PrepressCheckStatus.Ng)
            return null;

        if (string.IsNullOrWhiteSpace(ngReasonCode))
            return (InvalidReasonCode, "NgReasonCode is required when status=NG.");
        if (string.IsNullOrWhiteSpace(ngNote) || ngNote!.Length > 500)
            return (InvalidNgNote, "NgNote must be 1-500 chars when status=NG.");

        return null;
    }
}
