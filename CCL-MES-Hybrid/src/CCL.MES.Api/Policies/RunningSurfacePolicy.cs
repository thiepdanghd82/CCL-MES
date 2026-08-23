using CCL.MES.Shared.RunningSurface;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Luật KIỂM GIÁ TRỊ body của bề mặt RUNNING (run/qty · run/qty/correct ·
/// run/pause) — tách khỏi <c>RunningSurfaceController</c> theo mẫu
/// <see cref="IpqcJudgmentPolicy"/> / <see cref="WoQcJudgmentPolicy"/> để kiểm
/// được bằng unit test thuần, không dựng web host (L47 — "mua khả năng kiểm chứng").
///
/// <para><b>Thuần — không I/O.</b> Chỉ kiểm định-dạng/giá-trị của body (delta,
/// độ dài lý do/ghi chú, presence). Phần tra danh mục ReasonCode (Pause/Scrap)
/// là I/O DB nên Ở LẠI controller: <i>policy quyết định phần thuần, controller
/// tra DB + dựng response + emit audit</i>.</para>
///
/// <para><b>Non-null theo thiết kế.</b> Ca <c>req is null</c> giữ INLINE trong
/// controller (một dòng) để flow-analysis của compiler biết req≠null sau guard —
/// nếu nhét null-check vào policy thì mọi deref <c>req.</c> sau đó phải rải
/// <c>!</c>. Với run/pause, ca null trả cùng mã/message như ca blank
/// (<c>running.invalid_reason_code</c> · "ReasonCode is required.") nên tách
/// null ra inline KHÔNG đổi hành vi.</para>
///
/// <para><b>Byte-identical.</b> Mã lỗi + message + THỨ TỰ kiểm giữ nguyên hệt
/// bản inline trong controller (các test wire/integration cũ không đổi mà vẫn
/// xanh là bằng chứng). Với NG, format (reason rỗng → note dài) kiểm TRƯỚC lần
/// tra danh mục — đúng thứ tự cũ trong <c>ValidateNgAsync</c>.</para>
///
/// <para><b>Ghi chú phạm vi.</b> Nơi đúng của policy là Domain, nhưng
/// <c>src/CCL.MES.Domain</c> là baseline read-only tới khi cutover xong — đặt
/// tạm ở <c>Api/Policies/</c> cạnh các policy A2 khác.</para>
/// </summary>
public static class RunningSurfacePolicy
{
    public const string InvalidQtyDelta         = "running.invalid_qty_delta";
    public const string InvalidReasonCode       = "running.invalid_reason_code";
    public const string InvalidNgNote           = "running.invalid_ng_note";
    public const string InvalidCorrectionReason = "running.invalid_correction_reason";
    public const string InvalidNote             = "running.invalid_note";

    /// <summary>
    /// Kiểm giá trị <c>/run/qty</c>: delta không âm (âm phải qua /run/qty/correct);
    /// ít nhất một delta &gt; 0. Ca body null giữ inline ở controller
    /// (<c>running.invalid_body</c>). Kiểm danh mục NG (khi QtyNgDelta&gt;0) do
    /// controller làm SAU, qua <see cref="ValidateNgFormat"/> + tra DB.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateQtyAdd(RunQtyAddRequest req)
    {
        if (req.QtyDoneDelta < 0 || req.QtyNgDelta < 0)
            return (InvalidQtyDelta, "Add deltas must be >= 0; use /run/qty/correct for negative.");
        if (req.QtyDoneDelta == 0 && req.QtyNgDelta == 0)
            return (InvalidQtyDelta, "At least one of QtyDoneDelta or QtyNgDelta must be > 0.");
        return null;
    }

    /// <summary>
    /// Kiểm ĐỊNH DẠNG NG (khi QtyNgDelta&gt;0): mã lý do bắt buộc; ghi chú 1–500
    /// ký tự. KHÔNG tra danh mục ở đây — controller tra ReasonCodes(Kind=Scrap)
    /// SAU khi format hợp lệ. Thứ tự reason→note giữ nguyên bản cũ.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateNgFormat(string? ngReasonCode, string? ngNote)
    {
        if (string.IsNullOrWhiteSpace(ngReasonCode))
            return (InvalidReasonCode, "NgReasonCode is required when QtyNgDelta > 0.");
        if (string.IsNullOrWhiteSpace(ngNote) || ngNote!.Length > 500)
            return (InvalidNgNote, "NgNote must be 1-500 chars when QtyNgDelta > 0.");
        return null;
    }

    /// <summary>
    /// Kiểm giá trị <c>/run/qty/correct</c>: lý do sửa 1–500 ký tự. Ca body null
    /// giữ inline ở controller (<c>running.invalid_body</c>). Việc tra
    /// <c>LinkedEntryId</c> (tồn tại + đúng WO) là I/O DB nên ở lại controller.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateQtyCorrect(RunQtyCorrectRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CorrectionReason) || req.CorrectionReason.Length > 500)
            return (InvalidCorrectionReason, "CorrectionReason is required (1-500 chars).");
        return null;
    }

    /// <summary>
    /// Kiểm ĐỊNH DẠNG body <c>/run/pause</c>: mã lý do bắt buộc; ghi chú ≤500 ký
    /// tự. Ca body null giữ inline ở controller (cùng mã/message như ca blank).
    /// Việc tra danh mục ReasonCodes(Kind=Pause) là I/O DB nên ở lại controller.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidatePauseFormat(RunPauseRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ReasonCode))
            return (InvalidReasonCode, "ReasonCode is required.");
        if (req.Note is not null && req.Note.Length > 500)
            return (InvalidNote, "Note must be 0-500 chars.");
        return null;
    }
}
