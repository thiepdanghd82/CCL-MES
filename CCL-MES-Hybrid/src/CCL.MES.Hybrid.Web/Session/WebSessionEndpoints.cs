using Microsoft.AspNetCore.DataProtection;

namespace CCL.MES.Hybrid.Web.Session;

/// <summary>
/// Hai endpoint trình duyệt gọi bằng fetch (wwwroot/js/web-session.js):
/// <list type="bullet">
/// <item><c>POST /_session/claim</c> — đổi vé một lần ⇒ đặt cookie HttpOnly.</item>
/// <item><c>POST /_session/clear</c> — xoá phiên phía server + xoá cookie.</item>
/// </list>
/// Bắt buộc header <c>X-CCL-Session: 1</c>: form HTML từ trang khác không đặt được
/// header tuỳ ý (fetch khác origin thì phải qua preflight CORS mà host không cho) —
/// chặn kiểu gửi chéo trang. Vé chỉ circuit của đúng tab đó biết, sống 30 giây, dùng một lần.
/// </summary>
public static class WebSessionEndpoints
{
    public const string HeaderName = "X-CCL-Session";

    public static IEndpointRouteBuilder MapWebSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/_session/claim", async (HttpContext ctx, WebSessionStore store, IDataProtectionProvider dp) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (ctx.Request.Headers[HeaderName] != "1") return Results.BadRequest();

            ClaimRequest? body;
            try { body = await ctx.Request.ReadFromJsonAsync<ClaimRequest>(); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return Results.BadRequest(); }

            if (string.IsNullOrEmpty(body?.Ticket)
                || !store.TryRedeemTicket(body.Ticket, out var key)
                || !store.TryGet(key, out var session))
                return Results.BadRequest();

            WebSessionCookie.Write(ctx, dp, key, session.ExpiresAt);
            return Results.NoContent();
        });

        app.MapPost("/_session/clear", (HttpContext ctx, WebSessionStore store, IDataProtectionProvider dp) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (ctx.Request.Headers[HeaderName] != "1") return Results.BadRequest();
            if (WebSessionCookie.TryReadKey(ctx, dp, out var key)) store.Remove(key);
            WebSessionCookie.Delete(ctx);
            return Results.NoContent();
        });

        return app;
    }

    private sealed record ClaimRequest(string? Ticket);
}
