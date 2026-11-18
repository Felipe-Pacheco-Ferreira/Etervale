import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';
import { createPackedImports } from '../wwwroot/three-bridge.js';

const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
const triangle = [0, 0, 0, 1, 0, 0, 0, 1, 0];
const uv = [0, 0, 1, 0, 0, 1];
const frame = { view: identity, projection: identity, ambient: [1, 1, 1], sunColor: [0, 0, 0] };

function fakeRenderer(coordinateSystem = THREE.WebGPUCoordinateSystem) {
    return {
        coordinateSystem, calls: [], backend: { isWebGPUBackend: coordinateSystem === THREE.WebGPUCoordinateSystem },
        setPixelRatio() {}, setSize(w, h) { this.calls.push(['size', w, h]); },
        setScissorTest(value) { this.clipped = value; }, setScissor(...rectangle) { this.scissor = rectangle; },
        setViewport(...rectangle) { this.viewport = rectangle; },
        getRenderTarget() { return this.target; },
        setRenderTarget(target) { this.target = target; }, setClearColor() {},
        clear() { this.calls.push(['clear', this.target]); },
        render(scene, camera) {
            this.calls.push(['draw', this.target, scene.children.filter(child => child.isMesh || child.isGroup)
                .map(child => ({ name: child.name, visible: child.visible, mesh: child,
                    position: child.geometry?.getAttribute('position').array.slice(0, 9),
                    projection: camera.projectionMatrix.elements.slice() })),
                { clipped: this.target?.scissorTest ?? this.clipped, scissor: this.target?.scissor.toArray() ?? this.scissor,
                    viewport: this.target?.viewport.toArray() ?? this.viewport }]);
        },
        dispose() {}
    };
}

async function fixture(renderer = fakeRenderer()) {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.beginFrame(1280, 720);
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: triangle, uv, boneIndices: [0, 0, 0], boneCount: 1,
        material: { unlit: true } });
    graphics.createActor(1, 1, [1], 1);
    return { graphics, renderer };
}

test('scene skinning consumes world-space affine rows without applying world twice', async () => {
    const { graphics } = await fixture();
    graphics.updateActor(1, 1, { visible: true, opacity: 1, tint: [1, 1, 1] },
        [1, 0, 0, 7, 0, 1, 0, 11, 0, 0, 1, 13]);
    const actor = graphics.scene(1).actors.get(1);
    assert.deepEqual([...actor.skeleton.boneMatrices], [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 7, 11, 13, 1]);
    assert.deepEqual(actor.group.children[0].matrixWorld.elements, identity);
    assert.deepEqual([...actor.group.children[0].geometry.getAttribute('skinWeight').array], [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]);
    await graphics.dispose();
});

test('state-only actor updates preserve palettes and restore opaque rendering after a spawn fade', async () => {
    const { graphics } = await fixture();
    graphics.updateActor(1, 1, { visible: true, opacity: 0.4, tint: [0, 0, 0], outline: [1, 0.8, 0.2, 1], castsShadow: false },
        [1, 0, 0, 7, 0, 1, 0, 11, 0, 0, 1, 13]);
    const actor = graphics.scene(1).actors.get(1);
    const material = actor.group.children[0].material;
    assert.equal(material.transparent, true);
    assert.equal(material.depthWrite, false);
    assert.equal(actor.outlines[0].visible, true);
    assert.equal(actor.group.children[0].castShadow, false);
    assert.deepEqual(material.userData.state.actorLight.value.toArray(), [0, 0, 0]);
    graphics.updateActor(1, 1, { visible: true, opacity: 1, tint: [0, 0, 0], outline: [0, 0, 0, 0] }, []);
    assert.equal(material.transparent, false);
    assert.equal(material.depthWrite, true);
    assert.equal(actor.outlines[0].visible, false);
    assert.equal(actor.skeleton.boneMatrices[12], 7);
    await graphics.dispose();
});

test('actor overlays use decoded texture handles and leave the base material intact when removed', async () => {
    const { graphics } = await fixture();
    graphics.createTexture(-7, 1, 1, new Uint8Array([255, 100, 0, 255]));
    const material = graphics.scene(1).actors.get(1).group.children[0].material;
    graphics.updateActor(1, 1, { visible: true, overlayTextureId: -7, overlayForm: 4 }, []);
    assert.equal(material.userData.overlayActive, true);
    assert.equal(material.userData.state.overlay.value, graphics.texture(-7));
    graphics.animateMaterial(material, 1);
    assert.ok(material.userData.state.overlayOffset.value.x > 0);
    graphics.updateActor(1, 1, { visible: true, overlayTextureId: 0 }, []);
    assert.equal(material.userData.overlayActive, false);
    assert.equal(material.colorNode, material.userData.baseColorNode);
    await graphics.dispose();
});

test('native raw Sprite colors, multiplicative particles and reversed depth preserve their distinct semantics', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.beginFrame(100, 100);
    const colors = [0.5, 0.5, 0.5, 0.25, 0.5, 0.5, 0.5, 0.25, 0.5, 0.5, 0.5, 0.25];
    graphics.drawTriangles(triangle, uv, colors, [0, 1, 2], 0, identity, 'alpha', true, false, null, 0, null, true,
        [6, 1, 0, 0, 5, 0], null, 5);
    const clip = graphics.commands[0].batch.clip.value;
    graphics.present();
    const mesh = renderer.calls.find(call => call[0] === 'draw')[2][0].mesh;
    assert.equal(mesh.geometry.getAttribute('color').array[0], 0.5);
    assert.equal(mesh.geometry.getAttribute('color').array[3], 0.25);
    assert.equal(mesh.material.blendSrc, THREE.DstColorFactor);
    assert.equal(mesh.material.blendDst, THREE.ZeroFactor);
    assert.equal(mesh.material.depthFunc, THREE.LessEqualDepth);
    assert.deepEqual(mesh.matrixWorld.elements, identity);
    assert.equal(clip.elements[10], -1);
    assert.equal(clip.elements[14], 1);
    await graphics.dispose();
});

