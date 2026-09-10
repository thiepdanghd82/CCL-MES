using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;

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
    public const string LotRequired       = "prepress.lot_required";
    public const string LotNotReleased    = "prepress.lot_not_released";
    public const string PartScanRequired  = "prepress.part_scan_required";
    public const string PartScanMismatch  = "prepress.part_scan_mismatch";

    /// <summary>
    /// Mã quét phải TRÙNG mã vật tư của dòng BOM.
    ///
    /// <para><b>Vì sao ở server.</b> Client đã có đối chiếu và báo đúng ("Typed
    /// 30031638 ≠ line 30031146"), nhưng server nhận tất: <c>PrepressController</c>
    /// chỉ có <c>if (!IsNullOrWhiteSpace) row.PartScan = req.PartScan</c>. Cảnh
    /// báo mà không chặn thì chỉ cần bấm tiếp là qua — đo được trên WO thật:
    /// dòng mã 30031146 mang PartScan 30031638 mà Status vẫn Ok.</para>
    ///
    /// <para><b>Sai mã KHÔNG được nhân nhượng, kể cả Special Accept.</b> Special
    /// Accept là để chấp nhận một LÔ chưa đạt điều kiện — có lý do, có vai, có
    /// audit. Ghi nhầm MÃ thì không phải nhân nhượng mà là làm sai hồ sơ truy
    /// xuất: về sau không ai biết cuộn nào đã thật sự vào đơn hàng. Cùng tinh
    /// thần với <c>lot.part_mismatch</c> ở tầng lô — mã đó cũng không nới theo
    /// cờ EnforceReleased.</para>
    ///
    /// <para>Quét là BẮT BUỘC khi xác nhận Ok: dòng chưa quét là dòng chưa ai
    /// đối chiếu vật tư thật với BOM. Pending/NG không bị đòi.</para>
    /// </summary>
    public static (string ErrorCode, string Message)? ValidatePartScan(
        PrepressCheckStatus status, string? partScanAfterWrite, string? materialCode)
    {
        var scan = partScanAfterWrite?.Trim();

        // Có gửi mã quét thì phải khớp — đúng ở MỌI trạng thái, vì mã sai ghi
        // vào hồ sơ lúc Pending cũng vẫn là mã sai.
        if (!string.IsNullOrEmpty(scan)
            && !string.Equals(scan, materialCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (PartScanMismatch,
                $"Scanned part '{scan}' does not match this BOM line ('{materialCode}').");
        }

        if (status == PrepressCheckStatus.Ok && string.IsNullOrEmpty(scan))
            return (PartScanRequired,
                "Scan the material label before confirming this line OK.");

        return null;
    }

    /// <summary>
    /// Xác nhận Ok thì lô phải TRA ĐƯỢC về một MaterialLot và lô đó phải
    /// <c>Released</c> — nghĩa là IQC đã kết luận đạt.
    ///
    /// <para>Đây là bậc trên của <see cref="ValidateLotPresence"/>: có số lô mới
    /// chỉ chứng minh người vận hành đã gõ gì đó, chưa chứng minh cuộn đó được
    /// phép lên máy.</para>
    ///
    /// <para><b>Đường xả là Special Accept</b>, không phải nới luật. Đo trên
    /// live 2026-09-09: 46/103 dòng vật tư chưa có lô nào trong hệ (kho chưa
    /// nhập liệu IQC cho mã đó) — những dòng ấy đi qua Special Accept, vốn đã
    /// role-gate Engineer/Supervisor và bắt ghi lý do. Chặn cứng mà không có
    /// đường xả thì thành dừng chuyền; nới luật thì thành không có luật.</para>
    /// </summary>
    /// <param name="isInHouse">Bán thành phẩm tự làm ⇒ MIỄN cổng lô. Xem giải
    /// thích đầy đủ ở <see cref="MaterialsReadinessRollup.IsLineReady"/>: IQC
    /// chỉ phủ hàng MUA, nên bắt bán thành phẩm có MaterialLot Released là bắt
    /// một điều kiện vĩnh viễn không thể đạt.</param>
    public static (string ErrorCode, string Message)? ValidateLotReleased(
        PrepressCheckStatus status, long? materialLotId, string? lotStatus,
        bool isInHouse = false)
    {
        if (status != PrepressCheckStatus.Ok) return null;
        if (isInHouse) return null;

        if (materialLotId is null)
            return (LotNotReleased,
                "This lot is not registered from IQC — use Special Accept if you must proceed.");

        if (!string.Equals(lotStatus, nameof(MaterialLotStatus.Released), StringComparison.OrdinalIgnoreCase))
            return (LotNotReleased,
                $"Lot is '{lotStatus}', not Released by IQC — use Special Accept if you must proceed.");

        return null;
    }

    /// <summary>
    /// Xác nhận OK thì PHẢI có số lô.
    ///
    /// <para><b>Vì sao.</b> Đo trên live 2026-09-09, WO-TEST-LOT-01: màn hình
    /// báo <c>10/10 materials OK</c> + huy hiệu <c>Materials Ready</c>, trong
    /// khi DB có <b>10/10 dòng LotNo rỗng, MaterialLotId NULL, 0 dòng tiêu
    /// thụ</b>. Vòng quét đặt OK cho cả 10 dòng chỉ từ mã part, và không luật
    /// nào đòi lô — nên "sẵn sàng chạy" không chứng minh điều gì. Tệ hơn: IPQC
    /// cũng không đỡ được, vì luật divergence cần <c>MaterialLotId</c>; FK NULL
    /// làm MỌI dòng bật cờ lệch, mà tín hiệu bật cho tất cả thì hết là tín hiệu.
    /// </para>
    ///
    /// <para><b>Chỉ đòi SỐ LÔ, chưa đòi resolve về một MaterialLot.</b> Đo được
    /// <b>46/103</b> dòng vật tư hiện không có lô nào trong hệ (kho chưa nhập
    /// liệu IQC cho mã đó). Đòi FK ngay là 46 dòng vĩnh viễn không OK được và WO
    /// kẹt ở PREPRESS — siết chất lượng thành dừng chuyền. Bậc "phải resolve và
    /// phải Released" thuộc về chốt chặn ở rollup, đi kèm đường Engineer waiver.
    /// </para>
    ///
    /// <para>NG và Pending KHÔNG bị đòi: đánh NG là đang báo có vấn đề, bắt khai
    /// đủ giấy tờ trước khi cho báo là chặn nhầm hướng.</para>
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateLotPresence(
        PrepressCheckStatus status, string? lotAfterWrite)
    {
        if (status != PrepressCheckStatus.Ok) return null;
        if (!string.IsNullOrWhiteSpace(lotAfterWrite)) return null;
        return (LotRequired,
            "Lot number is required before a material line can be confirmed OK.");
    }

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
