// Figma-style pan/zoom for the family tree canvas.
// JS owns the transform; Blazor owns the content — no style conflicts.

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

export function init(viewportId, transformId, focusX, focusY, genTopY, genBotY) {
    dispose();

    _viewport = document.getElementById(viewportId);
    _transform = document.getElementById(transformId);
    if (!_viewport || !_transform) return;

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

    _syncTimeline();

    // Reveal after the correct position is applied — prevents the SSR-position flash
    _viewport.style.opacity = '1';

    _viewport.addEventListener('wheel', _onWheel, { passive: false });
    _viewport.addEventListener('mousedown', _onMouseDown);
    window.addEventListener('mousemove', _onMouseMove);
    window.addEventListener('mouseup', _onMouseUp);
    _viewport.style.cursor = 'grab';
}

export function dispose() {
    if (_viewport) {
        _viewport.removeEventListener('wheel', _onWheel);
        _viewport.removeEventListener('mousedown', _onMouseDown);
        _viewport.style.cursor = '';
    }
    window.removeEventListener('mousemove', _onMouseMove);
    window.removeEventListener('mouseup', _onMouseUp);
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

function _onMouseDown(e) {
    if (e.button !== 0) return;
    if (e.target.closest('.person-node')) return;
    _dragging = true;
    _lastX = e.clientX;
    _lastY = e.clientY;
    _viewport.style.cursor = 'grabbing';
    e.preventDefault();
}

function _onMouseMove(e) {
    if (!_dragging) return;
    _panX += e.clientX - _lastX;
    _panY += e.clientY - _lastY;
    _lastX = e.clientX;
    _lastY = e.clientY;
    _clampPan();
    _applyTransform();
}

function _onMouseUp() {
    _dragging = false;
    if (_viewport) _viewport.style.cursor = 'grab';
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
