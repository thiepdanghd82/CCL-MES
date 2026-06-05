using CCL.MES.Hybrid.Client.WorkOrders;
using CCL.MES.Shared.WorkOrders;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7a-1.3 — narrow <see cref="IAdvanceClient"/> stub the
/// AdvanceOrchestrator tests use. Only the 2 methods the
/// orchestrator depends on; everything else stays out of the
/// test surface entirely.
/// </summary>
public sealed class ScriptedApi : IAdvanceClient
{
    public Func<long, string, CancellationToken, Task<AdvanceWorkOrderResponse>>?
        AdvanceImpl { get; set; }

    public Func<string, CancellationToken, Task<WorkOrderSummary?>>?
        GetByNoImpl { get; set; }

    public Task<AdvanceWorkOrderResponse> AdvanceWorkOrderAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
        => AdvanceImpl is not null
            ? AdvanceImpl(workOrderId, ifMatchETag, ct)
            : throw new NotImplementedException("AdvanceImpl not set by test");

    public Task<WorkOrderSummary?> GetWorkOrderByNoAsync(string woNo, CancellationToken ct = default)
        => GetByNoImpl is not null
            ? GetByNoImpl(woNo, ct)
            : throw new NotImplementedException("GetByNoImpl not set by test");
}
