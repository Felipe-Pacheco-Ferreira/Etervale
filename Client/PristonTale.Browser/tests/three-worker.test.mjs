import test from 'node:test';
import assert from 'node:assert/strict';
import { createWorkerGraphics, createWorkerPackedImports } from '../wwwroot/three-worker.js';
import { WorkerOp, WorkerPacketReader, WorkerPacketWriter } from '../wwwroot/three-worker-protocol.js';
import { createWorkerMessageHandler, executeWorkerPacket } from '../wwwroot/three-worker-entry.js';

const pause = () => new Promise(resolve => setImmediate(resolve));
const empty = new Uint8Array(0);
const immediateCompletion = () => ({ afterSubmit: () => Promise.resolve(performance.timeOrigin + performance.now()), dispose() {} });
const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
function clone(value) {
    if (ArrayBuffer.isView(value)) return Array.from(value);
    if (Array.isArray(value)) return value.map(clone);
    if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, clone(item)]));
    return value;
}

function graphicsRecorder() {
    const graphics = { events: [], errors: new Set(), stats: { frames: 0 }, adapterInfo: { vendor: 'test', description: 'CPU recorder' },
        textureArraysRequested: true, textureArrayLayers: 128, textureArraysEnabled: false,
        getCapabilities() { return { backend: 'Worker recorder', internalPost: this.internalPostRequested === true,
            fusedSharpen: this.fusedSharpenRequested === true,
            effective: { quality: this.quality ?? 'Low' } }; },
        getProfile() { return { samples: this.stats.frames, renderBundles: { requested: true }, gpu: { mode: this.gpuMode ?? 'sampled' },
            zeroPointLights: { requested: true, enabled: this.zeroPointLightsEnabled ?? true } }; } };
    for (const method of ['beginFrame', 'present', 'flush', 'clear', 'setRenderTarget', 'createTexture', 'updateTexture', 'destroyTexture',
        'createScene', 'configureScene', 'configureSceneTexture', 'createMesh', 'removeMesh', 'createStage', 'removeStage',
        'setSkyLayers', 'setSkyParameters', 'createActor', 'removeActor', 'updateActor', 'updateMeshPose', 'drawScene', 'drawPortrait',
        'composeScene', 'disposeScene', 'setTextureArraysEnabled', 'setRenderBundlesEnabled', 'setStaticArrayObserversEnabled', 'setGpuTimingMode',
        'setZeroPointLightsEnabled', 'setInternalPostEnabled', 'setDynamicResolutionScale', 'dispose']) {
        graphics[method] = function (...args) {
            this.events.push([method, ...args.map(clone)]);
            if (method === 'present') this.stats.frames++;
            if (method === 'configureScene') this.quality = args[1].quality;
            if (method === 'setTextureArraysEnabled') this.textureArraysEnabled = args[0];
            if (method === 'setGpuTimingMode') this.gpuMode = args[0];
            if (method === 'setZeroPointLightsEnabled') this.zeroPointLightsEnabled = args[0];
            if (method === 'setDynamicResolutionScale') { this.dynamicScale = args[0]; this.dynamicShadowHz = args[1]; }
        };
    }
    graphics.drawNativeMemory = function (...args) {
        const [buffer, pos, uv, color, vertices, index, count, texture, matrix] = args;
        this.events.push(['native', { positions: Array.from(new Float32Array(buffer, pos, vertices * 3)),
            uv: Array.from(new Float32Array(buffer, uv, vertices * 2)), colors: Array.from(new Float32Array(buffer, color, vertices * 4)),
            indices: Array.from(new Uint32Array(buffer, index, count)), matrix: Array.from(new Float32Array(buffer, matrix, 16)),
            vertexCount: vertices, indexCount: count, textureId: texture, scalars: args.slice(9), packetBytes: buffer.byteLength }]);
    };
    graphics.drawRainMemory = function (...args) {
        const [buffer, pos, shape, vertices, index, count, texture, matrix, vertexParams, pixelParams] = args;
        this.events.push(['rain', { positions: Array.from(new Float32Array(buffer, pos, vertices * 4)),
            shapes: Array.from(new Float32Array(buffer, shape, vertices * 4)), indices: Array.from(new Uint32Array(buffer, index, count)),
            matrix: Array.from(new Float32Array(buffer, matrix, 16)), vs: Array.from(new Float32Array(buffer, vertexParams, 24)),
            ps: Array.from(new Float32Array(buffer, pixelParams, 12)), textureId: texture, scalars: args.slice(10) }]);
    };
    graphics.createRainGeometryMemory = function (id, buffer, positions, shapes, count, indices, indexCount) {
        this.events.push(['rainCreate', id, Array.from(new Float32Array(buffer, positions, count * 4)),
            Array.from(new Float32Array(buffer, shapes, count * 4)), Array.from(new Uint32Array(buffer, indices, indexCount))]);
    };
    graphics.destroyRainGeometry = function (id) { this.events.push(['rainDestroy', id]); };
    graphics.drawRainGeometryMemory = function (id, start, count, buffer, texture, matrix, vertex, pixel) {
        this.events.push(['rainStatic', id, start, count, texture, Array.from(new Float32Array(buffer, matrix, 16)),
            Array.from(new Float32Array(buffer, vertex, 24)), Array.from(new Float32Array(buffer, pixel, 12))]);
    };
    return graphics;
}

class RecorderWorker {
    constructor(graphics, captureCanvas, completionFactory = immediateCompletion) {
        this.graphics = graphics;
        this.messages = [];
        this.holdFrames = false;
        this.gates = [];
        this.queue = Promise.resolve();
        this.handle = createWorkerMessageHandler((message, transfer = []) => {
            const copy = structuredClone(message, { transfer });
            queueMicrotask(() => this.onmessage?.({ data: copy }));
        }, async (canvas, options) => {
            this.initOptions = options; graphics.canvas = canvas;
            graphics.internalPostRequested = options.internalPost === true || (options.internalPost === 'auto' && graphics.webGPUBackend !== false);
            graphics.fusedSharpenRequested = options.fusedSharpen === true || (options.fusedSharpen === 'auto' && graphics.webGPUBackend !== false);
            return graphics;
        }, captureCanvas, completionFactory);
    }

    postMessage(message, transfer = []) {
        const copy = structuredClone(message, { transfer });
        this.messages.push({ type: copy.type, length: copy.length, bufferBytes: copy.buffer?.byteLength,
            hasQueuedAt: Object.hasOwn(copy, 'queuedAtMs'), queuedAtMs: copy.queuedAtMs });
        this.queue = this.queue.then(async () => {
            if (copy.type === 'frame' && this.holdFrames) await new Promise(resolve => this.gates.push(resolve));
            await this.handle(copy);
        }).catch(error => this.onmessage?.({ data: { type: 'fatal', error: error.message } }));
    }

    terminate() { this.terminated = true; }
    async release() { while (!this.gates.length) await pause(); this.gates.shift()(); await pause(); }
}

