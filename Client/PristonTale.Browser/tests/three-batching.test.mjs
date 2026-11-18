import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { buildStaticStageBatches } from '../wwwroot/three-batching.js';

function geometry(x = 0, z = 0, indexed = true) {
    const result = new THREE.BufferGeometry();
    result.setAttribute('position', new THREE.Float32BufferAttribute([x, 1, z, x + 2, 3, z, x, 5, z + 2], 3));
    result.setAttribute('normal', new THREE.Float32BufferAttribute([0, 1, 0, 0, 1, 0, 0, 1, 0], 3));
    result.setAttribute('color', new THREE.Uint8BufferAttribute([10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255], 4, true));
    for (const name of ['uv', 'uv1', 'uv2']) result.setAttribute(name, new THREE.Float32BufferAttribute([0, 0, 1, 0, 0, 1], 2));
    result.setAttribute('tangent', new THREE.Int16BufferAttribute([1, 2, 3, -1, 4, 5, 6, 1, 7, 8, 9, -1], 4, true));
    if (indexed) result.setIndex([2, 0, 1]);
    return result;
}

function entry(id, { x = 0, z = 0, indexed = true, source = {}, descriptor = {} } = {}) {
    return { id, geometry: geometry(x, z, indexed),
        material: new THREE.MeshBasicMaterial({ blending: THREE.NoBlending }),
        descriptor: { boneCount: 0, isSky: false, material: { textureId: 7, blend: 'opaque', opacity: 1, ...source }, ...descriptor } };
}

function triangles(geometries) {
    const result = [];
    for (const geometry of geometries) {
        const names = Object.keys(geometry.attributes).sort();
        const indices = geometry.getIndex()?.array;
        const count = indices?.length ?? geometry.getAttribute('position').count;
        for (let at = 0; at < count; at += 3) {
            const vertices = [];
            for (let corner = 0; corner < 3; corner++) {
                const index = indices ? indices[at + corner] : at + corner;
                vertices.push(names.map(name => {
                    const attribute = geometry.getAttribute(name);
                    return [name, ...attribute.array.slice(index * attribute.itemSize, (index + 1) * attribute.itemSize)];
                }));
            }
            result.push(JSON.stringify(vertices));
        }
    }
    return result.sort();
}

test('static stage batching preserves complete indexed/nonindexed triangles, every attribute and input ownership', () => {
    const first = entry(1);
    const second = entry(2, { x: 10, indexed: false });
    const before = triangles([first.geometry, second.geometry]);
    const output = buildStaticStageBatches(THREE, [first, second]);
    assert.equal(output.batches.length, 1);
    assert.equal(output.unbatched.length, 0);
    const batch = output.batches[0];
    assert.equal(batch.material, first.material);
    assert.equal(batch.descriptor, first.descriptor);
    assert.deepEqual(batch.meshIds, [1, 2]);
    assert.equal(batch.geometry.getIndex().array.constructor, Uint32Array);
    assert.deepEqual([...batch.geometry.getIndex().array], [0, 1, 2, 3, 4, 5]);
    assert.deepEqual(triangles([batch.geometry]), before);
    assert.deepEqual(triangles([first.geometry, second.geometry]), before);
    for (const name of Object.keys(first.geometry.attributes)) {
        const original = first.geometry.getAttribute(name);
        const merged = batch.geometry.getAttribute(name);
        assert.equal(merged.array.constructor, original.array.constructor);
        assert.equal(merged.normalized, original.normalized);
        assert.equal(merged.itemSize, original.itemSize);
        assert.equal(merged.gpuType, original.gpuType);
        assert.notEqual(merged.array.buffer, original.array.buffer);
    }
    let mergedDisposals = 0, sourceDisposals = 0;
    batch.geometry.addEventListener('dispose', () => mergedDisposals++);
    for (const source of [first, second]) {
        source.geometry.addEventListener('dispose', () => sourceDisposals++);
        source.material.addEventListener('dispose', () => sourceDisposals++);
    }
    output.dispose();
    output.dispose();
    assert.equal(mergedDisposals, 1);
    assert.equal(sourceDisposals, 0);
});

