import test from 'node:test';
import assert from 'node:assert/strict';
import { enterLandscape, touchOnlyDevice } from '../wwwroot/touch-hud.js';

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
