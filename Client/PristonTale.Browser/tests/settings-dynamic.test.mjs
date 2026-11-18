import test from 'node:test';
import assert from 'node:assert/strict';
import { createSettings } from '../wwwroot/settings.js';

class Node {
    constructor(tag = 'div') {
        this.tag = tag; this.children = []; this.dataset = {}; this.attributes = {}; this.events = new Map();
        this.style = { setProperty() {} }; this.className = ''; this.scrollTop = 0;
        this.classList = { add: value => { this.className += ' ' + value; } };
    }
    setAttribute(key, value) { this.attributes[key] = value; }
    append(...children) { this.children.push(...children); }
    replaceChildren() { this.children = []; }
    addEventListener(name, handler) { this.events.set(name, handler); }
    fire(name) { this.events.get(name)?.({}); }
    showModal() { this.open = true; }
    close() { this.open = false; this.fire('close'); }
    focus() {}
    querySelector(selector) { return this.parts?.get(selector.match(/data-role="([^"]+)"/)?.[1]) ?? null; }
    querySelectorAll() { return []; }
    find(predicate) { if (predicate(this)) return this; for (const child of this.children) { const value = child.find?.(predicate); if (value) return value; } }
}

function fixture(run, saved = {}) {
    const originals = new Map(['document', 'localStorage', 'navigator', 'addEventListener'].map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
    const storage = new Map([['avalon.settings.quality', 'High']]), writes = [], saves = [], callbacks = [];
    let settings = { quality: 'High', antiAliasing: 'Fxaa', renderScale: 1, dynamicResolution: true, ...saved };
    const host = { Settings: () => JSON.stringify(settings), LoadSettings() {}, SaveSettings(json) {
        settings = JSON.parse(json); saves.push(settings); return JSON.stringify(settings);
    } };
    const root = new Node(), dialog = new Node(), opener = new Node(), body = new Node(), save = new Node();
    root.parts = new Map([['settings', dialog], ['settings-open', opener]]);
    dialog.parts = new Map([['settings-body', body], ['settings-save', save], ['settings-note', new Node()],
        ['settings-site', new Node()], ['settings-close', new Node()], ['settings-cancel', new Node()]]);
    Object.defineProperty(globalThis, 'document', { configurable: true, value: { createElement: tag => new Node(tag) } });
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: {} });
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
        getItem: key => storage.get(key) ?? null, setItem(key, value) { writes.push([key, value]); storage.set(key, value); }
    } });
    Object.defineProperty(globalThis, 'addEventListener', { configurable: true, value() {} });
    try {
        const api = createSettings(root, () => host, () => 'pt-BR', () => {}, () => callbacks.push(api.qualityLabel));
        api.start(host);
        run({ api, opener, body, save, writes, saves, callbacks, storage });
    } finally {
        for (const [key, descriptor] of originals) {
            if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
        }
    }
}

test('real settings checkbox edits and saves dynamicResolution without changing the preset', () => fixture(f => {
    assert.equal(f.api.dynamicResolutionEnabled, true);
    assert.equal(f.api.renderScale, 1);
    f.opener.fire('click');
    const control = f.body.find(node => node.attributes['aria-label'] === 'Resolução dinâmica');
    assert.ok(control); assert.equal(control.checked, true); assert.equal(control.disabled, false);
    control.checked = false; control.fire('change'); f.save.fire('click');
    assert.equal(f.saves.length, 1);
    assert.equal(f.saves[0].dynamicResolution, false);
    assert.equal(f.saves[0].quality, 'High');
    assert.equal(f.api.dynamicResolutionEnabled, false);
    assert.equal(f.storage.get('avalon.settings.quality'), 'High');
    assert.equal(f.api.qualityLabel, 'Alto');
}));

