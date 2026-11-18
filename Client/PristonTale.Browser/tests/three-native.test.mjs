import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { ExternalCamera } from '../wwwroot/three-camera.js';
import { createNativeBatches } from '../wwwroot/three-native.js';

const identity = new THREE.Matrix4().elements;
const positions = [-1, -1, .5, 1, -1, .5, 0, 1, .5];
const states = [
    ['Premultiplied', [0,5,0,0,5,0], 0], ['Alpha', [4,5,0,4,5,0], 0], ['Additive', [4,0,0,4,0,0], 0],
    ['VFX Shadow', [1,2,0,1,0,0], 1], ['VFX InverseShadow', [1,3,0,1,0,0], 0], ['VFX AddColor', [0,0,0,1,0,0], 0],
    ['Material Color', [2,3,0,0,5,0], 0], ['Material Shadow', [1,2,0,0,5,0], 1],
    ['Material AddColor', [2,0,0,0,5,0], 0], ['Material InverseShadow', [1,3,0,0,5,0], 0]
];

function fixture() {
    const target = new THREE.RenderTarget(128, 96, { count: 2, type: THREE.HalfFloatType });
    target.textures[0].name = 'output'; target.textures[1].name = 'normalRoughness';
    const renderer = { coordinateSystem: THREE.WebGPUCoordinateSystem, target, draws: 0,
        getRenderTarget() { return this.target; }, setScissorTest() {}, render() { this.draws++; } };
    const native = createNativeBatches(THREE, renderer, () => null, new THREE.Matrix4());
    return { target, renderer, native };
}

function prepare(native, blend, kind = 3) {
    return native.prepare(positions, null, null, [0,1,2], 0, identity, 'alpha', false, false, null, null,
        false, blend, null, 3, null, kind, kind === 2 ? [1,1,1,1,1,0] : [1,0,0,0]);
}

test('native Sprite, Unlit and VFX retain CullNone in one pass with no face-dependent fragment shading', () => {
    const { native, renderer, target } = fixture();
    renderer.target = null;
    const compiler = new THREE.WebGPURenderer({ canvas: { width:128, height:96, style:{}, addEventListener(){}, removeEventListener(){} } });
    compiler.hasFeature = () => false;
    compiler._bindings = undefined;
    for (const kind of [1, 2, 3]) {
        native.beginFrame();
        const batch = native.prepare(positions, null, null, [0,1,2,0,2,1], 0, identity, 'alpha', false, false, null, null,
            false, states[1][1], null, 3, null, kind, kind === 2 ? [1,1,1,1,1,0] : [1,0,0,0]);
        native.render([{batch}], 0, 1);
        assert.equal(batch.mesh.material.side, THREE.DoubleSide);
        assert.equal(batch.mesh.material.forceSinglePass, true);
        assert.deepEqual(Array.from(batch.mesh.geometry.index.array.slice(0,6)), [0,1,2,0,2,1]);
        const builder = compiler.backend.createNodeBuilder(batch.mesh, compiler);
        builder.camera = new ExternalCamera(THREE.WebGPUCoordinateSystem);
        builder.scene = new THREE.Scene();
        builder.sortBindingGroups = () => {};
        builder.build();
        assert.doesNotMatch(builder.fragmentShader, /front_facing|frontFacing|gl_FrontFacing/);
    }
    native.dispose();
    target.dispose();
});

test('catalogued native blends compile explicit MRT and preserve signed normals and roughness algebraically', () => {
    const { native, target } = fixture();
    const compiler = new THREE.WebGPURenderer({ canvas: { width:128, height:96, style:{}, addEventListener(){}, removeEventListener(){} } });
    compiler.hasFeature = () => false; compiler._bindings = undefined; compiler.setRenderTarget(target);
    const destination = [-.3, .4, .866, .37];
    const factor = (id, source, channel) => id === 0 ? 1 : id === 1 ? 0 : id === 2 ? source[channel]
        : id === 3 ? 1-source[channel] : id === 4 ? source[3] : 1-source[3];
    for (const [name, blend, neutral] of states) {
        native.beginFrame();
        const batch = prepare(native, blend);
        native.render([{batch}], 0, 1, true);
        const source = batch.mesh.material.fragmentNode.get('normalRoughness').value.toArray();
        assert.deepEqual(source, [neutral,neutral,neutral,0], name);
        for (let channel = 0; channel < 4; channel++) {
            const offset = channel === 3 ? 3 : 0;
            const result = source[channel]*factor(blend[offset],source,channel)
                + destination[channel]*factor(blend[offset+1],source,channel);
            assert.equal(result, destination[channel], `${name} channel ${channel}`);
        }
        const builder = compiler.backend.createNodeBuilder(batch.mesh, compiler);
        builder.camera = new ExternalCamera(THREE.WebGPUCoordinateSystem); builder.scene = new THREE.Scene();
        builder.sortBindingGroups = () => {}; builder.build();
        assert.match(builder.fragmentShader, /@location\(\s*1\s*\)/, name);
        assert.match(builder.vertexShader, /vec4<f32>\(\s*position, 1\.0\s*\)/, name);
    }
    native.dispose(); target.dispose();
});

test('native cached fragment variants switch between shadow MRT, alpha MRT and opaque canvas without rebuilding every draw', () => {
    const { native, renderer, target } = fixture();
    native.beginFrame();
    const batch = prepare(native, states[3][1]);
    native.render([{batch}],0,1,true);
    const shadow = batch.mesh.material.fragmentNode, version = batch.mesh.material.version;
    native.render([{batch}],0,1,true);
    assert.equal(batch.mesh.material.version, version);
    native.beginFrame(); assert.equal(prepare(native,states[1][1]),batch);
    native.render([{batch}],0,1,true);
    assert.notEqual(batch.mesh.material.fragmentNode,shadow);
    renderer.target = null;
    native.beginFrame(); assert.equal(prepare(native,[0,1,0,0,1,0],1),batch);
    native.render([{batch}],0,1);
    assert.equal(batch.mesh.material.fragmentNode,batch.fragment);
    assert.equal(batch.mesh.material.blendDst,THREE.ZeroFactor);
    renderer.target = target;
    native.beginFrame(); prepare(native,states[3][1]); native.render([{batch}],0,1,true);
    assert.equal(batch.mesh.material.fragmentNode,shadow);
    native.dispose(); target.dispose();
});

test('unknown or opaque blends refuse only the normal/roughness target before submitting a native group', () => {
    const { native, renderer, target } = fixture();
    for (const blend of [[0,1,0,0,1,0], [4,5,1,4,5,0], [6,1,0,0,5,0], [1,2,0,0,0,0]]) {
        native.beginFrame();
        const valid = prepare(native,states[1][1]), invalid = prepare(native,blend);
        const draws = renderer.draws;
        assert.throws(() => native.render([{batch:valid},{batch:invalid}],0,2,true), /Unsupported native blend for world normal\/roughness target\./);
        assert.equal(renderer.draws,draws);
        renderer.target = null;
        assert.doesNotThrow(() => native.render([{batch:invalid}],0,1));
        renderer.target = target;
    }
    native.dispose(); target.dispose();
});
