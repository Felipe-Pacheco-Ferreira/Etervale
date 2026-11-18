import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';
import { ExternalCamera } from '../wwwroot/three-camera.js';

const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
const positions = [-0.8, 0.8, 0.5, 0.5, 0.8, 0.8, 0.5, 0.5, 0.8, -0.8, 0.5, 0.5, -0.8, -0.8, 0.5, 0.5];
const shapes = [-1, -1, 0, 0.75, 1, -1, 0, 0.75, 1, 1, 0, 0.75, -1, 1, 0, 0.75];
const indices = [0, 1, 2, 0, 2, 3];
const vertexParams = [0, 0, 0, 0, 900, 2600, 900, 1 / 26, 0, -850, 0, 0, 0, 0, 0, 0, 0, 0, 0, 64, 128, 96, 1, 0.5];
const pixelParams = [0.5, 0.7, 1.2, 0.38, 0, 0, 8, 0, 256, 0, 0, 0];
const blend = [4, 5, 0, 4, 5, 0];

function fakeRenderer(coordinateSystem = THREE.WebGPUCoordinateSystem) {
    return { coordinateSystem, backend: { isWebGPUBackend: coordinateSystem === THREE.WebGPUCoordinateSystem }, calls: [],
        setPixelRatio() {}, setSize() {}, setScissorTest(value) { this.clipped = value; },
        setScissor(...values) { this.scissor = values; }, setViewport(...values) { this.viewport = values; },
        setRenderTarget(target) { this.target = target; }, getRenderTarget() { return this.target; }, setClearColor() {},
        clear() {}, render(scene) { this.calls.push({ target: this.target, meshes: scene.children.slice(), viewport: this.viewport }); },
        dispose() {} };
}

test('native Single occlusion textures preserve Float32 bits and partial unaligned byte uploads', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    const values = new Float32Array([-1e8, -40.25, 2.5, 1032.5, 7.75, 0]);
    const bytes = new Uint8Array(values.buffer);
    graphics.createTexture(13, 3, 2, bytes, false, 0, 13);
    const map = graphics.texture(13);
    assert.equal(map.format, THREE.RedFormat);
    assert.equal(map.type, THREE.FloatType);
    assert.equal(map.colorSpace, THREE.NoColorSpace);
    assert.equal(map.minFilter, THREE.NearestFilter);
    assert.equal(map.generateMipmaps, false);
    assert.deepEqual(Array.from(map.image.data), Array.from(values));
    const source = new Uint8Array(9);
    source.set(new Uint8Array(new Float32Array([-0.125, 2048.75]).buffer), 1);
    graphics.updateTexture(13, 1, 0, 1, 2, source.subarray(1));
    assert.deepEqual(Array.from(map.image.data), [-1e8, -0.125, 2.5, 1032.5, 2048.75, 0]);
    assert.equal(graphics.texture(13), map);
    assert.throws(() => graphics.createTexture(14, 1, 1, null, false, 1, 13), /raw linear/);
    await graphics.dispose();
});

test('rain preserves fourth vertex components, raw parameters and native depth/blend state in reusable batches', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    const vertices = new Float32Array(positions);
    const shape = new Float32Array(shapes);
    graphics.beginFrame(128, 96);
    graphics.drawRain(vertices, shape, indices, 0, identity, vertexParams, pixelParams, 5,
        true, false, [3, 5, 17, 11], [0, 0, 128, 96], 0, blend);
    const batch = graphics.commands[0].batch;
    vertices.fill(0);
    shape.fill(0);
    assert.deepEqual(Array.from(batch.mesh.geometry.getAttribute('rainPosition').array), positions.map(Math.fround));
    assert.deepEqual(Array.from(batch.mesh.geometry.getAttribute('rainShape').array), shapes);
    assert.equal(batch.mesh.material.depthTest, true);
    assert.equal(batch.mesh.material.depthWrite, false);
    assert.equal(batch.mesh.material.side, THREE.DoubleSide);
    assert.equal(batch.mesh.material.forceSinglePass, true);
    assert.equal(batch.mesh.material.transparent, true);
    assert.equal(batch.mesh.material.toneMapped, false);
    assert.equal(batch.mesh.material.depthFunc, THREE.LessEqualDepth);
    assert.equal(batch.mesh.material.blendSrc, THREE.SrcAlphaFactor);
    assert.equal(batch.mesh.material.blendDst, THREE.OneMinusSrcAlphaFactor);
    assert.equal(batch.mesh.material.blendSrcAlpha, THREE.SrcAlphaFactor);
    assert.deepEqual(batch.state.eye.value.toArray(), vertexParams.slice(16, 20));
    assert.deepEqual(batch.state.color.value.toArray(), pixelParams.slice(0, 4));
    const geometry = batch.mesh.geometry;
    graphics.present();
    graphics.beginFrame(128, 96);
    graphics.drawRain(positions, shapes, indices, 0, identity, vertexParams, pixelParams, 5);
    assert.equal(graphics.commands[0].batch, batch);
    assert.equal(batch.mesh.geometry, geometry);
    graphics.present();
    await graphics.dispose();
});

