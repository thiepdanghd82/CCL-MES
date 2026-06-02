// Phase 7 hạng mục 1 — JSInterop tối thiểu cho EngineerStructure
// Columns toggle persist. Single namespace `window.cclmes` để khỏi pollute global.
// localStorage có thể không khả dụng (private mode + quota); cả 2 method
// đều silent-fail, caller tự xử lý null.
window.cclmes = window.cclmes || {};

window.cclmes.storageGet = function (key) {
    try {
        return localStorage.getItem(key);
    } catch (_) {
        return null;
    }
};

window.cclmes.storageSet = function (key, value) {
    try {
        localStorage.setItem(key, value);
        return true;
    } catch (_) {
        return false;
    }
};

// Phase 8 PR #31c — Trigger file download cho Export endpoints.
// Pattern: tạo anchor <a download href=...> + click() programmatically.
// Browser auto-handle Content-Disposition + Save dialog tùy preference.
// KHÔNG dùng window.open vì popup-blocker có thể chặn; anchor click safer.
window.cclmes.downloadFile = function (url) {
    try {
        const a = document.createElement('a');
        a.href = url;
        a.style.display = 'none';
        // KHÔNG set download attribute — để Content-Disposition header
        // từ server quyết định filename (timestamp + correct extension).
        document.body.appendChild(a);
        a.click();
        // Defer cleanup để browser kịp khởi tạo download stream.
        setTimeout(() => document.body.removeChild(a), 100);
        return true;
    } catch (e) {
        console.error('cclmes.downloadFile failed:', e);
        return false;
    }
};

// ===========================================================================
// Phase 8 follow-up — Spec showcard print auto-fit (SpecHub pattern port).
// ===========================================================================
//
// Port of SpecHub `_fitOneFrame` + `_fitAllPages` from spechub-prototype.html.
// SpecHub solved the "aggressive font compression looks bad" problem by
// keeping the natural screen layout (fixed 1080px canvas = A4 landscape
// printable width at 96dpi minus 6mm margins) and dynamically scaling each
// .spec-frame to fit the printable HEIGHT (~760px @ A4 landscape).
//
// Result: the exported PDF looks like a clean SCALED snapshot of the
// on-screen showcard (the user's "right image" target), not a re-flowed
// font-compressed sheet.
//
// How to use from Blazor:
//   1. @media print CSS forces body width:1080px + .spec-frame width:1080px
//   2. Before window.print() fires, beforeprint event handler runs
//      _fitAllPages() which measures each .spec-frame natural height at
//      1080px and applies transform: scale(X) to fit 760px target.
//   3. transform-origin: top left keeps the scaled content anchored.
//   4. Wrapper element gets explicit height/width so trailing whitespace
//      doesn't trigger a phantom blank page (SpecHub 2026-05-29 fix).
//   5. .spec-history-block is moved into its own `.print-page page-history`
//      wrapper at beforeprint time so it lands on page 2 (Change Log alone)
//      with its own independent scale calculation.

