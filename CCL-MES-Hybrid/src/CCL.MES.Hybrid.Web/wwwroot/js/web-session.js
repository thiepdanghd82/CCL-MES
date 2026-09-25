// Phiên web (2026-09-25, phần 2). Circuit Blazor không đặt được cookie (không có
// HTTP response), nên sau đăng nhập server trao cho tab một VÉ một lần; trình duyệt
// đổi vé lấy cookie HttpOnly. JS KHÔNG bao giờ thấy token hay khoá phiên.
// Header X-CCL-Session chặn gửi chéo trang (form HTML không đặt được header tuỳ ý).
window.cclWebSession = {
    claim: async function (ticket) {
        try {
            const r = await fetch('_session/claim', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'X-CCL-Session': '1' },
                body: JSON.stringify({ ticket: ticket })
            });
            return r.ok;
        } catch (e) {
            return false;
        }
    },
    clear: async function () {
        try {
            const r = await fetch('_session/clear', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'X-CCL-Session': '1' }
            });
            return r.ok;
        } catch (e) {
            return false;
        }
    }
};
