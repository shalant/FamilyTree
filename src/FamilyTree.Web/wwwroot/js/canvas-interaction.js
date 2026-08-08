// Figma-style pan/zoom for the family tree canvas.
// JS owns the transform; Blazor owns the content — no style conflicts.
// Pointer Events unify mouse/touch/pen into one code path: setPointerCapture
// keeps delivering events to _viewport even if the pointer leaves its bounds,
// so no window-level fallback listeners are needed the way plain mousedown required.

// const EDGE_MARGIN = 150; // min pixels of canvas that must remain in viewport
const EDGE_MARGIN = 40; // min pixels of canvas that must remain in viewport

let _viewport = null;
let _transform = null;
let _zoom = 1;
let _panX = 0;
let _panY = 0;
let _dragging = false;
let _lastX = 0;
let _lastY = 0;
let _activePointers = new Map(); // pointerId -> {x, y}
let _pinchStartDist = null;      // distance between the two active pointers, updated each move

export function init(viewportId, transformId, focusX, focusY, genTopY, genBotY) {
    dispose();

    _viewport = document.getElementById(viewportId);
    _transform = document.getElementById(transformId);
    if (!_viewport || !_transform) return;

    _applyDefaultView(focusX, focusY, genTopY, genBotY);

    _syncTimeline();

    // Reveal after the correct position is applied — prevents the SSR-position flash
    _viewport.style.opacity = '1';

    _viewport.addEventListener('wheel', _onWheel, { passive: false });
    _viewport.addEventListener('pointerdown', _onPointerDown);
    _viewport.addEventListener('pointermove', _onPointerMove);
    _viewport.addEventListener('pointerup', _onPointerUp);
    _viewport.addEventListener('pointercancel', _onPointerCancel);
    _viewport.addEventListener('pointerleave', _onPointerUp);
    _viewport.style.cursor = 'grab';
}

// Re-fits ±1 generation around the focus person, same as the initial load —
// unlike centerOn(), this also resets zoom rather than just re-panning at
// whatever zoom level the user is currently at.
export function resetView(focusX, focusY, genTopY, genBotY) {
    if (!_viewport) return;
    _applyDefaultView(focusX, focusY, genTopY, genBotY);
}

function _applyDefaultView(focusX, focusY, genTopY, genBotY) {
    const rect = _viewport.getBoundingClientRect();

    if (genTopY != null && genBotY != null && genBotY > genTopY) {
        // Fit ±1 generation in the viewport (parents row → children row).
        const genHeight = genBotY - genTopY;
        const padding = 80;
        _zoom = Math.min(rect.height / (genHeight + padding * 2), 2.5);
        _zoom = Math.max(_zoom, 0.3);
        const midY = (genTopY + genBotY) / 2;
        _panX = rect.width  / 2 - focusX * _zoom;
        _panY = rect.height / 2 - midY    * _zoom;
    } else {
        _zoom = 0.8;
        _panX = rect.width  / 2 - focusX * _zoom;
        _panY = rect.height / 2 - focusY * _zoom;
    }

    _clampPan();
    _applyTransform();
}

export function dispose() {
    if (_viewport) {
        _viewport.removeEventListener('wheel', _onWheel);
        _viewport.removeEventListener('pointerdown', _onPointerDown);
        _viewport.removeEventListener('pointermove', _onPointerMove);
        _viewport.removeEventListener('pointerup', _onPointerUp);
        _viewport.removeEventListener('pointercancel', _onPointerCancel);
        _viewport.removeEventListener('pointerleave', _onPointerUp);
        _viewport.style.cursor = '';
    }
    _activePointers.clear();
    _pinchStartDist = null;
    _dragging = false;
    _viewport = null;
    _transform = null;
}

export function zoomIn() {
    if (!_viewport) return;
    const rect = _viewport.getBoundingClientRect();
    _applyZoomAt(1.2, rect.width / 2, rect.height / 2);
}

export function zoomOut() {
    if (!_viewport) return;
    const rect = _viewport.getBoundingClientRect();
    _applyZoomAt(1 / 1.2, rect.width / 2, rect.height / 2);
}

export function centerOn(focusX, focusY) {
    if (!_viewport) return;
    const rect = _viewport.getBoundingClientRect();
    _panX = rect.width  / 2 - focusX * _zoom;
    _panY = rect.height / 2 - focusY * _zoom;
    _clampPan();
    _applyTransform();
}

function _onWheel(e) {
    e.preventDefault();
    const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1;
    const rect = _viewport.getBoundingClientRect();
    _applyZoomAt(factor, e.clientX - rect.left, e.clientY - rect.top);
}

