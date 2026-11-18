import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createStageBounds } from '../wwwroot/three-stage-bounds.js';

const descriptor = {
    positions: [-2, -1, -3, 4, 5, 6, 1, 3, -1, -7, -2, 4, 2, 8, 1, 3, 0, -4],
    boneIndices: [0, 0, 0, 1, 1, 1], boneCount: 2, material: { windX: 7, windZ: -11 }
};
const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];

function skinned(source = descriptor) {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(source.positions, 3));
    const indices = new Uint16Array(source.boneIndices.length * 4), weights = new Float32Array(indices.length);
    for (let vertex = 0; vertex < source.boneIndices.length; vertex++) {
        indices[vertex * 4] = source.boneIndices[vertex]; weights[vertex * 4] = 1;
    }
    geometry.setAttribute('skinIndex', new THREE.BufferAttribute(indices, 4));
    geometry.setAttribute('skinWeight', new THREE.BufferAttribute(weights, 4));
    const bones = Array.from({ length: source.boneCount }, () => new THREE.Bone());
    const skeleton = new THREE.Skeleton(bones, bones.map(() => new THREE.Matrix4()));
    const mesh = new THREE.SkinnedMesh(geometry, new THREE.MeshBasicNodeMaterial());
    mesh.bindMode = THREE.DetachedBindMode;
    mesh.bind(skeleton, new THREE.Matrix4());
    return mesh;
}

function pose(mesh, rows) {
    for (let bone = 0; bone < mesh.skeleton.bones.length; bone++) {
        const at = bone * 12;
        mesh.skeleton.bones[bone].matrixWorld.set(...rows.slice(at, at + 12), 0, 0, 0, 1);
    }
    mesh.skeleton.update();
}

test('rigid per-bone bounds contain actual Three skinned positions under affine poses and post-skin wind extremes', () => {
    const bounds = createStageBounds(THREE, descriptor), mesh = skinned();
    bounds.attach(mesh);
    assert.equal(mesh.frustumCulled, false, 'unpublished bind geometry cannot hide animated scenery');
    const sphere = bounds.sphere;
    const poses = [
        [...identity, ...identity],
        [0, -2, 0, 120, 3, 0, 0, -45, 0, 0, -4, 70, -2, .5, 1, -90, 0, 3, .25, 8, 0, 0, .5, -15],
        [-1, 2, -.5, 9000, 0, -3, .75, -7000, .25, 0, 4, 6000, 0, -1, .5, 13, 2, 0, -1, 37, 0, .25, -3, 59]
    ];
    const point = new THREE.Vector3(), displaced = new THREE.Vector3();
    for (const rows of poses) {
        pose(mesh, rows);
        assert.equal(bounds.update(rows), true);
        assert.equal(bounds.sphere, sphere);
        assert.equal(mesh.boundingSphere, sphere);
        assert.equal(mesh.frustumCulled, true);
        for (let vertex = 0; vertex < descriptor.boneIndices.length; vertex++) {
            mesh.getVertexPosition(vertex, point);
            assert.equal(sphere.containsPoint(point), true);
            for (const x of [-7, 7]) for (const z of [-11, 11]) {
                displaced.copy(point); displaced.x += x; displaced.z += z;
                assert.equal(sphere.containsPoint(displaced), true, `posed vertex ${vertex} plus wind remains in bounds`);
            }
        }
    }
});

test('invalid rigid bind inputs and invalid or missing palettes always leave the owners uncullable', () => {
    for (const invalid of [{ ...descriptor, positions: [] }, { ...descriptor, boneCount: 0 },
        { ...descriptor, boneIndices: [.5, 0, 0, 1, 1, 1] }, { ...descriptor, boneIndices: [2, 0, 0, 1, 1, 1] },
        { ...descriptor, positions: [NaN, ...descriptor.positions.slice(1)] }, { ...descriptor, material: { windX: Infinity } }]) {
        const bounds = createStageBounds(THREE, invalid), mesh = skinned();
        bounds.attach(mesh);
        assert.equal(bounds.update([...identity, ...identity]), false);
        assert.equal(mesh.frustumCulled, false);
    }
    const bounds = createStageBounds(THREE, descriptor), mesh = skinned();
    bounds.attach(mesh);
    assert.equal(bounds.update([...identity, ...identity]), true);
    for (const rows of [null, [], identity, [Infinity, ...identity.slice(1), ...identity], [NaN, ...identity.slice(1), ...identity]]) {
        assert.equal(bounds.update(rows), false);
        assert.equal(mesh.frustumCulled, false);
        assert.equal(bounds.update([...identity, ...identity]), true, 'a later valid published pose recovers culling');
    }
});

test('a shared mesh keeps every scenery owner synchronized across detach, rebuild and disposal', () => {
    const bounds = createStageBounds(THREE, descriptor), first = skinned(), second = skinned();
    bounds.attach(first); bounds.attach(second);
    bounds.update([...identity, ...identity]);
    assert.equal(bounds.owners.size, 2);
    assert.equal(first.boundingSphere, second.boundingSphere);
    bounds.detach(first);
    assert.equal(first.frustumCulled, false);
    assert.equal(first.boundingSphere, null);
    const rebuilt = skinned();
    bounds.attach(rebuilt);
    assert.equal(rebuilt.frustumCulled, true, 'a stage rebuild uses the cached published pose');
    bounds.update([]);
    assert.equal(second.frustumCulled, false);
    assert.equal(rebuilt.frustumCulled, false);
    bounds.dispose(); bounds.dispose();
    assert.equal(bounds.owners.size, 0);
    assert.equal(second.boundingSphere, null);
    assert.equal(rebuilt.boundingSphere, null);
    assert.equal(bounds.update([...identity, ...identity]), false);
    assert.throws(() => bounds.attach(first), /disposed/);
});

test('a far-translated tiny mesh receives a world-magnitude float margin at a frustum boundary', () => {
    const source = { positions: [0, 0, 0, .01, 0, 0, 0, .01, 0], boneIndices: [0, 0, 0], boneCount: 1 };
    const bounds = createStageBounds(THREE, source), mesh = skinned(source);
    bounds.attach(mesh);
    bounds.update([1, 0, 0, 1000000, 0, 1, 0, -1000000, 0, 0, 1, 0]);
    assert.ok(bounds.sphere.radius > 1, 'padding follows world-coordinate precision, not only the tiny bind radius');
    const camera = new THREE.OrthographicCamera(1000000.5, 1000002, -999999, -1000002, .1, 30);
    camera.coordinateSystem = THREE.WebGPUCoordinateSystem;
    camera.position.z = 10;
    camera.updateProjectionMatrix(); camera.updateMatrixWorld();
    const frustum = new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(
        camera.projectionMatrix, camera.matrixWorldInverse), THREE.WebGPUCoordinateSystem);
    assert.equal(mesh.intersectsFrustum(frustum), true, 'rounding at the boundary must not cause a false negative');
});
