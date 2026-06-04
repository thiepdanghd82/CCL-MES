using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.QcSpecs;
using CCL.MES.Shared.Specs;
using CCL.MES.Shared.WorkOrders;

namespace CCL.MES.Hybrid.Client;

/// <summary>
/// Typed wrappers over the P10.1 CCL.MES.Api endpoints the P10.2 pilot
/// needs. Read-only — write paths land as later phases surface demand
/// (Q4 lock: stateful mutation stays online-only).
///
/// All methods can throw <see cref="HttpRequestException"/> on network
/// failure or <see cref="ApiException"/> when the server returns a
/// non-2xx with an <see cref="ApiError"/> body. Callers wrap in a UI
/// resilience layer (see ConnectivityMonitor + the NpiGrid page).
/// </summary>
public interface ICclApiClient
{
    // ── Auth ────────────────────────────────────────────────────────
    Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<UserInfo> GetMeAsync(CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    // ── NPI (pilot scope) ──────────────────────────────────────────
    Task<NpiPagedRaw<NpiWorkCenter>> GetWorkCentersAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiPagedRaw<NpiRawMaterial>> GetRawMaterialsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiPagedRaw<NpiRoutingOperation>> GetRoutingsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiPagedRaw<NpiStructure>> GetStructuresAsync(string? search, int page, int pageSize, CancellationToken ct = default);

    // ── Work Orders (P10.3 W4 — scan→accept) ──────────────────────
    /// <summary>Lookup WO by number. Returns null on 404; throws <see cref="ApiException"/>
    /// on any other non-2xx so the caller can show error UI.</summary>
    Task<WorkOrderSummary?> GetWorkOrderByNoAsync(string woNo, CancellationToken ct = default);

    /// <summary>Advance the WO via its existing state machine. Always returns
    /// a response object even when the domain guard rejects the move; the
    /// caller renders <see cref="AdvanceWorkOrderResponse.ErrorCode"/>.
    /// Throws <see cref="ApiException"/> on auth failure or genuine 404.</summary>
    Task<AdvanceWorkOrderResponse> AdvanceWorkOrderAsync(long workOrderId, CancellationToken ct = default);

    // ── Devices (P10.3 W4 — kiosk surface) ────────────────────────
    Task<ScanLogResponse> LogScanAsync(ScanLogRequest req, CancellationToken ct = default);
    Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest req, CancellationToken ct = default);
    /// <summary>Returns null on 404 (device never connected); throws on other errors.</summary>
    Task<DeviceInfoResponse?> GetDeviceInfoAsync(CancellationToken ct = default);

    // ── Specs (P10.5b — read-only; mutations land in P10.5c+) ─────
    /// <summary>Paged Spec list. View defaults to Active server-side.
    /// `view` accepts "active" / "trash" / "all" — case-insensitive
    /// per the legacy `SpecListView` enum.</summary>
    Task<NpiPagedRaw<SpecListItem>> GetSpecsAsync(string? search, int page, int pageSize, string? view, CancellationToken ct = default);

    /// <summary>Returns the full <see cref="SpecDetailItem"/> for a
    /// revision id. Null on 404 (mirrors Spec list view returning
    /// 404 for trashed-but-active-only views).</summary>
    Task<SpecDetailItem?> GetSpecDetailAsync(long revisionId, CancellationToken ct = default);

    /// <summary>Product dropdown used by the Create Spec modal.</summary>
    Task<List<SpecProductDropdownItem>> GetSpecProductsAsync(CancellationToken ct = default);

    // ── Spec mutations (P10.5c-1) ─────────────────────────────────
    /// <summary>Create a fresh Draft spec. Throws <see cref="ApiException"/>
    /// on duplicate code / validation error; the body carries
    /// <see cref="SpecMutationError"/> for VN mapping.</summary>
    Task<SpecMutationResponse> CreateSpecAsync(CreateSpecMutation req, CancellationToken ct = default);

    /// <summary>Approve a Draft → Approved. Throws on 404.</summary>
    Task<SpecMutationResponse> ApproveSpecAsync(long revisionId, CancellationToken ct = default);

    /// <summary>Copy a source spec into a new Draft revision.</summary>
    Task<SpecMutationResponse> CopySpecAsync(long sourceRevisionId, CopySpecMutation req, CancellationToken ct = default);

    /// <summary>Revise an Approved/Released → new Draft + parent lineage.
    /// Reason ≥5 chars enforced server-side; client UI validates first.</summary>
    Task<SpecMutationResponse> ReviseSpecAsync(long sourceRevisionId, ReviseSpecMutation req, CancellationToken ct = default);

