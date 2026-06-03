namespace CCL.MES.Shared;

/// <summary>
/// HTTP route version prefix for the new hybrid API. Reserved as <c>v2</c>
/// to avoid colliding with any legacy controllers that may already sit on
/// <c>/api/v1/*</c> in CCL.MES.Web. The legacy Web project never claims
/// <c>/api/v2/*</c>, so the new API can run side-by-side without route
/// conflict if the two hosts are ever co-located.
/// </summary>
public static class ApiVersion
{
    public const string Prefix = "api/v2";
}
