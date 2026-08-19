using CCL.MES.Domain.Entities;
using CCL.MES.Shared.WorkOrders;

namespace CCL.MES.Api.Mapping;

/// <summary>
/// Maps the EF <see cref="WorkOrder"/> entity → the flat, cycle-free
/// <see cref="WorkOrderListItem"/> wire DTO.
///
/// Lives in the Api layer — the only place that references BOTH
/// <c>CCL.MES.Domain</c> and <c>CCL.MES.Shared</c> — so the DTO stays a pure
/// POCO and the controller stays thin (bind · authorize · call · map). See
/// Lesson L51: <c>GET /work-orders</c> + <c>/{id}</c> used to return the
/// entity directly and every request 500'd with a System.Text.Json object
/// cycle (entity ⇄ Customer/Inspections navigations).
///
/// Enums are stringified and navigations dropped (the collection was one arm
/// of the cycle; <see cref="WorkOrderListItem.InspectionCount"/> keeps the
/// signal without the graph). <c>DateTime</c> → <c>DateTimeOffset</c> pins
/// <c>Utc</c> kind to match the convention used by the <c>/summary</c> map.
/// </summary>
public static class WorkOrderListItemMapper
{
    public static WorkOrderListItem ToListItem(this WorkOrder wo) => new()
    {
        Id = wo.Id,
        WoNo = wo.WoNo,
        CustomerId = wo.CustomerId,
        CustomerName = wo.Customer?.Name,
        ProductId = wo.ProductId,
        ProductName = wo.ProductName,
        ProductRevisionId = wo.ProductRevisionId,
        MachineCode = wo.MachineCode,
        MachineName = wo.MachineName,
        TargetQty = wo.TargetQty,
        Uom = wo.Uom,
        ProducedQty = wo.ProducedQty,
        CurrentStep = wo.CurrentStep.ToString(),
        Status = wo.Status.ToString(),
        MesPhase = wo.MesPhase,
        Priority = wo.Priority,
        MaterialsReady = wo.MaterialsReady,
        SetupConfirmed = wo.SetupConfirmed,
        RohsOk = wo.RohsOk,
        PlannedStart = ToUtcOffset(wo.PlannedStart),
        PlannedEnd = ToUtcOffset(wo.PlannedEnd),
        SettingStartAt = ToUtcOffset(wo.SettingStartAt),
        SettingEndAt = ToUtcOffset(wo.SettingEndAt),
        SettingDurationSec = wo.SettingDurationSec,
        QtyDoneCached = wo.QtyDoneCached,
        QtyNgCached = wo.QtyNgCached,
        InspectionCount = wo.Inspections?.Count ?? 0,
        CreatedAt = ToUtcOffset(wo.CreatedAt),
        CreatedBy = wo.CreatedBy,
        UpdatedAt = ToUtcOffset(wo.UpdatedAt),
        UpdatedBy = wo.UpdatedBy,
        ETag = wo.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : "",
    };

    private static DateTimeOffset ToUtcOffset(DateTime dt) =>
        new(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

    private static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt is null ? null : ToUtcOffset(dt.Value);
}
