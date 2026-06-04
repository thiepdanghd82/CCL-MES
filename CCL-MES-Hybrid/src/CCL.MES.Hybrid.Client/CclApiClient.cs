using System.Net;
using System.Net.Http.Json;
using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Drawings;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.QcSpecs;
using CCL.MES.Shared.Specs;
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

    public async Task<AdvanceWorkOrderResponse> AdvanceWorkOrderAsync(long workOrderId, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"/{ApiVersion.Prefix}/work-orders/{workOrderId}/advance");
        if (!string.IsNullOrWhiteSpace(_opts.DeviceId))
            msg.Headers.Add("X-Device-Id", _opts.DeviceId);
        using var resp = await _http.SendAsync(msg, ct);
        return await ReadAsAsync<AdvanceWorkOrderResponse>(resp, ct);
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
        generic ??= new ApiError { Code = "http.non_success", MessageEn = $"HTTP {(int)resp.StatusCode}" };
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
        error ??= new ApiError { Code = "http.non_success", MessageEn = $"HTTP {(int)resp.StatusCode}" };
        throw new ApiException((int)resp.StatusCode, error);
    }
}
