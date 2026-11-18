export const WorkerOp = Object.freeze({ begin: 1, present: 2, flush: 3, clear: 4, target: 5,
    textureCreate: 6, textureUpdate: 7, textureDestroy: 8, native: 9, rain: 10,
    sceneCreate: 11, sceneConfigure: 12, sceneTexture: 13, meshCreate: 14, meshRemove: 15,
    stageCreate: 16, stageRemove: 17, skyLayers: 18, skyParameters: 19, actorCreate: 20,
    actorRemove: 21, sceneFlush: 22, sceneDraw: 23, portrait: 24, compose: 25, sceneDispose: 26,
    textureArrays: 27, renderBundles: 28, staticObservers: 29, gpuTiming: 30, dynamicResolution: 31,
    rainGeometryCreate: 32, rainGeometryDraw: 33, rainGeometryDestroy: 34, zeroPointLights: 35, internalPost: 36 });

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const align = value => Math.ceil(value / 4) * 4;

export class WorkerPacketWriter {
    constructor(buffer = new ArrayBuffer(65536)) { this.reset(buffer); }

    reset(buffer) {
        this.buffer = buffer;
        this.view = new DataView(buffer);
        this.bytes = new Uint8Array(buffer);
        this.length = 0;
        this.recordStart = null;
    }

    reserve(length) {
        const needed = this.length + length;
        if (!Number.isSafeInteger(needed) || needed < 0) throw new RangeError('Invalid worker packet size.');
        if (needed <= this.buffer.byteLength) return;
        const buffer = new ArrayBuffer(Math.max(needed, this.buffer.byteLength * 2));
        new Uint8Array(buffer).set(this.bytes.subarray(0, this.length));
        this.buffer = buffer;
        this.view = new DataView(buffer);
        this.bytes = new Uint8Array(buffer);
    }

    begin(op) {
        this.reserve(8);
        const at = this.length;
        this.recordStart = at;
        this.view.setUint32(at, op, true);
        this.length += 8;
        return at;
    }

    end(at) { this.view.setUint32(at + 4, this.length - at, true); this.recordStart = null; }

    abort(error) {
        if (this.recordStart !== null) this.length = this.recordStart;
        this.recordStart = null;
        throw error;
    }

    number(value) {
        if (typeof value !== 'number' || !Number.isFinite(value)) this.abort(new TypeError('Invalid worker command scalar.'));
        this.reserve(8);
        this.view.setFloat64(this.length, value, true);
        this.length += 8;
    }

    raw(source) {
        const length = source.byteLength;
        if (!Number.isSafeInteger(length) || length < 0) this.abort(new RangeError('Invalid worker span length.'));
        this.reserve(4 + align(length));
        this.view.setUint32(this.length, length, true);
        this.length += 4;
        const target = this.bytes.subarray(this.length, this.length + length);
        try {
            if (source.copyTo) source.copyTo(target);
            else target.set(new Uint8Array(source.buffer ?? source, source.byteOffset ?? 0, length));
        } catch (error) { this.abort(error); }
        this.bytes.fill(0, this.length + length, this.length + align(length));
        this.length += align(length);
    }

    memory(buffer, pointer, length) {
        if (!Number.isSafeInteger(pointer) || !Number.isSafeInteger(length) || pointer < 0 || length < 0
            || pointer + length > buffer.byteLength) this.abort(new RangeError('Worker draw range exceeds CPU memory.'));
        this.raw(new Uint8Array(buffer, pointer, length));
    }

    text(value) { this.raw(encoder.encode(value)); }
    numbers(values) {
        const count = values.length;
        if (!Number.isSafeInteger(count) || count < 0) this.abort(new RangeError('Invalid worker number-array size.'));
        this.reserve(4 + count * 8);
        this.view.setUint32(this.length, count, true);
        this.length += 4;
        for (let index = 0; index < count; index++) this.number(values[index]);
    }
}

export class WorkerPacketReader {
    constructor(buffer, length) {
        if (!Number.isSafeInteger(length) || length < 0 || length > buffer.byteLength) throw new RangeError('Invalid worker packet length.');
        this.buffer = buffer;
        this.view = new DataView(buffer);
        this.length = length;
        this.at = 0;
        this.end = 0;
    }

    next() {
        if (this.at !== this.end) throw new Error('Worker command payload was not consumed exactly.');
        if (this.at === this.length) return 0;
        if (this.at + 8 > this.length) throw new RangeError('Truncated worker command.');
        const op = this.view.getUint32(this.at, true), length = this.view.getUint32(this.at + 4, true);
        if (!op || length < 8 || this.at + length > this.length) throw new RangeError('Invalid worker command bounds.');
        this.end = this.at + length;
        this.at += 8;
        return op;
    }

    number() {
        if (this.at + 8 > this.end) throw new RangeError('Truncated worker scalar.');
        const value = this.view.getFloat64(this.at, true);
        this.at += 8;
        if (!Number.isFinite(value)) throw new TypeError('Invalid worker scalar.');
        return value;
    }

    range() {
        if (this.at + 4 > this.end) throw new RangeError('Truncated worker span.');
        const length = this.view.getUint32(this.at, true);
        this.at += 4;
        if (this.at + align(length) > this.end) throw new RangeError('Worker span exceeds its command.');
        const at = this.at;
        this.at += align(length);
        this.lastLength = length;
        return at;
    }

    raw() {
        const at = this.range();
        return new Uint8Array(this.buffer, at, this.lastLength);
    }

    memoryRange(expected, optional = false) {
        const at = this.range();
        if (optional && this.lastLength === 0) return 0;
        if (this.lastLength !== expected) throw new RangeError('Worker draw span has an invalid size.');
        return at;
    }

    text() { return decoder.decode(this.raw()); }
    numbers() {
        if (this.at + 4 > this.end) throw new RangeError('Truncated worker number array.');
        const count = this.view.getUint32(this.at, true);
        this.at += 4;
        if (this.at + count * 8 > this.end) throw new RangeError('Worker number array exceeds its command.');
        const values = new Array(count);
        for (let index = 0; index < count; index++) values[index] = this.number();
        return values;
    }
}
