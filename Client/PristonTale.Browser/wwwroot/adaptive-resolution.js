const minimumScale = 0.5;
const windowMs = 1000;
// A preset change or a map entry compiles shaders for a few seconds; those frames say nothing about the budget.
const warmupMs = 5000;
const slowWindows = 3;
const recoveryWindows = 6;
const minimumHealthyRatio = 0.985;
// Below this the browser is throttling the page (its window behind another one), not the GPU running out of time.
const throttledFps = 10;
// Shadow refresh steps under pressure: 0 keeps the preset's own rate. A field is usually bound by the render worker's
// CPU, where the shadow pass costs as much as a third of a frame and resolution saves nothing, and a lower scale blurs
// the whole image, so both steps come before any scale step.
const shadowRates = [0, 30, 15];

export function internalPostMode(value) {
    if (value === undefined) return false;
    if (value === true || value === false || value === 'auto') return value;
    throw new TypeError('Internal post mode must be false, true or auto.');
}

export function createAdaptiveResolution() { return new AdaptiveResolution(); }

export function validateAdaptiveBudget(scale, shadowHz) {
    if (scale !== null && (!Number.isFinite(scale) || scale < minimumScale || scale > 1)) throw new RangeError('Invalid dynamic resolution scale.');
    if (!shadowRates.includes(shadowHz)) throw new RangeError('Invalid adaptive shadow refresh rate.');
}

class AdaptiveResolution {
    constructor() {
        this.scale = null;
        this.shadowHz = 0;
        this.cap = 1;
        this.enabled = false;
        this.world = false;
        this.width = 0;
        this.height = 0;
        this.target = 60;
        this.lastFps = null;
        this.settingsRevision = 0;
        this.resetWindow(0, 0, true);
    }

    resetWindow(time, frames, warmup) {
        this.started = time;
        this.frames = frames;
        this.readyAt = time + (warmup ? warmupMs : 0);
        this.slow = 0;
        this.healthy = 0;
    }

    budget(scale, shadowHz) {
        if (this.scale === scale && this.shadowHz === shadowHz) return false;
        this.scale = scale;
        this.shadowHz = shadowHz;
        return true;
    }

    // holdScale: the classic light keeps the saved scale, sharp like the original; pressure then only slows the shadows.
    update(time, frames, enabled, cap, maxFps, width, height, world, eligible, settingsRevision = 0, holdScale = false) {
        if (!Number.isFinite(time) || !Number.isFinite(frames) || frames < 0) return false;
        cap = Number.isFinite(cap) ? Math.min(1, Math.max(minimumScale, cap)) : 1;
        const target = maxFps > 0 ? Math.min(60, maxFps) : 60;
        enabled = !!enabled;
        world = !!world;
        const changed = this.enabled !== enabled || this.cap !== cap || this.target !== target || this.world !== world
            || this.width !== width || this.height !== height || this.settingsRevision !== settingsRevision;
        if (changed) {
            this.enabled = enabled;
            this.cap = cap;
            this.target = target;
            this.world = world;
            this.width = width;
            this.height = height;
            this.settingsRevision = settingsRevision;
            this.lastFps = null;
            this.resetWindow(time, frames, true);
            return this.budget(enabled && world ? cap : null, 0);
        }
        if (!enabled || !world) return false;
        if (!eligible || time < this.started || time - this.started > 2500 || frames < this.frames) {
            this.resetWindow(time, frames, true);
            return false;
        }
        if (time < this.readyAt) {
            this.started = time;
            this.frames = frames;
            return false;
        }
        const elapsed = time - this.started;
        if (elapsed < windowMs) return false;
        const fps = (frames - this.frames) * 1000 / elapsed;
        if (fps < throttledFps) {
            this.resetWindow(time, frames, true);
            return false;
        }
        this.started = time;
        this.frames = frames;
        this.lastFps = fps;
        if (fps < target * minimumHealthyRatio) {
            this.healthy = 0;
            if (++this.slow < slowWindows) return false;
            this.slow = 0;
            const shadowStep = shadowRates.indexOf(this.shadowHz);
            if (shadowStep < shadowRates.length - 1 && target >= 50) {
                return this.budget(this.scale, shadowRates[shadowStep + 1]);
            }
            if (holdScale) return false;
            const estimate = this.scale * Math.sqrt(Math.max(0, fps) / target);
            const next = Math.max(minimumScale, Math.min(this.scale - 0.05, Math.max(this.scale - 0.15, estimate)));
            return this.budget(Math.round(next * 20) / 20, this.shadowHz);
        }
        this.slow = 0;
        if (++this.healthy < recoveryWindows) return false;
        this.healthy = 0;
        if (this.scale < cap) return this.budget(Math.min(cap, Math.round((this.scale + 0.05) * 20) / 20), this.shadowHz);
        const step = shadowRates.indexOf(this.shadowHz);
        if (step > 0) return this.budget(this.scale, shadowRates[step - 1]);
        return false;
    }
}
