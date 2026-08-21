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

    // ── UI SCALE (L42) — công cụ chỉnh cỡ chữ/UI, giống display scaling của
    // Win/macOS. Giá trị = HỆ SỐ thập phân (chuỗi) áp thẳng vào --ui-scale:
    // '0.9' · '1' (mặc định) · '1.1' · '1.25' · '1.5'. Bậc RỜI (không tự do)
    // theo tiền lệ GOV.UK/Carbon text-zoom — clamp về '1' nếu giá trị lạ.
    // Áp qua --ui-scale ở :root ⇒ chữ (rem) + --sp-* + --d-tap phóng theo,
    // khung px (--ix-*) giữ nguyên. Đi chung localStorage + try/catch với
    // density/rail thay vì dựng thêm preference service.
    const SCALE_KEY = 'ccl.mes.uiscale';
    const SCALE_VALID = ['0.9', '1', '1.1', '1.25', '1.5'];
    const SCALE_DEFAULT = '1';

    function scaleRead() {
        try {
            const v = window.localStorage.getItem(SCALE_KEY);
            return SCALE_VALID.includes(v) ? v : SCALE_DEFAULT;
        } catch { return SCALE_DEFAULT; }
    }

    // Áp lên <html> qua biến --ui-scale. Không set khi giá trị lạ ⇒ clamp về 1.
    function scaleApply(value) {
        const v = SCALE_VALID.includes(value) ? value : SCALE_DEFAULT;
        try { document.documentElement.style.setProperty('--ui-scale', v); }
        catch { /* DOM chưa sẵn sàng — boot sẽ áp lại */ }
        return v;
    }

    function scaleGet() { return scaleRead(); }

    function scaleSet(value) {
        const v = scaleApply(value);
        try { window.localStorage.setItem(SCALE_KEY, v); } catch { /* thôi */ }
        return v;
    }

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

    // Trạng thái gập/mở của các nhóm accordion trong rail (Carbon SideNavMenu).
    // Lưu MỘT chuỗi CSV các key nhóm ĐANG ĐÓNG (mặc định = mở → CSV rỗng nghĩa
    // là mọi nhóm mở). Đi chung localStorage + try/catch với density/rail thay
    // vì dựng thêm preference service — ít bộ phận chuyển động hơn.
    const NAVGRP_KEY = 'ccl.mes.navgroups';
    function navGroupsGet() {
        try {
            const v = window.localStorage.getItem(NAVGRP_KEY);
            return typeof v === 'string' ? v : '';
        } catch { return ''; }
    }
    function navGroupsSet(csv) {
        try { window.localStorage.setItem(NAVGRP_KEY, typeof csv === 'string' ? csv : ''); }
        catch { }
        return typeof csv === 'string' ? csv : '';
    }

    // Điều hướng nâng cao (Phương án B) — hai danh sách CSV href, đi chung
    // localStorage + try/catch với density/rail/navgroups (không dựng thêm
    // preference service). PINNED = href người dùng chủ động ghim (thứ tự ghim
    // giữ nguyên). RECENT = MRU 5 route vừa thăm (mới nhất đầu danh sách, cắt 5).
    // Tiền lệ: Cloudscape/Carbon nav filter · VS Code/JetBrains "recent".
    const NAVPINS_KEY = 'ccl.mes.navpins';
    function navPinsGet() {
        try {
            const v = window.localStorage.getItem(NAVPINS_KEY);
            return typeof v === 'string' ? v : '';
        } catch { return ''; }
    }
    function navPinsSet(csv) {
        try { window.localStorage.setItem(NAVPINS_KEY, typeof csv === 'string' ? csv : ''); }
        catch { }
        return typeof csv === 'string' ? csv : '';
    }

    const NAVRECENT_KEY = 'ccl.mes.navrecent';
    function navRecentGet() {
        try {
            const v = window.localStorage.getItem(NAVRECENT_KEY);
            return typeof v === 'string' ? v : '';
        } catch { return ''; }
    }
    function navRecentSet(csv) {
        try { window.localStorage.setItem(NAVRECENT_KEY, typeof csv === 'string' ? csv : ''); }
        catch { }
        return typeof csv === 'string' ? csv : '';
    }

    // Gọi sớm nhất có thể để không chớp giao diện (FOUC) khi khởi động.
    // Áp CẢ density LẪN ui-scale trước khi Blazor render → không chớp cỡ chữ.
    function boot() { scaleApply(scaleRead()); return apply(read()); }

    return {
        get, set, apply, boot, scaleGet, scaleSet, scaleApply,
        railGet, railSet, navGroupsGet, navGroupsSet,
        navPinsGet, navPinsSet, navRecentGet, navRecentSet,
    };
})();