async function setup(options = {}) {
    const graphics = graphicsRecorder();
    options.configureGraphics?.(graphics);
    const worker = new RecorderWorker(graphics, options.captureCanvas, options.completionFactory);
    const canvas = { dataset: {}, transferControlToOffscreen() { return new ArrayBuffer(0); } };
    const proxy = await createWorkerGraphics(canvas, { profile: true, frameSlots: 3, workerFactory: () => worker, ...options });
    return { proxy, worker, graphics, imports: createWorkerPackedImports(proxy) };
}

function triangleMemory(size = 4 * 1024 * 1024) {
    const heap = new ArrayBuffer(size);
    new Float32Array(heap, 1024, 9).set([1, 2, 3, 4, 5, 6, 7, 8, 9]);
    new Float32Array(heap, 2048, 6).set([.1, .2, .3, .4, .5, .6]);
    new Float32Array(heap, 3072, 12).fill(.5);
    new Uint32Array(heap, 4096, 3).set([2, 1, 0]);
    new Float32Array(heap, 5120, 16).set(identity);
    new Int32Array(heap, 6144, 4).set([0, 0, 128, 96]);
    return heap;
}

function native(proxy, heap) {
    proxy.drawNativeMemory(heap, 1024, 2048, 3072, 3, 4096, 3, -8, 5120, 1,
        true, false, 0, 6144, 99, false, 0, 0, 5, 0, 3, 0);
}

test('production proxy initializes an explicit worker and keeps capabilities as cached acknowledged snapshots', async () => {
    const { proxy, worker } = await setup({ gpuTiming: true, forceWebGL: true, cutoutArrays: true, stageSkinCulling: true, zeroPointLights: true });
    assert.equal(proxy.workerRendering, true);
    assert.equal(proxy.canBeginFrame, true);
    assert.equal(proxy.getCapabilities().backend, 'Worker recorder');
    assert.equal(worker.initOptions.gpuTiming, true);
    assert.equal(worker.initOptions.forceWebGL, true);
    assert.equal(worker.initOptions.cutoutArrays, true);
    assert.equal(worker.initOptions.stageSkinCulling, true);
    assert.equal(worker.initOptions.zeroPointLights, true);
    assert.equal(proxy.workerStats.submittedFrames, 0);
    await proxy.dispose();
    assert.equal(worker.terminated, true);
});

test('borrowed packed scene data is copied before return and decoded by the actual production packed bridge', async () => {
    const { proxy, imports, graphics } = await setup();
    imports.createScene(1);
    const vertices = new Float32Array(51);
    vertices.set([3, 4, 5]); vertices[17] = 9; vertices[34] = 12;
    const descriptor = JSON.stringify({ vertexStride: 17, vertexCount: 3, boneCount: 0, material: { textureId: -1 } });
    let copies = 0;
    const borrowed = { byteLength: vertices.byteLength, copyTo(target) { copies++; target.set(new Uint8Array(vertices.buffer)); } };
    imports.createMesh(1, 5, descriptor, borrowed, new Int32Array([0, 1, 2]));
    vertices.fill(999);
    imports.createStage(1, 2, [5]);
    await proxy.flushResources();
    const mesh = graphics.events.find(event => event[0] === 'createMesh')[3];
    assert.equal(copies, 1);
    assert.deepEqual(mesh.positions, [3, 4, 5, 9, 0, 0, 12, 0, 0]);
    assert.deepEqual(graphics.events.filter(event => ['createScene', 'createMesh', 'createStage'].includes(event[0])).map(event => event[0]),
        ['createScene', 'createMesh', 'createStage']);
    await proxy.dispose();
});

