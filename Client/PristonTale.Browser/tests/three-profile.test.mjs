import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeProfiler, ThreePhase } from '../wwwroot/three-profile.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

function rendererStub() {
    return {
        coordinateSystem: THREE.WebGPUCoordinateSystem, backend: { isWebGPUBackend: true },
        info: { autoReset: true, render: { frameCalls: 0, drawCalls: 0, triangles: 0 },
            memory: { textures: 5, renderTargets: 2, programs: 3, uniformBuffers: 4, total: 1024 },
            reset() { this.render.frameCalls = this.render.drawCalls = this.render.triangles = 0; } },
        setPixelRatio() {}, setSize() {}, setScissorTest() {}, setViewport() {}, setScissor() {}, setClearColor() {}, clear() {},
        setRenderTarget(target) { this.target = target; }, getRenderTarget() { return this.target; },
        render(scene) {
            this.info.render.frameCalls++;
            this.info.render.drawCalls += 2;
            this.info.render.triangles += 10;
            if (scene.output) this.render({ isQuadMesh: true, name: 'Output Color Transform' });
            return 42;
        },
        dispose() {}
    };
}

test('profiling is opt-in and leaves the normal renderer unchanged', () => {
    const renderer = rendererStub();
    const original = renderer.render;
    assert.equal(createThreeProfiler(renderer), null);
    assert.equal(renderer.render, original);
    assert.equal(renderer.info.autoReset, true);
    assert.equal(createThreeProfiler({}), null);
});

test('profiling distinguishes phase CPU time, batches and nested output blits without double-counting time', () => {
    const renderer = rendererStub();
    const original = renderer.render;
    let time = 0;
    const profile = createThreeProfiler(renderer, true, () => time);
    assert.equal(renderer.info.autoReset, false);
    profile.beginFrame();
    let started = profile.start();
    assert.equal(renderer.render({ output: true }), 42);
    time += 3;
    profile.endPhase(ThreePhase.native, started, 4);
    started = profile.start();
    renderer.render({ output: true, isQuadMesh: true, name: 'Noxius post composition' });
    time += 5;
    profile.endPhase(ThreePhase.post, started);
    profile.endFrame();
    const result = profile.snapshot();
    assert.equal(result.samples, 1);
    assert.equal(result.phases.native.cpuMs.p95, 3);
    assert.equal(result.phases.post.cpuMs.p95, 5);
    assert.equal(result.renders.nativeBatches.mean, 4);
    assert.equal(result.renders.outputBlits.mean, 2);
    assert.equal(result.renders.quadCalls.mean, 3);
    assert.equal(result.renders.renderCalls.mean, 4);
    assert.equal(result.renders.drawCalls.mean, 8);
    assert.equal(result.renders.triangles.mean, 40);
    assert.deepEqual(result.memory, { textures: 5, renderTargets: 2, programs: 3, uniformBuffers: 4, bytes: 1024 });
    assert.match(result.measurement, /GPU duration is not measured/);
    assert.equal(profile.snapshot().samples, 0);
    profile.dispose();
    assert.equal(renderer.render, original);
    assert.equal(renderer.info.autoReset, true);
});

test('profile rings retain recent frames and snapshots reset only the reporting interval', () => {
    const renderer = rendererStub();
    let time = 0;
    const profile = createThreeProfiler(renderer, true, () => time);
    for (let index = 0; index < 700; index++) {
        profile.beginFrame();
        const started = profile.start();
        time += index;
        profile.endPhase(ThreePhase.scene, started);
        profile.endFrame();
    }
    const result = profile.snapshot(false);
    assert.equal(result.samples, 600);
    assert.equal(result.totalFrames, 700);
    assert.equal(result.phases.scene.cpuMs.p50, 399);
    assert.equal(result.phases.scene.cpuMs.p95, 670);
    assert.equal(result.phases.scene.cpuMs.max, 699);
    assert.equal(profile.snapshot().samples, 600);
    assert.equal(profile.snapshot().totalFrames, 700);
    profile.dispose();
});

