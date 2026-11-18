import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { buildTextureArrayStageBatches } from '../wwwroot/three-texture-arrays.js';

function texture(id, { width = 2, height = 2, encoding = 1, value = -id } = {}) {
    const data = new Uint8Array(width * height * 4);
    for (let at = 0; at < data.length; at += 4) data.set([value, value + 1, value + 2, 255], at);
    const texture = new THREE.DataTexture(data, width, height, THREE.RGBAFormat, THREE.UnsignedByteType);
    texture.colorSpace = encoding ? THREE.SRGBColorSpace : THREE.NoColorSpace;
    texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
    texture.minFilter = THREE.LinearMipmapLinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = true;
    texture.anisotropy = 4;
    return { width, height, texture, format: 0, encoding, sceneImmutable: true, target: null, renderTarget: false };
}

function entry(id, textureId, x = 0, z = 0) {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute([x, 0, z, x + 1, 1, z, x, 0, z + 1], 3));
    geometry.setAttribute('normal', new THREE.Float32BufferAttribute([0, 1, 0, 0, 1, 0, 0, 1, 0], 3));
    geometry.setAttribute('color', new THREE.Uint8BufferAttribute([1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255], 4, true));
    geometry.setAttribute('uv', new THREE.Float32BufferAttribute([-1, 2, 2, -1, 1, 1], 2));
    geometry.setAttribute('uv1', new THREE.Float32BufferAttribute([0, 1, 1, 0, 1, 1], 2));
    geometry.setAttribute('tangent', new THREE.Int16BufferAttribute([1, 2, 3, -1, 4, 5, 6, 1, 7, 8, 9, -1], 4, true));
    geometry.setIndex([2, 0, 1]);
    const material = new THREE.MeshBasicMaterial({ blending: THREE.NoBlending });
    return { id, geometry, material, descriptor: { boneCount: 0, isSky: false,
        material: { blend: 'opaque', textureId, textureIds: [textureId], opacity: 1, alphaCutoff: 0 } } };
}

function fixture(count = 2, textureOptions) {
    const entries = [], resources = new Map();
    for (let at = 1; at <= count; at++) {
        resources.set(-at, texture(-at, textureOptions));
        entries.push(entry(at, -at, at * 2));
    }
    const created = [];
    const createMaterial = (descriptor, arrayTexture, geometry) => {
        const material = new THREE.MeshBasicMaterial({ blending: THREE.NoBlending });
        material.userData = { descriptor, arrayTexture, geometry };
        created.push(material);
        return material;
    };
    return { entries, resources, created, createMaterial, build(options = {}) {
        return buildTextureArrayStageBatches(THREE, entries, resources, { createMaterial, ...options });
    } };
}

function triangles(entries, includeLayer = false) {
    const result = [];
    for (const entry of entries) {
        const geometry = entry.geometry, indices = geometry.index?.array;
        const names = Object.keys(geometry.attributes).filter(name => includeLayer || name !== 'textureLayer').sort();
        for (let at = 0; at < (indices?.length ?? geometry.getAttribute('position').count); at += 3) {
            result.push(JSON.stringify(Array.from({ length: 3 }, (_, corner) => {
                const vertex = indices?.[at + corner] ?? at + corner;
                return names.map(name => {
                    const attribute = geometry.getAttribute(name);
                    return [name, ...attribute.array.slice(vertex * attribute.itemSize, (vertex + 1) * attribute.itemSize)];
                });
            })));
        }
    }
    return result.sort();
}

function makeCutouts(f, cutoff = .25) {
    for (const source of f.entries) {
        source.descriptor.material.alphaCutoff = cutoff;
        source.material.alphaTest = cutoff;
        const data = f.resources.get(source.descriptor.material.textureId).texture.image.data;
        data[3] = 0; data[7] = 127;
    }
    return (descriptor, texture, geometry) => {
        const material = f.createMaterial(descriptor, texture, geometry);
        material.alphaTest = descriptor.material.alphaCutoff;
        return material;
    };
}