test('native commands transfer only used ranges without detaching or cloning the WASM-sized heap', async () => {
    const { proxy, worker, graphics } = await setup();
    const heap = triangleMemory();
    proxy.beginFrame(128, 96); native(proxy, heap); proxy.present();
    new Float32Array(heap, 1024, 9).fill(999);
    await proxy.firstFrame;
    assert.equal(heap.byteLength, 4 * 1024 * 1024);
    const frame = worker.messages.find(message => message.type === 'frame');
    assert.ok(frame.length < 1024);
    assert.ok(frame.bufferBytes < heap.byteLength);
    const draw = graphics.events.find(event => event[0] === 'native')[1];
    assert.deepEqual(draw.positions, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
    assert.deepEqual(draw.indices, [2, 1, 0]);
    assert.deepEqual(draw.matrix, identity);
    assert.equal(draw.textureId, -8);
    assert.deepEqual(draw.scalars.slice(0, 4), [1, true, false, 0]);
    assert.equal(draw.scalars[5], 99);
    assert.equal(draw.scalars[11], 3);
    assert.equal(proxy.workerStats.submittedFrames, 1);
    await proxy.dispose();
});

test('three transferable slots bound in-flight frames and first-draw acknowledgment waits for actual worker submission', async () => {
    const { proxy, worker, graphics } = await setup();
    worker.holdFrames = true;
    let first = false;
    proxy.firstFrame.then(() => { first = true; });
    for (let frame = 0; frame < 3; frame++) { proxy.beginFrame(128, 96); proxy.present(); }
    assert.equal(proxy.canBeginFrame, false);
    assert.equal(proxy.workerStats.inFlightFrames, 3);
    assert.equal(first, false);
    assert.equal(graphics.stats.frames, 0);
    assert.throws(() => proxy.beginFrame(128, 96), /wait before calling host.Frame/);
    let available = false;
    const wait = proxy.waitForFrameSlot().then(() => { available = true; });
    await pause(); assert.equal(available, false);
    await worker.release(); await wait;
    assert.equal(first, true);
    assert.equal(proxy.canBeginFrame, true);
    assert.equal(proxy.workerStats.submittedFrames, 1);
    assert.equal(proxy.workerStats.inFlightFrames, 2);
    await worker.release(); await worker.release();
    await proxy.dispose();
});

test('resource replacements, target routing, draw, compose, HUD and destruction preserve strict method order', async () => {
    const { proxy, imports, graphics } = await setup();
    imports.createScene(1);
    proxy.createTexture(99, 128, 96, null, true, 0, 0);
    proxy.beginFrame(128, 96);
    proxy.setRenderTarget(99);
    proxy.clear(0, 0, 0, 1);
    const header = new Float32Array(68); header.set(identity); header.set(identity, 16);
    imports.drawFramePacked(1, header, empty, empty, empty, empty, empty);
    imports.composeScene(1);
    native(proxy, triangleMemory());
    proxy.destroyTexture(99);
    proxy.createTexture(99, 192, 144, null, true, 0, 0);
    proxy.present();
    await proxy.firstFrame;
    const names = graphics.events.map(event => event[0]);
    for (const [before, after] of [['setRenderTarget', 'drawScene'], ['drawScene', 'composeScene'], ['composeScene', 'native'],
        ['native', 'destroyTexture'], ['destroyTexture', 'present']]) assert.ok(names.indexOf(before) < names.indexOf(after));
    assert.equal(graphics.events.filter(event => event[0] === 'createTexture').at(-1)[2], 192);
    assert.equal(graphics.stats.frames, 1);
    await proxy.dispose();
});

test('Single texture bit patterns and all four rain vertex channels survive the worker protocol', async () => {
    const { proxy, graphics } = await setup();
    const single = new Uint32Array([0x80000000, 0x7f800000, 0xc2000000, 0x3f000001]);
    proxy.createTexture(8, 2, 2, new Uint8Array(single.buffer), false, 0, 13);
    const heap = new ArrayBuffer(2048);
    new Float32Array(heap, 64, 4).set([1, 2, 3, .25]);
    new Float32Array(heap, 128, 4).set([-1, 1, 2, .75]);
    new Uint32Array(heap, 192, 3).set([0, 0, 0]);
    new Float32Array(heap, 256, 16).set(identity);
    new Float32Array(heap, 384, 24).fill(4.5);
    new Float32Array(heap, 512, 12).fill(7.5);
    proxy.beginFrame(128, 96);
    proxy.drawRainMemory(heap, 64, 128, 1, 192, 3, 8, 256, 384, 512, 4, true, false, 0, 0, 0, 0, 0, 5);
    proxy.present(); await proxy.firstFrame;
    const texture = graphics.events.find(event => event[0] === 'createTexture');
    assert.deepEqual(texture[4], Array.from(new Uint8Array(single.buffer)));
    assert.equal(texture.at(-1), 13);
    const rain = graphics.events.find(event => event[0] === 'rain')[1];
    assert.deepEqual(rain.positions, [1, 2, 3, .25]);
    assert.deepEqual(rain.shapes, [-1, 1, 2, .75]);
    assert.deepEqual(rain.vs, new Array(24).fill(4.5));
    assert.deepEqual(rain.ps, new Array(12).fill(7.5));
    assert.deepEqual(rain.scalars.slice(0, 3), [4, true, false]);
    await proxy.dispose();
});

test('static rain registration owns transferred geometry once while steady packets carry ranges and uniforms only', async () => {
    const { proxy, worker, graphics } = await setup();
    const heap = new ArrayBuffer(128 * 1024);
    new Float32Array(heap, 64, 4096).fill(0.25);
    new Float32Array(heap, 16448, 4096).fill(0.75);
    new Uint32Array(heap, 32832, 6).set([0, 1, 2, 0, 2, 3]);
    new Float32Array(heap, 32960, 16).set(identity);
    new Float32Array(heap, 33024, 24).fill(4.5);
    new Float32Array(heap, 33120, 12).fill(7.5);
    const draw = (start, count) => proxy.drawRainGeometryMemory(17, start, count, heap, 8, 32960, 33024, 33120,
        true, false, 0, 0, 0, 0, 0, 5);
    proxy.beginFrame(128, 96);
    proxy.createRainGeometryMemory(17, heap, 64, 16448, 1024, 32832, 6);
    draw(0, 6); proxy.present();
    new Float32Array(heap, 64, 4096).fill(999);
    await proxy.firstFrame;
    await proxy.waitForFrameSlot();
    proxy.beginFrame(128, 96); draw(3, 3); proxy.destroyRainGeometry(17); proxy.present();
    await pause(); await pause();
    const frames = worker.messages.filter(message => message.type === 'frame');
    assert.ok(frames[0].length > 32768);
    assert.ok(frames[1].length < 1024);
    assert.equal(graphics.events.filter(event => event[0] === 'rainCreate').length, 1);
    assert.equal(graphics.events.find(event => event[0] === 'rainCreate')[2][0], 0.25);
    assert.deepEqual(graphics.events.filter(event => event[0].startsWith('rain')).map(event => event[0]),
        ['rainCreate', 'rainStatic', 'rainStatic', 'rainDestroy']);
    const draws = graphics.events.filter(event => event[0] === 'rainStatic');
    assert.deepEqual(draws.map(event => event.slice(1, 4)), [[17, 0, 6], [17, 3, 3]]);
    assert.deepEqual(draws[1][5], identity);
    assert.deepEqual(draws[1][6], new Array(24).fill(4.5));
    await proxy.dispose();
});

test('render controls and profile cadence are acknowledged independently from main logic ticks', async () => {
    const { proxy, imports } = await setup();
    imports.createScene(1); imports.configureScene(1, JSON.stringify({ quality: 'High' }));
    await proxy.flushResources();
    assert.equal(proxy.getCapabilities().effective, null, 'configuration has not produced a GPU-completed frame');
    assert.ok(proxy.getProfile());
    await proxy.setTextureArraysEnabled(true);
    assert.equal(proxy.textureArraysEnabled, true);
    proxy.beginFrame(128, 96);
    await assert.rejects(proxy.requestProfile(), /between application frames/);
    await assert.rejects(proxy.setRenderBundlesEnabled(true), /between application frames/);
    proxy.present(); await proxy.firstFrame;
    assert.equal(proxy.getCapabilities().effective.quality, 'High');
    const profile = await proxy.requestProfile();
    assert.equal(profile.worker.intervalFrames, 1);
    assert.equal(profile.worker.submittedFrames, 1);
    assert.equal(profile.workerTransport.submittedFrames, 1);
    assert.match(profile.worker.measurement, /not main logic ticks/);
    assert.equal((await proxy.requestProfile()).worker.intervalFrames, 0);
    await proxy.dispose();
});

test('point-light experiment controls remain ordered and acknowledged without borrowing an active frame', async () => {
    const { proxy, graphics } = await setup({ zeroPointLights: true });
    proxy.beginFrame(128, 96);
    await assert.rejects(proxy.setZeroPointLightsEnabled(false), /between application frames/);
    proxy.present();
    await proxy.setZeroPointLightsEnabled(false);
    assert.equal(proxy.getProfile().zeroPointLights.enabled, false);
    await proxy.setZeroPointLightsEnabled(true);
    assert.equal(proxy.getProfile().zeroPointLights.enabled, true);
    assert.deepEqual(graphics.events.filter(event => event[0] === 'setZeroPointLightsEnabled'),
        [['setZeroPointLightsEnabled', false], ['setZeroPointLightsEnabled', true]]);
    await assert.rejects(proxy.setZeroPointLightsEnabled('false'), /boolean/);
    await proxy.dispose();
});

test('the worker preserves auto policy without profiling and refuses malformed modes before creating or transferring anything', async () => {
    const { proxy, worker } = await setup({ profile: false, zeroPointLights: 'auto' });
    assert.equal(worker.initOptions.zeroPointLights, 'auto');
    assert.equal(worker.initOptions.profile, false);
    assert.equal(worker.initOptions.gpuTiming, false);
    await proxy.dispose();
    for (const zeroPointLights of [null, 0, 1, 'true', 'false', '', {}, []]) {
        await assert.rejects(createWorkerGraphics({ transferControlToOffscreen() { assert.fail('invalid mode cannot transfer'); } },
            { zeroPointLights, workerFactory() { assert.fail('invalid mode cannot create a Worker'); } }), /mode must be/);
    }
});

test('invalid CPU ranges and malformed packets fail explicitly rather than suppressing draws', async () => {
    const { proxy } = await setup();
    proxy.beginFrame(128, 96);
    assert.throws(() => native(proxy, new ArrayBuffer(8)), /exceeds CPU memory/);
    await proxy.dispose();
    const writer = new WorkerPacketWriter(new ArrayBuffer(16));
    const at = writer.begin(999); writer.end(at);
    assert.throws(() => executeWorkerPacket({}, {}, writer.buffer, writer.length), /Unknown render worker operation/);
    const reader = new WorkerPacketReader(writer.buffer, writer.length - 1);
    assert.throws(() => reader.next(), /Truncated worker command/);
});

test('worker errors reject pending work, release the scheduler and retain the fatal error', async () => {
    const { proxy, worker } = await setup();
    worker.holdFrames = true;
    for (let frame = 0; frame < 3; frame++) { proxy.beginFrame(128, 96); proxy.present(); }
    const wait = proxy.waitForFrameSlot();
    worker.onmessage({ data: { type: 'fatal', error: 'real render failure' } });
    await assert.rejects(wait, /real render failure/);
    await assert.rejects(proxy.firstFrame, /real render failure/);
    assert.equal(proxy.canBeginFrame, false);
    assert.deepEqual([...proxy.errors], ['real render failure']);
    await proxy.dispose();
    assert.equal(worker.terminated, true);
});

test('explicit diagnostic capture snapshots immediately after present, transfers ownership and is bounded to one pending frame', async () => {
    let captures = 0;
    const { proxy, graphics } = await setup({ captureCanvas(canvas) {
        captures++;
        assert.equal(canvas, graphics.canvas);
        assert.equal(graphics.events.at(-1)[0], 'present');
        return Promise.resolve(new ArrayBuffer(8));
    } });
    proxy.beginFrame(128, 96); proxy.present(); await proxy.firstFrame;
    assert.equal(captures, 0);
    const capture = proxy.captureNextFrame();
    proxy.beginFrame(128, 96); proxy.present();
    await assert.rejects(proxy.captureNextFrame(), /already pending/);
    const bitmap = await capture;
    assert.equal(bitmap.byteLength, 8);
    assert.equal(captures, 1);
    const unused = assert.rejects(proxy.captureNextFrame(), /disposed/);
    await proxy.dispose(); await unused;
    const normal = await setup({ profile: false });
    await assert.rejects(normal.proxy.captureNextFrame(), /explicit profiling/);
    await normal.proxy.dispose();
});

test('transferred SMAA lookup ownership spans quality changes and closes only at final disposal or failed initialization', async () => {
    let closes = 0;
    const lookups = { area: { close() { closes++; } }, search: { close() { closes++; } } };
    const graphics = graphicsRecorder();
    const handle = createWorkerMessageHandler(() => {}, async () => graphics, undefined, immediateCompletion);
    await handle({ type: 'init', canvas: {}, options: {}, workerInit: { smaaLookups: lookups } });
    assert.equal(globalThis.__noxiusWorkerSmaaLookups, lookups);
    await handle({ type: 'control', op: WorkerOp.textureArrays, enabled: true });
    assert.equal(closes, 0);
    await handle({ type: 'dispose' });
    assert.equal(closes, 2);
    assert.equal(globalThis.__noxiusWorkerSmaaLookups, undefined);
    const failure = createWorkerMessageHandler(() => {}, async () => { throw Error('device init failed'); });
    await assert.rejects(failure({ type: 'init', canvas: {}, options: {}, workerInit: { smaaLookups: lookups } }), /device init failed/);
    assert.equal(closes, 4);
    assert.equal(globalThis.__noxiusWorkerSmaaLookups, undefined);
});

test('submission fences capture the queued frame after Draw and ignore earlier frame acknowledgments', async () => {
    const { proxy, worker } = await setup();
    worker.holdFrames = true;
    for (let frame = 0; frame < 3; frame++) { proxy.beginFrame(128, 96); proxy.present(); }
    assert.equal(proxy.lastQueuedFrameId, 3);
    let ready = false;
    const fence = proxy.waitForSubmittedFrame().then(() => { ready = true; });
    await worker.release(); assert.equal(ready, false);
    await proxy.waitForSubmittedFrame(1);
    await worker.release(); assert.equal(ready, false);
    await worker.release(); await fence;
    assert.equal(ready, true);
    await assert.rejects(proxy.waitForSubmittedFrame(4), /Invalid queued render frame id/);
    await proxy.dispose();
    const early = await setup();
    const first = assert.rejects(early.proxy.firstFrame, /disposed/);
    await early.proxy.dispose(); await first;
});

test('GPU timing controls return the worker-acknowledged mode rather than optimistic main state', async () => {
    const { proxy, graphics } = await setup();
    assert.equal(await proxy.setGpuTimingMode('off'), 'off');
    assert.equal(graphics.gpuMode, 'off');
    assert.equal(await proxy.setGpuTimingMode('continuous'), 'continuous');
    assert.equal(proxy.getProfile().gpu.mode, 'continuous');
    await assert.rejects(proxy.setGpuTimingMode('unknown'), /Unknown GPU timing mode/);
    await proxy.dispose();
});

test('worker initialization timeout terminates the abandoned worker and rejects its outstanding startup', async () => {
    let worker, terminated = false;
    const canvas = { dataset: {}, transferControlToOffscreen() { return new ArrayBuffer(0); } };
    await assert.rejects(createWorkerGraphics(canvas, { initTimeoutMs: 5, workerFactory() {
        return worker = { postMessage() {}, terminate() { terminated = true; } };
    } }), /initialization timed out/);
    assert.equal(terminated, true);
    assert.equal(canvas.dataset.rendererState, 'lost');
    assert.ok(worker);
});

function heldGpuCompletion() {
    const jobs = [];
    let disposed = false;
    return { jobs, get disposed() { return disposed; }, afterSubmit() {
        return new Promise((resolve, reject) => jobs.push({ resolve, reject }));
    }, dispose() {
        if (disposed) return;
        disposed = true;
        for (const job of jobs) job.reject(Error('completion cancelled'));
    } };
}

test('GPU completion, not CPU submission, owns all three frame slots and loading fences while the worker keeps encoding', async () => {
    const gpu = heldGpuCompletion();
    const { proxy, worker, graphics } = await setup({ completionFactory: () => gpu });
    let first = false, fenced = false, slot = false;
    proxy.firstFrame.then(() => { first = true; });
    for (let frame = 0; frame < 3; frame++) { proxy.beginFrame(128, 96); proxy.present(); }
    const fence = proxy.waitForSubmittedFrame().then(() => { fenced = true; });
    const available = proxy.waitForFrameSlot().then(() => { slot = true; });
    await worker.queue; await pause();
    assert.equal(graphics.stats.frames, 3, 'ordered handler encoded later frames without waiting on the first GPU fence');
    assert.equal(gpu.jobs.length, 3);
    assert.equal(proxy.workerStats.submittedFrames, 3);
    assert.equal(proxy.workerStats.completedFrames, 0);
    assert.equal(proxy.workerStats.inFlightFrames, 3);
    assert.equal(proxy.canBeginFrame, false);
    assert.equal(first, false); assert.equal(fenced, false); assert.equal(slot, false);
    assert.throws(() => proxy.beginFrame(128, 96), /no free frame slot/);
    proxy.createTexture(77, 1, 1, new Uint8Array([1, 2, 3, 4]));
    await proxy.flushResources();
    assert.ok(graphics.events.some(event => event[0] === 'createTexture' && event[1] === 77), 'resources remain FIFO without blocking on GPU completion');
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await available; await pause();
    assert.equal(proxy.workerStats.completedFrames, 1);
    assert.equal(proxy.workerStats.inFlightFrames, 2);
    assert.equal(first, true); assert.equal(fenced, false);
    proxy.beginFrame(128, 96); proxy.present(); await worker.queue; await pause();
    assert.equal(graphics.stats.frames, 4);
    assert.equal(proxy.workerStats.submittedFrames, 4);
    assert.equal(proxy.workerStats.completedFrames, 1);
    assert.equal(proxy.canBeginFrame, false);
    gpu.jobs[1].resolve(performance.timeOrigin + performance.now());
    gpu.jobs[2].resolve(performance.timeOrigin + performance.now()); await fence;
    assert.equal(fenced, true);
    gpu.jobs[3].resolve(performance.timeOrigin + performance.now()); await proxy.waitForSubmittedFrame();
    assert.equal(proxy.workerStats.completedFrames, 4);
    assert.ok(proxy.workerStats.lastCompletedAtMs >= proxy.workerStats.firstCompletedAtMs);
    const profile = await proxy.requestProfile();
    assert.equal(profile.worker.completedFrames, 4);
    assert.equal(profile.worker.submittedFrames, 4);
    assert.match(profile.worker.measurement, /GPU-fence completion/);
    await proxy.dispose();
});

test('GPU failure rejects loading and slot fences, cancels other completions and never returns buffers early', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu });
    for (let frame = 0; frame < 3; frame++) { f.proxy.beginFrame(128, 96); f.proxy.present(); }
    await f.worker.queue;
    const first = assert.rejects(f.proxy.firstFrame, /GPU queue failed/);
    const fence = assert.rejects(f.proxy.waitForSubmittedFrame(), /GPU queue failed/);
    const slot = assert.rejects(f.proxy.waitForFrameSlot(), /GPU queue failed/);
    gpu.jobs[0].reject(Error('GPU queue failed'));
    await Promise.all([first, fence, slot]);
    assert.equal(f.proxy.workerStats.completedFrames, 0);
    assert.equal(f.proxy.canBeginFrame, false);
    assert.equal(gpu.disposed, true);
    assert.equal(f.proxy.errors.size, 1);
    await f.proxy.dispose();
});

