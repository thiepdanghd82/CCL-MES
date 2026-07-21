// Floating-window controller for the Traceability detail showcards.
//
// Why Pointer Events (and NOT HTML5 draggable / dragstart):
//   HTML5 native drag is flaky inside WKWebView / Mac Catalyst — dragstart
//   frequently never fires and the drag image ghosts. Pointer Events +
//   setPointerCapture are reliable there and unify mouse / trackpad / touch.
//   This mirrors the same lesson the backup.js / clipboard.js helpers encode:
//   keep interaction on plain DOM primitives, never the fancy component path.
//
// Contract (window.cclMesFloat):
//   init(id, root, header, opts, dotnet) → gắn drag + 8 resize handle; trả id.
//   dispose(id) → removeEventListener + releasePointerCapture + clear. MUST be
//     called from the component's IAsyncDisposable so no listener outlives the
//     renderer (the project's RendererCrashBoundary must never see orphans).
//   bringToFront(id) / getRect(id) / setRect(id, rect) / toggleMaximize(id) /
//   nudge(id, dx, dy, dw, dh) / recenter(id, cascade).
//
// All geometry math lives here. Position/size are applied via transform:
//   translate() + inline width/height, batched in requestAnimationFrame, so a
//   pointermove never triggers a layout round-trip through Blazor. .NET only
//   receives the FINAL rect (on pointerup / resize-end / maximize / nudge) via
//   the DotNetObjectReference callback OnRectChanged — never per-pixel.

