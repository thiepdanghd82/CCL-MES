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

    // ── Home (P10.10) ──────────────────────────────────────────────
    Task<HomeSummaryDto?> GetHomeSummaryAsync(CancellationToken ct = default);

    // ── Machine Dashboard (P10.8) ──────────────────────────────────
    Task<MachineDashboardDto?> GetMachineDashboardAsync(CancellationToken ct = default);
    Task<MachineDetailDto?> GetMachineDetailAsync(long workCenterId, CancellationToken ct = default);
    Task<ShopOrderHistoryDto?> GetShopOrderHistoryAsync(string? period, string? search,
        string? status = null, string? customer = null, string? machine = null, CancellationToken ct = default);

    // ── QMS (P10.9) ────────────────────────────────────────────────
    Task<QmsQueueDto?> GetQmsQueueAsync(CancellationToken ct = default);
    Task<QcHistoryDto?> GetQcHistoryAsync(string? kind, string? judgment, string? search, CancellationToken ct = default);

    // ── NPI (pilot scope) ──────────────────────────────────────────
    Task<NpiPagedRaw<NpiWorkCenter>> GetWorkCentersAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiPagedRaw<NpiRawMaterial>> GetRawMaterialsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiPagedRaw<NpiRoutingOperation>> GetRoutingsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiPagedRaw<NpiStructure>> GetStructuresAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<NpiImportResultDto?> ImportNpiAsync(string kind, string fileName, byte[] content, CancellationToken ct = default);

    // ── Work Orders (P10.3 W4 — scan→accept) ──────────────────────
    /// <summary>Lookup WO by number. Returns null on 404; throws <see cref="ApiException"/>
    /// on any other non-2xx so the caller can show error UI.</summary>
    Task<IReadOnlyList<ActiveWorkOrderCard>> GetActiveWorkOrdersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WoAuditEntry>> GetWoAuditAsync(long workOrderId, CancellationToken ct = default);
    Task<WorkOrderSummary?> GetWorkOrderByNoAsync(string woNo, CancellationToken ct = default);

    /// <summary>Advance the WO via its existing state machine. Always returns
    /// a response object even when the domain guard rejects the move; the
    /// caller renders <see cref="AdvanceWorkOrderResponse.ErrorCode"/>.
    /// Throws <see cref="ApiException"/> on auth failure or genuine 404.
    ///
    /// P10.7a-1.3 — caller MUST supply the <paramref name="ifMatchETag"/>
    /// captured from the previous summary GET. Server returns 428 if
    /// the header is missing, 409 + <c>ErrorCode = "wo.state_conflict"</c>
    /// when another operator advanced the WO since the last GET (caller
    /// uses the new <c>ETag</c> field to refresh + retry / show banner).
    /// An <c>Idempotency-Key</c> is generated automatically per call so
    /// fast double-taps return the stored response without a second
    /// state-machine fire.</summary>
    Task<AdvanceWorkOrderResponse> AdvanceWorkOrderAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default);

    // ── PREPRESS row checks (P10.7b-2 — read + 3 PUTs) ────────────
    /// <summary>Read the 3 child-table sets + rollup flag + ETag. The server
    /// lazy-materialises rows from the BOM snapshot on first read so legacy
    /// pre-7b WOs surface a populated checklist on the operator's first hit.
    /// Throws <see cref="ApiException"/> on 404 / 401.</summary>
    Task<PrepressView> GetPrepressViewAsync(long workOrderId, CancellationToken ct = default);

    /// <summary>Set one wo_materials row (status Pending/Ok/Ng + optional
    /// QtyLoaded/LotNo + NG reason/note). 200 with the bumped ETag + post-
    /// write rollup; 409 + <c>ErrorCode = "wo.state_conflict"</c> on stale
    /// If-Match (response still carries the fresh server ETag). 422 on
    /// invalid_status / invalid_reason_code / invalid_ng_note /
    /// invalid_phase.</summary>
    Task<PrepressSetResponse> PutPrepressMaterialAsync(
        long workOrderId, int bomLineIdx, string ifMatchETag,
        SetPrepressMaterialRequest req, CancellationToken ct = default);

    /// <summary>Set the 1:1 wo_plate_check row. Same contract as
    /// <see cref="PutPrepressMaterialAsync"/>.</summary>
    Task<PrepressSetResponse> PutPrepressPlateAsync(
        long workOrderId, string ifMatchETag,
        SetPrepressPlateRequest req, CancellationToken ct = default);

    /// <summary>Set the 1:1 wo_cutter_check row. Same contract as
    /// <see cref="PutPrepressMaterialAsync"/>.</summary>
    Task<PrepressSetResponse> PutPrepressCutterAsync(
        long workOrderId, string ifMatchETag,
        SetPrepressCutterRequest req, CancellationToken ct = default);

    // ── Running Surface (P10.7c-3 — SETTING + RUNNING + PAUSED) ───
    /// <summary>Read view backing the SETTING / RUNNING / PAUSED
    /// dashboards. Single round-trip carries every field needed to
    /// render the next state (active session/pause + last 20 qty
    /// entries for the correction picker). Throws on 404 / 401.</summary>
    Task<RunningSurfaceView> GetRunningSurfaceViewAsync(long workOrderId, CancellationToken ct = default);

    /// <summary>Idempotent stamp of SettingStartAt. Closes the gap
    /// that /advance landed the WO in SETTING without starting the
    /// timer; the dashboard fires this once on first load when
    /// MesPhase=SETTING + SettingStartAt is null.</summary>
    Task<RunningSurfaceSetResponse> PostSettingEnterAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default);

    /// <summary>Mark setting done — SETTING → IPQC_WAIT. Server stamps
    /// EndAt = now + computes DurationSec.</summary>
    Task<RunningSurfaceSetResponse> PostSettingDoneAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default);

    /// <summary>Start a RUNNING session — IPQC_APPROVED → RUNNING.</summary>
    Task<RunningSurfaceSetResponse> PostRunStartAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default);

    /// <summary>Per-tap qty add (+done and/or +ng with reason/note).
    /// Server validates Ng reason against ReasonCodeKind.Scrap when
    /// QtyNgDelta &gt; 0. Use /run/qty/correct for negative.</summary>
    Task<RunningSurfaceSetResponse> PostRunQtyAddAsync(
        long workOrderId, string ifMatchETag,
        RunQtyAddRequest req, CancellationToken ct = default);

    /// <summary>Append-only negative-delta correction linked to a prior
    /// entry. CorrectionReason is required (1-500 chars). Deltas can
    /// be negative.</summary>
    Task<RunningSurfaceSetResponse> PostRunQtyCorrectAsync(
        long workOrderId, string ifMatchETag,
        RunQtyCorrectRequest req, CancellationToken ct = default);

    /// <summary>RUNNING → PAUSED. ReasonCode validated against
    /// ReasonCodeKind.Pause.</summary>
    Task<RunningSurfaceSetResponse> PostRunPauseAsync(
        long workOrderId, string ifMatchETag,
        RunPauseRequest req, CancellationToken ct = default);

    /// <summary>PAUSED → RUNNING.</summary>
    Task<RunningSurfaceSetResponse> PostRunResumeAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default);

    /// <summary>RUNNING|PAUSED → FQC_PENDING. Closes active pause +
    /// session. Requires QtyDoneCached &gt; 0.</summary>
    Task<RunningSurfaceSetResponse> PostRunFinishAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default);

    // ── IPQC review + QA approval (P10.7d-3 — operator dashboards) ─

    /// <summary>Read view backing the IpqcDashboard + QaApprovalDashboard.
    /// Single round-trip carries each of the 4 slots + judgment + QA
    /// fields + current ETag + 3 server-computed rollup flags
    /// (IsReadyForJudgment / AllOk / AnyNg) so the dashboard can render
    /// every state branch without a second hop. Throws on 404 / 401.</summary>
    Task<IpqcView> GetIpqcViewAsync(long workOrderId, CancellationToken ct = default);

    /// <summary>Set the Material recheck slot (status Pending/Ok/Ng +
    /// optional NG reason/note). On 200 the response carries the bumped
    /// ETag + post-write rollup flags. On 409 the response still carries
    /// the fresh server ETag so the caller can re-sync; ErrorCode =
    /// "wo.state_conflict".</summary>
    Task<IpqcSetResponse> PutIpqcMaterialAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default);

    /// <summary>Set the Print A (Color/ΔE) slot. Same contract as
    /// <see cref="PutIpqcMaterialAsync"/>.</summary>
    Task<IpqcSetResponse> PutIpqcPrintAAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default);

    /// <summary>Set the Print B (Registration) slot. Same contract as
    /// <see cref="PutIpqcMaterialAsync"/>.</summary>
    Task<IpqcSetResponse> PutIpqcPrintBAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default);

    /// <summary>Set the Print C (Content) slot. Same contract as
    /// <see cref="PutIpqcMaterialAsync"/>.</summary>
    Task<IpqcSetResponse> PutIpqcPrintCAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default);

    /// <summary>Submit IPQC judgment (GoRun / StopLine / SpecialAccept).
    /// Server validates: all 4 slots non-Pending; GoRun rejected when
    /// any slot=Ng; SpecialAccept requires reason 1-500 chars.
    /// Transition map: GoRun → IPQC_APPROVED; StopLine → PREPRESS;
    /// SpecialAccept → QA_PENDING.</summary>
    Task<IpqcSetResponse> PostIpqcJudgmentAsync(
        long workOrderId, string ifMatchETag,
        SubmitIpqcJudgmentRequest req, CancellationToken ct = default);

    /// <summary>QA approval of a SPECIAL_ACCEPT escalation. Server enforces
    /// Q3 dual-sig — when OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on (default)
    /// and the caller's username equals IpqcSubmittedBy, server emits
    /// 422 + ErrorCode = "qa.same_user_as_ipqc_submitter" +
    /// WO_QA_APPROVE_DENIED audit. UI MUST also gate the button before
    /// the round-trip so legitimate users see the banner inline, not
    /// after a server bounce.</summary>
    Task<IpqcSetResponse> PostQaApproveAsync(
        long workOrderId, string ifMatchETag,
        QaApproveRequest req, CancellationToken ct = default);

    // ── FQC + OQC review (P10.7e-3 — operator dashboards) ─────────
    /// <summary>Read view for FQC ({kind}="fqc") or OQC ({kind}="oqc").
    /// Returns items + judgment + 3-sig state + ETag + MesPhase.</summary>
    Task<WoQcView> GetWoQcViewAsync(long workOrderId, string kind, CancellationToken ct = default);

    /// <summary>PUT 1 item Status (Ok/Ng). On NG, NgReasonCode (from
    /// reason-code catalog kind=Scrap) + NgNote (1-500 chars) required.</summary>
    Task<WoQcSetResponse> PutWoQcItemAsync(
        long workOrderId, string kind, string itemKey, string ifMatchETag,
        SetWoQcItemRequest req, CancellationToken ct = default);

    /// <summary>FQC single-sig Inspector judgment (Pass | Reject).
    /// Transition: Pass → OQC_PENDING; Reject → PREPRESS (Q2 transient).</summary>
    Task<WoQcSetResponse> PostFqcJudgmentAsync(
        long workOrderId, string ifMatchETag,
        SubmitFqcJudgmentRequest req, CancellationToken ct = default);

    /// <summary>OQC sig 1 — Inspector commits the items.</summary>
    Task<WoQcSetResponse> PostOqcInspectAsync(
        long workOrderId, string ifMatchETag,
        OqcInspectRequest req, CancellationToken ct = default);

    /// <summary>OQC sig 2 — Reviewer. Server enforces Q5 invariant
    /// Reviewer ≠ Inspector (OqcRequireDistinctReviewer default on);
    /// violation: 422 + ErrorCode = "oqc.same_user_as_inspector" +
    /// WO_OQC_REVIEW_DENIED audit. Mirror this gate client-side.</summary>
    Task<WoQcSetResponse> PostOqcReviewAsync(
        long workOrderId, string ifMatchETag,
        OqcReviewRequest req, CancellationToken ct = default);

    /// <summary>OQC sig 3 — Approver. Server enforces 2 Q5 invariants
    /// (Approver ≠ Reviewer; Approver ≠ Inspector). On Approve happy
    /// path: WO advances OQC_PENDING → SHIPPED + emits WO_OQC_APPROVE +
    /// WO_SHIPPED audits. On Reject: WO → FQC_PENDING re-loop.</summary>
    Task<WoQcSetResponse> PostOqcApproveAsync(
        long workOrderId, string ifMatchETag,
        OqcApproveRequest req, CancellationToken ct = default);

    /// <summary>List photo metadata for one item (no blob payload).</summary>
    Task<IReadOnlyList<WoQcPhotoDto>> GetWoQcPhotosAsync(
        long workOrderId, string kind, string itemKey, CancellationToken ct = default);

    /// <summary>POST a JPEG/PNG photo (multipart/form-data, single "file"
    /// field, 5 MiB cap). Server SHA-256 + audit WoQcPhotoAdd; response
    /// carries new ETag + canonical MesPhase (L19) + uploaded photo
    /// metadata.</summary>
    Task<WoQcPhotoUploadResponse> UploadWoQcPhotoAsync(
        long workOrderId, string kind, string itemKey, string ifMatchETag,
        Stream content, string fileName, string mimeType,
        CancellationToken ct = default);

    /// <summary>DELETE a single photo by id. Audit WoQcPhotoDelete +
    /// blob removal best-effort.</summary>
    Task<WoQcSetResponse> DeleteWoQcPhotoAsync(
        long workOrderId, string kind, string itemKey, long photoId, string ifMatchETag,
        CancellationToken ct = default);

    /// <summary>Q8 — live-recomputed summary report for the
    /// ShippedSummaryDashboard. Includes OEE + pause Pareto + 3-leg QC.</summary>
    Task<WoSummaryReport> GetWoSummaryReportAsync(long workOrderId, CancellationToken ct = default);

    // ── Reason codes (P10.7b-3 — operator-facing picker) ──────────
    /// <summary>List active reason codes filtered by kind ("Pause" /
    /// "Scrap" / "Recovery"; omit for every kind). The PREPRESS dashboard
    /// picker calls this with kind="Scrap" to populate the NG dropdown
    /// for material / plate / cutter rows. Empty list (after a fresh DB
    /// boot before seed runs) surfaces to the dashboard as an empty
    /// picker → "danh mục NG trống — báo IT" banner instead of a
    /// silent submit-422 loop.</summary>
    Task<IReadOnlyList<ReasonCodeOption>> GetReasonCodesAsync(
        string? kind, CancellationToken ct = default);

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

    /// <summary>P10.10 — one-click duplicate: clone a source spec onto a fresh
    /// product (own Rev A, blank IFS code) ready to edit inline.</summary>
    Task<SpecMutationResponse> DuplicateSpecAsync(long sourceRevisionId, CancellationToken ct = default);

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

    // ── Audit Log — Admin-only (P10.6e) ─────────────────────────────
    /// <summary>Paged audit log fetch. Throws <see cref="ApiException"/>
    /// with 403 when the caller is not Admin.</summary>
    Task<AuditLogPagedResult> GetAuditLogAsync(
        string? search, string? action, string? actor,
        DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>Distinct action codes for the filter dropdown.</summary>
    Task<List<string>> GetAuditActionsAsync(CancellationToken ct = default);

    /// <summary>Download an audit log export. Saves the streamed
    /// bytes to <paramref name="destinationFilePath"/> and returns
    /// the size in bytes + the server-provided filename. Throws
    /// <see cref="ApiException"/> on 403 / 422.</summary>
    Task<AuditLogExportDownload> ExportAuditLogAsync(
        string format,
        string? search, string? action, string? actor,
        DateTime? fromUtc, DateTime? toUtc,
        string destinationFilePath, CancellationToken ct = default);

    // ── Account Control — Admin-only (P10.6c) ───────────────────────
    /// <summary>Paged user list. Throws <see cref="ApiException"/>
    /// with 403 when the caller is not Admin.</summary>
    Task<AccountPagedResult> ListAccountsAsync(string? search, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Create a new account. Throws <see cref="ApiException"/>
    /// on 403, 422 (validation), or 5xx. Server flips
    /// MustChangePassword=true on every freshly-created row.</summary>
    Task<AccountDto> CreateAccountAsync(CreateAccountRequest req, CancellationToken ct = default);

    /// <summary>Update displayName / role / department / IsActive.
    /// Server enforces last-admin + self-action guards.</summary>
    Task<AccountDto> UpdateAccountAsync(long userId, UpdateAccountRequest req, CancellationToken ct = default);

    /// <summary>Admin force-reset another user's password. Sets
    /// MustChangePassword=true on the target so they're prompted on
    /// next login. Throws 422 <c>accounts.self_action_forbidden</c>
    /// when invoked against the caller's own id.</summary>
    Task<AccountDto> ResetAccountPasswordAsync(long userId, ResetPasswordRequest req, CancellationToken ct = default);

    // ── Backup / Restore — Admin-only (P10.6h) ──────────────────────
    /// <summary>List existing snapshots in the server's backup dir.
    /// Throws <see cref="ApiException"/> with 403 when the caller is
    /// not Admin.</summary>
    Task<List<BackupSnapshotDto>> ListBackupsAsync(CancellationToken ct = default);

    /// <summary>Create a new online SQLite snapshot. Returns the new
    /// snapshot row on success; throws <see cref="ApiException"/>
    /// on 403 / 422.</summary>
    Task<BackupSnapshotDto> CreateBackupAsync(CancellationToken ct = default);

    /// <summary>Upload + restore from a snapshot file. The server
    /// validates the file (SQLite header + schema sanity), takes a
    /// pre-restore snapshot of the current live DB, then clones the
    /// uploaded file in-place via the SQLite online backup API.
    /// Throws <see cref="ApiException"/> on 403 / 422 with a stable
    /// <c>backup.*</c> error code.</summary>
    Task<RestoreResultDto> RestoreBackupAsync(Stream content, string fileName, CancellationToken ct = default);
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

/// <summary>P10.6e — return shape from
/// <see cref="ICclApiClient.ExportAuditLogAsync"/>. The bytes are
/// streamed to disk by the client wrapper; the page surfaces the
/// filename + size so the operator gets a "saved to …" toast.</summary>
public sealed record AuditLogExportDownload(string FileName, long Bytes, string ContentType);

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
