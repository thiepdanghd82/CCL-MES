using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared.Accounts;
using CCL.MES.Shared.Audit;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Backup;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Home;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.Machines;
using CCL.MES.Shared.Prepress;
using CCL.MES.Shared.Qms;
using CCL.MES.Shared.RunningSurface;
using CCL.MES.Shared.QcSpecs;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.Settings;
using CCL.MES.Shared.Specs;
using CCL.MES.Shared.WoQcReview;
using CCL.MES.Shared.WorkOrders;

namespace CCL.MES.Hybrid.Razor.Tests._Support;

/// <summary>
/// P10.7a-1.4 — records every API call the WorkOrders Razor page
/// makes so bUnit can assert wiring without booting MAUI. Methods
/// the page doesn't touch throw <see cref="NotImplementedException"/>
/// so an accidental future dependency surfaces loud.
/// </summary>
public sealed class RecordingApi : ICclApiClient
{
    public Func<string, CancellationToken, Task<WorkOrderSummary?>>? SummaryImpl { get; set; }
    public Func<long, string, CancellationToken, Task<AdvanceWorkOrderResponse>>? AdvanceImpl { get; set; }
    public Func<long, CancellationToken, Task<PrepressView>>? PrepressViewImpl { get; set; }
    public Func<long, int, string, SetPrepressMaterialRequest, CancellationToken, Task<PrepressSetResponse>>? PutPrepressMaterialImpl { get; set; }
    public Func<long, string, SetPrepressPlateRequest, CancellationToken, Task<PrepressSetResponse>>? PutPrepressPlateImpl { get; set; }
    public Func<long, string, SetPrepressCutterRequest, CancellationToken, Task<PrepressSetResponse>>? PutPrepressCutterImpl { get; set; }
    public Func<string?, CancellationToken, Task<IReadOnlyList<ReasonCodeOption>>>? ReasonCodesImpl { get; set; }

    // P10.7c-3 — Running Surface hooks. Each defaults to throwing when
    // unset so any uncovered call surfaces loud in CI.
    public Func<long, CancellationToken, Task<RunningSurfaceView>>? RunningSurfaceViewImpl { get; set; }
    public Func<long, string, CancellationToken, Task<RunningSurfaceSetResponse>>? SettingEnterImpl { get; set; }
    public Func<long, string, CancellationToken, Task<RunningSurfaceSetResponse>>? SettingDoneImpl { get; set; }
    public Func<long, string, CancellationToken, Task<RunningSurfaceSetResponse>>? RunStartImpl { get; set; }
    public Func<long, string, RunQtyAddRequest, CancellationToken, Task<RunningSurfaceSetResponse>>? RunQtyAddImpl { get; set; }
    public Func<long, string, RunQtyCorrectRequest, CancellationToken, Task<RunningSurfaceSetResponse>>? RunQtyCorrectImpl { get; set; }
    public Func<long, string, RunPauseRequest, CancellationToken, Task<RunningSurfaceSetResponse>>? RunPauseImpl { get; set; }
    public Func<long, string, CancellationToken, Task<RunningSurfaceSetResponse>>? RunResumeImpl { get; set; }
    public Func<long, string, CancellationToken, Task<RunningSurfaceSetResponse>>? RunFinishImpl { get; set; }

    // P10.7d-3 — IPQC + QA Approval hooks.
    public Func<long, CancellationToken, Task<IpqcView>>? IpqcViewImpl { get; set; }
    public Func<long, string, SetIpqcSlotRequest, CancellationToken, Task<IpqcSetResponse>>? PutIpqcMaterialImpl { get; set; }
    public Func<long, string, SetIpqcSlotRequest, CancellationToken, Task<IpqcSetResponse>>? PutIpqcPrintAImpl { get; set; }
    public Func<long, string, SetIpqcSlotRequest, CancellationToken, Task<IpqcSetResponse>>? PutIpqcPrintBImpl { get; set; }
    public Func<long, string, SetIpqcSlotRequest, CancellationToken, Task<IpqcSetResponse>>? PutIpqcPrintCImpl { get; set; }
    public Func<long, string, SubmitIpqcJudgmentRequest, CancellationToken, Task<IpqcSetResponse>>? PostIpqcJudgmentImpl { get; set; }
    public Func<long, string, QaApproveRequest, CancellationToken, Task<IpqcSetResponse>>? PostQaApproveImpl { get; set; }

    public List<long> IpqcViewCalls { get; } = new();
    public List<(long Id, string ETag, SetIpqcSlotRequest Req)> PutIpqcMaterialCalls { get; } = new();
    public List<(long Id, string ETag, SetIpqcSlotRequest Req)> PutIpqcPrintACalls { get; } = new();
    public List<(long Id, string ETag, SetIpqcSlotRequest Req)> PutIpqcPrintBCalls { get; } = new();
    public List<(long Id, string ETag, SetIpqcSlotRequest Req)> PutIpqcPrintCCalls { get; } = new();
    public List<(long Id, string ETag, SubmitIpqcJudgmentRequest Req)> PostIpqcJudgmentCalls { get; } = new();
    public List<(long Id, string ETag, QaApproveRequest Req)> PostQaApproveCalls { get; } = new();