test('cutout arrays are opt-in and retain exact partial alpha bytes and authored cutoff without blending', () => {
    const f = fixture(), createMaterial = makeCutouts(f);
    const before = triangles(f.entries);
    const ordinary = f.build({ createMaterial });
    assert.equal(ordinary.batches.length, 0);
    assert.equal(ordinary.bytes, 0);
    ordinary.dispose();
    const output = f.build({ allowCutouts: true, createMaterial });
    assert.equal(output.batches.length, 1);
    assert.deepEqual(triangles(output.batches), before);
    const batch = output.batches[0];
    assert.equal(batch.descriptor.material.alphaCutoff, .25);
    assert.equal(batch.material.alphaTest, .25);
    assert.equal(batch.material.transparent, false);
    assert.equal(batch.material.depthWrite, true);
    assert.deepEqual([...batch.material.userData.arrayTexture.image.data],
        [...f.resources.get(-1).texture.image.data, ...f.resources.get(-2).texture.image.data]);
    assert.deepEqual([...batch.material.userData.arrayTexture.image.data.slice(0, 8)].filter((_, index) => index % 4 === 3), [0, 127]);
    assert.equal(output.bytes, 40);
    output.dispose();
});

test('a cached alpha texture remains ineligible for opaque entries before or after an approved cutout uses it', () => {
    for (const reverse of [false, true]) {
        const f = fixture(4), createMaterial = makeCutouts(f);
        for (let index = 2; index < 4; index++) {
            const item = f.entries[index];
            item.descriptor.material.textureId = index === 2 ? -1 : -2;
            item.descriptor.material.textureIds = [item.descriptor.material.textureId];
            item.descriptor.material.alphaCutoff = 0;
            item.material.alphaTest = 0;
        }
        const opaque = f.entries.slice(2);
        if (reverse) f.entries.reverse();
        const output = f.build({ allowCutouts: true, createMaterial });
        assert.equal(output.batches.length, 1);
        assert.deepEqual(new Set(output.unbatched), new Set(opaque));
        assert.equal(output.stats.eligibleObjects, 2);
        output.dispose();
    }
});

test('cutout cutoff, colour and surface policies remain separate even when texture sampling matches', () => {
    const f = fixture(6), createMaterial = makeCutouts(f);
    for (let index = 2; index < 4; index++) {
        f.entries[index].descriptor.material.alphaCutoff = .75;
        f.entries[index].material.alphaTest = .75;
    }
    for (let index = 4; index < 6; index++) {
        f.entries[index].descriptor.material.color = [.4, .7, .2, 1];
        f.entries[index].descriptor.material.roughness = .4;
    }
    const output = f.build({ allowCutouts: true, createMaterial });
    assert.equal(output.stats.groups, 3);
    assert.equal(output.batches.length, 3);
    assert.equal(output.stats.pages, 3);
    assert.deepEqual(output.batches.map(batch => batch.material.alphaTest).sort(), [.25, .25, .75]);
    assert.ok(output.batches.some(batch => batch.descriptor.material.roughness === .4));
    output.dispose();
});

test('the cutout opt-in never admits blended, animated, skinned or mismatched material state', () => {
    const changes = [entry => { entry.material.transparent = true; }, entry => { entry.material.blending = THREE.NormalBlending; },
        entry => { entry.descriptor.material.blend = 'alpha'; }, entry => { entry.material.depthWrite = false; },
        entry => { entry.material.opacity = .5; }, entry => { entry.descriptor.material.windX = 1; },
        entry => { entry.descriptor.material.uvForm = 4; }, entry => { entry.descriptor.material.texture2Id = -2; },
        entry => { entry.descriptor.boneCount = 1; }, entry => { entry.material.alphaTest = .5; }];
    for (const change of changes) {
        const f = fixture(), createMaterial = makeCutouts(f);
        change(f.entries[0]);
        const output = f.build({ allowCutouts: true, createMaterial });
        assert.equal(output.batches.length, 0, change.toString());
        assert.deepEqual(new Set(output.unbatched), new Set(f.entries));
        output.dispose();
    }
});

