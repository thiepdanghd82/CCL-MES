namespace CCL.MES.Domain.Audit;

/// <summary>
/// A1 — mã audit của mạch lô nguyên vật liệu.
///
/// <para><b>Vì sao là class RIÊNG chứ không nối thêm vào
/// <see cref="AuditAction"/>.</b> Luật baseline read-only (AGENT-LOOP §4
/// STOP-gate #6, ngoại lệ Henry duyệt 2026-08-19) chỉ mở cho <b>file MỚI thuần
/// thêm</b>, dòng đăng ký <c>DbSet</c>/config, và migration — chứ không mở cho
/// việc sửa file cũ trong <c>src/CCL.MES.*</c>. Nối thêm hằng vào
/// <c>AuditAction.cs</c> tuy chỉ là thêm dòng nhưng vẫn là sửa file cũ, nên A1
/// giữ mã của mình ở đây. Giá trị chuỗi vẫn cùng một không gian tên phẳng như
/// mọi mã khác, nên truy vấn <c>AuditLogs</c> không đổi. Gộp lại vào
/// <c>AuditAction</c> là việc dọn dẹp một dòng-một-hằng, làm được bất cứ lúc
/// nào Henry cho phép chạm file đó.</para>
///
/// <para>Envelope JSON dùng chung cho cả 5 mã:
/// <c>{ wo_id, wo_no, bom_line_idx, leg_id, lot_no, material_lot_id, part_no,
/// qty_used, lot_status, error_code, enforced }</c> — trường nào không áp dụng
/// thì bỏ, KHÔNG ném cả entity vào detail.</para>
/// </summary>
public static class MaterialLotAuditAction
{
    /// <summary>Quét thành công: đã ghi một dòng <c>WoMaterialConsumptions</c>
    /// và trừ <c>MaterialLot.QtyAvailable</c>.</summary>
    public const string MaterialLotConsume     = "MATERIAL_LOT_CONSUME";

    /// <summary>Supervisor đảo một lần tiêu thụ (Đ3). Dòng cũ KHÔNG bị xoá —
    /// chỉ đánh dấu <c>ReversedAt/By/Reason</c>.</summary>
    public const string MaterialLotReverse     = "MATERIAL_LOT_REVERSE";

    /// <summary>
    /// Một lần quét BỊ TỪ CHỐI. Mọi ca từ chối đều emit — "ai đã cố nạp lô
    /// chưa Released, lên WO nào, lúc nào" là dữ liệu điều tra chất lượng,
    /// không phải noise. Tiền lệ đã có: <c>WO_QA_APPROVE_DENIED</c>,
    /// <c>WO_OQC_APPROVE_DENIED</c>.
    /// </summary>
    public const string MaterialLotScanDenied  = "MATERIAL_LOT_SCAN_DENIED";

    /// <summary>QC/Supervisor/Admin đổi trạng thái lô (IQC Pass/Fail, cách ly…).</summary>
    public const string MaterialLotStatusSet   = "MATERIAL_LOT_STATUS_SET";

    /// <summary>Gia hạn lô hết hạn sau kiểm lại (Đ3) — hai vai khác nhau.</summary>
    public const string MaterialLotExpiryExtended = "MATERIAL_LOT_EXPIRY_EXTENDED";
}