    public List<string> SummaryCalls { get; } = new();
    public List<(long Id, string ETag)> AdvanceCalls { get; } = new();
    public List<ScanLogRequest> ScanLogCalls { get; } = new();
    public List<long> PrepressViewCalls { get; } = new();
    public List<(long Id, int BomLineIdx, string ETag, SetPrepressMaterialRequest Req)> PutPrepressMaterialCalls { get; } = new();
    public List<(long Id, string ETag, SetPrepressPlateRequest Req)> PutPrepressPlateCalls { get; } = new();
    public List<(long Id, string ETag, SetPrepressCutterRequest Req)> PutPrepressCutterCalls { get; } = new();
    public List<string?> ReasonCodesCalls { get; } = new();

    public List<long> RunningSurfaceViewCalls { get; } = new();
    public List<(long Id, string ETag)> SettingEnterCalls { get; } = new();
    public List<(long Id, string ETag)> SettingDoneCalls { get; } = new();
    public List<(long Id, string ETag)> RunStartCalls { get; } = new();
    public List<(long Id, string ETag, RunQtyAddRequest Req)> RunQtyAddCalls { get; } = new();
    public List<(long Id, string ETag, RunQtyCorrectRequest Req)> RunQtyCorrectCalls { get; } = new();
    public List<(long Id, string ETag, RunPauseRequest Req)> RunPauseCalls { get; } = new();
    public List<(long Id, string ETag)> RunResumeCalls { get; } = new();
    public List<(long Id, string ETag)> RunFinishCalls { get; } = new();

    // P10.10 — Home summary. Defaults to a zero-count DTO so pages that
    // fetch it on init render the "—"/0 tiles without a bespoke stub.
    public HomeSummaryDto? HomeSummary { get; set; } = new HomeSummaryDto();
    public int HomeSummaryCalls { get; private set; }

    public Task<HomeSummaryDto?> GetHomeSummaryAsync(CancellationToken ct = default)
    {
        HomeSummaryCalls++;
        return Task.FromResult(HomeSummary);
    }

    // P10.5 — NPI CSV import. Records (kind, fileName) of each call.
    public CCL.MES.Hybrid.Client.Npi.NpiImportResultDto? NpiImport { get; set; }
        = new CCL.MES.Hybrid.Client.Npi.NpiImportResultDto { Kind = "structures", Inserted = 0, Skipped = 0 };
    public List<(string Kind, string FileName)> NpiImportCalls { get; } = new();

    public Task<CCL.MES.Hybrid.Client.Npi.NpiImportResultDto?> ImportNpiAsync(string kind, string fileName, byte[] content, CancellationToken ct = default)
    {
        NpiImportCalls.Add((kind, fileName));
        return Task.FromResult(NpiImport);
    }

    // WorkCenter write surface — injectable impls (default: echo / empty).
    public Func<CCL.MES.Hybrid.Client.Npi.NpiWorkCenterUpsert, CCL.MES.Hybrid.Client.Npi.NpiWorkCenter>? CreateWorkCenterImpl { get; set; }
    public Func<long, CCL.MES.Hybrid.Client.Npi.NpiWorkCenterUpsert, CCL.MES.Hybrid.Client.Npi.NpiWorkCenter>? UpdateWorkCenterImpl { get; set; }
    public List<long> DeleteWorkCenterCalls { get; } = new();
    public Func<string, byte[], CCL.MES.Hybrid.Client.Npi.NpiWorkCenterImportReport>? ImportWorkCentersImpl { get; set; }
    public Func<string?, string>? ExportWorkCentersImpl { get; set; }

    public Task<CCL.MES.Hybrid.Client.Npi.NpiWorkCenter> CreateWorkCenterAsync(CCL.MES.Hybrid.Client.Npi.NpiWorkCenterUpsert body, CancellationToken ct = default)
        => Task.FromResult(CreateWorkCenterImpl?.Invoke(body)
            ?? new CCL.MES.Hybrid.Client.Npi.NpiWorkCenter { Id = 1, Code = body.Code, Description = body.Description, Area = body.Area, IdealSpeedPcsH = body.IdealSpeedPcsH, ShiftPattern = body.ShiftPattern, Active = body.Active ?? true });

    public Task<CCL.MES.Hybrid.Client.Npi.NpiWorkCenter> UpdateWorkCenterAsync(long id, CCL.MES.Hybrid.Client.Npi.NpiWorkCenterUpsert body, CancellationToken ct = default)
        => Task.FromResult(UpdateWorkCenterImpl?.Invoke(id, body)
            ?? new CCL.MES.Hybrid.Client.Npi.NpiWorkCenter { Id = id, Code = body.Code, Description = body.Description, Area = body.Area, IdealSpeedPcsH = body.IdealSpeedPcsH, ShiftPattern = body.ShiftPattern, Active = body.Active ?? true });

    public Task DeleteWorkCenterAsync(long id, CancellationToken ct = default)
    {
        DeleteWorkCenterCalls.Add(id);
        return Task.CompletedTask;
    }

