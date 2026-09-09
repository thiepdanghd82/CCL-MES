using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Bấm TÊN nhà cung cấp trên Dashboard = "cho tôi xem tất cả lô NG của NCC này".
///
/// <para>Bộ lọc phải nằm ở SERVER. Bảng phiếu IQC thật có hơn 5.000 dòng và
/// list chia trang 20, nên lọc phía client chỉ lọc được TRANG ĐANG XEM: câu
/// hỏi "tất cả lô NG của NCC X" sẽ trả thiếu mà không ai biết. Ba thứ được
/// khoá ở đây:</para>
/// <list type="number">
///   <item><c>result</c> lọc đúng Pending/Pass/Fail;</item>
///   <item>search theo tên NCC + <c>result=Fail</c> giao nhau đúng — không phải
///         hợp hai tập;</item>
///   <item>giá trị <c>result</c> lạ bị BỎ QUA (= tất cả), không throw, vì nó
///         đến từ query string.</item>
/// </list>
/// </summary>
public sealed class IqcTicketListFilterTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcTicketListFilterTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private static IqcService Svc(MesDbContext db)
    {
        var audit = new InMemoryAuditWriter();
        var lots = new MaterialLotScanService(
            db, audit, Microsoft.Extensions.Options.Options.Create(new MaterialLotOptions()));
        return new IqcService(db, audit, lots);
    }

    private static IqcInspection Ticket(string supplier, QcResult result, string lot) => new()
    {
        PartNo = "30030146", ReceiptNo = $"XLS-{lot}", LotNumber = lot, BatchNumber = lot,
        SupplierName = supplier, ReceivedDate = new DateTime(2026, 9, 5),
        Quantity = 1, UomQty = "rolls", MaterialCategory = IqcMaterialCategory.Roll,
        Group = "Materials", Result = result,
    };

    private async Task SeedAsync()
    {
        using var db = _fx.NewContext();
        db.IqcInspections.AddRange(
            Ticket("Vietnam Paper Tube Co.", QcResult.Fail, "VPT-NG-1"),
            Ticket("Vietnam Paper Tube Co.", QcResult.Fail, "VPT-NG-2"),
            Ticket("Vietnam Paper Tube Co.", QcResult.Pass, "VPT-OK-1"),
            Ticket("Vietnam Paper Tube Co.", QcResult.Pending, "VPT-WAIT-1"),
            Ticket("Kingfisher Inks & Coatings LTD", QcResult.Fail, "KF-NG-1"),
            Ticket("Kingfisher Inks & Coatings LTD", QcResult.Pass, "KF-OK-1"));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Loc_result_Fail_chi_tra_phieu_NG()
    {
        await SeedAsync();
        using var db = _fx.NewContext();
        var page = await Svc(db).ListTicketsAsync(null, null, "Fail", 1, 50);
        Assert.Equal(3, page.Total);
        Assert.All(page.Items, x => Assert.Equal("Fail", x.Result));
    }

    [Fact]
    public async Task Ten_NCC_cong_result_Fail_giao_nhau_dung()
    {
        await SeedAsync();
        using var db = _fx.NewContext();
        var page = await Svc(db).ListTicketsAsync(null, "Vietnam Paper Tube", "Fail", 1, 50);

        // ĐÚNG 2 lô: NG của riêng NCC đó. Không phải 3 (tất cả NG) và không
        // phải 4 (tất cả phiếu của NCC đó) — hai tập phải GIAO, không HỢP.
        Assert.Equal(2, page.Total);
        Assert.All(page.Items, x =>
        {
            Assert.Equal("Fail", x.Result);
            Assert.Contains("Vietnam Paper Tube", x.SupplierName);
        });
        Assert.Equal(
            new[] { "VPT-NG-1", "VPT-NG-2" },
            page.Items.Select(x => x.LotBatchNo).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Result_null_va_result_la_deu_tra_TAT_CA()
    {
        await SeedAsync();
        using var db = _fx.NewContext();
        var svc = Svc(db);
        Assert.Equal(6, (await svc.ListTicketsAsync(null, null, null, 1, 50)).Total);
        // Query string do UI gửi — giá trị lạ KHÔNG được làm sập request.
        Assert.Equal(6, (await svc.ListTicketsAsync(null, null, "banana", 1, 50)).Total);
        Assert.Equal(6, (await svc.ListTicketsAsync(null, null, "", 1, 50)).Total);
    }

    [Fact]
    public async Task Loc_result_khong_pha_phan_trang()
    {
        await SeedAsync();
        using var db = _fx.NewContext();
        var p1 = await Svc(db).ListTicketsAsync(null, null, "Fail", 1, 2);
        Assert.Equal(3, p1.Total);          // tổng là tổng ĐÃ LỌC
        Assert.Equal(2, p1.Items.Count);    // trang vẫn đúng cỡ
    }

    [Fact]
    public async Task Result_khong_phan_biet_hoa_thuong()
    {
        await SeedAsync();
        using var db = _fx.NewContext();
        Assert.Equal(3, (await Svc(db).ListTicketsAsync(null, null, "fail", 1, 50)).Total);
    }
}
