namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11-1 — nguồn input semi cho một leg (đặc biệt ASSEMBLY). Tổng quát
/// hoá gate "đủ input" cho cả 2 kịch bản:
/// <list type="bullet">
///   <item><see cref="IN_LINE"/> — semi chạy trong CÙNG WO (sibling leg).
///     GATE = sibling leg <c>LEG_DONE</c> + đủ qty. <b>Chỉ mode này hoạt
///     động ở P11-1</b>.</item>
///   <item><see cref="FROM_STOCK"/> — semi xuất từ kho bán thành phẩm
///     (make-to-stock, keep stock). Kích hoạt ở P11.5 (SemiLot +
///     allocation). Khai báo sẵn để schema/luật ổn định.</item>
///   <item><see cref="MIXED"/> — một phần in-line + một phần kho. P11.5.</item>
/// </list>
/// </summary>
public enum InputSource
{
    IN_LINE,
    FROM_STOCK,
    MIXED,
}

/// <summary>
/// P11-1 — độ cứng của một cạnh phụ thuộc trong routing DAG (Q4 đã chốt):
/// <list type="bullet">
///   <item><see cref="SOFT"/> — cảnh báo, KHÔNG chặn (PRINT ∥ TAPE chạy
///     song song; CUT-after-PRINT). Giữ throughput.</item>
///   <item><see cref="HARD"/> — chặn thật (ASSEMBLY: không dán được khi
///     semi chưa done / thiếu qty). Kèm điều kiện qty ở
///     <c>WoLegDependency.RequiredQty</c>.</item>
/// </list>
/// </summary>
public enum DependencyGate
{
    SOFT,
    HARD,
}

/// <summary>
/// P11-1 — bộ surface một leg phải đi qua (Q5):
/// <list type="bullet">
///   <item><see cref="FULL"/> — PREPRESS → SETTING → IPQC → RUNNING
///     (PRINT/CUT/PRINT_CUT).</item>
///   <item><see cref="LITE"/> — RUNNING/qty (+ IPQC optional, không
///     setting timer) cho TAPE/ASSEMBLY — tránh bắt công nhân dán tape
///     qua setting/IPQC vô nghĩa; vẫn ghi qty + NG.</item>
/// </list>
/// </summary>
public enum SurfaceProfile
{
    FULL,
    LITE,
}
