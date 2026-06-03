# Phase 9 — Hub auth-expired reconnect PLAN

> **Status**: PLAN — chờ Henry duyệt 2 option + Q5 (sliding cookie
> yes/no) trước khi code.
> **Author**: 02/06/2026 sau khi đóng audit-retention track.
> **Trigger**: shop-floor báo realtime "chết im" sau ~8h khi máy
> Dashboard / Work Orders kanban chạy non-stop qua ca đêm — operator
> không thấy WO advance, OEE không refresh, nhưng UI cũ không hiển
> thị bất kỳ banner nào.

---

## 1. Root cause khảo sát

### 1.1 Cookie auth config — sliding 8h đã bật

`src/CCL.MES.Web/Program.cs:164-176`:

```csharp
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "ccl_mes_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;     // ← ĐÃ BẬT
    });
```

**SlidingExpiration = true** nghĩa là cookie tự renew khi user còn
hoạt động: nếu request HTTP đến trong nửa cuối khoảng 8h, ASP.NET
gia hạn cookie thêm 8h nữa. **Hết hạn cứng KHÔNG xảy ra khi user
đang chuyển trang/click**.

### 1.2 Vì sao SignalR vẫn chết im sau 8h?

Sliding renew xảy ra trên **HTTP request** (page navigation, form
post, API call). SignalR sau khi negotiate + connect xong **giữ
websocket open** — không gửi HTTP request nào nữa, chỉ websocket
frames. Cookie KHÔNG renew từ websocket traffic.

Kịch bản:

1. 22:00 ca đêm — operator mở Dashboard. Login OK → cookie set hết
   hạn 06:00 sáng. Websocket negotiate forward cookie → connect OK.
2. 22:00–06:00 — operator nhìn màn nhưng KHÔNG click/navigate.
   Websocket nhận `shopfloorChanged` push liên tục → trang refresh
   nhưng KHÔNG có HTTP request mới → cookie KHÔNG sliding renew.
3. 06:00 — cookie hết hạn cứng. Websocket vẫn còn open trên TCP
   level → push tiếp tục nhận miễn không có sự kiện reconnect.
4. 06:15 — wifi blip / proxy idle drop / browser tab background
   throttle → websocket gãy.
5. `WithAutomaticReconnect()` (default backoff [0, 2, 10, 30]s) cố
   reconnect → negotiate HTTP gửi cookie expired → **401**.
6. SignalR sau 4 lần retry vào state `Disconnected` **không có UI
   callback** → trang giữ data cũ. Operator KHÔNG biết kết nối đã
   chết. WO advance trên máy khác KHÔNG về tới UI này.

### 1.3 Client code hiện tại — đã có WithAutomaticReconnect, KHÔNG có UX

`src/CCL.MES.Web/Pages/Dashboard.razor:75-92` + `Pages/WorkOrders.razor:333-349`:

```csharp
_hub = new HubConnectionBuilder()
    .WithUrl(hubUri, options => { /* forward ccl_mes_auth cookie */ })
    .WithAutomaticReconnect()                    // default [0, 2, 10, 30]s
    .Build();
_hub.On<string>("shopfloorChanged", async _ => { ... });
await _hub.StartAsync();
```

Thiếu:
- Handler cho `_hub.Reconnecting += ...` (banner "đang kết nối lại").
- Handler cho `_hub.Reconnected += ...` (banner clear + refetch state).
- Handler cho `_hub.Closed += ...` (final state — reconnect attempts
  hết → banner "Phiên hết hạn, tải lại trang" + nút reload).
- KHÔNG cách phân biệt 401 (auth expired) vs network blip — cần đọc
  exception trong Closed callback.

### 1.4 Banner pattern hiện có

`Pages/WorkOrders.razor:101-106` đã có toast pattern (5s
auto-dismiss). Reuse được, **NHƯNG** banner phiên-hết-hạn cần:
- **Sticky** (không auto-dismiss — operator phải thấy + bấm reload)
- **Persistent** ngay cả khi page state đổi
- **Reload action** (call `Nav.NavigateTo("/login", forceLoad: true)`
  hoặc `Nav.NavigateTo(Uri.AbsoluteUri, forceLoad: true)`).

→ Cần component mới `<HubSessionBanner>` shared giữa Dashboard +
WorkOrders thay vì copy-paste 2 chỗ.

---

## 2. Đề xuất fix — 2 option

### Option A — Client reconnect handlers + sticky banner *(NÊN LÀM, chắc chắn)*

**Scope:**