test('real driver hooks classify main scene, portraits and native clip groups in one app frame', async () => {
    const renderer = rendererStub();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile: true });
    const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
    graphics.beginFrame(1280, 720);
    graphics.createScene(1);
    const frame = { view: identity, projection: identity, ambient: [1, 1, 1], sunColor: [0, 0, 0] };
    graphics.drawScene(1, frame);
    graphics.flush();
    graphics.drawPortrait(1, [], frame);
    graphics.flush();
    graphics.drawTriangles([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1, 0, 0, 1],
        [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1], [0, 1, 2], 0, identity, 'opaque');
    graphics.present();
    const result = graphics.getProfile();
    assert.equal(result.samples, 1);
    assert.equal(result.phases.scene.calls.mean, 1);
    assert.equal(result.phases.portrait.calls.mean, 1);
    assert.equal(result.phases.native.calls.mean, 1);
    assert.equal(result.renders.nativeBatches.mean, 1);
    assert.equal(result.phases.post.calls.mean, 0);
    await graphics.dispose();
});

function runFrames(profile, count) {
    for (let frame = 0; frame < count; frame++) {
        profile.beginFrame();
        profile.endFrame();
    }
}

function timestampRenderer(resolve) {
    const renderer = rendererStub();
    renderer.backend.trackTimestamp = true;
    renderer.hasFeature = name => name === 'timestamp-query';
    renderer.resolveTimestampsAsync = resolve;
    return renderer;
}

test('GPU timings are separately opt-in and unsupported adapters never call the resolver', async () => {
    let calls = 0;
    const renderer = timestampRenderer(() => { calls++; return Promise.resolve(2); });
    const profile = createThreeProfiler(renderer, true);
    runFrames(profile, 30);
    assert.equal(profile.snapshot(false).gpu, undefined);
    assert.equal(calls, 0);
    profile.dispose();
    for (const support of [false, 'throws']) {
        renderer.hasFeature = () => { if (support === 'throws') throw Error('uninitialized'); return support; };
        const unsupported = createThreeProfiler(renderer, true, undefined, true);
        runFrames(unsupported, 30);
        const result = unsupported.snapshot().gpu;
        assert.equal(result.supported, false);
        assert.equal(result.status, 'unsupported');
        assert.equal(result.renderMs, null);
        assert.equal(calls, 0);
        unsupported.dispose();
    }
});

test('GPU timing uses the pinned public renderer/backend resolver and preserves its last-frame sum', async () => {
    const renderer = rendererStub();
    renderer._initialized = true;
    renderer.backend = new THREE.WebGPUBackend({ trackTimestamp: true });
    renderer.backend.renderer = renderer;
    renderer.backend.hasFeature = name => name === 'timestamp-query';
    let calls = 0;
    renderer.backend.timestampQueryPool.render = { async resolveQueriesAsync() { calls++; return 12.75; } };
    renderer.hasFeature = THREE.WebGPURenderer.prototype.hasFeature;
    renderer.resolveTimestampsAsync = THREE.WebGPURenderer.prototype.resolveTimestampsAsync;
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile: true, gpuTiming: true });
    for (let frame = 0; frame < 10; frame++) {
        graphics.beginFrame(128, 96);
        assert.equal(graphics.present(), undefined);
    }
    await new Promise(resolve => setImmediate(resolve));
    const result = graphics.getProfile();
    assert.equal(calls, 1);
    assert.equal(renderer.info.render.timestamp, 12.75);
    assert.equal(result.samples, 10);
    assert.equal(result.gpu.samples, 1);
    assert.equal(result.gpu.renderMs.mean, 12.75);
    assert.equal(result.gpu.lastMs, 12.75);
    assert.equal(result.gpu.lastRequestedAfterAppFrame, 10);
    assert.equal(result.gpu.lastResolvedAtAppFrame, 10);
    assert.match(result.gpu.measurement, /last resolved backend frame/);
    assert.equal(result.phases.scene.cpuMs.mean, 0);
    await graphics.dispose();
});

