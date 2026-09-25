using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;

namespace CCL.MES.Hybrid.Web.Session;

/// <summary>
/// Cookie phiên web: chỉ chứa KHOÁ phiên (không phải token), đã mã hoá bằng Data
/// Protection. HttpOnly (JavaScript không đọc được) · SameSite=Strict · Path=/ ·
/// hết hạn cùng lúc với phiên (12 giờ). Secure chỉ bật khi request là HTTPS — trên
/// HTTP LAN hiện tại trình duyệt sẽ không gửi cookie Secure (sẽ bật cùng HTTPS).
/// </summary>
public static class WebSessionCookie
{
    public const string Name = "ccl_web_session";
    private const string Purpose = "CCL.MES.Hybrid.Web.SessionCookie.v1";

    public static void Write(HttpContext ctx, IDataProtectionProvider dp, string key, DateTimeOffset expiresAt)
        => ctx.Response.Cookies.Append(Name, dp.CreateProtector(Purpose).Protect(key), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Secure = ctx.Request.IsHttps,
            Expires = expiresAt,
            IsEssential = true,
        });

    public static void Delete(HttpContext ctx)
        => ctx.Response.Cookies.Delete(Name, new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict, HttpOnly = true });

    /// <summary>Khoá phiên trong cookie, nếu cookie hợp lệ (giải mã được). KHÔNG kiểm
    /// phiên còn sống — xem <see cref="ResolveForHostPage"/>.</summary>
    public static bool TryReadKey(HttpContext ctx, IDataProtectionProvider dp, out string key)
    {
        key = "";
        if (!ctx.Request.Cookies.TryGetValue(Name, out var raw) || string.IsNullOrEmpty(raw)) return false;
        try
        {
            key = dp.CreateProtector(Purpose).Unprotect(raw);
            return !string.IsNullOrEmpty(key);
        }
        catch (CryptographicException)
        {
            return false;   // giả mạo / khoá Data Protection đã đổi
        }
    }

    /// <summary>Trang host: khoá phiên để trao cho circuit mới, hoặc <c>null</c>. Cookie có
    /// mà phiên đã hết / bị xoá / giả ⇒ XOÁ cookie luôn để trình duyệt khỏi gửi lại.</summary>
    public static string? ResolveForHostPage(HttpContext ctx, IDataProtectionProvider dp, WebSessionStore store)
    {
        if (!ctx.Request.Cookies.ContainsKey(Name)) return null;
        if (TryReadKey(ctx, dp, out var key) && store.TryGet(key, out _)) return key;
        Delete(ctx);
        return null;
    }
}

/// <summary>Circuit (tab) hiện tại đang dùng phiên nào. Scoped = một bản mỗi tab.</summary>
public sealed class WebCircuitSession
{
    public string? Key { get; set; }
}

/// <summary>Nhờ trình duyệt đổi vé lấy cookie / xoá cookie — qua fetch tới
/// <see cref="WebSessionEndpoints"/>, vì circuit không có HTTP response để đặt cookie.</summary>
public interface IWebSessionCookie
{
    Task<bool> ClaimAsync(string ticket, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class JsWebSessionCookie : IWebSessionCookie
{
    private readonly IJSRuntime _js;
    private readonly ILogger<JsWebSessionCookie> _log;

    public JsWebSessionCookie(IJSRuntime js, ILogger<JsWebSessionCookie> log)
    {
        _js = js;
        _log = log;
    }

    public async Task<bool> ClaimAsync(string ticket, CancellationToken ct = default)
    {
        try
        {
            return await _js.InvokeAsync<bool>("cclWebSession.claim", ct, ticket);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or InvalidOperationException or TaskCanceledException)
        {
            // KHÔNG log vé.
            _log.LogWarning("[web-session] could not set session cookie ({Error}) — this tab works, a reload will ask to sign in again.", ex.GetType().Name);
            return false;
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        try
        {
            await _js.InvokeAsync<bool>("cclWebSession.clear", ct);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or InvalidOperationException or TaskCanceledException)
        {
            // Phiên phía server đã bị xoá trước đó ⇒ cookie còn sót cũng vô dụng.
            _log.LogInformation("[web-session] cookie clear skipped ({Error}); server session already removed.", ex.GetType().Name);
        }
    }
}
