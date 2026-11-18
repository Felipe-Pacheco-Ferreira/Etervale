import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { objectGroup, renderGroup } from '../wwwroot/vendor/three/three.tsl.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';
import { ExternalCamera } from '../wwwroot/three-camera.js';
import Bindings from '../node_modules/three/src/renderers/common/Bindings.js';
import NodeBuilderState from '../node_modules/three/src/renderers/common/nodes/NodeBuilderState.js';
import NodeManager from '../node_modules/three/src/renderers/common/nodes/NodeManager.js';

async function fixture() {
    const renderer = { coordinateSystem: THREE.WebGPUCoordinateSystem, backend: { isWebGPUBackend: true },
        setPixelRatio() {}, render() {}, dispose() {} };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer });
    graphics.createScene(1);
    graphics.createMesh(1, 1, { positions: [-1, -1, 0, 1, -1, 0, 0, 1, 0], indices: [0, 1, 2], material: { unlit: true } });
    graphics.createStage(1, 1, [1]);
    return { graphics, resource: graphics.scene(1), mesh: graphics.scene(1).stages.get(1).children[0] };
}

function compile(mesh, resource, forceWebGL = false) {
    const compiler = new THREE.WebGPURenderer({ forceWebGL, canvas: { width: 128, height: 96, style: {}, addEventListener() {}, removeEventListener() {} } });
    compiler.hasFeature = () => false;
    compiler._bindings = undefined;
    const builder = compiler.backend.createNodeBuilder(mesh, compiler);
    builder.camera = new ExternalCamera(forceWebGL ? THREE.WebGLCoordinateSystem : THREE.WebGPUCoordinateSystem);
    builder.scene = resource.scene;
    builder.fogNode = resource.scene.fogNode;
    builder.sortBindingGroups = () => {};
    builder.build();
    return { compiler, builder };
}

function fogGroup(groups, resource) {
    return groups.find(group => group.bindings.some(binding => binding.isNodeUniformsGroup
        && binding.uniforms.some(uniform => uniform.nodeUniform.node === resource.fogState.opacity)));
}

test('production world fog compiles into the shared render group on both shader backends', async () => {
    const { graphics, resource, mesh } = await fixture();
    for (const node of Object.values(resource.fogState)) assert.equal(node.getGroup(), renderGroup);
    assert.equal(renderGroup.shared, true);
    assert.equal(renderGroup.updateType, 'render');
    for (const webgl of [false, true]) {
        const { builder } = compile(mesh, resource, webgl);
        const group = fogGroup(builder.getBindings(), resource);
        assert.equal(group.name, 'render');
        const uniforms = group.bindings.flatMap(binding => binding.uniforms ?? []);
        for (const node of Object.values(resource.fogState)) {
            assert.ok(uniforms.some(uniform => uniform.nodeUniform.node === node));
        }
        assert.ok(builder.updateNodes.includes(renderGroup));
        assert.match(builder.fragmentShader, webgl ? /uniform render\s*\{/ : /var<uniform> render/);
    }
    await graphics.dispose();
});

test('pinned binding lifecycle shares fog across objects, updates once per render and isolates portrait contexts', async () => {
    const { graphics, resource, mesh } = await fixture();
    const groupsFor = () => {
        const { compiler, builder } = compile(mesh, resource);
        const nodes = new NodeManager(compiler, {});
        const bindings = new Bindings({ renderer: compiler }, nodes, null, null, {}, null);
        const state = new NodeBuilderState('', '', '', [], builder.getBindings(), [], [], [], null, false);
        const context = {};
        const groups = Array.from({ length: 100 }, () => fogGroup(state.createBindings(bindings, context), resource));
        return { nodes, bindings, state, groups };
    };
    // The original grouping is a control using the same production fog graph.
    for (const node of Object.values(resource.fogState)) node.setGroup(objectGroup);
    const before = groupsFor();
    assert.equal(new Set(before.groups).size, 100);
    const bindingOf = group => group.bindings.find(binding => binding.isNodeUniformsGroup);
    assert.equal(before.groups.filter(group => before.nodes.updateGroup(bindingOf(group))).length, 100);
    for (const node of Object.values(resource.fogState)) node.setGroup(renderGroup);
    const after = groupsFor();
    assert.equal(new Set(after.groups).size, 1);
    assert.equal(after.groups.filter(group => after.nodes.updateGroup(bindingOf(group))).length, 1);
    renderGroup.update();
    assert.equal(after.groups.filter(group => after.nodes.updateGroup(bindingOf(group))).length, 1);
    const main = bindingOf(after.groups[0]);
    const portrait = bindingOf(fogGroup(after.state.createBindings(after.bindings, {}), resource));
    assert.notEqual(main, portrait);
    graphics.light(resource, { fog: { mode: 1, start: 17, density: 0.003, maxOpacity: 0.75, color: [0.2, 0.4, 0.6] } });
    main.update();
    const opacityOffset = binding => binding.uniforms.find(uniform => uniform.nodeUniform.node === resource.fogState.opacity).offset;
    assert.equal(main.buffer[opacityOffset(main)], 0.75);
    graphics.light(resource, { fog: { mode: 0 } });
    portrait.update();
    assert.equal(portrait.buffer[opacityOffset(portrait)], 0);
    assert.equal(main.buffer[opacityOffset(main)], 0.75);
    graphics.light(resource, { fog: { mode: 1, start: 21, density: 0.006, maxOpacity: 0.5, color: [0.6, 0.3, 0.2] } });
    main.update();
    assert.equal(main.buffer[opacityOffset(main)], 0.5);
    assert.equal(portrait.buffer[opacityOffset(portrait)], 0);
    await graphics.dispose();
});