test('effective renderer reports update only the visible label and never persist or markCustom', () => fixture(f => {
    const originalCallbacks = f.callbacks.length;
    f.api.setDynamicResolutionState({ enabled: true, scale: 1, width: 1280, height: 720 });
    assert.equal(f.api.qualityLabel, 'Alto');
    assert.equal(f.callbacks.length, originalCallbacks);
    const state = { enabled: true, scale: .8, width: 1024, height: 576 };
    f.api.setDynamicResolutionState(state);
    assert.equal(f.api.qualityLabel, 'Alto · 3D 1024×576 (80%, dinâmica)');
    assert.equal(f.callbacks.length, originalCallbacks + 1);
    f.api.setDynamicResolutionState({ ...state });
    assert.equal(f.callbacks.length, originalCallbacks + 1);
    state.scale = .5;
    assert.equal(f.api.qualityLabel, 'Alto · 3D 1024×576 (80%, dinâmica)', 'caller mutation does not alter renderer state');
    f.api.setDynamicResolutionState({ enabled: false, scale: 1, width: 1280, height: 720 });
    assert.equal(f.api.qualityLabel, 'Alto');
    assert.equal(f.callbacks.length, originalCallbacks + 2);
    assert.equal(f.saves.length, 0);
    assert.equal(f.writes.length, 0);
    assert.equal(f.api.renderScale, 1);
}));

test('the saved render scale caps the suffix and invalid or restored reports remove it', () => fixture(f => {
    assert.equal(f.api.renderScale, .75);
    f.api.setDynamicResolutionState({ enabled: true, scale: .8, width: 1024, height: 576 });
    assert.equal(f.api.qualityLabel, 'Alto');
    f.api.setDynamicResolutionState({ enabled: true, scale: .5, width: 640, height: 360 });
    assert.equal(f.api.qualityLabel, 'Alto · 3D 640×360 (50%, dinâmica)');
    f.api.setDynamicResolutionState({ enabled: true, scale: NaN, width: 640, height: 360 });
    assert.equal(f.api.qualityLabel, 'Alto');
    f.api.setDynamicResolutionState({ enabled: true, scale: .75, width: 960, height: 540 });
    assert.equal(f.api.qualityLabel, 'Alto');
    assert.equal(f.writes.length, 0);
}, { renderScale: .75 }));

test('settings-free layouts retain the controller API without creating a fake dynamic state', () => {
    const api = createSettings({ querySelector: () => null }, () => null, () => 'en');
    assert.equal(api.dynamicResolutionEnabled, true); assert.equal(api.renderScale, 1);
    assert.doesNotThrow(() => api.setDynamicResolutionState({ enabled: true, scale: .5, width: 640, height: 360 }));
    assert.equal(api.qualityLabel, 'Automatic');
});

test('runtime shadow cadence reports are visible alone or with scaling without changing saved settings', () => fixture(f => {
    const state = { enabled: true, scale: 1, width: 1280, height: 720, shadowUpdateRate: 30 };
    f.api.setDynamicResolutionState(state);
    assert.equal(f.api.qualityLabel, 'Alto · sombras 30 Hz');
    const callbacks = f.callbacks.length;
    f.api.setDynamicResolutionState({ ...state });
    assert.equal(f.callbacks.length, callbacks);
    f.api.setDynamicResolutionState({ ...state, scale: .8, width: 1024, height: 576 });
    assert.equal(f.api.qualityLabel, 'Alto · 3D 1024×576 (80%, dinâmica) · sombras 30 Hz');
    f.api.setDynamicResolutionState({ ...state, shadowUpdateRate: 60 });
    assert.equal(f.api.qualityLabel, 'Alto');
    assert.equal(f.writes.length, 0); assert.equal(f.saves.length, 0);
}, { shadowUpdateRate: 60 }));

test('a base preset already using30Hz does not falsely report an adaptive shadow change', () => fixture(f => {
    f.api.setDynamicResolutionState({ enabled: true, scale: 1, width: 1280, height: 720, shadowUpdateRate: 30 });
    assert.equal(f.api.qualityLabel, 'Alto');
    assert.equal(f.writes.length, 0);
}, { shadowUpdateRate: 30 }));
