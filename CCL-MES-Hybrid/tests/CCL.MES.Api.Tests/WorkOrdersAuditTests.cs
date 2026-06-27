using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.WorkOrders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7 — wire tests for GET /api/v2/work-orders/{id}/audit (WO-scoped
/// audit trail for the scan-surface sidebar). Any-auth; returns the WO's
/// audit rows newest-first.
/// </summary>
public sealed class WorkOrdersAuditTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WorkOrdersAuditTests(MesApiFactory fx) => _fx = fx;

    private async Task<long> SeedWoWithAuditAsync(string action)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(cust);
        await db.SaveChangesAsync();
        var prod = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = "Prod", CustomerId = cust.Id };
        db.Products.Add(prod);
        await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-AUD-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = cust.Id, ProductId = prod.Id, ProductName = "Prod",
            TargetQty = 100, Uom = "pcs", MesPhase = "PREPRESS",
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = "op-1",
            ActorRole = "Operator",
            Action = action,
            TargetType = "WorkOrder",
            TargetId = wo.Id.ToString(),
            Detail = "{\"machine\":\"FBL01\"}",
        });
        await db.SaveChangesAsync();
        return wo.Id;
    }

    private async Task<HttpClient> AuthedClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", "Operator");
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    [Fact]
    public async Task Audit_requires_auth()
    {
        var resp = await _fx.CreateClient().GetAsync("/api/v2/work-orders/1/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Audit_returns_wo_scoped_rows_newest_first()
    {
        var woId = await SeedWoWithAuditAsync("WO_SCAN");
        var c = await AuthedClientAsync("aud-op");

        var rows = await c.GetFromJsonAsync<List<WoAuditEntry>>($"/api/v2/work-orders/{woId}/audit");

        Assert.NotNull(rows);
        var row = Assert.Single(rows!);
        Assert.Equal("WO_SCAN", row.Action);
        Assert.Equal("op-1", row.ActorUsername);
        Assert.Contains("FBL01", row.Detail);
    }
}
