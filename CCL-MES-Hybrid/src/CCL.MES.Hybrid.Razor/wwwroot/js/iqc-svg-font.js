// IQC dashboard — cỡ chữ SVG không phóng theo viewBox (L72).
//
// WKWebView biến CSS px trên <text> thành user unit của viewBox 1000, và
// `cqw` trên SVG không phải bề rộng vẽ. Đo width thật rồi gán user unit
// đảo ngược: quang học = font-size của .iqc-chart-scale (token --iqc-chart-fs).
// Pareto hẹp và Monthly trend rộng ra cùng một cỡ trên mọi cửa sổ.
//
// Pattern: namespace window + try/catch (density.js / clipboard.js).

window.cclIqcSvgFont = (() => {
    const RO = typeof ResizeObserver !== 'undefined' ? ResizeObserver : null;
    const observed = new WeakMap();

    function apply(scale) {
        try {
            const svg = scale.querySelector('svg.iqc-svg');
            if (!svg) return;
            const w = svg.getBoundingClientRect().width;
            if (w < 8) return;
            const targetPx = parseFloat(getComputedStyle(scale).fontSize) || 14;
            svg.style.setProperty('--iqc-svg-fs', (targetPx * 1000 / w) + 'px');
        } catch { /* renderer-safe */ }
    }

    function bind(root) {
        if (!root || !RO) return;
        try {
            root.querySelectorAll('.iqc-chart-scale').forEach((el) => {
                if (observed.has(el)) {
                    apply(el);
                    return;
                }
                const ro = new RO(() => apply(el));
                ro.observe(el);
                const svg = el.querySelector('svg.iqc-svg');
                if (svg) ro.observe(svg);
                observed.set(el, ro);
                apply(el);
            });
        } catch { /* no-op in tests / headless */ }
    }

    function unbind(root) {
        if (!root) return;
        try {
            root.querySelectorAll('.iqc-chart-scale').forEach((el) => {
                const ro = observed.get(el);
                if (ro) {
                    ro.disconnect();
                    observed.delete(el);
                }
            });
        } catch { /* ignore */ }
    }

    return { bind, unbind };
})();
