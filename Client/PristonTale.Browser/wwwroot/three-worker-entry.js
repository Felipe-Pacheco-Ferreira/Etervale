import { createThreeGraphics } from './three-scene.js';
import { createPackedImports } from './three-bridge.js';
import { WorkerOp, WorkerPacketReader } from './three-worker-protocol.js';
import { createGpuCompletionFence } from './three-worker-completion.js';

function native(graphics, reader) {
    const vertices = reader.number(), indices = reader.number(), texture = reader.number(), blend = reader.number();
    const depthTest = !!reader.number(), depthWrite = !!reader.number(), target = reader.number(), srgb = !!reader.number();
    const compare = reader.number(), kind = reader.number();
    const positions = reader.memoryRange(vertices * 12), uv = reader.memoryRange(vertices * 8), colors = reader.memoryRange(vertices * 16);
    const index = reader.memoryRange(indices * 4), matrix = reader.memoryRange(64), scissor = reader.memoryRange(16, true);
    const viewport = reader.memoryRange(16), blendState = reader.memoryRange(24, true), factor = reader.memoryRange(16, true);
    const sampler = reader.memoryRange(16, true), pixel = reader.memoryRange(32, true);
    graphics.drawNativeMemory(reader.buffer, positions, uv, colors, vertices, index, indices, texture, matrix, blend,
        depthTest, depthWrite, scissor, viewport, target, srgb, blendState, factor, compare, sampler, kind, pixel);
}

function rain(graphics, reader) {
    const vertices = reader.number(), indices = reader.number(), texture = reader.number(), kind = reader.number();
    const depthTest = !!reader.number(), depthWrite = !!reader.number(), target = reader.number(), compare = reader.number();
    const positions = reader.memoryRange(vertices * 16), shapes = reader.memoryRange(vertices * 16), index = reader.memoryRange(indices * 4);
    const matrix = reader.memoryRange(64), vertex = reader.memoryRange(96), pixel = reader.memoryRange(48);
    const scissor = reader.memoryRange(16, true), viewport = reader.memoryRange(16, true);
    const blend = reader.memoryRange(24, true), factor = reader.memoryRange(16, true);
    graphics.drawRainMemory(reader.buffer, positions, shapes, vertices, index, indices, texture, matrix, vertex, pixel,
        kind, depthTest, depthWrite, scissor, viewport, target, blend, factor, compare);
}

function rainGeometry(graphics, reader) {
    const id = reader.number(), start = reader.number(), count = reader.number(), texture = reader.number();
    const depthTest = !!reader.number(), depthWrite = !!reader.number(), target = reader.number(), compare = reader.number();
    const matrix = reader.memoryRange(64), vertex = reader.memoryRange(96), pixel = reader.memoryRange(48);
    const scissor = reader.memoryRange(16, true), viewport = reader.memoryRange(16, true);
    const blend = reader.memoryRange(24, true), factor = reader.memoryRange(16, true);
    graphics.drawRainGeometryMemory(id, start, count, reader.buffer, texture, matrix, vertex, pixel,
        depthTest, depthWrite, scissor, viewport, target, blend, factor, compare);
}

