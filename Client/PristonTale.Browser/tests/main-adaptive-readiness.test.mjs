import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createAdaptiveResolution } from '../wwwroot/adaptive-resolution.js';

const source = await readFile(new URL('../wwwroot/main.js', import.meta.url), 'utf8');
const readinessStart = source.indexOf('let worldLoadingEpoch = 0;');
const readinessEnd = source.indexOf('function showMessage(', readinessStart);
const adaptiveStart = source.indexOf('function updateAdaptiveResolution(');
const adaptiveEnd = source.indexOf('if (openingMusic && openingSound)', adaptiveStart);
const charactersStart = source.indexOf('            reportCharacters(state) {');
const charactersEnd = source.indexOf('            reportWorldLoading,', charactersStart);
assert.ok(readinessStart >= 0 && readinessEnd > readinessStart && adaptiveStart >= 0 && adaptiveEnd > adaptiveStart
    && charactersStart >= 0 && charactersEnd > charactersStart);
const pause = () => new Promise(resolve => setImmediate(resolve));

function fixture() {
    const controller = createAdaptiveResolution(), controls = [], labels = [], failures = [], gates = [], hud = [];
    const graphics = { workerRendering: true, effective: { renderScale: 1, renderWidth: 1920, renderHeight: 1080 },
        getCapabilities() { return { effective: this.effective }; },
        setDynamicResolutionScale(scale, shadowHz) { controls.push({ scale, shadowHz }); return Promise.resolve(); },
        waitForSubmittedFrame() { return new Promise((resolve, reject) => gates.push({ resolve, reject })); } };
    const loading = { hidden: false }, worldLoading = { hidden: true, setAttribute() {} };
    const progress = { value: 0, setAttribute() {} }, percent = {}, name = {}, status = {}, root = { dataset: {} };
    const document = { visibilityState: 'visible' }, settingsPanel = { open: false };
    const heroesPanel = { hidden: true, dataset: {} };
    const settings = { dynamicResolutionEnabled: true, renderScale: 1, maxFps: 0, appliedRevision: 1,
        setDynamicResolutionState(value) { labels.push(value); } };
    const context = { activeGraphics: graphics, graphics, loading, document, settingsPanel, settings,
        canvas: { width: 1920, height: 1080 }, adaptiveResolution: controller,
        worldLoading, worldLoadingProgress: progress, worldLoadingPercent: percent, worldLoadingName: name, worldLoadingStatus: status,
        root, text: { loadingMap: 'Mapa', mapProgress: 'Progresso', loadingWorld: 'Carregando' },
        profileWorldLoading() {}, worldHud: { show() { hud.push('show'); }, hide() { hud.push('hide'); } },
        heroes: { report(value) { heroesPanel.hidden = false; heroesPanel.dataset.screen = value.screen; } },
        part(role) { assert.equal(role, 'heroes'); return heroesPanel; } };
    const api = new Function(...Object.keys(context),
        'let adaptiveChangePending = false; let nextAdaptiveLabelTime = 0;\n'
        + source.slice(readinessStart, readinessEnd) + source.slice(adaptiveStart, adaptiveEnd)
        + 'const bridge = {' + source.slice(charactersStart, charactersEnd) + '};\n'
        + 'return { updateAdaptiveResolution, reportWorldLoading, fenceWorldReady, reportCharacters: bridge.reportCharacters,\n'
        + 'state: () => ({ ready: adaptiveWorldReady, loading: adaptiveWorldLoading, pending: !!pendingWorldReady }) };')(...Object.values(context));
    let time = 0, frames = 0;
    return { ...api, controller, controls, labels, failures, gates, hud, loading, worldLoading, progress, percent,
        settings, document, settingsPanel, graphics,
        async tick(nextTime = time, nextFrames = frames) {
            time = nextTime; frames = nextFrames;
            api.updateAdaptiveResolution(graphics, time, frames, error => failures.push(error)); await pause();
        },
        async run(seconds, fps) {
            for (let sample = 0; sample < seconds * 10; sample++) {
                time += 100; frames += fps / 10;
                api.updateAdaptiveResolution(graphics, time, frames, error => failures.push(error)); await pause();
            }
        },
        async releaseWorld(index = gates.length - 1) { gates[index].resolve(); await pause(); },
        get time() { return time; }, get frames() { return frames; } };
}

test('production adaptive readiness needs four completed frames AND the initial loader hidden before login adaptation', async () => {
    const f = fixture();
    f.loading.hidden = true; await f.tick(0, 3);
    assert.equal(f.controller.world, false); assert.equal(f.controller.scale, null);
    f.loading.hidden = false; await f.tick(100, 4);
    assert.equal(f.controller.world, false); assert.equal(f.controls.length, 0);
    f.loading.hidden = true; await f.tick(200, 4);
    assert.equal(f.controller.world, true); assert.equal(f.controller.scale, 1);
    assert.equal(f.state().ready, false, 'the login does not become an authenticated world');
    await f.run(4, 32);
    assert.ok(f.controller.scale < 1, 'real completed-frame pressure lowers the login budget after warmup');
    assert.equal(f.labels.at(-1).enabled, true);
    assert.equal(f.settings.renderScale, 1, 'transient adaptation never changes the saved cap');
    assert.deepEqual(f.failures, []);
});