    /// <summary>Mark Approved/Released → Superseded. Operator must type the
    /// SpecCode to confirm; server-side validates the typed value.</summary>
    Task<SpecMutationResponse> SupersedeSpecAsync(long revisionId, SupersedeSpecMutation req, CancellationToken ct = default);

    /// <summary>Soft-delete (move to Trash). Blocked when an active WO
    /// references the spec; the 422 body carries <see cref="SpecMutationError.ActiveWoCount"/>.</summary>
    Task<SpecMutationResponse> TrashSpecAsync(long revisionId, CancellationToken ct = default);

    /// <summary>Un-trash. 422 when the spec is not in Trash.</summary>
    Task<SpecMutationResponse> RestoreSpecAsync(long revisionId, CancellationToken ct = default);

    /// <summary>Edit fields on a Draft spec. 422 with code=immutable_status
    /// when the rev has moved past Draft.</summary>
    Task<SpecMutationResponse> UpdateSpecAsync(long revisionId, UpdateSpecMutation req, CancellationToken ct = default);

    // ── Spec import (P10.5c-2) ────────────────────────────────────
    /// <summary>Multipart upload of an xlsx Spec file. Streams the
    /// <paramref name="content"/> directly into the request body — does
    /// NOT buffer in memory (Lesson D-5b). The server parses + returns
    /// preview shape with dup info. The opaque <c>ParsedJson</c> in
    /// the response is echoed back on save.</summary>
    Task<SpecImportPreviewResponse> ImportPreviewSpecAsync(
        Stream content, string fileName, string plannerCategory, CancellationToken ct = default);

    /// <summary>Apply the operator-chosen save mode to a previously-
    /// previewed spec import. <paramref name="req"/> echoes the opaque
    /// payload + carries the dup-handling decision.</summary>
    Task<SpecImportSaveResponse> ImportSaveSpecAsync(
        SpecImportSaveRequest req, CancellationToken ct = default);

    // ── Drawings (P10.5b — read) ──────────────────────────────────
    /// <summary>9-slot drawing layout per revision. Empty list when the
    /// revision is unknown (server returns []; we return [] not null).</summary>
    Task<List<DrawingKindSlot>> GetDrawingsByRevisionAsync(long revisionId, CancellationToken ct = default);

    // ── QC Specs (P10.5b — read) ──────────────────────────────────
    /// <summary>QC windows keyed by stage. Server returns the legacy
    /// <c>Dictionary&lt;QcStage, SpecQcWindow?&gt;</c> — we expose as
    /// <c>Dictionary&lt;string, QcWindowItem?&gt;</c> so the QC Plans tab
    /// can render the 4 stage cards independently.</summary>
    Task<Dictionary<string, QcWindowItem?>> GetQcWindowsByRevisionAsync(long revisionId, CancellationToken ct = default);

    /// <summary>Flat list of captures across all stages of the revision.
    /// QC Capture tab groups client-side by <see cref="QcCaptureItem.SpecQcWindowId"/>.</summary>
    Task<List<QcCaptureItem>> GetQcCapturesByRevisionAsync(long revisionId, CancellationToken ct = default);

    /// <summary>NG reason-code master list. Cached locally per session.</summary>
    Task<List<QcReasonCode>> GetQcReasonCodesAsync(CancellationToken ct = default);
}

/// <summary>
/// Thrown when the API responds with a non-success status and we managed
/// to parse the standardised <see cref="ApiError"/> body. Carries the
/// status code so retry-able cases (5xx) can be distinguished from
/// permanent (4xx) ones.
/// </summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiError ApiError { get; }

    public ApiException(int statusCode, ApiError error)
        : base($"API returned {statusCode}: {error.Code} — {error.MessageEn}")
    {
        StatusCode = statusCode;
        ApiError = error;
    }
}

/// <summary>Paged response envelope mirroring the legacy
/// <c>PagedResult&lt;T&gt;</c> from the Application layer. Lives in the
/// client lib so the MAUI shell doesn't depend on CCL.MES.Domain.</summary>
public sealed record NpiPagedRaw<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

// Full NPI row DTOs (NpiWorkCenter / NpiRawMaterial / NpiRoutingOperation /
// NpiStructure) moved to CCL.MES.Hybrid.Client.Npi.NpiDtos.cs in P10.5a so
// the grid pages can lean on the full column shape without bloating this
// file. They were extended from the P10.2 pilot subset to match the
// Phase 7 entity expansions (28 / 20 / 16 / 6 cols respectively).