test('array stage batches preserve every triangle attribute, winding, exact RGBA layer bytes and sampler policy', () => {
    const f = fixture();
    const before = triangles(f.entries);
    const output = f.build();
    assert.equal(output.batches.length, 1);
    assert.equal(output.unbatched.length, 0);
    assert.deepEqual(output.stats, { inputObjects: 2, outputObjects: 1, eligibleObjects: 2, groups: 1,
        batchedSources: 2, batches: 1, pages: 1, layers: 2, bytes: 40 });
    const batch = output.batches[0], array = batch.material.userData.arrayTexture;
    assert.deepEqual(triangles(output.batches), before);
    assert.deepEqual(triangles(f.entries), before);
    assert.equal(batch.geometry.index.array.constructor, Uint32Array);
    assert.deepEqual([...batch.geometry.getAttribute('textureLayer').array], [0, 0, 0, 1, 1, 1]);
    assert.deepEqual(batch.textureIds, [-1, -2]);
    assert.equal(array.isDataArrayTexture, true);
    assert.deepEqual([...array.image.data], [...f.resources.get(-1).texture.image.data, ...f.resources.get(-2).texture.image.data]);
    assert.equal(array.image.depth, 2);
    for (const name of ['colorSpace', 'magFilter', 'minFilter', 'wrapS', 'wrapT', 'generateMipmaps', 'anisotropy', 'flipY']) {
        assert.equal(array[name], f.resources.get(-1).texture[name], name);
    }
    assert.equal(batch.descriptor.material.textureId, 0);
    assert.deepEqual(batch.descriptor.material.textureIds, []);
    for (const name of Object.keys(f.entries[0].geometry.attributes)) {
        const original = f.entries[0].geometry.getAttribute(name), merged = batch.geometry.getAttribute(name);
        assert.equal(merged.array.constructor, original.array.constructor);
        assert.equal(merged.itemSize, original.itemSize);
        assert.equal(merged.normalized, original.normalized);
        assert.notEqual(merged.array.buffer, original.array.buffer);
    }
    assert.equal(f.entries[0].geometry.hasAttribute('textureLayer'), false);
    assert.equal(array.image.data.buffer === f.resources.get(-1).texture.image.data.buffer, false);
    f.resources.get(-1).texture.image.data.fill(0);
    assert.equal(array.image.data[3], 255, 'page pixels must be immutable independent copies');
    output.dispose();
});

test('unsafe class, alpha, mutable, format, animation and layout sources fall back to their original resources', () => {
    const changes = [
        (e, r) => { e.descriptor.boneCount = 1; }, (e, r) => { e.descriptor.isSky = true; },
        e => { e.descriptor.material.windX = 1; }, e => { e.descriptor.material.uvForm = 4; },
        e => { e.descriptor.material.texture2Id = -9; }, e => { e.descriptor.material.textureIds = [-1, -2]; },
        e => { e.descriptor.material.textureIds = [-2]; }, e => { e.descriptor.material.alphaCutoff = 0.2; },
        e => { e.material.alphaTest = 0.2; }, e => { e.material.transparent = true; },
        e => { e.material.depthWrite = false; }, e => { e.material.opacity = 0.5; },
        e => { e.descriptor.material.opacity = 0.5; }, e => { e.descriptor.material.color = [1, 1, 1, 0.5]; },
        e => { e.descriptor.material.blink = true; }, (e, r) => { r.sceneImmutable = false; },
        (e, r) => { r.mutable = true; }, (e, r) => { r.renderTarget = true; }, (e, r) => { r.target = {}; },
        (e, r) => { r.format = 13; }, (e, r) => { r.texture.image.data[3] = 254; },
        (e, r) => { r.texture.flipY = true; }, (e, r) => { r.texture.type = THREE.FloatType; },
        (e, r) => { r.texture.colorSpace = THREE.NoColorSpace; },
        e => { e.geometry.getAttribute('position').setUsage(THREE.DynamicDrawUsage); },
        e => { e.geometry.setDrawRange(0, 3); }, e => { e.geometry.addGroup(0, 3, 0); },
        e => { e.geometry.setAttribute('textureLayer', new THREE.Float32BufferAttribute([0, 0, 0], 1)); },
        e => { e.geometry.getIndex().array[0] = 99; }
    ];
    for (const change of changes) {
        const f = fixture();
        change(f.entries[0], f.resources.get(-1));
        const output = f.build();
        assert.equal(output.batches.length, 0, change.toString());
        assert.deepEqual(new Set(output.unbatched), new Set(f.entries));
        assert.equal(f.created.length, 0);
        output.dispose();
    }
    const f = fixture();
    f.resources.set(7, f.resources.get(-1));
    f.entries[0].descriptor.material.textureId = 7;
    f.entries[0].descriptor.material.textureIds = [7];
    assert.equal(f.build().batches.length, 0, 'positive native HUD resources never enter an array');
});

