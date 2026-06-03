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
/// P10.3 W4 — coverage for the WO summary + advance endpoints:
///   GET /work-orders/by-no/{woNo}/summary  → 200 / 404
///   POST /work-orders/{id}/advance         → 200 ok=true / 200 ok=false (guard) / 404
/// + verifies that the X-Device-Id header emits the WO_ADVANCE_DEVICE
/// audit row alongside the existing WO_ADVANCE row.
/// </summary>
public sealed class WorkOrdersAdvanceTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WorkOrdersAdvanceTests(MesApiFactory fx) => _fx = fx;

    private async Task<WorkOrder> SeedWoAsync(string woNo, ProcessStepCode step = ProcessStepCode.ReadyToRun, bool materialsReady = true, bool setupConfirmed = true)
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
            CurrentStep = step,
            Status = WoStatus.InProgress,
            MaterialsReady = materialsReady,
            SetupConfirmed = setupConfirmed,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo;
    }

    [Fact]
    public async Task Summary_returns_404_when_wo_not_found()
    {
        await _fx.SeedUserAsync("wo1", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo1", "P@ss!");

        var resp = await client.GetAsync("/api/v2/work-orders/by-no/DOES-NOT-EXIST/summary");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("work_order.not_found", err!.Code);
    }

    [Fact]
    public async Task Summary_returns_shape_for_existing_wo()
    {
        await _fx.SeedUserAsync("wo2", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo2", "P@ss!");
        var wo = await SeedWoAsync("WO-W4-100", ProcessStepCode.ReadyToRun);

        var summary = await client.GetFromJsonAsync<WorkOrderSummary>(
            $"/api/v2/work-orders/by-no/{Uri.EscapeDataString(wo.WoNo)}/summary");

        Assert.NotNull(summary);
        Assert.Equal(wo.WoNo, summary!.WoNo);
        Assert.Equal("Customer WO-W4-100", summary.CustomerName);
        Assert.Equal("PROD-WO-W4-100", summary.ProductCode);
        Assert.Equal("ReadyToRun", summary.CurrentStep);
        // BadgeLabelKey gets populated by the WorkOrderStatusBadge mapper —
        // we don't assert the exact key here (avoid coupling to badge prose),
        // just that it's non-empty.
        Assert.False(string.IsNullOrEmpty(summary.BadgeLabelKey));
    }

    [Fact]
    public async Task Advance_ok_path_returns_next_step()
    {
        await _fx.SeedUserAsync("wo3", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo3", "P@ss!");
        // ReadyToRun → Running is unconditional, so this exercises the
        // success path without needing additional guard prep.
        var wo = await SeedWoAsync("WO-W4-200", ProcessStepCode.ReadyToRun);

        client.DefaultRequestHeaders.Add("X-Device-Id", "0193a1d9-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var resp = await client.PostAsync($"/api/v2/work-orders/{wo.Id}/advance", content: null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Ok);
        Assert.Equal("Running", body.CurrentStep);
        Assert.Null(body.ErrorCode);
    }

    [Fact]
    public async Task Advance_guard_failure_returns_200_with_error_code()
    {
        await _fx.SeedUserAsync("wo4", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo4", "P@ss!");
        // PrePressCheck without ProductRevisionId + MaterialsReady = guard fails.
        var wo = await SeedWoAsync("WO-W4-300", ProcessStepCode.PrePressCheck, materialsReady: false);

        var resp = await client.PostAsync($"/api/v2/work-orders/{wo.Id}/advance", content: null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Ok);
        Assert.Equal("PrePressCheck", body.CurrentStep);
        Assert.Equal("RequiresSpecAndMaterials", body.ErrorCode);
    }

    [Fact]
    public async Task Advance_returns_404_when_wo_id_does_not_exist()
    {
        await _fx.SeedUserAsync("wo5", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo5", "P@ss!");

        var resp = await client.PostAsync("/api/v2/work-orders/999999/advance", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Advance_with_device_id_emits_paired_audit_row()
    {
        await _fx.SeedUserAsync("wo6", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo6", "P@ss!");
        var wo = await SeedWoAsync("WO-W4-400", ProcessStepCode.ReadyToRun);
        var deviceId = "0193a1d9-cafe-cafe-cafe-cafecafecafe";

        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        var resp = await client.PostAsync($"/api/v2/work-orders/{wo.Id}/advance", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var devAudit = db.AuditLogs
            .Where(a => a.Action == "WO_ADVANCE_DEVICE" && a.TargetId == deviceId)
            .ToList();
        Assert.NotEmpty(devAudit);
        Assert.Contains(devAudit, a => a.Detail != null && a.Detail.Contains(wo.WoNo));
    }

    [Fact]
    public async Task Advance_audit_from_to_capture_uses_before_value()
    {
        // Regression guard — caught during hardware verify on 2026-06-03.
        // WorkOrderService.AdvanceAsync re-queries the WO via the same EF
        // tracked context, so reading existing.CurrentStep AFTER the call
        // gives the AFTER value. Controller now captures `fromStep` before
        // the call. This test asserts the recorded "from" matches the
        // pre-advance step and "to" matches the post-advance step.
        await _fx.SeedUserAsync("wo7", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo7", "P@ss!");
        var wo = await SeedWoAsync("WO-W4-500", ProcessStepCode.ReadyToRun);
        var deviceId = "0193a1d9-from-tocp-1234-567890abcdef";

        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        var resp = await client.PostAsync($"/api/v2/work-orders/{wo.Id}/advance", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var devAudit = db.AuditLogs
            .Where(a => a.Action == "WO_ADVANCE_DEVICE" && a.TargetId == deviceId)
            .Single();
        Assert.NotNull(devAudit.Detail);
        Assert.Contains("\"from\":\"ReadyToRun\"", devAudit.Detail);
        Assert.Contains("\"to\":\"Running\"", devAudit.Detail);
    }
}
