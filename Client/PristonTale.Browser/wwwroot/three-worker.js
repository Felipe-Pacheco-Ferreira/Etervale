import { WorkerOp, WorkerPacketWriter } from './three-worker-protocol.js';
import { validateAdaptiveBudget, internalPostMode } from './adaptive-resolution.js';
import { zeroPointLightMode } from './three-zero-lighting.js';

const empty = new Uint8Array(0);
const defaultFrameSlots = 4;

export async function createWorkerGraphics(canvas, options = {}) {
    zeroPointLightMode(options.zeroPointLights);
    internalPostMode(options.internalPost);
    internalPostMode(options.fusedSharpen);
    if (typeof canvas.transferControlToOffscreen !== 'function') throw new Error('OffscreenCanvas transfer is unavailable.');
    const frameSlots = options.frameSlots ?? defaultFrameSlots;
    if (frameSlots !== 3 && frameSlots !== defaultFrameSlots) {
        throw new RangeError('Render worker frame slots must be 3 or 4.');
    }
    const initTimeoutMs = options.initTimeoutMs ?? 20000;
    if (!Number.isFinite(initTimeoutMs) || initTimeoutMs <= 0) throw new RangeError('Invalid render worker initialization timeout.');
    const url = options.workerUrl ?? new URL('./three-worker-bundle.js', import.meta.url);
    const worker = options.workerFactory ? options.workerFactory(url) : new Worker(url, { type: 'module' });
    const proxy = new WorkerGraphicsProxy(worker, canvas, options, frameSlots);
    let timeout;
    try {
        const offscreen = canvas.transferControlToOffscreen();
        await Promise.race([proxy.initialize(offscreen, options), new Promise((_, reject) => {
            timeout = setTimeout(() => reject(new Error('Render worker initialization timed out.')), initTimeoutMs);
        })]);
    } catch (error) {
        try { proxy.fail(error); } finally { worker.terminate(); }
        throw error;
    }
    finally { clearTimeout(timeout); }
    return proxy;
}

class WorkerGraphicsProxy {
    constructor(worker, canvas, options, frameSlots) {
        this.worker = worker;
        this.canvas = canvas;
        this.workerRendering = true;
        this.options = options;
        this.errors = new Set();
        this.frameSlots = frameSlots;
        this.slots = Array.from({ length: frameSlots }, () => new WorkerPacketWriter());
        this.free = Array.from({ length: frameSlots }, (_, index) => frameSlots - index - 1);
        this.slotFrames = new Float64Array(frameSlots);
        this.waiters = [];
        this.submissionWaiters = [];
        this.requests = new Map();
        this.requestId = 0;
        this.nextFrame = 0;
        this.active = null;
        this.internalPostRequested = false;
        this.failed = null;
        this.disposed = false;
        this.workerTiming = options.profile === true ? { count: 0, total: 0, max: 0, epoch: 0, peak: 0 } : null;
        this.workerStats = { submittedFrames: 0, completedFrames: 0, lastFrameId: 0, lastSubmittedFrameId: 0,
            firstSubmittedAtMs: null, lastSubmittedAtMs: null, firstCompletedAtMs: null, lastCompletedAtMs: null,
            submissionFps: 0, completionFps: 0, inFlightFrames: 0, frameSlots,
            measurement: 'Separate worker submissions and GPU-fence completions; not main logic ticks or physical display presentation' };
        this.firstFrame = new Promise((resolve, reject) => { this.firstResolve = resolve; this.firstReject = reject; });
        this.firstFrame.catch(() => {});
        worker.onmessage = event => this.receive(event.data);
        worker.onerror = event => this.fail(new Error(event.message ?? 'Render worker failed.'));
        worker.onmessageerror = () => this.fail(new Error('Render worker message could not be decoded.'));
    }

