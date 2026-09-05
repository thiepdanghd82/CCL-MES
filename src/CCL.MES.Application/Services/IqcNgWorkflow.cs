using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// P13 bước 5 — vòng đời một vụ claim NCC, dạng THUẦN (không DB, không giờ) để
/// khoá được bằng test và tái dùng ở cả đường nhập tay lẫn đường nạp lịch sử.
///
/// <para><b>Luật đo được, không phải luật bịa.</b> Trên 169 dòng thật của sheet
/// <c>NG Material</c> 2026: <b>0</b> dòng ở trạng thái đã-xử-lý-xong mà thiếu
/// ngày claim. Bất biến đó có thật trong cách người ta đang làm việc, nên ép nó
/// vào app là chép lại thực tế chứ không phải áp luật mới lên ai.</para>
/// </summary>
public static class IqcNgWorkflow
{
    /// <summary>
    /// Chuyển trạng thái này có hợp lệ không.
    ///
    /// <para>Hai trạng thái cuối là ĐIỂM DỪNG. Muốn mở lại một vụ đã khép thì
    /// phải là một hành động riêng có ghi lý do, không phải một lần đổi trạng
    /// thái lặng lẽ — vì con số "đòi được bao nhiêu" là thứ đem đi đàm phán hợp
    /// đồng, không được sửa mà không để vết.</para>
    /// </summary>
    public static bool CanTransition(IqcNgStatus from, IqcNgStatus to) => (from, to) switch
    {
        _ when from == to => false,

        (IqcNgStatus.Open, IqcNgStatus.Claimed) => true,
        (IqcNgStatus.Open, IqcNgStatus.ClosedNoClaim) => true,

        (IqcNgStatus.Claimed, IqcNgStatus.SupplierConfirmed) => true,
        // NCC bù thẳng không cần báo trước — 84/169 vụ đi đường này.
        (IqcNgStatus.Claimed, IqcNgStatus.Settled) => true,
        (IqcNgStatus.Claimed, IqcNgStatus.ClosedNoClaim) => true,

        (IqcNgStatus.SupplierConfirmed, IqcNgStatus.Settled) => true,
        (IqcNgStatus.SupplierConfirmed, IqcNgStatus.ClosedNoClaim) => true,

        _ => false,
    };

    public static bool IsTerminal(IqcNgStatus s) =>
        s is IqcNgStatus.Settled or IqcNgStatus.ClosedNoClaim;

    /// <summary>
    /// Bản ghi có đủ điều kiện để KHÉP LẠI ở trạng thái <c>Settled</c> chưa.
    /// <c>null</c> = hợp lệ; ngược lại là mã lỗi.
    /// </summary>
    public static string? ValidateSettle(IqcNgRecord r, IqcClaimSettlement settlement)
    {
        if (settlement == IqcClaimSettlement.None)
            return "iqc.ng.settlement_required";
        // Đo được: 0/169 vụ đã xử lý xong mà thiếu ngày claim. Khép một vụ chưa
        // từng gửi claim nghĩa là ghi vào hồ sơ rằng NCC đã đền cho một việc
        // chưa ai báo họ.
        if (r.ClaimedAt is null)
            return "iqc.ng.claim_required_before_settle";
        return null;
    }

    /// <summary>
    /// Bản ghi mới do người dùng nhập có đủ thông tin tối thiểu chưa.
    ///
    /// <para>Dữ liệu lịch sử có 7/169 dòng thiếu cả ba đơn vị số lượng, 29 dòng
    /// không ghi tên lỗi, 23 dòng không có ngày phát hiện. KHÔNG sửa lịch sử —
    /// đường nạp lịch sử bỏ qua hàm này và gắn cờ <c>ImportSource</c>. Nhưng
    /// bản ghi MỚI thì phải đủ, nếu không tỉ lệ trống 17% sẽ đi tiếp vào app.</para>
    /// </summary>
    public static string? ValidateNew(IqcNgRecord r)
    {
        if (r.DetectedAt == default)
            return "iqc.ng.detected_at_required";
        if (string.IsNullOrWhiteSpace(r.DefectName) && string.IsNullOrWhiteSpace(r.DefectCode))
            return "iqc.ng.defect_required";
        // Ít nhất MỘT đơn vị. Không ép cả ba: kho đếm cuộn, NCC tính m², sản
        // xuất tính mét — mỗi vụ chỉ có người ghi biết đơn vị nào là thật.
        if (r.NgQty is null && r.NgAreaM2 is null && r.NgRolls is null)
            return "iqc.ng.quantity_required";
        if (r.NgQty is <= 0 || r.NgAreaM2 is <= 0 || r.NgRolls is <= 0)
            return "iqc.ng.quantity_must_be_positive";
        if (string.IsNullOrWhiteSpace(r.PartNo) && r.IqcInspectionId is null && r.MaterialLotId is null)
            // Một vụ NG không nói được là của vật liệu nào thì không đòi ai
            // được, và cũng không vào được báo cáo theo NCC.
            return "iqc.ng.material_required";
        return null;
    }
}
