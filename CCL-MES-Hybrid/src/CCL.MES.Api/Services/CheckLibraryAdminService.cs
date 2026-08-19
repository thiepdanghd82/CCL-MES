using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// Ghi cho master data thư viện check-item.
///
/// <para><b>Vì sao tồn tại thay vì viết thẳng trong controller:</b> gate
/// <c>gate-thin-controller.sh</c> (L40) bắt được ngay khi thêm
/// <c>SaveChangesAsync</c> thứ 23 vào <c>CheckItemLibraryController</c>. Luật là
/// controller chỉ bind / authorize / gọi / map lỗi — transaction thuộc về tầng
/// dưới. Nơi ĐÚNG là <c>CCL.MES.Application</c>, nhưng thư mục đó là baseline
/// read-only cho tới khi cutover xong, nên tạm đặt ở đây và ghi nợ vị trí.</para>
/// </summary>
public interface ICheckLibraryAdminService
{
    /// <summary>
    /// Bật/tắt một hạng mục mà KHÔNG xoá. Trả về <c>null</c> nếu không tìm thấy.
    /// Idempotent: đặt lại đúng giá trị đang có thì không ghi, không emit audit.
    /// </summary>
    Task<CheckItemLibraryActiveResult?> SetActiveAsync(
        string itemId, bool active, string actor, string role, CancellationToken ct = default);
}

/// <summary>Kết quả sau khi bật/tắt — <see cref="Changed"/> = false nghĩa là no-op.</summary>
public sealed record CheckItemLibraryActiveResult(Domain.Entities.CheckItemLibrary Item, bool Changed);

public sealed class CheckLibraryAdminService : ICheckLibraryAdminService
{
    private readonly MesDbContext _db;
    private readonly IAuditWriter _audit;

    public CheckLibraryAdminService(MesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<CheckItemLibraryActiveResult?> SetActiveAsync(
        string itemId, bool active, string actor, string role, CancellationToken ct = default)
    {
        var e = await _db.CheckItemLibraries.FirstOrDefaultAsync(x => x.ItemId == itemId, ct);
        if (e is null) return null;
        if (e.Active == active) return new CheckItemLibraryActiveResult(e, Changed: false);

        e.Active = active;
        e.UpdatedAt = DateTime.UtcNow;
        e.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);

        // Dùng lại QC_LIBRARY_ITEM_SET (đổi Active LÀ một dạng set) thay vì thêm
        // mã mới — AuditAction nằm trong baseline read-only.
        await _audit.EmitAsync(AuditAction.QcLibraryItemSet, actor, role,
            "CheckItemLibrary", itemId,
            $"{{\"active\":{(active ? "true" : "false")},\"via\":\"patch\"}}");

        return new CheckItemLibraryActiveResult(e, Changed: true);
    }
}