test('native combined MVP retains clip W and backend depth conversion without an affine world transform', async () => {
    const reverse = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 1, 0, 0, 0, 1);
    const webGlDepth = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2, -1, 0, 0, 0, 1);
    const camera = new THREE.PerspectiveCamera(50, 4 / 3, 1, 20);
    camera.coordinateSystem = THREE.WebGPUCoordinateSystem;
    camera.updateProjectionMatrix();
    const authored = camera.projectionMatrix.clone().premultiply(reverse);
    for (const coordinateSystem of [THREE.WebGPUCoordinateSystem, THREE.WebGLCoordinateSystem]) {
        const renderer = fakeRenderer(coordinateSystem);
        const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
        graphics.beginFrame(128, 96);
        for (const kind of [1, 2, 3]) {
            graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 0, authored.elements, 'alpha', true, true,
                null, 0, null, false, null, null, 5, null, kind);
            const batch = graphics.commands.at(-1).batch;
            const expected = camera.projectionMatrix.clone();
            if (coordinateSystem === THREE.WebGLCoordinateSystem) expected.premultiply(webGlDepth);
            assert.deepEqual(batch.clip.value.elements, expected.elements);
            assert.deepEqual(batch.mesh.matrixWorld.elements, identity);
            assert.equal(batch.mesh.material.vertexNode.isNode, true);
            const projected = new THREE.Vector4(0.2, 0.3, -4, 1).applyMatrix4(batch.clip.value);
            assert.equal(projected.w, 4);
            assert.notEqual(projected.z, 0);
        }
        graphics.present();
        await graphics.dispose();
    }
});

test('native texture formats and shader kinds preserve raw UI coverage while decoding scene inputs', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    assert.equal(renderer.outputColorSpace, THREE.LinearSRGBColorSpace);
    const rgba = new Uint8Array([128, 128, 128, 128]);
    graphics.createTexture(1, 1, 1, rgba);
    graphics.createTexture(2, 1, 1, rgba, false, 1);
    graphics.createTexture(-1, 1, 1, rgba);
    assert.equal(graphics.texture(1).colorSpace, THREE.NoColorSpace);
    assert.equal(graphics.texture(2).colorSpace, THREE.SRGBColorSpace);
    assert.equal(graphics.texture(-1).colorSpace, THREE.SRGBColorSpace);
    assert.deepEqual(Array.from(graphics.texture(1).image.data), Array.from(rgba));
    graphics.createTexture(3, 4, 4, null, true, 0);
    graphics.createTexture(4, 4, 4, null, true, 1);
    assert.equal(graphics.target(3).texture.colorSpace, THREE.NoColorSpace);
    assert.equal(graphics.target(4).texture.colorSpace, THREE.SRGBColorSpace);
    const colors = new Float32Array(12).fill(0.5);
    for (const kind of [1, 2, 3]) {
        graphics.beginFrame(128, 96);
        graphics.drawTriangles(triangle, uv, colors, [0, 1, 2], 1, identity, 'alpha', false, false,
            null, 0, null, true, null, null, 3, null, kind);
        const channel = graphics.commands[0].batch.mesh.geometry.getAttribute('color').array[0];
        assert.ok(Math.abs(channel - (kind === 2 ? 0.214041 : 0.5)) < 0.00001);
        graphics.present();
    }
    let cleared;
    renderer.setClearColor = color => { cleared = color.toArray(); };
    graphics.clear(51 / 255, 128 / 255, 0);
    graphics.present();
    assert.deepEqual(cleared, [51 / 255, 128 / 255, 0]);
    await graphics.dispose();
});

test('portrait display encoding surrounds only the selected actor render and retains the caller target', async () => {
    const renderer = fakeRenderer();
    const order = [];
    const intermediate = new THREE.RenderTarget(64, 64);
    const display = { begin(target, width, height) {
        order.push(['begin', target, width, height]); renderer.setRenderTarget(intermediate);
    }, compose(target, exposure) {
        order.push(['compose', target, exposure]); renderer.setRenderTarget(target);
    }, dispose() { intermediate.dispose(); } };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, display });
    graphics.beginFrame(128, 96);
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: triangle, material: { unlit: true } });
    graphics.createActor(1, 1, [1], 0);
    graphics.createTexture(8, 64, 64, null, true, 0);
    const target = graphics.target(8);
    graphics.setRenderTarget(8);
    graphics.drawPortrait(1, [1], { ...frame, exposure: 1.25 });
    graphics.present();
    assert.deepEqual(order, [['begin', target, 64, 64], ['compose', target, 1.25]]);
    assert.equal(renderer.calls.find(call => call[0] === 'draw')[1], intermediate);
    assert.equal(graphics.texture(8), target.texture);
    await graphics.dispose();
});

test('render target scissor and viewport use asymmetric top-left coordinates', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.createTexture(9, 128, 96, null, true);
    graphics.beginFrame(128, 96);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 0, identity, 'alpha', false, false, [70, 3, 17, 11], 9, [4, 2, 100, 80]);
    graphics.present();
    const captured = renderer.calls.find(call => call[0] === 'draw')[3];
    assert.deepEqual(captured.scissor, [70, 3, 17, 11]);
    assert.deepEqual(captured.viewport, [4, 2, 100, 80]);
    assert.equal(captured.clipped, true);
    await graphics.dispose();
});

test('native heap views are cached across frames and rebuilt after WASM memory growth', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    function heap() {
        const memory = new ArrayBuffer(512);
        new Float32Array(memory, 16, 9).set(triangle);
        new Float32Array(memory, 64, 6).set(uv);
        new Float32Array(memory, 96, 12).fill(1);
        new Uint32Array(memory, 160, 3).set([0, 1, 2]);
        new Float32Array(memory, 176, 16).set(identity);
        new Int32Array(memory, 256, 4).set([0, 0, 128, 96]);
        return memory;
    }
    const first = heap();
    function draw(memory) {
        graphics.beginFrame(128, 96);
        graphics.drawNativeMemory(memory, 16, 64, 96, 3, 160, 3, 0, 176, 1, false, false, 0, 256, 0, false, 0, 0);
        graphics.present();
    }
    draw(first);
    const original = graphics.memorySlots[0].positions.view;
    draw(first);
    assert.equal(graphics.memorySlots[0].positions.view, original);
    const grown = heap();
    draw(grown);
    assert.notEqual(graphics.memorySlots[0].positions.view, original);
    assert.equal(graphics.memorySlots[0].positions.view.buffer, grown);
    await graphics.dispose();
});

