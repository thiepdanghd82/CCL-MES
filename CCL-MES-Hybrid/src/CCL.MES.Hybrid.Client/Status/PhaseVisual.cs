namespace CCL.MES.Hybrid.Client.Status;

/// <summary>
/// Năm bậc trạng thái của CCL iX. MỌI trạng thái trong app quy về đúng năm bậc
/// này — không thêm bậc thứ sáu "cho trường hợp đặc biệt", vì mỗi bậc thêm vào
/// là một màu người vận hành phải học thuộc.
/// </summary>
public enum PhaseTone
{
    /// <summary>Chưa bắt đầu / không có gì đang xảy ra.</summary>
    Neutral,
    /// <summary>Đang tiến triển bình thường, chưa cần ai làm gì.</summary>
    Info,
    /// <summary>Tốt — đang chạy, đã duyệt, đã xong.</summary>
    Ok,
    /// <summary>Đang CHỜ một người cụ thể ra tay.</summary>
    Warn,
    /// <summary>Cần quyết định / đã hỏng — không tự trôi được.</summary>
    Alarm,
}

/// <summary>
/// Nguồn sự thật DUY NHẤT cho việc "trạng thái này hiện màu gì, chữ gì".
///
/// <para><b>Vì sao tồn tại.</b> Trước đây map phase → màu nằm rải rác ≥4 kiểu:
/// class cứng <c>rs-phase-fqc</c> nhúng thẳng vào 6 dashboard, một
/// <c>switch</c> riêng trong <c>RunningDashboard</c>, một <c>switch</c> khác
/// trong <c>LegsDashboard</c>, và một inline style nhét hex thô
/// (<c>#dc2626</c>) trong <c>ShopOrderHistory.razor</c>. Hệ quả: cùng một
/// <c>PAUSED</c> có thể ra hai màu khác nhau ở hai màn hình, và người đứng máy
/// phải học lại bảng màu mỗi khi đổi trang.</para>
///
/// <para><b>Thuần, không phụ thuộc UI</b> ⇒ unit-test được không cần render.
/// Nhận chuỗi (không nhận enum) vì DTO truyền phase dạng string qua wire.</para>
///
/// <para>Dùng chung cho CẢ <c>MesPhase</c> (tầng WO) và <c>LegPhase</c> (tầng
/// leg): các token trùng tên mang đúng cùng ý nghĩa vận hành, nên gộp một bảng
/// là đúng chứ không phải tiện tay.</para>
/// </summary>
public static class PhaseVisual
{
    public static PhaseTone Tone(string? phase) => Normalise(phase) switch
    {
        // ── chưa khởi động ───────────────────────────────────────────────
        "NEW"            => PhaseTone.Neutral,

        // ── đang trôi bình thường, không chờ ai ──────────────────────────
        "PREPRESS"       => PhaseTone.Info,
        "SPLIT"          => PhaseTone.Info,
        "SETTING"        => PhaseTone.Info,

        // ── ĐANG CHỜ một người cụ thể ────────────────────────────────────
        "IPQC_WAIT"      => PhaseTone.Warn,   // chờ QC vào ký
        "FQC_PENDING"    => PhaseTone.Warn,
        "OQC_PENDING"    => PhaseTone.Warn,
        "PAUSED"         => PhaseTone.Warn,   // máy dừng — có người phải quay lại

        // ── cần QUYẾT ĐỊNH, không tự trôi ────────────────────────────────
        "QA_PENDING"     => PhaseTone.Alarm,  // special-accept chờ QA Manager
        "CANCELLED"      => PhaseTone.Alarm,

        // ── tốt ──────────────────────────────────────────────────────────
        "IPQC_APPROVED"  => PhaseTone.Ok,
        "RUNNING"        => PhaseTone.Ok,
        "DONE"           => PhaseTone.Ok,
        "LEG_DONE"       => PhaseTone.Ok,
        "SHIPPED"        => PhaseTone.Ok,

        // Phase lạ (contract mở rộng mà quên cập nhật đây) hiện Neutral —
        // KHÔNG đoán màu. Hiện sai màu tệ hơn hiện không màu.
        _                => PhaseTone.Neutral,
    };

    /// <summary>Class CSS của pill — luôn đi qua bộ <c>.ix-pill*</c> trong ix.css.</summary>
    public static string CssClass(string? phase) => Tone(phase) switch
    {
        PhaseTone.Info  => "ix-pill ix-pill-info",
        PhaseTone.Ok    => "ix-pill ix-pill-ok",
        PhaseTone.Warn  => "ix-pill ix-pill-warn",
        PhaseTone.Alarm => "ix-pill ix-pill-alarm",
        _               => "ix-pill",
    };

    /// <summary>
    /// Key i18n của nhãn, hoặc <c>null</c> nếu chưa có bản dịch — khi null,
    /// <c>StatusPill</c> hiển thị nguyên token. CỐ Ý giữ vậy: bịa nhãn cho 14
    /// phase WO là quyết định về từ ngữ vận hành, phải do người vận hành chốt,
    /// không phải do component tự quyết. Xem IMPROVEMENT-BACKLOG.
    /// </summary>
    public static string? LabelKey(string? phase)
    {
        var p = Normalise(phase);
        return p switch
        {
            "PREPRESS" or "SETTING" or "IPQC_WAIT" or "IPQC_APPROVED"
                or "RUNNING" or "PAUSED" or "LEG_DONE" => "legs.phase." + p.ToLowerInvariant(),
            _ => null,
        };
    }

    private static string Normalise(string? phase) => (phase ?? "").Trim().ToUpperInvariant();
}
