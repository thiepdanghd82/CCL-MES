namespace CCL.MES.Web.Resources;

/// <summary>
/// Marker class for <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>.
/// The matching .resx files live next to this file: SharedResource.en.resx (default,
/// authored by devs) and SharedResource.vi.resx (Vietnamese). ASP.NET Core resolves
/// the active culture via RequestLocalizationMiddleware which reads cookie
/// `.AspNetCore.Culture` first, then Accept-Language, then falls back to EN.
/// </summary>
public class SharedResource
{
}
