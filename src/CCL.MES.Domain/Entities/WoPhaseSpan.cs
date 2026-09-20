using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// Một dòng cho MỖI LẦN một WO ở trong một công đoạn — đóng khoảng trống
/// thời gian của PREPRESS · IPQC · FQC · OQC (hiệu suất + OEE).
///
/// <para><b>Vì sao là BẢNG chứ không phải cột trên WorkOrders.</b> Hệ này có
/// vòng rework thật: IPQC StopLine → PREPRESS, QA Reject → PREPRESS, FQC
/// Reject → PREPRESS, OQC Reject → FQC_PENDING. Một WO vào cùng một công
/// đoạn NHIỀU LẦN. Tiền lệ SETTING (<c>WorkOrder.SettingStartAt</c>) né
/// chuyện đó bằng cách ghi rõ "set MỘT LẦN, re-entry không reset" — tức là
/// bỏ qua lần vào thứ hai. Copy cách ấy sang đây thì OEE sai đúng ở những WO
/// có rework, mà đó lại là những WO đáng đo nhất. Một dòng mỗi lần vào thì
/// giữ được cả tổng lẫn từng lần, không phải chọn.</para>
///
/// <para><b>Hình dạng cố ý trùng <see cref="WoRunSession"/></b>
/// (StartedAt + EndedAt nullable) để dùng thẳng
/// <c>WoRuntimeMath.ElapsedSeconds</c> — cùng hàm báo cáo OEE đang dùng cho
/// giờ chạy máy. Không đẻ thêm công thức thứ hai.</para>
///
/// <para><b>Phạm vi có chủ ý.</b> Chỉ ghi các công đoạn CHƯA có nguồn thời
/// gian: PREPRESS · IPQC_WAIT · QA_PENDING · FQC_PENDING · OQC_PENDING.
/// SETTING đã có 3 cột trên WorkOrder; RUNNING/PAUSED đã có WoRunSession +
/// WoPauseEvent. Ghi thêm ở đây nữa là dựng nguồn sự thật thứ hai cho cùng
/// một con số — đúng bệnh L63. Danh sách nằm ở
/// <c>WoPhaseSpanPolicy.TrackedPhases</c>, một chỗ, sửa được.</para>
/// </summary>
public class WoPhaseSpan : BaseEntity
{
    public long WoId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>Tên <c>MesPhase</c> CHÍNH TẮC (PREPRESS, IPQC_WAIT…), KHÔNG
    /// phải tên bước legacy <c>CurrentStep</c> (PrePressCheck, OpSetting…).
    /// Đây chính là chỗ audit <c>WO_ADVANCE</c> làm sai và vì thế không dùng
    /// được để suy ra thời gian công đoạn (bệnh phân kỳ L19).</summary>
    [MaxLength(32)] public string Phase { get; set; } = "";

    /// <summary>
    /// Lần thứ mấy WO vào công đoạn này, đếm từ 1. <c>VisitNo &gt; 1</c> nghĩa
    /// là WO ĐÃ BỊ TRẢ VỀ — chỉ số rework, hôm nay không đo được ở đâu cả.
    /// Suy được bằng cách sắp xếp, nhưng lưu thẳng thì đếm được bằng một câu
    /// SQL thay vì một window function, và "WO này quay lại IPQC 3 lần" là
    /// con số người quản lý hỏi trước tiên.
    /// </summary>
    public int VisitNo { get; set; } = 1;

    /// <summary>UTC — lúc WO VÀO công đoạn này.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC — lúc WO RỜI công đoạn. Null = đang ở trong công đoạn
    /// này; người đọc cộng tới "bây giờ", đúng như khoảng còn mở của
    /// <see cref="WoRunSession"/>.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Ai làm động tác khiến WO vào/rời công đoạn. Lấy từ
    /// <c>WorkOrder.UpdatedBy</c> của chính lần ghi đó, nên không cần luồn
    /// actor xuống tầng hạ tầng.</summary>
    [MaxLength(128)] public string StartedBy { get; set; } = "";
    [MaxLength(128)] public string? EndedBy { get; set; }

    // Concurrency: dòng này không mang RowVersion riêng — y như WoRunSession.
    // Mọi lần đổi phase đều chạm wo.MesPhase + wo.UpdatedAt, nên khoá lạc quan
    // của WorkOrder cha đã gác; hai người đua nhau thì SaveChanges ném
    // DbUpdateConcurrencyException trước khi tới đây.
}

/// <summary>
/// Luật thuần cho <see cref="WoPhaseSpan"/> — để tầng nào cũng hỏi được cùng
/// một câu trả lời, và để test khoá được danh sách công đoạn.
/// </summary>
public static class WoPhaseSpanPolicy
{
    /// <summary>
    /// Các công đoạn được ghi mốc. CHỈ những công đoạn chưa có nguồn thời
    /// gian nào khác — xem ghi chú "Phạm vi có chủ ý" ở
    /// <see cref="WoPhaseSpan"/>. Thêm phase vào đây là quyết định có ý thức:
    /// phải chắc nó KHÔNG được đo ở chỗ khác rồi.
    /// </summary>
    public static readonly IReadOnlyList<string> TrackedPhases = new[]
    {
        "PREPRESS",
        "IPQC_WAIT",
        "QA_PENDING",
        "FQC_PENDING",
        "OQC_PENDING",
    };

    public static bool IsTracked(string? phase) =>
        phase is not null && TrackedPhases.Contains(phase, StringComparer.Ordinal);
}