test('packed partial dirty updates reuse grow-only staging buffers and cached palette views', () => {
    const buffers = [];
    const adapter = createPackedImports({ createScene() {}, updateActor(id, actorId, state, palette) { buffers.push(palette); } });
    adapter.createScene(1);
    const empty = new Uint8Array(0);
    function update(count) {
        const meta = new Int32Array(count * 4);
        const states = new Float32Array(count * 24);
        const bones = new Float32Array(count * 12);
        for (let index = 0; index < count; index++) meta.set([index + 1, index * 12, 12, 1], index * 4);
        adapter.flushScenePacked(1, new Uint8Array(meta.buffer), new Uint8Array(states.buffer), new Uint8Array(bones.buffer), empty, empty);
    }
    update(2);
    const first = buffers[0];
    update(1);
    assert.equal(buffers[2], first);
    update(2);
    assert.equal(buffers[3], first);
});

test('packed nonzero camera coordinates keep authored skies centered and sun shadows focused', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    const imports = createPackedImports(graphics);
    imports.createScene(1);
    imports.configureScene(1, JSON.stringify({ shadowMapSize: 1024 }));
    imports.setSkyParameters(1, 0, 30, 1);
    const packed = new Float32Array(68);
    packed.set(identity, 0);
    packed.set(identity, 16);
    packed.set([1024, 700, -2048], 32);
    packed.set([1200, 200, -1900], 35);
    packed.set([0.5, 0.5, 0.5], 38);
    packed.set([0.3, -1, 0.2], 44);
    const empty = new Uint8Array(0);
    imports.drawFramePacked(1, new Uint8Array(packed.buffer), empty, empty, empty, empty, empty);
    graphics.present();
    const resource = graphics.scene(1);
    assert.deepEqual(resource.sky[0].position.toArray(), [1024, 670, -2048]);
    assert.deepEqual(resource.sun.target.position.toArray(), [1200, 200, -1900]);
    assert.equal(resource.sun.shadow.mapSize.width, 1024);
    await graphics.dispose();
});

test('real reversed perspective projections retain near and far planes on both backend clip ranges', async () => {
    const source = new THREE.PerspectiveCamera(60, 1.6, 0.2, 1000);
    source.coordinateSystem = THREE.WebGPUCoordinateSystem;
    source.updateProjectionMatrix();
    const reverse = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 1, 0, 0, 0, 1);
    const packedProjection = new THREE.Matrix4().multiplyMatrices(reverse, source.projectionMatrix).elements;
    for (const backend of [THREE.WebGPUCoordinateSystem, THREE.WebGLCoordinateSystem]) {
        const { graphics } = await fixture(fakeRenderer(backend));
        graphics.drawScene(1, { ...frame, projection: packedProjection });
        const camera = graphics.commands[0].camera;
        assert.ok(Math.abs(camera.near - 0.2) < 0.00001);
        assert.ok(Math.abs(camera.far - 1000) < 0.001);
        assert.equal(camera.isPerspectiveCamera, true);
        assert.ok(Math.abs(camera.fov - 60) < 0.00001);
        await graphics.dispose();
    }
});

test('explicit composition keeps native world effects in HDR and the following HUD in the output target', async () => {
    const renderer = fakeRenderer();
    const target = new THREE.RenderTarget(64, 48);
    const post = { active: false, target, scale: 0.5, beginFrame() {}, finishScene() {}, dispose() { target.dispose(); },
        beginScene() { this.active = true; return target; }, compose() { this.active = false; } };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, post });
    graphics.beginFrame(128, 96);
    graphics.createScene(1);
    graphics.drawScene(1, frame);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 0, identity, 'additive', false, false, null, 0, [0, 0, 128, 96],
        false, null, null, 3, null, 3, [1, 0, 0, 0, 0, 0, 0, 0]);
    graphics.composeScene(1);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 0, identity);
    graphics.present();
    const draws = renderer.calls.filter(call => call[0] === 'draw');
    assert.equal(draws[0][1], target);
    assert.equal(draws[1][1], target);
    assert.deepEqual(draws[1][3].viewport, [0, 0, 64, 48]);
    assert.equal(draws[2][1], null);
    assert.equal(graphics.commands[1].batch.pixel.hdr.value, 1);
    assert.equal(graphics.commands[3].batch.pixel.hdr.value, 0);
    await graphics.dispose();
});

test('transparent sky layers render before world queues into the same cleared post target', async () => {
    const renderer = fakeRenderer();
    const target = new THREE.RenderTarget(64, 48);
    const seen = [];
    const originalRender = renderer.render;
    renderer.render = function(scene, camera) {
        seen.push({ scene, target: this.target });
        originalRender.call(this, scene, camera);
    };
    const post = { active: false, target, beginFrame() {}, finishScene() {}, dispose() { target.dispose(); },
        beginScene() { renderer.setRenderTarget(target); renderer.clear(true, true, false); this.active = true; return target; },
        compose() { this.active = false; } };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, post });
    graphics.beginFrame(128, 96);
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: triangle, material: { unlit: true } });
    graphics.createMesh(1, 2, { positions: triangle, isSky: true, material: { unlit: true, blend: 'alpha' } });
    graphics.createStage(1, 1, [1]);
    graphics.setSkyLayers(1, [2], [], 0, 0, 1);
    const resource = graphics.scene(1);
    const sky = resource.skyObjects[0].get(2);
    assert.equal(sky.material.transparent, true);
    assert.equal(sky.material.depthTest, false);
    assert.equal(sky.material.depthWrite, false);
    assert.equal(sky.material.mrtNode, null);
    assert.equal(resource.sky[0].parent, resource.skyScene);
    assert.equal(resource.scene.children.includes(resource.sky[0]), false);
    graphics.drawScene(1, frame);
    graphics.present();
    assert.deepEqual(seen, [{ scene: resource.skyScene, target }, { scene: resource.scene, target }]);
    assert.deepEqual(renderer.calls.filter(call => call[0] !== 'size').map(call => call[0]), ['clear', 'draw', 'draw']);
    let skyDisposals = 0;
    sky.material.addEventListener('dispose', () => skyDisposals++);
    await graphics.dispose();
    assert.equal(skyDisposals, 1);
});

