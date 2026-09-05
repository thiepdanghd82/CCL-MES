namespace CCL.MES.Application.Audit;

/// <summary>
/// Phase 6 Bước 5 — abstract emit-an-audit-row operation. Application
/// services depend on this interface; the Web layer supplies the
/// implementation (<c>CCL.MES.Web.Services.AuditService</c>) so the IP
/// + HttpContext stay out of the Application class lib.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Append one audit row. Caller MUST sanitize <paramref name="detail"/>
    /// — never include password / hash / cookie / token bytes.
    /// </summary>
    /// <param name="action">Code from <c>CCL.MES.Domain.Audit.AuditAction</c>.</param>
    /// <param name="actor">Username; "anonymous" for pre-auth events.</param>
    /// <param name="actorRole">Role snapshot at emit time.</param>
    /// <param name="targetType">Logical target type (User, WorkOrder, …) or null.</param>
    /// <param name="targetId">Target id (long.ToString, filename, …) or null.</param>
    /// <param name="detail">JSON string with sanitized action-specific fields or null.</param>
    /// <param name="source">
    /// Kênh phát sinh sự kiện. <b>Để <c>null</c> (mặc định) thì WRITER tự điền
    /// theo transport của chính nó</b> — Api ghi "Api", Web ghi "Web".
    ///
    /// <para>KHÔNG đặt giá trị mặc định là một chuỗi cụ thể ở đây. C# gắn giá
    /// trị mặc định <b>lúc biên dịch, theo KIỂU TĨNH tại chỗ gọi</b>: mọi
    /// service giữ biến kiểu <c>IAuditWriter</c> sẽ lấy mặc định của
    /// INTERFACE, còn mặc định khai trong lớp hiện thực là code chết. Đó chính
    /// là cách 3.007 dòng audit của API bị đóng dấu "Web" suốt từ 2026-05 —
    /// trong khi app Web legacy đã đóng băng và không ai chạy nó.</para>
    ///
    /// <para>Chỉ truyền tường minh khi nguồn KHÁC transport thật: công cụ dòng
    /// lệnh ("Console"), tác vụ nền ("Scheduler").</para>
    /// </param>
    Task EmitAsync(
        string action,
        string actor,
        string actorRole,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
        string? source = null);
}
