using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared;
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
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Client;

/// <summary>
/// Concrete <see cref="ICclApiClient"/>. Stays VERY thin — the
/// <see cref="HttpClient"/> handed in is already configured with the
/// API base URL and the <see cref="AuthorizationDelegatingHandler"/>
/// chain, so the methods just translate the contract DTO ↔ HTTP.
/// </summary>
public sealed class CclApiClient : ICclApiClient
{
    private readonly HttpClient _http;
    private readonly ApiClientOptions _opts;

    public CclApiClient(HttpClient http, IOptions<ApiClientOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    // ── Auth ────────────────────────────────────────────────────────

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        // Login is anonymous — we attach the device id explicitly so the
        // audit row carries it. The bearer-attach handler skips when no
        // token is stored, which is the case at login time.
        var req = new LoginRequest { Username = username, Password = password, DeviceId = _opts.DeviceId };
        using var resp = await _http.PostAsJsonAsync($"/{ApiVersion.Prefix}/auth/login", req, ct);
        return await ReadAsAsync<LoginResponse>(resp, ct);
    }

    public async Task<UserInfo> GetMeAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/auth/me", ct);
        return await ReadAsAsync<UserInfo>(resp, ct);
    }

    // ── Home (P10.10) ──────────────────────────────────────────────

    public async Task<HomeSummaryDto?> GetHomeSummaryAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/home/summary", ct);
        return await ReadAsAsync<HomeSummaryDto>(resp, ct);
    }

    // ── Machine Dashboard (P10.8) ──────────────────────────────────

    public async Task<MachineDashboardDto?> GetMachineDashboardAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/machines/dashboard", ct);
        return await ReadAsAsync<MachineDashboardDto>(resp, ct);
    }

    public async Task<MachineDetailDto?> GetMachineDetailAsync(long workCenterId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/machines/{workCenterId}/detail", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsAsync<MachineDetailDto>(resp, ct);
    }

    public async Task<ShopOrderHistoryDto?> GetShopOrderHistoryAsync(string? period, string? search, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(period)) qs.Add($"period={Uri.EscapeDataString(period)}");
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        var url = $"/{ApiVersion.Prefix}/shop-orders/history" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        using var resp = await _http.GetAsync(url, ct);
        return await ReadAsAsync<ShopOrderHistoryDto>(resp, ct);
    }

    // ── QMS (P10.9) ────────────────────────────────────────────────

    public async Task<QmsQueueDto?> GetQmsQueueAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/qms/queue", ct);
        return await ReadAsAsync<QmsQueueDto>(resp, ct);
    }

    public async Task<QcHistoryDto?> GetQcHistoryAsync(string? kind, string? judgment, string? search, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(kind)) qs.Add($"kind={Uri.EscapeDataString(kind)}");
        if (!string.IsNullOrWhiteSpace(judgment)) qs.Add($"judgment={Uri.EscapeDataString(judgment)}");
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        var url = $"/{ApiVersion.Prefix}/qms/qc-history" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        using var resp = await _http.GetAsync(url, ct);
        return await ReadAsAsync<QcHistoryDto>(resp, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var req = new RefreshTokenRequest { RefreshToken = refreshToken };
        using var resp = await _http.PostAsJsonAsync($"/{ApiVersion.Prefix}/auth/logout", req, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnNonSuccess(resp, ct);
    }

    // ── NPI (pilot) ─────────────────────────────────────────────────

    public Task<NpiPagedRaw<NpiWorkCenter>> GetWorkCentersAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync<NpiWorkCenter>("workcenters", search, page, pageSize, ct);

    public Task<NpiPagedRaw<NpiRawMaterial>> GetRawMaterialsAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync<NpiRawMaterial>("rawmaterials", search, page, pageSize, ct);

    public Task<NpiPagedRaw<NpiRoutingOperation>> GetRoutingsAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync<NpiRoutingOperation>("routings", search, page, pageSize, ct);

    public Task<NpiPagedRaw<NpiStructure>> GetStructuresAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync<NpiStructure>("structures", search, page, pageSize, ct);

    // ── Work Orders ─────────────────────────────────────────────────

    public async Task<WorkOrderSummary?> GetWorkOrderByNoAsync(string woNo, CancellationToken ct = default)
    {
        // Path-segment encode — WO numbers may legitimately contain
        // characters that need escaping (slashes are blocked at the
        // model level but we still escape defensively).
        var encoded = Uri.EscapeDataString(woNo);
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/work-orders/by-no/{encoded}/summary", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsAsync<WorkOrderSummary>(resp, ct);
    }

    public async Task<AdvanceWorkOrderResponse> AdvanceWorkOrderAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/advance");

        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        // P10.7a-1.3 — RowVersion handshake (RFC 7232 If-Match) +
        // Idempotency-Key per intent. The server normalizes both quoted
        // and unquoted ETag values; we send the canonical quoted form.
        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");

        // One key per intent — fast double-tap on the Accept button
        // shares the same key (operator clicked once with intent to
        // advance), so the second physical tap hits the replay path
        // server-side and returns the stored response without a second
        // state-machine fire.
        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);

        // 200 (happy) AND 409 (stale ETag) both carry a usable
        // AdvanceWorkOrderResponse body — the 409 path returns the
        // server's current ETag so the caller can reload+retry without
        // a separate summary GET. ReadAsAsync would throw on the 409;
        // unwrap inline here so the response surfaces to the Razor
        // page's banner logic.
        if (resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>(cancellationToken: ct);
            return body ?? new AdvanceWorkOrderResponse
            {
                Ok = false,
                ErrorCode = "http.empty_body",
            };
        }

        // 428 / 422 / 400 / 404 / 401 — let the generic non-success
        // handler throw ApiException; the UI's central error mapper
        // (LocaliseAdvanceError etc.) handles the rest.
        return await ReadAsAsync<AdvanceWorkOrderResponse>(resp, ct);
    }

    // ── PREPRESS row checks ─────────────────────────────────────────

    public async Task<PrepressView> GetPrepressViewAsync(long workOrderId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/prepress", ct);
        return await ReadAsAsync<PrepressView>(resp, ct);
    }

    public Task<PrepressSetResponse> PutPrepressMaterialAsync(
        long workOrderId, int bomLineIdx, string ifMatchETag,
        SetPrepressMaterialRequest req, CancellationToken ct = default)
        => SendPrepressPutAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/materials/{bomLineIdx}",
            ifMatchETag, req, ct);

    public Task<PrepressSetResponse> PutPrepressPlateAsync(
        long workOrderId, string ifMatchETag,
        SetPrepressPlateRequest req, CancellationToken ct = default)
        => SendPrepressPutAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/plate-check",
            ifMatchETag, req, ct);

    public Task<PrepressSetResponse> PutPrepressCutterAsync(
        long workOrderId, string ifMatchETag,
        SetPrepressCutterRequest req, CancellationToken ct = default)
        => SendPrepressPutAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/cutter-check",
            ifMatchETag, req, ct);

    // ── Running Surface (P10.7c-3) ──────────────────────────────────

    public async Task<RunningSurfaceView> GetRunningSurfaceViewAsync(
        long workOrderId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/running-surface", ct);
        return await ReadAsAsync<RunningSurfaceView>(resp, ct);
    }

    public Task<RunningSurfaceSetResponse> PostSettingEnterAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/setting/enter",
            ifMatchETag, new SettingEnterRequest(), ct);

    public Task<RunningSurfaceSetResponse> PostSettingDoneAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/setting/done",
            ifMatchETag, new SettingDoneRequest(), ct);

    public Task<RunningSurfaceSetResponse> PostRunStartAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/run/start",
            ifMatchETag, new RunStartRequest(), ct);

    public Task<RunningSurfaceSetResponse> PostRunQtyAddAsync(
        long workOrderId, string ifMatchETag,
        RunQtyAddRequest req, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/run/qty",
            ifMatchETag, req, ct);

    public Task<RunningSurfaceSetResponse> PostRunQtyCorrectAsync(
        long workOrderId, string ifMatchETag,
        RunQtyCorrectRequest req, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/run/qty/correct",
            ifMatchETag, req, ct);

    public Task<RunningSurfaceSetResponse> PostRunPauseAsync(
        long workOrderId, string ifMatchETag,
        RunPauseRequest req, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/run/pause",
            ifMatchETag, req, ct);

    public Task<RunningSurfaceSetResponse> PostRunResumeAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/run/resume",
            ifMatchETag, new RunResumeRequest(), ct);

    public Task<RunningSurfaceSetResponse> PostRunFinishAsync(
        long workOrderId, string ifMatchETag, CancellationToken ct = default)
        => SendRunningSurfacePostAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/run/finish",
            ifMatchETag, new RunFinishRequest(), ct);

    private async Task<RunningSurfaceSetResponse> SendRunningSurfacePostAsync(
        string path, string ifMatchETag, object req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(req),
        };

        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");

        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);

        if (resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>(cancellationToken: ct);
            return body ?? new RunningSurfaceSetResponse
            {
                Ok = false,
                ErrorCode = "http.empty_body",
            };
        }

        return await ReadAsAsync<RunningSurfaceSetResponse>(resp, ct);
    }

    // ── IPQC review + QA approval (P10.7d-3) ───────────────────────

    public async Task<IpqcView> GetIpqcViewAsync(
        long workOrderId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/ipqc", ct);
        return await ReadAsAsync<IpqcView>(resp, ct);
    }

    public Task<IpqcSetResponse> PutIpqcMaterialAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default)
        => SendIpqcMutationAsync(
            HttpMethod.Put,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/ipqc/material",
            ifMatchETag, req, ct);

    public Task<IpqcSetResponse> PutIpqcPrintAAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default)
        => SendIpqcMutationAsync(
            HttpMethod.Put,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/ipqc/print-a",
            ifMatchETag, req, ct);

    public Task<IpqcSetResponse> PutIpqcPrintBAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default)
        => SendIpqcMutationAsync(
            HttpMethod.Put,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/ipqc/print-b",
            ifMatchETag, req, ct);

    public Task<IpqcSetResponse> PutIpqcPrintCAsync(
        long workOrderId, string ifMatchETag,
        SetIpqcSlotRequest req, CancellationToken ct = default)
        => SendIpqcMutationAsync(
            HttpMethod.Put,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/ipqc/print-c",
            ifMatchETag, req, ct);

    public Task<IpqcSetResponse> PostIpqcJudgmentAsync(
        long workOrderId, string ifMatchETag,
        SubmitIpqcJudgmentRequest req, CancellationToken ct = default)
        => SendIpqcMutationAsync(
            HttpMethod.Post,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/ipqc/judgment",
            ifMatchETag, req, ct);

    public Task<IpqcSetResponse> PostQaApproveAsync(
        long workOrderId, string ifMatchETag,
        QaApproveRequest req, CancellationToken ct = default)
        => SendIpqcMutationAsync(
            HttpMethod.Post,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qa/approve",
            ifMatchETag, req, ct);

    /// <summary>Shared mutation pipeline for all 6 IPQC + QA endpoints.
    /// Mirrors <see cref="SendRunningSurfacePostAsync"/> exactly so
    /// concurrency + idempotency-key handling stays identical to the
    /// 7c-3 running surface. 200 + 409 both deserialise as
    /// <see cref="IpqcSetResponse"/>; anything else gets the standard
    /// envelope error parse + throws <see cref="ApiException"/>.</summary>
    private async Task<IpqcSetResponse> SendIpqcMutationAsync(
        HttpMethod method, string path, string ifMatchETag, object req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(req),
        };

        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");

        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);

        // 200 (success), 409 (state conflict — carries fresh ETag), 422
        // (qa.same_user_as_ipqc_submitter + other domain rejects) all
        // return the typed envelope. The UI distinguishes by ErrorCode.
        if (resp.StatusCode == HttpStatusCode.OK
            || resp.StatusCode == HttpStatusCode.Conflict
            || resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>(cancellationToken: ct);
            return body ?? new IpqcSetResponse
            {
                Ok = false,
                ErrorCode = "http.empty_body",
            };
        }

        return await ReadAsAsync<IpqcSetResponse>(resp, ct);
    }

    // ── FQC + OQC review (P10.7e-3) ────────────────────────────────

    public async Task<WoQcView> GetWoQcViewAsync(
        long workOrderId, string kind, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/{kind}", ct);
        return await ReadAsAsync<WoQcView>(resp, ct);
    }

    public Task<WoQcSetResponse> PutWoQcItemAsync(
        long workOrderId, string kind, string itemKey, string ifMatchETag,
        SetWoQcItemRequest req, CancellationToken ct = default)
        => SendWoQcMutationAsync(
            HttpMethod.Put,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/{kind}/items/{Uri.EscapeDataString(itemKey)}",
            ifMatchETag, req, ct);

    public Task<WoQcSetResponse> PostFqcJudgmentAsync(
        long workOrderId, string ifMatchETag,
        SubmitFqcJudgmentRequest req, CancellationToken ct = default)
        => SendWoQcMutationAsync(
            HttpMethod.Post,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/fqc/judgment",
            ifMatchETag, req, ct);

    public Task<WoQcSetResponse> PostOqcInspectAsync(
        long workOrderId, string ifMatchETag,
        OqcInspectRequest req, CancellationToken ct = default)
        => SendWoQcMutationAsync(
            HttpMethod.Post,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/oqc/inspect",
            ifMatchETag, req, ct);

    public Task<WoQcSetResponse> PostOqcReviewAsync(
        long workOrderId, string ifMatchETag,
        OqcReviewRequest req, CancellationToken ct = default)
        => SendWoQcMutationAsync(
            HttpMethod.Post,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/oqc/review",
            ifMatchETag, req, ct);

    public Task<WoQcSetResponse> PostOqcApproveAsync(
        long workOrderId, string ifMatchETag,
        OqcApproveRequest req, CancellationToken ct = default)
        => SendWoQcMutationAsync(
            HttpMethod.Post,
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/oqc/approve",
            ifMatchETag, req, ct);

    private async Task<WoQcSetResponse> SendWoQcMutationAsync(
        HttpMethod method, string path, string ifMatchETag, object req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(req),
        };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);
        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");
        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);
        if (resp.StatusCode == HttpStatusCode.OK
            || resp.StatusCode == HttpStatusCode.Conflict
            || resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>(cancellationToken: ct);
            return body ?? new WoQcSetResponse { Ok = false, ErrorCode = "http.empty_body" };
        }
        return await ReadAsAsync<WoQcSetResponse>(resp, ct);
    }

    public async Task<IReadOnlyList<WoQcPhotoDto>> GetWoQcPhotosAsync(
        long workOrderId, string kind, string itemKey, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/{kind}/items/{Uri.EscapeDataString(itemKey)}/photos",
            ct);
        var rows = await ReadAsAsync<List<WoQcPhotoDto>>(resp, ct);
        return rows;
    }

    public async Task<WoQcPhotoUploadResponse> UploadWoQcPhotoAsync(
        long workOrderId, string kind, string itemKey, string ifMatchETag,
        Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/{kind}/items/{Uri.EscapeDataString(itemKey)}/photos";

        using var msg = new HttpRequestMessage(HttpMethod.Post, path);
        var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        form.Add(streamContent, "file", fileName);
        msg.Content = form;
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);
        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");
        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);
        if (resp.StatusCode == HttpStatusCode.OK
            || resp.StatusCode == HttpStatusCode.Conflict
            || resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await resp.Content.ReadFromJsonAsync<WoQcPhotoUploadResponse>(cancellationToken: ct);
            return body ?? new WoQcPhotoUploadResponse { Ok = false, ErrorCode = "http.empty_body" };
        }
        return await ReadAsAsync<WoQcPhotoUploadResponse>(resp, ct);
    }

    public async Task<WoQcSetResponse> DeleteWoQcPhotoAsync(
        long workOrderId, string kind, string itemKey, long photoId, string ifMatchETag,
        CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/qc/{kind}/items/{Uri.EscapeDataString(itemKey)}/photos/{photoId}";
        using var msg = new HttpRequestMessage(HttpMethod.Delete, path);
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);
        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");
        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);
        if (resp.StatusCode == HttpStatusCode.OK
            || resp.StatusCode == HttpStatusCode.Conflict
            || resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>(cancellationToken: ct);
            return body ?? new WoQcSetResponse { Ok = false, ErrorCode = "http.empty_body" };
        }
        return await ReadAsAsync<WoQcSetResponse>(resp, ct);
    }

    public async Task<WoSummaryReport> GetWoSummaryReportAsync(long workOrderId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/summary-report", ct);
        return await ReadAsAsync<WoSummaryReport>(resp, ct);
    }

    public async Task<IReadOnlyList<ReasonCodeOption>> GetReasonCodesAsync(
        string? kind, CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/reason-codes";
        if (!string.IsNullOrWhiteSpace(kind))
            path += $"?kind={Uri.EscapeDataString(kind)}";
        using var resp = await _http.GetAsync(path, ct);
        var rows = await ReadAsAsync<List<ReasonCodeOption>>(resp, ct);
        return rows;
    }

    private async Task<PrepressSetResponse> SendPrepressPutAsync(
        string path, string ifMatchETag, object req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(req),
        };

        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        if (!string.IsNullOrWhiteSpace(ifMatchETag))
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchETag}\"");

        msg.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        using var resp = await _http.SendAsync(msg, ct);

        if (resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadFromJsonAsync<PrepressSetResponse>(cancellationToken: ct);
            return body ?? new PrepressSetResponse
            {
                Ok = false,
                ErrorCode = "http.empty_body",
            };
        }

        return await ReadAsAsync<PrepressSetResponse>(resp, ct);
    }

    // ── Devices ─────────────────────────────────────────────────────

    public async Task<ScanLogResponse> LogScanAsync(ScanLogRequest req, CancellationToken ct = default)
    {
        var deviceId = RequireDeviceId();
        using var resp = await _http.PostAsJsonAsync(
            $"/{ApiVersion.Prefix}/devices/{Uri.EscapeDataString(deviceId)}/scan-log", req, ct);
        return await ReadAsAsync<ScanLogResponse>(resp, ct);
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest req, CancellationToken ct = default)
    {
        var deviceId = RequireDeviceId();
        using var resp = await _http.PostAsJsonAsync(
            $"/{ApiVersion.Prefix}/devices/{Uri.EscapeDataString(deviceId)}/heartbeat", req, ct);
        return await ReadAsAsync<HeartbeatResponse>(resp, ct);
    }

    public async Task<DeviceInfoResponse?> GetDeviceInfoAsync(CancellationToken ct = default)
    {
        var deviceId = RequireDeviceId();
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/devices/{Uri.EscapeDataString(deviceId)}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsAsync<DeviceInfoResponse>(resp, ct);
    }

    // ── Specs ───────────────────────────────────────────────────────

    public async Task<NpiPagedRaw<SpecListItem>> GetSpecsAsync(string? search, int page, int pageSize, string? view, string? planner = null, CancellationToken ct = default)
    {
        var qs = $"page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            qs += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(view))
            qs += $"&view={Uri.EscapeDataString(view)}";
        if (!string.IsNullOrWhiteSpace(planner))
            qs += $"&planner={Uri.EscapeDataString(planner)}";
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/specs?{qs}", ct);
        return await ReadAsAsync<NpiPagedRaw<SpecListItem>>(resp, ct);
    }

    public async Task<SpecDetailItem?> GetSpecDetailAsync(long revisionId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/specs/{revisionId}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsAsync<SpecDetailItem>(resp, ct);
    }

    public async Task<List<SpecProductDropdownItem>> GetSpecProductsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/specs/products", ct);
        return await ReadAsAsync<List<SpecProductDropdownItem>>(resp, ct);
    }

    // ── Spec mutations (P10.5c-1) ───────────────────────────────────

    public Task<SpecMutationResponse> CreateSpecAsync(CreateSpecMutation req, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs", req, ct);

    public Task<SpecMutationResponse> ApproveSpecAsync(long revisionId, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/revisions/{revisionId}/approve", body: null, ct);

    public Task<SpecMutationResponse> CopySpecAsync(long sourceRevisionId, CopySpecMutation req, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/{sourceRevisionId}/copy", req, ct);

    public Task<SpecMutationResponse> ReviseSpecAsync(long sourceRevisionId, ReviseSpecMutation req, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/{sourceRevisionId}/revise", req, ct);

    public Task<SpecMutationResponse> SupersedeSpecAsync(long revisionId, SupersedeSpecMutation req, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/{revisionId}/supersede", req, ct);

    public Task<SpecMutationResponse> TrashSpecAsync(long revisionId, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/{revisionId}/trash", body: null, ct);

    public Task<SpecMutationResponse> RestoreSpecAsync(long revisionId, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/{revisionId}/restore", body: null, ct);

    public Task<SpecMutationResponse> UpdateSpecAsync(long revisionId, UpdateSpecMutation req, CancellationToken ct = default) =>
        SendSpecMutationAsync(HttpMethod.Put, $"/{ApiVersion.Prefix}/specs/{revisionId}", req, ct);

    // ── Spec import (P10.5c-2) ───────────────────────────────────────

    public async Task<SpecImportPreviewResponse> ImportPreviewSpecAsync(
        Stream content, string fileName, string plannerCategory, CancellationToken ct = default)
    {
        // Multipart upload — stream the file part DIRECTLY without
        // buffering. StreamContent wraps the stream as-is so HttpClient
        // pumps it chunk-by-chunk into the socket; the 10 MB legacy cap
        // never lives entirely in heap memory. Lesson D-5b.
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(plannerCategory ?? "silkscreen"), "plannerCategory");

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/import/preview")
        {
            Content = form,
        };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        // 90s timeout per Henry's spec — covers slow WiFi upload of a
        // 10 MB xlsx. The handler chain still respects the per-call CT
        // for operator cancel.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        using var resp = await _http.SendAsync(msg, cts.Token);

        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, cts.Token);

        var body = await resp.Content.ReadFromJsonAsync<SpecImportPreviewResponse>(cancellationToken: cts.Token);
        return body ?? throw new InvalidOperationException(
            "Spec import preview returned 2xx but body was empty.");
    }

    public async Task<SpecImportSaveResponse> ImportSaveSpecAsync(
        SpecImportSaveRequest req, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"/{ApiVersion.Prefix}/specs/import/save")
        {
            Content = System.Net.Http.Json.JsonContent.Create(req),
        };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        using var resp = await _http.SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);

        var body = await resp.Content.ReadFromJsonAsync<SpecImportSaveResponse>(cancellationToken: ct);
        return body ?? throw new InvalidOperationException(
            "Spec import save returned 2xx but body was empty.");
    }

    /// <summary>
    /// Shared mutation helper: builds the request with X-Device-Id (W4
    /// audit-pairing pattern), optional JSON body, and reads either a
    /// <see cref="SpecMutationResponse"/> success or a
    /// <see cref="SpecMutationError"/> failure. Failure → throw
    /// <see cref="ApiException"/> + <see cref="ApiError"/> envelope so
    /// the existing client error pipeline (page banners + Thử lại) keeps
    /// working uniformly across the lifecycle.
    /// </summary>
    private async Task<SpecMutationResponse> SendSpecMutationAsync(HttpMethod verb, string path, object? body, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(verb, path);
        if (body is not null)
            msg.Content = System.Net.Http.Json.JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        using var resp = await _http.SendAsync(msg, ct);

        if (!resp.IsSuccessStatusCode)
        {
            await ThrowOnSpecMutationFailureAsync(resp, ct);
        }

        var success = await resp.Content.ReadFromJsonAsync<SpecMutationResponse>(cancellationToken: ct);
        return success ?? throw new InvalidOperationException(
            $"Spec mutation {verb} {path} returned 2xx but body was empty.");
    }

    /// <summary>
    /// Spec mutation errors land as <see cref="SpecMutationError"/> (the
    /// shape SpecsController projects on 4xx). We translate to the global
    /// <see cref="ApiError"/> envelope by lifting <c>Code</c> + composing
    /// the English fallback so page-level banners can render either the
    /// raw code (mapped to VN by the page) or the English error text.
    /// </summary>
    private static async Task ThrowOnSpecMutationFailureAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        SpecMutationError? mutErr = null;
        try { mutErr = await resp.Content.ReadFromJsonAsync<SpecMutationError>(cancellationToken: ct); }
        catch { /* fallback below */ }

        if (mutErr is not null && !string.IsNullOrWhiteSpace(mutErr.Code))
        {
            var details = new Dictionary<string, string>(StringComparer.Ordinal);
            if (mutErr.CurrentStatus is not null) details["currentStatus"] = mutErr.CurrentStatus;
            if (mutErr.ActiveWoCount is not null) details["activeWoCount"] = mutErr.ActiveWoCount.Value.ToString();
            throw new ApiException((int)resp.StatusCode, new ApiError
            {
                Code = mutErr.Code,
                MessageEn = mutErr.Error,
                Details = details.Count == 0 ? null : details,
            });
        }

        // Fall back to the generic non-success path so the caller always
        // sees an ApiException regardless of whether the body parsed.
        ApiError? generic = null;
        try { generic = await resp.Content.ReadFromJsonAsync<ApiError>(cancellationToken: ct); }
        catch { /* synthesise below */ }
        generic ??= new ApiError { Code = "http.non_success", // P10.6a hotfix — MessageEn carries the bare status code; the VN
// mapper for "http.non_success" wraps it as "Lỗi máy chủ (HTTP …)"
// so prepending "HTTP " here produced the operator-visible
// "HTTP HTTP 404" double-prefix Henry filed.
MessageEn = ((int)resp.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        throw new ApiException((int)resp.StatusCode, generic);
    }

    // ── Drawings ────────────────────────────────────────────────────

    public async Task<List<DrawingKindSlot>> GetDrawingsByRevisionAsync(long revisionId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/drawings/by-revision/{revisionId}", ct);
        return await ReadAsAsync<List<DrawingKindSlot>>(resp, ct);
    }

    // ── Drawings write surface (P10.5e-1) ────────────────────────────

    public async Task<DrawingUploadResponse> UploadDrawingAsync(
        long revisionId, string kind, Stream content, string fileName,
        string? changeReason = null, CancellationToken ct = default)
    {
        // Multipart streaming — wrap the supplied stream as-is so
        // HttpClient pumps chunk-by-chunk into the socket; the 10 MB
        // legacy cap never lives entirely in heap memory (Lesson
        // D-5b carried forward from PR #84).
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);
        if (!string.IsNullOrWhiteSpace(changeReason))
            form.Add(new StringContent(changeReason), "changeReason");

        var path = $"/{ApiVersion.Prefix}/specs/{revisionId}/drawings/upload?kind={Uri.EscapeDataString(kind)}";
        using var msg = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        // 90 s timeout via linked CTS covers slow WiFi upload of a
        // full 10 MB drawing without hanging the UI.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));

        using var resp = await _http.SendAsync(msg, cts.Token);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, cts.Token);

        var body = await resp.Content.ReadFromJsonAsync<DrawingUploadResponse>(cancellationToken: cts.Token);
        return body ?? throw new InvalidOperationException(
            "Drawing upload returned 2xx but body was empty.");
    }

    public async Task<DrawingDecideResponse> DecideDrawingAsync(
        long revisionId, long versionId, DrawingDecideRequest req,
        CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/specs/{revisionId}/drawings/{versionId}/decide";
        using var msg = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = System.Net.Http.Json.JsonContent.Create(req),
        };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        using var resp = await _http.SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);

        var body = await resp.Content.ReadFromJsonAsync<DrawingDecideResponse>(cancellationToken: ct);
        return body ?? throw new InvalidOperationException(
            "Drawing decide returned 2xx but body was empty.");
    }

    public async Task<long> DownloadDrawingToFileAsync(
        long revisionId, long versionId, string destinationFilePath,
        CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/specs/{revisionId}/drawings/{versionId}/file";
        // Range processing enabled on the server side, so HttpCompletionOption.
        // ResponseHeadersRead lets us stream chunks straight to disk without
        // buffering the whole response.
        using var resp = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);

        // Ensure target directory exists; caller is responsible for the
        // safe-download root selection (IFileOpener.GetSafeDownloadDirectory).
        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        await using var sourceStream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await sourceStream.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);
        return new FileInfo(destinationFilePath).Length;
    }

    // ── QC Specs ────────────────────────────────────────────────────

    public async Task<Dictionary<string, QcWindowItem?>> GetQcWindowsByRevisionAsync(long revisionId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/qc-specs/windows/by-revision/{revisionId}", ct);
        return await ReadAsAsync<Dictionary<string, QcWindowItem?>>(resp, ct);
    }

    public async Task<List<QcCaptureItem>> GetQcCapturesByRevisionAsync(long revisionId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/qc-specs/captures/by-revision/{revisionId}", ct);
        return await ReadAsAsync<List<QcCaptureItem>>(resp, ct);
    }

    public async Task<List<QcReasonCode>> GetQcReasonCodesAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/qc-specs/reason-codes", ct);
        return await ReadAsAsync<List<QcReasonCode>>(resp, ct);
    }

    // ── QC plan + capture write surface (P10.5f) ────────────────────

    public async Task<QcPlanUpsertResponse> UpsertQcPlanStageAsync(
        long revisionId, QcPlanUpsertRequest req, CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/qc-specs/windows/upsert-stage/{revisionId}";
        using var msg = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = System.Net.Http.Json.JsonContent.Create(req),
        };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);
        using var resp = await _http.SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<QcPlanUpsertResponse>(cancellationToken: ct);
        return body ?? throw new InvalidOperationException(
            "QC plan upsert returned 2xx but body was empty.");
    }

    public async Task<QcCaptureItem> CreateQcCaptureAsync(
        long revisionId, QcCaptureCreateRequest req, CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/qc-specs/captures/{revisionId}";
        using var msg = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = System.Net.Http.Json.JsonContent.Create(req),
        };
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);
        using var resp = await _http.SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<QcCaptureItem>(cancellationToken: ct);
        return body ?? throw new InvalidOperationException(
            "QC capture returned 2xx but body was empty.");
    }

    // ── Spec list / sheet exports (P10.5g) ──────────────────────────

    public Task<long> DownloadSpecListExportAsync(
        string format,
        string? search,
        string view,
        string? planner,
        string destinationFilePath,
        CancellationToken ct = default)
    {
        var f = (format ?? "").Trim().ToLowerInvariant();
        if (f != "csv" && f != "xlsx" && f != "pdf")
            throw new ArgumentOutOfRangeException(nameof(format),
                "Format must be one of: csv, xlsx, pdf.");

        var qs = $"view={Uri.EscapeDataString(view ?? "Active")}";
        if (!string.IsNullOrWhiteSpace(search))
            qs += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(planner))
            qs += $"&planner={Uri.EscapeDataString(planner)}";

        var path = $"/{ApiVersion.Prefix}/specs/export/{f}?{qs}";
        return StreamToFileAsync(path, destinationFilePath, ct);
    }

    public Task<long> DownloadSpecSheetPdfAsync(
        long revisionId, string destinationFilePath, CancellationToken ct = default)
    {
        var path = $"/{ApiVersion.Prefix}/specs/export/{revisionId}/sheet/pdf";
        return StreamToFileAsync(path, destinationFilePath, ct);
    }

    // ── Settings — My Profile + My Password (P10.6a) ────────────────

    public async Task<SettingsProfileDto> GetMyProfileAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/settings/me", ct);
        return await ReadAsAsync<SettingsProfileDto>(resp, ct);
    }

    public async Task<SettingsProfileDto> UpdateMyProfileAsync(
        UpdateProfileRequest req, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Patch, $"/{ApiVersion.Prefix}/settings/me")
        {
            Content = System.Net.Http.Json.JsonContent.Create(req),
        };
        using var resp = await _http.SendAsync(msg, ct);
        return await ReadAsAsync<SettingsProfileDto>(resp, ct);
    }

    public async Task<ChangePasswordResponse> ChangeMyPasswordAsync(
        ChangePasswordRequest req, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync($"/{ApiVersion.Prefix}/settings/password", req, ct);
        return await ReadAsAsync<ChangePasswordResponse>(resp, ct);
    }

    public async Task<AboutDto> GetAboutAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/settings/about", ct);
        return await ReadAsAsync<AboutDto>(resp, ct);
    }

    // ── Audit Log (P10.6e) ──────────────────────────────────────────

    public async Task<AuditLogPagedResult> GetAuditLogAsync(
        string? search, string? action, string? actor,
        DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default)
    {
        var qs = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
        };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(action)) qs.Add($"action={Uri.EscapeDataString(action)}");
        if (!string.IsNullOrWhiteSpace(actor))  qs.Add($"actor={Uri.EscapeDataString(actor)}");
        if (fromUtc.HasValue)
            qs.Add($"from={Uri.EscapeDataString(fromUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))}");
        if (toUtc.HasValue)
            qs.Add($"to={Uri.EscapeDataString(toUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))}");

        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/audit/log?{string.Join('&', qs)}", ct);
        return await ReadAsAsync<AuditLogPagedResult>(resp, ct);
    }

    public async Task<List<string>> GetAuditActionsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/audit/actions", ct);
        return await ReadAsAsync<List<string>>(resp, ct);
    }

    public async Task<AuditLogExportDownload> ExportAuditLogAsync(
        string format,
        string? search, string? action, string? actor,
        DateTime? fromUtc, DateTime? toUtc,
        string destinationFilePath, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(action)) qs.Add($"action={Uri.EscapeDataString(action)}");
        if (!string.IsNullOrWhiteSpace(actor))  qs.Add($"actor={Uri.EscapeDataString(actor)}");
        if (fromUtc.HasValue)
            qs.Add($"from={Uri.EscapeDataString(fromUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))}");
        if (toUtc.HasValue)
            qs.Add($"to={Uri.EscapeDataString(toUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))}");

        var query = qs.Count == 0 ? "" : "?" + string.Join('&', qs);
        var path = $"/{ApiVersion.Prefix}/audit/export/{format}{query}";

        using var msg = new HttpRequestMessage(HttpMethod.Get, path);
        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);

        // Server stamps the filename via Content-Disposition; fall back
        // to "AuditLog.{ext}" if the header is somehow missing.
        var serverName = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"AuditLog.{format}";
        var ct2 = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = new FileStream(
            destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await resp.Content.CopyToAsync(fs, ct);
        return new AuditLogExportDownload(serverName, fs.Length, ct2);
    }

    // ── Account Control (P10.6c) ────────────────────────────────────

    public async Task<AccountPagedResult> ListAccountsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        using var resp = await _http.GetAsync(
            $"/{ApiVersion.Prefix}/admin/users?{string.Join('&', qs)}", ct);
        return await ReadAsAsync<AccountPagedResult>(resp, ct);
    }

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest req, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync($"/{ApiVersion.Prefix}/admin/users", req, ct);
        return await ReadAsAsync<AccountDto>(resp, ct);
    }

    public async Task<AccountDto> UpdateAccountAsync(long userId, UpdateAccountRequest req, CancellationToken ct = default)
    {
        using var resp = await _http.PatchAsJsonAsync(
            $"/{ApiVersion.Prefix}/admin/users/{userId}", req, ct);
        return await ReadAsAsync<AccountDto>(resp, ct);
    }

    public async Task<AccountDto> ResetAccountPasswordAsync(
        long userId, ResetPasswordRequest req, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"/{ApiVersion.Prefix}/admin/users/{userId}/reset-password", req, ct);
        return await ReadAsAsync<AccountDto>(resp, ct);
    }

    // ── Backup / Restore (P10.6h) ───────────────────────────────────

    public async Task<List<BackupSnapshotDto>> ListBackupsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/backup", ct);
        return await ReadAsAsync<List<BackupSnapshotDto>>(resp, ct);
    }

    public async Task<BackupSnapshotDto> CreateBackupAsync(CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync($"/{ApiVersion.Prefix}/backup", content: null, ct);
        return await ReadAsAsync<BackupSnapshotDto>(resp, ct);
    }

    public async Task<RestoreResultDto> RestoreBackupAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var multipart = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(streamContent, "file", fileName);
        using var resp = await _http.PostAsync($"/{ApiVersion.Prefix}/backup/restore", multipart, ct);
        return await ReadAsAsync<RestoreResultDto>(resp, ct);
    }

    /// <summary>
    /// Shared helper for the 4 export endpoints — GETs the server URL with
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> so the body
    /// streams to <paramref name="destinationFilePath"/> chunk by chunk
    /// (same pattern as <see cref="DownloadDrawingToFileAsync"/>). Carries
    /// the device-id header so the server can pair audit emit.
    /// </summary>
    private async Task<long> StreamToFileAsync(
        string path, string destinationFilePath, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);

        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowOnSpecMutationFailureAsync(resp, ct);

        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        await using var sourceStream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await sourceStream.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);
        return new FileInfo(destinationFilePath).Length;
    }

    // ── helpers ─────────────────────────────────────────────────────

    private string RequireDeviceId()
    {
        if (string.IsNullOrWhiteSpace(_opts.DeviceId))
            throw new InvalidOperationException(
                "ApiClientOptions.DeviceId is not configured. The MAUI host must populate it from " +
                "IDeviceModeService.DeviceId at startup before device-scoped endpoints are called.");
        return _opts.DeviceId;
    }

    private async Task<NpiPagedRaw<T>> GetPagedAsync<T>(string segment, string? search, int page, int pageSize, CancellationToken ct)
    {
        var qs = $"page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            qs += $"&search={Uri.EscapeDataString(search)}";
        using var resp = await _http.GetAsync($"/{ApiVersion.Prefix}/npi/{segment}?{qs}", ct);
        return await ReadAsAsync<NpiPagedRaw<T>>(resp, ct);
    }

    private static async Task<T> ReadAsAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
            await ThrowOnNonSuccess(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return body ?? throw new InvalidOperationException(
            $"API returned 2xx but body was empty for {typeof(T).Name}.");
    }

    private static async Task ThrowOnNonSuccess(HttpResponseMessage resp, CancellationToken ct)
    {
        // The API consistently returns ApiError JSON on non-success. If parsing
        // fails (e.g. plain text from a misconfigured proxy) we fall back to a
        // synthetic ApiError so callers always get the same exception shape.
        ApiError? error = null;
        try { error = await resp.Content.ReadFromJsonAsync<ApiError>(cancellationToken: ct); }
        catch { /* swallow — synthesise below */ }
        error ??= new ApiError { Code = "http.non_success", // P10.6a hotfix — MessageEn carries the bare status code; the VN
// mapper for "http.non_success" wraps it as "Lỗi máy chủ (HTTP …)"
// so prepending "HTTP " here produced the operator-visible
// "HTTP HTTP 404" double-prefix Henry filed.
MessageEn = ((int)resp.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        throw new ApiException((int)resp.StatusCode, error);
    }
}
