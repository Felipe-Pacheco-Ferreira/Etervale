import test from 'node:test';
import assert from 'node:assert/strict';
import { makeDraggable } from '../wwwroot/draggable.js';

function element(rect) {
    const listeners = {};
    return {
        rect,
        style: {},
        listeners,
        addEventListener(name, listener) { listeners[name] = listener; },
        getBoundingClientRect() {
            const [x, y] = (this.style.translate || '0px 0px').split(' ').map(value => parseFloat(value));
            return { left: rect.left + x, top: rect.top + y, width: rect.width, height: rect.height };
        },
        setPointerCapture() {},
        releasePointerCapture() {}
    };
}

const pointer = (x, y, target = { closest: () => null }) => ({ button: 0, pointerId: 1, clientX: x, clientY: y, target, preventDefault() {} });

test('a page window follows its header within the viewport and leaves the header controls their clicks', () => {
    const view = { innerWidth: 800, innerHeight: 600 };
    const windowElement = element({ left: 200, top: 100, width: 300, height: 200 });
    const handle = element({ left: 200, top: 100, width: 300, height: 30 });
    makeDraggable(windowElement, handle, view);
    handle.listeners.pointerdown(pointer(250, 110));
    handle.listeners.pointermove(pointer(300, 160));
    assert.equal(windowElement.style.translate, '50px 50px');
    handle.listeners.pointermove(pointer(2000, -500));
    assert.equal(windowElement.style.translate, '300px -100px');
    handle.listeners.pointerup(pointer(2000, -500));
    handle.listeners.pointermove(pointer(250, 110));
    assert.equal(windowElement.style.translate, '300px -100px');

    const control = element({ left: 0, top: 0, width: 10, height: 10 });
    const fresh = element({ left: 200, top: 100, width: 300, height: 200 });
    const header = element({ left: 200, top: 100, width: 300, height: 30 });
    makeDraggable(fresh, header, view);
    header.listeners.pointerdown(pointer(250, 110, { closest: selector => selector.includes('button') ? control : null }));
    header.listeners.pointermove(pointer(300, 160));
    assert.equal(fresh.style.translate, undefined);
    assert.equal(makeDraggable(null, header, view), null);
});
