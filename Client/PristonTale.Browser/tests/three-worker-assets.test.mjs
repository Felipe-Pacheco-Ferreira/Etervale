import test from 'node:test';
import assert from 'node:assert/strict';
import { loadWorkerSmaaLookups } from '../wwwroot/three-worker-assets.js';

function fixture() {
    const state = { requests: [], decodes: [], images: [], cleared: [], timer: null };
    const environment = { location: { origin: 'https://game.test' }, AbortController,
        setTimeout(callback, duration) { state.timer = callback; state.duration = duration; return 1; },
        clearTimeout(timer) { state.cleared.push(timer); },
        async fetch(url, options) {
            state.requests.push({ url, options });
            return { status: 200, redirected: false, blob: async () => url };
        },
        async createImageBitmap(blob, options) {
            state.decodes.push({ blob, options });
            const area = blob.includes('smaa-area.png');
            const image = { width: area ? 160 : 66, height: area ? 560 : 33, closed: 0, close() { this.closed++; } };
            state.images.push(image);
            return image;
        } };
    return { state, environment, options: { environment, baseUrl: 'https://game.test/play/three-worker-assets.js' } };
}

test('Worker lookups decode exact same-origin PNGs with the verified data-image options and transfer contract', async () => {
    const { state, options } = fixture();
    const result = await loadWorkerSmaaLookups(options);
    assert.deepEqual(state.requests.map(item => item.url), ['https://game.test/play/vendor/three/addons/tsl/display/smaa-area.png',
        'https://game.test/play/vendor/three/addons/tsl/display/smaa-search.png']);
    for (const item of state.requests) {
        assert.equal(item.options.mode, 'same-origin'); assert.equal(item.options.redirect, 'error');
        assert.equal(item.options.credentials, 'same-origin'); assert.ok(item.options.signal instanceof AbortSignal);
    }
    for (const item of state.decodes) assert.deepEqual(item.options, { premultiplyAlpha: 'none', colorSpaceConversion: 'none', imageOrientation: 'none' });
    assert.deepEqual(result.transferList, [result.workerInit.smaaLookups.area, result.workerInit.smaaLookups.search]);
    assert.equal(state.duration, 20000); assert.deepEqual(state.cleared, [1]);
    assert.ok(state.images.every(image => image.closed === 0));
    result.release(); result.release();
    assert.ok(state.images.every(image => image.closed === 1));
});

test('Worker lookup origin and capability refusal happen before requests or canvas transfer', async () => {
    const { state, environment, options } = fixture();
    await assert.rejects(loadWorkerSmaaLookups({ ...options, baseUrl: 'https://other.test/play/three-worker-assets.js' }), /origin/);
    environment.createImageBitmap = undefined;
    await assert.rejects(loadWorkerSmaaLookups(options), /unavailable/);
    assert.equal(state.requests.length, 0);
});

test('Worker lookup failure aborts sibling fetch and closes already decoded and late images', async () => {
    const { state, environment, options } = fixture();
    let resolveSearch;
    const original = environment.createImageBitmap;
    environment.createImageBitmap = async (blob, decodeOptions) => {
        if (blob.includes('search')) return new Promise(resolve => { resolveSearch = resolve; });
        const image = await original(blob, decodeOptions); image.width = 159; return image;
    };
    await assert.rejects(loadWorkerSmaaLookups(options), /dimensions/);
    assert.equal(state.images[0].closed, 1);
    assert.ok(state.requests.every(item => item.options.signal.aborted));
    const late = { closed: 0, close() { this.closed++; } };
    resolveSearch(late);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(late.closed, 1);
});

test('Worker lookup deadline bounds decoding too and closes a bitmap resolving after timeout', async () => {
    const { state, environment, options } = fixture();
    const pending = [];
    environment.createImageBitmap = () => new Promise(resolve => pending.push(resolve));
    const promise = loadWorkerSmaaLookups(options);
    await new Promise(resolve => setImmediate(resolve));
    state.timer();
    await assert.rejects(promise, /timed out/);
    assert.deepEqual(state.cleared, [1]);
    for (const resolve of pending) {
        const image = { closed: 0, close() { this.closed++; } }; resolve(image); state.images.push(image);
    }
    await new Promise(resolve => setImmediate(resolve));
    assert.ok(state.images.every(image => image.closed === 1));
});

test('Worker lookup HTTP and redirect failures do not decode response bodies', async () => {
    for (const response of [{ status: 404, redirected: false }, { status: 200, redirected: true }]) {
        const { state, environment, options } = fixture();
        environment.fetch = async () => response;
        await assert.rejects(loadWorkerSmaaLookups(options), /could not be loaded/);
        assert.equal(state.decodes.length, 0); assert.deepEqual(state.cleared, [1]);
    }
});