export function executeWorkerPacket(graphics, imports, buffer, length, changes = null) {
    const reader = new WorkerPacketReader(buffer, length);
    let presents = 0;
    for (let op; (op = reader.next()) !== 0;) {
        switch (op) {
            case WorkerOp.begin: graphics.beginFrame(reader.number(), reader.number()); break;
            case WorkerOp.present: graphics.present(); presents++; break;
            case WorkerOp.flush: graphics.flush(); break;
            case WorkerOp.clear: graphics.clear(reader.number(), reader.number(), reader.number(), reader.number(), !!reader.number(), !!reader.number()); break;
            case WorkerOp.target: graphics.setRenderTarget(reader.number()); break;
            case WorkerOp.textureCreate: {
                const id = reader.number(), width = reader.number(), height = reader.number(), target = !!reader.number();
                const encoding = reader.number(), format = reader.number(), bytes = reader.raw();
                graphics.createTexture(id, width, height, bytes.length ? bytes : null, target, encoding, format);
                break;
            }
            case WorkerOp.textureUpdate: graphics.updateTexture(reader.number(), reader.number(), reader.number(), reader.number(), reader.number(), reader.raw()); break;
            case WorkerOp.textureDestroy: graphics.destroyTexture(reader.number()); break;
            case WorkerOp.native: native(graphics, reader); break;
            case WorkerOp.rain: rain(graphics, reader); break;
            case WorkerOp.rainGeometryCreate: {
                const id = reader.number(), vertices = reader.number(), indices = reader.number();
                const positions = reader.memoryRange(vertices * 16), shapes = reader.memoryRange(vertices * 16), index = reader.memoryRange(indices * 4);
                graphics.createRainGeometryMemory(id, reader.buffer, positions, shapes, vertices, index, indices);
                break;
            }
            case WorkerOp.rainGeometryDraw: rainGeometry(graphics, reader); break;
            case WorkerOp.rainGeometryDestroy: graphics.destroyRainGeometry(reader.number()); break;
            case WorkerOp.sceneCreate: imports.createScene(reader.number()); break;
            case WorkerOp.sceneConfigure:
                imports.configureScene(reader.number(), reader.text());
                if (changes) changes.metadataChanged = true;
                break;
            case WorkerOp.sceneTexture: imports.createSceneTexture(reader.number(), reader.number(), reader.number(), reader.number(), reader.raw()); break;
            case WorkerOp.meshCreate: imports.createMesh(reader.number(), reader.number(), reader.text(), reader.raw(), reader.raw()); break;
            case WorkerOp.meshRemove: imports.removeMesh(reader.number(), reader.number()); break;
            case WorkerOp.stageCreate: imports.createStage(reader.number(), reader.number(), reader.numbers()); break;
            case WorkerOp.stageRemove: imports.removeStage(reader.number(), reader.number()); break;
            case WorkerOp.skyLayers: imports.setSkyLayers(reader.number(), reader.numbers(), reader.numbers(), reader.number(), reader.number(), reader.number()); break;
            case WorkerOp.skyParameters: imports.setSkyParameters(reader.number(), reader.number(), reader.number(), reader.number()); break;
            case WorkerOp.actorCreate: imports.createActor(reader.number(), reader.number(), reader.numbers(), reader.number()); break;
            case WorkerOp.actorRemove: imports.removeActor(reader.number(), reader.number()); break;
            case WorkerOp.sceneFlush: imports.flushScenePacked(reader.number(), reader.raw(), reader.raw(), reader.raw(), reader.raw(), reader.raw()); break;
            case WorkerOp.sceneDraw: imports.drawFramePacked(reader.number(), reader.raw(), reader.raw(), reader.raw(), reader.raw(), reader.raw(), reader.raw()); break;
            case WorkerOp.portrait: imports.drawPortrait(reader.number(), reader.numbers(), reader.raw()); break;
            case WorkerOp.compose: imports.composeScene(reader.number()); break;
            case WorkerOp.sceneDispose: imports.disposeScene(reader.number()); break;
            default: throw new Error(`Unknown render worker operation ${op}.`);
        }
    }
    return presents;
}

