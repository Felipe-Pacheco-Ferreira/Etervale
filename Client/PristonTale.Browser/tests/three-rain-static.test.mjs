import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
function renderer() {
    return { coordinateSystem: THREE.WebGPUCoordinateSystem, calls: [], setSize() {}, setPixelRatio() {}, setViewport() {},
        setScissorTest() {}, setScissor() {}, setRenderTarget(target) { this.target = target; }, getRenderTarget() { return this.target; },
        render(scene) { this.calls.push(scene.children.map(mesh => ({ geometry: mesh.geometry, range: { ...mesh.geometry.drawRange } }))); }, dispose() {} };
}
function heap() {
    const buffer = new ArrayBuffer(2048);
    new Float32Array(buffer, 64, 32).set(Array.from({ length: 32 }, (_, i) => i / 32));
    new Float32Array(buffer, 256, 32).fill(0.75);
    new Uint32Array(buffer, 448, 12).set([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7]);
    new Float32Array(buffer, 512, 16).set(identity);
    new Float32Array(buffer, 576, 24).fill(1);
    new Float32Array(buffer, 672, 12).fill(1);
    return buffer;
}
function draw(graphics, buffer, id, start = 0, count = 6) {
    graphics.drawRainGeometryMemory(id, start, count, buffer, 0, 512, 576, 672, true, false, 0, 0, 0, 0, 0, 5);
}

test('static rain copies registration once and shares immutable attributes with independent near/far ranges', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: renderer() });
    const buffer = heap();
    graphics.createRainGeometryMemory(1, buffer, 64, 256, 8, 448, 12);
    const bytes = graphics.rain.getStats().uploadBytes;
    graphics.beginFrame(128, 96);
    draw(graphics, buffer, 1);
    draw(graphics, buffer, 1, 6);
    const near = graphics.commands[0].batch.mesh.geometry, far = graphics.commands[1].batch.mesh.geometry;
    assert.notEqual(near, far);
    for (const name of ['rainPosition', 'rainShape']) {
        assert.equal(near.getAttribute(name), far.getAttribute(name));
        assert.equal(near.getAttribute(name).usage, THREE.StaticDrawUsage);
        assert.equal(near.getAttribute(name).version, 0);
    }
    assert.equal(near.index, far.index);
    assert.deepEqual(near.drawRange, { start: 0, count: 6 });
    assert.deepEqual(far.drawRange, { start: 6, count: 6 });
    const saved = Array.from(near.getAttribute('rainPosition').array);
    new Float32Array(buffer, 64, 32).fill(99);
    for (let frame = 0; frame < 5; frame++) {
        graphics.present(); graphics.beginFrame(128, 96);
        draw(graphics, buffer, 1, 0, 3 + frame % 2 * 3);
        assert.equal(graphics.commands[0].batch.mesh.geometry, near);
        assert.deepEqual(Array.from(near.getAttribute('rainPosition').array), saved);
        assert.equal(near.getAttribute('rainPosition').version, 0);
        assert.equal(near.index.version, 0);
    }
    assert.equal(graphics.rain.getStats().uploadBytes, bytes);
    assert.equal(graphics.rain.getStats().creates, 1);
    await graphics.dispose();
    assert.equal(graphics.rain.getStats().resources, 0);
});

test('static rain owner retains inactive shared views and flushes before revision replacement or ID reuse', async () => {
    const output = renderer();
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: output });
    const buffer = heap();
    graphics.createRainGeometryMemory(1, buffer, 64, 256, 8, 448, 12);
    graphics.createRainGeometryMemory(2, buffer, 64, 256, 8, 448, 12);
    graphics.beginFrame(128, 96); draw(graphics, buffer, 1);
    const original = graphics.commands[0].batch.mesh.geometry;
    let disposed = 0;
    original.addEventListener('dispose', () => disposed++);
    graphics.present(); graphics.beginFrame(128, 96); draw(graphics, buffer, 2);
    assert.equal(disposed, 0);
    graphics.destroyRainGeometry(1);
    assert.equal(disposed, 1);
    assert.equal(output.calls.length, 2);
    graphics.destroyRainGeometry(2);
    assert.equal(graphics.rain.getStats().resources, 0);
    graphics.createRainGeometryMemory(1, buffer, 64, 256, 8, 448, 12);
    draw(graphics, buffer, 1);
    assert.notEqual(graphics.commands[0].batch.mesh.geometry, original);
    assert.throws(() => draw(graphics, buffer, 77), /draw range/);
    assert.throws(() => draw(graphics, buffer, 1, 10, 6), /draw range/);
    await graphics.dispose();
    const next = await createThreeGraphics({ dataset: {} }, { renderer: renderer() });
    assert.throws(() => draw(next, buffer, 1), /draw range/);
    await next.dispose();
});

test('dynamic rain and splashes keep copying changing channels after a static draw used the same batch slot', async () => {
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer: renderer() });
    const buffer = heap();
    graphics.createRainGeometryMemory(1, buffer, 64, 256, 8, 448, 12);
    graphics.beginFrame(128, 96); draw(graphics, buffer, 1); graphics.present();
    const positions = new Float32Array(buffer, 64, 32), shapes = new Float32Array(buffer, 256, 32), index = new Uint32Array(buffer, 448, 12);
    graphics.beginFrame(128, 96);
    graphics.drawRain(positions, shapes, index, 0, identity, new Float32Array(24), new Float32Array(12), 4);
    const dynamic = graphics.commands[0].batch.mesh.geometry;
    assert.equal(dynamic.getAttribute('rainPosition').usage, THREE.DynamicDrawUsage);
    graphics.present(); graphics.beginFrame(128, 96);
    positions[0] = 42;
    graphics.drawRain(positions, shapes, index, 0, identity, new Float32Array(24), new Float32Array(12), 5);
    assert.equal(graphics.commands[0].batch.mesh.geometry.getAttribute('rainPosition').array[0], 42);
    draw(graphics, buffer, 1);
    assert.equal(graphics.commands[1].batch.mesh.geometry.getAttribute('rainPosition').usage, THREE.StaticDrawUsage);
    await graphics.dispose();
});
