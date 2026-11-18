import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { patchWebGlCanvasClear, patchWebGpuBundleOrder } from '../vendor-three-patches.mjs';

function clearBackend() {
    const renderer = new THREE.WebGPURenderer({ forceWebGL: true, canvas: {
        width: 256, height: 192, style: {}, addEventListener() {}, removeEventListener() {}
    } });
    const backend = renderer.backend;
    assert.equal(backend.isWebGLBackend, true);
    const calls = [];
    backend.renderer = { getClearDepth: () => 1, getClearStencil: () => 0 };
    backend.gl = { FRAMEBUFFER: 0x8d40, COLOR_BUFFER_BIT: 0x4000, COLOR: 0x1800,
        clearColor(...values) { calls.push(['color', ...values]); },
        clear(mask) { calls.push(['clear', mask]); },
        clearBufferfv(...values) { calls.push(['buffer', ...values]); } };
    backend.state = {
        setColorMask(value) { calls.push(['mask', value]); },
        bindFramebuffer(target, framebuffer) { calls.push(['bind', target, framebuffer]); }
    };
    return { backend, calls };
}

test('actual WebGL backend binds the canvas before an explicit clear after an offscreen pass', () => {
    const { backend, calls } = clearBackend();
    const descriptor = { textures: null, clearColorValue: { r: 0.2, g: 0.2, b: 0.2, a: 1 } };
    backend.clear(true, false, false, descriptor);
    assert.deepEqual(calls, [
        ['mask', true], ['bind', 0x8d40, null], ['color', 0.2, 0.2, 0.2, 1], ['clear', 0x4000]
    ]);
    calls.length = 0;
    backend.clear(true, false, false, descriptor, false);
    assert.equal(calls.some(call => call[0] === 'bind'), false, 'a clear within an active pass rebound its framebuffer');
    assert.equal(calls.at(-1)[0], 'clear');
});

test('actual WebGL backend keeps offscreen clear and resolve ownership unchanged', () => {
    const { backend, calls } = clearBackend();
    const descriptor = { textures: [{}], mrt: null, clearColorValue: { r: 0, g: 0, b: 0, a: 0 } };
    backend._setFramebuffer = value => calls.push(['offscreen', value]);
    backend._resolveRenderTarget = value => calls.push(['resolve', value]);
    backend.clear(true, false, false, descriptor);
    assert.deepEqual(calls.map(call => call[0]), ['mask', 'offscreen', 'buffer', 'resolve']);
    assert.equal(calls[1][1], descriptor);
    assert.equal(calls.at(-1)[1], descriptor);
});

test('vendoring reproduces the reviewed pinned patch exactly and refuses source or version drift', async () => {
    const original = await readFile(new URL('../node_modules/three/build/three.webgpu.js', import.meta.url), 'utf8');
    const actual = await readFile(new URL('../wwwroot/vendor/three/three.webgpu.js', import.meta.url), 'utf8');
    assert.equal(patchWebGpuBundleOrder(patchWebGlCanvasClear(original, '0.187.0'), '0.187.0'), actual);
    assert.throws(() => patchWebGlCanvasClear(original, '0.188.0'), /before upgrading/);
    assert.throws(() => patchWebGlCanvasClear('', '0.187.0'), /exactly once/);
    assert.throws(() => patchWebGlCanvasClear(original + original, '0.187.0'), /exactly once/);
    assert.throws(() => patchWebGlCanvasClear(actual, '0.187.0'), /exactly once/);
    assert.throws(() => patchWebGpuBundleOrder(original, '0.188.0'), /before upgrading/);
    assert.throws(() => patchWebGpuBundleOrder('', '0.187.0'), /exactly once/);
    assert.throws(() => patchWebGpuBundleOrder(original + original, '0.187.0'), /exactly once/);
    assert.throws(() => patchWebGpuBundleOrder(actual, '0.187.0'), /exactly once/);
});

test('actual WebGPU backend executes opaque bundles before subsequent normal draws and clears invalidated binding caches', () => {
    const backend = new THREE.WebGPURenderer({ canvas: { width: 192, height: 144,
        style: {}, addEventListener() {}, removeEventListener() {} } }).backend;
    assert.equal(backend.isWebGPUBackend, true);
    const events = [];
    const context = { camera: new THREE.PerspectiveCamera(), depthTexture: null };
    const bundle = {};
    backend.get(bundle).bundleGPU = { label: 'opaque terrain' };
    const data = backend.get(context);
    data.renderBundles = [];
    data.currentSets = { attributes: { stale: true }, bindingGroups: ['stale'], pipeline: 'stale', index: 'stale' };
    data.currentBlendAlpha = 0.4;
    data.currentStencilRef = 7;
    data.currentPass = { executeBundles(values) { events.push(['opaque', values]); } };
    backend.addBundle(context, bundle);
    events.push(['transparent']);
    assert.deepEqual(events.map(event => event[0]), ['opaque', 'transparent']);
    assert.equal(events[0][1][0], backend.get(bundle).bundleGPU);
    assert.deepEqual(data.currentSets, { attributes: {}, bindingGroups: [], pipeline: null, index: null });
    assert.equal(data.renderBundles.length, 0, 'finishRender must not replay the bundle a second time');
    assert.equal(data.currentBlendAlpha, 0.4, 'executeBundles does not reset dynamic blend/stencil state');
    assert.equal(data.currentStencilRef, 7);
});

test('actual WebGPU backend preserves the separate layered-camera bundle finalization path', () => {
    const backend = new THREE.WebGPURenderer({ canvas: { width: 192, height: 144,
        style: {}, addEventListener() {}, removeEventListener() {} } }).backend;
    const context = { camera: new THREE.ArrayCamera(), depthTexture: { isArrayTexture: true } };
    const data = backend.get(context);
    const bundle = {};
    backend.get(bundle).bundleGPU = { label: 'layered terrain' };
    data.renderBundles = [];
    data.currentSets = { untouched: true };
    data.currentPass = { executeBundles() { assert.fail('layered replay must remain deferred'); } };
    backend.addBundle(context, bundle);
    assert.deepEqual(data.renderBundles, [backend.get(bundle).bundleGPU]);
    assert.deepEqual(data.currentSets, { untouched: true });
});
