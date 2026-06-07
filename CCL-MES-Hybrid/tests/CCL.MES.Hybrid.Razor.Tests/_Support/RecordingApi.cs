using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared.Accounts;
using CCL.MES.Shared.Audit;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Backup;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.Prepress;
using CCL.MES.Shared.RunningSurface;
using CCL.MES.Shared.QcSpecs;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.Settings;
using CCL.MES.Shared.Specs;
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
    public Task<NpiPagedRaw<NpiWorkCenter>> GetWorkCentersAsync(string? s, int p, int z, CancellationToken c = default) => throw new NotImplementedException();
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
    public Task<List<BackupSnapshotDto>> ListBackupsAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<BackupSnapshotDto> CreateBackupAsync(CancellationToken c = default) => throw new NotImplementedException();
    public Task<RestoreResultDto> RestoreBackupAsync(Stream a, string b, CancellationToken c = default) => throw new NotImplementedException();
}
