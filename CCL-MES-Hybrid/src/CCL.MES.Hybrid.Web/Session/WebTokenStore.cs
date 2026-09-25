using System.Security.Claims;
using CCL.MES.Hybrid.Client.Auth;

namespace CCL.MES.Hybrid.Web.Session;

/// <summary>
/// <see cref="ITokenStore"/> của web: không giữ token trong tab, mà đọc/ghi phiên dùng
/// chung trong <see cref="WebSessionStore"/> theo khoá của tab (<see cref="WebCircuitSession"/>).
///
/// <list type="bullet">
/// <item>Lưu khi tab CHƯA có phiên (đăng nhập) ⇒ tạo phiên + nhờ trình duyệt đổi vé lấy cookie.</item>
/// <item>Lưu khi đã có phiên của CÙNG người (làm mới token) ⇒ ghi đè token, cookie giữ nguyên.</item>
/// <item>Lưu khi đã có phiên của NGƯỜI KHÁC (đăng nhập tài khoản khác trên cùng trình
///       duyệt) ⇒ bỏ phiên cũ, tạo phiên mới — không để hai người dùng chung một khoá.</item>
/// <item>Xoá (đăng xuất / làm mới thất bại) ⇒ xoá phiên phía server TRƯỚC (cookie còn sót
///       cũng vô dụng), rồi mới nhờ trình duyệt xoá cookie.</item>
/// </list>
/// </summary>
public sealed class WebTokenStore : ITokenStore
{
    private readonly WebSessionStore _store;
    private readonly WebCircuitSession _circuit;
    private readonly IWebSessionCookie _cookie;
    private readonly ILogger<WebTokenStore> _log;

    public WebTokenStore(WebSessionStore store, WebCircuitSession circuit, IWebSessionCookie cookie, ILogger<WebTokenStore> log)
    {
        _store = store;
        _circuit = circuit;
        _cookie = cookie;
        _log = log;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        => Task.FromResult(Current()?.AccessToken);

    public Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
        => Task.FromResult(Current()?.RefreshToken);

    public async Task SaveAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        var subject = SubjectOf(accessToken);
        var key = _circuit.Key;
        if (key is not null && _store.TryGet(key, out var existing))
        {
            if (existing.Subject == subject && _store.Update(key, accessToken, refreshToken))
                return;
            _store.Remove(key);   // người khác đăng nhập trên cùng trình duyệt
        }

        var newKey = _store.Create(accessToken, refreshToken, subject);
        _circuit.Key = newKey;
        await _cookie.ClaimAsync(_store.IssueClaimTicket(newKey), ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var key = _circuit.Key;
        _circuit.Key = null;
        if (key is not null) _store.Remove(key);
        await _cookie.ClearAsync(ct);
    }

    private WebSessionSnapshot? Current()
    {
        var key = _circuit.Key;
        if (key is null) return null;
        if (_store.TryGet(key, out var s)) return s;
        _circuit.Key = null;   // phiên đã hết hạn / bị xoá ở tab khác
        return null;
    }

    /// <summary>Id người dùng trong JWT. API phát bằng JwtSecurityTokenHandler nên
    /// NameIdentifier ra thành "nameid"; các tên còn lại để phòng đổi handler.</summary>
    public static string SubjectOf(string accessToken)
    {
        var principal = JwtClaims.Parse(accessToken);
        foreach (var type in new[] { "nameid", ClaimTypes.NameIdentifier, "sub", "unique_name", ClaimTypes.Name })
        {
            var v = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return "";
    }
}