    initialize(canvas, options) {
        const rendererOptions = { forceWebGL: options.forceWebGL === true, profile: options.profile === true,
            gpuTiming: options.gpuTiming === true, textureArrays: options.textureArrays === true,
            cutoutArrays: options.cutoutArrays === true,
            internalPost: internalPostMode(options.internalPost),
            fusedSharpen: internalPostMode(options.fusedSharpen),
            stageSkinCulling: options.stageSkinCulling === true,
            zeroPointLights: zeroPointLightMode(options.zeroPointLights),
            renderBundles: options.renderBundles === true, staticArrayObservers: options.staticArrayObservers === true };
        return this.request('init', { canvas, options: rendererOptions, workerInit: options.workerInit ?? {} },
            [canvas, ...(options.workerInitTransferList ?? [])]);
    }

    request(type, data = {}, transfer = []) {
        if (this.failed) return Promise.reject(this.failed);
        if (this.disposed || (this.disposing && type !== 'dispose')) return Promise.reject(new Error('Render worker is disposed.'));
        const requestId = ++this.requestId;
        return new Promise((resolve, reject) => {
            this.requests.set(requestId, { resolve, reject });
            try { this.worker.postMessage({ type, requestId, ...data }, transfer); }
            catch (error) { this.requests.delete(requestId); reject(error); this.fail(error); }
        });
    }

    receive(message) {
        if (this.disposed || this.failed) { message.bitmap?.close?.(); return; }
        if (message.type === 'fatal') { this.fail(new Error(message.error)); return; }
        if (message.type === 'graphics-error') { this.reportError(message.error); return; }
        if (message.snapshot) this.applySnapshot(message.snapshot);
        if (this.failed) { message.bitmap?.close?.(); return; }
        if (message.type === 'submitted') {
            const stats = this.workerStats;
            stats.submittedFrames++;
            stats.lastSubmittedFrameId = Math.max(stats.lastSubmittedFrameId, message.frameId);
            stats.firstSubmittedAtMs ??= message.submittedAtMs;
            stats.lastSubmittedAtMs = message.submittedAtMs;
            const elapsed = stats.lastSubmittedAtMs - stats.firstSubmittedAtMs;
            stats.submissionFps = elapsed > 0 ? (stats.submittedFrames - 1) * 1000 / elapsed : 0;
            this.options.onFrameSubmitted?.(message);
            return;
        }
        if (message.type === 'frame') {
            const writer = this.slots[message.slot];
            if (!writer || this.slotFrames[message.slot] !== message.frameId) { this.fail(new Error('Unexpected render worker frame slot.')); return; }
            this.slotFrames[message.slot] = 0;
            writer.reset(message.buffer);
            this.free.push(message.slot);
            const stats = this.workerStats;
            stats.inFlightFrames--;
            stats.completedFrames++;
            stats.lastFrameId = Math.max(stats.lastFrameId, message.frameId);
            for (let index = this.submissionWaiters.length - 1; index >= 0; index--) {
                const waiter = this.submissionWaiters[index];
                if (waiter.frameId > message.frameId) continue;
                this.submissionWaiters.splice(index, 1);
                waiter.resolve();
            }
            stats.firstCompletedAtMs ??= message.completedAtMs;
            stats.lastCompletedAtMs = Math.max(stats.lastCompletedAtMs ?? 0, message.completedAtMs);
            const elapsed = stats.lastCompletedAtMs - stats.firstCompletedAtMs;
            stats.completionFps = elapsed > 0 ? (stats.completedFrames - 1) * 1000 / elapsed : 0;
            if (message.captureRequestId) {
                this.capturePendingId = 0;
                const capture = this.requests.get(message.captureRequestId);
                if (capture) { this.requests.delete(message.captureRequestId); capture.resolve(message.bitmap); }
                else message.bitmap?.close();
            }
            this.firstResolve(message);
            this.options.onFrameCompleted?.(message);
            for (const resolve of this.waiters.splice(0)) resolve();
            return;
        }
        const pending = this.requests.get(message.requestId);
        if (pending) {
            this.requests.delete(message.requestId);
            pending.resolve(message);
        }
    }

