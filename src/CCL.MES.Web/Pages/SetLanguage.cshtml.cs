using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CCL.MES.Web.Pages;

/// <summary>
/// Writes the <c>.AspNetCore.Culture</c> cookie that
/// <c>CookieRequestCultureProvider</c> reads on every subsequent request,
/// then 302s the user back to <paramref name="returnUrl"/>. Anonymous so
/// it is reachable from the login screen before sign-in.
///
/// Supported languages: <c>en</c> (default), <c>vi</c>. Anything else is
/// silently coerced to <c>en</c>. <paramref name="returnUrl"/> is validated
/// against <see cref="IUrlHelper.IsLocalUrl"/> so an attacker cannot use
/// the endpoint as an open redirect.
/// </summary>
[AllowAnonymous]
public class SetLanguageModel : PageModel
{
    private static readonly string[] Supported = new[] { "en", "vi" };

    public IActionResult OnGet(string? lang, string? returnUrl)
    {
        var picked = !string.IsNullOrEmpty(lang) && Supported.Contains(lang)
            ? lang
            : "en";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(picked, picked)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false, // readable from JS if Phase 3 wants client-side display
                SameSite = SameSiteMode.Lax,
            });

        // Local-only redirect guard.
        return Redirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/");
    }
}
