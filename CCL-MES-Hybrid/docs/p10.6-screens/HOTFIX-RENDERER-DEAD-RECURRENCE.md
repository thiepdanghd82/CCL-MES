# P10.6a hotfix — renderer-dead recurrence (Henry test 0/4 blocked)

**Branch**: `feat/p10.6a-settings-profile-password`
**Symptom**: Henry blocked at Login step 0 on Catalyst. Tab from
username → password → button doesn't work. Click Đăng nhập does
nothing. No spinner, no error banner, no redirect.

This is the EXACT pattern documented as "3 lessons" in earlier
incidents. The hypothesis check Henry asked for surfaced the actual
root cause — the lessons were **documented but not codified**.

---

## Root cause analysis

### Step 1 — Diff PR #91 vs main pre-merge

15 files changed, 1430 insertions, 3 deletions. Touched surfaces:

- 3 new Settings Razor pages (`/settings`, `/settings/profile`, `/settings/password`).
- `NavMenu.razor` — added a `CÀI ĐẶT` group always-visible (was previously inside `ScanEnabled + Admin/Sup/Eng` gate).
- New API controller + service + DTOs + tests + client methods + mapper codes + CSS.

**None of these touch Login.razor, the layouts, the keyboard fix, the
heartbeat service, or App.xaml.cs.** PR #91 is innocent of introducing
the regression.

### Step 2 — Lessons grep on main pre-PR #91

User's explicit instruction:
> Kiểm tra 3 lessons có thực sự còn nguyên trên main sau merge #91 không — confirm bằng grep, không suy đoán.

```
$ grep -c "wkMatchesCatalyst\|window.webkit" \
    CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor/Shared/MacCatalystKeyboardFix.razor
0
$ find CCL-MES-Hybrid -name "GlobalErrorLogger*"
(no matches)
$ find CCL-MES-Hybrid -name "RendererCrash*"
(no matches)
$ grep -c "SafeWaitNextTickAsync\|outer guard" \
    CCL-MES-Hybrid/src/CCL.MES.Hybrid/Services/DeviceHeartbeatHostedService.cs
0
$ grep -c "<InputText" CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor/Pages/Login.razor
2
```

**Confirmed: 0 of 3 lessons present on main.** The P10.5g hotfix series
(commits c4ad008 + 0604df0 + 3fff2d0) was NOT merged into main even
though PR #90's title implies it. Only the original `feat(p10.5g)`
commit (151096a) reached main; the three hardening + LESSONS commits
landed on the feature branch but were not part of the final merge.

### Step 3 — Why does the bug surface now if it was always live?

The bug class — `<InputText> + @bind-Value:event="oninput" +
@onkeydown` throws `ChangeEventArgs cannot be converted to System.String`
on every keystroke — has been live on main since before P10.5g. The
single-signal MacCatalystKeyboardFix UA detect has been brittle since
the same era.

Two events compounded recently:
1. The Catalyst SDK + macOS combo Henry runs now likely has a UA token
   shift that breaks the single-signal detect. WKWebView surface probe
   added in 3fff2d0 handles this — but that code wasn't on main.
2. The InputText throw silently corrupts the renderer dispatcher.
   Without `RendererCrashBoundary` the failure is invisible — symptom
   is "click does nothing", same as Tab not working.

**Both classes of bug have been live the whole time.** Earlier
operator-side "all green" reports almost certainly involved
auto-fill or paste rather than character-by-character keyboard input
(which is what the InputText combo trips).

### Step 4 — NavMenu null-user hypothesis (the user's #1)

Refuted: Login uses `@layout EmptyLayout` which does NOT include
`<NavMenu />`. Looking at the layouts:
```razor
EmptyLayout.razor:   <div class="empty-layout">@Body</div>  <MacCatalystKeyboardFix />
MainLayout.razor:    <div class="app-shell"><NavMenu /><main>…@Body…</main></div>  <MacCatalystKeyboardFix />
```
NavMenu only renders on `MainLayout`, which only mounts for
`[Authorize]` routes. The Settings group is rendered to
authenticated users only. Even my new `CÀI ĐẶT` always-on entries
sit inside `<nav>` of `<NavMenu />` which itself only ever appears
post-login.