    public Task<CCL.MES.Hybrid.Client.Npi.NpiWorkCenterImportReport> ImportWorkCentersAsync(string fileName, byte[] content, CancellationToken ct = default)
        => Task.FromResult(ImportWorkCentersImpl?.Invoke(fileName, content)
            ?? new CCL.MES.Hybrid.Client.Npi.NpiWorkCenterImportReport());

    public Task<string> ExportWorkCentersCsvAsync(string? search, CancellationToken ct = default)
        => Task.FromResult(ExportWorkCentersImpl?.Invoke(search) ?? "");

    // P10.8 — Machine Dashboard. Defaults to an empty board.
    public MachineDashboardDto? MachineDashboard { get; set; } = new MachineDashboardDto();
    public int MachineDashboardCalls { get; private set; }

    public Task<MachineDashboardDto?> GetMachineDashboardAsync(CancellationToken ct = default)
    {
        MachineDashboardCalls++;
        return Task.FromResult(MachineDashboard);
    }

    // P10.8 slice 3 — per-machine detail. Settable per test; records the
    // requested work-center id.
    public MachineDetailDto? MachineDetail { get; set; }
    public List<long> MachineDetailCalls { get; } = new();

    public Task<MachineDetailDto?> GetMachineDetailAsync(long workCenterId, CancellationToken ct = default)
    {
        MachineDetailCalls.Add(workCenterId);
        return Task.FromResult(MachineDetail);
    }

    // P10.8 — Shop Order History. Records the filters of each call.
    public ShopOrderHistoryDto? ShopOrderHistory { get; set; } = new ShopOrderHistoryDto();
    public List<(string? Period, string? Search, string? Status, string? Customer, string? Machine)> ShopOrderHistoryCalls { get; } = new();

    public Task<ShopOrderHistoryDto?> GetShopOrderHistoryAsync(string? period, string? search,
        string? status = null, string? customer = null, string? machine = null, CancellationToken ct = default)
    {
        ShopOrderHistoryCalls.Add((period, search, status, customer, machine));
        return Task.FromResult(ShopOrderHistory);
    }

    // P10.9 — QMS Inspection Queue.
    public QmsQueueDto? QmsQueue { get; set; } = new QmsQueueDto();
    public int QmsQueueCalls { get; private set; }

    public Task<QmsQueueDto?> GetQmsQueueAsync(CancellationToken ct = default)
    {
        QmsQueueCalls++;
        return Task.FromResult(QmsQueue);
    }

    // P10.9 — QC History. Records (kind, judgment, search) of each call.
    public QcHistoryDto? QcHistory { get; set; } = new QcHistoryDto();
    public List<(string? Kind, string? Judgment, string? Search)> QcHistoryCalls { get; } = new();

    public Task<QcHistoryDto?> GetQcHistoryAsync(string? kind, string? judgment, string? search, CancellationToken ct = default)
    {
        QcHistoryCalls.Add((kind, judgment, search));
        return Task.FromResult(QcHistory);
    }

    public IReadOnlyList<ActiveWorkOrderCard> ActiveWorkOrders { get; set; } = System.Array.Empty<ActiveWorkOrderCard>();
    public Task<IReadOnlyList<ActiveWorkOrderCard>> GetActiveWorkOrdersAsync(CancellationToken ct = default)
        => Task.FromResult(ActiveWorkOrders);

    public IReadOnlyList<WoAuditEntry> WoAudit { get; set; } = System.Array.Empty<WoAuditEntry>();
    public Task<IReadOnlyList<WoAuditEntry>> GetWoAuditAsync(long workOrderId, CancellationToken ct = default)
        => Task.FromResult(WoAudit);

    public Task<WorkOrderSummary?> GetWorkOrderByNoAsync(string woNo, CancellationToken ct = default)
    {
        SummaryCalls.Add(woNo);
        return SummaryImpl is null
            ? Task.FromResult<WorkOrderSummary?>(null)
            : SummaryImpl(woNo, ct);
    }

    public Task<AdvanceWorkOrderResponse> AdvanceWorkOrderAsync(long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        AdvanceCalls.Add((workOrderId, ifMatchETag));
        return AdvanceImpl is null
            ? throw new InvalidOperationException("AdvanceImpl not set")
            : AdvanceImpl(workOrderId, ifMatchETag, ct);
    }

    public Task<PrepressView> GetPrepressViewAsync(long workOrderId, CancellationToken ct = default)
    {
        PrepressViewCalls.Add(workOrderId);
        return PrepressViewImpl is null
            ? throw new InvalidOperationException("PrepressViewImpl not set")
            : PrepressViewImpl(workOrderId, ct);
    }

    public Task<PrepressSetResponse> PutPrepressMaterialAsync(
        long workOrderId, int bomLineIdx, string ifMatchETag,
        SetPrepressMaterialRequest req, CancellationToken ct = default)
    {
        PutPrepressMaterialCalls.Add((workOrderId, bomLineIdx, ifMatchETag, req));
        return PutPrepressMaterialImpl is null
            ? throw new InvalidOperationException("PutPrepressMaterialImpl not set")
            : PutPrepressMaterialImpl(workOrderId, bomLineIdx, ifMatchETag, req, ct);
    }

