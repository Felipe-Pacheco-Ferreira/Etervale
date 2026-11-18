import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import * as TSL from '../wwwroot/vendor/three/three.tsl.js';
import { fusedSharpen } from '../wwwroot/three-sharpen.js';

function compile(gl, denoise) {
    const renderer = new THREE.WebGPURenderer({ forceWebGL: gl, canvas: {
        width: 32, height: 32, style: {}, addEventListener() {}, removeEventListener() {}
    } });
    renderer.hasFeature = () => false;
    renderer._bindings = undefined;
    const input = new THREE.DataTexture(new Uint16Array(16), 2, 2, THREE.RGBAFormat, THREE.HalfFloatType);
    const node = fusedSharpen(TSL.texture(input), .8, denoise);
    const material = new THREE.NodeMaterial();
    material.fragmentNode = node;
    material.depthTest = material.depthWrite = material.toneMapped = false;
    const mesh = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), material);
    const builder = renderer.backend.createNodeBuilder(mesh, renderer);
    builder.camera = new THREE.OrthographicCamera(-1, 1, 1, -1, .1, 10);
    builder.scene = new THREE.Scene();
    builder.sortBindingGroups = () => {};
    builder.build();
    return { builder, node, input, mesh };
}

test('fused RCAS compiles five input loads and explicit RGBA16F rounding in actual WGSL and GLSL', () => {
    for (const gl of [false, true]) for (const denoise of [false, true]) {
        const { builder, node, input, mesh } = compile(gl, denoise);
        const shader = builder.fragmentShader;
        assert.equal((shader.match(gl ? /texelFetch\(/g : /textureLoad\(/g) ?? []).length, 5);
        assert.equal((shader.match(gl ? /\bpackHalf2x16\(/g : /\bpack2x16float\(/g) ?? []).length, 2);
        assert.equal((shader.match(gl ? /unpackHalf2x16\(/g : /unpack2x16float\(/g) ?? []).length, 2);
        assert.equal(node.textureNode.value, input);
        assert.equal(node.updateBeforeType, TSL.NodeUpdateType.NONE, 'no separate sharpening draw is scheduled');
        assert.equal(node.renderTarget, undefined);
        node.dispose(); mesh.geometry.dispose(); mesh.material.dispose(); input.dispose();
    }
});

test('fused RCAS owns no input texture and preserves its caller-owned graph and strength', () => {
    const input = new THREE.DataTexture(new Float32Array([-.5, 4, 32, .5]), 1, 1, THREE.RGBAFormat, THREE.FloatType);
    let disposed = false;
    input.addEventListener('dispose', () => { disposed = true; });
    const texture = TSL.texture(input);
    const node = fusedSharpen(texture, 2, true);
    assert.equal(node.textureNode, texture);
    assert.equal(node.sharpness.value, 2);
    assert.equal(node.denoise.value, true);
    node.dispose();
    assert.equal(disposed, false);
    input.dispose();
});
