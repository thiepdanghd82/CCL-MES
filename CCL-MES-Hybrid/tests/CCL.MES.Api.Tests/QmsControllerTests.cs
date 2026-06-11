using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Qms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.9 — wire tests for GET /api/v2/qms/queue. The inspection queue is
/// the worklist of WOs due for each QC stage, derived from MesPhase
/// (IPQC_WAIT / FQC_PENDING / OQC_PENDING). Asserts bucketing + that
/// non-QC phases are excluded + the auth gate.
/// </summary>
public sealed class QmsControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public QmsControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task SeedWoAsync(string woNo, string mesPhase, string productName)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
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
            QtyDoneCached = 900,
            Uom = "pcs",
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> AuthedClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", "QC");
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    [Fact]
    public async Task Queue_requires_auth()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/qms/queue");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Queue_buckets_wos_by_qc_stage_and_excludes_others()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedWoAsync($"WO-IPQC-{tag}", "IPQC_WAIT", $"P-{tag}");
        await SeedWoAsync($"WO-FQC-{tag}", "FQC_PENDING", $"P-{tag}");
        await SeedWoAsync($"WO-OQC-{tag}", "OQC_PENDING", $"P-{tag}");
        await SeedWoAsync($"WO-RUN-{tag}", "RUNNING", $"P-{tag}");

        var client = await AuthedClientAsync($"qms-{tag}");
        var dto = await client.GetFromJsonAsync<QmsQueueDto>("/api/v2/qms/queue");

        Assert.NotNull(dto);
        Assert.Contains(dto!.Ipqc, r => r.WoNo == $"WO-IPQC-{tag}");
        Assert.Contains(dto.Fqc, r => r.WoNo == $"WO-FQC-{tag}");
        Assert.Contains(dto.Oqc, r => r.WoNo == $"WO-OQC-{tag}");

        // The RUNNING WO is in no QC bucket.
        var all = dto.Ipqc.Concat(dto.Fqc).Concat(dto.Oqc);
        Assert.DoesNotContain(all, r => r.WoNo == $"WO-RUN-{tag}");

        // Counts match their lists.
        Assert.Equal(dto.Ipqc.Count, dto.IpqcCount);
        Assert.Equal(dto.Fqc.Count, dto.FqcCount);
        Assert.Equal(dto.Oqc.Count, dto.OqcCount);
    }
}
