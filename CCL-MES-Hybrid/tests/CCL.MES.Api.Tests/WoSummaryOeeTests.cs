using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.WoQcReview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Đợt 1 C3 — GET /work-orders/{id}/summary-report, OEE performance leg.
///
/// The endpoint used to read Machine.IdealCycleTimeSec, a second speed
/// source that diverged from the WorkCenter.IdealSpeedPcsH the rest of the
/// system uses, and returned a bare null whenever it came up empty. These
/// tests pin the new contract end to end: WorkCenter is the source, and a
/// null performance always ships a reason code.
///
/// Coverage per cmes-thin-controller: happy path · guard-fail branch ·
/// 403 branch.
/// </summary>
public sealed class WoSummaryOeeTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WoSummaryOeeTests(MesApiFactory fx) => _fx = fx;

    /// <summary>Seeds a SHIPPED WO with one closed 1-hour run session and
    /// an optional WorkCenter whose code matches the WO's MachineCode.</summary>
    private async Task<long> SeedWoAsync(
        string? machineCode, double? workCenterSpeed, int qtyDone, bool createWorkCenter = true)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var product = new Product
        {
            ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Prod",
            CustomerId = customer.Id,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        if (machineCode is not null && createWorkCenter)
        {
            db.WorkCenters.Add(new WorkCenter
            {
                Code = machineCode,
                Description = "Slitter",
                IdealSpeedPcsH = workCenterSpeed,
                Active = true,
            });
            await db.SaveChangesAsync();
        }

        var wo = new WorkOrder
        {
            WoNo = "WO-OEE-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 1000,
            Uom = "pcs",
            MachineCode = machineCode,
            ProducedQty = qtyDone,
            CurrentStep = ProcessStepCode.Oqc,
            MesPhase = "SHIPPED",
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();

        // Exactly 3600s of run time → with 600 pcs/h the planned output is
        // 600 units, so qtyDone=300 lands on a clean 50%.
        var start = new DateTime(2026, 06, 05, 08, 00, 00, DateTimeKind.Utc);
        db.WoRunSessions.Add(new WoRunSession
        {
            WoId = wo.Id,
            StartedAt = start,
            EndedAt = start.AddHours(1),
        });
        await db.SaveChangesAsync();

        return wo.Id;
    }

    private async Task<HttpClient> QcClientAsync()
    {
        var name = "qc-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(name, "Pa55w.rd!", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, name, "Pa55w.rd!");
        return client;
    }

    // ── happy path ────────────────────────────────────────────────

    [Fact]
    public async Task Performance_comes_from_workcenter_ideal_speed()
    {
        var woId = await SeedWoAsync(machineCode: "WC-OEE-OK", workCenterSpeed: 600, qtyDone: 300);
        var client = await QcClientAsync();

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/summary-report");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var report = await resp.Content.ReadFromJsonAsync<WoSummaryReport>();
        Assert.NotNull(report);
        Assert.NotNull(report!.Oee.Performance);
        Assert.Equal(0.5, report.Oee.Performance!.Value, precision: 6);
        // Non-null performance ⇒ no reason. The pair is exclusive.
        Assert.Null(report.Oee.PerformanceUnavailableReason);
    }

    // ── guard-fail branches: null performance ALWAYS carries a reason ──

    [Fact]
    public async Task Missing_workcenter_speed_returns_reason_not_silent_null()
    {
        // The production-shaped case: work center exists, speed never filled
        // in. 38 of 43 work centers look like this today.
        var woId = await SeedWoAsync(machineCode: "WC-OEE-NOSPEED", workCenterSpeed: null, qtyDone: 300);
        var client = await QcClientAsync();

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/summary-report");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var report = await resp.Content.ReadFromJsonAsync<WoSummaryReport>();
        Assert.Null(report!.Oee.Performance);
        Assert.Equal("workcenter_speed_missing", report.Oee.PerformanceUnavailableReason);
        // Performance is a factor of OEE, so OEE stays null too — honestly.
        Assert.Null(report.Oee.Oee);
    }

    [Fact]
    public async Task No_machine_code_returns_workcenter_missing_reason()
    {
        var woId = await SeedWoAsync(machineCode: null, workCenterSpeed: null, qtyDone: 300);
        var client = await QcClientAsync();

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/summary-report");
        var report = await resp.Content.ReadFromJsonAsync<WoSummaryReport>();

        Assert.Null(report!.Oee.Performance);
        Assert.Equal("workcenter_missing", report.Oee.PerformanceUnavailableReason);
    }

    [Fact]
    public async Task Machine_code_with_no_matching_workcenter_reports_missing()
    {
        var woId = await SeedWoAsync(
            machineCode: "WC-OEE-ORPHAN", workCenterSpeed: null, qtyDone: 300, createWorkCenter: false);
        var client = await QcClientAsync();

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/summary-report");
        var report = await resp.Content.ReadFromJsonAsync<WoSummaryReport>();

        Assert.Null(report!.Oee.Performance);
        Assert.Equal("workcenter_missing", report.Oee.PerformanceUnavailableReason);
    }

    // ── 403 branch ────────────────────────────────────────────────

    [Fact]
    public async Task Summary_report_denies_operator_role()
    {
        // QcRead = Admin / Supervisor / QC (Program.cs). Operator is out.
        var woId = await SeedWoAsync(machineCode: "WC-OEE-403", workCenterSpeed: 600, qtyDone: 300);
        var name = "op-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(name, "Pa55w.rd!", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, name, "Pa55w.rd!");

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/summary-report");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
