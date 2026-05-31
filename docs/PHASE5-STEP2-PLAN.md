# Phase 5 — Bước 2: SignalR hub auth (KHẢO SÁT + PHƯƠNG ÁN)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Sau khi chọn phương án em sẽ tạo `feat/phase5-hub-auth` để triển khai.

---

## 1. Khảo sát hiện trạng

### 1.1 Kiến trúc — xác nhận Blazor Server (KHÔNG có WASM)

| File:line | Trích |
|---|---|
| `src/CCL.MES.Web/CCL.MES.Web.csproj:1` | `<Project Sdk="Microsoft.NET.Sdk.Web">` |
| `src/CCL.MES.Web/CCL.MES.Web.csproj:14` | PackageReference `Microsoft.AspNetCore.SignalR.Client@10.0.8` (CLIENT lib trên SERVER) |
| `src/CCL.MES.Web/Program.cs:80` | `builder.Services.AddServerSideBlazor();` |
| `src/CCL.MES.Web/Program.cs:117` | `app.MapBlazorHub();` |
| `src/CCL.MES.Web/Pages/_Host.cshtml:19` | `<component type="typeof(CCL.MES.Web.App)" render-mode="ServerPrerendered" />` |

**Kết luận**: Blazor Server thuần. Component code chạy trong **server-side circuit**, không có Browser-side WASM. `HubConnection` được khởi tạo trong `OnInitializedAsync` chạy **trên server**, dùng `Microsoft.AspNetCore.SignalR.Client` (.NET client lib) để gọi loopback tới chính server qua HTTP/WebSocket.

→ Hệ quả quan trọng: **cookie browser KHÔNG tự đính vào negotiate** vì `HttpClient` nội bộ trong `HubConnection` không truy cập được cookie jar của trình duyệt — nó là một HttpClient riêng trong process server.

### 1.2 Cấu hình hub hiện tại

| File:line | Trích |
|---|---|
| `src/CCL.MES.Web/Program.cs:52-64` | Cookie auth scheme: `ccl_mes_auth`, HttpOnly, SameSite=Lax, 8h sliding |
| `src/CCL.MES.Web/Program.cs:65-70` | `FallbackPolicy = RequireAuthenticatedUser` |
| `src/CCL.MES.Web/Program.cs:117-125` | Comment + `app.MapHub<ShopfloorHub>("/hubs/shopfloor").AllowAnonymous();` ← **chỗ cần đụng** |
| `src/CCL.MES.Web/Hubs/ShopfloorHub.cs:9-11` | `public class ShopfloorHub : Hub { }` — KHÔNG có `[Authorize]` |
| `src/CCL.MES.Web/Hubs/ShopfloorHub.cs:17-24` | `ShopfloorNotifier` chỉ gọi `_hub.Clients.All.SendAsync(...)` — server → client, không đụng auth |

### 1.3 Client-side HubConnection

`Dashboard.razor:64-80` và `WorkOrders.razor:82-104` xây HubConnection **giống nhau**:

```csharp
@using Microsoft.AspNetCore.SignalR.Client
...
_hub = new HubConnectionBuilder()
    .WithUrl(Nav.ToAbsoluteUri("/hubs/shopfloor"))
    .WithAutomaticReconnect()
    .Build();
_hub.On<string>("shopfloorChanged", async _ => { ... });
await _hub.StartAsync();
```

- **Không có** `options.AccessTokenProvider`.
- **Không có** `options.Cookies.Add(...)`.
- `Nav.ToAbsoluteUri("/hubs/shopfloor")` → `http://localhost:5080/hubs/shopfloor` (cùng origin với server).

Dispose: cả 2 page đều `DisposeAsync` đúng (`_hub.DisposeAsync()`).

---

## 2. Phương án

### Phương án A — Cookie forward thủ công qua `HttpMessageHandlerFactory` / `options.Cookies`

**Cách làm**:
1. Capture giá trị cookie `ccl_mes_auth` lúc **render đầu tiên** (lưu vào scoped service / cascading value từ `_Host.cshtml` qua `IHttpContextAccessor` — đây là KHE THỜI GIAN duy nhất HttpContext còn sống đúng trong Blazor Server).
2. Trong `HubConnectionBuilder.WithUrl(...)`, dùng overload có `options =>`:
   ```csharp
   .WithUrl(Nav.ToAbsoluteUri("/hubs/shopfloor"), options =>
   {
       var c = new System.Net.Cookie("ccl_mes_auth", capturedToken, "/", uri.Host);
       options.Cookies.Add(c);
   })
   ```