test('unlit sky compiles for unnamed single targets and applies neutral MRT only to SSR targets', async () => {
    const { graphics, renderer: sceneRenderer } = await fixture();
    graphics.createMesh(1, 2, { positions: triangle, isSky: true, material: { unlit: true, blend: 'alpha' } });
    graphics.setSkyLayers(1, [2], [], 0, 0, 1);
    const sky = graphics.scene(1).skyObjects[0].get(2);
    const renderer = new THREE.WebGPURenderer({ canvas: {
        width: 128, height: 96, style: {}, addEventListener() {}, removeEventListener() {}
    } });
    renderer.hasFeature = () => false;
    renderer._bindings = undefined;
    sceneRenderer.getMRT = () => renderer.getMRT();
    for (const count of [1, 2, 1]) {
        const target = new THREE.RenderTarget(128, 96, { count });
        if (count === 2) {
            target.textures[0].name = 'output';
            target.textures[1].name = 'normalRoughness';
        }
        renderer.setRenderTarget(target);
        renderer.setMRT(count === 2 ? graphics.scene(1).skyMrt : null);
        graphics.drawScene(1, frame);
        graphics.present();
        assert.equal(sky.material.mrtNode !== null, count === 2);
        const builder = renderer.backend.createNodeBuilder(sky, renderer);
        builder.camera = new THREE.PerspectiveCamera();
        builder.scene = graphics.scene(1).skyScene;
        builder.sortBindingGroups = () => {};
        builder.build();
        assert.match(builder.fragmentShader, /@fragment/);
        assert.doesNotMatch(builder.fragmentShader, /struct\s+OutputType\s*\{\s*\}/);
        if (count === 2) assert.match(builder.fragmentShader, /@location\(\s*1\s*\)/);
        target.dispose();
    }
    await graphics.dispose();
});

test('external canvas backing resize refreshes Three even when logical size is unchanged', async () => {
    const renderer = fakeRenderer();
    const canvas = { dataset: {}, width: 1, height: 1 };
    let pixelRatio = 1;
    renderer.setPixelRatio = value => { pixelRatio = value; };
    renderer.getPixelRatio = () => pixelRatio;
    const originalSetSize = renderer.setSize;
    renderer.setSize = function(width, height) {
        originalSetSize.call(this, width, height);
        canvas.width = Math.floor(width * pixelRatio);
        canvas.height = Math.floor(height * pixelRatio);
    };
    const graphics = await createThreeGraphics(canvas, { renderer });
    renderer.setPixelRatio(1.5);
    graphics.beginFrame(127, 95);
    assert.deepEqual([canvas.width, canvas.height], [190, 142]);
    canvas.width = 700;
    canvas.height = 500;
    graphics.beginFrame(127, 95);
    assert.deepEqual([canvas.width, canvas.height], [190, 142]);
    graphics.beginFrame(127, 95);
    assert.equal(renderer.calls.filter(call => call[0] === 'size').length, 2, 'unchanged matching canvas must not resize every frame');
    await graphics.dispose();
});

test('portraits exclude the sky and leave its world layers ready for the following frame', async () => {
    const { graphics, renderer } = await fixture();
    graphics.createMesh(1, 2, { positions: triangle, isSky: true, material: { unlit: true, blend: 'alpha' } });
    graphics.setSkyLayers(1, [2], [], 0, 0, 1);
    const resource = graphics.scene(1);
    const seen = [];
    const originalRender = renderer.render;
    renderer.render = function(scene, camera) {
        seen.push(scene);
        originalRender.call(this, scene, camera);
    };
    graphics.createTexture(8, 64, 64, null, true);
    graphics.setRenderTarget(8);
    graphics.drawPortrait(1, [1], frame);
    graphics.setRenderTarget(0);
    graphics.drawScene(1, frame);
    graphics.present();
    assert.deepEqual(seen, [resource.scene, resource.skyScene, resource.scene]);
    assert.equal(resource.sky[0].visible, true);
    assert.equal(resource.actors.get(1).group.visible, true);
    seen.length = 0;
    graphics.setSkyLayers(1, [], [2], 1, 0, 1);
    graphics.setSkyParameters(1, 0, 0, 1);
    graphics.drawScene(1, frame);
    graphics.present();
    assert.deepEqual(seen, [resource.scene], 'expired sky transitions must not add an empty background pass');
    await graphics.dispose();
});

test('native shader parameters and numeric depth booleans reach the actual material state', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.beginFrame(128, 96);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 0, identity, 'alpha', 0, 0, null, 0, null,
        false, null, null, 3, null, 2, [0.5, 0.25, 1, 0.4, 2, 1, 0, 0]);
    const batch = graphics.commands[0].batch;
    assert.equal(batch.mesh.material.depthTest, false);
    assert.equal(batch.mesh.material.depthWrite, false);
    assert.deepEqual(batch.pixel.tint.value.toArray(), [0.5, 0.25, 1, 0.4]);
    assert.equal(batch.pixel.exposure.value, 2);
    assert.equal(batch.pixel.display.value, 1);
    await graphics.dispose();
});

