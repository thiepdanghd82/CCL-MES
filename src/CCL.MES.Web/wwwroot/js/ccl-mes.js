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
