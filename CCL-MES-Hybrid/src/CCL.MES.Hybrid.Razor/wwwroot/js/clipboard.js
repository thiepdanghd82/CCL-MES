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

    return { copy, prettyJson };
})();
