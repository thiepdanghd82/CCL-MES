using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WorkOrders;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// L51 regression belt — <c>GET /work-orders</c> (list) and
/// <c>GET /work-orders/{id}</c> must return the flat
/// <see cref="WorkOrderListItem"/> DTO, NOT the EF entity.
///
/// The service <c>.Include(w =&gt; w.Inspections)</c>s, and
/// <c>QcInspection.WorkOrder</c> is a back-navigation ⇒ WorkOrder ⇄
/// Inspection is a reference cycle. Returning the entity graph made
/// System.Text.Json throw <c>JsonException: A possible object cycle was
/// detected</c> ⇒ HTTP 500 for ALL work orders, every caller. Each test
/// below seeds a WO WITH an inspection so the cycle is live; if these
/// endpoints ever revert to <c>Ok(entity)</c> they 500 here at CI instead
/// of at operator runtime.
/// </summary>
public sealed class WorkOrdersListDtoTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WorkOrdersListDtoTests(MesApiFactory fx) => _fx = fx;

    private async Task<WorkOrder> SeedWoWithInspectionAsync(string woNo)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var customer = new Customer { Code = "CUST-" + woNo, Name = "Customer " + woNo };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var product = new Product { ProductCode = "PROD-" + woNo, Name = "Product " + woNo, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = woNo,
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            MachineCode = "M-1",
            MachineName = "Press 1",
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.ReadyToRun,
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();

        // The back-navigation that made the entity graph cycle. Without a row
        // here the endpoint would happen to serialise fine and the regression
        // would go unproven.
        db.Set<QcInspection>().Add(new QcInspection
        {
            WorkOrderId = wo.Id,
            Type = QcType.IPQC,
            Result = QcResult.Pass,
            SampleSize = 5,
        });
        await db.SaveChangesAsync();
        return wo;
    }

    [Fact]
    public async Task List_returns_dto_not_entity_and_does_not_object_cycle()
    {
        await _fx.SeedUserAsync("wo-list-dto", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-list-dto", "P@ss!");
        var wo = await SeedWoWithInspectionAsync("WO-L51-LIST");

        var resp = await client.GetAsync("/api/v2/work-orders");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);   // was 500 (object cycle)

        var raw = await resp.Content.ReadAsStringAsync();
        // DTO shape: flat customerName present, NO nested navigation objects.
        Assert.Contains("customerName", raw);
        Assert.DoesNotContain("\"inspections\"", raw);
        Assert.DoesNotContain("\"workOrder\"", raw);   // the cycle back-ref

        var list = await resp.Content.ReadFromJsonAsync<List<WorkOrderListItem>>();
        Assert.NotNull(list);
        var item = Assert.Single(list!, x => x.WoNo == wo.WoNo);
        Assert.Equal("Customer WO-L51-LIST", item.CustomerName);
        Assert.Equal("ReadyToRun", item.CurrentStep);
        Assert.Equal("InProgress", item.Status);
        Assert.Equal(1, item.InspectionCount);
        Assert.False(string.IsNullOrEmpty(item.MesPhase));
    }

    [Fact]
    public async Task Get_by_id_returns_dto_200_with_etag()
    {
        await _fx.SeedUserAsync("wo-get-dto", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-get-dto", "P@ss!");
        var wo = await SeedWoWithInspectionAsync("WO-L51-GET");

        var resp = await client.GetAsync($"/api/v2/work-orders/{wo.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);   // was 500 (object cycle)

        var dto = await resp.Content.ReadFromJsonAsync<WorkOrderListItem>();
        Assert.NotNull(dto);
        Assert.Equal(wo.Id, dto!.Id);
        Assert.Equal(wo.WoNo, dto.WoNo);
        Assert.Equal("Customer WO-L51-GET", dto.CustomerName);
        Assert.False(string.IsNullOrEmpty(dto.ETag));
        // ETag also surfaced at the HTTP layer, mirroring /summary.
        Assert.NotNull(resp.Headers.ETag);
    }

    [Fact]
    public async Task Get_by_id_returns_404_when_missing()
    {
        await _fx.SeedUserAsync("wo-404-dto", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-404-dto", "P@ss!");

        var resp = await client.GetAsync("/api/v2/work-orders/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