### Step 5 — SettingsController DI hypothesis (the user's #2)

Refuted: `UserProfileService` is registered as `AddScoped` and only
constructed on first request to a Settings endpoint. Login flow
(`POST /api/v2/auth/login`) does not touch it. The service has no
static constructor, no module initializer, no startup-pipeline
side-effects.

---

## Fix

Re-apply the three lessons IN CODE on top of PR #91, plus convert the
new Settings pages preventatively. Same content as 3fff2d0 +
0604df0 + the 5g hardening commits, plus targeted CI guards.

### Layer 1 — Background path (3 sub-pieces)

| File | Change |
| --- | --- |
| NEW `src/CCL.MES.Hybrid/Services/GlobalErrorLogger.cs` | `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` (with `SetObserved()`). Rolling 50-line file at `FileSystem.AppDataDirectory/logs/error.log`. |
| MODIFY `src/CCL.MES.Hybrid/MauiProgram.cs` | `GlobalErrorLogger.Install()` BEFORE `MauiApp.CreateBuilder()`. |
| MODIFY `src/CCL.MES.Hybrid/Services/DeviceHeartbeatHostedService.cs` | Outer try/catch wrapping `RunLoopAsync` end-to-end; new `SafeWaitNextTickAsync` wrap on `PeriodicTimer.WaitForNextTickAsync`. |
| MODIFY `src/CCL.MES.Hybrid/App.xaml.cs` | Per-service try/catch INSIDE the foreach (was wrapping the whole loop). |

### Layer 2 — Foreground render path

| File | Change |
| --- | --- |
| NEW `src/CCL.MES.Hybrid.Razor/Shared/RendererCrashBoundary.razor` | Subclasses `ErrorBoundaryBase`; OnErrorAsync logs sentinel `[renderer-crash]` to console.error + Console.WriteLine; VN fallback card + Tải lại button. |
| MODIFY `src/CCL.MES.Hybrid.Razor/Shared/MainLayout.razor` | Wrap `@Body` in `<RendererCrashBoundary>`. |
| MODIFY `src/CCL.MES.Hybrid.Razor/Shared/EmptyLayout.razor` | Wrap `@Body` in `<RendererCrashBoundary>`. |
| MODIFY `src/CCL.MES.Hybrid.Razor/Shared/MacCatalystKeyboardFix.razor` | Dual-signal detect (UA combo OR WKWebView probe) + boot log `[keyboard-fix] ua=… wk=… active=…` + always-on JS `[js-uncaught]` / `[js-unhandled-rejection]` capture. |

### Layer 3 — ChangeEventArgs throw

| File | Change |
| --- | --- |
| MODIFY `Login.razor` | InputText combo → plain `<input> + @bind + @bind:event="oninput" + @onkeydown` (Lock.razor pattern). EditForm + DataAnnotationsValidator dropped; HandleSubmitAsync already does Required check inline. |
| MODIFY `SettingsProfile.razor` | Same conversion preventatively. |
| MODIFY `SettingsPassword.razor` | Same conversion preventatively. |

### Layer 4 — Regression-guard tests

| File | Tests added |
| --- | --- |
| NEW `tests/.../Layout/MacCatalystKeyboardFixRegressionTests.cs` | 6 tests: both layouts contain `<MacCatalystKeyboardFix />`, boot-log sentinel, dual signals, both layouts wrap `<RendererCrashBoundary>`, RendererCrashBoundary inherits ErrorBoundaryBase + uses OnErrorAsync + emits `[renderer-crash]` sentinel, MacCatalystKeyboardFix carries `[js-uncaught]` + `[js-unhandled-rejection]`. |
| NEW `tests/.../Layout/BackgroundCrashContainmentTests.cs` | 6 tests: `MauiProgram` ordering (Install BEFORE CreateBuilder), GlobalErrorLogger wires AppDomain + TaskScheduler with SetObserved, heartbeat outer-guard + SafeWaitNextTickAsync present, App.xaml.cs per-service try INSIDE foreach, three Razor pages (Login + SettingsProfile + SettingsPassword) lack `<InputText`, repo-wide tripwire for the InputText combo. |