    applySnapshot(snapshot) {
        if (!Number.isSafeInteger(snapshot.revision) || snapshot.revision < 1) {
            this.fail(new Error('Invalid render worker snapshot revision.'));
            return;
        }
        if (!Number.isSafeInteger(snapshot.effectiveFrameId) || snapshot.effectiveFrameId < 0) {
            this.fail(new Error('Invalid render worker effective-frame revision.'));
            return;
        }
        if (snapshot.effectiveFrameId > (this.effectiveFrameId ?? -1)) {
            this.effectiveFrameId = snapshot.effectiveFrameId;
            this.committedEffective = snapshot.capabilities.effective;
            if (this.capabilities) this.capabilities.effective = this.committedEffective;
        }
        if (snapshot.revision <= (this.snapshotRevision ?? 0)) return;
        this.snapshotRevision = snapshot.revision;
        this.adapterInfo = snapshot.adapterInfo;
        this.capabilities = snapshot.capabilities;
        this.internalPostRequested = this.capabilities.internalPost === true;
        this.capabilities.effective = this.committedEffective ?? null;
        if (Object.hasOwn(snapshot, 'profile')) {
            this.profile = snapshot.profile;
            if (this.profile && this.workerTiming) {
                const timing = this.workerTiming;
                this.profile.workerTiming ??= {};
                this.profile.workerTiming.proxy = { slotWaitMs: { count: timing.count, mean: timing.count ? timing.total / timing.count : 0, max: timing.max },
                    inFlightFrames: this.workerStats.inFlightFrames, peakInFlightFrames: timing.peak, frameSlots: this.frameSlots };
                timing.count = timing.total = timing.max = 0;
                timing.epoch++;
                timing.peak = this.workerStats.inFlightFrames;
            }
        }
        this.textureArraysRequested = snapshot.textureArraysRequested;
        this.textureArrayLayers = snapshot.textureArrayLayers;
        this.textureArraysEnabled = snapshot.textureArraysEnabled;
        this.canvas.dataset.renderer = snapshot.capabilities.backend;
        for (const error of snapshot.errors ?? []) this.reportError(error);
    }

    reportError(error) {
        const message = String(error).slice(0, 512);
        if (this.errors.size >= 40 || this.errors.has(message)) return;
        this.errors.add(message);
        this.canvas.dataset.rendererErrors = JSON.stringify([...this.errors]);
        this.canvas.dataset.webgpuErrors = this.canvas.dataset.rendererErrors;
        this.options.onError?.(new Error(message));
    }

    fail(error) {
        if (this.failed || this.disposed) return;
        this.failed = error instanceof Error ? error : new Error(String(error));
        this.reportError(this.failed.message);
        this.canvas.dataset.rendererState = 'lost';
        this.firstReject(this.failed);
        for (const pending of this.requests.values()) pending.reject(this.failed);
        this.requests.clear();
        for (const waiter of this.submissionWaiters.splice(0)) waiter.reject(this.failed);
        for (const resolve of this.waiters.splice(0)) resolve();
    }

    get canBeginFrame() { return !this.failed && !this.disposed && !this.disposing && this.active === null && this.free.length > 0; }
    get canAdvanceFrame() { return !this.failed && !this.disposed && !this.disposing && (this.active !== null || this.free.length > 0); }
    get lastQueuedFrameId() { return this.nextFrame; }

    waitForSubmittedFrame(frameId = this.nextFrame) {
        if (this.failed) return Promise.reject(this.failed);
        if (this.disposed || this.disposing) return Promise.reject(new Error('Render worker is disposed.'));
        if (!Number.isSafeInteger(frameId) || frameId < 0 || frameId > this.nextFrame) return Promise.reject(new RangeError('Invalid queued render frame id.'));
        if (this.workerStats.lastFrameId >= frameId) return Promise.resolve();
        return new Promise((resolve, reject) => this.submissionWaiters.push({ frameId, resolve, reject }));
    }

