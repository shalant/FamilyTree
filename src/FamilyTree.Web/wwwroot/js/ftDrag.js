/**
 * ftDrag — lightweight drag-to-reposition for fixed-position elements.
 * Persists positions to localStorage. Supports a "snap-to-top" mode for the AppBar
 * which restores full-width behaviour when dragged back near the top edge.
 */
window.ftDrag = {

    init(elementId, storageKey, opts) {
        opts = opts || {};
        const el = document.getElementById(elementId);
        if (!el) return;

        const snapToTop = !!opts.snapToTop;

        // Restore saved position, if any
        const saved = ftDrag._load(storageKey);
        if (saved && saved.snapped === false) {
            ftDrag._applyFloat(el, saved.left, saved.top, snapToTop);
        } else if (saved && saved.snapped) {
            // Was snapped — keep full-width default CSS
        }
        // If no save, leave CSS defaults in place (bottom:24px / top:0 etc.)

        // Find the designated handle, or fall back to the entire element
        const handle = el.querySelector('[data-drag-handle]') || el;
        handle.style.cursor = 'grab';

        let active = false;
        let originX, originY, startLeft, startTop;

        function pointerDown(clientX, clientY) {
            active = true;
            const r = el.getBoundingClientRect();
            originX = clientX;
            originY = clientY;
            startLeft = r.left;
            startTop  = r.top;
            el.style.transition = 'none';
            el.classList.add('ft-dragging');
            document.body.style.userSelect = 'none';
            handle.style.cursor = 'grabbing';
        }

        function pointerMove(clientX, clientY) {
            if (!active) return;
            let left = startLeft + (clientX - originX);
            let top  = startTop  + (clientY - originY);

            // Clamp so element stays on screen
            left = Math.max(0, Math.min(left, window.innerWidth  - el.offsetWidth));
            top  = Math.max(0, Math.min(top,  window.innerHeight - 48));

            ftDrag._applyFloat(el, left, top, snapToTop);
        }

        function pointerUp() {
            if (!active) return;
            active = false;
            el.classList.remove('ft-dragging');
            document.body.style.userSelect = '';
            handle.style.cursor = 'grab';

            const r = el.getBoundingClientRect();

            // Snap back to full-width top bar if within 32px of top edge
            if (snapToTop && r.top < 32) {
                ftDrag._snapToTop(el);
                ftDrag._save(storageKey, { snapped: true });
                return;
            }

            ftDrag._save(storageKey, { left: r.left, top: r.top, snapped: false });
        }

        // Mouse
        handle.addEventListener('mousedown', e => {
            if (e.button !== 0) return;
            e.preventDefault();
            pointerDown(e.clientX, e.clientY);
        });
        document.addEventListener('mousemove', e => pointerMove(e.clientX, e.clientY));
        document.addEventListener('mouseup',   () => pointerUp());

        // Touch
        handle.addEventListener('touchstart', e => {
            e.preventDefault();
            const t = e.touches[0];
            pointerDown(t.clientX, t.clientY);
        }, { passive: false });
        document.addEventListener('touchmove', e => {
            const t = e.touches[0];
            pointerMove(t.clientX, t.clientY);
        });
        document.addEventListener('touchend', () => pointerUp());
    },

    _applyFloat(el, left, top, snapToTop) {
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
    },

    _snapToTop(el) {
        el.style.left   = '0';
        el.style.top    = '0';
        el.style.right  = '0';
        el.style.bottom = 'auto';
        el.style.width  = '100%';
        el.classList.remove('ft-appbar-floating');
        el.classList.add('ft-appbar-snapped');
    },

    reset(elementId, storageKey) {
        localStorage.removeItem('ftdrag:' + storageKey);
        const el = document.getElementById(elementId);
        if (!el) return;
        el.style.left   = '';
        el.style.top    = '';
        el.style.right  = '';
        el.style.bottom = '';
        el.style.width  = '';
        el.classList.remove('ft-appbar-floating', 'ft-appbar-snapped', 'ft-dragging');
    },

    _load(key) {
        try { return JSON.parse(localStorage.getItem('ftdrag:' + key)); }
        catch { return null; }
    },
    _save(key, val) {
        localStorage.setItem('ftdrag:' + key, JSON.stringify(val));
    }
};
