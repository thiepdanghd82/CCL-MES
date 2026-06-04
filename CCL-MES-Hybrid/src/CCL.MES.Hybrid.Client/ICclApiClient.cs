using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.QcSpecs;
using CCL.MES.Shared.Settings;
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
    /// per the legacy `SpecListView` enum. `planner` accepts canonical
    /// planner codes (SILK / FLEXO / LETTER / INDIGO / DIECUT) and is
    /// ignored when null/empty/unknown.</summary>
    Task<NpiPagedRaw<SpecListItem>> GetSpecsAsync(string? search, int page, int pageSize, string? view, string? planner = null, CancellationToken ct = default);

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

    // ── Drawings write surface (P10.5e-1) ─────────────────────────
    /// <summary>Multipart upload of a new drawing version. Streams
    /// <paramref name="content"/> directly into the request body — no
    /// full-file buffer. Server forwards to
    /// <c>DrawingsService.UploadAsync</c> which handles find-or-create
    /// of the parent Drawing, version-number bump, blob store with the
    /// 6 security guards (extension allowlist / size cap / sha256 /
    /// path-segment / containment / atomic write), and 3-Pending
    /// approval row seed.</summary>
    Task<DrawingUploadResponse> UploadDrawingAsync(
        long revisionId, string kind, Stream content, string fileName,
        string? changeReason = null, CancellationToken ct = default);

    /// <summary>Download a drawing version blob to <paramref name="destinationFilePath"/>.
    /// Streams the response body chunk-by-chunk (no full-file buffer
    /// in memory) so a 10 MB pdf survives slow WiFi without OOM.
    /// Returns the file size persisted; throws on 404 / network /
    /// blob-missing errors.</summary>
    Task<long> DownloadDrawingToFileAsync(
        long revisionId, long versionId, string destinationFilePath,
        CancellationToken ct = default);

    /// <summary>P10.5e-2 — Decide on a 3-role approval chip (Npi /
    /// Production / Qc) for a specific version. Comment is REQUIRED
    /// server-side when <paramref name="req"/>.Decision = Rejected;
    /// the caller's <c>_submitting</c> guard prevents double-fire.
    /// On <c>drawing.department_mismatch</c> (403) the operator's
    /// claim doesn't match — the chip should have been disabled at UI
    /// gate but the server is still the source of truth.</summary>
    Task<DrawingDecideResponse> DecideDrawingAsync(
        long revisionId, long versionId, DrawingDecideRequest req,
        CancellationToken ct = default);

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

    // ── QC plan + capture write surface (P10.5f) ────────────────────
    /// <summary>Atomic per-stage QC plan upsert. Mirrors legacy
    /// <c>SpecQcWindowService.UpsertStageAsync</c>: reads current
    /// criteria, computes a delete/update/insert diff against the
    /// supplied <paramref name="req"/>.Rows, writes inside a single
    /// transaction. Server-side role gate (Admin/Engineer) — 403
    /// surfaces as <c>qc.forbidden</c>.</summary>
    Task<QcPlanUpsertResponse> UpsertQcPlanStageAsync(
        long revisionId, QcPlanUpsertRequest req, CancellationToken ct = default);

    /// <summary>Append-only QC capture write. Server validates
    /// FAIL → reason required, reason exists + active. Returns the
    /// persisted capture with server-assigned Id + CapturedAt.</summary>
    Task<QcCaptureItem> CreateQcCaptureAsync(
        long revisionId, QcCaptureCreateRequest req, CancellationToken ct = default);

    // ── Spec list / sheet exports (P10.5g) ──────────────────────────
    /// <summary>
    /// Stream the server-rendered Spec list export (CSV / XLSX / PDF) to
    /// <paramref name="destinationFilePath"/>. Server filters by the same
    /// <paramref name="view"/> + <paramref name="planner"/> chip the Spec
    /// list page uses, so what the operator sees == what they get. Uses
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> + chunked
    /// CopyTo so a 10 k-row xlsx (~ few MB) never lives in memory all at
    /// once. Returns the bytes persisted; throws on non-2xx.
    /// </summary>
    /// <param name="format">"csv" / "xlsx" / "pdf" — anything else throws
    /// <see cref="ArgumentOutOfRangeException"/>.</param>
    Task<long> DownloadSpecListExportAsync(
        string format,
        string? search,
        string view,
        string? planner,
        string destinationFilePath,
        CancellationToken ct = default);

    /// <summary>
    /// Stream the single-spec sheet PDF (mirror of web PR #31d) to
    /// <paramref name="destinationFilePath"/>. Same chunked-download
    /// semantics as <see cref="DownloadSpecListExportAsync"/>.
    /// </summary>
    Task<long> DownloadSpecSheetPdfAsync(
        long revisionId,
        string destinationFilePath,
        CancellationToken ct = default);

    // ── Settings — My Profile + My Password (P10.6a) ────────────────
    /// <summary>Fetch the signed-in user's profile (Username + Role
    /// from claims; DisplayName + Department + MustChangePassword
    /// from the DB row). The Settings/My Profile page renders this.</summary>
    Task<SettingsProfileDto> GetMyProfileAsync(CancellationToken ct = default);

    /// <summary>Update the signed-in user's DisplayName. Returns the
    /// refreshed <see cref="SettingsProfileDto"/> so the page re-renders
    /// against the server-confirmed shape. Throws
    /// <see cref="ApiException"/> with code <c>profile.display_name_too_long</c>
    /// on 422, <c>profile.not_found</c> on 404.</summary>
    Task<SettingsProfileDto> UpdateMyProfileAsync(
        UpdateProfileRequest req, CancellationToken ct = default);

    /// <summary>Change the signed-in user's password. Throws
    /// <see cref="ApiException"/> with code <c>auth.wrong_current</c>
    /// on 422 (old pwd mismatch), <c>auth.new_too_short</c> on 422
    /// (< 4 chars), <c>auth.missing_fields</c> on 422 (blank), or
    /// <c>profile.not_found</c> on 404.</summary>
    Task<ChangePasswordResponse> ChangeMyPasswordAsync(
        ChangePasswordRequest req, CancellationToken ct = default);

    // ── Settings — About (P10.6d) ───────────────────────────────────
    /// <summary>Server build + DB inventory snapshot for the About
    /// page. Anonymous-friendly DTO shape — no credentials or PII;
    /// the controller still requires auth so we don't expose the
    /// data dir path on the public internet. Throws
    /// <see cref="ApiException"/> on 401 / 5xx.</summary>
    Task<AboutDto> GetAboutAsync(CancellationToken ct = default);
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