    waitForFrameSlot() {
        if (this.failed) return Promise.reject(this.failed);
        if (this.disposed || this.disposing) return Promise.reject(new Error('Render worker is disposed.'));
        if (this.canAdvanceFrame) return Promise.resolve();
        const timing = this.workerTiming;
        const started = timing ? performance.now() : 0;
        const epoch = timing?.epoch;
        return new Promise(resolve => this.waiters.push(resolve)).then(() => {
            if (this.failed) throw this.failed;
            if (this.disposed || this.disposing) throw new Error('Render worker is disposed.');
            if (timing && timing.epoch === epoch) {
                const elapsed = performance.now() - started;
                timing.count++;
                timing.total += elapsed;
                timing.max = Math.max(timing.max, elapsed);
            }
        });
    }

    start(op) {
        if (this.failed) throw this.failed;
        if (this.disposed || this.disposing) throw new Error('Render worker is disposed.');
        this.writer = this.active === null ? new WorkerPacketWriter(new ArrayBuffer(1024)) : this.slots[this.active];
        return this.writer.begin(op);
    }

    end(at) {
        this.writer.end(at);
        if (this.active === null) this.worker.postMessage({ type: 'ops', buffer: this.writer.buffer, length: this.writer.length }, [this.writer.buffer]);
    }

    beginFrame(width, height) {
        if (this.active !== null) throw new Error('Render worker already has an active frame.');
        if (!this.canBeginFrame) throw new Error('Render worker has no free frame slot; wait before calling host.Frame.');
        this.active = this.free.pop();
        const at = this.start(WorkerOp.begin);
        this.writer.number(width); this.writer.number(height); this.end(at);
    }

    present() {
        if (this.active === null) throw new Error('Render worker present requires an active frame.');
        const at = this.start(WorkerOp.present); this.end(at);
        const slot = this.active, writer = this.writer;
        this.active = null;
        this.workerStats.inFlightFrames++;
        const frameId = ++this.nextFrame;
        this.slotFrames[slot] = frameId;
        const captureRequestId = this.captureRequestId ?? 0;
        this.captureRequestId = 0;
        const message = { type: 'frame', frameId, slot, buffer: writer.buffer, length: writer.length, captureRequestId };
        if (this.workerTiming) {
            message.queuedAtMs = performance.timeOrigin + performance.now();
            this.workerTiming.peak = Math.max(this.workerTiming.peak, this.workerStats.inFlightFrames);
        }
        try { this.worker.postMessage(message, [writer.buffer]); }
        catch (error) { this.fail(error); throw error; }
    }

    flush() { const at = this.start(WorkerOp.flush); this.end(at); }
    setRenderTarget(id = 0) { const at = this.start(WorkerOp.target); this.writer.number(id); this.end(at); }

    clear(r, g, b, a = 1, clearDepth = true, clearColor = true) {
        const at = this.start(WorkerOp.clear);
        this.writer.number(r); this.writer.number(g); this.writer.number(b); this.writer.number(a);
        this.writer.number(+clearDepth); this.writer.number(+clearColor); this.end(at);
    }

    createTexture(id, width, height, rgba = null, target = false, encoding = id < 0 ? 1 : 0, format = 0) {
        const at = this.start(WorkerOp.textureCreate);
        this.writer.number(id); this.writer.number(width); this.writer.number(height); this.writer.number(+target);
        this.writer.number(encoding); this.writer.number(format); this.writer.raw(rgba ?? empty); this.end(at);
    }

    updateTexture(id, x, y, width, height, rgba) {
        const at = this.start(WorkerOp.textureUpdate);
        this.writer.number(id); this.writer.number(x); this.writer.number(y); this.writer.number(width); this.writer.number(height);
        this.writer.raw(rgba); this.end(at);
    }

