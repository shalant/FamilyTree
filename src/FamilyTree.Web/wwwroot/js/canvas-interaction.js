// Figma-style pan/zoom for the family tree canvas.
// JS owns the transform; Blazor owns the content — no style conflicts.

const EDGE_MARGIN = 150; // minimum pixels of canvas that must remain visible

let _viewport = null;
let _transform = null;
let _zoom = 1;
let _panX = 0;
let _panY = 0;
let _dragging = false;
let _lastX = 0;
let _lastY = 0;

export function init(viewportId, transformId, focusX, focusY) {
    dispose();

    _viewport = document.getElementById(viewportId);
    _transform = document.getElementById(transformId);
    if (!_viewport || !_transform) return;

    const rect = _viewport.getBoundingClientRect();
    _zoom = 0.85;
    _panX = rect.width / 2 - focusX * _zoom;
    _panY = rect.height / 2 - focusY * _zoom;
    _applyTransform();

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
    _panX = rect.width / 2 - focusX * _zoom;
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

// Prevent the canvas from being panned so far that only EDGE_MARGIN px remain visible.
function _clampPan() {
    if (!_viewport || !_transform) return;
    const vw = _viewport.clientWidth;
    const vh = _viewport.clientHeight;
    const content = _transform.firstElementChild;
    if (!content) return;
    const cw = content.offsetWidth * _zoom;
    const ch = content.offsetHeight * _zoom;

    // panX: left edge of canvas in screen coords
    // Canvas right edge must stay >= EDGE_MARGIN from left of viewport
    _panX = Math.max(_panX, EDGE_MARGIN - cw);
    // Canvas left edge must stay <= vw - EDGE_MARGIN from right of viewport
    _panX = Math.min(_panX, vw - EDGE_MARGIN);
    // Same for Y
    _panY = Math.max(_panY, EDGE_MARGIN - ch);
    _panY = Math.min(_panY, vh - EDGE_MARGIN);
}

function _applyTransform() {
    if (!_transform) return;
    _transform.style.transform = `translate(${_panX}px, ${_panY}px) scale(${_zoom})`;
}