test('GPU resolution is nonblocking, bounded to one in flight, and independently ring-buffered', async () => {
    let calls = 0, complete;
    const renderer = timestampRenderer(type => {
        assert.equal(type, 'render');
        calls++;
        return new Promise(resolve => { complete = resolve; });
    });
    const profile = createThreeProfiler(renderer, true, () => 100, true);
    runFrames(profile, 9);
    assert.equal(calls, 0);
    runFrames(profile, 21);
    assert.equal(calls, 1);
    assert.equal(profile.snapshot(false).gpu.pending, true);
    complete(7.5);
    await Promise.resolve();
    const result = profile.snapshot(false);
    assert.equal(result.samples, 30);
    assert.equal(result.gpu.samples, 1);
    assert.equal(result.gpu.renderMs.p95, 7.5);
    assert.equal(result.gpu.lastResolvedAtAppFrame, 30);
    runFrames(profile, 10);
    assert.equal(calls, 2);
    complete(4.5);
    await Promise.resolve();
    assert.equal(profile.snapshot(false).gpu.renderMs.mean, 6);
    profile.dispose();
});

test('GPU interval resets and disposal isolate already pending timestamp results', async () => {
    let complete;
    const profile = createThreeProfiler(timestampRenderer(() => new Promise(resolve => { complete = resolve; })), true, undefined, true);
    runFrames(profile, 10);
    assert.equal(profile.snapshot().gpu.pending, true);
    complete(2.5);
    await Promise.resolve();
    let gpu = profile.snapshot(false).gpu;
    assert.equal(gpu.samples, 0);
    assert.equal(gpu.totalSamples, 1);
    assert.equal(gpu.lastMs, null);
    runFrames(profile, 10);
    complete(5);
    await Promise.resolve();
    gpu = profile.snapshot(false).gpu;
    assert.equal(gpu.samples, 1);
    assert.equal(gpu.renderMs.mean, 5);
    runFrames(profile, 10);
    profile.dispose();
    complete(99);
    await Promise.resolve();
    gpu = profile.snapshot(false).gpu;
    assert.equal(gpu.status, 'disposed');
    assert.equal(gpu.samples, 1);
    assert.equal(gpu.lastMs, 5);
});

test('GPU failures are bounded and stop polling without console noise', async () => {
    let calls = 0;
    const profile = createThreeProfiler(timestampRenderer(() => { calls++; return Promise.reject(Error('x'.repeat(500))); }), true, undefined, true);
    runFrames(profile, 10);
    await Promise.resolve();
    runFrames(profile, 100);
    const gpu = profile.snapshot().gpu;
    assert.equal(calls, 1);
    assert.equal(gpu.status, 'failed');
    assert.equal(gpu.error.length, 160);
    assert.equal(gpu.pending, false);
    profile.dispose();
});

test('empty timestamp pools retry but invalid GPU durations never become CPU or GPU samples', async () => {
    let calls = 0;
    const profile = createThreeProfiler(timestampRenderer(() => ++calls === 1 ? Promise.resolve(undefined) : Promise.resolve(NaN)), true, undefined, true);
    runFrames(profile, 10);
    await Promise.resolve();
    assert.equal(profile.snapshot(false).gpu.status, 'collecting');
    runFrames(profile, 10);
    await Promise.resolve();
    runFrames(profile, 100);
    const result = profile.snapshot();
    assert.equal(calls, 2);
    assert.equal(result.gpu.status, 'failed');
    assert.equal(result.gpu.samples, 0);
    assert.equal(result.gpu.lastMs, null);
    assert.equal(result.samples, 120);
    profile.dispose();
});