test('explicit profiling may hold four GPU-owned frame buffers but cannot submit a fifth before a completion', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ frameSlots: 4, completionFactory: () => gpu });
    for (let frame = 0; frame < 4; frame++) { f.proxy.beginFrame(128, 96); f.proxy.present(); }
    await f.worker.queue;
    assert.equal(f.proxy.workerStats.frameSlots, 4);
    assert.equal(f.proxy.workerStats.submittedFrames, 4);
    assert.equal(f.proxy.workerStats.completedFrames, 0);
    assert.equal(f.proxy.workerStats.inFlightFrames, 4);
    assert.equal(gpu.jobs.length, 4);
    assert.equal(f.proxy.canAdvanceFrame, false);
    assert.throws(() => f.proxy.beginFrame(128, 96), /no free frame slot/);
    let available = false;
    const slot = f.proxy.waitForFrameSlot().then(() => { available = true; });
    await pause();
    assert.equal(available, false);
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await slot;
    assert.equal(f.proxy.workerStats.completedFrames, 1);
    assert.equal(f.proxy.free.length, 1, 'only the completed GPU frame returns its buffer');
    f.proxy.beginFrame(128, 96); f.proxy.present(); await f.worker.queue;
    assert.equal(f.proxy.workerStats.inFlightFrames, 4);
    assert.equal(f.proxy.canAdvanceFrame, false);
    for (const job of gpu.jobs.slice(1)) job.resolve(performance.timeOrigin + performance.now());
    await f.proxy.waitForSubmittedFrame();
    assert.equal(f.proxy.workerStats.completedFrames, 5);
    assert.equal(f.proxy.free.length, 4);
    assert.equal((await f.proxy.requestProfile()).workerTiming.proxy.frameSlots, 4);
    await f.proxy.dispose();
});