test('scene textures retain mipmaps while native HUD samplers clamp without atlas mip bleeding', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    const imports = createPackedImports(graphics);
    imports.createScene(1);
    imports.createSceneTexture(1, -1, 2, 2, new Uint8Array(16).fill(255));
    assert.equal(graphics.texture(-1).generateMipmaps, true);
    assert.equal(graphics.texture(-1).minFilter, THREE.LinearMipmapLinearFilter);
    assert.equal(graphics.texture(-1).wrapS, THREE.RepeatWrapping);
    graphics.createTexture(1, 2, 2, new Uint8Array(16).fill(255));
    graphics.beginFrame(128, 96);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 1, identity, 'alpha', false, false, null, 0, null,
        false, null, null, 3, [1, 1, 1, 1]);
    graphics.present();
    assert.equal(graphics.texture(1).generateMipmaps, false);
    assert.equal(graphics.texture(1).wrapS, THREE.ClampToEdgeWrapping);
    assert.equal(graphics.texture(1).magFilter, THREE.NearestFilter);
    await graphics.dispose();
});

test('animated scenery advances its own material clock alongside the CPU bone palette', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: triangle, uv, boneIndices: [0, 0, 0], boneCount: 1,
        material: { unlit: true, windX: 8 } });
    graphics.createStage(1, 1, [1]);
    graphics.updateMeshPose(1, 1, [1, 0, 0, 10, 0, 1, 0, 20, 0, 0, 1, 30]);
    graphics.drawScene(1, { ...frame, timeSeconds: 3 });
    graphics.present();
    const mesh = graphics.scene(1).meshes.get(1);
    assert.equal(mesh.scenery.material.userData.state.clock.value, 3);
    assert.equal(mesh.skeleton.boneMatrices[12], 10);
    await graphics.dispose();
});

test('opt-in scenery bounds follow published WORLD poses, camera-independent shadows and shared stage ownership', async () => {
    const renderer = fakeRenderer();
    renderer.info = { render: {}, reset() {} };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile: true, stageSkinCulling: true });
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: triangle, uv, boneIndices: [0, 0, 0], boneCount: 1, material: { unlit: true } });
    graphics.createStage(1, 1, [1]); graphics.createStage(1, 2, [1]);
    const resource = graphics.scene(1), mesh = resource.meshes.get(1), bounds = mesh.stageBounds;
    const first = resource.stages.get(1).children[0], second = resource.stages.get(2).children[0];
    assert.equal(bounds.owners.size, 2);
    assert.equal(first.frustumCulled, false);
    graphics.updateMeshPose(1, 1, [1, 0, 0, 100, 0, 1, 0, 0, 0, 0, 1, 0]);
    const point = new THREE.Vector3();
    for (let vertex = 0; vertex < 3; vertex++) {
        first.getVertexPosition(vertex, point);
        assert.equal(first.boundingSphere.containsPoint(point), true);
    }
    const main = new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4(), THREE.WebGPUCoordinateSystem);
    const lightCamera = new THREE.OrthographicCamera(90, 110, 10, -10, .1, 30);
    lightCamera.coordinateSystem = THREE.WebGPUCoordinateSystem;
    lightCamera.position.z = 10;
    lightCamera.updateProjectionMatrix(); lightCamera.updateMatrixWorld();
    const shadow = new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(
        lightCamera.projectionMatrix, lightCamera.matrixWorldInverse), THREE.WebGPUCoordinateSystem);
    assert.equal(first.intersectsFrustum(main), false);
    assert.equal(first.intersectsFrustum(shadow), true);
    assert.equal(first.visible, true); assert.equal(first.castShadow, true);
    graphics.drawScene(1, frame); graphics.present();
    assert.equal(graphics.getProfile().stageCulling.outsideMain, 2);
    graphics.removeStage(1, 1);
    assert.equal(first.frustumCulled, false);
    assert.equal(bounds.owners.size, 1);
    graphics.createStage(1, 3, [1]);
    const rebuilt = resource.stages.get(3).children[0];
    assert.equal(rebuilt.boundingSphere, second.boundingSphere);
    assert.equal(rebuilt.frustumCulled, true, 'rebuild does not wait for a redundant palette publication');
    graphics.updateMeshPose(1, 1, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]);
    assert.equal(second.intersectsFrustum(main), true);
    assert.equal(rebuilt.intersectsFrustum(main), true);
    assert.equal(graphics.getProfile().stageCulling.insideMain, 2);
    assert.throws(() => graphics.updateMeshPose(1, 1, []), /Invalid scene bone palette/);
    assert.equal(second.frustumCulled, false); assert.equal(rebuilt.frustumCulled, false);
    assert.equal(graphics.getProfile().stageCulling.invalidFallback, 2);
    graphics.removeMesh(1, 1);
    assert.equal(bounds.owners.size, 0);
    assert.equal(bounds.disposed, true);
    assert.equal(resource.stages.get(2).children.length, 0);
    assert.equal(resource.stages.get(3).children.length, 0);
    await graphics.dispose();
});

test('scenery culling honors its feature flag independently of profiling and never owns actor or sky bounds', async () => {
    for (const [profile, enabled] of [[false, true], [true, true], [false, false], [true, false]]) {
        const renderer = fakeRenderer(); renderer.info = { render: {}, reset() {} };
        const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile, stageSkinCulling: enabled });
        graphics.createScene(1);
        graphics.createMesh(1, 1, { positions: triangle, boneIndices: [0, 0, 0], boneCount: 1, material: { unlit: true } });
        graphics.createStage(1, 1, [1]);
        graphics.updateMeshPose(1, 1, [1, 0, 0, 100, 0, 1, 0, 0, 0, 0, 1, 0]);
        assert.equal(!!graphics.scene(1).meshes.get(1).stageBounds, enabled);
        assert.equal(graphics.scene(1).meshes.get(1).scenery.frustumCulled, enabled);
        if (!profile) assert.equal(graphics.profiler, null);
        await graphics.dispose();
    }
    const renderer = fakeRenderer(); renderer.info = { render: {}, reset() {} };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile: true, stageSkinCulling: true });
    graphics.createScene(1);
    for (const [id, isSky] of [[1, false], [2, true]]) {
        graphics.createMesh(1, id, { positions: triangle, boneIndices: [0, 0, 0], boneCount: 1, isSky, material: { unlit: true } });
    }
    graphics.createActor(1, 1, [1], 1); graphics.createStage(1, 1, [2]);
    assert.equal(graphics.scene(1).meshes.get(1).stageBounds, null);
    assert.equal(graphics.scene(1).meshes.get(2).stageBounds, null);
    assert.equal(graphics.getProfile().stageCulling.eligibleSkinned, 0);
    await graphics.dispose();
});

