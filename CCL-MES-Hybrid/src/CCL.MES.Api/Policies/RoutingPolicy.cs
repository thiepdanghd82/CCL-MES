using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Kết quả parse pha đích của một leg (advance). <see cref="ErrorCode"/> khác
/// null nghĩa là body không hợp lệ — controller trả 422. Khi hợp lệ,
/// <see cref="Phase"/> mang pha đã parse.
/// </summary>
public sealed record LegPhaseParse
{
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public LegPhase Phase { get; init; }

    public bool IsValid => ErrorCode is null;

    public static LegPhaseParse Ok(LegPhase phase) => new() { Phase = phase };
    public static LegPhaseParse Fail(string code, string message) =>
        new() { ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// Luật thuần của bề mặt Routing DAG — tách khỏi <c>RoutingController</c> theo mẫu
/// các policy A2 khác (L47). Phần LỚN luật routing (gate HARD/SOFT, DAG validate,
/// resolve op→leg, stock-satisfaction) ĐÃ nằm ở Domain (RoutingLegGate /
/// RoutingDagValidator / RoutingLegResolver / SemiStockAllocator) từ P11 — nên ở
/// đây chỉ còn 3 mẩu thuần mà controller tự giữ: parse ToPhase, kiểm reason
/// rework, và tính soft/hard cho picker.
///
/// <para><b>Thuần — không I/O.</b> Chỉ tính trên body + đồ thị leg đã nạp sẵn.
/// Concurrency (If-Match), tra DB, emit audit vẫn ở controller.</para>
///
/// <para><b>Byte-identical.</b> Mã lỗi + message giữ nguyên hệt bản inline
/// (test wire/integration cũ không sửa mà vẫn xanh là bằng chứng).</para>
///
/// <para><b>Ghi chú phạm vi.</b> Nơi đúng là Domain, nhưng
/// <c>src/CCL.MES.Domain</c> baseline read-only tới cutover — đặt tạm ở
/// <c>Api/Policies/</c> cạnh các policy A2 khác.</para>
/// </summary>
public static class RoutingPolicy
{
    public const string InvalidPhase  = "leg.invalid_phase";
    public const string InvalidReason = "leg.invalid_reason";

    /// <summary>
    /// Parse ToPhase của /advance: null/blank/unparseable → invalid_phase.
    /// Bọc <see cref="RoutingLegGate.TryParse"/> + kiểm rỗng, byte-identical
    /// với check inline cũ ("ToPhase không hợp lệ.").
    /// </summary>
    public static LegPhaseParse ParseAdvanceToPhase(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !RoutingLegGate.TryParse(raw, out var phase))
            return LegPhaseParse.Fail(InvalidPhase, "ToPhase không hợp lệ.");
        return LegPhaseParse.Ok(phase);
    }

    /// <summary>
    /// Kiểm reason của /rework: bắt buộc 1–500 ký tự. Ca body null giữ inline ở
    /// controller (cùng mã/message). Trả (ErrorCode, Message) khi vi phạm.
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateReworkReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason!.Length > 500)
            return (InvalidReason, "Reason bắt buộc (1-500 ký tự).");
        return null;
    }

    /// <summary>
    /// Trạng thái phụ thuộc SOFT/HARD của một leg cho GET picker.
    /// <c>hard</c> = còn predecessor HARD chưa LEG_DONE (hoặc chưa đủ RequiredQty)
    /// ⇒ leg chưa được vào RUNNING; <c>soft</c> = còn predecessor SOFT chưa done
    /// (advisory). Leg đã ở RUNNING/LEG_DONE ⇒ (false,false). Thuần trên đồ thị
    /// leg+edge đã nạp — không I/O.
    /// </summary>
    public static (bool Soft, bool Hard) DependencyStatus(WorkOrder wo, WoLeg leg)
    {
        if (leg.LegPhase == nameof(LegPhase.RUNNING) || leg.LegPhase == nameof(LegPhase.LEG_DONE))
            return (false, false);
        bool soft = false, hard = false;
        foreach (var e in wo.LegEdges.Where(e => e.LegId == leg.Id))
        {
            var pred = wo.Legs.FirstOrDefault(l => l.Id == e.DependsOnLegId);
            if (pred is null) continue;
            var done = pred.LegPhase == nameof(LegPhase.LEG_DONE)
                       && (e.RequiredQty <= 0 || pred.QtyDoneCached >= e.RequiredQty);
            if (done) continue;
            if (string.Equals(e.DependencyGate, nameof(DependencyGate.HARD), StringComparison.OrdinalIgnoreCase)) hard = true;
            else soft = true;
        }
        return (soft, hard);
    }
}
