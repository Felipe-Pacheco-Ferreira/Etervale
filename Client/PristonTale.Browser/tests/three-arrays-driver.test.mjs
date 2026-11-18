import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

const identity = new THREE.Matrix4().elements;
const frame = { view: identity, projection: identity };

function renderer(layers = 128) {
    return { coordinateSystem: THREE.WebGPUCoordinateSystem, backend: { isWebGPUBackend: true,
        device: { limits: { maxTextureArrayLayers: layers } } },
    info: { autoReset: true, render: { frameCalls: 0, drawCalls: 0, triangles: 0 }, reset() {} },
    setPixelRatio() {}, setSize() {}, setScissorTest() {}, setViewport() {}, setRenderTarget() {},
    getRenderTarget() { return null; }, getMaxAnisotropy() { return 8; }, render() {}, dispose() {} };
}

async function setup(options = {}) {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: renderer(), ...options });
    graphics.createScene(1);
    graphics.configureScene(1, { quality: 'Medium', shadowMapSize: 0 });
    return graphics;
}

function pixels(size, value) {
    const data = new Uint8Array(size * size * 4);
    for (let at = 0; at < data.length; at += 4) data.set([value, 80, 140, 255], at);
    return data;
}

function resources(graphics, size = 4) {
    for (const id of [-1, -2]) {
        graphics.createTexture(id, size, size, pixels(size, id === -1 ? 60 : 180));
        graphics.configureSceneTexture(1, id);
    }
    for (let id = 1; id <= 4; id++) {
        const x = id * 3;
        const textureId = id % 2 ? -1 : -2;
        graphics.createMesh(1, id, { positions: [x, 0, 0, x + 1, 0, 0, x, 1, 0],
            normals: [0, 0, 1, 0, 0, 1, 0, 0, 1], colors: [1, 0.7, 0.4, 1, 1, 0.7, 0.4, 1, 1, 0.7, 0.4, 1],
            uv: [-1, 0, 2, 0, -1, 3], indices: [0, 1, 2],
            material: { textureId, textureIds: [textureId], unlit: true, opacity: 1, alphaCutoff: 0,
                doubleSided: true, roughness: 0.63, metallic: 0.1 } });
    }
}

test('texture arrays honor an explicit disable and require an actual layer capability', async () => {
    for (const options of [{}, { profile: true }, { textureArrays: false },
        { profile: true, textureArrays: true, renderer: renderer(1) }]) {
        const graphics = await setup(options);
        resources(graphics);
        graphics.createStage(1, 1, [1, 2, 3, 4]);
        assert.equal(graphics.textureArraysEnabled, false);
        assert.equal(graphics.setTextureArraysEnabled(true), false);
        assert.equal(graphics.textureArrayBytes, 0);
        assert.equal(graphics.scene(1).stages.get(1).children.length, 2);
        await graphics.dispose();
    }
});

test('the production array path works without allocating a diagnostic profiler', async () => {
    const graphics = await setup({ textureArrays: true });
    resources(graphics);
    graphics.createStage(1, 1, [1, 2, 3, 4]);
    assert.equal(graphics.profiler, null);
    assert.equal(graphics.textureArraysEnabled, true);
    assert.equal(graphics.scene(1).stages.get(1).children.length, 1);
    assert.ok(graphics.textureArrayBytes > 0);
    assert.equal(graphics.scene(1).stages.get(1).children[0].material.userData.arrayTexture.image.depth, 2);
    await graphics.dispose();
    assert.equal(graphics.textureArrayBytes, 0);
});

test('production cutout pages require their own profile opt-in and preserve sampled alpha and shadow material policy', async () => {
    for (const [options, enabled] of [[{ textureArrays: true, cutoutArrays: true }, false],
        [{ profile: true, textureArrays: true }, false], [{ profile: true, textureArrays: false, cutoutArrays: true }, false],
        [{ profile: true, textureArrays: true, cutoutArrays: true }, true]]) {
        const graphics = await setup(options);
        resources(graphics);
        graphics.updateTexture(-1, 0, 0, 1, 1, new Uint8Array([60, 80, 140, 0]));
        graphics.updateTexture(-2, 0, 0, 1, 1, new Uint8Array([180, 80, 140, 127]));
        for (const mesh of graphics.scene(1).meshes.values()) {
            mesh.descriptor.material.alphaCutoff = .4;
            mesh.material.alphaTest = .4;
        }
        graphics.createStage(1, 1, [1, 2, 3, 4]);
        const stage = graphics.scene(1).stages.get(1);
        assert.equal(stage.children.length, enabled ? 1 : 2);
        assert.equal(graphics.textureArrayBytes > 0, enabled);
        if (options.profile) assert.equal(graphics.getProfile().textureArrays.cutoutsEnabled, enabled);
        if (enabled) {
            const object = stage.children[0], material = object.material;
            assert.equal(material.alphaTest, .4);
            assert.equal(material.transparent, false);
            assert.equal(material.blending, THREE.NoBlending);
            assert.equal(material.depthWrite, true);
            assert.equal(material.side, THREE.DoubleSide);
            assert.equal(material.map, null);
            assert.ok(material.colorNode.a);
            assert.equal(object.castShadow, true);
            assert.equal(object.receiveShadow, true);
            const page = material.userData.arrayTexture;
            assert.equal(page.image.data[3], 0);
            assert.equal(page.image.data[4 * 4 * 4 + 3], 127);
            graphics.updateTexture(-1, 0, 0, 1, 1, new Uint8Array([60, 80, 140, 64]));
            assert.equal(graphics.textureArrayBytes, 0, 'a source update retires derived cutout pages before reuse');
        }
        await graphics.dispose();
    }
});

