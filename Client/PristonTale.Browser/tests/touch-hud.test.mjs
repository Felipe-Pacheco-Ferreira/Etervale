import test from 'node:test';
import assert from 'node:assert/strict';
import { chatLineParts, cooldownAt, enterLandscape, pictureCanvas, touchOnlyDevice } from '../wwwroot/touch-hud.js';

test('a chat line splits its speaker up to an early colon, in its channel colours', () => {
    const party = chatLineParts('Bruno: bora', 'Party');
    assert.deepEqual([party.name, party.rest, party.body, party.nameColor], ['Bruno:', ' bora', 'rgb(121,224,255)', 'rgb(170,238,255)']);
    const plain = chatLineParts('O servidor reinicia em 5 minutos', 'System');
    assert.deepEqual([plain.name, plain.rest, plain.body], ['', 'O servidor reinicia em 5 minutos', 'rgb(236,225,203)']);
    assert.equal(chatLineParts(':)', 'Player').name, '', 'a colon at the start names no one');
    assert.equal(chatLineParts('x'.repeat(45) + ': late', 'Player').name, '', 'a colon past 40 characters is part of the text');
});

test('a picture from the game lands on a canvas of its size with its pixels, and a short one is refused', () => {
    const drawn = [];
    globalThis.ImageData ??= class {
        constructor(data, width, height) {
            Object.assign(this, { data, width, height });
        }
    };
    const makeCanvas = () => ({ getContext: () => ({ putImageData: image => drawn.push(image) }) });
    const bytes = new Uint8Array([2, 0, 1, 0, 1, 2, 3, 4, 5, 6, 7, 8]);
    const canvas = pictureCanvas(bytes, makeCanvas);
    assert.equal(canvas.width, 2);
    assert.equal(canvas.height, 1);
    assert.deepEqual([...drawn[0].data], [1, 2, 3, 4, 5, 6, 7, 8]);
    assert.equal(pictureCanvas(new Uint8Array([2, 0, 1, 0, 1, 2]), makeCanvas), null, 'fewer pixels than its size');
    assert.equal(pictureCanvas(new Uint8Array(0), makeCanvas), null, 'no picture');
});

test('a recovering skill counts its whole seconds down and its shade shrinks with the time left', () => {
    assert.deepEqual(cooldownAt(5000, 4000, 1000), { seconds: 4, fraction: 1 });
    assert.deepEqual(cooldownAt(5000, 4000, 3500), { seconds: 2, fraction: 0.375 });
    assert.deepEqual(cooldownAt(5000, 4000, 6000), { seconds: 0, fraction: 0 });
    assert.deepEqual(cooldownAt(0, 0, 100), { seconds: 0, fraction: 0 }, 'a ready skill');
});

function media(matches) {
    return query => ({ matches: matches[query] ?? false });
}

test('only a touch screen without any fine pointer is a touch-only device', () => {
    const phone = media({ '(pointer: coarse)': true, '(any-pointer: fine)': false });
    const laptop = media({ '(pointer: coarse)': true, '(any-pointer: fine)': true });
    assert.equal(touchOnlyDevice(phone, 5), true);
    assert.equal(touchOnlyDevice(laptop, 10), false, 'a touch laptop with a trackpad keeps the PC HUD');
    assert.equal(touchOnlyDevice(phone, 0), false, 'no touch points');
    assert.equal(touchOnlyDevice(undefined, 5), false, 'no media queries');
});

test('entering landscape asks for full screen, then locks landscape, and survives refusals', async () => {
    const calls = [];
    const doc = { fullscreenElement: null, documentElement: { requestFullscreen: async options => calls.push(['fullscreen', options.navigationUI]) } };
    const screenInfo = { orientation: { lock: async kind => calls.push(['lock', kind]) } };
    await enterLandscape(doc, screenInfo);
    assert.deepEqual(calls, [['fullscreen', 'hide'], ['lock', 'landscape']]);

    const refusing = { fullscreenElement: null, documentElement: { requestFullscreen: async () => { throw new Error('denied'); } } };
    const noLock = { orientation: { lock: async () => { throw new Error('not supported'); } } };
    await enterLandscape(refusing, noLock);
    await enterLandscape(undefined, undefined);
});