3. Bỏ `AllowAnonymous()` trên `MapHub`. Cookie scheme middleware sẽ giải mã cookie tại negotiate → cấp `ClaimsPrincipal` → `[Authorize]` (qua FallbackPolicy) pass.

**Ưu**:
- Smallest patch: ~40 LOC, 3 files (`_Host.cshtml.cs` mới hoặc dùng `OnGet` capture; `IHubAuthCookieAccessor` scoped service; 2 page tiêu thụ).
- KHÔNG cần auth scheme mới, KHÔNG cần endpoint token mới, KHÔNG đụng DB.
- Một source-of-truth identity (cookie) → debug dễ, audit dễ.

**Nhược / rủi ro**:
- `IHttpContextAccessor` trong Blazor Server **bị Microsoft khuyến cáo tránh** dùng cho auth-flow ngoài initial-render (ref: "Use the HttpContext object cautiously" trong docs Blazor Server) — vì HttpContext không an toàn lưu trữ ngoài lifetime request đầu. Mình PHẢI capture cookie value (string) **ngay** tại first-render rồi giữ trong scoped service của circuit, không giữ tham chiếu HttpContext.
- Nếu user logout-rồi-login cùng tab mà không reload, cookie cached có thể stale. Trên thực tế Blazor Server tear-down circuit khi navigation forceLoad / logout post-back, nên rủi ro nhỏ.
- Cookie 8h sliding → nếu circuit sống >8h và user idle, negotiate refresh có thể fail. Cũng nhỏ trong môi trường MES (operator login đầu ca).

**Độ phức tạp**: ⭐ (1/5)

---

### Phương án B — Ephemeral token + `AccessTokenProvider` + custom auth scheme

**Cách làm**:
1. Thêm endpoint `GET /api/hub-token` (cookie-auth required) trả token ngắn hạn (5 phút), HMAC-signed `userId|expiresAt` (hoặc bọc qua `IDataProtector`).
2. Thêm 1 custom AuthenticationHandler đọc token từ header `Authorization: Bearer ...` cho hub. Đăng ký schemes: cookie (mặc định) + hub-token (chỉ cho `/hubs/*`).
3. Trên `MapHub`: `.RequireAuthorization()` với scheme = `"HubToken"`.
4. Client: `await Http.GetFromJsonAsync<string>("/api/hub-token")` rồi `.WithUrl(..., o => o.AccessTokenProvider = () => Task.FromResult(token))`.

**Ưu**:
- Best practice "đúng sách" cho SignalR — separate concern: cookie cho UI, token cho realtime.
- Không phụ thuộc IHttpContextAccessor trong Blazor.
- Token có thể short-lived → giảm bề mặt rủi ro.
- Dễ mở rộng nếu sau này có WASM / native client cần SignalR.

**Nhược / rủi ro**:
- Lớn nhất: **2 auth schemes song song** → tăng surface lỗi nếu cấu hình lệch (rớt scheme name, rớt fallback).
- Phải quản lý HMAC key — thêm 1 env / KeyRing mới (giống `OPS_TOTP_KEY` bên Ops Control v1.2).
- ~150 LOC + endpoint + handler + 1 đoạn re-issue token khi sắp hết hạn (Blazor circuit dài hơn token).
- Test phức tạp hơn: phải test cả token-mint, token-validate, expiry, reconnect-with-fresh-token.

**Độ phức tạp**: ⭐⭐⭐ (3/5)

---

### Phương án C — `IHttpContextAccessor` đẩy thẳng cookie header (variant của A)

Đây thực ra là một biến thể của A: thay vì capture cookie value vào scoped service, ta inject `IHttpContextAccessor` trực tiếp trong page và đọc `Http.HttpContext?.Request.Cookies["ccl_mes_auth"]` ngay trong `OnInitializedAsync`.

