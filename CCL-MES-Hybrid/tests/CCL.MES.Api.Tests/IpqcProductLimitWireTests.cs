using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Ngưỡng số theo SẢN PHẨM phải đi tới HỒ SƠ WO, không dừng ở bảng spec
/// (Thiệp chốt 2026-09-11).
///
/// <para>Đường đi: <c>QcCriterion.LibraryItemKey</c> → hạng mục thư viện →
/// <c>WoIpqcCheckItems.LimitLow/Up/Nominal/Unit</c>. Chỉ lấy từ kế hoạch QC
/// <b>đã duyệt</b>, và chỉ khi stage của kế hoạch hợp công đoạn của hạng mục.</para>
/// </summary>
public sealed class IpqcProductLimitWireTests : IClassFixture<MesApiFactory>
{
    private const string LibKey = "TST-LIM-1";
    private readonly MesApiFactory _fx;
    public IpqcProductLimitWireTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> QcClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    /// <summary>Thư viện một hạng mục LABEL + bản đồ dòng SX.</summary>
    private async Task SeedLibraryAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.CheckItemLibraries.AnyAsync(c => c.ItemId == LibKey))
        {
            db.CheckItemLibraries.Add(new CheckItemLibrary
            {
                ItemId = LibKey, ProcessLine = "LABEL", Ipqc = true, GroupLabel = "B",
                Code = "B1", ItemVi = "Kích thước tổng thể", ItemEn = "Overall size",
                AcceptanceVi = "Trong dung sai bản vẽ", AcceptanceEn = "Within drawing tolerance",
                CheckType = "Measure", Active = true, Sort = 10,
            });
            await db.SaveChangesAsync();
        }
        await DbSeeder.SeedProcessLineMapAsync(db);
    }

    /// <summary>WO ở IPQC_WAIT, CÓ ProductRevisionId (khoá tra kế hoạch QC).</summary>
    private async Task<(long WoId, long RevisionId)> SeedWoAsync(string tag)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(cust); await db.SaveChangesAsync();

        var code = "P-" + Guid.NewGuid().ToString("N")[..8];
        var prod = new Product { ProductCode = code, Name = "Prod", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();

        var rev = new ProductRevision { ProductId = prod.Id, RevisionCode = "A" };
        db.ProductRevisions.Add(rev); await db.SaveChangesAsync();

        db.RoutingOperations.Add(new RoutingOperation
        {
            PartNo = code, OpNo = "20", Operation = "op",
            WorkCenterNo = "GFL01", WorkCenterDescription = "Flexo (Gallus 4C)",
        });

        var wo = new WorkOrder
        {
            WoNo = $"WO-LIM-{tag}-" + Guid.NewGuid().ToString("N")[..5],
            CustomerId = cust.Id, ProductId = prod.Id, ProductName = prod.Name,
            ProductRevisionId = rev.Id,
            TargetQty = 1000, Uom = "pcs",
            CurrentStep = ProcessStepCode.IpqcApproval, MesPhase = "IPQC_WAIT",
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        return (wo.Id, rev.Id);
    }

    private async Task<long> SeedPlanAsync(
        long revisionId, QcStage stage, SpecQcWindowStatus status,
        string? libKey, double? low, double? up)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var w = new SpecQcWindow
        {
            ProductRevisionId = revisionId, Stage = stage, Status = status,
            Title = $"plan-{stage}",
        };
        db.SpecQcWindows.Add(w); await db.SaveChangesAsync();

        db.QcCriteria.Add(new QcCriterion
        {
            SpecQcWindowId = w.Id, Seq = 1, Name = "Kích thước tổng thể",
            LibraryItemKey = libKey,
            ToleranceMin = low, ToleranceMax = up,
            TargetValue = low is not null && up is not null ? (low + up) / 2 : null,
            Unit = "mm",
        });
        await db.SaveChangesAsync();
        return w.Id;
    }

    private async Task<WoIpqcCheckItem?> HitAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.WoIpqcCheckItems.AsNoTracking()
            .Join(db.WoIpqcChecks.AsNoTracking(), i => i.WoIpqcCheckId, c => c.Id, (i, c) => new { i, c })
            .Where(x => x.c.WorkOrderId == woId && x.i.ItemKey == LibKey)
            .Select(x => x.i).FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Ke_hoach_DA_DUYET_thi_nguong_xuong_ho_so_WO()
    {
        await SeedLibraryAsync();
        var (woId, revId) = await SeedWoAsync("ok");
        var windowId = await SeedPlanAsync(revId, QcStage.IpqcPrint, SpecQcWindowStatus.Approved,
            LibKey, 19.5, 20.5);

        var client = await QcClientAsync("qc-lim-ok");
        await client.GetAsync($"/api/v2/work-orders/{woId}/ipqc");

        var hit = await HitAsync(woId);
        Assert.NotNull(hit);
        Assert.Equal(19.5, hit!.LimitLow!.Value, 3);          // ← đỏ nếu không nối
        Assert.Equal(20.5, hit.LimitUp!.Value, 3);
        Assert.Equal("mm", hit.LimitUnit);
        Assert.Equal(windowId, hit.LimitSourceWindowId);      // truy ngược được về kế hoạch
    }

    [Theory]
    [InlineData(SpecQcWindowStatus.Draft)]
    [InlineData(SpecQcWindowStatus.Superseded)]
    public async Task Ke_hoach_CHUA_DUYET_hoac_da_thay_the_thi_KHONG_lay(SpecQcWindowStatus st)
    {
        // Draft là thứ kỹ sư đang sửa dở — đóng băng nó vào hồ sơ đã ký là ký
        // lên một tiêu chuẩn chưa ai duyệt.
        await SeedLibraryAsync();
        var (woId, revId) = await SeedWoAsync(st.ToString().ToLowerInvariant());
        await SeedPlanAsync(revId, QcStage.IpqcPrint, st, LibKey, 19.5, 20.5);

        var client = await QcClientAsync($"qc-lim-{st}".ToLowerInvariant());
        await client.GetAsync($"/api/v2/work-orders/{woId}/ipqc");

        var hit = await HitAsync(woId);
        Assert.NotNull(hit);
        Assert.Null(hit!.LimitLow);                           // ← đỏ nếu bỏ lọc Status
        Assert.Null(hit.LimitSourceWindowId);
    }

    [Fact]
    public async Task Ke_hoach_khau_CAT_khong_bom_nguong_sang_hang_muc_khau_IN()
    {
        await SeedLibraryAsync();
        var (woId, revId) = await SeedWoAsync("stage");
        await SeedPlanAsync(revId, QcStage.IpqcCut, SpecQcWindowStatus.Approved, LibKey, 4.5, 5.5);

        var client = await QcClientAsync("qc-lim-stage");
        await client.GetAsync($"/api/v2/work-orders/{woId}/ipqc");

        var hit = await HitAsync(woId);
        Assert.NotNull(hit);
        Assert.Null(hit!.LimitLow);      // hạng mục thuộc LABEL (khâu IN), kế hoạch là khâu CẮT
    }

    [Fact]
    public async Task Chua_ai_soan_ke_hoach_thi_hang_muc_van_dung_binh_thuong()
    {
        // Trạng thái hôm nay: SpecQcWindows 0 dòng trên live. Không có ngưỡng
        // KHÔNG được làm hỏng việc dựng hạng mục — người vẫn chấm như trước.
        await SeedLibraryAsync();
        var (woId, _) = await SeedWoAsync("none");

        var client = await QcClientAsync("qc-lim-none");
        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/ipqc");
        Assert.True(resp.IsSuccessStatusCode);

        var hit = await HitAsync(woId);
        Assert.NotNull(hit);                                   // vẫn dựng đủ hạng mục
        Assert.Null(hit!.LimitLow);
    }
}
