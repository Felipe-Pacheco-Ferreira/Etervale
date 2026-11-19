// A window of the page dragged by its header: the pointer moves it with the CSS translate property, which adds to the
// window's own placement and transform, and keeps it inside the viewport. Controls in the header keep their clicks.
// The position lasts while the page does; a double click on the header, or Settings' reset, puts the window back.
const controls = 'button, select, input, textarea, a, label';
const windows = new Set();

export function resetDraggables() {
    for (const entry of windows) entry.place(0, 0);
}

export function makeDraggable(windowElement, handle, view = globalThis) {
    if (!windowElement || !handle) return null;
    let x = 0;
    let y = 0;
    let grab = null;

    function place(nextX, nextY) {
        const rect = windowElement.getBoundingClientRect();
        const left = rect.left - x;
        const top = rect.top - y;
        x = clamp(nextX, -left, view.innerWidth - rect.width - left);
        y = clamp(nextY, -top, view.innerHeight - rect.height - top);
        windowElement.style.translate = x || y ? `${x}px ${y}px` : '';
    }

    handle.addEventListener('pointerdown', event => {
        if (event.button !== 0 || event.target?.closest?.(controls)) return;
        grab = { x: event.clientX - x, y: event.clientY - y, pointer: event.pointerId };
        handle.setPointerCapture?.(event.pointerId);
        event.preventDefault();
    });
    handle.addEventListener('pointermove', event => {
        if (grab && event.pointerId === grab.pointer) place(event.clientX - grab.x, event.clientY - grab.y);
    });
    const release = event => {
        if (!grab || event.pointerId !== grab.pointer) return;
        grab = null;
        handle.releasePointerCapture?.(event.pointerId);
    };
    handle.addEventListener('pointerup', release);
    handle.addEventListener('pointercancel', release);
    handle.addEventListener('dblclick', event => {
        if (!event.target?.closest?.(controls)) place(0, 0);
    });
    const entry = { get offset() { return { x, y }; }, place };
    windows.add(entry);
    return entry;
}

// A window larger than the viewport keeps its top left corner on screen.
function clamp(value, low, high) {
    return Math.max(low, Math.min(value, Math.max(low, high)));
}
