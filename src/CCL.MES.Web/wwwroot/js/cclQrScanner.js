// Phase 8 PR #32b — Thin Blazor JS-interop wrapper around the vendored
// html5-qrcode library at /lib/html5-qrcode/2.3.8/. Exposes a single
// `window.ccl.qr` namespace consumed from `Pages/ShopOrder.razor`.
//
// Contracts:
//   ccl.qr.isAvailable()
//     → boolean. True only when secure-context + mediaDevices.getUserMedia
//       are both present. UI uses this to toggle the Camera button between
//       enabled and disabled+tooltip. Manual LOOKUP (shipped PR #32a) stays
//       the always-on path; camera is the bonus affordance.
//
//   ccl.qr.start(elementId, dotnetRef, methodName)
//     → starts the scanner inside the DOM node `elementId`, calls
//       `dotnetRef.invokeMethodAsync(methodName, decodedText)` on first
//       successful decode, and stops itself. Returns a promise resolved
//       with `{ok:true, code:<text>}` or `{ok:false, error:<reason>}`.
//       Errors classified into: permission_denied / no_camera / no_secure
//       / library_missing / other.
//
//   ccl.qr.stop(elementId)
//     → idempotent stop. Releases the camera stream (via the underlying
//       html5-qrcode.stop()). Safe to call even when scanner already
//       stopped; required when the Razor component disposes (operator
//       closes the page mid-scan).
//
// HTTPS / secure-context note (Q2): `navigator.mediaDevices.getUserMedia`
// only runs on https:// or http://localhost. On http://lan-ip:5080 the
// browser returns mediaDevices as undefined → `isAvailable()` returns
// false → UI disables the Camera button and the manual input remains the
// canonical way to look up a WO. See LESSONS_LEARNED.md "HTTPS constraint
// for getUserMedia".
(function () {
    'use strict';

    var ccl = window.ccl = window.ccl || {};
    ccl.qr = ccl.qr || {};

    // Cache the running instance by element id so stop() is idempotent.
    var _instances = {};

    function libPresent() {
        return typeof window.Html5Qrcode === 'function';
    }

    ccl.qr.isAvailable = function () {
        try {
            return !!(window.isSecureContext
                && navigator.mediaDevices
                && typeof navigator.mediaDevices.getUserMedia === 'function');
        } catch (_e) {
            return false;
        }
    };

    ccl.qr.libLoaded = function () {
        return libPresent();
    };

    ccl.qr.start = async function (elementId, dotnetRef, methodName) {
        if (!ccl.qr.isAvailable()) {
            return { ok: false, error: 'no_secure' };
        }
        if (!libPresent()) {
            return { ok: false, error: 'library_missing' };
        }
        var el = document.getElementById(elementId);
        if (!el) {
            return { ok: false, error: 'element_missing' };
        }

        // If a previous instance still exists on the same element, stop it
        // first so we never double-attach a stream (defensive idempotency).
        if (_instances[elementId]) {
            try { await _instances[elementId].stop(); } catch (_e) { /* noop */ }
            try { _instances[elementId].clear(); } catch (_e) { /* noop */ }
            delete _instances[elementId];
        }

        var scanner;
        try {
            scanner = new window.Html5Qrcode(elementId, /* verbose */ false);
        } catch (e) {
            return { ok: false, error: 'init_failed', detail: String(e && e.message || e) };
        }
        _instances[elementId] = scanner;

        var config = {
            fps: 10,
            qrbox: { width: 240, height: 240 },
            aspectRatio: 1.333
        };

        var settled = false;
        function settle(result) {
            if (settled) return;
            settled = true;
            try { scanner.stop().catch(function () {}); } catch (_e) {}
            try { scanner.clear(); } catch (_e) {}
            delete _instances[elementId];
            return result;
        }

        return new Promise(function (resolve) {
            scanner.start(
                { facingMode: 'environment' },
                config,
                function onScan(decodedText /*, decodedResult */) {
                    if (settled) return;
                    var payload = settle({ ok: true, code: decodedText });
                    // Notify Blazor before resolving so .NET handler runs
                    // even if the caller ignores the promise.
                    if (dotnetRef && methodName) {
                        try { dotnetRef.invokeMethodAsync(methodName, decodedText); }
                        catch (_e) { /* swallow — UI handles via promise too */ }
                    }
                    resolve(payload);
                },
                function onFailure(/* scanErrMsg, scanErr */) {
                    // Per-frame decode failure is noise; ignore. Real
                    // permission / camera errors arrive through the catch
                    // chain below.
                }
            ).catch(function (err) {
                var msg = (err && (err.name || err.message)) || String(err);
                var code = 'other';
                if (/NotAllowed|Permission/i.test(msg)) code = 'permission_denied';
                else if (/NotFound|Devices/i.test(msg))  code = 'no_camera';
                else if (/NotSupported|Secure/i.test(msg)) code = 'no_secure';
                resolve(settle({ ok: false, error: code, detail: msg }));
            });
        });
    };

    ccl.qr.stop = async function (elementId) {
        var scanner = _instances[elementId];
        if (!scanner) return;
        try { await scanner.stop(); } catch (_e) { /* noop */ }
        try { scanner.clear(); } catch (_e) { /* noop */ }
        delete _instances[elementId];
    };

    // Fail-safe: when the user navigates away from the page, dispose
    // any running scanner so the camera light turns off.
    window.addEventListener('pagehide', function () {
        Object.keys(_instances).forEach(function (id) {
            ccl.qr.stop(id);
        });
    });
})();
