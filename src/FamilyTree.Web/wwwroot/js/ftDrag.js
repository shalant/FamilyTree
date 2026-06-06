/**
 * ftDrag — self-initializing drag-to-reposition for fixed-position elements.
 *
 * Uses MutationObserver to watch for #ft-toolbar and #ft-appbar to appear in
 * the DOM, then wires up drag immediately — no dependency on Blazor's rendering
 * lifecycle or OnAfterRenderAsync timing.
 *
 * A second MutationObserver on each element's style attribute detects when
 * Blazor re-renders reset the inline style and immediately re-applies the
 * saved drag position from localStorage.
 */
window.ftDrag = (() => {

    // ── helpers ───────────────────────────────────────────────────────────────

    function load(key) {
        try { return JSON.parse(localStorage.getItem('ftdrag:' + key)); }
        catch { return null; }
    }
    function save(key, val) {
        localStorage.setItem('ftdrag:' + key, JSON.stringify(val));
    }
    function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

    function setPos(el, left, top, snapToTop) {
        el.style.position = 'fixed';
        el.style.left     = left + 'px';
        el.style.top      = top  + 'px';
        el.style.right    = 'auto';
        el.style.bottom   = 'auto';
        if (snapToTop) {
            el.style.width = 'auto';
            el.classList.remove('ft-appbar-snapped');
            el.classList.add('ft-appbar-floating');
        }
    }
    function snapTop(el) {
        el.style.left   = '0';
        el.style.top    = '0';
        el.style.right  = '0';
        el.style.bottom = 'auto';
        el.style.width  = '100%';
        el.classList.remove('ft-appbar-floating');
        el.classList.add('ft-appbar-snapped');
    }

    // ── per-element initialiser ───────────────────────────────────────────────

    function initElement(el, storageKey, snapToTop) {
        if (el._ftDragReady) return; // already wired up
        el._ftDragReady   = true;
        el._ftSettling    = false;   // prevents observer ↔ setPos recursion

        // Restore saved position immediately
        const saved = load(storageKey);
        if (saved && !saved.snapped) setPos(el, saved.left, saved.top, snapToTop);

        // Watch for Blazor re-renders resetting the style attribute
        const styleObs = new MutationObserver(() => {
            if (el._ftSettling) return;
            const s = load(storageKey);
            if (s && !s.snapped) {
                el._ftSettling = true;
                setPos(el, s.left, s.top, snapToTop);
                el._ftSettling = false;
            }
        });
        styleObs.observe(el, { attributes: true, attributeFilter: ['style'] });

        // Find the designated grip or fall back to the whole element
        const handle = el.querySelector('[data-drag-handle]') || el;
        handle.style.cursor = 'grab';

        handle.addEventListener('mousedown', e => {
            if (e.button !== 0) return;
            e.preventDefault();

            const rect = el.getBoundingClientRect();
            const sx = e.clientX, sy = e.clientY;
            const sl = rect.left,  st = rect.top;

            document.documentElement.classList.add('ft-no-select');
            handle.style.cursor = 'grabbing';

            function onMove(ev) {
                const left = clamp(sl + ev.clientX - sx, 0, window.innerWidth  - el.offsetWidth);
                const top  = clamp(st + ev.clientY - sy, 0, window.innerHeight - 48);
                el._ftSettling = true;
                setPos(el, left, top, snapToTop);
                el._ftSettling = false;
            }

            function onUp() {
                document.documentElement.classList.remove('ft-no-select');
                handle.style.cursor = 'grab';
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup',   onUp);

                const r = el.getBoundingClientRect();
                if (snapToTop && r.top < 32) {
                    snapTop(el);
                    save(storageKey, { snapped: true });
                } else {
                    save(storageKey, { left: r.left, top: r.top, snapped: false });
                }
            }

            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup',   onUp);
        });
    }

    // ── DOM watcher ───────────────────────────────────────────────────────────

    function scanAndInit() {
        const toolbar = document.getElementById('ft-toolbar');
        if (toolbar) initElement(toolbar, 'toolbar', false);

        const appbar = document.getElementById('ft-appbar');
        if (appbar) initElement(appbar, 'appbar', true);
    }

    // Run once immediately (handles elements already in the DOM)
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scanAndInit);
    } else {
        scanAndInit();
    }

    // Watch for Blazor adding elements dynamically
    const bodyObs = new MutationObserver(scanAndInit);
    const startBodyObs = () => bodyObs.observe(document.body, { childList: true, subtree: true });
    if (document.body) startBodyObs();
    else document.addEventListener('DOMContentLoaded', startBodyObs);

    // ── public API (Blazor can still call init, it's a no-op after auto-init) ─

    return {
        init(elementId, storageKey, opts) {
            const el = document.getElementById(elementId);
            if (!el) return;
            initElement(el, storageKey, !!(opts && opts.snapToTop));
        },
        reset(elementId, storageKey) {
            localStorage.removeItem('ftdrag:' + storageKey);
            const el = document.getElementById(elementId);
            if (!el) return;
            el.style.left = ''; el.style.top = '';
            el.style.right = ''; el.style.bottom = ''; el.style.width = '';
            el._ftDragReady = false;
            el.classList.remove('ft-appbar-floating', 'ft-appbar-snapped');
        }
    };
})();