    destroyTexture(id) { const at = this.start(WorkerOp.textureDestroy); this.writer.number(id); this.end(at); }

    optional(buffer, pointer, length) { if (pointer) this.writer.memory(buffer, pointer, length); else this.writer.raw(empty); }

    drawNativeMemory(buffer, positions, uvs, colors, vertexCount, indices, indexCount, textureId, matrix, blend,
        depthTest, depthWrite, scissor, viewport, targetId, colorsAreSrgb, rawBlend, blendFactor, depthCompare = 3,
        sampler = 0, shaderKind = 1, pixelParams = 0) {
        const at = this.start(WorkerOp.native);
        this.writer.number(vertexCount); this.writer.number(indexCount); this.writer.number(textureId); this.writer.number(blend);
        this.writer.number(+depthTest); this.writer.number(+depthWrite); this.writer.number(targetId); this.writer.number(+colorsAreSrgb);
        this.writer.number(depthCompare); this.writer.number(shaderKind);
        this.writer.memory(buffer, positions, vertexCount * 12); this.writer.memory(buffer, uvs, vertexCount * 8);
        this.writer.memory(buffer, colors, vertexCount * 16); this.writer.memory(buffer, indices, indexCount * 4);
        this.writer.memory(buffer, matrix, 64); this.optional(buffer, scissor, 16); this.writer.memory(buffer, viewport, 16);
        this.optional(buffer, rawBlend, 24); this.optional(buffer, blendFactor, 16); this.optional(buffer, sampler, 16);
        this.optional(buffer, pixelParams, 32); this.end(at);
    }

    drawRainMemory(buffer, positions, shapes, vertexCount, indices, indexCount, textureId, matrix, vertexParams, pixelParams,
        kind, depthTest, depthWrite, scissor, viewport, targetId, rawBlend, blendFactor, depthCompare) {
        const at = this.start(WorkerOp.rain);
        this.writer.number(vertexCount); this.writer.number(indexCount); this.writer.number(textureId); this.writer.number(kind);
        this.writer.number(+depthTest); this.writer.number(+depthWrite); this.writer.number(targetId); this.writer.number(depthCompare);
        this.writer.memory(buffer, positions, vertexCount * 16); this.writer.memory(buffer, shapes, vertexCount * 16);
        this.writer.memory(buffer, indices, indexCount * 4); this.writer.memory(buffer, matrix, 64);
        this.writer.memory(buffer, vertexParams, 96); this.writer.memory(buffer, pixelParams, 48);
        this.optional(buffer, scissor, 16); this.optional(buffer, viewport, 16);
        this.optional(buffer, rawBlend, 24); this.optional(buffer, blendFactor, 16); this.end(at);
    }

    createRainGeometryMemory(id, buffer, positions, shapes, vertexCount, indices, indexCount) {
        const at = this.start(WorkerOp.rainGeometryCreate);
        this.writer.number(id); this.writer.number(vertexCount); this.writer.number(indexCount);
        this.writer.memory(buffer, positions, vertexCount * 16); this.writer.memory(buffer, shapes, vertexCount * 16);
        this.writer.memory(buffer, indices, indexCount * 4); this.end(at);
    }

    destroyRainGeometry(id) {
        const at = this.start(WorkerOp.rainGeometryDestroy);
        this.writer.number(id); this.end(at);
    }

    drawRainGeometryMemory(id, start, count, buffer, textureId, matrix, vertexParams, pixelParams,
        depthTest, depthWrite, scissor, viewport, targetId, rawBlend, blendFactor, depthCompare) {
        const at = this.start(WorkerOp.rainGeometryDraw);
        this.writer.number(id); this.writer.number(start); this.writer.number(count); this.writer.number(textureId);
        this.writer.number(+depthTest); this.writer.number(+depthWrite); this.writer.number(targetId); this.writer.number(depthCompare);
        this.writer.memory(buffer, matrix, 64); this.writer.memory(buffer, vertexParams, 96); this.writer.memory(buffer, pixelParams, 48);
        this.optional(buffer, scissor, 16); this.optional(buffer, viewport, 16);
        this.optional(buffer, rawBlend, 24); this.optional(buffer, blendFactor, 16); this.end(at);
    }

