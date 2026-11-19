import test from 'node:test';
import assert from 'node:assert/strict';
import { rowDisabled, setRendererOptions, shadowChoice, shadowSettings, tabs, textureQualityFor } from '../wwwroot/settings-options.js';
import { createSettingsEffects, lifeEffects, wantsFullScreen } from '../wwwroot/settings-effects.js';
import { stageShadows } from '../wwwroot/three-scene.js';
import { makeDraggable, resetDraggables } from '../wwwroot/draggable.js';

const rows = () => Object.values(tabs).flat(2).flatMap(card => card.rows ?? []);

test('every visible row edits a setting, runs an action or explains', () => {
    for (const row of rows()) {
        if (row.caption || row.pair) continue;
        assert.ok(row.field || row.run, JSON.stringify(row.label ?? row.action));
        assert.notEqual(row.unavailable, true, row.field);
    }
});

test('a shadow choice is a map size, Simple keeps the shadows to characters, and the choice reads back', () => {
    for (const choice of ['High', 'Medium', 'Low', 'Simple', 'Off']) {
        assert.equal(shadowChoice({ shadowMapSize: 2048, ...shadowSettings(choice) }), choice);
    }
    assert.deepEqual(shadowSettings('Simple'), { shadowsEnabled: true, shadowMapSize: 2048, actorShadowsOnly: true });
    // The Low preset's 2048 map with two cascades is Medium here, not Simple.
    assert.equal(shadowChoice({ shadowsEnabled: true, shadowMapSize: 2048, shadowCascades: 2 }), 'Medium');
});

test('automatic textures follow the device memory and a chosen quality wins', () => {
    assert.equal(textureQualityFor('Auto', 2), 'VeryLight');
    assert.equal(textureQualityFor('Auto', 4), 'Light');
    assert.equal(textureQualityFor('Auto', 8), 'Original');
    assert.equal(textureQualityFor(undefined, undefined), 'Original');
    assert.equal(textureQualityFor('Light', 16), 'Light');
});

test('the finishing effects lock under the classic light and the fog distance without fog', () => {
    const tone = rows().find(row => row.field === 'toneMapping');
    const distance = rows().find(row => row.field === 'fogDistance');
    assert.equal(rowDisabled(tone, { classicLighting: true }), true);
    assert.equal(rowDisabled(tone, { classicLighting: false }), false);
    assert.equal(rowDisabled(distance, { distanceFog: 'Off' }), true);
    assert.equal(rowDisabled(distance, { distanceFog: 'Atmospheric' }), false);
});

test('rows the renderer cannot draw are hidden, not shown disabled', () => {
    setRendererOptions({ antiAliasing: ['Off', 'Fxaa'], unsupported: { autoExposure: 'x', proceduralSky: 'x' } });
    const find = field => rows().find(row => row.field === field);
    assert.equal(find('autoExposure').hidden, true);
    assert.equal(find('realisticSky').hidden, true);
    assert.equal(find('cloudQuality').hidden, true);
    assert.notEqual(find('ambientOcclusion').hidden, true);
});

test('life readings turn into the grey screen, the low-life border and one death', () => {
    const on = {};
    assert.deepEqual(lifeEffects(on, -1, false), { grey: false, lowLife: false, died: false, dead: false });
    assert.equal(lifeEffects(on, 0.2, false).lowLife, true);
    assert.equal(lifeEffects({ lowHealthWarning: false }, 0.2, false).lowLife, false);
    assert.deepEqual(lifeEffects(on, 0, false), { grey: true, lowLife: false, died: true, dead: true });
    assert.equal(lifeEffects(on, 0, true).died, false);
    assert.equal(lifeEffects({ grayscaleOnDeath: false }, 0, false).grey, false);
    assert.equal(wantsFullScreen({ windowMode: 'Fullscreen' }), true);
    assert.equal(wantsFullScreen({ windowMode: 'Windowed' }), false);
});

test('the page effects grey the canvas, hide the corner shortcuts and ask before leaving', () => {
    const style = () => ({});
    const canvas = { style: style() }, controls = { style: style() }, hud = { style: style() };
    const root = {
        append() {},
        querySelector(selector) {
            return { canvas, '.world-hud-controls': controls, '[data-role="world-hud"]': hud }[selector] ?? null;
        }
    };
    const listeners = {};
    const view = {
        document: { createElement: () => ({ style: {}, setAttribute() {} }), hidden: false },
        addEventListener(name, listener) { listeners[name] = listener; },
        removeEventListener() {}, setInterval: () => 1, clearInterval() {}
    };
    const host = { PlayerLife: () => 0, HudHidden: () => true };
    const effects = createSettingsEffects(root, () => host, () => ({ hideHudIcons: true }), () => 'died', view);
    effects.start();
    effects.tick();
    assert.equal(canvas.style.filter, 'grayscale(1)');
    assert.equal(controls.style.display, 'none');
    assert.equal(hud.style.visibility, 'hidden');
    const event = { preventDefault() { this.prevented = true; } };
    listeners.beforeunload(event);
    assert.equal(event.prevented, true);
});

test('Simple shadows stop the scenery casting and a later choice restores it', () => {
    const object = { castShadow: true, userData: {} };
    const group = { userData: { renderObjects: [object] }, children: [] };
    stageShadows(group, { actorShadowsOnly: true });
    assert.equal(object.castShadow, false);
    stageShadows(group, { actorShadowsOnly: false });
    assert.equal(object.castShadow, true);
});

test('resetting windows puts every dragged window back', () => {
    const view = { innerWidth: 800, innerHeight: 600 };
    const element = () => ({
        style: {}, listeners: {}, addEventListener(name, listener) { this.listeners[name] = listener; },
        getBoundingClientRect() { return { left: 100, top: 100, width: 100, height: 100 }; },
        setPointerCapture() {}, releasePointerCapture() {}
    });
    const windowElement = element();
    const handle = element();
    const dragged = makeDraggable(windowElement, handle, view);
    dragged.place(40, 30);
    assert.notEqual(windowElement.style.translate, '');
    resetDraggables();
    assert.equal(windowElement.style.translate, '');
    dragged.place(40, 30);
    handle.listeners.dblclick({ target: { closest: () => null } });
    assert.equal(windowElement.style.translate, '');
});
