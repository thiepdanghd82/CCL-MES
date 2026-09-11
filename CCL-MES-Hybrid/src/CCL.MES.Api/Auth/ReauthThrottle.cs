using System.Collections.Concurrent;

namespace CCL.MES.Api.Auth;

/// <summary>
/// Hãm thử-sai cho ô KÝ ĐIỆN TỬ tại chuyền (Thiệp chốt 2026-09-11).
///
/// <para><b>Vì sao bắt buộc.</b> Skill <c>cmes-secrets-jwt</c> ghi thẳng:
/// <i>"Đừng thêm endpoint login mới mà không có delay/lockout. Brute-force HTTP
/// LAN là kịch bản thật."</i> Một ô nhập mật khẩu đặt ngay trên màn hình xưởng
/// CHÍNH LÀ một endpoint kiểu login: không hãm thì nó thành chỗ dò mật khẩu của
/// đồng nghiệp, và động cơ có sẵn — ký duyệt chính lô hàng mình vừa làm hỏng.</para>
///
/// <para><b>Khoá theo TÊN TÀI KHOẢN, không theo IP.</b> Cả xưởng đi chung một
/// máy và một đường mạng; khoá theo IP là khoá cả xưởng khi một người gõ sai.
/// Khoá theo tên thì chỉ tài khoản bị nhắm mới bị chặn — và đó đúng là tài
/// khoản cần được bảo vệ.</para>
///
/// <para><b>Giới hạn đã biết, nói ra chứ không giấu:</b> trạng thái nằm trong bộ
/// nhớ tiến trình, khởi động lại API là mất. Với mối đe doạ thật ở đây — người
/// đứng máy gõ tay — như thế là đủ: họ không khởi động lại được API. Đây KHÔNG
/// phải bản thay thế cho lockout đăng nhập toàn hệ thống (Phase 7 còn treo).</para>
/// </summary>
public sealed class ReauthThrottle
{
    /// <summary>Số lần sai liên tiếp trước khi khoá.</summary>
    public const int MaxFailures = 5;

    /// <summary>Cửa sổ đếm — sai rải rác cả ngày không cộng dồn thành khoá.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Khoá bao lâu sau khi đủ số lần sai.</summary>
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset FirstFailureAt;
        public DateTimeOffset? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> _byUser =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<DateTimeOffset> _now;

    public ReauthThrottle() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>Đồng hồ tiêm được — để test khoá/mở khoá mà không phải chờ thật.</summary>
    public ReauthThrottle(Func<DateTimeOffset> now) => _now = now;

    /// <summary>Đang bị khoá thì còn bao lâu; <c>null</c> = không khoá.</summary>
    public TimeSpan? LockedFor(string? username)
    {
        var key = Key(username);
        if (key is null || !_byUser.TryGetValue(key, out var e)) return null;
        if (e.LockedUntil is not { } until) return null;

        var left = until - _now();
        if (left > TimeSpan.Zero) return left;

        // Hết hạn khoá — xoá hẳn để lần sau đếm lại từ đầu.
        _byUser.TryRemove(key, out _);
        return null;
    }

    /// <summary>Ghi một lần ký SAI. Trả về true nếu lần này làm tài khoản bị khoá.</summary>
    public bool RegisterFailure(string? username)
    {
        var key = Key(username);
        if (key is null) return false;
        var now = _now();

        var e = _byUser.AddOrUpdate(key,
            _ => new Entry { Failures = 1, FirstFailureAt = now },
            (_, cur) =>
            {
                // Ngoài cửa sổ đếm ⇒ bắt đầu chuỗi mới, không cộng dồn vô hạn.
                if (now - cur.FirstFailureAt > Window)
                {
                    cur.Failures = 1;
                    cur.FirstFailureAt = now;
                    cur.LockedUntil = null;
                    return cur;
                }
                cur.Failures++;
                return cur;
            });

        lock (e)
        {
            if (e.Failures < MaxFailures || e.LockedUntil is not null) return false;
            e.LockedUntil = now + LockFor;
            return true;
        }
    }

    /// <summary>Ký ĐÚNG ⇒ xoá sạch lịch sử sai của tài khoản đó.</summary>
    public void RegisterSuccess(string? username)
    {
        var key = Key(username);
        if (key is not null) _byUser.TryRemove(key, out _);
    }

    private static string? Key(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : username.Trim();
}