test('normal default four-slot ownership remains GPU-fenced with profiling and capture disabled', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ profile: false, frameSlots: undefined, completionFactory: () => gpu });
    assert.equal(f.proxy.workerStats.frameSlots, 4);
    assert.equal(f.worker.initOptions.profile, false);
    assert.equal(f.worker.initOptions.gpuTiming, false);
    await assert.rejects(f.proxy.captureNextFrame(), /explicit profiling/);
    for (let frame = 0; frame < 4; frame++) { f.proxy.beginFrame(128, 96); f.proxy.present(); }
    await f.worker.queue;
    assert.equal(f.proxy.workerStats.completedFrames, 0);
    assert.equal(f.proxy.workerStats.inFlightFrames, 4);
    assert.equal(f.worker.messages.filter(message => message.type === 'frame').every(message => !message.hasQueuedAt), true);
    assert.throws(() => f.proxy.beginFrame(128, 96), /no free frame slot/);
    let available = false;
    const slot = f.proxy.waitForFrameSlot().then(() => { available = true; });
    await pause(); assert.equal(available, false);
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await slot;
    assert.equal(f.proxy.free.length, 1);
    assert.equal(f.proxy.workerStats.completedFrames, 1);
    for (const job of gpu.jobs.slice(1)) job.resolve(performance.timeOrigin + performance.now());
    await f.proxy.waitForSubmittedFrame();
    assert.equal(f.proxy.free.length, 4);
    assert.equal(f.proxy.getProfile().workerTiming, undefined);
    await f.proxy.dispose();
});

test('frame-slot limits reject values outside the existing three/four contract before creating a Worker', async () => {
    for (const options of [0, 1, 2, 5, -1, 3.5, '4', NaN, Infinity].map(frameSlots => ({ profile: false, frameSlots }))) {
        await assert.rejects(createWorkerGraphics({ transferControlToOffscreen() { assert.fail('invalid settings cannot transfer'); } },
            { ...options, workerFactory() { assert.fail('invalid settings cannot create a Worker'); } }), /frame slots must be/);
    }
});

test('disposal cancels pending GPU completions without ACKs or fatal noise and rejects startup/map waiters', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu });
    for (let frame = 0; frame < 3; frame++) { f.proxy.beginFrame(128, 96); f.proxy.present(); }
    await f.worker.queue;
    const first = assert.rejects(f.proxy.firstFrame, /disposed/);
    const fence = assert.rejects(f.proxy.waitForSubmittedFrame(), /disposed/);
    const disposal = f.proxy.dispose();
    assert.equal(f.proxy.canBeginFrame, false);
    await assert.rejects(f.proxy.waitForFrameSlot(), /disposed/);
    await Promise.all([disposal, f.proxy.dispose(), first, fence]);
    assert.equal(f.worker.messages.filter(message => message.type === 'dispose').length, 1);
    assert.equal(f.proxy.workerStats.completedFrames, 0);
    assert.equal(f.proxy.errors.size, 0);
    assert.equal(gpu.disposed, true);
    assert.equal(f.worker.terminated, true);
    assert.equal(f.graphics.events.at(-1)[0], 'dispose');
    gpu.jobs[0].resolve(10); await pause();
    assert.equal(f.proxy.workerStats.completedFrames, 0);
});