test('array stages start after the existing batching guard and retain the authored static material policy', async () => {
    const graphics = await setup({ profile: true, textureArrays: true });
    resources(graphics);
    const original = graphics.scene(1).meshes.get(1);
    graphics.createStage(1, 1, [1, 2, 3, 4]);
    const stage = graphics.scene(1).stages.get(1);
    assert.equal(stage.userData.baselineObjectCount, 2);
    assert.equal(stage.children.length, 1);
    const batch = stage.children[0];
    const array = batch.material.userData.arrayTexture;
    assert.ok(array.isDataArrayTexture);
    assert.equal(array.image.depth, 2);
    assert.equal(array.generateMipmaps, true);
    assert.equal(array.wrapS, THREE.RepeatWrapping);
    assert.equal(array.minFilter, THREE.LinearMipmapLinearFilter);
    assert.equal(array.colorSpace, THREE.SRGBColorSpace);
    assert.equal(batch.material.map, null);
    assert.equal(batch.material.vertexColors, true);
    assert.equal(batch.material.roughness, original.material.roughness);
    assert.equal(batch.material.metalness, original.material.metalness);
    assert.equal(batch.material.side, original.material.side);
    assert.equal(batch.castShadow, true);
    assert.equal(batch.receiveShadow, true);
    assert.ok(batch.geometry.hasAttribute('textureLayer'));
    assert.deepEqual(new Set(batch.geometry.getAttribute('textureLayer').array), new Set([0, 1]));
    assert.equal(original.material.map, graphics.texture(-1));
    assert.equal(original.geometry.hasAttribute('textureLayer'), false);
    const census = graphics.getProfile().textureArrays;
    assert.equal(census.originalObjects, 4);
    assert.equal(census.baselineObjects, 2);
    assert.equal(census.outputObjects, 1);
    assert.equal(census.eligibleObjects, 2);
    assert.equal(census.pages, 1);
    assert.equal(census.bytes, graphics.textureArrayBytes);
    graphics.configureScene(1, { quality: 'Low', shadowMapSize: 0 });
    assert.equal(array.anisotropy, graphics.texture(-1).anisotropy);
    assert.equal(stage.children[0], batch);
    await graphics.dispose();
    assert.equal(graphics.textureArrayBytes, 0);
});

test('updates, texture reuse and source removal invalidate only derived pages and rebuild before drawing', async () => {
    const graphics = await setup({ profile: true, textureArrays: true });
    resources(graphics);
    const original = graphics.texture(-1);
    let originalDisposals = 0;
    original.addEventListener('dispose', () => originalDisposals++);
    graphics.createStage(1, 1, [1, 2, 3, 4]);
    const page = graphics.scene(1).stages.get(1).children[0].material.userData.arrayTexture;
    let pageDisposals = 0;
    page.addEventListener('dispose', () => pageDisposals++);
    graphics.updateTexture(-1, 0, 0, 4, 4, pixels(4, 220));
    assert.equal(pageDisposals, 1);
    assert.equal(originalDisposals, 0);
    assert.equal(graphics.textureArrayBytes, 0);
    assert.equal(graphics.scene(1).stages.get(1).children.length, 2);
    graphics.drawScene(1, frame);
    graphics.present();
    const rebuilt = graphics.scene(1).stages.get(1).userData.textureArrayBatches.batches[0];
    assert.notEqual(rebuilt.material.userData.arrayTexture, page);
    const offset = rebuilt.textureIds.indexOf(-1) * 4 * 4 * 4;
    assert.equal(rebuilt.material.userData.arrayTexture.image.data[offset], 220);
    assert.equal(graphics.texture(-1), original);
    graphics.destroyTexture(-1);
    assert.equal(graphics.textureArrayBytes, 0);
    assert.equal(originalDisposals, 1);
    graphics.setTextureArraysEnabled(false);
    graphics.setTextureArraysEnabled(true);
    graphics.createTexture(-1, 4, 4, pixels(4, 110));
    graphics.configureSceneTexture(1, -1);
    graphics.drawScene(1, frame);
    graphics.present();
    assert.equal(graphics.scene(1).stages.get(1).children.length, 1);
    graphics.removeMesh(1, 1);
    graphics.removeMesh(1, 3);
    assert.equal(graphics.textureArrayBytes, 0);
    assert.equal(graphics.scene(1).stages.get(1).children.length, 1);
    assert.equal(graphics.scene(1).meshes.has(1), false);
    await graphics.dispose();
});

