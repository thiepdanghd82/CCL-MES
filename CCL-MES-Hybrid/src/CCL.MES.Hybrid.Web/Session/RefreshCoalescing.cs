using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCL.MES.Shared.Auth;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Web.Session;

/// <summary>
/// Gộp các lần làm mới token trùng nhau giữa CÁC TAB của cùng một phiên.
///
/// <para><b>Vì sao cần:</b> khoá chống làm mới song song trong
/// AuthorizationDelegatingHandler là field của TỪNG handler — đủ cho app Mac (một
/// handler), nhưng trên web mỗi tab có handler riêng. Hai tab cùng hết hạn access token
/// sẽ cùng gửi CÙNG refresh token; API coi lần thứ hai là dùng lại token đã xoay và thu
/// hồi cả họ token ⇒ đăng xuất mọi tab. Ở đây: cùng refresh token ⇒ chỉ MỘT request tới
/// API; tab khác nhận chung kết quả (cả khi tới muộn trong cửa sổ ngắn).</para>
///
/// <para>Khoá tra cứu là SHA-256 của refresh token — không giữ token trần làm khoá.
/// Chỉ nhớ kết quả THÀNH CÔNG; thất bại thì lần sau gọi lại thật.</para>
/// </summary>
public sealed class RefreshCoalescer
{
    public readonly record struct Result(HttpStatusCode Status, byte[] Body, string? ContentType);

    private readonly ConcurrentDictionary<string, Lazy<Task<Result>>> _inflight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (Result Result, DateTimeOffset At)> _done = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _window;

    public RefreshCoalescer(IOptions<WebSessionOptions> opts, TimeProvider clock)
    {
        _clock = clock;
        _window = opts.Value.RefreshCoalesceWindow;
    }

    public async Task<Result> RunAsync(string refreshToken, Func<Task<Result>> send)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var now = _clock.GetUtcNow();
        foreach (var (k, v) in _done)
            if (now - v.At >= _window) _done.TryRemove(k, out _);

        if (_done.TryGetValue(id, out var cached)) return cached.Result;

        var lazy = _inflight.GetOrAdd(id, _ => new Lazy<Task<Result>>(send, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var result = await lazy.Value;
            if ((int)result.Status is >= 200 and < 300)
                _done[id] = (result, _clock.GetUtcNow());
            return result;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<Result>>>(id, lazy));
        }
    }
}

/// <summary>Gắn vào HttpClient làm mới token (tên <c>CclApiRefresh</c>) của web host.</summary>
public sealed class RefreshCoalescingHandler : DelegatingHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly RefreshCoalescer _coalescer;

    public RefreshCoalescingHandler(RefreshCoalescer coalescer) => _coalescer = coalescer;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Post
            || request.RequestUri?.AbsolutePath.EndsWith("/auth/refresh", StringComparison.Ordinal) != true
            || request.Content is null)
            return await base.SendAsync(request, ct);

        var body = await request.Content.ReadAsByteArrayAsync(ct);
        string? token;
        try { token = JsonSerializer.Deserialize<RefreshTokenRequest>(body, Json)?.RefreshToken; }
        catch (JsonException) { token = null; }
        if (string.IsNullOrEmpty(token))
            return await base.SendAsync(request, ct);

        var contentType = request.Content.Headers.ContentType;
        var result = await _coalescer.RunAsync(token, async () =>
        {
            // CancellationToken.None: request dùng CHUNG cho nhiều tab — tab đầu đóng lại
            // không được huỷ kết quả mà tab khác đang chờ (HttpClient timeout vẫn áp).
            using var forward = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = new ByteArrayContent(body),
            };
            forward.Content.Headers.ContentType = contentType;
            foreach (var h in request.Headers) forward.Headers.TryAddWithoutValidation(h.Key, h.Value);
            using var resp = await base.SendAsync(forward, CancellationToken.None);
            return new RefreshCoalescer.Result(
                resp.StatusCode,
                await resp.Content.ReadAsByteArrayAsync(CancellationToken.None),
                resp.Content.Headers.ContentType?.ToString());
        });

        var message = new HttpResponseMessage(result.Status) { RequestMessage = request, Content = new ByteArrayContent(result.Body) };
        if (result.ContentType is not null && MediaTypeHeaderValue.TryParse(result.ContentType, out var mt))
            message.Content.Headers.ContentType = mt;
        return message;
    }
}