test('an older completed frame snapshot cannot revert newer acknowledged settings or profiles', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu });
    f.imports.createScene(1);
    f.proxy.beginFrame(128, 96);
    f.imports.configureScene(1, JSON.stringify({ quality: 'Low' }));
    f.proxy.present(); await f.worker.queue;
    assert.equal(f.proxy.workerStats.completedFrames, 0);
    f.imports.configureScene(1, JSON.stringify({ quality: 'High' }));
    await f.proxy.setTextureArraysEnabled(true);
    const profile = await f.proxy.requestProfile(), revision = f.proxy.snapshotRevision;
    assert.equal(f.proxy.getCapabilities().effective, null);
    assert.equal(f.proxy.textureArraysEnabled, true);
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now());
    await f.proxy.waitForSubmittedFrame();
    assert.ok(f.proxy.snapshotRevision > revision);
    assert.equal(f.proxy.getCapabilities().effective.quality, 'Low', 'the completed frame was rendered before the newer configuration');
    assert.equal(f.proxy.textureArraysEnabled, true);
    assert.equal(f.proxy.getProfile(), profile);
    assert.equal(f.proxy.workerStats.completedFrames, 1, 'stale metadata does not prevent the GPU-completed slot ACK');
    assert.equal(f.proxy.canBeginFrame, true);
    f.proxy.beginFrame(128, 96); f.proxy.present(); await f.worker.queue;
    gpu.jobs[1].resolve(performance.timeOrigin + performance.now());
    await f.proxy.waitForSubmittedFrame();
    assert.equal(f.proxy.getCapabilities().effective.quality, 'High');
    await f.proxy.dispose();
});

function mutablePost(graphics) {
    graphics.width = 1280; graphics.height = 720; graphics.capabilityReads = 0;
    graphics.post = { effective: { renderScale: 1, quality: 'High' }, target: { width: 1280, height: 720 } };
    graphics.getCapabilities = function () { this.capabilityReads++; return { backend: 'Worker recorder', internalPost: this.internalPostRequested === true, effective: this.post.effective }; };
    graphics.beginFrame = function (width, height) { this.width = width; this.height = height; };
    graphics.present = function () {
        this.stats.frames++;
        this.post.effective.renderScale = this.dynamicScale ?? 1;
        this.post.effective.dynamicShadowUpdateRate = this.dynamicShadowHz ?? 0;
        this.post.target.width = Math.round(this.width * this.post.effective.renderScale);
        this.post.target.height = Math.round(this.height * this.post.effective.renderScale);
    };
}

test('effective render size is copied at render and committed only by the newest GPU-completed frame', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu, configureGraphics: mutablePost });
    assert.equal(f.proxy.getCapabilities().effective, null);
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective, null, 'a newer profile cannot expose an incomplete first frame');
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await f.proxy.firstFrame;
    assert.equal(f.proxy.getCapabilities().effective.renderScale, 1);
    const profile = f.proxy.getProfile();
    await f.proxy.setDynamicResolutionScale(.7, 30);
    assert.equal(f.proxy.getProfile(), profile, 'automatic DRS acknowledgments do not reset measurement windows');
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    await f.proxy.setTextureArraysEnabled(true); await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective.renderScale, 1, 'control/profile acknowledgments retain the GPU-committed size');
    await f.proxy.setDynamicResolutionScale(.9, 0);
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    gpu.jobs[2].resolve(performance.timeOrigin + performance.now()); await f.proxy.waitForSubmittedFrame(3);
    assert.deepEqual([f.proxy.getCapabilities().effective.renderScale, f.proxy.getCapabilities().effective.renderWidth,
        f.proxy.getCapabilities().effective.renderHeight], [.9, 1152, 648]);
    f.graphics.post.effective.renderScale = .6;
    gpu.jobs[1].resolve(performance.timeOrigin + performance.now()); await pause();
    await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective.renderScale, .9, 'older completion and later CPU mutation cannot change the committed copy');
    assert.equal(f.proxy.textureArraysEnabled, true);
    assert.equal(f.proxy.workerStats.completedFrames, 3);
    assert.equal(f.proxy.effectiveFrameId, 3);
    await f.proxy.dispose();
});

test('effective frame revision advances independently of newer general metadata revisions', async () => {
    const f = await setup();
    await f.proxy.setTextureArraysEnabled(true); await f.proxy.requestProfile();
    const revision = f.proxy.snapshotRevision;
    const snapshot = { revision: revision - 1, effectiveFrameId: 2,
        capabilities: { backend: 'older backend', effective: { renderScale: .8 } }, textureArraysEnabled: false };
    f.proxy.applySnapshot(snapshot);
    assert.equal(f.proxy.getCapabilities().effective.renderScale, .8);
    assert.equal(f.proxy.getCapabilities().backend, 'Worker recorder');
    assert.equal(f.proxy.textureArraysEnabled, true);
    f.proxy.applySnapshot({ ...snapshot, revision: revision + 1, effectiveFrameId: 0,
        capabilities: { backend: 'new backend', effective: null }, textureArraysEnabled: true, errors: [] });
    assert.equal(f.proxy.getCapabilities().effective.renderScale, .8, 'new general metadata with an older frame cannot erase the latest GPU commit');
    assert.equal(f.proxy.getCapabilities().backend, 'new backend');
    await f.proxy.dispose();
});

test('unchanged frames reuse effective state while growing targets wait for GPU completion', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu, configureGraphics: mutablePost });
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await f.proxy.firstFrame;
    const effective = f.proxy.getCapabilities().effective, reads = f.graphics.capabilityReads;
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    gpu.jobs[1].resolve(performance.timeOrigin + performance.now()); await f.proxy.waitForSubmittedFrame();
    assert.equal(f.graphics.capabilityReads, reads, 'unchanged frames do not capture capability/effective snapshots');
    assert.equal(f.proxy.getCapabilities().effective, effective);
    f.proxy.beginFrame(1920, 1080); f.proxy.present(); await f.worker.queue;
    await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective.renderWidth, 1280);
    gpu.jobs[2].resolve(performance.timeOrigin + performance.now()); await f.proxy.waitForSubmittedFrame();
    assert.deepEqual([f.proxy.getCapabilities().effective.renderWidth, f.proxy.getCapabilities().effective.renderHeight], [1920, 1080]);
    await f.proxy.dispose();
});

