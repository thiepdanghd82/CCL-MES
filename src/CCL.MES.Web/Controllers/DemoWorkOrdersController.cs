using System.Globalization;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Web.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 8 PR #32d — Demo Work Order creation endpoint. Called by the
/// "Start Demo" buttons in the <c>/workorders</c> Demo section. Three
/// hardcoded templates are mirrored here (Code → Demo definition); the
/// component sends only the Code so the server can authoritatively
/// resolve every FK from real seed data, never trusting client-supplied
/// Customer/Product IDs.
///
/// Flow (per approved PR #32d plan):
///   1. RBAC gate via <c>[Authorize(Roles = "Admin,Supervisor")]</c>
///      (Q9 — Operator must not create WOs from demo).
///   2. Resolve template by code; 404 if unknown.
///   3. Resolve real FKs from DB (Customer / Product / ProductRevision)
///      by Code / ProductCode. 422 if seed data missing.
///   4. Generate WoNo with distinct <c>DEMO-yyyyMMdd-HHmmss</c> prefix
///      so it cannot collide with the production <c>WO-yy-NNNN</c>
///      naming. Re-roll with suffix <c>-2</c>, <c>-3</c>, … if a click
///      lands within the same second as a prior demo.
///   5. Call existing <c>WorkOrderService.CreateAsync</c> — body NOT
///      modified.
///   6. Emit <c>WO_CREATE</c> audit at callsite with detail JSON
///      <c>{ template_code, wo_no, wo_id, customer_id, product_id,
///      machine_code, target_qty, uom, source: "demo" }</c>.
///   7. Notify ShopfloorHub so other circuits' card view refreshes too.
///   8. Return 201 with <c>{ woNo, id }</c>.
///
/// Phase 6 vùng cấm preserved: <c>CreateAsync</c> body untouched, no
/// state-machine edit, no migration, no ProductionLog touch.
/// </summary>
[ApiController]
[Route("api/workorders/demo")]
[Authorize(Roles = "Admin,Supervisor")]
public class DemoWorkOrdersController : ControllerBase
{
    private readonly WorkOrderService _wo;
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ShopfloorNotifier _notifier;

    public DemoWorkOrdersController(
        WorkOrderService wo,
        IMesDbContext db,
        IAuditWriter audit,
        ShopfloorNotifier notifier)
    {
        _wo = wo;
        _db = db;
        _audit = audit;
        _notifier = notifier;
    }

    private static readonly Dictionary<string, DemoTemplate> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["demo-1"] = new DemoTemplate("demo-1", "BRADY", "BRD-7656-D", "ACNC3", 5000,  "pcs"),
        ["demo-2"] = new DemoTemplate("demo-2", "BRADY", "BRD-7656-D", "ACNC3", 20000, "pcs"),
        ["demo-3"] = new DemoTemplate("demo-3", "SEV",   "GH68-55731L","ACNC3", 10000, "pcs"),
    };

    [HttpPost("{templateCode}")]
    public async Task<IActionResult> Create(string templateCode)
    {
        try
        {
            if (!Templates.TryGetValue(templateCode, out var tpl))
            {
                return Problem(title: "Demo template not found", detail: templateCode, statusCode: 404);
            }

            var customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == tpl.CustomerCode);
            if (customer is null)
            {
                return Problem(title: "Seed customer missing", detail: tpl.CustomerCode, statusCode: 422);
            }

            var product = await _db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductCode == tpl.ProductCode && p.CustomerId == customer.Id);
            if (product is null)
            {
                return Problem(title: "Seed product missing", detail: tpl.ProductCode, statusCode: 422);
            }

            // ProductRevision linkage is the FK protected by Sprint S-D15-COSTING
            // baseline check (ProductRevision↔WO must remain intact). Resolve the
            // newest revision for the seeded product so the FK is always set.
            var revision = await _db.ProductRevisions
                .AsNoTracking()
                .Where(r => r.ProductId == product.Id)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            var actor = User?.Identity?.Name ?? "anonymous";
            var woNo = await GenerateDemoWoNoAsync();

            var req = new CreateWoRequest
            {
                WoNo = woNo,
                CustomerId = customer.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                ProductRevisionId = revision?.Id,
                MachineCode = tpl.MachineCode,
                MachineName = null,
                TargetQty = tpl.TargetQty,
                Uom = tpl.Uom,
            };

            var wo = await _wo.CreateAsync(req);

            await _audit.EmitAsync(
                AuditAction.WoCreate,
                actor,
                actorRole: "",
                targetType: "WorkOrder",
                targetId: wo.Id.ToString(CultureInfo.InvariantCulture),
                detail: JsonSerializer.Serialize(new
                {
                    template_code = tpl.Code,
                    wo_no = wo.WoNo,
                    wo_id = wo.Id,
                    customer_id = customer.Id,
                    product_id = product.Id,
                    machine_code = tpl.MachineCode,
                    target_qty = tpl.TargetQty,
                    uom = tpl.Uom,
                    source = "demo",
                }));

            // SignalR push so other circuits' WO card list refreshes too.
            await _notifier.NotifyChangedAsync("create");

            return StatusCode(201, new { woNo = wo.WoNo, id = wo.Id });
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Demo Work Order creation failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Build a WoNo guaranteed not to collide with the production
    /// <c>WO-yy-NNNN</c> scheme. Format: <c>DEMO-yyyyMMdd-HHmmss</c>.
    /// If two clicks land within the same second, append a numeric
    /// suffix (<c>-2</c>, <c>-3</c>, …) until the DB has no row with
    /// that WoNo. Max 50 tries — far more than realistic.
    /// </summary>
    private async Task<string> GenerateDemoWoNoAsync()
    {
        var stamp = DateTime.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = $"DEMO-{stamp}";
        for (int i = 2; i <= 50; i++)
        {
            var exists = await _db.WorkOrders.AsNoTracking().AnyAsync(w => w.WoNo == candidate);
            if (!exists) return candidate;
            candidate = $"DEMO-{stamp}-{i}";
        }
        // 50 collisions in the same second is operationally impossible.
        // If somehow reached, return a high-entropy fallback.
        return $"DEMO-{stamp}-{Guid.NewGuid():N}".Substring(0, 32);
    }

    private sealed record DemoTemplate(
        string Code,
        string CustomerCode,
        string ProductCode,
        string MachineCode,
        int TargetQty,
        string Uom);
}