test('stage census preserves the last world camera after a pooled command is reused for a portrait', async () => {
    const renderer = fakeRenderer(); renderer.info = { render: {}, reset() {} };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile: true, stageSkinCulling: true });
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: triangle, boneIndices: [0, 0, 0], boneCount: 1, material: { unlit: true } });
    graphics.createStage(1, 1, [1]);
    graphics.updateMeshPose(1, 1, [1, 0, 0, 100, 0, 1, 0, 0, 0, 0, 1, 0]);
    graphics.drawScene(1, frame); graphics.present();
    const resource = graphics.scene(1), camera = resource.lastWorldCamera;
    assert.notEqual(camera, graphics.commands[0].camera);
    assert.equal(graphics.getProfile().stageCulling.outsideMain, 1);
    const portraitView = new THREE.Matrix4().makeTranslation(-100, 0, 0).elements;
    graphics.drawPortrait(1, [], { ...frame, view: portraitView });
    assert.equal(graphics.getProfile().stageCulling.outsideMain, 1, 'preparing a portrait cannot mutate the last world snapshot');
    graphics.present();
    assert.equal(resource.lastWorldCamera, camera);
    assert.equal(graphics.getProfile().stageCulling.outsideMain, 1, 'executing the reused portrait command preserves world classifications');
    assert.equal(graphics.getProfile().stageCulling.insideMain, 0);
    await graphics.dispose();
});

test('selected actor colour add follows the view-dependent rim without replacing its albedo', async () => {
    const { graphics } = await fixture();
    const actor = graphics.scene(1).actors.get(1);
    const material = actor.group.children[0].material;
    const baseColor = material.color.toArray();
    const baseNode = material.colorNode;
    graphics.updateActor(1, 1, { visible: true, colorAdd: [0.4, 0.4, 0.4], tint: [0, 0, 0], floorLight: 1 }, []);
    assert.deepEqual(material.color.toArray(), baseColor);
    assert.equal(material.colorNode, baseNode);
    const operations = new Set();
    material.emissiveNode.traverse(node => { if (node.isMathNode) operations.add(node.method); });
    assert.ok(operations.has('dot') && operations.has('pow'), 'selection emission lost its normal/view rim falloff');
    graphics.updateActor(1, 1, { visible: true, colorAdd: [0, 0, 0], tint: [0, 0, 0], floorLight: 1 }, []);
    assert.deepEqual(material.userData.state.colorAdd.value.toArray(), [0, 0, 0]);
    await graphics.dispose();
});

test('portrait targets retain their GPU texture identity and version across point/linear sampler changes', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.createTexture(12, 122, 122, null, true);
    const portrait = graphics.texture(12);
    const target = graphics.target(12);
    const version = portrait.version;
    let disposed = 0;
    portrait.addEventListener('dispose', () => disposed++);
    assert.equal(portrait.isRenderTargetTexture, true);
    for (const filter of [1, 0, 1, 0]) {
        graphics.beginFrame(128, 96);
        graphics.setRenderTarget(12);
        graphics.clear(0, 0, 0, 0);
        graphics.setRenderTarget(0);
        graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 12, identity, 'alpha', false, false, null, 0, null,
            false, null, null, 3, [1, 1, filter, 1]);
        graphics.present();
        assert.equal(graphics.texture(12), portrait);
        assert.equal(graphics.target(12), target);
        assert.equal(portrait.version, version, 'sampler change invalidated a render-target image');
        assert.equal(disposed, 0);
    }
    await graphics.dispose();
});

test('WebGL portrait sampler changes refresh only parameters without recreating the attachment', async () => {
    const renderer = fakeRenderer(THREE.WebGLCoordinateSystem);
    let parameterUpdates = 0;
    renderer.backend = { isWebGLBackend: true, get() { return { textureGPU: {}, glTextureType: 1 }; },
        state: { bindTexture() {} }, textureUtils: { setTextureParameters() { parameterUpdates++; } } };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.createTexture(12, 122, 122, null, true);
    const portrait = graphics.texture(12);
    const version = portrait.version;
    for (const filter of [1, 0, 1]) {
        graphics.beginFrame(128, 96);
        graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 12, identity, 'alpha', false, false, null, 0, null,
            false, null, null, 3, [1, 1, filter, 1]);
        graphics.present();
    }
    assert.equal(parameterUpdates, 3);
    assert.equal(portrait.version, version);
    await graphics.dispose();
});

test('static materials omit actor-only deformation and highlight nodes while actors retain them', async () => {
    const { graphics } = await fixture();
    const resource = graphics.scene(1);
    const terrain = resource.meshes.get(1).material;
    const actor = resource.actors.get(1).group.children[0].material;
    assert.equal(terrain.positionNode ?? null, null);
    assert.equal(terrain.emissiveNode ?? null, null);
    assert.equal(terrain.userData.state.wobble, undefined);
    assert.equal(terrain.userData.state.colorAdd, undefined);
    assert.ok(actor.positionNode && actor.emissiveNode);
    assert.ok(actor.userData.state.wobble && actor.userData.state.colorAdd);
    graphics.createMesh(1, 2, { positions: triangle, uv, material: { windX: 8, windZ: 28 } });
    const foliage = resource.meshes.get(2).material;
    assert.ok(foliage.positionNode, 'authored static wind was removed with the actor deformation');
    assert.equal(foliage.userData.state.wobble, undefined);
    await graphics.dispose();
});

