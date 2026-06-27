using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using System.Linq;
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

    // Seeds two WOs sharing the search tag in their WoNo. WO-A carries a
    // completed FQC Pass + OQC Reject; WO-B carries an FQC Pending (which
    // QC History must exclude). One check per (WO, kind) — the table has a
    // unique (WorkOrderId, QcKind) index. Returns the shared search term.
    private async Task<string> SeedWoWithQcChecksAsync(string tag)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = "Prod", CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        async Task<long> AddWoAsync(string suffix)
        {
            var wo = new WorkOrder
            {
                WoNo = $"WO-QCH-{tag}-{suffix}",
                CustomerId = customer.Id,
                ProductId = product.Id,
                ProductName = "Prod",
                TargetQty = 1000,
                Uom = "pcs",
                MesPhase = "SHIPPED",
                Status = WoStatus.Finished,
            };
            db.WorkOrders.Add(wo);
            await db.SaveChangesAsync();
            return wo.Id;
        }

        var woA = await AddWoAsync("A");
        var woB = await AddWoAsync("B");

        db.WoQcChecks.AddRange(
            new WoQcCheck { WorkOrderId = woA, QcKind = "FQC", Judgment = WoQcJudgment.Pass, InspectedBy = $"insp-{tag}", InspectedAt = DateTime.UtcNow },
            new WoQcCheck { WorkOrderId = woA, QcKind = "OQC", Judgment = WoQcJudgment.Reject, InspectedBy = $"insp-{tag}", ApprovedBy = $"appr-{tag}", ApprovedAt = DateTime.UtcNow, JudgmentReason = "bad" },
            new WoQcCheck { WorkOrderId = woB, QcKind = "FQC", Judgment = WoQcJudgment.Pending });
        await db.SaveChangesAsync();
        return $"WO-QCH-{tag}";
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

    [Fact]
    public async Task QcHistory_excludes_pending_and_rolls_up_pass_reject()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woNo = await SeedWoWithQcChecksAsync(tag);
        var client = await AuthedClientAsync($"qch-{tag}");

        var dto = await client.GetFromJsonAsync<QcHistoryDto>($"/api/v2/qms/qc-history?search={woNo}");

        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Total);          // Pass + Reject; Pending excluded
        Assert.Equal(1, dto.Pass);
        Assert.Equal(1, dto.Reject);
        Assert.Equal(50, dto.PassRatePct);
        Assert.All(dto.Rows, r => Assert.NotEqual("Pending", r.Judgment));
        Assert.Contains(dto.Rows, r => r.QcKind == "OQC" && r.Judgment == "Reject" && r.ApprovedBy == $"appr-{tag}");
    }

    [Fact]
    public async Task QcHistory_kind_filter_narrows_to_one_kind()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woNo = await SeedWoWithQcChecksAsync(tag);
        var client = await AuthedClientAsync($"qch2-{tag}");

        var dto = await client.GetFromJsonAsync<QcHistoryDto>($"/api/v2/qms/qc-history?search={woNo}&kind=OQC");

        Assert.NotNull(dto);
        Assert.All(dto!.Rows, r => Assert.Equal("OQC", r.QcKind));
        Assert.Single(dto.Rows);              // only the OQC reject
    }
}