**Tại sao tách**:
- ASP.NET Core docs WARN rằng `IHttpContextAccessor.HttpContext` trong Blazor Server có thể là `null` hoặc trỏ về context đã dispose nếu gọi sau initial render.
- Trong `OnInitializedAsync` của Blazor Server, **lần render đầu tiên có HttpContext** (vì là prerender pass), nhưng nếu page được mount lại bởi navigation interna (qua Router), `HttpContext` đã không còn.

**Khả thi nhưng** dễ bug hơn A vì lệ thuộc vào "may mắn" timing. Không khuyến nghị.

**Độ phức tạp**: ⭐⭐ (2/5) — đơn giản viết, nhưng nguy hiểm tinh vi.

---

## 3. Đề xuất

**Chọn Phương án A** (Cookie forward thủ công qua scoped capture từ `_Host.cshtml`).

Lý do:
1. **Đúng kiến trúc hiện tại**: Cookie là single source of identity của app. SignalR đi cùng origin nên forward cookie là semantic chính xác.
2. **Smallest blast radius**: không thêm auth scheme mới, không thêm key mới phải quản lý qua deploy.
3. **Pattern chuẩn cho Blazor Server**: capture cookie ở `_Host.cshtml.cs` (chỗ HttpContext còn sống đầy đủ), inject scoped accessor xuống circuit. Microsoft `BlazorServerAuthenticationStateProvider` cũng dùng pattern này.
4. **Test đơn giản**: chỉ cần curl smoke (anonymous → 401 negotiate; authenticated cookie → 101 switching protocols hoặc 200 long-polling).
5. **Reversible**: nếu sau này có WASM/native client, có thể bổ sung Phương án B song song mà không phá Phương án A.

Phương án B em đề xuất **deferred** sang sprint sau, đánh dấu trong FINAL-REPORT § "Phase 5+ TODO" — chỉ làm khi thực sự cần (vd. ship kiosk Android cần realtime).

---

## 4. Rủi ro chi tiết

| Hạng mục | Rủi ro | Mức | Giảm thiểu |
|---|---|---|---|
| Vỡ realtime đang chạy (Start/Pause/Finish → Dashboard) | Cookie capture sai key name / scoped service không inject → HubConnection 401 → Dashboard không reload | **TRUNG BÌNH** | Smoke test sau khi merge: login admin, mở Dashboard 2 tab, Start ở tab A, xác nhận tab B reload trong 1-2s |
| Ảnh hưởng `RedirectToLogin` / `FallbackPolicy` | Bỏ `AllowAnonymous()` trên MapHub không đụng FallbackPolicy hay route khác | **THẤP** | `MapHub` policy độc lập với `RequireAuthenticatedUser` của routing |
| Cần đụng DB | **Không** | — | — |
| Cookie stale sau logout-relogin cùng tab | Circuit cached cookie cũ → reconnect fail 401 | **THẤP** | Logout đã forceLoad → circuit teardown → reseed cookie. Verify trong smoke |
| Circuit sống dài (>8h) hết hạn cookie | Reconnect sau idle có thể fail | **THẤP** | Cookie sliding refresh trên mọi request thường (Razor pages, API). Operator MES thường active liên tục. Nếu vẫn lo: bổ sung re-fetch cookie qua endpoint trước reconnect (Phase 5+) |
| `Nav.ToAbsoluteUri("/hubs/shopfloor")` không khớp cookie domain | `Cookie.Domain` rỗng → cookie không gắn vào request | **THẤP** | Đặt `Cookie.Domain = uri.Host` (loopback OK) hoặc `Cookie.Path = "/"`. Test cẩn thận trong smoke |

**Không** đụng DB; không đụng migration; không đụng auth scheme; không đụng FallbackPolicy.

---

## 5. Kế hoạch test + DoD

### Smoke test (manual + curl)