test('sparse GPU timing writes only one whole frame in ten and pinned descriptors clear stale writes on other frames', async () => {
    const renderer = rendererStub();
    renderer._initialized = true;
    renderer.backend = new THREE.WebGPUBackend({ trackTimestamp: true });
    renderer.backend.renderer = renderer;
    renderer.backend.hasFeature = name => name === 'timestamp-query';
    let allocations = 0, resolves = 0;
    renderer.backend.timestampQueryPool.render = { querySet: {}, allocateQueriesForContext() { return allocations++ * 2; },
        async resolveQueriesAsync() { resolves++; return 3.5; } };
    renderer.hasFeature = THREE.WebGPURenderer.prototype.hasFeature;
    renderer.resolveTimestampsAsync = function (type) {
        assert.equal(this.backend.trackTimestamp, true, 'resolve starts before tracking is disabled');
        return THREE.WebGPURenderer.prototype.resolveTimestampsAsync.call(this, type);
    };
    const profile = createThreeProfiler(renderer, true, undefined, true);
    assert.equal(renderer.backend.trackTimestamp, false);
    const descriptor = { timestampWrites: { stale: true } };
    for (let frame = 1; frame <= 30; frame++) {
        profile.beginFrame();
        renderer.backend.initTimestampQuery('render', 'frame-' + frame, descriptor);
        assert.equal(descriptor.timestampWrites !== undefined, frame % 10 === 0);
        profile.endFrame();
        assert.equal(renderer.backend.trackTimestamp, false);
        await new Promise(resolve => setImmediate(resolve));
    }
    let gpu = profile.snapshot(false).gpu;
    assert.equal(gpu.mode, 'sampled');
    assert.equal(allocations, 3);
    assert.equal(resolves, 3);
    assert.equal(gpu.trackedAppFrames, 3);
    assert.equal(gpu.sampledAppFrames, 3);
    assert.equal(profile.setGpuTimingMode('continuous'), 'continuous');
    for (let frame = 0; frame < 20; frame++) {
        profile.beginFrame();
        assert.equal(renderer.backend.trackTimestamp, true);
        profile.endFrame();
        await new Promise(resolve => setImmediate(resolve));
    }
    gpu = profile.snapshot(false).gpu;
    assert.equal(gpu.trackedAppFrames, 20);
    assert.equal(gpu.sampledAppFrames, 2);
    assert.equal(resolves, 5);
    assert.equal(profile.setGpuTimingMode('off'), 'off');
    runFrames(profile, 30);
    gpu = profile.snapshot(false).gpu;
    assert.equal(gpu.mode, 'off');
    assert.equal(gpu.status, 'disabled');
    assert.equal(gpu.enabled, false);
    assert.equal(gpu.samples, 0);
    assert.equal(gpu.lastMs, null);
    assert.equal(gpu.trackedAppFrames, 0);
    assert.equal(resolves, 5);
    profile.dispose();
    assert.equal(renderer.backend.trackTimestamp, true, 'dispose restores the original backend option');
});

test('GPU mode changes drain pending work without publishing it into the new mode and reject mid-frame changes', async () => {
    let complete, calls = 0;
    const renderer = timestampRenderer(() => { calls++; return new Promise(resolve => { complete = resolve; }); });
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile: true, gpuTiming: true });
    for (let frame = 0; frame < 10; frame++) { graphics.beginFrame(128, 96); graphics.present(); }
    assert.equal(calls, 1);
    assert.equal(graphics.setGpuTimingMode('off'), 'off');
    for (let frame = 0; frame < 20; frame++) { graphics.beginFrame(128, 96); graphics.present(); }
    complete(40);
    await Promise.resolve();
    let gpu = graphics.getProfile().gpu;
    assert.equal(gpu.pending, false);
    assert.equal(gpu.status, 'disabled');
    assert.equal(gpu.samples, 0);
    assert.equal(gpu.lastMs, null);
    assert.equal(calls, 1);
    assert.equal(graphics.setGpuTimingMode('sampled'), 'sampled');
    graphics.beginFrame(128, 96);
    assert.throws(() => graphics.setGpuTimingMode('off'), /between application frames/);
    graphics.present();
    assert.throws(() => graphics.setGpuTimingMode('bogus'), /Unknown GPU timing mode/);
    await graphics.dispose();
    assert.equal(renderer.backend.trackTimestamp, true);
});
