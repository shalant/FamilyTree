// ES module — imported by Home.razor via IJSObjectReference.
// Blazor owns the final position (C# state). JS owns the gesture.

export function loadPos(key) {
    try {
        const p = JSON.parse(localStorage.getItem('ftdrag:' + key));
        return p ? [p.left, p.top] : null;
    } catch {
        return null;
    }
}

export function init(dotNetRef) {
    setup(dotNetRef, 'ft-toolbar', '[data-drag-handle]', 'toolbar', 0);
    setup(dotNetRef, 'ft-hero',    '[data-drag-handle]', 'hero',    68);
}

function setup(dotNetRef, elId, handleSel, key, minTop) {
    const el = document.getElementById(elId);
    if (!el || el._ftDragReady) return;
    el._ftDragReady = true;

    el.addEventListener('mousedown', e => {
        if (e.button !== 0) return;

        // Only drag when the click starts inside the designated handle.
        // We look up the handle at click time (not at init time) so that
        // Blazor's async rendering doesn't matter.
        const handle = el.querySelector(handleSel);
        if (handle && !handle.contains(e.target)) return;

        e.preventDefault();

        const rect = el.getBoundingClientRect();
        const grabOffsetX = e.clientX - rect.left;
        const grabOffsetY = e.clientY - rect.top;

        document.documentElement.classList.add('ft-no-select');
        if (handle) handle.style.cursor = 'grabbing';

        function onMove(ev) {
            const left = Math.round(Math.max(0,
                Math.min(window.innerWidth  - el.offsetWidth,  ev.clientX - grabOffsetX)));
            const top  = Math.round(Math.max(minTop,
                Math.min(window.innerHeight - el.offsetHeight, ev.clientY - grabOffsetY)));
            el.style.left   = left + 'px';
            el.style.top    = top  + 'px';
            el.style.right  = 'auto';
            el.style.bottom = 'auto';
        }

        function onUp() {
            document.documentElement.classList.remove('ft-no-select');
            if (handle) handle.style.cursor = '';
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup',   onUp);

            // Read the final resting position and hand it to Blazor.
            // Blazor re-renders with these values so it never fights JS.
            const r    = el.getBoundingClientRect();
            const left = Math.round(r.left);
            const top  = Math.round(r.top);
            localStorage.setItem('ftdrag:' + key, JSON.stringify({ left, top }));
            dotNetRef.invokeMethodAsync('OnDragEnd', key, left, top);
        }

        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup',   onUp);
    });
}
