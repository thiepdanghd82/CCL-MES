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
/// P10.8 — wire tests for GET /api/v2/machines/dashboard. The read-model
/// loads WorkCenters + derives a live status from the active WO matched
/// by MachineCode. This fixture seeds two idle work-centers (no active
/// WO) and asserts the projection + KPI counts + the auth gate. The
/// Running/Setup rendering paths are covered by MachineDashboardTests
/// (bUnit) against stubbed data; a WO-join integration test lands in
/// slice 2 alongside the area/filter work.
/// </summary>
public sealed class MachinesControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public MachinesControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task SeedWorkCentersAsync(params (string Code, string Desc, string Area)[] wcs)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        foreach (var (code, desc, area) in wcs)
        {
            db.WorkCenters.Add(new WorkCenter
            {
                Code = code,
                Description = desc,
                Area = area,
                Active = true,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedRunningWoOnAsync(string machineCode)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = "Prod", CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.WorkOrders.Add(new WorkOrder
        {
            WoNo = "WO-MD-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            MachineCode = machineCode,
            TargetQty = 1000,
            QtyDoneCached = 250,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.Running,
            MesPhase = "RUNNING",
            Status = WoStatus.InProgress,
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
    public async Task Dashboard_requires_auth()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/machines/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Dashboard_lists_idle_work_centers_with_kpi_counts()
    {
        await SeedWorkCentersAsync(
            ("TST-MD-A", "Test press A", "FLEXO"),
            ("TST-MD-B", "Test press B", "DIECUT"));
        var client = await AuthedClientAsync("md-sup");

        var resp = await client.GetAsync("/api/v2/machines/dashboard");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<MachineDashboardDto>();
        Assert.NotNull(dto);

        // KPI invariant: counts partition the total.
        Assert.Equal(dto!.Total, dto.Running + dto.Setup + dto.Idle);
        Assert.Equal(dto.Machines.Count, dto.Total);

        // Our two seeded WCs are present + idle (no active WO).
        var a = Assert.Single(dto.Machines, m => m.Code == "TST-MD-A");
        Assert.Equal("Idle", a.Status);
        Assert.Equal("FLEXO", a.Area);
        Assert.Null(a.ActiveWoNo);
        Assert.Contains(dto.Machines, m => m.Code == "TST-MD-B" && m.Status == "Idle");
        Assert.True(dto.Idle >= 2);
    }

    [Fact]
    public async Task Dashboard_marks_machine_running_when_a_running_wo_is_on_it()
    {
        await SeedWorkCentersAsync(("TST-MD-RUN", "Running press", "FLEXO"));
        await SeedRunningWoOnAsync("TST-MD-RUN");
        var client = await AuthedClientAsync("md-run-sup");

        var dto = await client.GetFromJsonAsync<MachineDashboardDto>("/api/v2/machines/dashboard");

        Assert.NotNull(dto);
        var m = Assert.Single(dto!.Machines, x => x.Code == "TST-MD-RUN");
        Assert.Equal("Running", m.Status);
        Assert.NotNull(m.ActiveWoNo);
        Assert.Equal("RUNNING", m.ActiveMesPhase);
        Assert.Equal(1000, m.TargetQty);
        Assert.Equal(250, m.QtyDone);
        Assert.True(dto.Running >= 1);
    }
}