function _applyZoomAt(factor, mx, my) {
    const newZoom = Math.min(Math.max(_zoom * factor, 0.05), 8);
    const scale = newZoom / _zoom;
    _panX = mx - scale * (mx - _panX);
    _panY = my - scale * (my - _panY);
    _zoom = newZoom;
    _clampPan();
    _applyTransform();
}

function _distance(p1, p2) {
    return Math.hypot(p1.x - p2.x, p1.y - p2.y);
}

function _midpoint(p1, p2) {
    return { x: (p1.x + p2.x) / 2, y: (p1.y + p2.y) / 2 };
}

function _onPointerDown(e) {
    if (e.button !== 0) return;
    if (e.target.closest('.person-node')) return;

    _viewport.setPointerCapture(e.pointerId);
    _activePointers.set(e.pointerId, { x: e.clientX, y: e.clientY });

    if (_activePointers.size === 2) {
        _dragging = false;
        const [p1, p2] = [..._activePointers.values()];
        _pinchStartDist = _distance(p1, p2);
    } else if (_activePointers.size === 1) {
        _dragging = true;
        _lastX = e.clientX;
        _lastY = e.clientY;
        if (e.pointerType === 'mouse') _viewport.style.cursor = 'grabbing';
        e.preventDefault();
    }
}

function _onPointerMove(e) {
    if (!_activePointers.has(e.pointerId)) return;
    _activePointers.set(e.pointerId, { x: e.clientX, y: e.clientY });

    if (_activePointers.size === 2) {
        const rect = _viewport.getBoundingClientRect();
        const [p1, p2] = [..._activePointers.values()];
        const newDist = _distance(p1, p2);
        const mid = _midpoint(p1, p2);
        if (_pinchStartDist) {
            const factor = newDist / _pinchStartDist;
            _applyZoomAt(factor, mid.x - rect.left, mid.y - rect.top);
        }
        _pinchStartDist = newDist;
        return;
    }

    if (!_dragging) return;
    _panX += e.clientX - _lastX;
    _panY += e.clientY - _lastY;
    _lastX = e.clientX;
    _lastY = e.clientY;
    _clampPan();
    _applyTransform();
}

function _onPointerUp(e) {
    _activePointers.delete(e.pointerId);
    if (_viewport && _viewport.hasPointerCapture && _viewport.hasPointerCapture(e.pointerId)) {
        _viewport.releasePointerCapture(e.pointerId);
    }

    if (_activePointers.size === 1) {
        // Dropping from a pinch back to one finger — resume panning from
        // that finger's current position instead of jumping.
        const [remaining] = [..._activePointers.values()];
        _dragging = true;
        _lastX = remaining.x;
        _lastY = remaining.y;
        _pinchStartDist = null;
    } else {
        _dragging = false;
        _pinchStartDist = null;
    }

    if (_viewport) _viewport.style.cursor = 'grab';
}

function _onPointerCancel(e) {
    _onPointerUp(e);
}

// Prevent panning past EDGE_MARGIN px beyond canvas boundaries.
// When the canvas is smaller than the viewport (zoomed out), center it instead.
function _clampPan() {
    if (!_viewport || !_transform) return;
    const vw = _viewport.clientWidth;
    const vh = _viewport.clientHeight;
    const content = _transform.firstElementChild;
    if (!content) return;
    const cw = content.offsetWidth  * _zoom;
    const ch = content.offsetHeight * _zoom;

    // Allow panning so that any point on the canvas can be centered.
    // Range: from "rightmost canvas pixel at viewport centre"
    //        to   "leftmost canvas pixel at viewport centre".
    // This fixes the bug where nodes near the canvas left/top edge
    // couldn't be centred because the old clamp (maxX=0) prevented
    // positive panX values.
    _panX = Math.min(vw / 2, Math.max(vw / 2 - cw, _panX));
    _panY = Math.min(vh / 2, Math.max(vh / 2 - ch, _panY));
}

function _applyTransform() {
    if (!_transform) return;
    _transform.style.transform = `translate(${_panX}px, ${_panY}px) scale(${_zoom})`;
    _syncTimeline();
}

function _syncTimeline() {
    const timeline = document.getElementById('ft-timeline');
    if (!timeline) return;
    const vh = _viewport ? _viewport.clientHeight : window.innerHeight;
    const labels = timeline.querySelectorAll('[data-canvas-y]');
    labels.forEach(el => {
        const canvasY = parseFloat(el.dataset.canvasY);
        const screenY = canvasY * _zoom + _panY;
        el.style.top = screenY + 'px';
        el.style.visibility = (screenY < -20 || screenY > vh + 20) ? 'hidden' : 'visible';
    });
}
