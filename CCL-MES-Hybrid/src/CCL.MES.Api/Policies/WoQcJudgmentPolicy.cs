using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Kết quả parse phán quyết FQC. <see cref="ErrorCode"/> khác null nghĩa là
/// body không hợp lệ (thiếu / sai giá trị) — controller trả 422 với
/// <see cref="ErrorCode"/> + <see cref="Message"/>. Khi hợp lệ,
/// <see cref="Judgment"/> mang giá trị Pass hoặc Reject (không bao giờ Pending).
/// </summary>
public sealed record WoQcJudgmentParse
{
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public WoQcJudgment Judgment { get; init; }

    public bool IsValid => ErrorCode is null;

    public static WoQcJudgmentParse Ok(WoQcJudgment judgment) => new() { Judgment = judgment };
    public static WoQcJudgmentParse Fail(string code, string message) =>
        new() { ErrorCode = code, Message = message };
}

/// <summary>
/// Transition FQC đã chốt theo phán quyết: pha kế tiếp + audit action + có
/// freeze snapshot khi Pass hay không.
/// </summary>
public sealed record WoQcFqcTransition
{
    public required string NextPhase { get; init; }
    public required string AuditAction { get; init; }
    public required bool FreezeOnPass { get; init; }
}

/// <summary>
/// Luật QUYẾT ĐỊNH FQC judgment — tách khỏi <c>WoQcReviewController</c>
/// (1.400+ dòng) theo mẫu L47 để kiểm được bằng unit test thuần, không dựng
/// web host.
///
/// <para><b>Thuần — không I/O.</b> Nhận body thô (chuỗi phán quyết + lý do) và
/// trả phán quyết. Việc đọc/ghi DB, dựng HTTP response, emit audit vẫn ở
/// controller: <i>policy quyết định, controller thực thi</i>.</para>
///
/// <para><b>Nhiều hàm nhỏ chứ không một hàm gộp — cố ý.</b> Trong action
/// <c>PostFqcJudgment</c>, hai nhóm kiểm bị NGẮT QUÃNG bởi phần kiểm sẵn-sàng
/// (readiness) đọc từ DB: parse phán quyết chạy TRƯỚC readiness, còn kiểm
/// lý-do-khi-Reject chạy SAU readiness. Gộp <c>ParseJudgment</c> +
/// <c>ValidateRejectReason</c> vào một lời gọi sẽ đổi mã lỗi trả về cho request
/// vừa gửi phán quyết sai vừa chưa sẵn sàng — một thay đổi hành vi âm thầm ở ca
/// biên. Giữ hai hàm để chèn đúng chỗ cũ (mirror L47 <c>OqcSignaturePolicy</c>).</para>
///
/// <para><b>Ghi chú phạm vi.</b> Nơi đúng của policy là Domain, nhưng
/// <c>src/CCL.MES.Domain</c> là baseline read-only cho tới khi cutover xong —
/// đặt tạm trong <c>Api/Policies/</c> cạnh <see cref="OqcSignaturePolicy"/>.</para>
/// </summary>
public static class WoQcJudgmentPolicy
{
    // Mã kind chuẩn hoá — mirror hằng số private trong WoQcReviewController.
    private const string KindFqc = "FQC";

    public const string InvalidJudgment = "qc.invalid_judgment";
    public const string InvalidReason   = "qc.invalid_reason";

    /// <summary>
    /// Pha mong đợi cho một kind QC. FQC → FQC_PENDING; mọi kind khác (đã
    /// normalize về OQC) → OQC_PENDING. Giữ đúng semantics của biểu thức
    /// <c>normKind == KindFqc ? "FQC_PENDING" : "OQC_PENDING"</c> lặp ở 3 nơi.
    /// </summary>
    public static string ExpectedPhaseForKind(string kind) =>
        kind == KindFqc ? "FQC_PENDING" : "OQC_PENDING";

    /// <summary>
    /// Parse chuỗi phán quyết → Pass/Reject. Gọi TRƯỚC kiểm readiness.
    /// Null/blank → invalid_judgment ("required"); parse-fail HOẶC Pending →
    /// invalid_judgment ("must be Pass or Reject"). Thứ tự + message giữ
    /// nguyên byte-identical với bản trong controller.
    /// </summary>
    public static WoQcJudgmentParse ParseJudgment(string? rawJudgment)
    {
        if (string.IsNullOrWhiteSpace(rawJudgment))
            return WoQcJudgmentParse.Fail(InvalidJudgment,
                "Judgment is required (\"Pass\" or \"Reject\").");

        if (!Enum.TryParse<WoQcJudgment>(rawJudgment, ignoreCase: true, out var judgment)
            || judgment == WoQcJudgment.Pending)
            return WoQcJudgmentParse.Fail(InvalidJudgment,
                $"Judgment must be Pass or Reject; got \"{rawJudgment}\".");

        return WoQcJudgmentParse.Ok(judgment);
    }

    /// <summary>
    /// Kiểm lý do khi phán quyết là Reject. Gọi SAU kiểm readiness. Reject bắt
    /// buộc lý do 1–500 ký tự; Pass → luôn hợp lệ (trả null). Trả tuple
    /// (ErrorCode, Message) khi vi phạm, null khi hợp lệ.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateRejectReason(
        WoQcJudgment judgment, string? judgmentReason)
    {
        if (judgment != WoQcJudgment.Reject)
            return null;

        if (string.IsNullOrWhiteSpace(judgmentReason) || judgmentReason!.Length > 500)
            return (InvalidReason,
                "JudgmentReason is required (1-500 chars) for Reject.");

        return null;
    }

    /// <summary>
    /// Transition đã chốt cho một phán quyết hợp lệ: Pass → OQC_PENDING +
    /// WO_FQC_JUDGMENT + freeze; Reject → PREPRESS + WO_FQC_REJECT_TO_PREPRESS
    /// + no-freeze.
    /// </summary>
    public static WoQcFqcTransition Transition(WoQcJudgment judgment) =>
        judgment == WoQcJudgment.Pass
            ? new WoQcFqcTransition
            {
                NextPhase    = "OQC_PENDING",
                AuditAction  = AuditAction.WoFqcJudgment,
                FreezeOnPass = true,
            }
            : new WoQcFqcTransition
            {
                NextPhase    = "PREPRESS",
                AuditAction  = AuditAction.WoFqcRejectToPrepress,
                FreezeOnPass = false,
            };

    /// <summary>
    /// Lý do lưu vào <c>WoQcCheck.JudgmentReason</c>: chỉ giữ khi Reject; Pass →
    /// null. Tách thành helper để controller không lặp biểu thức điều kiện.
    /// </summary>
    public static string? PersistedReason(WoQcJudgment judgment, string? judgmentReason) =>
        judgment == WoQcJudgment.Reject ? judgmentReason : null;
}