(function () {
    const BASE_W = 1080;        // A4 landscape printable width at 96dpi minus 6mm margins
    const TARGET_H = 760;       // A4 landscape printable height; floor scale=0.5
    const MIN_SCALE = 0.5;
    const EDGE_PAD = 2;         // reserves room for outermost border so it isn't clipped

    function _fitOneFrame(frame) {
        if (!frame) return 1;
        frame.style.transform = '';                  // reset for re-measure
        const actualH = frame.offsetHeight;
        let scale = 1;
        if (actualH > TARGET_H) {
            scale = Math.max(MIN_SCALE, TARGET_H / actualH);
            frame.style.transform = 'scale(' + scale.toFixed(3) + ')';
            frame.style.transformOrigin = 'top left';
        }
        const wrapper = frame.parentElement;
        if (wrapper && wrapper.classList.contains('print-page')) {
            wrapper.style.height = (Math.ceil(actualH * scale) + EDGE_PAD) + 'px';
            wrapper.style.width = (Math.ceil(BASE_W * scale) + EDGE_PAD) + 'px';
            wrapper.style.overflow = 'hidden';
        }
        return scale;
    }

    function _fitAllPages() {
        const pages = document.querySelectorAll('.print-page .spec-frame');
        let maxScale = 0;
        for (let i = 0; i < pages.length; i++) {
            const s = _fitOneFrame(pages[i]);
            if (s > maxScale) maxScale = s;
        }
        if (maxScale > 0) {
            const bodyW = Math.ceil(BASE_W * maxScale) + EDGE_PAD;
            document.body.style.setProperty('width', bodyW + 'px', 'important');
        }
    }

    // Split .spec-frame into two .print-page wrappers (page-spec + page-history)
    // when a .spec-history-block exists, so Change Log lands on page 2 with
    // its own scale calc. Idempotent — checks existing wrappers before splitting.
    function _wrapPagesForPrint() {
        const frame = document.querySelector('.spec-sheet-page .spec-frame');
        if (!frame) return;

        // Already wrapped? (re-run guard)
        if (frame.closest('.print-page')) return;

        const historyBlock = frame.querySelector('.spec-history-block');
        const parent = frame.parentElement;

        // Wrap the existing frame as page-spec (excluding history)
        const pageSpec = document.createElement('div');
        pageSpec.className = 'print-page page-spec';
        parent.insertBefore(pageSpec, frame);
        pageSpec.appendChild(frame);

        // Move history into its own page-history wrapper (separate .spec-frame
        // shell so _fitOneFrame can scale it independently)
        if (historyBlock) {
            historyBlock.parentElement.removeChild(historyBlock);
            const pageHist = document.createElement('div');
            pageHist.className = 'print-page page-history';
            const histFrame = document.createElement('div');
            histFrame.className = 'spec-frame spec-frame-history';
            histFrame.appendChild(historyBlock);
            pageHist.appendChild(histFrame);
            parent.appendChild(pageHist);
        }
    }

    function _unwrapPagesAfterPrint() {
        const wrappers = document.querySelectorAll('.spec-sheet-page .print-page');
        wrappers.forEach((wrapper) => {
            const isHistoryPage = wrapper.classList.contains('page-history');
            if (isHistoryPage) {
                // Restore the history block back to the main frame
                const histFrame = wrapper.querySelector('.spec-frame');
                const histBlock = wrapper.querySelector('.spec-history-block');
                const mainFrame = document.querySelector('.spec-sheet-page .spec-frame:not(.spec-frame-history)');
                if (mainFrame && histBlock) {
                    mainFrame.appendChild(histBlock);
                }
                wrapper.parentElement.removeChild(wrapper);
            } else {
                // Unwrap the main frame
                const frame = wrapper.querySelector('.spec-frame');
                if (frame) {
                    frame.style.transform = '';
                    frame.style.transformOrigin = '';
                    wrapper.parentElement.insertBefore(frame, wrapper);
                }
                wrapper.parentElement.removeChild(wrapper);
            }
        });
        document.body.style.width = '';
    }

    // beforeprint fires synchronously before the print dialog renders.
    // Wrap → fit → let browser print. afterprint cleans up so the on-screen
    // view returns to normal (no leftover scale / phantom wrappers).
    window.addEventListener('beforeprint', function () {
        try {
            _wrapPagesForPrint();
            _fitAllPages();
        } catch (e) {
            console.error('cclmes print fit failed:', e);
        }
    });
    window.addEventListener('afterprint', function () {
        try {
            _unwrapPagesAfterPrint();
        } catch (e) {
            console.error('cclmes print cleanup failed:', e);
        }
    });

    // Expose for manual re-fit (e.g. Blazor SSR rehydration timing edge cases).
    window.cclmes.fitSpecForPrint = function () {
        _wrapPagesForPrint();
        _fitAllPages();
    };
    window.cclmes.unwrapAfterPrint = function () {
        _unwrapPagesAfterPrint();
    };
})();