test('equivalent material policies merge independent of property order; every distinct policy and attribute layout partitions', () => {
    const first = entry(1, { source: { alphaCutoff: 0.24, doubleSided: true, textureIds: [7], layer: { mode: 'multiply', strength: 0.5 } } });
    const reordered = entry(2);
    reordered.descriptor.material = { layer: { strength: 0.5, mode: 'multiply' }, textureIds: [7], doubleSided: true,
        alphaCutoff: 0.24, opacity: 1, blend: 'opaque', textureId: 7 };
    const sources = [first, reordered];
    for (const change of [{ textureId: 8 }, { texture2Id: 9 }, { alphaCutoff: 0.5 }, { doubleSided: false }, { metallic: 0.5 }]) {
        sources.push(entry(sources.length + 1, { source: { ...first.descriptor.material, ...change } }));
    }
    const differentLayout = entry(99, { source: first.descriptor.material });
    differentLayout.geometry.deleteAttribute('uv2');
    sources.push(differentLayout);
    const output = buildStaticStageBatches(THREE, sources);
    assert.equal(output.batches.length, 1);
    assert.deepEqual(output.batches[0].meshIds, [1, 2]);
    assert.deepEqual(new Set(output.unbatched), new Set(sources.slice(2)));
    output.dispose();
});

test('animated, skinned, sky, transparent and nonstandard resources remain untouched', () => {
    const sources = [];
    const add = (options = {}) => {
        const source = entry(sources.length + 1, options);
        sources.push(source);
        return source;
    };
    add({ descriptor: { boneCount: 1 } });
    add({ descriptor: { isSky: true } });
    add({ source: { blend: 'alpha' } });
    add({ source: { blend: 'additive' } });
    add({ source: { opacity: 0.5 } });
    add({ source: { color: [1, 1, 1, 0.5] } });
    add({ source: { windX: 2 } });
    add({ source: { windZ: 2 } });
    add({ source: { blink: 1 } });
    add({ source: { textureIds: [7, 8] } });
    add({ source: { uvForm: 4 } });
    add({ source: { uvForm2: 6 } });
    add().material.transparent = true;
    add().material.depthWrite = false;
    add().geometry.getAttribute('position').setUsage(THREE.DynamicDrawUsage);
    add().geometry.getIndex().setUsage(THREE.DynamicDrawUsage);
    add().geometry.setDrawRange(0, 3);
    add().geometry.addGroup(0, 3, 0);
    add().geometry.morphAttributes.position = [new THREE.Float32BufferAttribute(new Float32Array(9), 3)];
    add().geometry.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(new Uint16Array(12), 4));
    add().geometry.setAttribute('normal', new THREE.Float16BufferAttribute(new Uint16Array(9), 3));
    const interleaved = new THREE.InterleavedBuffer(new Float32Array(9), 3);
    add().geometry.setAttribute('position', new THREE.InterleavedBufferAttribute(interleaved, 3, 0));
    add().geometry.isInstancedBufferGeometry = true;
    add().geometry.getIndex().array[0] = 99;
    const output = buildStaticStageBatches(THREE, sources);
    assert.equal(output.batches.length, 0);
    assert.deepEqual(output.unbatched, sources);
    output.dispose();
});

test('world triangle chunks preserve coverage and conservative bounds across negative cells and long edges', () => {
    const source = entry(1);
    source.geometry.setAttribute('position', new THREE.Float32BufferAttribute([
        -2050, 1, -10, -2048, 2, -10, -2049, 3, -8,
        1000, 4, 0, 3000, 5, 0, 1000, 6, 2,
        5000, 7, 0, 5002, 8, 0, 5000, 9, 2], 3));
    for (const name of Object.keys(source.geometry.attributes)) if (name !== 'position') source.geometry.deleteAttribute(name);
    source.geometry.setIndex([2, 0, 1, 3, 4, 5, 8, 6, 7]);
    const second = entry(2, { x: 5004 });
    const third = entry(3, { x: 5008 });
    const fourth = entry(4, { x: 5012 });
    const sources = [source, second, third, fourth];
    for (const extra of sources.slice(1)) {
        for (const name of Object.keys(extra.geometry.attributes)) if (name !== 'position') extra.geometry.deleteAttribute(name);
    }
    const output = buildStaticStageBatches(THREE, sources);
    assert.equal(output.batches.length, 3);
    assert.equal(output.unbatched.length, 0);
    assert.deepEqual(triangles(output.batches.map(batch => batch.geometry)), triangles(sources.map(entry => entry.geometry)));
    const point = new THREE.Vector3();
    for (const { geometry } of output.batches) {
        const position = geometry.getAttribute('position');
        for (let index = 0; index < position.count; index++) {
            point.fromBufferAttribute(position, index);
            assert.equal(geometry.boundingBox.containsPoint(point), true);
            assert.ok(geometry.boundingSphere.distanceToPoint(point) <= 0.00001);
        }
    }
    assert.ok(output.batches.some(batch => batch.geometry.boundingBox.max.x === 3000));
    output.dispose();
    assert.throws(() => buildStaticStageBatches(THREE, [], { chunkSize: 0 }), RangeError);
});