test('actual creation and name reports do not reset a continuous courtyard budget or authorize the world HUD', async () => {
    const f = fixture(); f.loading.hidden = true; await f.tick(0, 4); await f.run(4, 32);
    const scale = f.controller.scale, readyAt = f.controller.readyAt, revision = f.controller.settingsRevision;
    for (const [screen, name] of [['roster', ''], ['tribe', ''], ['class', ''], ['face', 'N'], ['face', 'No'], ['face', 'Noxius']]) {
        f.reportCharacters({ screen, name }); await f.tick();
        assert.equal(f.controller.scale, scale); assert.equal(f.controller.readyAt, readyAt);
        assert.equal(f.controller.settingsRevision, revision); assert.equal(f.state().ready, false);
    }
    assert.equal(f.hud.includes('show'), false);
});

test('map loading and its actual pending GPU fence block adaptation and keep99percent until completion', async () => {
    const f = fixture(); f.loading.hidden = true; await f.tick(0, 4); await f.run(4, 32);
    f.reportWorldLoading('loading', 0, 'Ricarten'); await f.tick();
    assert.equal(f.state().loading, true); assert.equal(f.controller.world, false); assert.equal(f.controller.scale, null);
    f.reportWorldLoading('loading', 99, 'Ricarten'); f.reportWorldLoading('ready', 100, 'Ricarten');
    f.fenceWorldReady(f.graphics, error => f.failures.push(error)); await f.run(5, 15);
    assert.equal(f.state().pending, true); assert.equal(f.state().ready, false);
    assert.equal(f.progress.value, 99); assert.equal(f.worldLoading.hidden, false); assert.equal(f.controller.scale, null);
    await f.releaseWorld(); await f.tick();
    assert.equal(f.state().pending, false); assert.equal(f.state().loading, false); assert.equal(f.state().ready, true);
    assert.equal(f.progress.value, 100); assert.equal(f.worldLoading.hidden, true);
    assert.equal(f.controller.scale, 1); assert.equal(f.controller.readyAt, f.time + 2000);
    const noLoadingReport = fixture(); noLoadingReport.loading.hidden = true; await noLoadingReport.tick(0, 4);
    noLoadingReport.reportWorldLoading('ready', 100, 'Ricarten'); await noLoadingReport.tick();
    assert.equal(noLoadingReport.state().loading, false); assert.equal(noLoadingReport.state().pending, true);
    assert.equal(noLoadingReport.controller.world, false, 'the pending GPU fence guards readiness even without a loading callback');
});

test('world/courtyard transitions and saved revisions restore the cap and warm up while hidden/settings pauses freeze it', async () => {
    const f = fixture(); f.loading.hidden = true; await f.tick(0, 4); await f.run(4, 32);
    f.reportWorldLoading('ready', 100, 'Ricarten'); f.fenceWorldReady(f.graphics, assert.fail); await f.releaseWorld(); await f.tick();
    assert.equal(f.controller.settingsRevision, 3); assert.equal(f.controller.scale, 1);
    await f.run(4, 32); const worldScale = f.controller.scale;
    assert.ok(worldScale < 1);
    f.reportCharacters({ screen: 'roster' }); await f.tick();
    assert.equal(f.controller.settingsRevision, 2); assert.equal(f.controller.scale, 1);
    await f.run(4, 32); const courtyardScale = f.controller.scale;
    f.settingsPanel.open = true; await f.run(10, 1);
    assert.equal(f.controller.scale, courtyardScale);
    f.settingsPanel.open = false; f.document.visibilityState = 'hidden'; await f.run(10, 1);
    assert.equal(f.controller.scale, courtyardScale);
    f.document.visibilityState = 'visible'; await f.run(2, 1);
    assert.equal(f.controller.scale, courtyardScale, 'visible measurement must warm up again');
    f.settings.appliedRevision++; await f.tick();
    assert.equal(f.controller.settingsRevision, 4); assert.equal(f.controller.scale, 1);
    f.settings.dynamicResolutionEnabled = false; await f.tick();
    assert.equal(f.controller.scale, null); assert.equal(f.controller.shadowHz, 0);
});

test('cancelled map attempts cannot let an older ready completion reopen adaptation during the new load', async () => {
    const f = fixture(); f.loading.hidden = true; await f.tick(0, 4);
    f.reportWorldLoading('loading', 0, 'Ricarten'); f.reportWorldLoading('ready', 100, 'Ricarten');
    f.fenceWorldReady(f.graphics, assert.fail);
    f.reportWorldLoading('cancelled'); f.reportWorldLoading('loading', 0, 'Pillai');
    f.reportWorldLoading('loading', 99, 'Pillai'); f.reportWorldLoading('ready', 100, 'Pillai');
    f.fenceWorldReady(f.graphics, assert.fail);
    await f.releaseWorld(0); await f.tick();
    assert.equal(f.state().pending, true); assert.equal(f.controller.world, false); assert.equal(f.progress.value, 99);
    await f.releaseWorld(1); await f.tick();
    assert.equal(f.state().pending, false); assert.equal(f.controller.world, true); assert.equal(f.progress.value, 100);
});
