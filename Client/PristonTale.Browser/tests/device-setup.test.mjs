import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../wwwroot/main.js', import.meta.url), 'utf8');
const from = source.indexOf('async function createDevice(');
const to = source.indexOf('function enableTextureArrayBenchmark(', from);
assert.ok(from >= 0 && to > from);

function fixture(search, { workerAvailable = true, transferAvailable = true, workerError = null } = {}) {
    const calls = [];
    const canvas = { attributes: [{ name: 'id', value: 'canvas' }, { name: 'aria-label', value: 'Noxius' },
        { name: 'data-renderer-state', value: 'lost' }, { name: 'data-renderer-errors', value: 'old error' }],
        replaceWith(value) { calls.push(['replace', value]); } };
    if (transferAvailable) canvas.transferControlToOffscreen = () => {};
    const replacement = { attributes: [], setAttribute(name, value) { this.attributes.push({ name, value }); } };
    const mainResult = { kind: 'main' }, workerResult = { kind: 'worker' };
    const document = { createElement(tag) { assert.equal(tag, 'canvas'); return replacement; } };
    const worldHud = { setCanvas(value) { calls.push(['rebind', value]); } };
    const create = new Function('URLSearchParams', 'location', 'canvas', 'Worker', 'createWorkerDevice', 'createMainGraphics',
        'document', 'worldHud', 'console', source.slice(from, to) + ';return createDevice;')(
        URLSearchParams, { search }, canvas, workerAvailable ? function Worker() {} : undefined,
        async options => { calls.push(['worker', options]); if (workerError) throw workerError; return workerResult; },
        async (target, options) => { calls.push(['main', target, options]); return mainResult; }, document, worldHud,
        { warn(...args) { calls.push(['warning', ...args]); } });
    return { create, calls, canvas, replacement, mainResult, workerResult };
}

test('normal visits choose Worker, four slots and stage bounds without enabling diagnostic features', async () => {
    const f = fixture('');
    assert.equal(await f.create(), f.workerResult);
    assert.equal(f.calls.length, 1);
    assert.equal(f.calls[0][0], 'worker');
    const options = f.calls[0][1];
    assert.equal(options.frameSlots, 4);
    assert.equal(options.stageSkinCulling, true);
    assert.equal(options.textureArrays, true);
    assert.equal(options.zeroPointLights, 'auto');
    assert.equal(options.internalPost, 'auto');
    assert.equal(options.fusedSharpen, 'auto');
    for (const field of ['profile', 'gpuTiming', 'cutoutArrays', 'renderBundles', 'staticArrayObservers']) assert.equal(options[field], false, field);
});

test('render-worker=0 retains the direct renderer and original canvas', async () => {
    const f = fixture('?render-worker=0');
    assert.equal(await f.create(), f.mainResult);
    assert.equal(f.calls.length, 1);
    assert.equal(f.calls[0][0], 'main');
    assert.equal(f.calls[0][1], f.canvas);
    assert.equal(f.calls[0][2].profile, false);
});

test('explicit worker visits retain the selected backend and diagnostic options', async () => {
    const f = fixture('?render-worker=1&renderer=webgl2&profile=1&gpu-timing=1&observer-cache=1');
    assert.equal(await f.create(), f.workerResult);
    assert.equal(f.calls.length, 1);
    assert.equal(f.calls[0][0], 'worker');
    assert.equal(f.calls[0][1].forceWebGL, true);
    assert.equal(f.calls[0][1].gpuTiming, true);
    assert.equal(f.calls[0][1].staticArrayObservers, true);
});

test('unsupported worker or canvas transfer keeps the existing canvas and direct renderer', async () => {
    for (const options of [{ workerAvailable: false }, { transferAvailable: false }]) {
        const f = fixture('?render-worker=1', options);
        assert.equal(await f.create(), f.mainResult);
        assert.equal(f.calls.length, 1);
        assert.equal(f.calls[0][1], f.canvas);
    }
});