test('wide unique sources and unprofitable components retain original draw objects without blocking useful local merges', () => {
    const source = entry(1);
    source.geometry = new THREE.BufferGeometry();
    source.geometry.setAttribute('position', new THREE.Float32BufferAttribute([
        0, 0, 0, 1, 0, 0, 0, 1, 0,
        4096, 0, 0, 4097, 0, 0, 4096, 1, 0,
        8192, 0, 0, 8193, 0, 0, 8192, 1, 0], 3));
    let disposed = 0;
    source.geometry.addEventListener('dispose', () => disposed++);
    const alone = buildStaticStageBatches(THREE, [source]);
    assert.equal(alone.batches.length, 0, 'one wide material must not expand into three draw objects');
    assert.deepEqual(alone.unbatched, [source]);
    alone.dispose();
    assert.equal(disposed, 0);

    const others = [entry(2, { x: 8196 }), entry(3, { x: 15000 }), entry(4, { x: 15010 })];
    for (const item of others) {
        for (const name of Object.keys(item.geometry.attributes)) if (name !== 'position') item.geometry.deleteAttribute(name);
    }
    const sources = [source, ...others];
    const output = buildStaticStageBatches(THREE, sources);
    assert.equal(output.batches.length, 1, 'the independent two-source local merge must remain enabled');
    assert.deepEqual(output.batches[0].meshIds, [3, 4]);
    assert.deepEqual(new Set(output.unbatched), new Set([source, others[0]]));
    assert.ok(output.batches.length + output.unbatched.length < sources.length);
    assert.deepEqual(triangles([...output.batches, ...output.unbatched].map(entry => entry.geometry)),
        triangles(sources.map(entry => entry.geometry)));
    output.dispose();
    assert.equal(disposed, 0);
});

test('spatial batching never increases source object count across sparse and dense world layouts', () => {
    let seed = 8734;
    const random = () => (seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0);
    for (let layout = 0; layout < 24; layout++) {
        const sources = [];
        for (let id = 1; id <= 8; id++) {
            const source = entry(id, { source: { textureId: 7 + id % 2 } });
            const vertices = [];
            for (let triangle = 0; triangle < 1 + id % 3; triangle++) {
                const x = ((random() % (1 + layout % 8)) - 4) * 1024 + id;
                const z = ((random() % 4) - 2) * 1024;
                vertices.push(x, 0, z, x + 1, 0, z, x, 1, z + 1);
            }
            source.geometry = new THREE.BufferGeometry();
            source.geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
            sources.push(source);
        }
        const output = buildStaticStageBatches(THREE, sources);
        const objects = [...output.batches, ...output.unbatched];
        assert.ok(objects.length <= sources.length, `layout ${layout} expanded ${sources.length} sources into ${objects.length} objects`);
        assert.deepEqual(triangles(objects.map(entry => entry.geometry)), triangles(sources.map(entry => entry.geometry)));
        output.dispose();
    }
});

test('merged stage indices remain correct beyond the 16-bit vertex limit', () => {
    const make = id => {
        const source = entry(id);
        source.geometry = new THREE.BufferGeometry();
        const vertices = new Float32Array(36000 * 3);
        for (let at = 0; at < vertices.length; at += 3) {
            vertices[at] = (at / 3) % 3;
            vertices[at + 1] = id;
        }
        source.geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
        source.geometry.setIndex(new THREE.BufferAttribute(Uint16Array.from({ length: 36000 }, (_, at) => at), 1));
        return source;
    };
    const output = buildStaticStageBatches(THREE, [make(1), make(2)]);
    assert.equal(output.batches.length, 1);
    const geometry = output.batches[0].geometry;
    assert.equal(geometry.getAttribute('position').count, 72000);
    assert.equal(geometry.getIndex().array.constructor, Uint32Array);
    assert.equal(geometry.getIndex().array[71999], 71999);
    assert.equal(geometry.getIndex().array[36000], 36000);
    output.dispose();
});
