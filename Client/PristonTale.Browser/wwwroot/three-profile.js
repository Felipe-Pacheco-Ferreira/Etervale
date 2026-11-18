export const ThreePhase = Object.freeze({ scene: 0, portrait: 1, native: 2, post: 3 });

const phaseNames = ['scene', 'portrait', 'native', 'post'];
const capacity = 600;
const phaseCount = phaseNames.length;
const metric = Object.freeze({ renderCalls: 8, drawCalls: 9, triangles: 10, outputBlits: 11, quadCalls: 12, nativeBatches: 13 });
const metricCount = 14;
const gpuSampleInterval = 10;
const wallClock = () => performance.now();
const compareNumbers = (a, b) => a - b;

export function createThreeProfiler(renderer, enabled = false, clock = wallClock, gpuTiming = false) {
    return enabled ? new ThreeProfiler(renderer, clock, gpuTiming) : null;
}

function summary(source, count) {
    const values = Array.from(source.subarray(0, count));
    values.sort(compareNumbers);
    let total = 0;
    for (const value of values) total += value;
    const round = value => Math.round(value * 1000) / 1000;
    return { p50: round(values[Math.floor((count - 1) * 0.5)] ?? 0), p95: round(values[Math.ceil((count - 1) * 0.95)] ?? 0),
        max: round(values[count - 1] ?? 0), mean: round(count ? total / count : 0) };
}

class GpuRenderTiming {
    constructor(renderer) {
        this.renderer = renderer;
        this.supported = false;
        this.status = 'unsupported';
        this.pending = false;
        this.disposed = false;
        this.samples = 0;
        this.totalSamples = 0;
        this.index = 0;
        this.generation = 0;
        this.lastMs = null;
        this.lastRequestedAfterAppFrame = null;
        this.lastResolvedAtAppFrame = null;
        this.error = null;
        this.originalTrackTimestamp = renderer.backend?.trackTimestamp;
        this.sampledAppFrames = 0;
        this.totalSampledAppFrames = 0;
        this.trackedAppFrames = 0;
        this.totalTrackedAppFrames = 0;
        try {
            this.supported = renderer.backend?.trackTimestamp === true
                && typeof renderer.resolveTimestampsAsync === 'function' && renderer.hasFeature?.('timestamp-query') === true;
        } catch {
            this.supported = false;
        }
        if (this.supported) {
            this.status = 'collecting';
            this.ring = new Float64Array(capacity);
            renderer.backend.trackTimestamp = false;
        }
        this.enabled = this.supported;
        this.mode = this.supported ? 'sampled' : 'off';
        this.onResolved = value => this.complete(value);
        this.onRejected = error => this.fail(error);
    }

    beginFrame(appFrame) {
        this.appFrame = appFrame;
        this.trackedFrame = this.supported && this.enabled && !this.disposed && this.status !== 'failed'
            && (this.mode === 'continuous' || (!this.pending && appFrame % gpuSampleInterval === 0));
        this.sampledFrame = this.trackedFrame && !this.pending && appFrame % gpuSampleInterval === 0;
        if (this.supported && !this.disposed) this.renderer.backend.trackTimestamp = this.trackedFrame;
    }

    endFrame(appFrame) {
        this.appFrame = appFrame;
        if (this.trackedFrame) { this.trackedAppFrames++; this.totalTrackedAppFrames++; }
        if (!this.sampledFrame) {
            if (this.supported && !this.disposed) this.renderer.backend.trackTimestamp = false;
            return;
        }
        this.sampledFrame = false;
        this.sampledAppFrames++;
        this.totalSampledAppFrames++;
        this.pending = true;
        this.requestGeneration = this.generation;
        this.requestAppFrame = appFrame;
        try {
            // The public resolver checks trackTimestamp synchronously before its first asynchronous wait.
            Promise.resolve(this.renderer.resolveTimestampsAsync('render')).then(this.onResolved, this.onRejected);
        } catch (error) {
            this.fail(error);
        } finally {
            this.renderer.backend.trackTimestamp = false;
        }
    }

    complete(value) {
        this.pending = false;
        if (this.disposed || value === undefined) return;
        if (!Number.isFinite(value) || value < 0) {
            this.fail(new Error('Invalid GPU render timestamp duration.'));
            return;
        }
        this.totalSamples++;
        if (this.requestGeneration !== this.generation || !this.enabled) return;
        this.status = 'ready';
        this.lastMs = value;
        this.lastRequestedAfterAppFrame = this.requestAppFrame;
        this.lastResolvedAtAppFrame = this.appFrame;
        this.ring[this.index] = value;
        this.index = (this.index + 1) % capacity;
        this.samples = Math.min(this.samples + 1, capacity);
    }

    fail(error) {
        this.pending = false;
        if (this.disposed || this.requestGeneration !== this.generation || !this.enabled) return;
        this.status = 'failed';
        this.enabled = false;
        this.mode = 'off';
        this.error = String(error?.message ?? error).slice(0, 160);
        if (this.supported) this.renderer.backend.trackTimestamp = false;
    }

