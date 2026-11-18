import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createNativeDisplayEncoder } from '../wwwroot/three-display.js';

function harness() {
    const output = new THREE.RenderTarget(122, 122);
    output.texture.colorSpace = THREE.NoColorSpace;
    output.viewport.set(4, 8, 100, 96);
    output.scissor.set(12, 16, 60, 70);
    output.scissorTest = true;
    const viewport = new THREE.Vector4(2, 3, 200, 180);
    const scissor = new THREE.Vector4(5, 6, 150, 140);
    const clear = new THREE.Color(0.1, 0.2, 0.3);
    const originalMrt = { original: true };
    const calls = [];
    const renderer = { outputColorSpace: THREE.LinearSRGBColorSpace, current: output, mrt: originalMrt, alpha: 0.7, scissorTest: true,
        getRenderTarget() { return this.current; },
        setRenderTarget(value) { this.current = value; },
        getMRT() { return this.mrt; },
        setMRT(value) { this.mrt = value; },
        getViewport(value) { return value.copy(viewport); },
        setViewport(...values) { values[0]?.isVector4 ? viewport.copy(values[0]) : viewport.set(...values); },
        getScissor(value) { return value.copy(scissor); },
        setScissor(...values) { values[0]?.isVector4 ? scissor.copy(values[0]) : scissor.set(...values); },
        getScissorTest() { return this.scissorTest; },
        setScissorTest(value) { this.scissorTest = value; },
        getClearColor(value) { return value.copy(clear); },
        getClearAlpha() { return this.alpha; },
        setClearColor(value, alpha) { value?.isColor ? clear.copy(value) : clear.set(value); this.alpha = alpha; },
        clear() { calls.push(['clear', this.current]); },
        render(quad) { calls.push(['render', this.current, quad.material, viewport.clone(), this.current.viewport.clone()]); } };
    return { renderer, output, viewport, scissor, clear, originalMrt, calls, encoder: createNativeDisplayEncoder(renderer) };
}

test('portrait display pass preserves output identity, background and all caller viewport state', () => {
    const { renderer, output, viewport, scissor, clear, originalMrt, calls, encoder } = harness();
    const originalTexture = output.texture;
    const originalViewport = output.viewport.clone();
    const originalScissor = output.scissor.clone();
    const savedViewport = viewport.clone();
    const savedScissor = scissor.clone();
    const savedClear = clear.clone();
    const hdr = encoder.begin(output, 122, 122);
    assert.equal(hdr.texture.type, THREE.HalfFloatType);
    assert.equal(hdr.texture.colorSpace, THREE.LinearSRGBColorSpace);
    assert.equal(hdr.depthBuffer, true);
    assert.deepEqual(hdr.viewport, originalViewport);
    assert.deepEqual(hdr.scissor, originalScissor);
    assert.equal(hdr.scissorTest, true);
    assert.deepEqual(calls, [['clear', hdr]]);
    encoder.compose(output, 1.75);
    const draw = calls.at(-1);
    assert.equal(draw[1], output);
    assert.equal(draw[2].blendSrc, THREE.OneFactor);
    assert.equal(draw[2].blendDst, THREE.OneMinusSrcAlphaFactor);
    assert.equal(draw[2].blendSrcAlpha, THREE.OneFactor);
    assert.equal(draw[2].blendDstAlpha, THREE.OneMinusSrcAlphaFactor);
    assert.equal(draw[2].depthTest, false);
    assert.equal(draw[2].depthWrite, false);
    assert.equal(draw[2].toneMapped, false);
    assert.deepEqual(draw[4], new THREE.Vector4(0, 0, 122, 122));
    assert.equal(output.texture, originalTexture);
    assert.deepEqual(output.viewport, originalViewport);
    assert.deepEqual(output.scissor, originalScissor);
    assert.equal(output.scissorTest, true);
    assert.equal(renderer.getRenderTarget(), output);
    assert.equal(renderer.getMRT(), originalMrt);
    assert.deepEqual(viewport, savedViewport);
    assert.deepEqual(scissor, savedScissor);
    assert.deepEqual(clear, savedClear);
    assert.equal(renderer.getClearAlpha(), 0.7);
    assert.equal(encoder.exposure.value, 1.75);
    encoder.dispose();
    output.dispose();
});

test('portrait display pass reuses its HDR resource and rejects a different composition target', () => {
    const { output, encoder } = harness();
    const hdr = encoder.begin(output, 122, 122);
    encoder.compose(output, 1);
    let disposals = 0;
    hdr.addEventListener('dispose', () => disposals++);
    assert.equal(encoder.begin(output, 122, 122), hdr);
    assert.throws(() => encoder.compose(null, 1), /does not match/);
    encoder.compose(output, 0.8);
    assert.equal(disposals, 0);
    assert.equal(encoder.begin(output, 140, 100), hdr);
    encoder.compose(output, 1);
    assert.equal(hdr.width, 140);
    assert.equal(hdr.height, 100);
    assert.equal(disposals, 1);
    encoder.dispose();
    assert.equal(disposals, 2);
    output.dispose();
});

test('portrait display shader builds the real unpremultiply, tone and output graph without GPU', () => {
    const { encoder, output } = harness();
    const renderer = new THREE.WebGPURenderer({ canvas: {
        width: 122, height: 122, style: {}, addEventListener() {}, removeEventListener() {}
    } });
    renderer.outputColorSpace = THREE.LinearSRGBColorSpace;
    renderer.hasFeature = () => false;
    renderer._bindings = undefined;
    const builder = renderer.backend.createNodeBuilder(encoder.quad, renderer);
    builder.camera = encoder.quad.camera;
    builder.scene = new THREE.Scene();
    builder.sortBindingGroups = () => {};
    builder.build();
    assert.match(builder.fragmentShader, /0\.76/);
    assert.match(builder.fragmentShader, /0\.0031308/);
    assert.match(builder.fragmentShader, /0\.000001/);
    encoder.dispose();
    output.dispose();
});
