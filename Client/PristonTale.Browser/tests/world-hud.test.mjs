import test from 'node:test';
import assert from 'node:assert/strict';
import { createWorldHud, latencyGrade } from '../wwwroot/world-hud.js';

test('the ping is good under 80 ms, fair under 200 and poor from there, and unknown without a sample', () => {
    assert.equal(latencyGrade(79), 'good');
    assert.equal(latencyGrade(80), 'fair');
    assert.equal(latencyGrade(199), 'fair');
    assert.equal(latencyGrade(200), 'poor');
    assert.equal(latencyGrade(undefined), '');
});

class Element {
    constructor() { this.style = {}; this.dataset = {}; this.handlers = new Map(); this.textContent = ''; this.open = false; }
    addEventListener(name, handler) { let list = this.handlers.get(name); if (!list) this.handlers.set(name, list = new Set()); list.add(handler); }
    removeEventListener(name, handler) { this.handlers.get(name)?.delete(handler); }
    dispatch(name, event = {}) { for (const handler of this.handlers.get(name) ?? []) handler(event); }
    setAttribute() {}
    append(child) { this.child = child; }
    querySelectorAll() { return []; }
    focus() { this.focused = true; }
}

function fixture(getRenderCount = () => null) {
    const parts = new Map(['world-hud', 'world-realm', 'world-fps', 'world-ping'].map(name => [name, new Element()]));
    const canvas = new Element(), root = new Element(), calls = [];
    root.querySelector = selector => selector === 'canvas' ? canvas : parts.get(selector.match(/data-role="([^"]+)"/)?.[1]) ?? null;
    const host = { SetWorldCanvasPointer(value) { calls.push(['pointer', value]); }, RunWorldAction(action) { calls.push(['action', action]); return true; },
        WorldActions() { return 'inventory'; }, WorldAutoHuntActive() { return false; }, EnableWorldHud() {} };
    const previous = globalThis.document;
    globalThis.document = { createElement: () => new Element() };
    let hud;
    try { hud = createWorldHud(root, () => host, () => 'pt-BR', () => 'High · 1280×720', getRenderCount); }
    finally { globalThis.document = previous; }
    return { hud, root, canvas, calls, fps: parts.get('world-fps'), quality: parts.get('world-hud').child };
}

test('world FPS uses acknowledged render-count deltas rather than main-loop callbacks or lifetime average', () => {
    let submitted = 100;
    const f = fixture(() => submitted);
    f.hud.show(); f.hud.frame(0);
    for (let tick = 1; tick <= 120; tick++) {
        submitted = 100 + Math.floor(tick / 4);
        f.hud.frame(tick * 1000 / 120);
    }
    assert.equal(f.fps.textContent, '30 FPS');
    for (let tick = 1; tick <= 120; tick++) f.hud.frame(1000 + tick * 1000 / 120);
    assert.equal(f.fps.textContent, '0 FPS', 'logic callbacks do not invent worker frames');
    assert.equal(f.quality.textContent, 'Qualidade: High · 1280×720');
});

test('world FPS resets cleanly when changing renderer counters or returning to direct rendering', () => {
    let submitted = 50;
    const f = fixture(() => submitted);
    f.hud.show(); f.hud.frame(0); submitted = 80; f.hud.frame(1000);
    assert.equal(f.fps.textContent, '30 FPS');
    submitted = 2; f.hud.frame(1001); submitted = 22; f.hud.frame(2001);
    assert.equal(f.fps.textContent, '20 FPS');
    submitted = null; f.hud.frame(2002);
    for (let frame = 1; frame <= 60; frame++) f.hud.frame(2002 + frame * 1000 / 60);
    assert.equal(f.fps.textContent, '60 FPS');
    f.hud.hide(); f.hud.show();
    assert.equal(f.fps.textContent, '');
});

test('fallback canvas replacement removes old pointer handlers and directs actions/focus to the new canvas', () => {
    const f = fixture(), replacement = new Element();
    f.hud.show(); f.canvas.dispatch('pointermove');
    assert.deepEqual(f.calls.at(-1), ['pointer', true]);
    f.hud.setCanvas(replacement);
    assert.deepEqual(f.calls.at(-1), ['pointer', false]);
    assert.equal(f.canvas.handlers.get('pointermove').size, 0);
    assert.equal(f.canvas.handlers.get('pointerleave').size, 0);
    const count = f.calls.length;
    f.canvas.dispatch('pointermove'); assert.equal(f.calls.length, count);
    replacement.dispatch('pointermove'); assert.deepEqual(f.calls.at(-1), ['pointer', true]);
    const button = { disabled: false, dataset: { worldAction: 'inventory' } };
    f.root.dispatch('click', { target: { closest: () => button } });
    assert.equal(replacement.focused, true);
    assert.equal(f.canvas.focused, undefined);
    f.hud.hide(); assert.deepEqual(f.calls.at(-1), ['pointer', false]);
    f.hud.setCanvas(replacement);
    assert.equal(replacement.handlers.get('pointermove').size, 1, 'same canvas is not rebound twice');
});

test('pages without the world HUD expose a harmless canvas-rebinding hook', () => {
    const hud = createWorldHud({ querySelector() { return null; } }, () => null, () => 'pt-BR');
    assert.doesNotThrow(() => hud.setCanvas({}));
});
