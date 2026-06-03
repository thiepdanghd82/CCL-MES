namespace CCL.MES.Shared.Envelopes;

/// <summary>
/// Single shape for non-2xx responses across the new API. Uses an i18n key
/// rather than a literal English message so a Vietnamese client can render
/// the right text — clients hold the SharedResource translation map either
/// inline or by reaching back into the legacy resx via a downloadable
/// snapshot (P10.2).
///
/// Fields:
///   <c>Code</c> is a short stable key (eg. <c>"auth.invalid_credentials"</c>).
///   <c>MessageEn</c> is a fallback English string for ops / logs.
///   <c>Details</c> is an optional bag for field-specific validation errors
///   (key = field path, value = i18n key).
/// </summary>
public sealed record ApiError
{
    public string Code { get; init; } = "error.unknown";
    public string MessageEn { get; init; } = "An error occurred.";
    public Dictionary<string, string>? Details { get; init; }

    public static ApiError Of(string code, string messageEn) =>
        new() { Code = code, MessageEn = messageEn };
}
