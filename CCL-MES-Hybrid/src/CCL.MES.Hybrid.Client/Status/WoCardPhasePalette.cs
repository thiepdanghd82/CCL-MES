namespace CCL.MES.Hybrid.Client.Status;

/// <summary>
/// D3/D4 — bảng màu chip phase trên WO card (WorkOrders.razor). Đây là palette
/// GIÀU 13 màu per-phase (khác với <see cref="PhaseVisual"/> 5-tone semantic dùng
/// cho pill dashboard — hai hệ cố tình khác nhau, L19). Tách khỏi .razor để:
///   1. WO card chip không còn ôm switch 14 nhánh (de-bloat).
///   2. Unit-test được phủ đủ mọi MesPhase — chống drift "thêm phase mà quên màu"
///      (khe hở đã tồn tại: SPLIT từng rơi về <c>wo-phase-other</c> generic).
/// Pure, no I/O. KHÔNG đổi màu bất kỳ phase nào đang có.
/// </summary>
public static class WoCardPhasePalette
{
    public static string CssClass(string? mesPhase, string? badgeFallback = null)
    {
        if (string.IsNullOrEmpty(mesPhase)) return badgeFallback ?? "";
        return mesPhase.ToUpperInvariant() switch
        {
            "NEW"            => "wo-phase-new",
            "PREPRESS"       => "wo-phase-prepress",
            // SPLIT = umbrella đa-leg; domain ProjectToLegacy(SPLIT)=PrePressCheck
            // nên dùng chung tông prepress (theo tiền lệ QA_PENDING↦ipqc-wait).
            "SPLIT"          => "wo-phase-prepress",
            "SETTING"        => "wo-phase-setting",
            "IPQC_WAIT"      => "wo-phase-ipqc-wait",
            "QA_PENDING"     => "wo-phase-ipqc-wait",
            "IPQC_APPROVED"  => "wo-phase-ipqc-approved",
            "RUNNING"        => "wo-phase-running",
            "PAUSED"         => "wo-phase-paused",
            "FQC_PENDING"    => "wo-phase-fqc-pending",
            "OQC_PENDING"    => "wo-phase-oqc-pending",
            "DONE"           => "wo-phase-done",
            "SHIPPED"        => "wo-phase-shipped",
            "CANCELLED"      => "wo-phase-cancelled",
            _                => "wo-phase-other",
        };
    }
}