    public Task<PrepressSetResponse> PutPrepressPlateAsync(
        long workOrderId, string ifMatchETag,
        SetPrepressPlateRequest req, CancellationToken ct = default)
    {
        PutPrepressPlateCalls.Add((workOrderId, ifMatchETag, req));
        return PutPrepressPlateImpl is null
            ? throw new InvalidOperationException("PutPrepressPlateImpl not set")
            : PutPrepressPlateImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<PrepressSetResponse> PutPrepressCutterAsync(
        long workOrderId, string ifMatchETag,
        SetPrepressCutterRequest req, CancellationToken ct = default)
    {
        PutPrepressCutterCalls.Add((workOrderId, ifMatchETag, req));
        return PutPrepressCutterImpl is null
            ? throw new InvalidOperationException("PutPrepressCutterImpl not set")
            : PutPrepressCutterImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Func<long, int, string, SpecialAcceptMaterialRequest, CancellationToken, Task<PrepressSetResponse>>? SpecialAcceptMaterialImpl { get; set; }
    public List<(long Id, int BomLineIdx, string ETag, SpecialAcceptMaterialRequest Req)> SpecialAcceptMaterialCalls { get; } = new();

    public Task<PrepressSetResponse> SpecialAcceptMaterialAsync(
        long workOrderId, int bomLineIdx, string ifMatchETag,
        SpecialAcceptMaterialRequest req, CancellationToken ct = default)
    {
        SpecialAcceptMaterialCalls.Add((workOrderId, bomLineIdx, ifMatchETag, req));
        return SpecialAcceptMaterialImpl is null
            ? Task.FromResult(new PrepressSetResponse { Ok = true, ETag = "special==" })
            : SpecialAcceptMaterialImpl(workOrderId, bomLineIdx, ifMatchETag, req, ct);
    }

    public Task<IReadOnlyList<ReasonCodeOption>> GetReasonCodesAsync(string? kind, CancellationToken ct = default)
    {
        ReasonCodesCalls.Add(kind);
        return ReasonCodesImpl is null
            ? Task.FromResult<IReadOnlyList<ReasonCodeOption>>(Array.Empty<ReasonCodeOption>())
            : ReasonCodesImpl(kind, ct);
    }

    public Task<RunningSurfaceView> GetRunningSurfaceViewAsync(long workOrderId, CancellationToken ct = default)
    {
        RunningSurfaceViewCalls.Add(workOrderId);
        return RunningSurfaceViewImpl is null
            ? throw new InvalidOperationException("RunningSurfaceViewImpl not set")
            : RunningSurfaceViewImpl(workOrderId, ct);
    }

    public Task<RunningSurfaceSetResponse> PostSettingEnterAsync(long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        SettingEnterCalls.Add((workOrderId, ifMatchETag));
        return SettingEnterImpl is null
            ? throw new InvalidOperationException("SettingEnterImpl not set")
            : SettingEnterImpl(workOrderId, ifMatchETag, ct);
    }

    public Task<RunningSurfaceSetResponse> PostSettingDoneAsync(long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        SettingDoneCalls.Add((workOrderId, ifMatchETag));
        return SettingDoneImpl is null
            ? throw new InvalidOperationException("SettingDoneImpl not set")
            : SettingDoneImpl(workOrderId, ifMatchETag, ct);
    }

    public Task<RunningSurfaceSetResponse> PostRunStartAsync(long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        RunStartCalls.Add((workOrderId, ifMatchETag));
        return RunStartImpl is null
            ? throw new InvalidOperationException("RunStartImpl not set")
            : RunStartImpl(workOrderId, ifMatchETag, ct);
    }

    public Task<RunningSurfaceSetResponse> PostRunQtyAddAsync(long workOrderId, string ifMatchETag, RunQtyAddRequest req, CancellationToken ct = default)
    {
        RunQtyAddCalls.Add((workOrderId, ifMatchETag, req));
        return RunQtyAddImpl is null
            ? throw new InvalidOperationException("RunQtyAddImpl not set")
            : RunQtyAddImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<RunningSurfaceSetResponse> PostRunQtyCorrectAsync(long workOrderId, string ifMatchETag, RunQtyCorrectRequest req, CancellationToken ct = default)
    {
        RunQtyCorrectCalls.Add((workOrderId, ifMatchETag, req));
        return RunQtyCorrectImpl is null
            ? throw new InvalidOperationException("RunQtyCorrectImpl not set")
            : RunQtyCorrectImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<RunningSurfaceSetResponse> PostRunPauseAsync(long workOrderId, string ifMatchETag, RunPauseRequest req, CancellationToken ct = default)
    {
        RunPauseCalls.Add((workOrderId, ifMatchETag, req));
        return RunPauseImpl is null
            ? throw new InvalidOperationException("RunPauseImpl not set")
            : RunPauseImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<RunningSurfaceSetResponse> PostRunResumeAsync(long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        RunResumeCalls.Add((workOrderId, ifMatchETag));
        return RunResumeImpl is null
            ? throw new InvalidOperationException("RunResumeImpl not set")
            : RunResumeImpl(workOrderId, ifMatchETag, ct);
    }

    public Task<RunningSurfaceSetResponse> PostRunFinishAsync(long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        RunFinishCalls.Add((workOrderId, ifMatchETag));
        return RunFinishImpl is null
            ? throw new InvalidOperationException("RunFinishImpl not set")
            : RunFinishImpl(workOrderId, ifMatchETag, ct);
    }

    // ── IPQC + QA Approval (P10.7d-3) ──────────────────────────────

    public Task<IpqcView> GetIpqcViewAsync(long workOrderId, CancellationToken ct = default)
    {
        IpqcViewCalls.Add(workOrderId);
        return IpqcViewImpl is null
            ? throw new InvalidOperationException("IpqcViewImpl not set")
            : IpqcViewImpl(workOrderId, ct);
    }

    public Task<IpqcSetResponse> PutIpqcMaterialAsync(long workOrderId, string ifMatchETag, SetIpqcSlotRequest req, CancellationToken ct = default)
    {
        PutIpqcMaterialCalls.Add((workOrderId, ifMatchETag, req));
        return PutIpqcMaterialImpl is null
            ? throw new InvalidOperationException("PutIpqcMaterialImpl not set")
            : PutIpqcMaterialImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<IpqcSetResponse> PutIpqcPrintAAsync(long workOrderId, string ifMatchETag, SetIpqcSlotRequest req, CancellationToken ct = default)
    {
        PutIpqcPrintACalls.Add((workOrderId, ifMatchETag, req));
        return PutIpqcPrintAImpl is null
            ? throw new InvalidOperationException("PutIpqcPrintAImpl not set")
            : PutIpqcPrintAImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<IpqcSetResponse> PutIpqcPrintBAsync(long workOrderId, string ifMatchETag, SetIpqcSlotRequest req, CancellationToken ct = default)
    {
        PutIpqcPrintBCalls.Add((workOrderId, ifMatchETag, req));
        return PutIpqcPrintBImpl is null
            ? throw new InvalidOperationException("PutIpqcPrintBImpl not set")
            : PutIpqcPrintBImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<IpqcSetResponse> PutIpqcPrintCAsync(long workOrderId, string ifMatchETag, SetIpqcSlotRequest req, CancellationToken ct = default)
    {
        PutIpqcPrintCCalls.Add((workOrderId, ifMatchETag, req));
        return PutIpqcPrintCImpl is null
            ? throw new InvalidOperationException("PutIpqcPrintCImpl not set")
            : PutIpqcPrintCImpl(workOrderId, ifMatchETag, req, ct);
    }

    // Phương án C — Bước 6: thư viện read.
    public Func<string?, string?, string?, CancellationToken, Task<IReadOnlyList<CCL.MES.Shared.CheckLibrary.CheckLibraryItemDto>>>? GetCheckLibraryImpl { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<CCL.MES.Shared.CheckLibrary.CheckLibraryLineDto>>>? GetCheckLibraryLinesImpl { get; set; }

    public Task<IReadOnlyList<CCL.MES.Shared.CheckLibrary.CheckLibraryItemDto>> GetCheckLibraryAsync(
        string? line = null, string? stage = null, string? q = null, CancellationToken ct = default)
        => GetCheckLibraryImpl is null
            ? Task.FromResult<IReadOnlyList<CCL.MES.Shared.CheckLibrary.CheckLibraryItemDto>>(Array.Empty<CCL.MES.Shared.CheckLibrary.CheckLibraryItemDto>())
            : GetCheckLibraryImpl(line, stage, q, ct);

    public Task<IReadOnlyList<CCL.MES.Shared.CheckLibrary.CheckLibraryLineDto>> GetCheckLibraryLinesAsync(
        CancellationToken ct = default)
        => GetCheckLibraryLinesImpl is null
            ? Task.FromResult<IReadOnlyList<CCL.MES.Shared.CheckLibrary.CheckLibraryLineDto>>(Array.Empty<CCL.MES.Shared.CheckLibrary.CheckLibraryLineDto>())
            : GetCheckLibraryLinesImpl(ct);

    // Phương án C — Bước 4: item-level IPQC PUT (data-driven).
    public Func<long, string, string, SetIpqcItemRequest, CancellationToken, Task<IpqcSetResponse>>? PutIpqcItemImpl { get; set; }
    public List<(long Id, string ETag, string ItemKey, SetIpqcItemRequest Req)> PutIpqcItemCalls { get; } = new();

    public Task<IpqcSetResponse> PutIpqcItemAsync(long workOrderId, string ifMatchETag, string itemKey, SetIpqcItemRequest req, CancellationToken ct = default)
    {
        PutIpqcItemCalls.Add((workOrderId, ifMatchETag, itemKey, req));
        return PutIpqcItemImpl is null
            ? Task.FromResult(new IpqcSetResponse { Ok = true, ETag = ifMatchETag, MesPhase = "IPQC_WAIT" })
            : PutIpqcItemImpl(workOrderId, ifMatchETag, itemKey, req, ct);
    }

    public Task<IpqcSetResponse> PostIpqcJudgmentAsync(long workOrderId, string ifMatchETag, SubmitIpqcJudgmentRequest req, CancellationToken ct = default)
    {
        PostIpqcJudgmentCalls.Add((workOrderId, ifMatchETag, req));
        return PostIpqcJudgmentImpl is null
            ? throw new InvalidOperationException("PostIpqcJudgmentImpl not set")
            : PostIpqcJudgmentImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<IpqcSetResponse> PostQaApproveAsync(long workOrderId, string ifMatchETag, QaApproveRequest req, CancellationToken ct = default)
    {
        PostQaApproveCalls.Add((workOrderId, ifMatchETag, req));
        return PostQaApproveImpl is null
            ? throw new InvalidOperationException("PostQaApproveImpl not set")
            : PostQaApproveImpl(workOrderId, ifMatchETag, req, ct);
    }

    // ── P10.7e-3 — WoQc (FQC + OQC + photo + summary) hooks ───────
    public Func<long, string, CancellationToken, Task<WoQcView>>? WoQcViewImpl { get; set; }
    public Func<long, string, string, string, SetWoQcItemRequest, CancellationToken, Task<WoQcSetResponse>>? PutWoQcItemImpl { get; set; }
    public Func<long, string, SubmitFqcJudgmentRequest, CancellationToken, Task<WoQcSetResponse>>? PostFqcJudgmentImpl { get; set; }
    public Func<long, string, OqcInspectRequest, CancellationToken, Task<WoQcSetResponse>>? PostOqcInspectImpl { get; set; }
    public Func<long, string, OqcReviewRequest, CancellationToken, Task<WoQcSetResponse>>? PostOqcReviewImpl { get; set; }
    public Func<long, string, OqcApproveRequest, CancellationToken, Task<WoQcSetResponse>>? PostOqcApproveImpl { get; set; }
    public Func<long, string, string, CancellationToken, Task<IReadOnlyList<WoQcPhotoDto>>>? WoQcPhotosImpl { get; set; }
    public Func<long, string, string, string, Stream, string, string, CancellationToken, Task<WoQcPhotoUploadResponse>>? UploadWoQcPhotoImpl { get; set; }
    public Func<long, string, string, long, string, CancellationToken, Task<WoQcSetResponse>>? DeleteWoQcPhotoImpl { get; set; }
    public Func<long, CancellationToken, Task<WoSummaryReport>>? WoSummaryReportImpl { get; set; }
    public Func<string?, int, int, CancellationToken, Task<NpiPagedRaw<CCL.MES.Shared.Quality.TraceListRow>>>? TraceabilityImpl { get; set; }
    public List<(string? Search, int Page, int PageSize)> TraceabilityCalls { get; } = new();
    public Func<string, CancellationToken, Task<CCL.MES.Shared.Quality.TraceabilityDetailDto>>? TraceabilityDetailImpl { get; set; }
    public List<string> TraceabilityDetailCalls { get; } = new();

    public List<(long Id, string Kind)> WoQcViewCalls { get; } = new();
    public List<(long Id, string Kind, string ItemKey, string ETag, SetWoQcItemRequest Req)> PutWoQcItemCalls { get; } = new();
    public List<(long Id, string ETag, SubmitFqcJudgmentRequest Req)> PostFqcJudgmentCalls { get; } = new();
    public List<(long Id, string ETag)> PostOqcInspectCalls { get; } = new();
    public List<(long Id, string ETag)> PostOqcReviewCalls { get; } = new();
    public List<(long Id, string ETag, OqcApproveRequest Req)> PostOqcApproveCalls { get; } = new();
    public List<long> WoSummaryReportCalls { get; } = new();

    public Task<WoQcView> GetWoQcViewAsync(long workOrderId, string kind, CancellationToken ct = default)
    {
        WoQcViewCalls.Add((workOrderId, kind));
        return WoQcViewImpl is null
            ? throw new InvalidOperationException("WoQcViewImpl not set")
            : WoQcViewImpl(workOrderId, kind, ct);
    }

    public Task<WoQcSetResponse> PutWoQcItemAsync(long workOrderId, string kind, string itemKey, string ifMatchETag, SetWoQcItemRequest req, CancellationToken ct = default)
    {
        PutWoQcItemCalls.Add((workOrderId, kind, itemKey, ifMatchETag, req));
        return PutWoQcItemImpl is null
            ? throw new InvalidOperationException("PutWoQcItemImpl not set")
            : PutWoQcItemImpl(workOrderId, kind, itemKey, ifMatchETag, req, ct);
    }

    public Task<WoQcSetResponse> PostFqcJudgmentAsync(long workOrderId, string ifMatchETag, SubmitFqcJudgmentRequest req, CancellationToken ct = default)
    {
        PostFqcJudgmentCalls.Add((workOrderId, ifMatchETag, req));
        return PostFqcJudgmentImpl is null
            ? throw new InvalidOperationException("PostFqcJudgmentImpl not set")
            : PostFqcJudgmentImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<WoQcSetResponse> PostOqcInspectAsync(long workOrderId, string ifMatchETag, OqcInspectRequest req, CancellationToken ct = default)
    {
        PostOqcInspectCalls.Add((workOrderId, ifMatchETag));
        return PostOqcInspectImpl is null
            ? throw new InvalidOperationException("PostOqcInspectImpl not set")
            : PostOqcInspectImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<WoQcSetResponse> PostOqcReviewAsync(long workOrderId, string ifMatchETag, OqcReviewRequest req, CancellationToken ct = default)
    {
        PostOqcReviewCalls.Add((workOrderId, ifMatchETag));
        return PostOqcReviewImpl is null
            ? throw new InvalidOperationException("PostOqcReviewImpl not set")
            : PostOqcReviewImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<WoQcSetResponse> PostOqcApproveAsync(long workOrderId, string ifMatchETag, OqcApproveRequest req, CancellationToken ct = default)
    {
        PostOqcApproveCalls.Add((workOrderId, ifMatchETag, req));
        return PostOqcApproveImpl is null
            ? throw new InvalidOperationException("PostOqcApproveImpl not set")
            : PostOqcApproveImpl(workOrderId, ifMatchETag, req, ct);
    }

    public Task<IReadOnlyList<WoQcPhotoDto>> GetWoQcPhotosAsync(long workOrderId, string kind, string itemKey, CancellationToken ct = default)
    {
        return WoQcPhotosImpl is null
            ? Task.FromResult<IReadOnlyList<WoQcPhotoDto>>(Array.Empty<WoQcPhotoDto>())
            : WoQcPhotosImpl(workOrderId, kind, itemKey, ct);
    }

    public Task<WoQcPhotoUploadResponse> UploadWoQcPhotoAsync(long workOrderId, string kind, string itemKey, string ifMatchETag, Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        return UploadWoQcPhotoImpl is null
            ? throw new InvalidOperationException("UploadWoQcPhotoImpl not set")
            : UploadWoQcPhotoImpl(workOrderId, kind, itemKey, ifMatchETag, content, fileName, mimeType, ct);
    }

    public Task<WoQcSetResponse> DeleteWoQcPhotoAsync(long workOrderId, string kind, string itemKey, long photoId, string ifMatchETag, CancellationToken ct = default)
    {
        return DeleteWoQcPhotoImpl is null
            ? throw new InvalidOperationException("DeleteWoQcPhotoImpl not set")
            : DeleteWoQcPhotoImpl(workOrderId, kind, itemKey, photoId, ifMatchETag, ct);
    }

    public Task<WoSummaryReport> GetWoSummaryReportAsync(long workOrderId, CancellationToken ct = default)
    {
        WoSummaryReportCalls.Add(workOrderId);
        return WoSummaryReportImpl is null
            ? throw new InvalidOperationException("WoSummaryReportImpl not set")
            : WoSummaryReportImpl(workOrderId, ct);
    }

    public Task<NpiPagedRaw<CCL.MES.Shared.Quality.TraceListRow>> GetTraceabilityAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        TraceabilityCalls.Add((search, page, pageSize));
        return TraceabilityImpl is null
            ? throw new InvalidOperationException("TraceabilityImpl not set")
            : TraceabilityImpl(search, page, pageSize, ct);
    }

    public Task<CCL.MES.Shared.Quality.TraceabilityDetailDto> GetTraceabilityDetailAsync(
        string woNo, CancellationToken ct = default)
    {
        TraceabilityDetailCalls.Add(woNo);
        return TraceabilityDetailImpl is null
            ? throw new InvalidOperationException("TraceabilityDetailImpl not set")
            : TraceabilityDetailImpl(woNo, ct);
    }

    public Task<ScanLogResponse> LogScanAsync(ScanLogRequest req, CancellationToken ct = default)
    {
        ScanLogCalls.Add(req);
        return Task.FromResult(new ScanLogResponse
        {
            ScanId = Guid.NewGuid(),
            ServerTimestamp = DateTimeOffset.UtcNow,
        });
    }

    // ── Surface the WorkOrders Razor page does NOT touch — every
    //    method throws so an accidental future dependency on Spec /
    //    Drawing / Account / Audit / Backup work from this page
    //    surfaces loud in CI rather than at the operator's tap. ──────

    public Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserInfo> GetMeAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task LogoutAsync(string refreshToken, CancellationToken ct = default) => throw new NotImplementedException();
    public NpiPagedRaw<NpiWorkCenter>? WorkCenters { get; set; }
    public Task<NpiPagedRaw<NpiWorkCenter>> GetWorkCentersAsync(string? s, int p, int z, CancellationToken c = default)
        => Task.FromResult(WorkCenters ?? new NpiPagedRaw<NpiWorkCenter> { Items = System.Array.Empty<NpiWorkCenter>(), Total = 0, Page = 1, PageSize = 20 });
    public Task<NpiPagedRaw<NpiRawMaterial>> GetRawMaterialsAsync(string? s, int p, int z, CancellationToken c = default) => throw new NotImplementedException();
    public Task<NpiPagedRaw<NpiRoutingOperation>> GetRoutingsAsync(string? s, int p, int z, CancellationToken c = default) => throw new NotImplementedException();
    public Task<NpiPagedRaw<NpiStructure>> GetStructuresAsync(string? s, int p, int z, CancellationToken c = default) => throw new NotImplementedException();
    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest req, CancellationToken c = default) => throw new NotImplementedException();
    public Task<DeviceInfoResponse?> GetDeviceInfoAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<NpiPagedRaw<SpecListItem>> GetSpecsAsync(string? s, int p, int z, string? v, string? planner = null, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecDetailItem?> GetSpecDetailAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<List<SpecProductDropdownItem>> GetSpecProductsAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> CreateSpecAsync(CreateSpecMutation r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> ApproveSpecAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> CopySpecAsync(long s, CopySpecMutation r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> DuplicateSpecAsync(long s, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> NewVersionSpecAsync(long s, CancellationToken c = default) => throw new NotImplementedException();
    public Task<byte[]> DownloadDrawingBytesAsync(long r, long v, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> ReviseSpecAsync(long s, ReviseSpecMutation r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> SupersedeSpecAsync(long r, SupersedeSpecMutation req, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> TrashSpecAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> RestoreSpecAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecMutationResponse> UpdateSpecAsync(long r, UpdateSpecMutation req, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecImportPreviewResponse> ImportPreviewSpecAsync(Stream a, string b, string c2, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SpecImportSaveResponse> ImportSaveSpecAsync(SpecImportSaveRequest req, CancellationToken c = default) => throw new NotImplementedException();
    public Task<List<DrawingKindSlot>> GetDrawingsByRevisionAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<DrawingUploadResponse> UploadDrawingAsync(long a, string b, Stream s, string n, string? r = null, CancellationToken c = default) => throw new NotImplementedException();
    public Task<long> DownloadDrawingToFileAsync(long a, long b, string c, CancellationToken d = default) => throw new NotImplementedException();
    public Task<DrawingDecideResponse> DecideDrawingAsync(long a, long b, DrawingDecideRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<DrawingDeleteResponse> DeleteDrawingVersionAsync(long a, long b, DrawingDeleteRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<Dictionary<string, QcWindowItem?>> GetQcWindowsByRevisionAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<List<QcCaptureItem>> GetQcCapturesByRevisionAsync(long r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<List<QcReasonCode>> GetQcReasonCodesAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<QcPlanUpsertResponse> UpsertQcPlanStageAsync(long r, QcPlanUpsertRequest req, CancellationToken c = default) => throw new NotImplementedException();
    public Task<QcCaptureItem> CreateQcCaptureAsync(long r, QcCaptureCreateRequest req, CancellationToken c = default) => throw new NotImplementedException();
    public Task<long> DownloadSpecListExportAsync(string a, string? b, string d, string? e, string f, CancellationToken c = default) => throw new NotImplementedException();
    public Task<long> DownloadSpecSheetPdfAsync(long a, string b, CancellationToken c = default) => throw new NotImplementedException();
    public Task<SettingsProfileDto> GetMyProfileAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<SettingsProfileDto> UpdateMyProfileAsync(UpdateProfileRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<ChangePasswordResponse> ChangeMyPasswordAsync(ChangePasswordRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<AboutDto> GetAboutAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<AuditLogPagedResult> GetAuditLogAsync(string? a, string? b, string? d, DateTime? e, DateTime? f, int g, int h, CancellationToken c = default) => throw new NotImplementedException();
    public Task<List<string>> GetAuditActionsAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<AuditLogExportDownload> ExportAuditLogAsync(string a, string? b, string? d, string? e, DateTime? f, DateTime? g, string h, CancellationToken c = default) => throw new NotImplementedException();
    public Task<AccountPagedResult> ListAccountsAsync(string? a, int b, int d, CancellationToken c = default) => throw new NotImplementedException();
    public Task<AccountDto> CreateAccountAsync(CreateAccountRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<AccountDto> UpdateAccountAsync(long a, UpdateAccountRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<AccountDto> ResetAccountPasswordAsync(long a, ResetPasswordRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<AccountDto> DeleteAccountAsync(long a, CancellationToken c = default) => throw new NotImplementedException();
    public Task<List<BackupSnapshotDto>> ListBackupsAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<BackupSnapshotDto> CreateBackupAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<RestoreResultDto> RestoreBackupAsync(Stream a, string b, CancellationToken c = default) => throw new NotImplementedException();
    public Task<BackupScheduleStatusDto> GetBackupScheduleAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<BackupScheduleStatusDto> SetBackupScheduleAsync(BackupScheduleUpdateRequest r, CancellationToken c = default) => throw new NotImplementedException();
    public Task<BackupRunResultDto> RunBackupNowAsync(CancellationToken c = default) => throw new NotImplementedException();
}