1. Component mới `Shared/HubSessionBanner.razor`:
   - States: `Connected` (ẩn) / `Reconnecting` (banner vàng "Đang
     kết nối lại…") / `SessionExpired` (banner đỏ sticky "Phiên hết
     hạn — tải lại trang" + nút `[Tải lại]` → `forceLoad`) /
     `Disconnected` (banner cam "Mất kết nối — nhấn để kết nối lại"
     + nút `[Thử lại]` → reload page).
   - i18n keys EN/VI: `hub.banner.reconnecting / .session_expired /
     .disconnected / .reload_button / .retry_button`.

2. Helper service `HubConnectionExtensions.WireSessionHandlers(hub,
   onChange)`:
   - Wire `_hub.Reconnecting += ...` → callback "reconnecting"
   - Wire `_hub.Reconnected += ...` → callback "connected" + clear banner
   - Wire `_hub.Closed += async ex => { ... }` → inspect exception:
     - Nếu `ex is HttpRequestException` với 401 status code → state
       "SessionExpired" (auth dead, không tự reconnect được).
     - Nếu network error → state "Disconnected" (operator có thể
       thử lại sau, hoặc reload).
   - Reuse được — Dashboard + WorkOrders gọi cùng 1 helper.

3. Wire vào 2 page hiện tại (Dashboard.razor + WorkOrders.razor):
   - Thêm `<HubSessionBanner @ref="_banner" />` ngay đầu page render.
   - Sau `_hub.Build()` gọi `_hub.WireSessionHandlers(state =>
     _banner.UpdateState(state))`.

**Pros:**
- KHÔNG đụng `ShopfloorHub` server / `ShopfloorNotifier` broadcast
  contract / state machine.
- KHÔNG đụng auth config (cookie 8h sliding giữ nguyên).
- Operator thấy ngay khi mất kết nối → bấm reload trong < 3s,
  shop-floor không bị "chết im".
- Reuse navy + button + i18n có sẵn (#27 banner pattern).

**Cons:**
- Vẫn yêu cầu operator bấm reload mỗi 8h. Không tự động re-login.
- Nếu operator bỏ qua banner → tiếp tục thấy data cũ (nhưng banner
  sticky đỏ luôn hiển thị → khó bỏ qua).

**Effort:** ~150-200 LOC + 1 component + 1 helper + i18n 5 key x 2
ngôn ngữ. ~3-4h dev + verify trên 2 page.

---

### Option B — Sliding cookie expiration via websocket ping *(CHỜ HENRY CHỐT)*

**Cách 1 — Server-side: SignalR ping renew cookie tự động**

Khi user còn hoạt động (websocket frames đến/đi), trigger HTTP
request "/auth/heartbeat" mỗi vài phút từ client → ASP.NET sliding
renew kích hoạt → cookie tự gia hạn.

Implementation:
- Endpoint mới `GET /auth/heartbeat` returns 204 — chỉ để renew cookie.
- Client (Dashboard + WorkOrders) `setInterval(fetch('/auth/heartbeat'),
  20 * 60 * 1000)` (20 phút < 4h half-window).
- KHÔNG sửa cookie config; sliding đã bật → mỗi heartbeat tự renew.

**Cách 2 — Bật `o.ExpireTimeSpan = TimeSpan.FromHours(24)` hoặc dài hơn**

Đơn giản nhất: cookie 24h thay vì 8h. Operator ca 12h không bị
logout giữa ca. Nhưng:
- **Security implication**: token compromised valid 24h thay vì 8h.
- Compliance: ISO 27001 thường khuyến nghị ≤ 8h cho session token
  trong môi trường sản xuất. Phải check với security team.
- Auth log audit team thấy session dài hơn → cần report lý do.

**Pros (Option B - cả 2 cách):**
- Operator KHÔNG phải reload mỗi 8h.
- Tự nhiên với UX "shop-floor screen running 24/7".

**Cons (Option B - cả 2 cách):**
- App-wide auth change. Security implication.
- Cần legal/compliance review trước khi áp dụng.
- Cách 1 thêm endpoint mới + JS interval (extra dep).
- Cách 2 cookie dài hơn = window tấn công lớn hơn.

**Effort:**
- Cách 1: ~80 LOC server + ~20 LOC client/page = ~2h. Test cookie
  expire-renew cycle.
- Cách 2: 1-line change. Test session timeout = 24h.

→ **Đề xuất**: ship Option A trong PR sắp tới. Option B chờ Henry
duyệt config-policy + security signoff trước khi mở PR riêng.

---

## 3. Trade-off summary

| Tiêu chí | Option A (banner) | Option B-1 (heartbeat) | Option B-2 (cookie 24h) |
|---|---|---|---|
| Operator UX | Thấy banner → reload | Hoàn toàn transparent | Hoàn toàn transparent |
| Security | KHÔNG đổi | KHÔNG đổi cookie | ⚠ Window tăng |
| Compliance | KHÔNG đổi | KHÔNG đổi | ⚠ ISO 27001 review |
| Code effort | M (~150-200 LOC) | S (~100 LOC) | XS (1 line) |
| Đụng server logic | KHÔNG | Thêm endpoint | Đổi config |
| Đụng Notifier/Hub contract | KHÔNG | KHÔNG | KHÔNG |
| Phù hợp **ngay** | ✅ | ⚠ Cần B-1 + A để guard | ⚠ Cần A để guard |

**Em đề xuất**:
1. **Option A luôn ship** — defense-in-depth UX. Ngay cả khi B áp
   dụng sau, banner vẫn cần khi network blip / server restart.
2. **Option B-1 (heartbeat)** ship sau nếu Henry confirm operator UX
   "không bao giờ reload" là requirement. Cần audit cookie renewal
   trail trong audit log.
3. **Option B-2 (cookie 24h)** chỉ làm nếu compliance signoff —
   default em KHÔNG khuyên.

---

## 4. Q1..Q6

### Q1 — Option A scope
- **A (default)**: ship cùng PR. Banner sticky cho `SessionExpired`,
  banner thoáng cho `Reconnecting`, banner sticky-cam cho
  `Disconnected` (operator có thể thử reload thủ công).
- B: chỉ ship `SessionExpired` banner (đơn giản hóa).

→ A. Cả 3 state đều cần distinct UX.

### Q2 — Component shared cho Dashboard + WorkOrders
- **A (default)**: 1 component `<HubSessionBanner>` + 1 helper
  `WireSessionHandlers(hub, onChange)`. Reuse giữa 2 page.
- B: copy-paste 2 chỗ.

→ A. Lesson #27 — duplicate UI = duplicate logic risk.

### Q3 — Action button trên banner SessionExpired
- **A (default)**: `[Tải lại trang]` → `Nav.NavigateTo(Uri.AbsoluteUri,
  forceLoad: true)`. Browser navigate qua login redirect nếu cookie
  thật sự dead.
- B: `[Đăng nhập lại]` → `Nav.NavigateTo("/login", forceLoad: true)`
  trực tiếp.

→ A. forceLoad cùng URL → FallbackPolicy gate → /login redirect
nếu auth fail → sau khi login về lại đúng trang.

### Q4 — Detect 401 cho SessionExpired vs Disconnected
- **A (default)**: parse `ex.Message` hoặc `ex.InnerException` —
  SignalR client wrap 401 trong `HubConnection.Closed` exception.
  Specific check `Message.Contains("Status code '401'")` hoặc cast
  `HttpRequestException`. Nếu match → SessionExpired; nếu không
  → Disconnected.
- B: KHÔNG phân biệt — luôn dùng "Phiên/Mất kết nối — tải lại trang"
  generic banner.

→ A. UX khác nhau cho 2 case: SessionExpired thì RELOAD là cách
duy nhất; Disconnected thì có thể wait thêm.

### Q5 — Sliding cookie via heartbeat (Option B-1) — yes/no?
- **CHỜ HENRY CHỐT**.
- A: Có — ship sau Option A trong PR riêng.
- B: KHÔNG — Option A đủ; operator reload 8h là chấp nhận được.
- C: Bật cookie 24h (Option B-2) thay vì heartbeat — đơn giản hơn
  nhưng security implication.

→ Em đề xuất B. A có thể mở Phase 9.C sau nếu operator yêu cầu.
C → cần compliance signoff.

### Q6 — i18n key namespace
- **A (default)**: `hub.banner.*` (reconnecting / session_expired /
  disconnected / reload_button / retry_button).
- B: `common.banner.*` (generic — có thể reuse cho banner khác sau).

→ A. Specific cho hub session để tránh collision khi sau này thêm
generic banner.

---

## 5. Out of scope

- iOS Safari / Chrome Android websocket idle drop khác desktop —
  shop-floor dùng desktop ChromeOS / Win, ít quan trọng.
- Reconnect retry budget config (current default `[0, 2, 10, 30]s
  total 42s` — đủ cho transient blip).
- Push notification "session sắp hết hạn 5 phút trước" — premature
  optimization; nếu operator reload 1 lần mỗi ca là chấp nhận được.
- TLS termination layer behavior (HAProxy / nginx websocket idle
  timeout) — defer cho ops sysadmin nếu sau này blip nhiều.

---

## 6. Cấu trúc commit Option A (preview)

```
feat(hub): UI banner cho SignalR session-expired + reconnect handlers

* Shared/HubSessionBanner.razor (new) — 4 state UI + i18n.
* Services/HubConnectionExtensions.cs (new) — WireSessionHandlers
  helper, wire Reconnecting/Reconnected/Closed callbacks.
* Pages/Dashboard.razor — <HubSessionBanner> + WireSessionHandlers
  callback.
* Pages/WorkOrders.razor — same.
* Resources/SharedResource.{resx,vi.resx} — 5 i18n key x 2 ngôn ngữ.
* (KHÔNG đụng ShopfloorHub / ShopfloorNotifier / state machine.)
```

---

*Plan author: Claude. STOP — chờ Henry chốt Q5 (sliding cookie
yes/no/cookie 24h) + duyệt Option A scope trước khi tạo branch
`feat/hub-session-banner` base main.*