test('four slots are the startup default and frame-slots=3 remains an independent bounded escape', async () => {
    for (const [query, expected] of [['', 4], ['&frame-slots=3', 3], ['&frame-slots=4', 4],
        ['&profile=1&frame-slots=3', 3], ['&frame-slots=2', 4], ['&frame-slots=5', 4]]) {
        const f = fixture('?render-worker=1' + query);
        assert.equal(await f.create(), f.workerResult);
        assert.equal(f.calls[0][1].frameSlots, expected, query);
    }
});

test('cutout array batching is an explicit profile-only startup flag', async () => {
    for (const [query, expected] of [['', false], ['&cutout-arrays=1', false], ['&profile=0&cutout-arrays=1', false],
        ['&profile=1&cutout-arrays=0', false], ['&profile=1&cutout-arrays=1', true]]) {
        const f = fixture('?render-worker=1' + query);
        await f.create();
        assert.equal(f.calls[0][1].cutoutArrays, expected, query);
    }
});

test('point lights use actual-backend auto policy with an explicit zero escape and profile-only forced override', async () => {
    for (const [query, expected] of [['', 'auto'], ['&zero-point-lights=1', 'auto'], ['&profile=0&zero-point-lights=1', 'auto'],
        ['&zero-point-lights=0', false], ['&profile=1&zero-point-lights=0', false], ['&profile=1&zero-point-lights=1', true],
        ['&renderer=webgl2', 'auto'], ['&renderer=webgl2&profile=1&zero-point-lights=1', true]]) {
        const f = fixture('?render-worker=1' + query);
        await f.create();
        assert.equal(f.calls[0][1].zeroPointLights, expected, query);
    }
});

test('animated stage bounds are enabled independently of profiling with an explicit zero escape', async () => {
    for (const [query, expected] of [['', true], ['&stage-culling=1', true], ['&stage-culling=0', false],
        ['&profile=1&stage-culling=0', false], ['&profile=1&stage-culling=1', true]]) {
        const f = fixture('?render-worker=1' + query);
        await f.create();
        assert.equal(f.calls[0][1].stageSkinCulling, expected, query);
    }
});

test('failed worker initialization replaces a potentially transferred canvas and rebinds input before fallback', async () => {
    const f = fixture('?render-worker=1&renderer=webgl2&texture-arrays=0', { workerError: new Error('Initialization timed out.') });
    assert.equal(await f.create(), f.mainResult);
    assert.deepEqual(f.calls.map(call => call[0]), ['worker', 'replace', 'rebind', 'warning', 'main']);
    assert.equal(f.calls.at(-1)[1], f.replacement);
    assert.deepEqual(f.replacement.attributes, f.canvas.attributes.filter(attribute => !attribute.name.startsWith('data-renderer')));
    assert.equal(f.calls.at(-1)[2].forceWebGL, true);
    assert.equal(f.calls.at(-1)[2].textureArrays, false);
});


test('internal post selects actual-backend auto by default and preserves the zero escape and forced profile mode on both paths', async () => {
    for (const renderer of ['', '&render-worker=0']) for (const [query, expected] of [['', 'auto'], ['&internal-post=1', 'auto'],
        ['&profile=0&internal-post=1', 'auto'], ['&internal-post=0', false], ['&profile=1&internal-post=0', false],
        ['&renderer=webgl2', 'auto'], ['&profile=1&internal-post=1', true]]) {
        const f = fixture('?unused=1' + renderer + query); await f.create();
        const call = f.calls.find(call => call[0] === 'worker' || call[0] === 'main');
        assert.equal(call[call[0] === 'worker' ? 1 : 2].internalPost, expected, renderer + query);
    }
});

test('fused sharpening selects backend auto by default and preserves its zero escape and forced profile mode', async () => {
    for (const renderer of ['', '&render-worker=0']) for (const [query, expected] of [['', 'auto'], ['&fused-sharpen=1', 'auto'],
        ['&profile=0&fused-sharpen=1', 'auto'], ['&fused-sharpen=0', false], ['&profile=1&fused-sharpen=0', false],
        ['&renderer=webgl2', 'auto'], ['&profile=1&fused-sharpen=1', true]]) {
        const f = fixture('?unused=1' + renderer + query); await f.create();
        const call = f.calls.find(call => call[0] === 'worker' || call[0] === 'main');
        assert.equal(call[call[0] === 'worker' ? 1 : 2].fusedSharpen, expected, renderer + query);
    }
});
