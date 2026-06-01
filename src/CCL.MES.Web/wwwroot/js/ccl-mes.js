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