---

## Verify

| Check | Result |
| --- | --- |
| `dotnet build CCL-MES-Hybrid.sln -c Debug` | **0 errors** (1 pre-existing path warning) |
| `dotnet test tests/CCL.MES.Hybrid.Client.Tests` | **443 / 443 PASS** (+12 since the pre-hotfix P10.6a) |
| `dotnet test tests/CCL.MES.Api.Tests` | **154 / 154 PASS** (unchanged — server-side untouched by hotfix) |

The 12 new client tests catch every layer:
- MauiProgram ordering — bumps if the install call moves below CreateBuilder.
- GlobalErrorLogger wiring — bumps if AppDomain/TaskScheduler handler drops or SetObserved removed.
- Heartbeat outer-guard — bumps if SafeWaitNextTickAsync or GlobalErrorLogger.Log dropped.
- App per-service try — bumps if the try moves outside the foreach.
- Login + SettingsProfile + SettingsPassword plain-input — fails CI with the offending file path the moment InputText returns.
- Repo-wide InputText+bind-Value:event=oninput tripwire — same shape, any future page.
- Layout RendererCrashBoundary + KeyboardFix presence — bumps if either tag drops.

---

## Henry verify (≤ 90 s on Mac Catalyst)

1. Clean rebuild Catalyst → launch.
2. Mở Safari → Develop → Mac Catalyst → CCL MES → Console. Confirm
   exactly one line at boot:
   ```
   [keyboard-fix] ua=1 wk=1 active=1
   ```
   (If `wk=1`, the WKWebView probe is doing the heavy lifting and the
   UA token alone wouldn't have lit up the script — that's the
   condition that broke this incident.)
3. Login screen: type `admin` character-by-character into username.
   Text should appear, no `[renderer-crash]` card.
4. **Tab** → focus password. Type `admin`. **Enter** → button **DIMS**
   ("Đang đăng nhập…"), redirect to `/`.
5. After login: sidebar `CÀI ĐẶT` group shows Tổng quan / Hồ sơ / Mật khẩu.
6. `/settings/profile` → edit DisplayName → Tab/Enter → green "Đã lưu thay đổi".
7. `/settings/password` → 5 paths: blank / mismatch / short /
   wrong_current / happy. Re-login với mật khẩu mới.
8. Inspect `~/Library/Application Support/com.ccl.mes.hybrid/logs/error.log` —
   should contain ONLY the `install` sentinel under normal flow.

If ANY of (3) typing / (4) click+redirect fails:
- Forward the Safari Web Inspector Console dump (look for
  `[js-uncaught]` / `[renderer-crash]` / `[ccl-err]`)
- Forward `error.log` content
- That gives the next round a verifiable cause instead of speculation.

---

## Why this won't recur this time

The 12 regression-guard tests are CI canaries. The earlier "lessons"
were prose in `HOTFIX-RENDERER-CRASH-REPORT.md`; a merge / revert /
refactor could erase them without anyone noticing. The new tests fail
the build the moment any guard rail breaks — same enforcement
mechanism used for `dead-code lint`, `commit message style`, and
`runtime-deps` over in Ops Control. Specifically:

```
$ dotnet test --filter "BackgroundCrashContainmentTests|MacCatalystKeyboardFixRegressionTests"
12 / 12 PASS
```

A revert of any layer breaks at least one of those tests within
seconds of `git push`, before the change reaches operator hardware.

🤖 Hotfix generated with [Claude Code](https://claude.com/claude-code)