| # | Bước | Kỳ vọng |
|---|---|---|
| 1 | `dotnet build` | 0 warning, 0 error |
| 2 | Anonymous: `curl -i http://localhost:5080/hubs/shopfloor/negotiate?negotiateVersion=1` | **401 Unauthorized** (trước Phase 5 Bước 2 là 200) |
| 3 | Login admin (cookie jar), `curl -b jar -i .../negotiate?...` | **200 OK** với `connectionId` + `availableTransports` |
| 4 | Browser admin login → mở `/dashboard` | KHÔNG còn lỗi 401 trong console; chỉ báo `● live` xanh |
| 5 | Browser admin login → mở `/workorders` 2 tab → Start ở tab A | Tab B reload trong 1-2s (qua `shopfloorChanged` event) |
| 6 | Browser operator login → mở `/dashboard` | Cũng realtime OK (Bước 2 không phân biệt role cho hub) |
| 7 | Browser admin → logout → mở `/login` lại → login lại → `/dashboard` | Realtime tiếp tục OK sau re-login (circuit teardown + reseed) |
| 8 | VI culture: `set-language?lang=vi` rồi mở `/dashboard` | Live indicator hiển thị "trực tiếp" (key `common.live`), realtime OK |
| 9 | NPI rows | 43 / 2127 / 38441 / 20530 — KHÔNG ĐỔI |
| 10 | Forbidden dirs guard | `Ops Control v1.2/`, `CMES/`, `Old ver/`, `SpecHub/` không bị đụng |

### Definition of Done (DoD)

- [ ] `MapHub<ShopfloorHub>("/hubs/shopfloor")` KHÔNG còn `.AllowAnonymous()`.
- [ ] Anonymous negotiate → 401.
- [ ] Authenticated negotiate → 200, HubConnection start success.
- [ ] Dashboard + WorkOrders realtime sự kiện vẫn chạy (manual test 10 đầu mục).
- [ ] `dotnet build` clean.
- [ ] Comment Phase 4+ trong Program.cs:118-124 được gỡ hoặc cập nhật ("Phase 5: cookie forward via scoped CookieAccessor").
- [ ] Data integrity: 4 bảng NPI count không đổi.
- [ ] PR `feat/phase5-hub-auth` base = `main` (giả sử PR #4 đã merge tại thời điểm bắt đầu Bước 2).
- [ ] STOP, báo cáo, chờ duyệt.

---

## 6. Files dự kiến đụng (chi tiết hơn để em review)

| File | Hành động | Ước LOC |
|---|---|---|
| `src/CCL.MES.Web/Services/HubCookieAccessor.cs` (mới) | Scoped service giữ token cookie capture từ `_Host.cshtml.cs` | ~25 |
| `src/CCL.MES.Web/Pages/_Host.cshtml.cs` (mới hoặc mở rộng) | `OnGet`: đọc `HttpContext.Request.Cookies["ccl_mes_auth"]` → set `HubCookieAccessor.Token` | ~20 |
| `src/CCL.MES.Web/Pages/_Host.cshtml` | Thêm `@model HostModel` hoặc giữ inline nếu logic nhỏ | ~2 |
| `src/CCL.MES.Web/Program.cs` | (a) `AddScoped<HubCookieAccessor>()`; (b) gỡ `.AllowAnonymous()` ở line 125; (c) cập nhật comment Phase 4+ → Phase 5 | ~5 |
| `src/CCL.MES.Web/Pages/Dashboard.razor` | `@inject HubCookieAccessor HubCookie` + `.WithUrl(uri, o => { if(HubCookie.Token != null) o.Cookies.Add(new Cookie("ccl_mes_auth", HubCookie.Token, "/", uri.Host)); })` | ~10 |
| `src/CCL.MES.Web/Pages/WorkOrders.razor` | Cùng pattern Dashboard | ~10 |

**Tổng**: ~70 LOC, 6 files. Không đụng DB, không đụng `Resources/*.resx`, không đụng `Hubs/ShopfloorHub.cs` (hub vẫn rỗng — auth gate ở MapHub).

---

## 7. Câu hỏi cho em duyệt

1. **Chọn phương án nào?** (Đề xuất: A — Cookie forward)
2. **Branch base**: tạo `feat/phase5-hub-auth` từ `main` (giả sử PR #4 đã merge) hay stack lên `feat/phase5-rbac`?
3. **Comment trong Program.cs**: anh muốn em ghi "Phase 5 — cookie forward via scoped CookieAccessor" hay viết dài hơn (1 đoạn giải thích lý do chọn pattern này cho người mới đọc code)?

Sau khi em duyệt 3 mục trên, em tạo branch + code + commit + push + PR + STOP báo cáo.