window.cclMesFloat = (() => {
    let zTop = 2000;                 // shared z-index stack across all windows
    const wins = new Map();          // id -> state
    const HANDLES = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw'];

    const MINW_DEFAULT = 480, MINH_DEFAULT = 320;
    const KEEP_X = 160;              // px of the card kept reachable horizontally
    const HEADER_SAFE = 44;          // px the header stays inside the top/bottom

    const vw = () => window.innerWidth || document.documentElement.clientWidth;
    const vh = () => window.innerHeight || document.documentElement.clientHeight;
    const clamp = (v, lo, hi) => (v < lo ? lo : (v > hi ? hi : v));

    // Keep the window on-screen: never let the whole card slide off, and always
    // keep the header row reachable (can't drag the title bar out of view).
    function clampRect(r, minW, minH) {
        const W = vw(), H = vh();
        r.w = clamp(r.w, minW, W);
        r.h = clamp(r.h, minH, H);
        r.x = clamp(r.x, -(r.w - KEEP_X), W - KEEP_X);
        r.y = clamp(r.y, 0, Math.max(0, H - HEADER_SAFE));
        return r;
    }

    // Effective min-height depends on collapse state: a minimized window is a
    // header-only strip, so its floor is the collapsed height, not st.minH —
    // otherwise dragging it would snap the body back open (re-expand to minH).
    function clampSt(st) {
        return clampRect(st.rect, st.minW, st.minimized ? st.collapsedH : st.minH);
    }

    function apply(st) {
        const r = st.rect;
        st.root.style.transform = `translate(${Math.round(r.x)}px, ${Math.round(r.y)}px)`;
        st.root.style.width = Math.round(r.w) + 'px';
        st.root.style.height = Math.round(r.h) + 'px';
    }

    // Batch DOM writes to the next frame so a burst of pointermove events
    // collapses to one paint (no layout-thrash per pixel).
    function scheduleApply(st) {
        if (st.raf) return;
        st.raf = requestAnimationFrame(() => { st.raf = 0; apply(st); });
    }

    function defaultRect(cascade) {
        const W = vw(), H = vh();
        const w = Math.min(1200, Math.max(MINW_DEFAULT, W - 80));
        const h = Math.min(800, Math.max(MINH_DEFAULT, H - 80));
        const off = (cascade || 0) * 24;   // cascade so a new card doesn't cover the last
        const x = Math.max(8, (W - w) / 2 + off);
        const y = Math.max(8, (H - h) / 2 - 40 + off);
        return { x, y, w, h, maximized: false };
    }

    function raise(st) { st.root.style.zIndex = String(++zTop); }

    function notify(st) {
        if (!st.dotnet) return;
        try {
            st.dotnet.invokeMethodAsync('OnRectChanged_JS', {
                x: st.rect.x, y: st.rect.y, w: st.rect.w, h: st.rect.h,
                maximized: !!st.maximized,
            });
        } catch (e) { /* component disposed mid-drag — safe to drop */ }
    }

    // ── drag (header) ────────────────────────────────────────────────
    function beginDrag(st, e) {
        // Never drag from an interactive control or an explicit no-drag zone
        // (✕ button, tab, text input). Text selection in the header stays put.
        if (e.target.closest('button, a, input, select, textarea, [data-fw-nodrag]')) return;
        if (e.button !== undefined && e.button !== 0) return;   // left button / touch only
        raise(st);
        if (st.maximized) return;      // a maximized window doesn't move
        st.drag = { px: e.clientX, py: e.clientY, ox: st.rect.x, oy: st.rect.y };
        try { st.header.setPointerCapture(e.pointerId); } catch (_) { }
        st.dragPointer = e.pointerId;
        e.preventDefault();
    }
    function moveDrag(st, e) {
        if (!st.drag) return;
        st.rect.x = st.drag.ox + (e.clientX - st.drag.px);
        st.rect.y = st.drag.oy + (e.clientY - st.drag.py);
        clampSt(st);
        scheduleApply(st);
    }
    function endDrag(st) {
        if (!st.drag) return;
        try { st.header.releasePointerCapture(st.dragPointer); } catch (_) { }
        st.drag = null;
        notify(st);
    }

    // ── resize (8 handles) ───────────────────────────────────────────
    function beginResize(st, dir, el, e) {
        if (st.maximized || st.minimized) return;   // no resize while max/min
        raise(st);
        st.resize = { dir, el, pid: e.pointerId, px: e.clientX, py: e.clientY,
            ox: st.rect.x, oy: st.rect.y, ow: st.rect.w, oh: st.rect.h };
        try { el.setPointerCapture(e.pointerId); } catch (_) { }
        e.preventDefault();
        e.stopPropagation();
    }
    function moveResize(st, e) {
        const z = st.resize;
        if (!z) return;
        const dx = e.clientX - z.px, dy = e.clientY - z.py, d = z.dir;
        let x = z.ox, y = z.oy, w = z.ow, h = z.oh;
        if (d.includes('e')) w = z.ow + dx;
        if (d.includes('s')) h = z.oh + dy;
        if (d.includes('w')) { w = z.ow - dx; x = z.ox + dx; }
        if (d.includes('n')) { h = z.oh - dy; y = z.oy + dy; }
        // Enforce the minimum by anchoring the opposite edge when pulling n/w.
        if (w < st.minW) { if (d.includes('w')) x -= (st.minW - w); w = st.minW; }
        if (h < st.minH) { if (d.includes('n')) y -= (st.minH - h); h = st.minH; }
        st.rect.x = x; st.rect.y = y; st.rect.w = w; st.rect.h = h;
        clampRect(st.rect, st.minW, st.minH);
        scheduleApply(st);
    }
    function endResize(st) {
        const z = st.resize;
        if (!z) return;
        try { z.el.releasePointerCapture(z.pid); } catch (_) { }
        st.resize = null;
        if (st.raf) { cancelAnimationFrame(st.raf); st.raf = 0; }
        apply(st);      // guarantee the final frame matches st.rect exactly
        notify(st);     // then persist the exact rect back to Blazor
    }

    // Collapse to a header-only strip (macOS yellow "minimize"). Restores the
    // pre-collapse height on toggle. Resize handles + body/tabs hide via the
    // .trace-win-min CSS class; the header stays draggable.
    function doMinimize(st) {
        if (st.maximized) doMaximize(st, 0);   // leave fullscreen before collapsing
        if (st.minimized) {
            st.minimized = false;
            st.root.classList.remove('trace-win-min');
            st.rect.h = st.savedH || st.minH;
            st.savedH = 0;
            clampRect(st.rect, st.minW, st.minH);
        } else {
            st.minimized = true;
            st.savedH = st.rect.h;
            st.root.classList.add('trace-win-min');
            // Header height is stable once the collapse class is applied (it
            // only hides siblings, not the header itself).
            st.collapsedH = (st.header.offsetHeight || HEADER_SAFE) + 2;
            st.rect.h = st.collapsedH;
        }
        raise(st);
        apply(st);
        notify(st);
    }

    function doMaximize(st, cascadeFallback) {
        if (st.minimized) {                    // un-collapse before fullscreen
            st.minimized = false;
            st.root.classList.remove('trace-win-min');
            st.rect.h = st.savedH || st.minH;
            st.savedH = 0;
        }
        if (st.maximized) {
            st.maximized = false;
            st.rect = { ...(st.saved || defaultRect(cascadeFallback || 0)) };
            st.root.classList.remove('trace-win-max');
        } else {
            st.saved = { ...st.rect };
            st.maximized = true;
            st.rect = { x: 0, y: 0, w: vw(), h: vh(), maximized: true };
            st.root.classList.add('trace-win-max');
        }
        clampRect(st.rect, st.minW, st.minH);
        raise(st);
        apply(st);
        notify(st);
    }

    return {
        init(id, root, header, opts, dotnet) {
            opts = opts || {};
            const minW = opts.minW || MINW_DEFAULT, minH = opts.minH || MINH_DEFAULT;
            let rect;
            if (opts.rect && typeof opts.rect.w === 'number' && opts.rect.w > 0) {
                rect = { x: opts.rect.x, y: opts.rect.y, w: opts.rect.w, h: opts.rect.h,
                    maximized: !!opts.rect.maximized };
            } else {
                rect = defaultRect(opts.cascade || 0);
            }
            const st = { id, root, header, dotnet, rect, minW, minH,
                maximized: !!rect.maximized, minimized: false, saved: null,
                savedH: 0, collapsedH: 0, raf: 0, drag: null, resize: null };
            wins.set(id, st);

            if (st.maximized) {
                st.saved = defaultRect(opts.cascade || 0);
                st.rect = { x: 0, y: 0, w: vw(), h: vh(), maximized: true };
                root.classList.add('trace-win-max');
            }
            clampRect(st.rect, minW, minH);
            apply(st);
            raise(st);
            root.style.visibility = 'visible';   // revealed only after first paint is positioned

            // Header: drag + double-click maximize/restore.
            const hDown = (e) => beginDrag(st, e);
            const hMove = (e) => moveDrag(st, e);
            const hUp = () => endDrag(st);
            const hDbl = (e) => {
                if (e.target.closest('button, a, input, select, textarea, [data-fw-nodrag]')) return;
                doMaximize(st, opts.cascade || 0);
            };
            header.addEventListener('pointerdown', hDown);
            header.addEventListener('pointermove', hMove);
            header.addEventListener('pointerup', hUp);
            header.addEventListener('pointercancel', hUp);
            header.addEventListener('dblclick', hDbl);
            st.headerListeners = [['pointerdown', hDown], ['pointermove', hMove],
                ['pointerup', hUp], ['pointercancel', hUp], ['dblclick', hDbl]];

            // Bring-to-front on any pointerdown inside the card (capture phase).
            const rDown = () => raise(st);
            root.addEventListener('pointerdown', rDown, true);
            st.rootDown = rDown;

            // 8 resize handles (declared in the Blazor markup; wired here).
            st.handleListeners = [];
            HANDLES.forEach(dir => {
                const el = root.querySelector('[data-fw-handle="' + dir + '"]');
                if (!el) return;
                const down = (e) => beginResize(st, dir, el, e);
                const move = (e) => moveResize(st, e);
                const up = () => endResize(st);
                el.addEventListener('pointerdown', down);
                el.addEventListener('pointermove', move);
                el.addEventListener('pointerup', up);
                el.addEventListener('pointercancel', up);
                st.handleListeners.push([el, down, move, up]);
            });

            return id;
        },

        dispose(id) {
            const st = wins.get(id);
            if (!st) return;
            if (st.raf) { try { cancelAnimationFrame(st.raf); } catch (_) { } }
            try { (st.headerListeners || []).forEach(([t, fn]) => st.header.removeEventListener(t, fn)); } catch (_) { }
            try { if (st.rootDown) st.root.removeEventListener('pointerdown', st.rootDown, true); } catch (_) { }
            try {
                (st.handleListeners || []).forEach(([el, down, move, up]) => {
                    el.removeEventListener('pointerdown', down);
                    el.removeEventListener('pointermove', move);
                    el.removeEventListener('pointerup', up);
                    el.removeEventListener('pointercancel', up);
                });
            } catch (_) { }
            // Release any capture still held if disposed mid-gesture.
            try { if (st.drag) st.header.releasePointerCapture(st.dragPointer); } catch (_) { }
            try { if (st.resize) st.resize.el.releasePointerCapture(st.resize.pid); } catch (_) { }
            wins.delete(id);
        },

        bringToFront(id) { const st = wins.get(id); if (st) raise(st); },

        getRect(id) {
            const st = wins.get(id);
            return st ? { x: st.rect.x, y: st.rect.y, w: st.rect.w, h: st.rect.h, maximized: !!st.maximized } : null;
        },

        setRect(id, rect) {
            const st = wins.get(id);
            if (!st || !rect) return;
            st.maximized = !!rect.maximized;
            st.root.classList.toggle('trace-win-max', st.maximized);
            st.rect = { x: rect.x, y: rect.y, w: rect.w, h: rect.h, maximized: st.maximized };
            clampRect(st.rect, st.minW, st.minH);
            apply(st); raise(st); notify(st);
        },

        toggleMaximize(id) { const st = wins.get(id); if (st) doMaximize(st, 0); },

        toggleMinimize(id) { const st = wins.get(id); if (st) doMinimize(st); },

        // Keyboard: arrows move, Shift+arrows resize (called from .NET keydown).
        nudge(id, dx, dy, dw, dh) {
            const st = wins.get(id);
            if (!st || st.maximized) return;
            st.rect.x += dx; st.rect.y += dy; st.rect.w += dw; st.rect.h += dh;
            clampRect(st.rect, st.minW, st.minH);
            apply(st); notify(st);
        },

        recenter(id, cascade) {
            const st = wins.get(id);
            if (!st) return;
            st.maximized = false;
            st.root.classList.remove('trace-win-max');
            st.rect = defaultRect(cascade || 0);
            clampRect(st.rect, st.minW, st.minH);
            apply(st); raise(st); notify(st);
        },
    };
})();
