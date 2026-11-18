import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createWorkerGraphics } from '../wwwroot/three-worker.js';
import { createWorkerMessageHandler } from '../wwwroot/three-worker-entry.js';

const source = await readFile(new URL('../wwwroot/main.js', import.meta.url), 'utf8');
const start = source.indexOf('        let drawn = 0;');
const end = source.indexOf('\n        requestFrame();\n    }\n} catch', start) + '\n        requestFrame();'.length;
assert.ok(start >= 0 && end > start);
const loopSource = source.slice(start, end);
const readyStart = source.indexOf('let worldLoadingEpoch = 0;');
const readyEnd = source.indexOf('function showMessage(', readyStart);
assert.ok(readyStart >= 0 && readyEnd > readyStart);
const pause = () => new Promise(resolve => setImmediate(resolve));

function loopFixture(worker = true, failFrame = false, custom = {}) {
    const queue = [], timers = [], events = [], errors = [];
    let free = 3, pendingSlot, calls = 0;
    const graphics = custom.graphics ?? { stats: { frames: 0 }, workerRendering: worker, workerStats: { submittedFrames: 0, completedFrames: 0 }, get canAdvanceFrame() { return free > 0; },
        waitForFrameSlot() { events.push('wait'); return new Promise((resolve, reject) => { pendingSlot = { resolve, reject }; }); },
        getProfile() { return {}; }, requestProfile() { return Promise.resolve({}); }, getCapabilities() { return {}; } };
    const loading = { hidden: false }, root = { dataset: {} };
    const context = { graphics, host: { Frame() { events.push('host'); calls++; if (failFrame) throw Error('game failed');
        if (custom.draw) custom.draw(calls); else if (worker) free--; else graphics.stats.frames++; },
        EnableFrameProfiling() {}, FrameProfile() { return '{}'; } },
        root, loading, settings: { maxFps: 0, setRendererCapabilities() {} }, worldHud: { frame() { events.push('hud'); } },
        profileFrames: false, publicAssetCache: null, enableTextureArrayBenchmark() {},
        console: { info() {}, error(...args) { errors.push(args); } }, performance: { now: () => 100 },
        requestAnimationFrame(callback) { queue.push(callback); }, setTimeout(callback) { timers.push(callback); },
        reportWorldLoading(phase) { events.push(phase); }, fenceWorldReady() { events.push('fence'); }, updateAdaptiveResolution() {}, showLoading() { events.push('error-ui'); },
        text: { stopped: error => error.message }, remainingDelayMs: 1000, loginDelayMs: 0, phases: { runtime: 0, files: 0, start: 0 },
        revealLogin() {}, prefetchCharacters() {}, fetchRemaining() {} };
    new Function(...Object.keys(context), 'let loginPainted = false;\n' + loopSource)(...Object.values(context));
    return { graphics, events, errors, loading, root, timers, queue, get calls() { return calls; },
        tick(time = 0) { assert.ok(queue.length, 'one scheduled frame'); queue.shift()(time); },
        ack(count = 1) { free += count; graphics.workerStats.submittedFrames += count; graphics.workerStats.completedFrames += count;
            if (pendingSlot) { const pending = pendingSlot; pendingSlot = null; pending.resolve(); } },
        failWait() { pendingSlot.reject(Error('worker failed')); pendingSlot = null; } };
}

test('production main loop applies backpressure before host.Frame and schedules one continuation per ACK', async () => {
    const f = loopFixture();
    f.tick(); f.tick(16); f.tick(32);
    assert.equal(f.calls, 3);
    f.tick(48);
    assert.equal(f.calls, 3);
    assert.equal(f.events.filter(event => event === 'wait').length, 1);
    assert.equal(f.queue.length, 0);
    f.ack(2); await pause();
    assert.equal(f.queue.length, 1);
    f.tick(64);
    assert.equal(f.calls, 4);
    assert.equal(f.queue.length, 1);
    assert.deepEqual(f.events.slice(0, 3), ['host', 'fence', 'hud']);
});

test('startup loading waits for four actual worker submissions, including ACK count jumps', async () => {
    const f = loopFixture();
    f.tick(); f.tick(16); f.tick(32); f.tick(48);
    assert.equal(f.loading.hidden, false);
    f.ack(3); await pause(); f.tick(64);
    assert.equal(f.calls, 4);
    assert.equal(f.loading.hidden, false);
    f.ack(); f.tick(80);
    assert.equal(f.loading.hidden, true);
    assert.equal(f.root.dataset.state, 'running');
    assert.equal(f.timers.length, 2);
    f.tick(96);
    assert.equal(f.timers.length, 2, 'opening timers are installed once');
});

test('CPU-submitted worker frames alone cannot end initial loading before GPU-completion ACKs', () => {
    const f = loopFixture();
    f.graphics.workerStats.submittedFrames = 1000;
    f.tick(); f.tick(); f.tick(); f.tick();
    assert.equal(f.loading.hidden, false);
    assert.equal(f.graphics.workerStats.completedFrames, 0);
    assert.equal(f.calls, 3);
    assert.equal(f.queue.length, 0);
});