function metadata(graphics, includeProfile, effective, effectiveFrameId) {
    const adapter = graphics.adapterInfo ?? {};
    const snapshot = { adapterInfo: { vendor: adapter.vendor ?? '', architecture: adapter.architecture ?? '',
        device: adapter.device ?? '', description: adapter.description ?? '' }, capabilities: graphics.getCapabilities(),
        textureArraysRequested: graphics.textureArraysRequested, textureArrayLayers: graphics.textureArrayLayers,
        textureArraysEnabled: graphics.textureArraysEnabled, errors: [...graphics.errors] };
    snapshot.capabilities.effective = effective;
    snapshot.effectiveFrameId = effectiveFrameId;
    if (includeProfile) {
        snapshot.profile = graphics.getProfile();
        if (snapshot.profile?.internalPost) {
            snapshot.profile.internalPost.width = effective?.postProcessingWidth ?? null;
            snapshot.profile.internalPost.height = effective?.postProcessingHeight ?? null;
        }
        if (snapshot.profile?.fusedSharpen) snapshot.profile.fusedSharpen.enabled = effective?.fusedSharpen ?? false;
    }
    return snapshot;
}

const timingMetric = () => ({ count: 0, total: 0, max: 0 });
function recordTiming(metric, value) {
    if (!Number.isFinite(value) || value < 0) return;
    metric.count++;
    metric.total += value;
    metric.max = Math.max(metric.max, value);
}
function takeTiming(metric) {
    const result = { count: metric.count, mean: metric.count ? metric.total / metric.count : 0, max: metric.max };
    metric.count = metric.total = metric.max = 0;
    return result;
}

