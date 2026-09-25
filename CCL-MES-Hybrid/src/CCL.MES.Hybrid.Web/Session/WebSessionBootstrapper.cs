using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Shared.Auth;

namespace CCL.MES.Hybrid.Web.Session;

/// <summary>
/// Tải lại trang ⇒ circuit mới có khoá phiên từ cookie nhưng <see cref="IAuthSession"/>
/// còn trống. Khôi phục TRƯỚC khi giao diện render (xem WebRoot.razor), nếu không
/// router sẽ đá sang /login rồi mới quay lại.
///
/// <para><b>Vì sao gọi /auth/me thay vì chỉ đọc claim từ token:</b>
/// <see cref="IAuthSession.RestoreFromStorageAsync"/> chỉ dựng quyền từ JWT, để
/// <c>CurrentUserInfo</c> = null (tên hiển thị, username…). Nhiều màn hình đọc nó —
/// kể cả chốt "người duyệt khác người nộp" phía client. /auth/me còn là phép thử phiên
/// còn sống: access hết hạn ⇒ handler tự làm mới; làm mới thất bại ⇒ đăng xuất.</para>
/// </summary>
public sealed class WebSessionBootstrapper
{
    private readonly ITokenStore _tokens;
    private readonly IAuthSession _session;
    private readonly ICclApiClient _api;
    private readonly ILogger<WebSessionBootstrapper> _log;

    public WebSessionBootstrapper(ITokenStore tokens, IAuthSession session, ICclApiClient api, ILogger<WebSessionBootstrapper> log)
    {
        _tokens = tokens;
        _session = session;
        _api = api;
        _log = log;
    }

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(await _tokens.GetAccessTokenAsync(ct))) return;

        UserInfo me;
        try
        {
            me = await _api.GetMeAsync(ct);
        }
        catch (ApiException ex)
        {
            _log.LogInformation("[web-session] stored session rejected by API ({Status}) — sign-in required.", ex.StatusCode);
            await _session.SignOutAsync(ct);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // API tạm không tới được: giữ phiên, dựng quyền từ token; lần gọi API sau sẽ tự phân xử.
            _log.LogWarning("[web-session] API unreachable while restoring ({Error}) — using token claims only.", ex.GetType().Name);
            await _session.RestoreFromStorageAsync(ct);
            return;
        }

        // /auth/me có thể đã làm xoay token ⇒ đọc lại cặp MỚI NHẤT rồi mới dựng phiên.
        var access = await _tokens.GetAccessTokenAsync(ct);
        var refresh = await _tokens.GetRefreshTokenAsync(ct);
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return;
        await _session.ApplyTokenRotationAsync(new LoginResponse { AccessToken = access, RefreshToken = refresh, User = me }, ct);
    }
}
