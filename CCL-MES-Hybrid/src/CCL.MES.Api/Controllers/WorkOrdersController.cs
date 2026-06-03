using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Read-side surface for the legacy <see cref="WorkOrderService"/>. P10.1
/// exposes the queries that the MAUI Hybrid pilot needs to render a
/// work-order list + drawer (P10.2 default pilot pick per Q10). Write
/// surface (Create / Advance / UpdateFlags) lands as the MAUI surface
/// drives the endpoint demand; we resist exposing them speculatively to
/// keep the JWT-secured contract surface minimal.
///
/// Default authorization = whatever the global FallbackPolicy enforces
/// (RequireAuthenticatedUser). Endpoints that need tighter gates declare
/// their own <c>[Authorize(Policy = "...")]</c>.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class WorkOrdersController : ControllerBase
{
    private readonly WorkOrderService _svc;
    public WorkOrdersController(WorkOrderService svc) => _svc = svc;

    /// <summary>Flat list — small datasets only. Use <c>shop-orders</c>
    /// instead for the grouped operator view.</summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkOrder>>> List() =>
        Ok(await _svc.GetAllAsync());

    [HttpGet("{id:long}")]
    public async Task<ActionResult<WorkOrder>> Get(long id)
    {
        var wo = await _svc.GetAsync(id);
        return wo is null ? NotFound() : Ok(wo);
    }

    /// <summary>
    /// Grouped shop-order view — mirrors the legacy Blazor Dashboard
    /// rollup so the MAUI pilot can render identical data.
    /// </summary>
    [HttpGet("shop-orders")]
    public async Task<ActionResult<ShopOrderListResult>> ShopOrders() =>
        Ok(await _svc.ShopOrderListAsync());

    /// <summary>
    /// Drawer payload for a single Work Order, keyed by WO number string
    /// (matches the legacy URL shape).
    /// </summary>
    [HttpGet("by-no/{woNo}")]
    public async Task<ActionResult<WorkOrderDrawerView>> Drawer(string woNo)
    {
        var view = await _svc.GetDrawerAsync(woNo);
        return view is null ? NotFound() : Ok(view);
    }
}