test('every distinct material, dimensions, encoding or sampler partitions independently', () => {
    const changes = [e => { e.descriptor.material.roughness = 0.4; }, e => { e.descriptor.material.metallic = 0.2; },
        e => { e.descriptor.material.doubleSided = true; }, e => { e.material.side = THREE.DoubleSide; },
        e => { e.material.fog = false; }, (e, r) => { r.texture.anisotropy = 8; },
        (e, r) => { r.texture.magFilter = THREE.NearestFilter; }, (e, r) => { r.texture.wrapS = THREE.ClampToEdgeWrapping; },
        (e, r) => { r.texture.offset.x = 0.5; }, (e, r) => { r.texture.generateMipmaps = false; },
        (e, r, f) => { f.resources.set(-1, texture(-1, { width: 4 })); },
        (e, r, f) => { f.resources.set(-1, texture(-1, { encoding: 0 })); }];
    for (const change of changes) {
        const f = fixture();
        change(f.entries[0], f.resources.get(-1), f);
        const output = f.build();
        assert.equal(output.batches.length, 0, change.toString());
        assert.deepEqual(new Set(output.unbatched), new Set(f.entries));
        output.dispose();
    }
    const f = fixture();
    f.entries[1].descriptor.material = { textureIds: [-2], textureId: -2, alphaCutoff: 0, opacity: 1, blend: 'opaque' };
    const output = f.build();
    assert.equal(output.batches.length, 1, 'equivalent policies do not depend on property order');
    output.dispose();
});

test('mip-inclusive page, backend-layer and global budgets keep original fallbacks and never exceed hard caps', () => {
    const f = fixture(5);
    const limited = f.build({ maxLayers: 2 });
    assert.equal(limited.stats.pages, 2);
    assert.equal(limited.stats.layers, 4);
    assert.equal(limited.bytes, 80);
    assert.equal(limited.unbatched.length, 1);
    limited.dispose();
    const global = f.build({ maxLayers: 2, maxTotalBytes: 60 });
    assert.equal(global.stats.pages, 1);
    assert.equal(global.bytes, 40);
    assert.equal(global.unbatched.length, 3);
    global.dispose();
    const page = f.build({ maxPageBytes: 39 });
    assert.equal(page.bytes, 0, 'two 2x2 RGBA8 full mip chains cost40 rather than raw32 bytes');
    assert.equal(page.unbatched.length, 5);
    page.dispose();
    const backend = f.build({ maxLayers: 1 });
    assert.equal(backend.bytes, 0);
    backend.dispose();
    const large = fixture(13, { width: 512, height: 512 });
    const hard = large.build({ maxPageBytes: 1e9, maxLayers: 1e9, maxTotalBytes: 1e9 });
    assert.equal(hard.stats.pages, 1);
    assert.equal(hard.stats.layers, 12);
    assert.ok(hard.bytes <= 16 * 1024 * 1024);
    assert.equal(hard.unbatched.length, 1);
    hard.dispose();
    assert.throws(() => f.build({ chunkSize: 0 }), RangeError);
    assert.throws(() => f.build({ maxTotalBytes: -1 }), RangeError);
    assert.throws(() => buildTextureArrayStageBatches(THREE, [], new Map()), RangeError);
});

