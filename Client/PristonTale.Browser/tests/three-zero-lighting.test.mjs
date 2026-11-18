import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import * as TSL from '../wwwroot/vendor/three/three.tsl.js';
import { createZeroPointLighting } from '../wwwroot/three-zero-lighting.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

function compile(count, gl, { Material = THREE.MeshStandardNodeMaterial, owned = true, shadow = false, customModel = false } = {}) {
    const renderer = new THREE.WebGPURenderer({ forceWebGL: gl, canvas: {
        width: 320, height: 240, style: {}, addEventListener() {}, removeEventListener() {}
    } });
    renderer.hasFeature = () => false; renderer._bindings = undefined;
    const lighting = createZeroPointLighting(THREE, TSL);
    const lights = [new THREE.DirectionalLight(0xffffff, .5), new THREE.HemisphereLight(0xffffff, 0x333333, .2)];
    for (let index = 0; index < count; index++) {
        const light = new THREE.PointLight(0xffffff, 12, 20, 2);
        light.castShadow = shadow; light.position.set(index * 5, 10, 3); lights.push(light);
    }
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(), new Material());
    if (customModel) mesh.material.setupLightingModel = function (builder) {
        return THREE.MeshStandardNodeMaterial.prototype.setupLightingModel.call(this, builder);
    };
    if (owned) lighting.brand(mesh.material);
    const node = lighting.createNode(lights);
    const builder = renderer.backend.createNodeBuilder(mesh, renderer);
    builder.camera = new THREE.PerspectiveCamera(); builder.scene = new THREE.Scene();
    builder.lightsNode = node; builder.sortBindingGroups = () => {}; builder.build();
    return { shader: builder.fragmentShader, lighting, builder, node, mesh };
}

test('zero-point lighting compiles owned default PBR branches in actual WGSL and GLSL with accumulators outside them', () => {
    for (const gl of [false, true]) for (const count of [0, 8, 16, 32]) {
        const { shader, lighting } = compile(count, gl);
        assert.ok(shader.length > 1000);
        assert.equal(lighting.snapshot().guardedPoints, count);
        assert.equal(lighting.snapshot().fallbackLights, 1, 'directional light remains on its ordinary path');
        assert.equal((shader.match(/if\s*\([^\n]*noxiusPointRadiance_/g) ?? []).length, count);
        if (!count) continue;
        const branch = shader.indexOf('if ( ('), declaration = shader.indexOf('noxiusPointRadiance_');
        const guard = shader.indexOf('noxiusPointRadiance_', shader.indexOf('void main') > 0 ? shader.indexOf('void main') : shader.indexOf('@fragment'));
        const start = shader.indexOf('if ( (', guard), end = shader.indexOf('\n\t}', start);
        const direct = shader.indexOf('directSpecular = ( directSpecular +', start);
        assert.ok(branch > 0 && declaration > 0 && start > guard && direct > start && direct < end);
        assert.match(shader.slice(0, start), /directDiffuse\s*=.*0/);
        assert.match(shader.slice(0, start), /directSpecular\s*=.*0/);
        assert.match(shader, /any\([^\n]*noxiusPointRadiance_/);
    }
});

test('unowned, custom lighting-model, Physical and shadowed point lights retain the ordinary direct-light path', () => {
    for (const options of [{ owned: false }, { customModel: true }, { Material: THREE.MeshPhysicalNodeMaterial }, { shadow: true }]) {
        const result = compile(8, false, options);
        assert.equal(result.lighting.snapshot().guardedPoints, 0);
        assert.equal(result.lighting.snapshot().fallbackLights, 9);
        assert.doesNotMatch(result.shader, /noxiusPointRadiance/);
    }
});

test('same-session original/optimized controls change a shared render uniform without rebuilding nodes or material versions', () => {
    const { lighting, builder, node, mesh } = compile(8, false);
    const key = node.getCacheKey(), version = mesh.material.version, shader = builder.fragmentShader, counts = lighting.snapshot();
    const bools = builder.uniforms.fragment.filter(uniform => typeof uniform.node.value === 'boolean');
    assert.equal(bools.length, 1);
    assert.equal(bools[0].node.groupNode, TSL.renderGroup);
    assert.equal(lighting.setEnabled(false), false);
    assert.equal(bools[0].node.value, false);
    assert.equal(lighting.snapshot().enabled, false);
    assert.equal(lighting.setEnabled(true), true);
    assert.equal(bools[0].node.value, true);
    assert.equal(mesh.material.version, version); assert.equal(node.getCacheKey(), key); assert.equal(builder.fragmentShader, shader);
    assert.equal(lighting.snapshot().guardedPoints, counts.guardedPoints);
    assert.throws(() => lighting.setEnabled('true'), /boolean/);
    assert.notEqual(node.getCacheKey(), new THREE.Lighting().createNode(node.getBuiltinLights()).getCacheKey());
});

test('the actual driver resolves auto only from its actual backend and preserves the generic opt-in API without normal profiling', async () => {
    for (const [profile, requested, gpu, expected] of [[false, undefined, true, false], [false, false, true, false],
        [false, 'auto', true, true], [false, 'auto', false, false], [true, 'auto', true, true], [true, 'auto', false, false],
        [false, true, false, true], [true, true, false, true], [true, false, true, false]]) {
        const renderer = { coordinateSystem: gpu ? THREE.WebGPUCoordinateSystem : THREE.WebGLCoordinateSystem, backend: { isWebGPUBackend: gpu },
            info: { render: {}, reset() {} }, setPixelRatio() {}, setRenderTarget() {}, setViewport() {}, setScissorTest() {}, render() {}, dispose() {} };
        const graphics = await createThreeGraphics({ dataset: {} }, { renderer, profile, zeroPointLights: requested });
        graphics.createScene(1); graphics.createMesh(1, 1, { positions: [0,0,0,1,0,0,0,1,0], material: { blend: 'opaque' } });
        assert.equal(!!graphics.zeroPointLights, expected);
        assert.equal(!!graphics.profiler, profile);
        if (expected) {
            assert.equal(renderer.lighting, graphics.zeroPointLights);
            assert.equal(graphics.zeroPointLights.snapshot().branded, 1);
            assert.equal(graphics.setZeroPointLightsEnabled(false), false);
            assert.equal(graphics.zeroPointLights.snapshot().enabled, false);
        }
        if (!profile) assert.equal(graphics.getProfile(), null);
        if (profile) {
            graphics.light(graphics.scene(1), { pointLights: [
                { position: [0,0,0], color: [1,1,1] }, { position: [1,2,3], color: [.2,.3,.4] }
            ] });
            assert.deepEqual(graphics.getProfile().lighting, { activePointLights: 2, maxScenePointLights: 2, allocatedPointLights: 2, lastWorldPointLights: 0 });
            graphics.light(graphics.scene(1), { pointLights: [] });
            assert.deepEqual(graphics.getProfile().lighting, { activePointLights: 0, maxScenePointLights: 0, allocatedPointLights: 2, lastWorldPointLights: 0 });
        }
        await graphics.dispose();
    }
});

test('invalid point-light modes fail before the driver changes renderer state', async () => {
    for (const zeroPointLights of [null, 0, 1, 'true', 'false', '', {}, []]) {
        await assert.rejects(createThreeGraphics({ dataset: {} }, { zeroPointLights,
            renderer: { setPixelRatio() { assert.fail('invalid mode must not change the renderer'); } } }), /mode must be/);
    }
});