test('failure to transfer the canvas terminates the Worker already created by the actual factory', async () => {
    let terminated = false;
    const canvas = { dataset: {}, transferControlToOffscreen() { throw Error('canvas already has a context'); } };
    await assert.rejects(createWorkerGraphics(canvas, { workerFactory: () => ({
        postMessage() { assert.fail('initialization cannot be sent after transfer fails'); },
        terminate() { terminated = true; }
    }) }), /canvas already has a context/);
    assert.equal(terminated, true);
    assert.equal(canvas.dataset.rendererState, 'lost');
});

test('native initialization may pre-open a frame without blocking the first host tick or permitting a second begin', async () => {
    const { proxy } = await setup();
    proxy.beginFrame(128, 96);
    assert.equal(proxy.canBeginFrame, false, 'the native begin operation cannot be repeated');
    assert.equal(proxy.canAdvanceFrame, true, 'host may finish a frame opened by native initialization');
    await proxy.waitForFrameSlot();
    assert.throws(() => proxy.beginFrame(128, 96), /already has an active frame/);
    proxy.clear(0, 0, 0, 1);
    proxy.present();
    await proxy.firstFrame;
    assert.equal(proxy.workerStats.completedFrames, 1);
    assert.equal(proxy.canBeginFrame, true);
    await proxy.dispose();
});

function timedFramePacket() {
    const writer = new WorkerPacketWriter();
    let at = writer.begin(WorkerOp.begin); writer.number(128); writer.number(96); writer.end(at);
    at = writer.begin(WorkerOp.clear);
    for (const value of [0, 0, 0, 1, 1, 1]) writer.number(value);
    writer.end(at); at = writer.begin(WorkerOp.present); writer.end(at);
    return { buffer: writer.buffer, length: writer.length };
}

test('profile-only worker timing measures complete packet CPU and paired completion latency independently', async () => {
    let now = 100;
    const messages = [], graphics = graphicsRecorder(), gpu = heldGpuCompletion();
    graphics.beginFrame = () => { now += 2; };
    graphics.clear = () => { now += 3; };
    graphics.present = () => { now += 5; };
    const handle = createWorkerMessageHandler(message => messages.push(message), async () => graphics, undefined, () => gpu, () => now);
    await handle({ type: 'init', canvas: {}, options: { profile: true } });
    await handle({ type: 'frame', frameId: 1, slot: 0, queuedAtMs: performance.timeOrigin + 93, ...timedFramePacket() });
    now = 140;
    await handle({ type: 'frame', frameId: 2, slot: 1, queuedAtMs: performance.timeOrigin + 130, ...timedFramePacket() });
    const submitted = messages.filter(message => message.type === 'submitted');
    gpu.jobs[0].resolve(submitted[0].submittedAtMs + 25);
    gpu.jobs[1].resolve(submitted[1].submittedAtMs + 35);
    await pause();
    await handle({ type: 'profile', requestId: 20 });
    const timing = messages.at(-1).snapshot.profile.workerTiming;
    assert.deepEqual(timing.packetCpuMs, { count: 2, mean: 10, max: 10 });
    assert.deepEqual(timing.queuedToWorkerStartMs, { count: 2, mean: 8.5, max: 10 });
    assert.deepEqual(timing.submitToGpuCompletionMs, { count: 2, mean: 30, max: 35 });
    assert.equal(timing.peakPendingCompletionAcks, 2);
    assert.equal(timing.pendingCompletionAcks, 0);
    assert.match(timing.measurement, /not shader-only time/);
    await handle({ type: 'profile', requestId: 21 });
    assert.equal(messages.at(-1).snapshot.profile.workerTiming.packetCpuMs.count, 0);
    await handle({ type: 'dispose' });
});

test('timing is absent on normal packets and profile slot waits report bounded queue pressure without changing ACKs', async () => {
    const normal = await setup({ profile: false });
    normal.proxy.beginFrame(128, 96); normal.proxy.present(); await normal.proxy.firstFrame;
    assert.equal(normal.worker.messages.find(message => message.type === 'frame').hasQueuedAt, false);
    assert.equal(normal.proxy.getProfile().workerTiming, undefined);
    await normal.proxy.dispose();
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu });
    for (let frame = 0; frame < 3; frame++) { f.proxy.beginFrame(128, 96); f.proxy.present(); }
    const wait = f.proxy.waitForFrameSlot();
    await f.worker.queue;
    assert.ok(f.worker.messages.filter(message => message.type === 'frame').every(message => message.hasQueuedAt && Number.isFinite(message.queuedAtMs)));
    assert.equal(f.proxy.workerStats.completedFrames, 0);
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await wait;
    const timing = (await f.proxy.requestProfile()).workerTiming;
    assert.equal(timing.proxy.slotWaitMs.count, 1);
    assert.ok(timing.proxy.slotWaitMs.mean >= 0);
    assert.equal(timing.proxy.slotWaitMs.mean, timing.proxy.slotWaitMs.max);
    assert.equal(timing.proxy.inFlightFrames, 2);
    assert.equal(timing.proxy.peakInFlightFrames, 3);
    assert.equal(timing.proxy.frameSlots, 3);
    assert.equal((await f.proxy.requestProfile()).workerTiming.proxy.slotWaitMs.count, 0);
    await f.proxy.dispose();
});

test('new timing epochs exclude older pending completion and slot-wait samples', async () => {
    const gpu = heldGpuCompletion(), f = await setup({ completionFactory: () => gpu });
    for (let frame = 0; frame < 3; frame++) { f.proxy.beginFrame(128, 96); f.proxy.present(); }
    const wait = f.proxy.waitForFrameSlot();
    await f.worker.queue;
    await f.proxy.requestProfile();
    gpu.jobs[0].resolve(performance.timeOrigin + performance.now()); await wait;
    const timing = (await f.proxy.requestProfile()).workerTiming;
    assert.equal(timing.submitToGpuCompletionMs.count, 0);
    assert.equal(timing.proxy.slotWaitMs.count, 0);
    assert.equal(f.proxy.workerStats.completedFrames, 1, 'measurement resets do not change real completion counters');
    await f.proxy.dispose();
});


test('internal post ordered controls use the acknowledged backend capability and refuse unavailable activation locally', async () => {
    const normal = await setup({ profile: false, internalPost: false });
    assert.equal(normal.worker.initOptions.internalPost, false);
    await assert.rejects(normal.proxy.setInternalPostEnabled(true), /renderer configuration/);
    assert.equal(normal.proxy.failed, null); await normal.proxy.dispose();
    const f = await setup({ internalPost: true });
    assert.equal(f.worker.initOptions.internalPost, true);
    await f.proxy.setInternalPostEnabled(false); await f.proxy.setInternalPostEnabled(true);
    assert.deepEqual(f.graphics.events.filter(event => event[0] === 'setInternalPostEnabled'),
        [['setInternalPostEnabled', false], ['setInternalPostEnabled', true]]);
    for (const invalid of [null, 1, 'true', undefined]) await assert.rejects(f.proxy.setInternalPostEnabled(invalid), TypeError);
    f.proxy.beginFrame(128, 96);
    await assert.rejects(f.proxy.setInternalPostEnabled(false), /between application frames/);
    f.proxy.present(); await f.proxy.waitForSubmittedFrame(); await f.proxy.dispose();
});