test('connected spatial components strictly reduce baseline objects and preserve coverage across long edges', () => {
    const f = fixture(4);
    f.entries[0].geometry = new THREE.BufferGeometry();
    f.entries[0].geometry.setAttribute('position', new THREE.Float32BufferAttribute([
        0, 0, 0, 1, 0, 0, 0, 1, 0, 8192, 0, 0, 8193, 0, 0, 8192, 1, 0], 3));
    f.entries[1] = entry(2, -2, 8196);
    f.entries[2] = entry(3, -3, 20000);
    f.entries[3] = entry(4, -4, 20002);
    for (const source of f.entries) {
        for (const name of Object.keys(source.geometry.attributes)) if (name !== 'position') source.geometry.deleteAttribute(name);
    }
    const before = triangles(f.entries);
    const output = f.build();
    assert.equal(output.batches.length, 1);
    assert.deepEqual(new Set(output.unbatched), new Set(f.entries.slice(0, 2)));
    assert.deepEqual(new Set(output.batches[0].meshIds), new Set([3, 4]));
    assert.deepEqual(triangles([...output.batches, ...output.unbatched]), before);
    assert.ok(output.stats.outputObjects < output.stats.inputObjects);
    const point = new THREE.Vector3();
    for (const batch of output.batches) {
        const position = batch.geometry.getAttribute('position');
        for (let vertex = 0; vertex < position.count; vertex++) {
            point.fromBufferAttribute(position, vertex);
            assert.ok(batch.geometry.boundingBox.containsPoint(point));
            assert.ok(batch.geometry.boundingSphere.distanceToPoint(point) <= 1e-5);
        }
    }
    output.dispose();
});

test('array disposal is idempotent, releases only new resources and cleans partial material factory failures', () => {
    const f = fixture(4);
    const originalDisposals = [], ownedDisposals = [];
    for (const entry of f.entries) {
        entry.geometry.addEventListener('dispose', () => originalDisposals.push('geometry'));
        entry.material.addEventListener('dispose', () => originalDisposals.push('material'));
    }
    for (const resource of f.resources.values()) resource.texture.addEventListener('dispose', () => originalDisposals.push('texture'));
    const output = f.build({ maxLayers: 2 });
    for (const batch of output.batches) {
        for (const [kind, resource] of [['geometry', batch.geometry], ['material', batch.material], ['page', batch.material.userData.arrayTexture]]) {
            resource.addEventListener('dispose', () => ownedDisposals.push(kind));
        }
    }
    output.dispose();output.dispose();
    assert.deepEqual(ownedDisposals.sort(), ['geometry', 'geometry', 'material', 'material', 'page', 'page']);
    assert.deepEqual(originalDisposals, []);
    const partial = fixture(4);
    let count = 0, released = 0;
    assert.throws(() => partial.build({ maxLayers: 2, createMaterial(descriptor, arrayTexture, geometry) {
        arrayTexture.addEventListener('dispose', () => released++);
        geometry.addEventListener('dispose', () => released++);
        if (count++) throw new Error('factory failure');
        const material = new THREE.MeshBasicMaterial();
        material.addEventListener('dispose', () => released++);
        return material;
    } }), /factory failure/);
    assert.equal(released, 5);
    assert.throws(() => f.build({ createMaterial: () => f.entries[0].material }), /independently owned/);
});

