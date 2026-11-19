import test from 'node:test';
import assert from 'node:assert/strict';
import { dynamicResolutionLabel, normalizeRendererSettings, qualityLabel, setRendererOptions, tabs } from '../wwwroot/settings-options.js';
import { createWorldHud } from '../wwwroot/world-hud.js';

const capabilities = { antiAliasing: ['Off', 'Fxaa', 'Smaa', 'Msaa'] };

test('the visible quality label follows the applied UI preset rather than capability-normalized Custom', () => {
    assert.equal(qualityLabel('High', 'Custom'), 'Alto');
    assert.equal(qualityLabel('VeryLow', 'Custom'), 'Muito baixo');
    assert.equal(qualityLabel('Medium', 'Custom'), 'Médio');
    assert.equal(qualityLabel('Auto', 'Low'), 'Automático · Baixo');
    assert.equal(qualityLabel('Auto', 'Custom'), 'Automático');
    assert.equal(qualityLabel('Custom', 'High'), 'Personalizado');
    assert.equal(qualityLabel('Ultra', 'Ultra'), 'Alto');
    assert.equal(qualityLabel('Auto', 'Medium', 'en'), 'Automatic · Medium');
});

test('dynamic resolution suffix reports actual3D dimensions and scale without renaming the preset', () => {
    const state = { enabled: true, scale: .8, width: 1024, height: 576 };
    assert.equal(qualityLabel('High', 'Custom') + dynamicResolutionLabel(state), 'Alto · 3D 1024×576 (80%, dinâmica)');
    assert.equal(dynamicResolutionLabel(state, 1, 'en'), ' · 3D 1024×576 (80%, dynamic)');
    assert.equal(dynamicResolutionLabel(state, .8), '');
    assert.equal(dynamicResolutionLabel({ ...state, scale: .80000001 }, .8), '');
    assert.equal(dynamicResolutionLabel(state, .75), '');
    for (const invalid of [null, { ...state, enabled: false }, { ...state, scale: NaN }, { ...state, scale: 0 },
        { ...state, width: 0 }, { ...state, height: 1.5 }]) assert.equal(dynamicResolutionLabel(invalid), '');
});

test('the existing dynamic resolution switch is active, persisted and independent from preset identity', () => {
    const rows = Object.values(tabs).flat(2).flatMap(card => card.rows ?? []);
    const row = rows.find(option => option.label?.['pt-BR'] === 'Resolução dinâmica');
    assert.equal(row.field, 'dynamicResolution');
    assert.equal(row.toggle, true);
    assert.equal(row.unavailable, false);
    assert.equal(row.fallback, true);
    assert.notEqual(row.preset, true);
});

test('the adaptive label exposes only an actual shadow cadence cap, including cadence-only adaptation', () => {
    const state = { enabled: true, scale: 1, width: 1280, height: 720, shadowUpdateRate: 30 };
    assert.equal(dynamicResolutionLabel(state), ' · sombras 30 Hz');
    assert.equal(dynamicResolutionLabel(state, 1, 'en'), ' · shadows 30 Hz');
    assert.equal(dynamicResolutionLabel({ ...state, scale: .8, width: 1024, height: 576 }), ' · 3D 1024×576 (80%, dinâmica) · sombras 30 Hz');
    assert.equal(dynamicResolutionLabel(state, 1, 'pt-BR', 30), '');
    assert.equal(dynamicResolutionLabel(state, 1, 'pt-BR', 0), ' · sombras 30 Hz');
    for (const shadowUpdateRate of [undefined, false, 0, 60, NaN]) assert.equal(dynamicResolutionLabel({ ...state, shadowUpdateRate }), '');
    assert.equal(dynamicResolutionLabel({ ...state, enabled: false }), '');
});