test('post processing dimensions commit only after their matching GPU frame, despite later control/profile ACKs', async () => {
    const gpu = heldGpuCompletion();
    const f = await setup({ internalPost: true, completionFactory: () => gpu, configureGraphics(graphics) {
        mutablePost(graphics);
        graphics.internalPostEnabled = true;
        const profile = graphics.getProfile;
        graphics.getProfile = function () {
            return { ...profile.call(this), internalPost: { requested: true, enabled: this.internalPostEnabled,
                width: this.post.effective.postProcessingWidth, height: this.post.effective.postProcessingHeight } };
        };
        graphics.setInternalPostEnabled = function (value) { this.internalPostEnabled = value; };
        const present = graphics.present;
        graphics.present = function () {
            present.call(this);
            const scale = this.internalPostEnabled ? this.dynamicScale ?? 1 : 1;
            this.post.effective.internalPost = this.internalPostEnabled;
            this.post.effective.postProcessingWidth = Math.floor(this.width * scale);
            this.post.effective.postProcessingHeight = Math.floor(this.height * scale);
        };
    } });
    await f.proxy.setDynamicResolutionScale(.5);
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    gpu.jobs[0].resolve(); await f.proxy.firstFrame;
    assert.deepEqual([f.proxy.getCapabilities().effective.postProcessingWidth, f.proxy.getCapabilities().effective.postProcessingHeight], [640, 360]);
    const firstEffective = f.proxy.getCapabilities().effective;
    await f.proxy.setInternalPostEnabled(false);
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    await f.proxy.setInternalPostEnabled(true); await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective, firstEffective, 'control/profile metadata does not publish an uncompleted footprint');
    assert.equal(f.proxy.getProfile().internalPost.width, 640, 'profile dimensions also retain the last GPU-completed footprint');
    f.proxy.beginFrame(1281, 721); f.proxy.present(); await f.worker.queue;
    gpu.jobs[2].resolve(); await f.proxy.waitForSubmittedFrame(3);
    const latest = f.proxy.getCapabilities().effective;
    assert.equal(latest.internalPost, true); assert.deepEqual([latest.postProcessingWidth, latest.postProcessingHeight], [640, 360]);
    gpu.jobs[1].resolve(); await pause();
    assert.equal(f.proxy.getCapabilities().effective, latest, 'older full-size completion cannot overwrite the newer internal-size GPU commit');
    assert.equal(f.graphics.post.effective.postProcessingWidth, 640, 'source has already moved to the new size');
    await f.proxy.dispose();
});


test('normal Worker initialization preserves auto without profiling and validates before Worker creation or canvas transfer', async () => {
    for (const [webGPUBackend, expected] of [[true, true], [false, false]]) {
        const f = await setup({ profile: false, internalPost: 'auto', configureGraphics(graphics) { graphics.webGPUBackend = webGPUBackend; graphics.getProfile = () => null; } });
        assert.equal(f.worker.initOptions.internalPost, 'auto'); assert.equal(f.worker.initOptions.profile, false);
        assert.equal(f.proxy.getCapabilities().internalPost, expected); assert.equal(f.proxy.internalPostRequested, expected);
        assert.equal(f.proxy.workerTiming, null); assert.equal(f.proxy.getProfile(), null);
        if (!expected) {
            await assert.rejects(f.proxy.setInternalPostEnabled(true), /renderer configuration/);
            assert.equal(f.proxy.failed, null, 'an unavailable toggle is rejected locally without breaking the Worker');
        }
        await f.proxy.dispose();
    }
    for (const internalPost of [null, 0, 1, 'true', 'false', {}, []]) {
        await assert.rejects(createWorkerGraphics({ transferControlToOffscreen() { assert.fail('invalid mode must not transfer the canvas'); } }, {
            internalPost, workerFactory() { assert.fail('invalid mode must not create a Worker'); }
        }), /mode must be/);
    }
});

test('Worker fused sharpening preserves tri-state auto without enabling profiling and acknowledges the actual backend', async () => {
    for (const [fusedSharpen, webGPUBackend, expectedMode, expected] of [
        ['auto', true, 'auto', true], ['auto', false, 'auto', false], [true, false, true, true],
        [false, true, false, false], [undefined, true, false, false]]) {
        const f = await setup({ profile: false, fusedSharpen, configureGraphics(graphics) {
            graphics.webGPUBackend = webGPUBackend; graphics.getProfile = () => null;
        } });
        assert.equal(f.worker.initOptions.fusedSharpen, expectedMode);
        assert.equal(f.worker.initOptions.profile, false); assert.equal(f.worker.initOptions.gpuTiming, false);
        assert.equal(f.proxy.getCapabilities().fusedSharpen, expected);
        assert.equal(f.proxy.workerTiming, null); assert.equal(f.proxy.getProfile(), null);
        assert.equal(f.proxy.setFusedSharpenEnabled, undefined);
        await f.proxy.dispose();
    }
    for (const fusedSharpen of [null, 0, 1, 'true', 'false', '', {}, []]) {
        await assert.rejects(createWorkerGraphics({ transferControlToOffscreen() { assert.fail('invalid mode must not transfer the canvas'); } }, {
            fusedSharpen, workerFactory() { assert.fail('invalid mode must not create a Worker'); }
        }), /mode must be/);
    }
});

test('fused usage follows the matching completed GPU frame, including profile ACKs and out-of-order completions', async () => {
    const gpu = heldGpuCompletion();
    const f = await setup({ fusedSharpen: true, completionFactory: () => gpu, configureGraphics(graphics) {
        mutablePost(graphics);
        graphics.fusedUsage = true;
        const profile = graphics.getProfile;
        graphics.getProfile = function () { return { ...profile.call(this), fusedSharpen: { requested: true,
            enabled: this.post.effective.fusedSharpen, compositions: this.stats.frames } }; };
        const present = graphics.present;
        graphics.present = function () { present.call(this); this.post.effective.fusedSharpen = this.fusedUsage; };
    } });
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective, null);
    assert.equal(f.proxy.getProfile().fusedSharpen.enabled, false);
    gpu.jobs[0].resolve(); await f.proxy.firstFrame; await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective.fusedSharpen, true);
    assert.equal(f.proxy.getProfile().fusedSharpen.enabled, true);
    f.graphics.fusedUsage = false;
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    await f.proxy.requestProfile();
    assert.equal(f.proxy.getCapabilities().effective.fusedSharpen, true, 'submitted zero-strength graph is not GPU committed');
    assert.equal(f.proxy.getProfile().fusedSharpen.enabled, true);
    f.graphics.fusedUsage = true;
    f.proxy.beginFrame(1280, 720); f.proxy.present(); await f.worker.queue;
    gpu.jobs[2].resolve(); await f.proxy.waitForSubmittedFrame(3);
    const latest = f.proxy.getCapabilities().effective;
    gpu.jobs[1].resolve(); await pause();
    assert.equal(latest.fusedSharpen, true);
    assert.equal(f.proxy.getCapabilities().effective, latest, 'older unfused frame cannot replace the newer fused commit');
    await f.proxy.dispose();
});