    getCapabilities() { return this.capabilities; }
    getProfile() { if (this.profile) this.profile.workerTransport = this.workerStats; return this.profile; }

    captureNextFrame() {
        if (this.options.profile !== true) return Promise.reject(new Error('Worker frame capture requires explicit profiling.'));
        if (this.failed) return Promise.reject(this.failed);
        if (this.disposed || this.disposing) return Promise.reject(new Error('Render worker is disposed.'));
        if (this.capturePendingId) return Promise.reject(new Error('A worker frame capture is already pending.'));
        const requestId = ++this.requestId;
        this.captureRequestId = requestId;
        this.capturePendingId = requestId;
        return new Promise((resolve, reject) => this.requests.set(requestId, { resolve, reject }));
    }

    async requestProfile() {
        if (this.active !== null) throw new Error('Request render diagnostics between application frames.');
        await this.request('profile');
        return this.getProfile();
    }

    flushResources() {
        if (this.active !== null) return Promise.reject(new Error('Flush resources between application frames.'));
        return this.request('barrier');
    }

    control(op, enabled) {
        if (this.active !== null) return Promise.reject(new Error('Apply render controls between application frames.'));
        return this.request('control', { op, enabled: !!enabled });
    }

    setTextureArraysEnabled(enabled) { return this.control(WorkerOp.textureArrays, enabled); }
    setRenderBundlesEnabled(enabled) { return this.control(WorkerOp.renderBundles, enabled); }
    setStaticArrayObserversEnabled(enabled) { return this.control(WorkerOp.staticObservers, enabled); }

    async setGpuTimingMode(mode) {
        if (this.active !== null) throw new Error('Apply render controls between application frames.');
        if (mode !== 'off' && mode !== 'sampled' && mode !== 'continuous') throw new RangeError('Unknown GPU timing mode.');
        await this.request('control', { op: WorkerOp.gpuTiming, mode });
        return this.profile?.gpu?.mode ?? 'off';
    }

    async setDynamicResolutionScale(scale, shadowHz = 0) {
        validateAdaptiveBudget(scale, shadowHz);
        if (this.active !== null) throw new Error('Apply resolution changes between application frames.');
        await this.request('control', { op: WorkerOp.dynamicResolution, scale, shadowHz });
    }

    async setInternalPostEnabled(enabled) {
        if (typeof enabled !== 'boolean') throw new TypeError('Internal post requires a boolean.');
        if (enabled && !this.internalPostRequested) throw new Error('Internal post requires an enabled renderer configuration.');
        if (this.active !== null) throw new Error('Apply internal post changes between application frames.');
        await this.request('control', { op: WorkerOp.internalPost, enabled });
    }

    async setZeroPointLightsEnabled(enabled) {
        if (typeof enabled !== 'boolean') throw new TypeError('Point-light optimization requires a boolean.');
        if (this.active !== null) throw new Error('Apply light changes between application frames.');
        await this.request('control', { op: WorkerOp.zeroPointLights, enabled });
    }

    async dispose() {
        if (this.disposed) return;
        if (this.disposing) return this.disposeRequest;
        if (this.active !== null && !this.failed) this.present();
        this.disposing = true;
        this.disposeRequest = this.failed ? Promise.resolve() : this.request('dispose');
        try { await this.disposeRequest; }
        finally {
            this.disposed = true;
            this.worker.terminate();
            const error = new Error('Render worker is disposed.');
            if (this.workerStats.completedFrames === 0) this.firstReject(error);
            for (const pending of this.requests.values()) pending.reject(new Error('Render worker is disposed.'));
            this.requests.clear();
            for (const waiter of this.submissionWaiters.splice(0)) waiter.reject(error);
            for (const resolve of this.waiters.splice(0)) resolve();
        }
    }
}

