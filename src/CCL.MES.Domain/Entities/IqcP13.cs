using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P13 — nhóm vật liệu quyết định BỘ hạng mục nào áp cho phiếu. Bốn nhóm này
/// là bốn sheet ghi chép riêng của file master IQC 2026, và hạng mục của chúng
/// khác nhau hẳn (Roll 13 ô đếm lỗi, Chem chỉ 3 và là khẳng định đóng gói).
/// </summary>
public enum IqcMaterialCategory
{
    /// <summary>Áp cho MỌI nhóm (tem nhãn, hồ sơ HSF…).</summary>
    Any = 0,
    /// <summary>Cuộn / băng dính — 3.711 bản ghi 2026, nhóm lớn nhất.</summary>
    Roll = 1,
    /// <summary>Tấm / miếng rời.</summary>
    Pcs = 2,
    /// <summary>Hoá chất — mực, keo, dung môi.</summary>
    Chem = 3,
    /// <summary>Dụng cụ — dao chặt, bản kẽm.</summary>
    Tool = 4,
}

/// <summary>
/// P13 — hạng mục được GHI NHẬN kiểu gì. Đây là thứ quyết định ô nhập nào hiện
/// ra và luật chấm nào chạy, nên nó thuộc về THƯ VIỆN chứ không phải UI.
/// </summary>
public enum IqcCheckKind
{
    /// <summary>Đạt / không đạt do người bấm. Mặc định của 21 hạng mục cũ.</summary>
    Verdict = 0,

    /// <summary>ĐẾM SỐ LỖI (Nhăn, Xước, Bavia…). Ô nhập là số nguyên ≥ 0.
    /// Luật chấm: Ac = 0 — bất kỳ số nào &gt; 0 là trượt.</summary>
    DefectCount = 1,

    /// <summary>ĐO nhiều lần (độ rộng ×5, độ dày ×5). Số lần lấy ở
    /// <see cref="IqcCheckItemLibrary.MeasureCount"/>. Chấm theo ngưỡng số của
    /// spec; không có ngưỡng thì người chấm.</summary>
    Measure = 2,

    /// <summary>Hồ sơ giấy (HSF, COA, RoHS, PEFC-FSC). Không đo, không đếm —
    /// chỉ xác nhận có/không và còn hạn.</summary>
    Document = 3,
}

/// <summary>
/// P13 — MỘT phép đo trong một hạng mục kiểu <see cref="IqcCheckKind.Measure"/>.
///
/// <para><b>Vì sao là BẢNG CON chứ không phải JSON trong một ô.</b> File master
/// ghi 5 phép đo cho mỗi hạng mục kích thước. Nhét chúng vào một chuỗi JSON thì
/// (a) không truy vấn được "cuộn nào có phép đo lệch nhất tháng này", (b) không
/// dựng được biểu đồ SPC sau này mà không phải bung JSON của từng dòng, và
/// (c) không có chỗ nào để ghi rằng phép đo thứ 3 là cái làm trượt cả hạng mục.
/// Bảng con giải quyết cả ba, và đúng khuôn <c>WoQcCheckItem</c> đang chạy.</para>
///
/// <para>Không có navigation property — repo này dùng FK trần ở mọi bảng con
/// IQC/QC (xem <c>WoIpqcCheckItem</c>, <c>IqcResultDetail</c>).</para>
/// </summary>
public class IqcResultMeasurement : BaseEntity
{
    /// <summary>FK tới dòng kết quả (<c>IqcResultDetail.Id</c>).</summary>
    public long IqcResultDetailId { get; set; }

    /// <summary>Thứ tự phép đo trong hạng mục, 1-based. Unique cùng
    /// <see cref="IqcResultDetailId"/> — hai lần đo cùng số thứ tự là dữ liệu
    /// hỏng, không phải hai lần đo.</summary>
    public int Seq { get; set; } = 1;

    /// <summary><c>null</c> = CHƯA ĐO. Khác hẳn 0 (đo được kết quả 0). Ép chưa-đo
    /// về 0 là đúng bài học L67 — bản ghi bằng chứng thiếu một chiều thông tin
    /// thì nó nói dối im lặng.</summary>
    public double? Value { get; set; }
}

/// <summary>
/// P13 — trạng thái duyệt của một bộ tiêu chuẩn nhập từ file ngoài.
///
/// <para>Henry chốt 2026-09-04: import cả 1.028 mã từ file master nhưng gắn cờ
/// chờ duyệt; phiếu dùng mã chưa duyệt hiện băng nhắc, KHÔNG chặn sản xuất.
/// Chặn thì QC sẽ bỏ qua app và quay lại Excel — mất luôn cả dấu vết.</para>
/// </summary>
public enum IqcSpecApproval
{
    /// <summary>Nhập từ file ngoài, chưa ai trong QC xác nhận.</summary>
    PendingQc = 0,
    /// <summary>QC đã đối chiếu và ký.</summary>
    Approved = 1,
    /// <summary>QC xem rồi và bác — giữ lại để không import lại vòng sau.</summary>
    Rejected = 2,
}