export function createWorkerMessageHandler(send, createGraphics = createThreeGraphics, captureCanvas = canvas => createImageBitmap(canvas),
    createCompletion = graphics => createGpuCompletionFence(graphics.renderer), timingNow = () => performance.now()) {
    let graphics, imports, lookups, completion, failed, timing, closed = false, profiling = false;
    const changes = { metadataChanged: false };
    let effectiveDirty = true, effectiveSource, latestRenderedEffective = null, committedEffective = null, effectiveFrameId = 0;
    const pendingFrames = new Set();
    const closeLookups = () => {
        lookups?.area?.close?.();
        if (lookups?.search !== lookups?.area) lookups?.search?.close?.();
        if (globalThis.__noxiusWorkerSmaaLookups === lookups) delete globalThis.__noxiusWorkerSmaaLookups;
        lookups = null;
    };
    let submittedFrames = 0, completedFrames = 0, intervalFrames = 0, intervalCompletedFrames = 0, intervalStarted = performance.now();
    let snapshotRevision = 0;
    const snapshot = includeProfile => {
        const value = metadata(graphics, includeProfile, committedEffective, effectiveFrameId);
        value.revision = ++snapshotRevision;
        if (value.profile) {
            const now = performance.now(), elapsedMs = now - intervalStarted;
            value.profile.worker = { submittedFrames, completedFrames, intervalFrames, intervalCompletedFrames, elapsedMs,
                submissionFps: elapsedMs > 0 ? intervalFrames * 1000 / elapsedMs : 0,
                completionFps: elapsedMs > 0 ? intervalCompletedFrames * 1000 / elapsedMs : 0,
                measurement: 'Worker submission and GPU-fence completion counts; not main logic ticks or physical display presentation' };
            intervalFrames = 0;
            intervalCompletedFrames = 0;
            intervalStarted = now;
            if (timing) {
                value.profile.workerTiming = { packetCpuMs: takeTiming(timing.cpu), queuedToWorkerStartMs: takeTiming(timing.queue),
                    submitToGpuCompletionMs: takeTiming(timing.gpu), pendingCompletionAcks: pendingFrames.size,
                    peakPendingCompletionAcks: timing.peak,
                    measurement: 'Packet CPU includes decode/prepare/render; paired GPU wait includes queue execution and completion notification, not shader-only time' };
                timing.epoch++;
                timing.peak = pendingFrames.size;
            }
        }
        return value;
    };
    const fail = error => {
        if (closed || failed) return;
        failed = error instanceof Error ? error : new Error(String(error));
        completion?.dispose();
        send({ type: 'fatal', error: String(failed.stack ?? failed).slice(0, 2048) });
    };
    return async message => {
        if (closed) throw new Error('Render worker is disposed.');
        if (failed && message.type !== 'dispose') return;
        if (message.type === 'init') {
            if (graphics) throw new Error('Render worker is already initialized.');
            message.canvas.dataset = {};
            profiling = message.options?.profile === true;
            if (profiling) timing = { cpu: timingMetric(), queue: timingMetric(), gpu: timingMetric(), epoch: 0, peak: 0 };
            lookups = message.workerInit?.smaaLookups;
            if (lookups) globalThis.__noxiusWorkerSmaaLookups = lookups;
            try {
                graphics = await createGraphics(message.canvas, { ...message.options, ...message.workerInit,
                    onError: error => send({ type: 'graphics-error', error: String(error.message ?? error).slice(0, 512) }) });
                completion = createCompletion(graphics);
            } catch (error) {
                try { await graphics?.dispose(); } finally { closeLookups(); }
                throw error;
            }
            imports = createPackedImports(graphics);
            intervalStarted = performance.now();
            send({ type: 'ready', requestId: message.requestId, snapshot: snapshot(true) });
            return;
        }
        if (!graphics) throw new Error('Render worker is not initialized.');
        if (message.type === 'ops' || message.type === 'frame') {
            changes.metadataChanged = false;
            const timedFrame = timing && message.type === 'frame';
            const started = timedFrame ? timingNow() : 0;
            const timingEpoch = timedFrame ? timing.epoch : 0;
            const presents = executeWorkerPacket(graphics, imports, message.buffer, message.length, changes);
            if (changes.metadataChanged) effectiveDirty = true;
            if (timedFrame) {
                recordTiming(timing.cpu, timingNow() - started);
                if (Number.isFinite(message.queuedAtMs)) recordTiming(timing.queue, Math.max(0, performance.timeOrigin + started - message.queuedAtMs));
            }
            if (message.type === 'frame') {
                if (presents !== 1) throw new Error('Render worker frame must submit exactly once.');
                submittedFrames++;
                intervalFrames++;
                const submittedAtMs = performance.timeOrigin + performance.now();
                let bitmap, abandoned = false;
                let capture = null;
                if (message.captureRequestId) {
                    if (!profiling) throw new Error('Worker frame capture requires explicit profiling.');
                    // Start the snapshot before any yield; WebGL may discard its non-preserved drawing buffer afterward.
                    capture = captureCanvas(graphics.canvas);
                }
                const post = graphics.post;
                if (post?.effective && (post.effective !== effectiveSource || post.target?.width !== latestRenderedEffective?.renderWidth
                    || post.target?.height !== latestRenderedEffective?.renderHeight
                    || post.effective.dynamicShadowUpdateRate !== latestRenderedEffective?.dynamicShadowUpdateRate
                    || post.effective.internalPost !== latestRenderedEffective?.internalPost
                    || post.effective.fusedSharpen !== latestRenderedEffective?.fusedSharpen
                    || post.effective.postProcessingWidth !== latestRenderedEffective?.postProcessingWidth
                    || post.effective.postProcessingHeight !== latestRenderedEffective?.postProcessingHeight)) effectiveDirty = true;
                if (effectiveDirty) {
                    const source = post ? post.effective : graphics.getCapabilities().effective;
                    if (source) {
                        latestRenderedEffective = { ...source };
                        if (post?.target) {
                            latestRenderedEffective.renderWidth = post.target.width;
                            latestRenderedEffective.renderHeight = post.target.height;
                        }
                        effectiveSource = source;
                        effectiveDirty = false;
                    }
                }
                const frameEffective = latestRenderedEffective;
                const publishMetadata = changes.metadataChanged;
                let effectiveChanged = false;
                const captured = Promise.resolve(capture).then(value => {
                    if (abandoned) { value?.close(); return null; }
                    bitmap = value;
                    return value;
                });
                let gpuDone;
                try { gpuDone = completion.afterSubmit(); } catch (error) { gpuDone = Promise.reject(error); }
                const completed = Promise.resolve(gpuDone).then(completedAtMs => {
                    const finished = completedAtMs ?? performance.timeOrigin + performance.now();
                    if (!closed && !failed) {
                        completedFrames++; intervalCompletedFrames++;
                        if (timedFrame && timing.epoch === timingEpoch) recordTiming(timing.gpu, finished - submittedAtMs);
                        if (frameEffective && message.frameId > effectiveFrameId) {
                            effectiveChanged = committedEffective !== frameEffective;
                            committedEffective = frameEffective;
                            effectiveFrameId = message.frameId;
                        }
                    }
                    return finished;
                });
                send({ type: 'submitted', frameId: message.frameId, submittedAtMs });
                const ready = Promise.all([completed, captured]).then(([completedAtMs]) => {
                    if (closed || failed) { bitmap?.close(); return; }
                    send({ type: 'frame', frameId: message.frameId, slot: message.slot, buffer: message.buffer,
                        submittedAtMs, completedAtMs, stats: graphics.stats, captureRequestId: message.captureRequestId, bitmap,
                        snapshot: publishMetadata || effectiveChanged ? snapshot(false) : undefined }, bitmap ? [message.buffer, bitmap] : [message.buffer]);
                }).catch(error => {
                    abandoned = true;
                    bitmap?.close();
                    fail(error);
                });
                pendingFrames.add(ready);
                if (timedFrame) timing.peak = Math.max(timing.peak, pendingFrames.size);
                ready.then(() => pendingFrames.delete(ready));
            } else if (presents !== 0) throw new Error('Resource packet cannot present an application frame.');
            else if (changes.metadataChanged) send({ type: 'metadata', snapshot: snapshot(false) });
            return;
        }
        if (message.type === 'dispose') {
            closed = true;
            completion.dispose();
            await Promise.allSettled(pendingFrames);
            try { await graphics.dispose(); }
            finally { closeLookups(); closed = true; }
            send({ type: 'disposed', requestId: message.requestId });
            return;
        }
        if (message.type === 'control') {
            switch (message.op) {
                case WorkerOp.textureArrays: graphics.setTextureArraysEnabled(message.enabled); break;
                case WorkerOp.renderBundles: graphics.setRenderBundlesEnabled(message.enabled); break;
                case WorkerOp.staticObservers: graphics.setStaticArrayObserversEnabled(message.enabled); break;
                case WorkerOp.gpuTiming: graphics.setGpuTimingMode(message.mode); break;
                case WorkerOp.zeroPointLights: graphics.setZeroPointLightsEnabled(message.enabled); break;
                case WorkerOp.internalPost:
                    graphics.setInternalPostEnabled(message.enabled);
                    effectiveDirty = true;
                    break;
                case WorkerOp.dynamicResolution:
                    graphics.setDynamicResolutionScale(message.scale, message.shadowHz);
                    effectiveDirty = true;
                    break;
                default: throw new Error('Unknown render worker control.');
            }
        } else if (message.type !== 'profile' && message.type !== 'barrier') throw new Error('Unknown render worker message.');
        send({ type: 'ack', requestId: message.requestId,
            snapshot: snapshot(message.type !== 'barrier' && !(message.type === 'control' && message.op === WorkerOp.dynamicResolution)) });
    };
}

if (typeof self !== 'undefined' && typeof document === 'undefined') {
    const send = (message, transfer = []) => self.postMessage(message, transfer);
    const handle = createWorkerMessageHandler(send);
    let sequence = Promise.resolve();
    let broken = false;
    const fatal = error => {
        if (broken) return;
        broken = true;
        send({ type: 'fatal', error: String(error?.stack ?? error).slice(0, 2048) });
    };
    self.onmessage = event => { sequence = sequence.then(() => { if (!broken) return handle(event.data); }).catch(fatal); };
    self.addEventListener('error', event => fatal(event.error ?? event.message));
    self.addEventListener('unhandledrejection', event => fatal(event.reason));
}