test('actor WORLD bounds cull body and outline per camera without hiding off-camera shadow casters', async () => {
    const { graphics } = await fixture();
    const state = { visible: true, castsShadow: true, boundsMin: [99, -2, -2], boundsMax: [101, 2, 2],
        outline: [1, 0.8, 0.2, 1], wobble: true };
    graphics.updateActor(1, 1, state, [1, 0, 0, 100, 0, 1, 0, 0, 0, 0, 1, 0]);
    const actor = graphics.scene(1).actors.get(1);
    const body = actor.group.children[0];
    const shell = actor.outlines[0];
    assert.equal(body.frustumCulled, true);
    assert.equal(shell.frustumCulled, true);
    assert.equal(body.boundingSphere, shell.boundingSphere);
    assert.deepEqual(body.boundingSphere.center.toArray(), [100, 0, 0]);
    assert.ok(body.boundingSphere.radius >= 3 + Math.sqrt(12) + 2.5);
    const main = new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4(), THREE.WebGPUCoordinateSystem);
    const lightCamera = new THREE.OrthographicCamera(90, 110, 10, -10, 0.1, 30);
    lightCamera.coordinateSystem = THREE.WebGPUCoordinateSystem;
    lightCamera.position.set(0, 0, 10);
    lightCamera.updateProjectionMatrix();
    lightCamera.updateMatrixWorld();
    const shadow = new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(
        lightCamera.projectionMatrix, lightCamera.matrixWorldInverse), THREE.WebGPUCoordinateSystem);
    assert.equal(body.intersectsFrustum(main), false);
    assert.equal(body.intersectsFrustum(shadow), true);
    assert.equal(actor.group.visible, true);
    assert.equal(body.castShadow, true);
    const sphere = body.boundingSphere;
    graphics.updateActor(1, 1, { ...state, boundsMin: [NaN, 0, 0] }, []);
    assert.equal(body.frustumCulled, false, 'invalid or unprepared bounds must not hide a character');
    assert.equal(body.boundingSphere, sphere);
    await graphics.dispose();
});

test('packed actor bounds preserve C# world coordinates without another actor transform or axis flip', () => {
    let received;
    const imports = createPackedImports({ createScene() {}, updateActor(id, actorId, state) {
        received = { min: Array.from(state.boundsMin), max: Array.from(state.boundsMax) };
    } });
    imports.createScene(1);
    const meta = new Int32Array([1, 0, 0, 1]);
    const state = new Float32Array(24);
    state.set([1024, 20, -2048], 16);
    state.set([1100, 500, -1800], 19);
    const empty = new Uint8Array(0);
    imports.flushScenePacked(1, new Uint8Array(meta.buffer), new Uint8Array(state.buffer), empty, empty, empty);
    assert.deepEqual(received, { min: [1024, 20, -2048], max: [1100, 500, -1800] });
});

test('static stage batching reduces scene objects and owns only merged geometry', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    graphics.createScene(1);
    for (const id of [1, 2]) graphics.createMesh(1, id, { positions: triangle, uv, material: { unlit: true } });
    const resource = graphics.scene(1);
    let sourceDisposals = 0;
    for (const mesh of resource.meshes.values()) {
        mesh.geometry.addEventListener('dispose', () => sourceDisposals++);
        mesh.material.addEventListener('dispose', () => sourceDisposals++);
    }
    const members = [1, 2];
    graphics.createStage(1, 7, members);
    members.length = 0;
    const group = resource.stages.get(7);
    assert.equal(group.children.length, 1, 'equivalent opaque sources should share one spatial batch');
    assert.deepEqual(group.userData.meshIds, [1, 2]);
    const merged = group.children[0].geometry;
    assert.equal(merged.getIndex().count, 6);
    assert.equal(group.children[0].material, resource.meshes.get(1).material);
    assert.equal(resource.meshes.get(1).scenery, null);
    assert.equal(resource.meshes.get(2).scenery, null);
    let mergedDisposals = 0;
    merged.addEventListener('dispose', () => mergedDisposals++);
    graphics.removeStage(1, 7);
    graphics.removeStage(1, 7);
    assert.equal(mergedDisposals, 1);
    assert.equal(sourceDisposals, 0);
    await graphics.dispose();
    assert.equal(sourceDisposals, 4);
});

test('replacing or removing an active static source rebuilds batches before source disposal', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    graphics.createScene(1);
    for (const id of [1, 2]) graphics.createMesh(1, id, { positions: triangle, uv, material: { unlit: true } });
    graphics.createStage(1, 7, [1, 2]);
    graphics.createStage(1, 8, [1, 2]);
    const resource = graphics.scene(1);
    const oldSource = resource.meshes.get(1);
    let oldMergedDisposals = 0;
    let oldSourceDisposals = 0;
    for (const group of resource.stages.values()) group.children[0].geometry.addEventListener('dispose', () => oldMergedDisposals++);
    oldSource.geometry.addEventListener('dispose', () => oldSourceDisposals++);
    oldSource.material.addEventListener('dispose', () => oldSourceDisposals++);
    graphics.createMesh(1, 1, { positions: triangle.map((value, index) => value + (index % 3 === 0 ? 10 : 0)),
        uv, material: { unlit: true } });
    assert.equal(oldMergedDisposals, 2);
    assert.equal(oldSourceDisposals, 2);
    let replacedMergedDisposals = 0;
    for (const group of resource.stages.values()) {
        assert.deepEqual(group.userData.meshIds, [1, 2]);
        assert.equal(group.children.length, 1);
        assert.equal(group.children[0].geometry.boundingBox.max.x, 11);
        assert.equal(group.children[0].material, resource.meshes.get(1).material);
        group.children[0].geometry.addEventListener('dispose', () => replacedMergedDisposals++);
    }
    const survivor = resource.meshes.get(2);
    graphics.removeMesh(1, 1);
    assert.equal(replacedMergedDisposals, 2);
    for (const group of resource.stages.values()) {
        assert.deepEqual(group.userData.meshIds, [2]);
        assert.equal(group.children.length, 1);
        assert.equal(group.children[0].geometry, survivor.geometry);
        assert.equal(group.children[0].material, survivor.material);
    }
    await graphics.dispose();
});