// Phase 8 PR #32d — Demo Work Order create.
// Posts to the controller with the browser's auth cookie + the antiforgery
// token if one is present in the document. Returns a small object the
// Blazor component reads. Failure swallowed inside try/catch — caller
// surfaces the `ok=false` shape.
window.cclmes.workorders = window.cclmes.workorders || {};
window.cclmes.workorders.createDemo = async function (templateCode) {
    try {
        const headers = { 'Accept': 'application/json' };
        // Pick up the antiforgery cookie if MVC issued one (cookie-name match
        // covers both default `.AspNetCore.Antiforgery.*` and any custom set).
        const csrfMeta = document.querySelector('meta[name="csrf-token"]');
        if (csrfMeta && csrfMeta.content) {
            headers['RequestVerificationToken'] = csrfMeta.content;
        }
        const resp = await fetch('/api/workorders/demo/' + encodeURIComponent(templateCode), {
            method: 'POST',
            credentials: 'same-origin',
            headers: headers,
        });
        let body = '';
        try { body = await resp.text(); } catch (_) { /* empty */ }
        if (!resp.ok) {
            return { ok: false, status: resp.status, woNo: null, body: body };
        }
        let woNo = null;
        try {
            const parsed = JSON.parse(body);
            woNo = parsed && parsed.woNo ? parsed.woNo : null;
        } catch (_) { /* server returned non-JSON success — surface raw */ }
        return { ok: true, status: resp.status, woNo: woNo, body: body };
    } catch (e) {
        return { ok: false, status: 0, woNo: null, body: (e && e.message) || String(e) };
    }
};

// Phase 8 PR-L1 — Spec lifecycle Copy + Update.
// Both helpers use the same fetch shape so the Blazor modal code paths stay
// minimal. Returned object encodes:
//   { ok, status, id?, specCode?, code?, currentStatus?, noChanges?, body? }
// where `code` mirrors the server's RFC 7807-style `code` field (e.g.
// "duplicate_spec_code" / "immutable_status" / "trashed" / "validation").
window.cclmes.specs = window.cclmes.specs || {};

window.cclmes.specs.copy = async function (sourceRevId, payload) {
    try {
        const resp = await fetch('/api/specs/' + sourceRevId + '/copy', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload),
        });
        let text = '';
        try { text = await resp.text(); } catch (_) {}
        if (!resp.ok) {
            let code = null, problem = text;
            try {
                const parsed = JSON.parse(text);
                code = parsed.code || null;
                problem = parsed.error || parsed.title || text;
            } catch (_) {}
            return { ok: false, status: resp.status, code: code, body: problem };
        }
        let parsed = {};
        try { parsed = JSON.parse(text); } catch (_) {}
        return {
            ok: true,
            status: resp.status,
            id: parsed.id || null,
            specCode: parsed.specCode || null,
            body: text,
        };
    } catch (e) {
        return { ok: false, status: 0, code: null, body: (e && e.message) || String(e) };
    }
};

window.cclmes.specs.update = async function (revId, payload) {
    try {
        const resp = await fetch('/api/specs/' + revId, {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload),
        });
        let text = '';
        try { text = await resp.text(); } catch (_) {}
        if (!resp.ok) {
            let code = null, problem = text, currentStatus = null;
            try {
                const parsed = JSON.parse(text);
                code = parsed.code || null;
                problem = parsed.error || parsed.title || text;
                currentStatus = parsed.currentStatus || null;
            } catch (_) {}
            return {
                ok: false, status: resp.status,
                code: code, currentStatus: currentStatus, body: problem,
            };
        }
        let parsed = {};
        try { parsed = JSON.parse(text); } catch (_) {}
        return {
            ok: true,
            status: resp.status,
            noChanges: parsed.noChanges === true,
            body: text,
        };
    } catch (e) {
        return { ok: false, status: 0, code: null, body: (e && e.message) || String(e) };
    }
};

