using CCL.MES.Domain;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Kết quả parse phán quyết IPQC. <see cref="ErrorCode"/> khác null nghĩa là
/// body không hợp lệ (thiếu / sai giá trị) — controller trả 422 với
/// <see cref="ErrorCode"/> + <see cref="ErrorMessage"/>. Khi hợp lệ,
/// <see cref="Judgment"/> mang giá trị GoRun / StopLine / SpecialAccept
/// (không bao giờ Pending).
/// </summary>
public sealed record IpqcJudgmentParse
{
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IpqcJudgment Judgment { get; init; }

    public bool IsValid => ErrorCode is null;

    public static IpqcJudgmentParse Ok(IpqcJudgment judgment) => new() { Judgment = judgment };
    public static IpqcJudgmentParse Fail(string code, string message) =>
        new() { ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// Transition IPQC đã chốt theo phán quyết: pha kế tiếp + có freeze snapshot khi
/// GoRun hay không. Audit action IPQC judgment LUÔN là <c>WO_IPQC_JUDGMENT</c>
/// (bất kể phán quyết) nên KHÔNG mang trong record này — controller giữ nguyên.
/// </summary>
public sealed record IpqcJudgmentTransition
{
    public required string NextPhase { get; init; }
    public required bool FreezeOnGoRun { get; init; }
}

/// <summary>
/// Luật QUYẾT ĐỊNH IPQC judgment — tách khỏi <c>IpqcReviewController</c>
/// theo mẫu <see cref="WoQcJudgmentPolicy"/> để kiểm được bằng unit test thuần,
/// không dựng web host.
///
/// <para><b>Thuần — không I/O.</b> Nhận body thô (chuỗi phán quyết + lý do) và
/// trả phán quyết. Việc đọc/ghi DB, dựng HTTP response, emit audit, freeze
/// snapshot vẫn ở controller: <i>policy quyết định, controller thực thi</i>.</para>
///
/// <para><b>Nhiều hàm nhỏ chứ không một hàm gộp — cố ý.</b> Trong action
/// <c>PostJudgment</c>, hai nhóm kiểm bị NGẮT QUÃNG bởi phần kiểm sẵn-sàng
/// (readiness) + consistency đọc từ DB: <see cref="ParseJudgment"/> chạy TRƯỚC
/// readiness, còn <see cref="ValidateSpecialAcceptReason"/> chạy SAU consistency.
/// Gộp hai hàm vào một lời gọi sẽ đổi mã lỗi trả về cho request vừa gửi phán
/// quyết sai vừa chưa ready (phải nhận <c>ipqc.invalid_judgment</c> trước), hoặc
/// cho request SpecialAccept thiếu lý do nhưng inconsistent (phải nhận
/// <c>ipqc.judgment_inconsistent</c> trước) — một thay đổi hành vi âm thầm ở ca
/// biên. Giữ hai hàm để chèn đúng chỗ cũ (mirror <see cref="WoQcJudgmentPolicy"/>).</para>
///
/// <para><b>Ghi chú phạm vi.</b> Nơi đúng của policy là Domain, nhưng
/// <c>src/CCL.MES.Domain</c> là baseline read-only cho tới khi cutover xong —
/// đặt tạm trong <c>Api/Policies/</c> cạnh <see cref="WoQcJudgmentPolicy"/>.</para>
/// </summary>
public static class IpqcJudgmentPolicy
{
    public const string InvalidJudgment            = "ipqc.invalid_judgment";
    public const string InvalidSpecialAcceptReason = "ipqc.invalid_special_accept_reason";

    /// <summary>
    /// Parse chuỗi phán quyết → GoRun/StopLine/SpecialAccept. Gọi TRƯỚC kiểm
    /// readiness. Null/blank → invalid_judgment ("required"); parse-fail HOẶC
    /// Pending → invalid_judgment ("must be GoRun / StopLine / SpecialAccept")
    /// dùng CHUỖI GỐC (raw) trong message. Thứ tự + message giữ nguyên
    /// byte-identical với bản trong controller.
    /// </summary>
    public static IpqcJudgmentParse ParseJudgment(string? rawJudgment)
    {
        if (string.IsNullOrWhiteSpace(rawJudgment))
            return IpqcJudgmentParse.Fail(InvalidJudgment,
                "Judgment is required (\"GoRun\", \"StopLine\", or \"SpecialAccept\").");

        if (!Enum.TryParse<IpqcJudgment>(rawJudgment, ignoreCase: true, out var judgment)
            || judgment == IpqcJudgment.Pending)
            return IpqcJudgmentParse.Fail(InvalidJudgment,
                $"Judgment must be GoRun / StopLine / SpecialAccept; got \"{rawJudgment}\".");

        return IpqcJudgmentParse.Ok(judgment);
    }

    /// <summary>
    /// Kiểm lý do khi phán quyết là SpecialAccept. Gọi SAU kiểm consistency.
    /// SpecialAccept bắt buộc lý do 1–500 ký tự; GoRun / StopLine → luôn hợp lệ
    /// (trả null). Trả tuple (ErrorCode, Message) khi vi phạm, null khi hợp lệ.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateSpecialAcceptReason(
        IpqcJudgment judgment, string? reason)
    {
        if (judgment != IpqcJudgment.SpecialAccept)
            return null;

        if (string.IsNullOrWhiteSpace(reason) || reason!.Length > 500)
            return (InvalidSpecialAcceptReason,
                "SpecialAcceptReason is required (1-500 chars) for SpecialAccept judgment.");

        return null;
    }

    /// <summary>
    /// Transition đã chốt cho một phán quyết hợp lệ: GoRun → IPQC_APPROVED +
    /// freeze; StopLine → PREPRESS + no-freeze; SpecialAccept → QA_PENDING +
    /// no-freeze. Với giá trị ngoài 3 nhánh (không xảy ra sau ParseJudgment),
    /// giữ semantics cũ: pha không đổi (controller <c>wo.MesPhase</c> giữ nguyên)
    /// + no-freeze.
    /// </summary>
    public static IpqcJudgmentTransition Transition(IpqcJudgment judgment) =>
        judgment switch
        {
            IpqcJudgment.GoRun         => new IpqcJudgmentTransition { NextPhase = "IPQC_APPROVED", FreezeOnGoRun = true },
            IpqcJudgment.StopLine      => new IpqcJudgmentTransition { NextPhase = "PREPRESS",      FreezeOnGoRun = false },
            IpqcJudgment.SpecialAccept => new IpqcJudgmentTransition { NextPhase = "QA_PENDING",    FreezeOnGoRun = false },
            _                          => new IpqcJudgmentTransition { NextPhase = "",              FreezeOnGoRun = false },
        };
}
