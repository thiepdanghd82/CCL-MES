using System.Net;
using System.Net.Http.Json;
using CCL.MES.Shared;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Envelopes;
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

    // ── helpers ─────────────────────────────────────────────────────

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