    setMode(mode) {
        if (mode !== 'off' && mode !== 'sampled' && mode !== 'continuous') throw new RangeError('Unknown GPU timing mode.');
        if (!this.supported || this.disposed || this.status === 'failed') return 'off';
        if (mode === this.mode) return this.mode;
        this.mode = mode;
        this.enabled = mode !== 'off';
        this.sampledFrame = false;
        this.renderer.backend.trackTimestamp = false;
        this.generation++;
        this.index = this.samples = this.sampledAppFrames = this.trackedAppFrames = 0;
        this.lastMs = this.lastRequestedAfterAppFrame = this.lastResolvedAtAppFrame = null;
        this.status = this.enabled ? 'collecting' : 'disabled';
        return this.mode;
    }

    snapshot(reset) {
        const result = { requested: true, supported: this.supported, enabled: this.enabled, mode: this.mode, status: this.status, pending: this.pending,
            measurement: 'Asynchronous GPU render-pass duration sum for the last resolved backend frame; not CPU time or an interval average',
            sampleIntervalAppFrames: gpuSampleInterval, samples: this.samples, totalSamples: this.totalSamples,
            sampledAppFrames: this.sampledAppFrames, totalSampledAppFrames: this.totalSampledAppFrames,
            trackedAppFrames: this.trackedAppFrames, totalTrackedAppFrames: this.totalTrackedAppFrames,
            renderMs: this.ring ? summary(this.ring, this.samples) : null, lastMs: this.lastMs,
            lastRequestedAfterAppFrame: this.lastRequestedAfterAppFrame, lastResolvedAtAppFrame: this.lastResolvedAtAppFrame,
            error: this.error };
        if (reset) {
            this.index = 0;
            this.samples = 0;
            this.sampledAppFrames = 0;
            this.trackedAppFrames = 0;
            this.generation++;
        }
        return result;
    }

    dispose() {
        this.disposed = true;
        this.status = 'disposed';
        if (this.supported) this.renderer.backend.trackTimestamp = this.originalTrackTimestamp;
    }
}

class ThreeProfiler {
    constructor(renderer, clock, gpuTiming) {
        if (!renderer.info?.render || typeof renderer.info.reset !== 'function') throw new TypeError('Three profiling requires renderer.info.');
        this.renderer = renderer;
        this.clock = clock;
        this.frame = new Float64Array(metricCount);
        this.ring = new Array(metricCount);
        for (let index = 0; index < metricCount; index++) this.ring[index] = new Float64Array(capacity);
        this.index = 0;
        this.samples = 0;
        this.totalFrames = 0;
        this.running = false;
        this.gpu = gpuTiming === true ? new GpuRenderTiming(renderer) : null;
        this.autoReset = renderer.info.autoReset;
        renderer.info.autoReset = false;
        this.originalRender = renderer.render;
        const profiler = this;
        this.renderWrapper = function (scene, camera) {
            if (profiler.running && scene?.isQuadMesh) {
                profiler.frame[metric.quadCalls]++;
                if (scene.name === 'Output Color Transform') profiler.frame[metric.outputBlits]++;
            }
            return profiler.originalRender.call(this, scene, camera);
        };
        renderer.render = this.renderWrapper;
    }

    beginFrame() {
        this.gpu?.beginFrame(this.totalFrames + 1);
        this.frame.fill(0);
        this.renderer.info.reset();
        this.running = true;
    }

    start() {
        return this.clock();
    }

    setGpuTimingMode(mode) {
        if (this.running) throw new Error('Apply GPU timing between application frames.');
        return this.gpu?.setMode(mode) ?? 'off';
    }

    endPhase(phase, started, batches = 0) {
        if (!this.running) return;
        this.frame[phase] += this.clock() - started;
        this.frame[phaseCount + phase]++;
        if (phase === ThreePhase.native) this.frame[metric.nativeBatches] += batches;
    }

    endFrame() {
        if (!this.running) return;
        const info = this.renderer.info.render;
        this.frame[metric.renderCalls] = info.frameCalls;
        this.frame[metric.drawCalls] = info.drawCalls;
        this.frame[metric.triangles] = info.triangles;
        for (let index = 0; index < metricCount; index++) this.ring[index][this.index] = this.frame[index];
        this.index = (this.index + 1) % capacity;
        this.samples = Math.min(this.samples + 1, capacity);
        this.totalFrames++;
        this.running = false;
        this.gpu?.endFrame(this.totalFrames);
    }

    snapshot(reset = true) {
        const phases = {};
        for (let index = 0; index < phaseCount; index++) {
            phases[phaseNames[index]] = { cpuMs: summary(this.ring[index], this.samples), calls: summary(this.ring[phaseCount + index], this.samples) };
        }
        const renders = {};
        for (const [name, index] of Object.entries(metric)) renders[name] = summary(this.ring[index], this.samples);
        const memory = this.renderer.info.memory;
        const result = { samples: this.samples, totalFrames: this.totalFrames, measurement: 'CPU submission wall time; GPU duration is not measured by these CPU phase metrics',
            phases, renders, memory: memory ? { textures: memory.textures, renderTargets: memory.renderTargets,
                programs: memory.programs, uniformBuffers: memory.uniformBuffers, bytes: memory.total } : null };
        if (this.gpu) result.gpu = this.gpu.snapshot(reset);
        if (reset) {
            this.index = 0;
            this.samples = 0;
        }
        return result;
    }

    dispose() {
        if (this.renderer.render === this.renderWrapper) this.renderer.render = this.originalRender;
        this.renderer.info.autoReset = this.autoReset;
        this.running = false;
        this.gpu?.dispose();
    }
}
