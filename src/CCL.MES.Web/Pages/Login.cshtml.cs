using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Pages;

/// <summary>
/// Phase 2 login page. Pure Razor Page (not Blazor) because cookie issuance
/// via <c>HttpContext.SignInAsync</c> is far simpler in the MVC pipeline
/// than from a Blazor Server circuit. Anonymous access required.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IMesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public LoginModel(IMesDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    /// <summary>
    /// i18n key for the inline error, or <c>null</c> when no error.
    /// We surface the KEY (not the translated string) so the Razor view
    /// can call <c>Loc[key]</c> and pick up the current culture.
    /// </summary>
    public string? ErrorKey { get; private set; }

    public string ReturnPathForPicker() =>
        $"/login{(string.IsNullOrEmpty(Input.ReturnUrl) ? "" : $"?returnUrl={Uri.EscapeDataString(Input.ReturnUrl)}")}";

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User?.Identity?.IsAuthenticated == true)
            return Redirect(SafeReturnUrl(returnUrl));

        Input.ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorKey = "login.error.missing";
            return Page();
        }

        var username = Input.Username.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null
            || _hasher.VerifyHashedPassword(user, user.PasswordHash, Input.Password) == PasswordVerificationResult.Failed)
        {
            // Same error for "user not found" + "wrong password" so we don't
            // leak which usernames exist.
            ErrorKey = "login.error.invalid";
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("display_name", user.DisplayName ?? user.Username),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Redirect(SafeReturnUrl(Input.ReturnUrl));
    }

    private string SafeReturnUrl(string? candidate)
    {
        if (!string.IsNullOrEmpty(candidate) && Url.IsLocalUrl(candidate))
            return candidate;
        return "/";
    }

    public class LoginInput
    {
        [Required]
        public string Username { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public string? ReturnUrl { get; set; }
    }
}
