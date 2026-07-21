# Floating showcard — manual QA checklist (Traceability detail)

The drag/resize geometry lives in `wwwroot/js/floating-window.js` (pure JS,
Pointer Events). bUnit covers the Blazor wiring (open/close/cap/persist — see
`QualityTraceabilityTests`), but the pointer gestures themselves are verified by
hand in the **desktop app** (Mac Catalyst / WKWebView), where the WKWebView
quirks this module works around actually apply.

Run: API `:5100` → open the app → **Quality → Traceability data** → double-click
a WO row to open a showcard.

## Drag
- [ ] Drag the **header** → the window moves; body/table content does not.
- [ ] Press-drag on the **✕ button**, a **tab**, or while **selecting text** in
      the body → the window does **not** move (drag is suppressed there).
- [ ] Drag toward any screen edge → the **header stays reachable** (can't be
      pushed fully off-screen); release and it stays put.

## Resize (8 handles)
- [ ] **E / W** edges resize width; **N / S** edges resize height.
- [ ] **NE / NW / SE / SW** corners resize both axes; cursor matches
      (`ew`/`ns`/`nwse`/`nesw`).
- [ ] Resizing from **N** or **W** keeps the opposite edge anchored.
- [ ] Shrink past the minimum → clamps at **480×320** (table stays intact, no
      overflow break).
- [ ] Grow past the viewport → clamps to the viewport.

## Multi-window
- [ ] Open several rows → multiple independent showcards; each drags/resizes on
      its own.
- [ ] Click any card → it comes **to the front** (z-index raise on pointerdown).
- [ ] Opening a **WO already open** brings that one forward instead of opening a
      duplicate.
- [ ] A new card **cascades ~24px** off the previous one.
- [ ] Opening a **7th** card is blocked with the "tối đa 6 cửa sổ" notice.

## Maximize / persistence / keyboard
- [ ] **Double-click header** → maximize to full viewport; double-click again →
      restore to the previous rect. Resize handles hidden while maximized.
- [ ] Move/resize a card, close it, re-open the same WO → it **restores** to
      where it was left (per-session memory).
- [ ] Toolbar **"⤢ Reset windows"** → recenters all open cards + forgets saved
      positions.
- [ ] Focus the window, **arrow keys** move it, **Shift+arrows** resize it,
      **Esc** closes it.

## Non-modal + lifecycle
- [ ] With a card open, the **list underneath is still usable** (scroll, search,
      open another row) — no blocking scrim.
- [ ] Close a card → no console errors; open/close many in a row → no leak
      (listeners removed + pointer capture released on dispose).

## WKWebView note
Uses **Pointer Events + setPointerCapture** deliberately — HTML5
`draggable`/`dragstart` is unreliable in WKWebView / Mac Catalyst. See
**Lesson L31** in `docs/LESSONS-LEARNED.md`.
