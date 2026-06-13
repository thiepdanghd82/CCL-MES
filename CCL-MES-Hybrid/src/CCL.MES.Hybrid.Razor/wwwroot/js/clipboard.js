// P10.6e UI fix — clipboard copy helper for the audit + backup pages.
// Pattern mirrors backup.js (P10.6h) — tiny window-scoped namespace
// + try/catch so a denied permission never bubbles to the renderer
// (the renderer-dead lesson layer stays untouched).

window.cclMesClipboard = (() => {
    async function copy(text) {
        try {
            if (navigator?.clipboard?.writeText) {
                await navigator.clipboard.writeText(text);
                return true;
            }
            // Catalyst WKWebView fallback — create a hidden <textarea>,
            // select, execCommand('copy'). Deprecated but still works
            // inside Mac Catalyst when navigator.clipboard is gated.
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            const ok = document.execCommand('copy');
            document.body.removeChild(ta);
            return ok;
        } catch (e) {
            return false;
        }
    }

    function prettyJson(raw) {
        // Defensive: never throw on bad input — return as-is so the
        // detail panel still shows something even when the audit row
        // happens to carry a non-JSON detail string.
        try {
            if (typeof raw !== 'string' || raw.length === 0) return raw ?? '';
            const obj = JSON.parse(raw);
            return JSON.stringify(obj, null, 2);
        } catch (e) {
            return raw;
        }
    }

    // P10.8 — trigger a client-side file download (Shop Order History
    // "Export CSV"). Same defensive try/catch posture: a blocked blob URL
    // never bubbles to the renderer.
    function download(filename, text, mime) {
        try {
            const blob = new Blob([text ?? ''], { type: mime || 'text/csv;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename || 'export.csv';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            return true;
        } catch (e) {
            return false;
        }
    }

    return { copy, prettyJson, download };
})();

// P10.10 — grid auto-fit: measure how many rows fit the (flex-filled)
// .grid-scroll viewport so a paginated grid fills the screen, then
// re-measure on window resize. Keyed by a per-component id so register/
// unregister pair up regardless of DotNetObjectReference proxy identity.
window.cclMesGrid = (() => {
    const regs = new Map();

    function measure(sel, rowPx) {
        const el = document.querySelector(sel);
        if (!el || el.clientHeight <= 0) return 0;
        const row = el.querySelector('tbody tr');
        const rh = row && row.offsetHeight ? row.offsetHeight : rowPx;
        const head = el.querySelector('thead');
        const hh = head && head.offsetHeight ? head.offsetHeight : rh;
        if (rh <= 0) return 0;
        return Math.max(8, Math.floor((el.clientHeight - hh) / rh));
    }

    function register(id, ref, sel, rowPx) {
        const fire = () => {
            const n = measure(sel, rowPx);
            if (n > 0) { try { ref.invokeMethodAsync('Fit', n); } catch (e) { /* disposed */ } }
        };
        let t;
        const onResize = () => { clearTimeout(t); t = setTimeout(fire, 200); };
        regs.set(id, onResize);
        window.addEventListener('resize', onResize);
        // Let the flex layout settle before the first measure.
        setTimeout(fire, 80);
    }

    function unregister(id) {
        const h = regs.get(id);
        if (h) { window.removeEventListener('resize', h); regs.delete(id); }
    }

    return { register, unregister };
})();

// P10.10 — drawing preview blob URLs. WKWebView renders PDFs/images reliably
// from a blob: URL (data: PDFs + the native Launcher are flaky on Catalyst).
window.cclMesDrawings = (() => {
    function toObjectUrl(base64, mime) {
        try {
            const bin = atob(base64);
            const len = bin.length;
            const arr = new Uint8Array(len);
            for (let i = 0; i < len; i++) arr[i] = bin.charCodeAt(i);
            const blob = new Blob([arr], { type: mime || 'application/octet-stream' });
            return URL.createObjectURL(blob);
        } catch (e) { return ''; }
    }
    function revoke(url) { try { URL.revokeObjectURL(url); } catch (e) { /* noop */ } }
    return { toObjectUrl, revoke };
})();
