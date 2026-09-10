using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Tests.Integration._Support;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Tạo WO PHẢI để lại dấu vết.
///
/// <para>Đo trên DB live 2026-09-10: cả bảng <c>AuditLogs</c> (3.364 dòng) chỉ
/// có <b>1</b> dòng <c>WO_CREATE</c>, đến từ một đường khác — <see
/// cref="WorkOrderService.CreateAsync"/> chưa bao giờ emit. Hệ quả không phải
/// lý thuyết: hôm ấy phải điều tra 27 WO mang trạng thái mà app không tạo ra
/// nổi, và KHÔNG truy được chúng từ đâu ra, vì ngay cả một WO tạo hợp lệ cũng
/// chẳng để lại vết. Thiếu dòng audit thì "không tìm thấy bằng chứng" và
/// "không có chuyện gì xảy ra" trông y hệt nhau.</para>
/// </summary>
public class WorkOrderCreateAuditTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;
    public WorkOrderCreateAuditTests(IsolatedDbFixture fx) => _fx = fx;

    private CreateWoRequest Req(string woNo) => new()
    {
        WoNo = woNo,
        CustomerId = _fx.SeedCustomerId,
        ProductId = _fx.SeedProductId,
        ProductName = _fx.SeedProductCode,
        ProductRevisionId = _fx.SeedRevisionId,
        MachineCode = "ACNC3",
        TargetQty = 1000,
        Uom = "pcs",
    };

    [Fact]
    public async Task Tao_WO_thi_ghi_mot_dong_WO_CREATE_mang_ten_nguoi_tao()
    {
        await using var db = _fx.NewContext();
        var audit = new InMemoryAuditWriter();

        var wo = await new WorkOrderService(db, audit).CreateAsync(Req("WO-AUD-01"), "engineer");

        var row = Assert.Single(audit.ByAction(AuditAction.WoCreate));   // ← đỏ nếu bỏ emit
        Assert.Equal("engineer", row.Actor);
        Assert.Equal("WorkOrder", row.TargetType);
        Assert.Equal(wo.Id.ToString(), row.TargetId);
    }

    [Fact]
    public async Task Detail_mang_du_thu_can_de_TRUY_lai_ve_sau()
    {
        // Bốn trường này là thứ mà cuộc điều tra 2026-09-10 đã đi tìm và không
        // có: WO nào, mã hàng nào, sinh ra ở phase nào, và có bao nhiêu dòng
        // BOM thật sự được materialise.
        await using var db = _fx.NewContext();
        var audit = new InMemoryAuditWriter();

        await new WorkOrderService(db, audit).CreateAsync(Req("WO-AUD-02"), "engineer");

        var detail = Assert.Single(audit.ByAction(AuditAction.WoCreate)).Detail;
        Assert.False(string.IsNullOrWhiteSpace(detail));
        using var doc = JsonDocument.Parse(detail!);
        var root = doc.RootElement;
        Assert.Equal("WO-AUD-02", root.GetProperty("wo_no").GetString());
        Assert.Equal(_fx.SeedProductId, root.GetProperty("product_id").GetInt64());
        Assert.True(root.TryGetProperty("mes_phase", out _));
        Assert.True(root.TryGetProperty("bom_lines", out _));
        // Cờ sẵn-sàng phải được ghi lại NGAY lúc sinh: WO mới không bao giờ
        // được đẻ ra đã "sẵn sàng" — đúng thứ 42 dòng lệch đã vi phạm.
        Assert.False(root.GetProperty("materials_ready").GetBoolean());
    }

    [Fact]
    public async Task Khong_truyen_nguoi_tao_thi_ghi_anonymous_chu_khong_boi_dong_audit()
    {
        // Đường gọi duy nhất còn lại nằm trong app legacy ĐÃ ĐÓNG BĂNG và không
        // truyền actor. Thà ghi "anonymous" còn hơn không ghi gì — vẫn biết WO
        // ấy do app tạo, khác hẳn một dòng chèn thẳng bằng SQL.
        await using var db = _fx.NewContext();
        var audit = new InMemoryAuditWriter();

        await new WorkOrderService(db, audit).CreateAsync(Req("WO-AUD-03"));

        Assert.Equal("anonymous", Assert.Single(audit.ByAction(AuditAction.WoCreate)).Actor);
    }
}
