using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Machines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.8 — wire tests for GET /api/v2/shop-orders/history. The forensic
/// view lists closed WOs (MesPhase SHIPPED|CANCELLED) with KPI roll-ups
/// + period/search filters. Asserts the closed-only filter, the KPI
/// sums, search, and the auth gate.
/// </summary>
public sealed class ShopOrdersControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public ShopOrdersControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task SeedWoAsync(string woNo, string mesPhase, string customerName,
        string productName, int done, int ng)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = customerName };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = productName, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.WorkOrders.Add(new WorkOrder
        {
            WoNo = woNo,
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = productName,
            MachineCode = "FBL01",
            TargetQty = 1000,
            QtyDoneCached = done,
            QtyNgCached = ng,
            Uom = "pcs",
            MesPhase = mesPhase,
            Status = WoStatus.Finished,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> AuthedClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", "Supervisor");
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    [Fact]
    public async Task History_requires_auth()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/shop-orders/history");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task History_lists_only_closed_wos_with_kpis()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedWoAsync($"WO-SHIP-{tag}", "SHIPPED", "Acme", $"Label-{tag}", done: 1000, ng: 10);
        await SeedWoAsync($"WO-RUN-{tag}", "RUNNING", "Acme", $"Running-{tag}", done: 500, ng: 0);

        var client = await AuthedClientAsync($"soh-{tag}");
        var dto = await client.GetFromJsonAsync<ShopOrderHistoryDto>(
            $"/api/v2/shop-orders/history?search=Label-{tag}");

        Assert.NotNull(dto);
        var row = Assert.Single(dto!.Rows);
        Assert.Equal($"WO-SHIP-{tag}", row.WoNo);
        Assert.Equal("SHIPPED", row.MesPhase);
        Assert.Equal("Acme", row.CustomerName);
        Assert.Equal(1000, row.QtyDone);
        Assert.Equal(99, row.YieldPct);            // (1000-10)/1000 = 99%
        Assert.Equal(1, dto.TotalWos);
        Assert.Equal(1000, dto.Output);
        Assert.Equal(10, dto.Reject);
        Assert.Equal(99, dto.YieldPct);
    }

    [Fact]
    public async Task History_running_wo_is_excluded()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedWoAsync($"WO-RUNONLY-{tag}", "RUNNING", "Beta", $"OnlyRun-{tag}", done: 200, ng: 1);

        var client = await AuthedClientAsync($"soh2-{tag}");
        var dto = await client.GetFromJsonAsync<ShopOrderHistoryDto>(
            $"/api/v2/shop-orders/history?search=OnlyRun-{tag}");

        Assert.NotNull(dto);
        Assert.Empty(dto!.Rows);    // RUNNING is not a closed phase
    }
}