test('unbatched animated stage materials are disposed per object without disposing shared sources', async () => {
    const { graphics } = await fixture();
    const resource = graphics.scene(1);
    const mesh = resource.meshes.get(1);
    graphics.createStage(1, 7, [1]);
    graphics.createStage(1, 8, [1]);
    let clonesDisposed = 0;
    let sourceDisposed = 0;
    for (const group of resource.stages.values()) group.children[0].material.addEventListener('dispose', () => clonesDisposed++);
    mesh.material.addEventListener('dispose', () => sourceDisposed++);
    graphics.removeStage(1, 7);
    graphics.removeStage(1, 8);
    assert.equal(clonesDisposed, 2);
    assert.equal(sourceDisposed, 0);
    await graphics.dispose();
    assert.equal(sourceDisposed, 1);
});

test('portrait targets, clears, scene draws and native HUD samples preserve their call order', async () => {
    const { graphics, renderer } = await fixture();
    graphics.createTexture(8, 64, 64, null, true);
    graphics.setRenderTarget(8);
    graphics.clear(0, 0, 0, 0);
    graphics.drawPortrait(1, [1], frame);
    graphics.setRenderTarget(0);
    graphics.clear(0, 0, 0, 1);
    graphics.drawScene(1, frame);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 8, identity);
    graphics.present();
    const calls = renderer.calls.filter(call => call[0] !== 'size');
    assert.deepEqual(calls.map(call => call[0]), ['clear', 'draw', 'clear', 'draw', 'draw']);
    assert.equal(calls[0][1], graphics.textures.get(8).target);
    assert.equal(calls[1][1], calls[0][1]);
    assert.equal(calls[2][1], null);
    assert.equal(calls[4][2][0].mesh.material.map, graphics.textures.get(8).target.texture);
    await graphics.dispose();
});

test('native batches copy WASM views before they are reused and retain GPU objects on the next frame', async () => {
    const renderer = fakeRenderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    const vertices = new Float32Array(triangle);
    graphics.beginFrame(1280, 720);
    graphics.drawTriangles(vertices, uv, null, [0, 1, 2], 0, identity);
    vertices.fill(99);
    graphics.present();
    const first = renderer.calls.find(call => call[0] === 'draw')[2][0];
    assert.deepEqual([...first.position], triangle);
    const geometry = first.mesh.geometry;
    graphics.beginFrame(1280, 720);
    graphics.drawTriangles(triangle, uv, null, [0, 1, 2], 0, identity);
    graphics.present();
    assert.equal(first.mesh.geometry, geometry);
    await graphics.dispose();
});

test('WebGL fallback converts clip depth without changing camera handedness or actor translation', async () => {
    const { graphics, renderer } = await fixture(fakeRenderer(THREE.WebGLCoordinateSystem));
    graphics.drawScene(1, frame);
    graphics.present();
    const projection = renderer.calls.find(call => call[0] === 'draw')[2][0].projection;
    assert.equal(projection[10], -2);
    assert.equal(projection[14], 1);
    await graphics.dispose();
});

test('partial texture uploads retain untouched pixels and reject invalid channels', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer() });
    graphics.createTexture(-1, 2, 2, new Uint8Array(16).fill(10));
    graphics.updateTexture(-1, 1, 0, 1, 2, new Uint8Array([20, 21, 22, 23, 30, 31, 32, 33]));
    assert.deepEqual([...graphics.texture(-1).image.data], [10, 10, 10, 10, 20, 21, 22, 23, 10, 10, 10, 10, 30, 31, 32, 33]);
    assert.throws(() => graphics.updateTexture(-1, 1, 1, 2, 2, new Uint8Array(16)), /Invalid texture upload/);
    await graphics.dispose();
});


test('internal post auto resolves the actual renderer backend independently of requested API and normal profiling', async () => {
    for (const [mode, coordinateSystem, requestedWebGL, expected] of [
        ['auto', THREE.WebGPUCoordinateSystem, true, true], ['auto', THREE.WebGLCoordinateSystem, false, false],
        [false, THREE.WebGPUCoordinateSystem, false, false], [true, THREE.WebGLCoordinateSystem, true, true],
        [undefined, THREE.WebGPUCoordinateSystem, false, false]]) {
        const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer(coordinateSystem),
            internalPost: mode, forceWebGL: requestedWebGL, profile: false });
        assert.equal(graphics.internalPostRequested, expected);
        assert.equal(graphics.getCapabilities().internalPost, expected);
        assert.equal(graphics.profiler, null); assert.equal(graphics.getProfile(), null);
        await graphics.dispose();
    }
    for (const internalPost of [null, 0, 1, 'true', 'false', {}, []]) await assert.rejects(createThreeGraphics({ dataset: {} }, {
        internalPost, renderer: { setPixelRatio() { assert.fail('invalid policy must fail before renderer mutation'); } }
    }), /mode must be/);
});

test('fused sharpening auto resolves the actual backend while generic callers and normal profiling stay unchanged', async () => {
    for (const [fusedSharpen, coordinateSystem, requestedWebGL, expected] of [
        ['auto', THREE.WebGPUCoordinateSystem, true, true], ['auto', THREE.WebGLCoordinateSystem, false, false],
        [true, THREE.WebGLCoordinateSystem, true, true], [false, THREE.WebGPUCoordinateSystem, false, false],
        [undefined, THREE.WebGPUCoordinateSystem, false, false]]) {
        const graphics = await createThreeGraphics({ dataset: {} }, { renderer: fakeRenderer(coordinateSystem),
            profile: false, fusedSharpen, forceWebGL: requestedWebGL });
        assert.equal(graphics.fusedSharpenRequested, expected);
        assert.equal(graphics.getCapabilities().fusedSharpen, expected);
        assert.equal(graphics.profiler, null); assert.equal(graphics.getProfile(), null);
        assert.equal(graphics.setFusedSharpenEnabled, undefined);
        await graphics.dispose();
    }
    for (const fusedSharpen of [null, 0, 1, 'true', 'false', '', {}, []]) await assert.rejects(createThreeGraphics({ dataset: {} }, {
        fusedSharpen, renderer: { setPixelRatio() { assert.fail('invalid mode must precede renderer mutation'); } }
    }), /mode must be/);
});