test('rain WASM views rebuild after memory growth while queued data and frame resources remain owned', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    function heap() {
        const buffer = new ArrayBuffer(1024);
        new Float32Array(buffer, 64, 16).set(positions);
        new Float32Array(buffer, 128, 16).set(shapes);
        new Uint32Array(buffer, 192, 6).set(indices);
        new Float32Array(buffer, 256, 16).set(identity);
        new Float32Array(buffer, 320, 24).set(vertexParams);
        new Float32Array(buffer, 416, 12).set(pixelParams);
        new Int32Array(buffer, 528, 4).set([0, 0, 128, 96]);
        new Int32Array(buffer, 544, 6).set(blend);
        return buffer;
    }
    let buffer = heap();
    let saved;
    for (let frame = 0; frame < 3; frame++) {
        if (frame === 2) buffer = heap();
        graphics.beginFrame(128, 96);
        graphics.drawRainMemory(buffer, 64, 128, frame === 1 ? 3 : 4, 192, frame === 1 ? 3 : 6, 0, 256, 320, 416, 5, 1, 0, 0, 528, 0, 544, 0, 5);
        const view = graphics.memorySlots[0].rainRange.floats;
        if (frame === 1) assert.equal(view, saved);
        if (frame === 2) assert.notEqual(view, saved);
        saved = view;
        graphics.present();
    }
    await graphics.dispose();
});

test('rain uses the existing HDR depth before explicit composition and leaves the following HUD on its caller target', async () => {
    const renderer = fakeRenderer();
    const target = new THREE.RenderTarget(64, 48);
    const post = { active: false, target, scale: 0.5, beginFrame() {}, finishScene() {},
        beginScene() { this.active = true; return target; }, compose() { this.active = false; }, dispose() { target.dispose(); } };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, post });
    graphics.beginFrame(128, 96);
    graphics.createScene(1);
    graphics.drawScene(1, { view: identity, projection: identity });
    graphics.drawRain(positions, shapes, indices, 0, identity, vertexParams, pixelParams, 5,
        true, false, null, [0, 0, 128, 96], 0, blend);
    graphics.composeScene(1);
    graphics.drawTriangles([0, 0, 0, 1, 0, 0, 0, 1, 0], null, null, [0, 1, 2], 0, identity);
    graphics.present();
    assert.deepEqual(renderer.calls.map(call => call.target), [target, target, null]);
    assert.deepEqual(renderer.calls[1].viewport, [0, 0, 64, 48]);
    await graphics.dispose();
});

test('actual rain streak, toroidal occlusion, splash ring and spray node graphs compile to WGSL', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.createTexture(13, 2, 2, new Uint8Array(new Float32Array([-1e8, 0, 10, 50]).buffer), false, 0, 13);
    const compiler = new THREE.WebGPURenderer({ canvas: { width: 128, height: 96, style: {}, addEventListener() {}, removeEventListener() {} } });
    compiler.hasFeature = () => false;
    compiler._bindings = undefined;
    for (const kind of [4, 5]) {
        graphics.beginFrame(128, 96);
        graphics.drawRain(positions, shapes, indices, 13, identity, vertexParams, pixelParams, kind);
        const batch = graphics.commands[0].batch;
        graphics.present();
        const builder = compiler.backend.createNodeBuilder(batch.mesh, compiler);
        builder.camera = new ExternalCamera(THREE.WebGPUCoordinateSystem);
        builder.scene = new THREE.Scene();
        builder.sortBindingGroups = () => {};
        builder.build();
        assert.match(builder.vertexShader, /noxiusRainWorld/);
        assert.match(builder.fragmentShader, /@fragment/);
        if (kind === 4) {
            assert.match(builder.fragmentShader, /textureLoad/);
            assert.match(builder.fragmentShader, /discard/);
        } else {
            assert.match(builder.fragmentShader, /exp\(/);
        }
    }
    await graphics.dispose();
});

test('rain explicitly writes zero-alpha auxiliary MRT output and returns to the single-target graph', async () => {
    const renderer = fakeRenderer();
    const target = new THREE.RenderTarget(128, 96, { count: 2, type: THREE.HalfFloatType });
    target.textures[0].name = 'output';
    target.textures[1].name = 'normalRoughness';
    const post = { active: true, target, scale: 1, beginFrame() {}, compose() { this.active = false; }, dispose() { target.dispose(); } };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, post });
    const compiler = new THREE.WebGPURenderer({ canvas: { width: 128, height: 96, style: {}, addEventListener() {}, removeEventListener() {} } });
    compiler.hasFeature = () => false;
    compiler._bindings = undefined;
    compiler.setRenderTarget(target);
    for (const kind of [4, 5]) {
        graphics.beginFrame(128, 96);
        graphics.drawRain(positions, shapes, indices, 0, identity, vertexParams, pixelParams, kind,
            true, false, null, null, 0, blend);
        const batch = graphics.commands[0].batch;
        graphics.present();
        assert.equal(batch.hasMrt, true);
        assert.equal(batch.mesh.material.fragmentNode, batch.mrtFragment);
        const auxiliary = batch.mrtFragment.get('normalRoughness');
        assert.deepEqual(auxiliary.value.toArray(), [0, 0, 0, 0]);
        const builder = compiler.backend.createNodeBuilder(batch.mesh, compiler);
        builder.camera = new ExternalCamera(THREE.WebGPUCoordinateSystem);
        builder.scene = new THREE.Scene();
        builder.sortBindingGroups = () => {};
        builder.build();
        assert.match(builder.fragmentShader, /@location\(\s*1\s*\)/);
        assert.match(builder.fragmentShader, /output\.m1 = vec4<f32>\(\s*0\.0, 0\.0, 0\.0, 0\.0\s*\)/);
        const version = batch.mesh.material.version;
        post.active = false;
        graphics.beginFrame(128, 96);
        graphics.drawRain(positions, shapes, indices, 0, identity, vertexParams, pixelParams, kind);
        assert.equal(graphics.commands[0].batch, batch);
        graphics.present();
        assert.equal(batch.hasMrt, false);
        assert.equal(batch.mesh.material.fragmentNode, batch.fragment);
        assert.ok(batch.mesh.material.version > version);
        post.active = true;
    }
    await graphics.dispose();
});
