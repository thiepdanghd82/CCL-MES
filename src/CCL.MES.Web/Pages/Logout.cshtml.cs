using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CCL.MES.Web.Pages;

/// <summary>
/// Sign-out endpoint. POST-only against CSRF in spirit, but the
/// anti-forgery token is suppressed because the MainLayout form is
/// rendered from a Blazor circuit (no Razor Pages tag helper to emit
/// the hidden field) and "log me out" is not a state-changing action
/// an attacker can usefully abuse.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LogoutModel : PageModel
{
    private readonly IAuditWriter _audit;

    public LogoutModel(IAuditWriter audit) => _audit = audit;

    public async Task<IActionResult> OnPostAsync()
    {
        // Phase 6 Bước 5 — emit before sign-out so the principal is still
        // attached and we record who actually signed out.
        var (actor, role) = User.AuditIdentity();
        await _audit.EmitAsync(AuditAction.Logout, actor, role);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }

    /// <summary>Treat a stray GET as "go to login" without changing auth state.</summary>
    public IActionResult OnGet() => Redirect("/login");
}
