using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Envelopes;
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