export function createWorkerPackedImports(proxy) {
    const scalar = (op, id) => { const at = proxy.start(op); proxy.writer.number(id); proxy.end(at); };
    const pair = (op, scene, id) => { const at = proxy.start(op); proxy.writer.number(scene); proxy.writer.number(id); proxy.end(at); };
    return {
        createScene(id) { scalar(WorkerOp.sceneCreate, id); },
        configureScene(id, json) { const at = proxy.start(WorkerOp.sceneConfigure); proxy.writer.number(id); proxy.writer.text(json); proxy.end(at); },
        createSceneTexture(id, textureId, width, height, rgba) {
            const at = proxy.start(WorkerOp.sceneTexture);
            proxy.writer.number(id); proxy.writer.number(textureId); proxy.writer.number(width); proxy.writer.number(height);
            proxy.writer.raw(rgba); proxy.end(at);
        },
        createMesh(scene, id, json, vertices, indices) {
            const at = proxy.start(WorkerOp.meshCreate); proxy.writer.number(scene); proxy.writer.number(id);
            proxy.writer.text(json); proxy.writer.raw(vertices); proxy.writer.raw(indices); proxy.end(at);
        },
        removeMesh(scene, id) { pair(WorkerOp.meshRemove, scene, id); },
        createStage(scene, id, ids) {
            const at = proxy.start(WorkerOp.stageCreate); proxy.writer.number(scene); proxy.writer.number(id); proxy.writer.numbers(ids); proxy.end(at);
        },
        removeStage(scene, id) { pair(WorkerOp.stageRemove, scene, id); },
        setSkyLayers(scene, current, previous, opacity, height, brightness) {
            const at = proxy.start(WorkerOp.skyLayers); proxy.writer.number(scene); proxy.writer.numbers(current); proxy.writer.numbers(previous);
            proxy.writer.number(opacity); proxy.writer.number(height); proxy.writer.number(brightness); proxy.end(at);
        },
        setSkyParameters(scene, opacity, height, brightness) {
            const at = proxy.start(WorkerOp.skyParameters); proxy.writer.number(scene); proxy.writer.number(opacity);
            proxy.writer.number(height); proxy.writer.number(brightness); proxy.end(at);
        },
        createActor(scene, id, ids, bones) {
            const at = proxy.start(WorkerOp.actorCreate); proxy.writer.number(scene); proxy.writer.number(id);
            proxy.writer.numbers(ids); proxy.writer.number(bones); proxy.end(at);
        },
        removeActor(scene, id) { pair(WorkerOp.actorRemove, scene, id); },
        flushScenePacked(id, meta, states, bones, meshMeta, meshBones) {
            const at = proxy.start(WorkerOp.sceneFlush); proxy.writer.number(id);
            proxy.writer.raw(meta); proxy.writer.raw(states); proxy.writer.raw(bones); proxy.writer.raw(meshMeta); proxy.writer.raw(meshBones); proxy.end(at);
        },
        drawFramePacked(id, frame, meta, states, bones, meshMeta, meshBones) {
            const at = proxy.start(WorkerOp.sceneDraw); proxy.writer.number(id);
            proxy.writer.raw(frame); proxy.writer.raw(meta); proxy.writer.raw(states); proxy.writer.raw(bones);
            proxy.writer.raw(meshMeta); proxy.writer.raw(meshBones); proxy.end(at);
        },
        drawPortrait(id, ids, bytes) {
            const at = proxy.start(WorkerOp.portrait); proxy.writer.number(id); proxy.writer.numbers(ids); proxy.writer.raw(bytes); proxy.end(at);
        },
        composeScene(id) { scalar(WorkerOp.compose, id); },
        disposeScene(id) { scalar(WorkerOp.sceneDispose, id); }
    };
}