// Phase 8 PR-L2 — Spec lifecycle Revise + Supersede helpers.
// Mirror cclmes.specs.copy/update shape so the Blazor modals can map the
// returned envelope uniformly. The `code` field surfaces server's
// structured error code: "invalid_source_status" / "reason_required" /
// "trashed" / "invalid_status" / "confirm_mismatch".

window.cclmes.specs.revise = async function (sourceRevId, payload) {
    try {
        const resp = await fetch('/api/specs/' + sourceRevId + '/revise', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload),
        });
        let text = '';
        try { text = await resp.text(); } catch (_) {}
        if (!resp.ok) {
            let code = null, problem = text, currentStatus = null;
            try {
                const parsed = JSON.parse(text);
                code = parsed.code || null;
                problem = parsed.error || parsed.title || text;
                currentStatus = parsed.currentStatus || null;
            } catch (_) {}
            return {
                ok: false, status: resp.status,
                code: code, currentStatus: currentStatus, body: problem,
            };
        }
        let parsed = {};
        try { parsed = JSON.parse(text); } catch (_) {}
        return {
            ok: true,
            status: resp.status,
            id: parsed.id || null,
            specCode: parsed.specCode || null,
            revision: parsed.revision || null,
            parentId: parsed.parentId || null,
            body: text,
        };
    } catch (e) {
        return { ok: false, status: 0, code: null, body: (e && e.message) || String(e) };
    }
};

window.cclmes.specs.supersede = async function (revId, payload) {
    try {
        const resp = await fetch('/api/specs/' + revId + '/supersede', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload),
        });
        let text = '';
        try { text = await resp.text(); } catch (_) {}
        if (!resp.ok) {
            let code = null, problem = text, currentStatus = null;
            try {
                const parsed = JSON.parse(text);
                code = parsed.code || null;
                problem = parsed.error || parsed.title || text;
                currentStatus = parsed.currentStatus || null;
            } catch (_) {}
            return {
                ok: false, status: resp.status,
                code: code, currentStatus: currentStatus, body: problem,
            };
        }
        let parsed = {};
        try { parsed = JSON.parse(text); } catch (_) {}
        return {
            ok: true,
            status: resp.status,
            id: parsed.id || null,
            specCode: parsed.specCode || null,
            status_after: parsed.status || null,
            body: text,
        };
    } catch (e) {
        return { ok: false, status: 0, code: null, body: (e && e.message) || String(e) };
    }
};

// Phase 8 PR-L3 — Spec lifecycle Trash + Restore.
window.cclmes.specs.trash = async function (revId) {
    try {
        const resp = await fetch('/api/specs/' + revId + '/trash', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: '{}',
        });
        let text = '';
        try { text = await resp.text(); } catch (_) {}
        if (!resp.ok) {
            let code = null, problem = text, activeWoCount = null;
            try {
                const parsed = JSON.parse(text);
                code = parsed.code || null;
                problem = parsed.error || parsed.title || text;
                activeWoCount = (typeof parsed.activeWoCount === 'number') ? parsed.activeWoCount : null;
            } catch (_) {}
            return {
                ok: false, status: resp.status,
                code: code, activeWoCount: activeWoCount, body: problem,
            };
        }
        return { ok: true, status: resp.status, body: text };
    } catch (e) {
        return { ok: false, status: 0, code: null, body: (e && e.message) || String(e) };
    }
};

window.cclmes.specs.restore = async function (revId) {
    try {
        const resp = await fetch('/api/specs/' + revId + '/restore', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: '{}',
        });
        let text = '';
        try { text = await resp.text(); } catch (_) {}
        if (!resp.ok) {
            let code = null, problem = text;
            try {
                const parsed = JSON.parse(text);
                code = parsed.code || null;
                problem = parsed.error || parsed.title || text;
            } catch (_) {}
            return { ok: false, status: resp.status, code: code, body: problem };
        }
        return { ok: true, status: resp.status, body: text };
    } catch (e) {
        return { ok: false, status: 0, code: null, body: (e && e.message) || String(e) };
    }
};