test('the world quality badge shares HUD visibility and updates directly without host polling', () => {
    const previousDocument = globalThis.document;
    const node = () => ({ dataset: {}, style: {}, children: [], textContent: '', attributes: {},
        setAttribute(key, value) { this.attributes[key] = value; },
        append(child) { this.children.push(child); child.parentNode = this; }, querySelectorAll() { return []; } });
    globalThis.document = { createElement: node };
    try {
        const hud = node(), realm = node(), fps = node(), ping = node();
        const parts = { 'world-hud': hud, 'world-realm': realm, 'world-fps': fps, 'world-ping': ping };
        const root = { querySelector(selector) { return parts[selector.match(/data-role="([^"]+)"/)?.[1]] ?? null; },
            querySelectorAll() { return []; }, addEventListener() {}, append() {} };
        let label = 'Alto';
        const api = createWorldHud(root, () => null, () => 'pt-BR', () => label);
        const badge = hud.children[0];
        assert.equal(badge.parentNode, hud);
        assert.equal(badge.dataset.role, 'world-quality');
        assert.equal(badge.style.pointerEvents, 'none');
        api.show();
        assert.equal(hud.dataset.state, 'active');
        assert.equal(badge.textContent, 'Qualidade: Alto');
        label = 'Personalizado';
        api.updateQuality();
        assert.equal(badge.textContent, 'Qualidade: Personalizado');
        api.hide();
        assert.equal(hud.dataset.state, 'inactive');
        label = 'Baixo';
        api.updateQuality();
        assert.equal(hud.dataset.state, 'inactive');
        api.show();
        assert.equal(badge.textContent, 'Qualidade: Baixo');
    } finally {
        globalThis.document = previousDocument;
    }
});

test('renderer settings retain supported AA and unrelated preferences', () => {
    const settings = { antiAliasing: 'Fxaa', quality: 'High', renderScale: 0.9, musicEnabled: true };
    assert.equal(normalizeRendererSettings(settings, capabilities), settings);
    assert.equal(normalizeRendererSettings(settings, undefined), settings);
});

test('unsupported saved or preset AA becomes an available mode in the applied settings', () => {
    const settings = { antiAliasing: 'Taa', quality: 'High', renderScale: 0.9, musicEnabled: true };
    assert.deepEqual(normalizeRendererSettings(settings, capabilities), { ...settings, antiAliasing: 'Smaa' });
    assert.equal(normalizeRendererSettings(settings, { antiAliasing: ['Off'] }).antiAliasing, 'Off');
});

test('the actual gear row exposes only renderer-supported AA choices', () => {
    setRendererOptions(capabilities);
    const rows = Object.values(tabs).flat(2).flatMap(card => card.rows ?? []);
    const row = rows.find(option => option.field === 'antiAliasing');
    assert.ok(row);
    assert.equal(row.options.find(option => option[0] === 'Taa')[2], true);
    assert.equal(row.options.find(option => option[0] === 'Msaa')[2], false);
    assert.equal(row.options.find(option => option[0] === 'Smaa')[2], false);
});

test('unsupported advanced controls keep saved preferences while disabling inert edits', () => {
    const renderer = { ...capabilities, unsupported: { autoExposure: 'pending', volumetricLight: 'pending',
        contactShadows: 'pending', colorGradingLut: 'pending', proceduralSky: 'pending', shadowUpdateRate: 'pending' } };
    const settings = { antiAliasing: 'Fxaa', autoExposure: true, contactShadows: true, renderScale: 0.9 };
    assert.equal(normalizeRendererSettings(settings, renderer), settings);
    setRendererOptions(renderer);
    const rows = Object.values(tabs).flat(2).flatMap(card => card.rows ?? []);
    for (const field of ['autoExposure', 'volumetricLight', 'contactShadows', 'colorGrading', 'realisticSky', 'cloudQuality', 'shadowUpdateRate']) {
        assert.equal(rows.find(row => row.field === field)?.unavailable, true, field);
    }
    assert.equal(rows.find(row => row.field === 'ambientOcclusion').unavailable, false);
});