test('real proxy plus production scheduler finishes the native frame opened by host.Start initialization', async () => {
    const worker = { queue: Promise.resolve(), terminate() {}, postMessage(message, transfer = []) {
        const copy = structuredClone(message, { transfer });
        this.queue = this.queue.then(() => handle(copy));
    } };
    const gpu = { errors: new Set(), stats: { frames: 0 }, getCapabilities: () => ({ backend: 'recorder' }), getProfile: () => null,
        beginFrame() {}, clear() {}, present() { this.stats.frames++; }, dispose() {} };
    const handle = createWorkerMessageHandler((message, transfer = []) => {
        const copy = structuredClone(message, { transfer });
        queueMicrotask(() => worker.onmessage({ data: copy }));
    }, async () => gpu, undefined, () => ({ afterSubmit: () => Promise.resolve(1), dispose() {} }));
    const proxy = await createWorkerGraphics({ dataset: {}, transferControlToOffscreen: () => new ArrayBuffer(0) }, { workerFactory: () => worker });
    proxy.beginFrame(128, 96);
    assert.equal(proxy.canBeginFrame, false);
    assert.equal(proxy.canAdvanceFrame, true);
    const f = loopFixture(true, false, { graphics: proxy, draw() { proxy.clear(0, 0, 0, 1); proxy.present(); } });
    f.tick();
    assert.equal(f.calls, 1, 'the first host tick must run instead of waiting on its own unsubmitted frame');
    await proxy.firstFrame;
    assert.equal(gpu.stats.frames, 1);
    assert.equal(proxy.workerStats.completedFrames, 1);
    assert.equal(proxy.canBeginFrame, true);
    await proxy.dispose();
});

test('direct rendering retains four-frame startup and both synchronous and async failures stop the loop', async () => {
    const direct = loopFixture(false);
    for (let frame = 0; frame < 4; frame++) direct.tick(frame * 16);
    assert.equal(direct.loading.hidden, true);
    assert.equal(direct.events.includes('fence'), false);
    const sync = loopFixture(false, true); sync.tick();
    assert.equal(sync.root.dataset.state, 'failed');
    assert.equal(sync.queue.length, 0);
    const async = loopFixture();
    async.tick(); async.tick(); async.tick(); async.tick();
    async.failWait(); await pause();
    assert.equal(async.root.dataset.state, 'failed');
    assert.equal(async.queue.length, 0);
    assert.ok(async.events.includes('cancelled'));
});

test('main-renderer startup counts real Present operations rather than host ticks that skipped Draw', () => {
    const f = loopFixture(false, false, { draw() {} });
    for (let index = 0; index < 8; index++) f.tick(index * 16);
    assert.equal(f.loading.hidden, false);
    f.graphics.stats.frames = 4;
    f.tick(144);
    assert.equal(f.loading.hidden, true);
});

function loadingFixture(worker = true) {
    const waits = [], profiles = [], events = [];
    const graphics = { workerRendering: worker, waitForSubmittedFrame() {
        return new Promise((resolve, reject) => waits.push({ resolve, reject }));
    } };
    const worldLoading = { hidden: true, setAttribute() {} }, root = { dataset: {} };
    const value = { value: 0, setAttribute() {} }, percent = { textContent: '' };
    const context = { activeGraphics: graphics, worldLoading, root, worldLoadingProgress: value, worldLoadingPercent: percent,
        worldLoadingName: { textContent: '' }, worldLoadingStatus: { textContent: '' },
        worldHud: { show() { events.push('show'); }, hide() { events.push('hide'); } },
        profileWorldLoading(...args) { profiles.push(args); }, text: { loadingMap: 'Preparing map', mapProgress: 'Map progress', loadingWorld: 'Loading' } };
    const create = new Function(...Object.keys(context), source.slice(readyStart, readyEnd) + ';return {reportWorldLoading,fenceWorldReady};');
    return { ...create(...Object.values(context)), graphics, waits, profiles, events, worldLoading, value, percent };
}

test('map readiness keeps actual99% until the newly queued world frame is acknowledged', async () => {
    const f = loadingFixture();
    f.reportWorldLoading('loading', 0, 'Ricarten');
    f.reportWorldLoading('loading', 99, 'Ricarten');
    f.reportWorldLoading('ready', 100, 'Ricarten');
    assert.equal(f.value.value, 99);
    assert.equal(f.worldLoading.hidden, false);
    assert.equal(f.profiles.some(value => value[0] === 'ready'), false);
    f.fenceWorldReady(f.graphics, assert.fail);
    f.fenceWorldReady(f.graphics, assert.fail);
    assert.equal(f.waits.length, 1);
    f.waits[0].resolve(); await pause();
    assert.equal(f.value.value, 100);
    assert.equal(f.worldLoading.hidden, true);
    assert.equal(f.events.at(-1), 'show');
    assert.equal(f.profiles.at(-1)[0], 'ready');
});

test('cancel/new world attempt invalidates stale rendering ACKs without hiding the new loading panel', async () => {
    const f = loadingFixture();
    f.reportWorldLoading('loading', 0, 'Ricarten'); f.reportWorldLoading('ready', 100, 'Ricarten');
    f.fenceWorldReady(f.graphics, assert.fail);
    f.reportWorldLoading('cancelled');
    f.reportWorldLoading('loading', 0, 'Pillai'); f.reportWorldLoading('loading', 99, 'Pillai');
    f.reportWorldLoading('ready', 100, 'Pillai'); f.fenceWorldReady(f.graphics, assert.fail);
    f.waits[0].resolve(); await pause();
    assert.equal(f.worldLoading.hidden, false);
    assert.equal(f.value.value, 99);
    assert.equal(f.events.includes('show'), false);
    f.waits[1].resolve(); await pause();
    assert.equal(f.worldLoading.hidden, true);
    assert.equal(f.profiles.filter(item => item[0] === 'ready').length, 1);
    assert.equal(f.profiles.at(-1)[2], 'Pillai');
    const direct = loadingFixture(false);
    direct.reportWorldLoading('ready', 100, 'Ricarten');
    assert.equal(direct.worldLoading.hidden, true);
    assert.equal(direct.events.at(-1), 'show');
});
