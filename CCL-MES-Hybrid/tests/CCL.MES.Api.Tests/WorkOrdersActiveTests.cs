using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.WorkOrders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7 landing — wire tests for GET /api/v2/work-orders/active
/// (SpecHub "Active Work Orders" card list). Returns non-terminal WOs
/// with the card fields; SHIPPED/CANCELLED are excluded.
/// </summary>
public sealed class WorkOrdersActiveTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WorkOrdersActiveTests(MesApiFactory fx) => _fx = fx;

    private async Task<string> SeedWoAsync(string mesPhase, string product, string customer, int target)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = customer };
        db.Customers.Add(cust);
        await db.SaveChangesAsync();
        var prod = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = product, CustomerId = cust.Id };
        db.Products.Add(prod);
        await db.SaveChangesAsync();
        var woNo = "WO-ACT-" + Guid.NewGuid().ToString("N")[..6];
        db.WorkOrders.Add(new WorkOrder
        {
            WoNo = woNo,
            CustomerId = cust.Id,
            ProductId = prod.Id,
            ProductName = product,
            MachineCode = "FBL01",
            TargetQty = target,
            QtyDoneCached = 100,
            Uom = "pcs",
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return woNo;
    }

    private async Task<HttpClient> AuthedClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", "Operator");
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    [Fact]
    public async Task Active_requires_auth()
    {
        var resp = await _fx.CreateClient().GetAsync("/api/v2/work-orders/active");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Active_returns_in_progress_wos_and_excludes_shipped()
    {
        var active = await SeedWoAsync("PREPRESS", "Battery Caution Label", "Panasonic VN", 9000);
        var shipped = await SeedWoAsync("SHIPPED", "Done Label", "Acme", 1000);
        var c = await AuthedClientAsync("wo-active");

        var cards = await c.GetFromJsonAsync<List<ActiveWorkOrderCard>>("/api/v2/work-orders/active");

        Assert.NotNull(cards);
        var card = Assert.Single(cards!, x => x.WoNo == active);
        Assert.Equal("Panasonic VN", card.CustomerName);
        Assert.Equal("Battery Caution Label", card.ProductName);
        Assert.Equal("PREPRESS", card.MesPhase);
        Assert.Equal(9000, card.TargetQty);
        Assert.Equal(100, card.QtyDone);

        Assert.DoesNotContain(cards!, x => x.WoNo == shipped);   // terminal excluded
    }
}
