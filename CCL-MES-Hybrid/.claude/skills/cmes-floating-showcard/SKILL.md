---
name: cmes-floating-showcard
description: >
  How to build a SHOWCARD / detail dialog in CCL-MES Hybrid. Every keep-open-
  alongside detail window MUST reuse the shared <FloatingWindow> chrome — never
  hand-roll drag/resize/traffic-lights. Use when adding a new tab/showcard/detail
  dialog, or reviewing one.
---

# CMES floating showcard

**Rule (enforced):** any SHOWCARD — a detail/view/monitor window a user would
plausibly move, resize, or keep open alongside others — is built by wrapping its
body in `Shared/FloatingWindow.razor`. Do **not** re-create `.trace-win` /
`.fw-handle` / `.fw-traffic` markup or the `cclMesFloat` JS interop by hand.

CI gate: `scripts/gate-floating-showcard.sh` fails a PR that adds a
`*DetailDialog.razor` / `*Showcard*.razor` component without `<FloatingWindow>`.
Lesson: `docs/LESSONS-LEARNED.md` **L34**.

## Showcard vs. modal — pick the pattern

| Use `<FloatingWindow>` (showcard) | Use `<Modal>` (centred scrim) |
| --- | --- |
| Detail/monitor view, may open several at once, user drags/resizes/keeps open | Transactional: fill a form or confirm, then close |
| e.g. Traceability detail, a live monitor, a multi-record inspector | e.g. Create/Edit/Confirm/Import forms, yes/no confirms |

A transactional `<Modal>` can opt into showcard chrome with `Float="true"` (it
renders through `<FloatingWindow>`) — only when it truly becomes keep-open.

## Minimal example

```razor
@using CCL.MES.Hybrid.Client.Windows

<FloatingWindow Title="@Code" Subtitle="@_name"
                AriaLabel="@($"Detail {Code}")"
                WindowId="@WindowId" Rect="Rect" CascadeIndex="CascadeIndex"
                OnClose="OnClose" OnRectChanged="OnRectChanged">
    <TabBar>            @* optional row under the header, e.g. a tab strip *@
        ...
    </TabBar>
    <ChildContent>     @* the scrollable body *@
        ...
    </ChildContent>
</FloatingWindow>

@code {
    [Parameter] public string Code { get; set; } = "";
    [Parameter] public EventCallback OnClose { get; set; }
    // Pass-through so the PARENT owns multi-window state + persistence:
    [Parameter] public string WindowId { get; set; } = Guid.NewGuid().ToString("N");
    [Parameter] public WindowRect? Rect { get; set; }
    [Parameter] public int CascadeIndex { get; set; }
    [Parameter] public EventCallback<WindowRect> OnRectChanged { get; set; }
}
```

### Parent hosts N windows (cascade + persistence)

The parent page keeps a list of open windows, each with a stable `Id`, a
cascade index, and a restored rect from the session store. Mirror
`QualityTraceability.razor`:

```csharp
@inject CCL.MES.Hybrid.Client.Windows.IFloatingWindowStore WinStore
// open: reuse if already open (bring-to-front), else add with WinStore.Get(key)
// OnRectChanged: WinStore.Save(key, rect)
```

`IFloatingWindowStore` is registered in `AddCclHybridClient`. `WindowId`
distinguishes windows in `floating-window.js`; `CascadeIndex` offsets a new one.

## Parity checklist (must all hold)

- [ ] Drag by header; 8-way resize (N/S/E/W + 4 corners).
- [ ] Traffic-lights: minimize · maximize/restore · close (close outermost).
- [ ] Keyboard: Esc closes, arrows move, Shift+arrows resize.
- [ ] Multiple windows open independently + bring-to-front on click.
- [ ] Rect persists per session (reopen restores position/size).
- [ ] `DisposeAsync` runs `cclMesFloat.dispose` — no orphan listeners (RendererCrashBoundary).
- [ ] S9 responsive body (overflow-x auto, sticky heads, container queries).
- [ ] `role="dialog"` + `aria-label`.

`FloatingWindow` already provides all of the above — you inherit them by wrapping.
Cover new content with bUnit (see `FloatingWindowTests` + `QualityTraceabilityTests`).

## Do NOT

- Hand-write `.trace-win` / `.fw-handle` / `.fw-traffic` or call `cclMesFloat.*`
  directly in a page — that is what `FloatingWindow` is for.
- Force `Float="true"` on a confirm/edit modal just to make it draggable —
  transactional surfaces stay centred modals.

## Showcards can be INLINE — the gate scans markup, not just filenames (P11)

A showcard need NOT live in a `*Showcard*.razor` / `*DetailDialog*.razor` file.
The per-leg IPQC inspector was first hand-rolled as an inline overlay INSIDE
`LegsDashboard.razor` (a plain `<div role="dialog">` with only a `× Close` — no
drag/resize/traffic-lights). It dodged the old **filename-based** gate.

**Enforcement (extended):** `gate-floating-showcard.sh` now ALSO flags any `.razor`
that writes a literal `role="dialog"` but does **not** wrap `<FloatingWindow>`
(the two dialog primitives `FloatingWindow.razor` + `Modal.razor` are allow-listed).
So an inline showcard now FAILs CI with the exact `file:line`. A prompt asking for
a showcard also trips a `UserPromptSubmit` hook that echoes this workflow.

**Fix pattern (inline → showcard):** extract the inspector body into a small
component that wraps `<FloatingWindow>` (rich `HeaderContent` = the record identity,
`ChildContent` = the reused body), and let the PARENT own the multi-window list +
`IFloatingWindowStore` persistence (mirror `QualityTraceability.razor`). See
`IpqcLegShowcard.razor` (component) + `LegsDashboard.razor` `_ipqcWins` (parent host).

## Window geometry — DESKTOP-BOUNDED, mở FULL mặc định (Henry rule 2026-08-21)

**Rule (enforced ở `floating-window.js`, áp cho MỌI window/tab):**

1. **Mở tab nào cũng FULL trong desktop.** Window mới (không có rect đã lưu trong
   phiên) mở ở trạng thái **maximised-to-workspace** — lấp đầy vùng nội dung, KHÔNG
   phải một card nổi nhỏ. Người dùng bấm restore (đèn xanh) để thu nhỏ + kéo/resize.

2. **Maximise = vùng DESKTOP, KHÔNG phải full viewport.** Window maximised lấp đầy
   **`.app-content`** (bên phải sidebar, dưới top-bar, trên taskbar) — **KHÔNG BAO
   GIỜ che sidebar/topbar/taskbar**. Bounds đo từ `getBoundingClientRect()` của
   `.app-content` (trừ chiều cao `.taskbar`), qua hàm `workspaceBounds()`.

3. **Bám theo layout động.** Một `ResizeObserver` trên `.app-content` +
   `window.resize` gọi `refitMaximized()` → mọi window maximised re-fit khi rail
   collapse/expand hoặc viewport đổi. Không hardcode chiều rộng sidebar.

**Vì sao (RCA):** `.trace-win` là `position:fixed` → maximise cũ đặt
`{x:0,y:0,w:vw(),h:vh()}` = full màn hình che sidebar. Sửa: `doMaximize` +
`init`(default-open) + `refitMaximized` đều dùng `workspaceBounds()`.

**Khi thêm tab/window mới:** KHÔNG cần làm gì thêm — hành vi này ở tầng
`FloatingWindow`/`floating-window.js` dùng chung, mọi window tự hưởng. Đừng tự
đặt lại rect `vw/vh` cho maximise ở bất kỳ đâu.
