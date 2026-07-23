namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P11-1 — sub-state-machine của MỘT <c>WoLeg</c> trong routing DAG.
/// Là subset production của <see cref="MesPhase"/> (PREPRESS..RUNNING)
/// cộng terminal <see cref="LEG_DONE"/>. Cố tình TÁI DÙNG đúng tên token
/// của MesPhase để leg-level transition dùng lại
/// <see cref="WorkOrderStateMachine.ClassifyTransition"/> cho subset này
/// — KHÔNG viết matrix riêng.
///
/// WO-level phase khi có ≥2 leg = <see cref="MesPhase.SPLIT"/>; từng leg
/// chạy độc lập qua các giá trị dưới đây. Reject 1 leg (IPQC/NG) → chỉ
/// leg đó về <see cref="PREPRESS"/> (Q10), WO giữ ở SPLIT tới khi re-pass.
/// Lưu DB dạng string (HasConversion + MaxLength(16)).
/// </summary>
public enum LegPhase
{
    /// <summary>Leg vừa được tạo; row-level Materials/Plate/Cutter đang điền.</summary>
    PREPRESS = 0,

    /// <summary>Setup máy của leg; setting timer chạy.</summary>
    SETTING = 1,

    /// <summary>Setup xong; IPQC inspector của khu vực chưa ký.</summary>
    IPQC_WAIT = 2,

    /// <summary>IPQC SPECIAL_ACCEPT; QA approval của leg pending.</summary>
    QA_PENDING = 3,

    /// <summary>IPQC GO_RUN (hoặc QA-approved special accept) cho leg.</summary>
    IPQC_APPROVED = 4,

    /// <summary>Leg đang sản xuất; qty ledger accruing.</summary>
    RUNNING = 5,

    /// <summary>Leg tạm dừng; downtime reason captured.</summary>
    PAUSED = 6,

    /// <summary>Terminal của leg — công đoạn của leg hoàn tất. Khi MỌI
    /// leg terminal (không có successor trong DAG) đạt LEG_DONE, WO
    /// join <c>SPLIT → FQC_PENDING</c>.</summary>
    LEG_DONE = 7,
}