test('merged array geometry retains Uint32 indices beyond65535 vertices and never shares source arrays', () => {
    const f = fixture();
    for (const entry of f.entries) {
        entry.geometry = new THREE.BufferGeometry();
        const position = new Float32Array(36000 * 3);
        for (let at = 0; at < position.length; at += 3) {
            position[at] = at % 9;
            position[at + 1] = entry.id;
        }
        entry.geometry.setAttribute('position', new THREE.BufferAttribute(position, 3));
    }
    const output = f.build();
    const geometry = output.batches[0].geometry;
    assert.equal(geometry.getAttribute('position').count, 72000);
    assert.equal(geometry.index.array.constructor, Uint32Array);
    assert.equal(geometry.index.array[71999], 71999);
    assert.equal(geometry.getAttribute('textureLayer').array[71999], 1);
    output.dispose();
});

test('array layer cap, odd mip chains and reused diffuse resources retain exact budget accounting', () => {
    const layers = fixture(129, { width: 1, height: 1 });
    const output = layers.build({ maxLayers: 1000 });
    assert.equal(output.stats.layers, 128);
    assert.equal(output.stats.pages, 1);
    assert.equal(output.unbatched.length, 1);
    assert.equal(output.bytes, 128 * 4);
    output.dispose();
    const odd = fixture(2, { width: 3, height: 5 });
    const tooSmall = odd.build({ maxPageBytes: 143 });
    assert.equal(tooSmall.batches.length, 0);
    tooSmall.dispose();
    const exact = odd.build({ maxPageBytes: 144 });
    assert.equal(exact.bytes, 144, '3x5,1x2,1x1 use72 bytes per full mip chain');
    exact.dispose();
    const reused = fixture(3);
    reused.entries[2].descriptor.material.textureId = -1;
    reused.entries[2].descriptor.material.textureIds = [-1];
    const shared = reused.build();
    assert.equal(shared.bytes, 40);
    assert.equal(shared.stats.layers, 2);
    assert.equal(shared.batches[0].geometry.getAttribute('textureLayer').count, 9);
    shared.dispose();
});

test('deterministic world layouts never expand objects and preserve each triangle diffuse layer through remapping', () => {
    let seed = 8734;
    const random = () => (seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0);
    const texturedTriangles = entries => {
        const result = [];
        for (const item of entries) {
            const geometry = item.geometry;
            const index = geometry.index?.array;
            for (let at = 0; at < (index?.length ?? geometry.getAttribute('position').count); at += 3) {
                const vertices = [index?.[at] ?? at, index?.[at + 1] ?? at + 1, index?.[at + 2] ?? at + 2];
                const layer = geometry.getAttribute('textureLayer');
                if (layer) assert.ok(vertices.every(vertex => layer.getX(vertex) === layer.getX(vertices[0])));
                const id = layer ? item.textureIds[layer.getX(vertices[0])] : item.descriptor.material.textureId;
                result.push(JSON.stringify([id, vertices.map(vertex => [...geometry.getAttribute('position').array.slice(vertex * 3, vertex * 3 + 3)])]));
            }
        }
        return result.sort();
    };
    for (let layout = 0; layout < 24; layout++) {
        const f = fixture(8);
        for (const item of f.entries) {
            const values = [];
            for (let triangle = 0; triangle < 1 + item.id % 3; triangle++) {
                const x = ((random() % (1 + layout % 5)) - 2) * 4096 + item.id;
                const z = ((random() % 3) - 1) * 4096;
                values.push(x, 0, z, x + 1, 1, z, x, 0, z + 1);
            }
            item.geometry = new THREE.BufferGeometry();
            item.geometry.setAttribute('position', new THREE.Float32BufferAttribute(values, 3));
        }
        const before = texturedTriangles(f.entries);
        const output = f.build();
        assert.ok(output.stats.outputObjects <= output.stats.inputObjects, `layout${layout} expanded draw objects`);
        assert.deepEqual(texturedTriangles([...output.batches, ...output.unbatched]), before);
        assert.ok(output.bytes <= 16 * 1024 * 1024 * output.stats.pages);
        output.dispose();
    }
});
