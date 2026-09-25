using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Web.Session;

/// <summary>Cấu hình phiên web — section <c>WebSession</c>.</summary>
public sealed class WebSessionOptions
{
    /// <summary>Phiên sống theo CA: hết hạn tuyệt đối sau khoảng này kể từ lúc đăng
    /// nhập, dù vẫn đang dùng (Henry chốt 2026-09-25: 12 giờ).</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Vé đổi cookie chỉ sống đủ cho một lệnh fetch ngay sau đăng nhập.</summary>
    public TimeSpan ClaimTicketLifetime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Hai tab cùng phiên làm mới bằng CÙNG refresh token trong khoảng này
    /// ⇒ tab sau dùng lại kết quả của tab trước (xem <see cref="RefreshCoalescer"/>).</summary>
    public TimeSpan RefreshCoalesceWindow { get; set; } = TimeSpan.FromMinutes(2);
}

public readonly record struct WebSessionSnapshot(
    string Subject, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Token của từng phiên web nằm PHÍA SERVER, theo một khoá ngẫu nhiên 256-bit; trình
/// duyệt chỉ giữ cookie mang khoá đó (đã mã hoá — xem <see cref="WebSessionCookie"/>).
///
/// <para><b>Dùng chung giữa mọi circuit là CỐ Ý</b> (singleton trong allowlist): hai tab
/// của cùng một người mang cùng cookie ⇒ cùng khoá ⇒ cùng một bản token. Tab A làm mới
/// token thì tab B thấy ngay bản mới — nếu mỗi tab giữ bản riêng, tab B sẽ dùng lại
/// refresh token đã bị xoay và API thu hồi CẢ HỌ token (đăng xuất mọi tab).</para>
///
/// <para><b>Giới hạn đã biết:</b> nằm trong RAM — khởi động lại web host là mọi người
/// phải đăng nhập lại (giống API: refresh store của API cũng nằm trong RAM).</para>
/// </summary>
public sealed class WebSessionStore
{
    private sealed class Entry
    {
        public required string Subject { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public string Access = "";
        public string Refresh = "";
    }

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Key, DateTimeOffset ExpiresAt)> _tickets = new(StringComparer.Ordinal);
    private readonly WebSessionOptions _opts;
    private readonly TimeProvider _clock;

    public WebSessionStore(IOptions<WebSessionOptions> opts, TimeProvider clock)
    {
        _opts = opts.Value;
        _clock = clock;
    }

    public int Count => _sessions.Count;

    public string Create(string accessToken, string refreshToken, string subject)
    {
        Prune();
        var key = NewSecret();
        _sessions[key] = new Entry
        {
            Subject = subject,
            ExpiresAt = _clock.GetUtcNow() + _opts.Lifetime,
            Access = accessToken,
            Refresh = refreshToken,
        };
        return key;
    }

    /// <summary>Chỉ trả phiên CÒN HẠN; phiên hết hạn bị xoá ngay lúc đọc.</summary>
    public bool TryGet(string key, out WebSessionSnapshot session)
    {
        if (_sessions.TryGetValue(key, out var e))
        {
            lock (e)
            {
                if (e.ExpiresAt > _clock.GetUtcNow())
                {
                    session = new WebSessionSnapshot(e.Subject, e.Access, e.Refresh, e.ExpiresAt);
                    return true;
                }
            }
            _sessions.TryRemove(key, out _);
        }
        session = default;
        return false;
    }

    /// <summary>Ghi cặp token mới (làm mới token). Phiên hết hạn ⇒ false, không hồi sinh.</summary>
    public bool Update(string key, string accessToken, string refreshToken)
    {
        if (!_sessions.TryGetValue(key, out var e)) return false;
        lock (e)
        {
            if (e.ExpiresAt <= _clock.GetUtcNow()) return false;
            e.Access = accessToken;
            e.Refresh = refreshToken;
            return true;
        }
    }

    public void Remove(string key) => _sessions.TryRemove(key, out _);

    /// <summary>Vé một lần để trình duyệt đổi lấy cookie HttpOnly — circuit Blazor không
    /// tự đặt cookie được (không có HTTP response).</summary>
    public string IssueClaimTicket(string key)
    {
        PruneTickets();
        var ticket = NewSecret();
        _tickets[ticket] = (key, _clock.GetUtcNow() + _opts.ClaimTicketLifetime);
        return ticket;
    }

    /// <summary>Đổi vé ⇒ khoá phiên. Vé bị XOÁ ngay khi đổi (dùng một lần), kể cả khi đã hết hạn.</summary>
    public bool TryRedeemTicket(string ticket, out string key)
    {
        key = "";
        if (!_tickets.TryRemove(ticket, out var t)) return false;
        if (t.ExpiresAt <= _clock.GetUtcNow()) return false;
        key = t.Key;
        return true;
    }

    private void Prune()
    {
        var now = _clock.GetUtcNow();
        foreach (var (k, e) in _sessions)
            if (e.ExpiresAt <= now) _sessions.TryRemove(k, out _);
    }

    private void PruneTickets()
    {
        var now = _clock.GetUtcNow();
        foreach (var (t, v) in _tickets)
            if (v.ExpiresAt <= now) _tickets.TryRemove(t, out _);
    }

    private static string NewSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
