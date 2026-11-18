import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

function renderer() {
    return { coordinateSystem: THREE.WebGPUCoordinateSystem, backend: { isWebGPUBackend: true },
        info: { render: { calls: 0, triangles: 0 }, reset() {} },
        setPixelRatio() {}, setSize() {}, setScissorTest() {}, setRenderTarget() {}, setViewport() {}, render() {}, dispose() {} };
}

async function fixture(profile = true) {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: renderer(), profile });
    graphics.createScene(1);
    for (const id of [-1, -2, -3]) {
        const data = new Uint8Array(16).fill(255);
        if (id === -1) data[7] = 32;
        graphics.createTexture(id, 2, 2, data, false, 1);
        if (id !== -3) graphics.configureSceneTexture(1, id);
    }
    const mesh = (id, source = {}, descriptor = {}) => graphics.createMesh(1, id, {
        positions: [1, 0, 1, 2, 0, 1, 1, 1, 1], uv: [0, 0, 1, 0, 0, 1],
        material: { blend: 'opaque', textureId: -1, ...source }, ...descriptor });
    return { graphics, mesh };
}

test('production stage census counts actual material flags once across bundle wrappers and excludes actors and sky', async () => {
    const { graphics, mesh } = await fixture();
    mesh(1);
    mesh(2, { blend: 'alpha', doubleSided: true, alphaCutoff: .25 });
    mesh(3, { doubleSided: true, alphaCutoff: .25 });
    mesh(4, { alphaCutoff: .25 }, { boneCount: 1, boneIndices: [0, 0, 0] });
    mesh(5, { blend: 'alpha', alphaCutoff: .25, windX: 1 });
    mesh(6, { alphaCutoff: .25, texture2Id: -2 });
    mesh(7, { alphaCutoff: .25, textureId: -3 });
    mesh(8, { alphaCutoff: .25, textureId: -2 });
    mesh(9, { blend: 'alpha', doubleSided: true }, { isSky: true });
    mesh(10, { blend: 'alpha', doubleSided: true });
    graphics.createStage(1, 1, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
    graphics.createActor(1, 1, [10], 0);
    graphics.setSkyLayers(1, [9], [], 0, 0, 1);
    const stage = graphics.scene(1).stages.get(1), wrapper = new THREE.BundleGroup();
    for (const object of stage.userData.renderObjects) wrapper.add(object);
    stage.add(wrapper);
    const duplicate = new THREE.Group();
    duplicate.userData = { meshIds: [], baselineObjectCount: 0, renderObjects: stage.userData.renderObjects };
    graphics.scene(1).stages.set(2, duplicate);
    assert.deepEqual(graphics.getProfile().stageMaterials, { objects: 8, opaque: 6, transparent: 2,
        doubleSideTransparent: 1, doubleSideTransparentTwoPass: 1, skinned: 1, alphaCutout: 7,
        immutableSingleTextureCutout: 3, immutableSingleTextureCutoutTransparent: 1,
        immutableSingleTextureCutoutDoubleSide: 2, cutoutTextures: 2, cutoutTexturesWithAlpha: 1, cutoutSamplerGroups: 1 });
    graphics.scene(1).stages.delete(2);
    await graphics.dispose();
});

test('census reads current uploaded alpha and sampler state without changing material rendering policy', async () => {
    const { graphics, mesh } = await fixture();
    mesh(1, { blend: 'alpha', doubleSided: true, alphaCutoff: .25 });
    mesh(2, { alphaCutoff: .25, textureId: -2 });
    graphics.createStage(1, 1, [1, 2]);
    const material = graphics.scene(1).meshes.get(1).material;
    material.forceSinglePass = true;
    const first = graphics.getProfile().stageMaterials;
    assert.equal(first.doubleSideTransparent, 1);
    assert.equal(first.doubleSideTransparentTwoPass, 0);
    assert.equal(first.cutoutTexturesWithAlpha, 1);
    assert.equal(first.cutoutSamplerGroups, 1);
    graphics.updateTexture(-1, 1, 0, 1, 1, new Uint8Array([255, 255, 255, 255]));
    graphics.textures.get(-2).texture.anisotropy = 8;
    const second = graphics.getProfile().stageMaterials;
    assert.equal(second.cutoutTexturesWithAlpha, 0);
    assert.equal(second.cutoutSamplerGroups, 2);
    assert.equal(material.transparent, true);
    assert.equal(material.side, THREE.DoubleSide);
    assert.equal(material.alphaTest, .25);
    assert.equal(material.forceSinglePass, true);
    await graphics.dispose();
});

test('normal visits return before inspecting stage resources or alpha data', async () => {
    const { graphics } = await fixture(false);
    graphics.scenes.values = () => { throw Error('profiling-only census must not run'); };
    assert.equal(graphics.getProfile(), null);
    delete graphics.scenes.values;
    await graphics.dispose();
});