test('the aggregate array budget spans all stages and returns to later stages after disposal', async () => {
    const graphics = await setup({ profile: true, textureArrays: true });
    resources(graphics, 512);
    for (let id = 1; id <= 25; id++) graphics.createStage(1, id, [1, 2]);
    assert.ok(graphics.textureArrayBytes <= 64 * 1024 * 1024);
    assert.equal(graphics.scene(1).stages.get(24).children.length, 1);
    assert.equal(graphics.scene(1).stages.get(25).children.length, 2);
    graphics.removeStage(1, 1);
    graphics.createStage(1, 25, [1, 2]);
    assert.equal(graphics.scene(1).stages.get(25).children.length, 1);
    assert.ok(graphics.textureArrayBytes <= 64 * 1024 * 1024);
    graphics.setTextureArraysEnabled(false);
    assert.equal(graphics.textureArrayBytes, 0);
    graphics.setTextureArraysEnabled(true);
    assert.ok(graphics.textureArrayBytes <= 64 * 1024 * 1024);
    await graphics.dispose();
    assert.equal(graphics.textureArrayBytes, 0);
});

test('same-instance array toggles preserve nonidentity stage transforms, visibility, ownership and the draw baseline', async () => {
    const graphics = await setup({ profile: true, textureArrays: true });
    resources(graphics);
    graphics.createStage(1, 9, [1, 2, 3, 4]);
    let stage = graphics.scene(1).stages.get(9);
    stage.position.set(7, -3, 12);
    stage.quaternion.setFromEuler(new THREE.Euler(0.2, 0.4, -0.1));
    stage.scale.set(2, 3, 4);
    stage.matrix.compose(stage.position, stage.quaternion, stage.scale);
    stage.matrixWorld.copy(stage.matrix).premultiply(new THREE.Matrix4().makeTranslation(200, 10, -30));
    stage.matrixAutoUpdate = false;
    stage.matrixWorldAutoUpdate = false;
    stage.matrixWorldNeedsUpdate = false;
    stage.visible = false;
    stage.renderOrder = 73;
    stage.layers.set(3);
    const expected = { position: stage.position.toArray(), quaternion: stage.quaternion.toArray(), scale: stage.scale.toArray(),
        matrix: stage.matrix.toArray(), world: stage.matrixWorld.toArray() };
    const originalTexture = graphics.texture(-1);
    let originalsDisposed = 0, pagesDisposed = 0;
    originalTexture.addEventListener('dispose', () => originalsDisposed++);
    for (let round = 0; round < 3; round++) {
        const page = stage.children[0].material.userData.arrayTexture;
        page.addEventListener('dispose', () => pagesDisposed++);
        for (const enabled of [false, true]) {
            assert.equal(graphics.setTextureArraysEnabled(enabled), enabled);
            stage = graphics.scene(1).stages.get(9);
            assert.deepEqual(stage.userData.meshIds, [1, 2, 3, 4]);
            assert.deepEqual(stage.position.toArray(), expected.position);
            assert.deepEqual(stage.quaternion.toArray(), expected.quaternion);
            assert.deepEqual(stage.scale.toArray(), expected.scale);
            assert.deepEqual(stage.matrix.toArray(), expected.matrix);
            assert.deepEqual(stage.matrixWorld.toArray(), expected.world);
            assert.equal(stage.matrixAutoUpdate, false);
            assert.equal(stage.matrixWorldAutoUpdate, false);
            assert.equal(stage.matrixWorldNeedsUpdate, false);
            assert.equal(stage.visible, false);
            assert.equal(stage.renderOrder, 73);
            assert.equal(stage.layers.mask, 8);
            assert.equal(stage.children.length, enabled ? 1 : 2);
            assert.equal(stage.userData.baselineObjectCount, 2);
            assert.equal(graphics.textureArrayBytes > 0, enabled);
            assert.equal(originalsDisposed, 0);
            const same = stage;
            graphics.setTextureArraysEnabled(enabled);
            assert.equal(graphics.scene(1).stages.get(9), same);
        }
        assert.equal(pagesDisposed, round + 1);
    }
    assert.equal(graphics.texture(-1), originalTexture);
    await graphics.dispose();
    assert.equal(graphics.textureArrayBytes, 0);
});
