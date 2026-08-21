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
/// Kết quả parse outcome QA approve. <see cref="ErrorCode"/> khác null nghĩa là
/// body không hợp lệ (thiếu / sai giá trị) — controller trả 422 với
/// <see cref="ErrorCode"/> + <see cref="ErrorMessage"/>. Khi hợp lệ,
/// <see cref="Outcome"/> mang giá trị Approve hoặc Reject (không bao giờ Pending).
/// </summary>
public sealed record QaApproveOutcomeParse
{
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public QaOutcome Outcome { get; init; }

    public bool IsValid => ErrorCode is null;

    public static QaApproveOutcomeParse Ok(QaOutcome outcome) => new() { Outcome = outcome };
    public static QaApproveOutcomeParse Fail(string code, string message) =>
        new() { ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// Transition QA approve đã chốt theo outcome: pha kế tiếp + có freeze IPQC
/// snapshot khi Approve hay không. Audit action QA approve LUÔN là
/// <c>WO_QA_APPROVE</c> (bất kể outcome) nên KHÔNG mang trong record này —
/// controller giữ nguyên.
/// </summary>
public sealed record QaApproveTransitionResult
{
    public required string NextPhase { get; init; }
    public required bool FreezeOnApprove { get; init; }
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
    public const string InvalidQaOutcome           = "qa.invalid_outcome";
    public const string InvalidQaReason            = "qa.invalid_qa_reason";

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

    // ─────────────────────────────────────────────────────────────────
    // QA approve — outcome parse + qa-reason + transition.
    //
    // Q3 dual-sig guard (WoIpqcCheckService.ValidateDualSig + WO_QA_APPROVE_DENIED
    // audit) do controller giữ NGUYÊN VĂN. Ở đây CHỈ tách phần OUTCOME/REASON/
    // TRANSITION của action qa/approve: parse "Approve"/"Reject", kiểm qa-reason,
    // và ánh xạ outcome → (pha kế, freeze-on-Approve).
    //
    // ⚠ THỨ TỰ trong controller giữ NGUYÊN: ParseQaOutcome → GetOrCreateCheck →
    // Q3 dual-sig guard → ValidateQaReason. ParseQaOutcome đứng TRƯỚC dual-sig
    // để request vừa sai outcome vừa trùng vai vẫn nhận qa.invalid_outcome trước;
    // ValidateQaReason đứng SAU dual-sig để request đúng outcome + trùng vai +
    // thiếu reason nhận qa.same_user_as_ipqc_submitter trước — không đổi hành vi
    // ca biên. TÁCH 2 HÀM (không gộp) vì hai kiểm bị dual-sig NGẮT QUÃNG.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse chuỗi outcome QA approve → Approve/Reject. Gọi TRƯỚC dual-sig guard.
    /// Null/blank → invalid_outcome ("required"); parse-fail HOẶC Pending →
    /// invalid_outcome ("must be Approve or Reject") dùng CHUỖI GỐC (raw) trong
    /// message. Thứ tự + message giữ nguyên byte-identical với bản trong controller.
    /// </summary>
    public static QaApproveOutcomeParse ParseQaOutcome(string? rawOutcome)
    {
        if (string.IsNullOrWhiteSpace(rawOutcome))
            return QaApproveOutcomeParse.Fail(InvalidQaOutcome,
                "Outcome is required (\"Approve\" or \"Reject\").");

        if (!Enum.TryParse<QaOutcome>(rawOutcome, ignoreCase: true, out var outcome)
            || outcome == QaOutcome.Pending)
            return QaApproveOutcomeParse.Fail(InvalidQaOutcome,
                $"Outcome must be Approve or Reject; got \"{rawOutcome}\".");

        return QaApproveOutcomeParse.Ok(outcome);
    }

    /// <summary>
    /// Kiểm QA reason theo outcome. Gọi SAU dual-sig guard. Reject bắt buộc lý do
    /// 1–500 ký tự; Approve → lý do optional NHƯNG nếu có và >500 → lỗi (hai
    /// message KHÁC NHAU word-for-word). Trả tuple (ErrorCode, Message) khi vi
    /// phạm, null khi hợp lệ.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateQaReason(
        QaOutcome outcome, string? reason)
    {
        if (outcome == QaOutcome.Reject)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason!.Length > 500)
                return (InvalidQaReason,
                    "QaReason is required (1-500 chars) for Reject outcome.");
        }
        else if (reason is not null && reason.Length > 500)
        {
            return (InvalidQaReason,
                "QaReason must be 0-500 chars on Approve outcome.");
        }

        return null;
    }

    /// <summary>
    /// Transition đã chốt cho outcome QA approve hợp lệ: Approve → IPQC_APPROVED +
    /// freeze; Reject → PREPRESS + no-freeze. Với giá trị ngoài 2 nhánh (không xảy
    /// ra sau ParseQaOutcome), giữ semantics cũ: Reject-path (PREPRESS + no-freeze).
    /// </summary>
    public static QaApproveTransitionResult QaApproveTransition(QaOutcome outcome) =>
        outcome == QaOutcome.Approve
            ? new QaApproveTransitionResult { NextPhase = "IPQC_APPROVED", FreezeOnApprove = true }
            : new QaApproveTransitionResult { NextPhase = "PREPRESS",      FreezeOnApprove = false };
}
