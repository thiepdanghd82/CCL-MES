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
        // Rows vary in height (descriptions wrap to 1–3 lines). Measuring only
        // the FIRST row biases the estimate toward whatever that row's height
        // is — a tall wrapped first row made the grid request ~10 rows and
        // leave the bottom half of the viewport blank. Average across the
        // rendered rows so the page size tracks the real mean row height and
        // fills the viewport (a slight over/under just adds/removes one row).
        const rows = el.querySelectorAll('tbody tr');
        let rh = rowPx, n = 0, sum = 0;
        for (const r of rows) { if (r.offsetHeight > 0) { sum += r.offsetHeight; n++; } }
        if (n > 0) rh = sum / n;
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
        const debounced = () => { clearTimeout(t); t = setTimeout(fire, 200); };

        // window.resize covers the browser viewport changing. But a page shown
        // INSIDE a floating window fills only that window's body: maximizing /
        // resizing the WINDOW resizes a DOM ELEMENT — it does NOT fire
        // window.resize, so the page size used to stay stuck at its small
        // startup value (grid short → dead gap to the pager). A ResizeObserver on
        // the scroll container re-measures whenever the (now flex-filled) region
        // changes height, so element-driven resizes (window maximize/resize)
        // re-fit too. Belt-and-braces: keep window.resize as a fallback for hosts
        // without ResizeObserver.
        window.addEventListener('resize', debounced);
        let ro = null;
        const el = document.querySelector(sel);
        if (el && typeof ResizeObserver !== 'undefined') {
            ro = new ResizeObserver(debounced);
            try { ro.observe(el); } catch (e) { ro = null; }
        }
        regs.set(id, { onResize: debounced, ro });

        // Fire after the flex layout settles. WKWebView (Mac Catalyst) can
        // report a stale clientHeight if measured too early, so fire across a
        // double rAF and again at 250ms — the largest (fullest) result wins.
        requestAnimationFrame(() => requestAnimationFrame(fire));
        setTimeout(fire, 250);
    }

    function unregister(id) {
        const h = regs.get(id);
        if (h) {
            window.removeEventListener('resize', h.onResize);
            if (h.ro) { try { h.ro.disconnect(); } catch (e) { /* ignore */ } }
            regs.delete(id);
        }
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
    function openInNewWindow(url) {
        try { window.open(url, '_blank', 'noopener,noreferrer'); } catch (e) { /* noop */ }
    }
    return { toObjectUrl, revoke, openInNewWindow };
})();

// P10.10 — PDF rendering via PDF.js. WKWebView (Mac Catalyst) renders a blob:
// PDF blank inside an <iframe>, so we rasterise each page onto a <canvas>.
// Pages render at fit-to-width × the caller's zoom; rotation is applied per
// page. The parsed document is cached by URL so zoom/rotate re-renders don't
// re-parse the file.
window.cclMesPdf = (() => {
    const WORKER = '_content/CCL.MES.Hybrid.Razor/lib/pdfjs/pdf.worker.min.js';
    const _docs = {};   // url -> pdfDoc

    async function load(url) {
        if (_docs[url]) return _docs[url];
        const lib = window.pdfjsLib;
        if (!lib) throw new Error('pdfjsLib not loaded');
        try { lib.GlobalWorkerOptions.workerSrc = WORKER; } catch (e) { /* noop */ }
        const doc = await lib.getDocument(url).promise;
        _docs[url] = doc;
        return doc;
    }

    async function render(elId, url, zoom, rotation) {
        const el = document.getElementById(elId);
        if (!el) return 0;
        el.setAttribute('data-rendering', '1');
        let doc;
        try {
            doc = await load(url);
        } catch (e) {
            el.innerHTML = '<div class="dwg-viewer-empty">Could not render this PDF (' + (e && e.message ? e.message : 'engine error') + ').</div>';
            return 0;
        }
        const rot = ((rotation || 0) % 360 + 360) % 360;
        // Fit-to-width off page 1, then scale by the caller's zoom factor.
        const first = await doc.getPage(1);
        const base = first.getViewport({ scale: 1, rotation: rot });
        const avail = Math.max(200, (el.clientWidth || 800) - 28);
        const scale = Math.max(0.1, (avail / base.width) * (zoom || 1));

        const frag = document.createDocumentFragment();
        for (let p = 1; p <= doc.numPages; p++) {
            const page = await doc.getPage(p);
            const vp = page.getViewport({ scale: scale, rotation: rot });
            const canvas = document.createElement('canvas');
            canvas.className = 'dwg-pdf-page';
            const ratio = window.devicePixelRatio || 1;
            canvas.width = Math.floor(vp.width * ratio);
            canvas.height = Math.floor(vp.height * ratio);
            canvas.style.width = Math.floor(vp.width) + 'px';
            canvas.style.height = Math.floor(vp.height) + 'px';
            const ctx = canvas.getContext('2d');
            ctx.scale(ratio, ratio);
            await page.render({ canvasContext: ctx, viewport: vp }).promise;
            frag.appendChild(canvas);
        }
        el.innerHTML = '';
        el.appendChild(frag);
        el.removeAttribute('data-rendering');
        return doc.numPages;
    }

    function dispose(url) {
        const d = _docs[url];
        if (d) { try { d.destroy(); } catch (e) { /* noop */ } delete _docs[url]; }
    }

    return { render, dispose };
})();
