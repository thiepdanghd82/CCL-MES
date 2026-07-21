// Tiny placement helper for RowContextMenu — positions the menu at the pointer,
// CLAMPS it inside the viewport (never off the right/bottom edge), focuses the
// first enabled item (keyboard a11y), and auto-closes on scroll / wheel / resize
// / window-blur via a one-shot dotnet callback. Mirrors the window-scoped
// pattern of backup.js / clipboard.js (no ES module, loaded by a <script> tag).
window.cclMesMenu = (() => {
    function place(el, x, y, dotnet) {
        if (!el) return;
        const vw = window.innerWidth || document.documentElement.clientWidth;
        const vh = window.innerHeight || document.documentElement.clientHeight;
        // Measure at the raw anchor, then nudge back inside the viewport.
        const r = el.getBoundingClientRect();
        let left = x, top = y;
        if (left + r.width > vw - 8) left = Math.max(8, vw - r.width - 8);
        if (top + r.height > vh - 8) top = Math.max(8, vh - r.height - 8);
        el.style.left = Math.round(left) + 'px';
        el.style.top = Math.round(top) + 'px';
        el.style.visibility = 'visible';
        const first = el.querySelector('button:not([disabled])');
        if (first) { try { first.focus(); } catch (_) { } }

        // Dismiss on any scroll/zoom/blur — the anchor is stale after those.
        const close = () => { try { dotnet.invokeMethodAsync('CloseFromJs'); } catch (_) { } };
        window.addEventListener('scroll', close, { once: true, capture: true });
        window.addEventListener('wheel', close, { once: true, passive: true });
        window.addEventListener('resize', close, { once: true });
        window.addEventListener('blur', close, { once: true });
    }

    // Roving focus for Arrow Up/Down over the enabled items.
    function move(el, delta) {
        if (!el) return;
        const btns = Array.from(el.querySelectorAll('button:not([disabled])'));
        if (btns.length === 0) return;
        const i = btns.indexOf(document.activeElement);
        const n = i < 0 ? 0 : (i + delta + btns.length) % btns.length;
        try { btns[n].focus(); } catch (_) { }
    }

    return { place, move };
})();
