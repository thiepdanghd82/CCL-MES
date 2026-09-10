using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P10.7b-1 — pure helper computing the legacy <c>WorkOrder.MaterialsReady</c>
/// bool from the new row-level PREPRESS child tables (per breakdown
/// "Legacy parity decision — additive cached rollup").
///
/// The rollup is true only when:
///   1. At least one <see cref="WoMaterial"/> row exists for the WO
///      (no snapshot ⇒ legacy bool stays authoritative — preserves
///      zero-diff for pre-7b WOs that have no BOM snapshot yet).
///   2. ALL existing material rows have <see cref="PrepressCheckStatus.Ok"/>.
///   3. The plate check exists and is <see cref="PrepressCheckStatus.Ok"/>.
///   4. The cutter check exists and is <see cref="PrepressCheckStatus.Ok"/>.
///
/// When ALL three surfaces (materials + plate + cutter) are present and
/// all OK, the helper returns true → caller flips <c>WorkOrder.MaterialsReady</c>.
/// Otherwise returns false → caller leaves the bool as-is (legacy
/// pre-7b WOs with no rows keep their pre-existing bool value;
/// post-7b WOs with at least one row track the rollup state).
///
/// The fourth return value (<see cref="HasSnapshot"/>) lets the caller
/// distinguish "no snapshot yet → defer to legacy bool" from "snapshot
/// exists but not all rows OK → MaterialsReady = false".
/// </summary>
public static class MaterialsReadinessRollup
{
    /// <summary>
    /// Compute the rollup. Caller passes the loaded child rows (or null
    /// where the entity is missing).
    /// </summary>
    /// <param name="materials">All wo_materials rows for the WO (empty if no snapshot).</param>
    /// <param name="plate">wo_plate_check row for the WO (null if no snapshot).</param>
    /// <param name="cutter">wo_cutter_check row for the WO (null if no snapshot).</param>
    /// <returns>
    /// <c>HasSnapshot</c> = true iff at least one of (materials, plate,
    /// cutter) is present — caller should treat the rollup as authoritative.
    /// <c>AllOk</c> = the boolean to write to <c>WorkOrder.MaterialsReady</c>
    /// when <c>HasSnapshot</c>; meaningless when <c>HasSnapshot</c> is false.
    /// </returns>
    /// <summary>
    /// Một dòng vật tư đã Ok có được TÍNH là sẵn sàng không, khi soi lại lô.
    ///
    /// <para><b>Vì sao phải soi LẠI ở đây</b>, dù lúc xác nhận Ok đã có luật
    /// đòi lô Released: (a) dòng được đặt Ok TRƯỚC khi luật ấy tồn tại vẫn còn
    /// nguyên trong DB; (b) lô có thể bị IQC đánh Rejected SAU khi Pre-press đã
    /// gắn — gắn một lần là ảnh chụp, không phải sự thật liên tục. Rollup chạy
    /// mỗi lần ghi nên nó là chỗ rẻ nhất để phát hiện cả hai.</para>
    ///
    /// <para><b>Special Accept được TÍNH LÀ ĐẠT.</b> Nó chính là đường xả đã có
    /// chữ ký: role-gate Engineer/Supervisor, bắt ghi lý do, có audit. Chặn lại
    /// lần nữa ở rollup là double-gate và làm Special Accept thành vô dụng.
    /// Dấu nhận biết: Status=Ok mà NgReasonCode khác null — đường Ok thường
    /// LUÔN xoá NgReasonCode về null, nên cặp này chỉ có thể do Special Accept
    /// sinh ra.</para>
    ///
    /// <para><paramref name="lotStatus"/> null nghĩa là dòng chưa gắn lô nào.</para>
    /// </summary>
    public static bool IsLineReady(WoMaterial m, string? lotStatus)
    {
        if (m.Status != PrepressCheckStatus.Ok) return false;
        if (!string.IsNullOrWhiteSpace(m.NgReasonCode)) return true;   // đã Special Accept
        return string.Equals(lotStatus, nameof(MaterialLotStatus.Released),
                             StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bản có CỔNG LÔ. <paramref name="lotStatusOf"/> trả trạng thái lô đang
    /// gắn của một dòng (null khi chưa gắn) — caller phải tự giải, vì
    /// <c>MaterialLotId</c> là shadow property nên helper thuần không đọc được.
    ///
    /// <para>Quá tải 3 tham số ở dưới giữ NGUYÊN hợp đồng cũ (chỉ soi Status) —
    /// 5 test LegacyParity khoá nó, và WO trước 7b không có dữ liệu lô để soi.</para>
    /// </summary>
    public static (bool HasSnapshot, bool AllOk) Compute(
        IReadOnlyCollection<WoMaterial>? materials,
        WoPlateCheck? plate,
        WoCutterCheck? cutter,
        Func<WoMaterial, string?> lotStatusOf)
        => ComputeCore(materials, plate, cutter, lotStatusOf);

    public static (bool HasSnapshot, bool AllOk) Compute(
        IReadOnlyCollection<WoMaterial>? materials,
        WoPlateCheck? plate,
        WoCutterCheck? cutter)
        => ComputeCore(materials, plate, cutter, lotStatusOf: null);

    private static (bool HasSnapshot, bool AllOk) ComputeCore(
        IReadOnlyCollection<WoMaterial>? materials,
        WoPlateCheck? plate,
        WoCutterCheck? cutter,
        Func<WoMaterial, string?>? lotStatusOf)
    {
        var hasMaterials = materials is { Count: > 0 };
        var hasPlate = plate is not null;
        var hasCutter = cutter is not null;
        var hasSnapshot = hasMaterials || hasPlate || hasCutter;

        if (!hasSnapshot) return (HasSnapshot: false, AllOk: false);

        var materialsOk = hasMaterials && (lotStatusOf is null
            ? materials!.All(m => m.Status == PrepressCheckStatus.Ok)
            : materials!.All(m => IsLineReady(m, lotStatusOf(m))));
        var plateOk = hasPlate && plate!.Status == PrepressCheckStatus.Ok;
        var cutterOk = hasCutter && cutter!.Status == PrepressCheckStatus.Ok;

        var allOk = materialsOk && plateOk && cutterOk;
        return (HasSnapshot: true, AllOk: allOk);
    }
}
