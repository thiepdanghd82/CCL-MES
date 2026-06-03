using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Envelopes;

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

// ── Minimal NPI DTOs (legacy Domain entities live behind the API; we
//    mirror the shape we need rather than referencing CCL.MES.Domain
//    so the MAUI shell doesn't pull EF entities into the client). ──

public sealed record NpiPagedRaw<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed record NpiWorkCenter
{
    public long Id { get; init; }
    public string Code { get; init; } = "";
    public string? Description { get; init; }
    public string? Area { get; init; }
    // Legacy WorkCenter.Active is nullable bool — surface as nullable so
    // the UI can distinguish "not set" from "explicitly inactive".
    public bool? Active { get; init; }
}

public sealed record NpiRawMaterial
{
    public long Id { get; init; }
    public string PartNo { get; init; } = "";
    public string? PartDescription { get; init; }
    public string? Uom { get; init; }
}

public sealed record NpiRoutingOperation
{
    public long Id { get; init; }
    public string PartNo { get; init; } = "";
    public string? OpNo { get; init; }
    public string? WorkCenter { get; init; }
}

public sealed record NpiStructure
{
    public long Id { get; init; }
    public string ParentPartNo { get; init; } = "";
    public string ComponentPartNo { get; init; } = "";
    public decimal Qty { get; init; }
}
