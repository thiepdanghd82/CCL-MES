// CCL iX — công tắc mật độ hiển thị (office / shopfloor).
//
// Cùng một markup, hai bộ số: office = kỹ sư/QA ngồi bàn (dòng 32px, chữ 14px);
// shopfloor = người đứng máy ĐEO GĂNG (vùng chạm 44px, chữ 16px, dòng 56px).
// Xem :root[data-density="shopfloor"] trong css/ix.css.
//
// Pattern theo clipboard.js/backup.js: namespace nhỏ gắn window + try/catch để
// một lỗi storage (quota / private mode) không bao giờ nổi lên renderer —
// lớp lesson renderer-dead (L1/L2/L3) giữ nguyên.

window.cclMesDensity = (() => {
    const KEY = 'ccl.mes.density';
    const VALID = ['office', 'shopfloor'];

    function read() {
        try {
            const v = window.localStorage.getItem(KEY);
            return VALID.includes(v) ? v : 'office';
        } catch { return 'office'; }
    }

    // Áp lên <html>. office = XOÁ thuộc tính (không phải set "office") để
    // :root mặc định vẫn là nguồn duy nhất của bộ số office — tránh hai chỗ
    // cùng định nghĩa một trạng thái.
    function apply(value) {
        const v = VALID.includes(value) ? value : 'office';
        try {
            if (v === 'shopfloor') document.documentElement.dataset.density = 'shopfloor';
            else delete document.documentElement.dataset.density;
        } catch { /* DOM chưa sẵn sàng — boot script sẽ áp lại */ }
        return v;
    }

    function set(value) {
        const v = apply(value);
        try { window.localStorage.setItem(KEY, v); } catch { /* không lưu được thì thôi */ }
        return v;
    }

    function get() { return read(); }

    // Trạng thái thu gọn rail đi chung cơ chế (cùng localStorage, cùng try/catch)
    // thay vì dựng thêm một preference service — ít bộ phận chuyển động hơn.
    const RAIL_KEY = 'ccl.mes.rail';
    function railGet() {
        try { return window.localStorage.getItem(RAIL_KEY) === 'collapsed'; }
        catch { return false; }
    }
    function railSet(collapsed) {
        try { window.localStorage.setItem(RAIL_KEY, collapsed ? 'collapsed' : 'expanded'); }
        catch { }
        return !!collapsed;
    }

    // Gọi sớm nhất có thể để không chớp giao diện (FOUC) khi khởi động.
    function boot() { return apply(read()); }

    return { get, set, apply, boot, railGet, railSet };
})();
