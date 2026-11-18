import { ExternalCamera } from './three-camera.js';
import { Fn, If, attribute, float, ivec2, mix, mrt, textureLoad, uniform, varyingProperty, vec2, vec3, vec4 } from './vendor/three/three.tsl.js';

export function createRainBatches(THREE, renderer, texture, native, toWebGlDepth) {
    const scene = new THREE.Scene();
    const camera = new ExternalCamera(renderer.coordinateSystem);
    const batches = [[], []];
    const next = [0, 0];
    const geometries = new Map();
    const stats = { creates: 0, destroys: 0, draws: 0, uploadBytes: 0 };
    const rainBlend = new Int32Array([4, 5, 0, 4, 5, 0]);
    const reverseDepth = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 1, 0, 0, 0, 1);
    const emptyMap = new THREE.DataTexture(new Float32Array([-1e8]), 1, 1, THREE.RedFormat, THREE.FloatType);
    emptyMap.minFilter = emptyMap.magFilter = THREE.NearestFilter;
    emptyMap.needsUpdate = true;

    function create(kind) {
        const state = {
            matrix: uniform(new THREE.Matrix4()), volume: uniform(new THREE.Vector4()), boxes: uniform(new THREE.Vector4()),
            fall: uniform(new THREE.Vector4()), velocity: uniform(new THREE.Vector4()), eye: uniform(new THREE.Vector4()),
            screen: uniform(new THREE.Vector4()), color: uniform(new THREE.Vector4()),
            occlusion: uniform(new THREE.Vector4()), cells: uniform(256)
        };
        const world = varyingProperty('vec3', 'noxiusRainWorld');
        const shape = varyingProperty('vec3', 'noxiusRainShape');
        const shade = varyingProperty('vec4', 'noxiusRainShade');
        const age = varyingProperty('float', 'noxiusRainAge');
        const position = attribute('rainPosition', 'vec4');
        const corner = attribute('rainShape', 'vec4');
        const material = new THREE.NodeMaterial();
        material.side = THREE.DoubleSide;
        material.toneMapped = false;
        material.transparent = true;
        material.forceSinglePass = true;
        const wrap = (seed, centre, width) => seed.sub(centre).div(width).add(0.5).fract().sub(0.5).mul(width).add(centre);
        material.vertexNode = Fn(() => {
            if (kind === 5) {
                world.assign(position.xyz);
                shape.assign(corner.xyz);
                shade.assign(vec4(1, 1, 1, corner.w));
                age.assign(position.w);
                return state.matrix.mul(vec4(position.xyz, 1));
            }
            const width = mix(state.boxes.x, state.boxes.y, corner.z);
            const speed = mix(0.8, 1.2, position.w.mul(7.31).fract());
            const velocity = state.fall.xyz.mul(speed);
            const travelled = velocity.mul(state.volume.w);
            const head = vec3(wrap(position.x.mul(width).add(travelled.x), state.volume.x, width),
                wrap(position.y.mul(state.boxes.z).add(travelled.y), state.volume.y, state.boxes.z),
                wrap(position.z.mul(width).add(travelled.z), state.volume.z, width));
            const tail = head.sub(velocity.sub(state.velocity.xyz).mul(state.boxes.w));
            const headClip = state.matrix.mul(vec4(head, 1));
            const tailClip = state.matrix.mul(vec4(tail, 1));
            const clip = vec4(0, 0, -2, 1).toVar();
            world.assign(vec3(0));
            shape.assign(vec3(0));
            shade.assign(vec4(0));
            age.assign(0);
            If(headClip.w.greaterThanEqual(1).and(tailClip.w.greaterThanEqual(1)), () => {
                const along = tailClip.xy.div(tailClip.w).sub(headClip.xy.div(headClip.w)).mul(state.screen.xy).mul(0.5);
                const length = along.length();
                const direction = vec2(0, 1).toVar();
                If(length.greaterThan(0.001), () => { direction.assign(along.div(length)); });
                const across = vec2(direction.y.negate(), direction.x);
                const thickness = state.screen.w.mul(state.eye.w).div(headClip.w);
                const pixels = thickness.max(state.screen.z);
                If(corner.y.lessThan(0.5), () => {
                    clip.assign(headClip);
                    world.assign(head);
                }).Else(() => {
                    clip.assign(tailClip);
                    world.assign(tail);
                });
                clip.xy.addAssign(across.mul(corner.x.mul(pixels).mul(0.5)).div(state.screen.xy.mul(0.5)).mul(clip.w));
                const fromCentre = head.xz.sub(state.volume.xz).abs().div(width.mul(0.5));
                const edge = float(1).sub(fromCentre.x.max(fromCentre.y)).mul(5).saturate();
                const nearFade = head.sub(state.eye.xyz).length().sub(12).div(30).saturate();
                const brightness = mix(0.6, 1, position.w.mul(13.7).fract()).mul(mix(1, 0.7, corner.z));
                shade.assign(vec4(vec3(brightness), edge.mul(nearFade).mul(thickness.div(pixels).add(0.08).saturate())));
                shape.assign(vec3(corner.xy, 0));
            });
            return clip;
        })();
        const map = textureLoad(emptyMap, ivec2(0));
        material.fragmentNode = Fn(() => {
            const alpha = float(0).toVar();
            if (kind === 4) {
                const surface = float(-1e8).toVar();
                const cells = world.xz.div(state.occlusion.z);
                const fromCentre = cells.sub(state.occlusion.xy.div(state.occlusion.z)).abs();
                If(state.occlusion.w.greaterThanEqual(0.5)
                    .and(fromCentre.x.max(fromCentre.y).lessThan(state.cells.mul(0.5).sub(1))), () => {
                    const texel = ivec2(cells.floor()).bitAnd(ivec2(state.cells.toInt().sub(1)));
                    surface.assign(map.sample(texel).r);
                });
                world.y.lessThan(surface).discard();
                const across = float(1).sub(shape.x.mul(shape.x));
                const along = shape.y.smoothstep(0, 0.15).mul(float(1).sub(shape.y.mul(0.6)));
                alpha.assign(state.color.a.mul(shade.a).mul(across).mul(along));
            } else {
                If(shape.z.lessThan(0.5), () => {
                    const ring = shape.xy.length().sub(mix(0.15, 0.95, age));
                    alpha.assign(ring.mul(ring).mul(-300).exp().mul(float(1).sub(age)).mul(0.6));
                }).Else(() => {
                    for (let k = 0; k < 4; k++) {
                        const side = (k - 1.5) / 4;
                        const droplet = vec2(age.mul(side * 1.6),
                            age.mul(float(1).sub(age)).mul((1.6 - Math.abs(side)) * 4).sub(1));
                        const delta = shape.xy.sub(droplet);
                        alpha.addAssign(delta.dot(delta).mul(-220).exp());
                    }
                    alpha.mulAssign(float(1).sub(age.mul(age)));
                });
                alpha.mulAssign(state.color.a.mul(shade.a));
            }
            return vec4(state.color.rgb.mul(shade.rgb).min(60000), alpha);
        })();
        const fragment = material.fragmentNode;
        // Native rain is non-premultiplied source-alpha blending. A zero-alpha
        // auxiliary output leaves the existing world normal/roughness untouched.
        // Explicit fragment nodes bypass NodeMaterial's automatic MRT setup.
        const mrtFragment = mrt({ output: fragment, normalRoughness: vec4(0) });
        material.userData.rain = state;
        const mesh = new THREE.Mesh(new THREE.BufferGeometry(), material);
        mesh.matrixAutoUpdate = false;
        mesh.matrixWorldAutoUpdate = false;
        mesh.frustumCulled = false;
        return { mesh, map, state, kind, fragment, mrtFragment, hasMrt: false, capacity: 0, indexCapacity: 0,
            resource: null, scissor: new Int32Array(4), viewport: new Int32Array(4), sampler: new Int32Array([1, 1, 1, 1]), clipped: false, hasViewport: false };
    }

    function createGeometry(id, positions, shapes, indices) {
        if (!Number.isInteger(id) || id <= 0 || id > 0xffffffff || geometries.has(id)
            || !positions.length || positions.length % 4 || shapes.length !== positions.length || !indices.length) {
            throw new Error('Invalid static rain geometry.');
        }
        const count = positions.length / 4;
        for (let index = 0; index < indices.length; index++) {
            if (!Number.isInteger(indices[index]) || indices[index] < 0 || indices[index] >= count) throw new Error('Invalid static rain index.');
        }
        const position = new THREE.BufferAttribute(new Float32Array(positions), 4);
        const shape = new THREE.BufferAttribute(new Float32Array(shapes), 4);
        const index = new THREE.BufferAttribute(new Uint32Array(indices), 1);
        geometries.set(id, { position, shape, index, views: new Map() });
        stats.creates++;
        stats.uploadBytes += position.array.byteLength + shape.array.byteLength + index.array.byteLength;
    }

    function destroyGeometry(id) {
        const resource = geometries.get(id);
        if (!resource) return;
        for (const [batch, geometry] of resource.views) {
            if (batch.mesh.geometry === geometry) {
                batch.mesh.geometry = new THREE.BufferGeometry();
                batch.resource = null;
                batch.capacity = batch.indexCapacity = 0;
            }
            geometry.dispose();
        }
        geometries.delete(id);
        stats.destroys++;
    }

    function prepareGeometry(id, start, count, textureId, matrix, vertexParams, pixelParams,
        depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare) {
        const resource = geometries.get(id);
        if (!resource || !Number.isInteger(start) || !Number.isInteger(count) || start < 0 || count < 0
            || start + count > resource.index.count) throw new Error('Invalid static rain draw range.');
        const slot = next[0]++;
        const batch = batches[0][slot] ??= create(4);
        if (batch.resource !== resource) {
            if (!batch.resource) batch.mesh.geometry.dispose();
            let geometry = resource.views.get(batch);
            if (!geometry) {
                geometry = new THREE.BufferGeometry();
                geometry.setAttribute('position', resource.position);
                geometry.setAttribute('rainPosition', resource.position);
                geometry.setAttribute('rainShape', resource.shape);
                geometry.setIndex(resource.index);
                resource.views.set(batch, geometry);
            }
            batch.mesh.geometry = geometry;
            batch.resource = resource;
        }
        batch.mesh.geometry.setDrawRange(start, count);
        prepareUniforms(batch, textureId, matrix, vertexParams, pixelParams,
            depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare);
        stats.draws++;
        return batch;
    }

    function prepare(positions, shapes, indices, textureId, matrix, vertexParams, pixelParams, kind,
        depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare, range = null) {
        if (kind !== 4 && kind !== 5) throw new Error('Unsupported rain shader kind.');
        const count = range ? range.vertexCount : positions.length / 4;
        const indexCount = range ? range.indexCount : indices.length;
        if (!Number.isInteger(count) || count < 0 || !Number.isInteger(indexCount) || indexCount < 0
            || (!range && shapes.length !== positions.length) || matrix.length !== 16
            || vertexParams.length !== 24 || pixelParams.length !== 12) throw new Error('Invalid native rain channels.');
        if (range && (range.positionOffset + count * 4 > positions.length || range.shapeOffset + count * 4 > shapes.length
            || range.indexOffset + indexCount > indices.length)) throw new Error('Native rain channels exceed WASM memory.');
        const group = kind - 4;
        const slot = next[group]++;
        const batch = batches[group][slot] ??= create(kind);
        const mesh = batch.mesh;
        if (batch.resource) {
            batch.resource = null;
            mesh.geometry = new THREE.BufferGeometry();
            batch.capacity = batch.indexCapacity = 0;
        }
        if (batch.capacity < count || batch.indexCapacity < indexCount) {
            batch.capacity = Math.max(count, batch.capacity * 2, 4);
            batch.indexCapacity = Math.max(indexCount, batch.indexCapacity * 2, 6);
            mesh.geometry.dispose();
            const geometry = mesh.geometry = new THREE.BufferGeometry();
            const position = new THREE.BufferAttribute(new Float32Array(batch.capacity * 4), 4).setUsage(THREE.DynamicDrawUsage);
            geometry.setAttribute('position', position);
            geometry.setAttribute('rainPosition', position);
            geometry.setAttribute('rainShape', new THREE.BufferAttribute(new Float32Array(batch.capacity * 4), 4).setUsage(THREE.DynamicDrawUsage));
            geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(batch.indexCapacity), 1).setUsage(THREE.DynamicDrawUsage));
        }
        const geometry = mesh.geometry;
        const position = geometry.getAttribute('rainPosition');
        if (range) {
            for (let index = 0; index < count * 4; index++) position.array[index] = positions[range.positionOffset + index];
        } else position.array.set(positions);
        position.needsUpdate = true;
        const shape = geometry.getAttribute('rainShape');
        if (range) {
            for (let index = 0; index < count * 4; index++) shape.array[index] = shapes[range.shapeOffset + index];
        } else shape.array.set(shapes);
        shape.needsUpdate = true;
        if (range) {
            for (let index = 0; index < indexCount; index++) geometry.index.array[index] = indices[range.indexOffset + index];
        } else geometry.index.array.set(indices);
        geometry.index.needsUpdate = true;
        geometry.setDrawRange(0, indexCount);
        prepareUniforms(batch, textureId, matrix, vertexParams, pixelParams,
            depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare);
        return batch;
    }

    function prepareUniforms(batch, textureId, matrix, vertexParams, pixelParams,
        depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare) {
        if (matrix.length !== 16 || vertexParams.length !== 24 || pixelParams.length !== 12) throw new Error('Invalid native rain uniforms.');
        const state = batch.state;
        state.matrix.value.fromArray(matrix);
        if (depthCompare === 5 || depthCompare === 6) state.matrix.value.premultiply(reverseDepth);
        if (renderer.coordinateSystem === THREE.WebGLCoordinateSystem) state.matrix.value.premultiply(toWebGlDepth);
        state.volume.value.fromArray(vertexParams, 0);
        state.boxes.value.fromArray(vertexParams, 4);
        state.fall.value.fromArray(vertexParams, 8);
        state.velocity.value.fromArray(vertexParams, 12);
        state.eye.value.fromArray(vertexParams, 16);
        state.screen.value.fromArray(vertexParams, 20);
        state.color.value.fromArray(pixelParams, 0);
        state.occlusion.value.fromArray(pixelParams, 4);
        state.cells.value = pixelParams[8];
        batch.textureId = textureId;
        native.prepareState(batch, 'alpha', depthTest, depthWrite, scissor, viewport, rawBlend ?? rainBlend, blendFactor, depthCompare);
    }

    return {
        beginFrame() { next[0] = next[1] = 0; },
        prepare,
        createGeometry,
        destroyGeometry,
        prepareGeometry,
        getStats() { return { ...stats, resources: geometries.size }; },
        sameClip: native.sameClip,
        render(commands, start, end, hdr = false, scale = 1) {
            scene.clear();
            native.setClip(commands[start].batch, scale);
            const target = renderer.getRenderTarget();
            let hasMrt = false;
            if (target) for (const attachment of target.textures) {
                if (attachment.name === 'normalRoughness') { hasMrt = true; break; }
            }
            for (let index = start; index < end; index++) {
                const batch = commands[index].batch;
                if (batch.hasMrt !== hasMrt) {
                    batch.hasMrt = hasMrt;
                    batch.mesh.material.fragmentNode = hasMrt ? batch.mrtFragment : batch.fragment;
                    batch.mesh.material.needsUpdate = true;
                }
                batch.map.value = texture(batch.textureId) ?? emptyMap;
                batch.mesh.renderOrder = index - start;
                scene.add(batch.mesh);
            }
            renderer.render(scene, camera);
        },
        dispose() {
            for (const id of geometries.keys()) destroyGeometry(id);
            for (const group of batches) for (const batch of group) {
                batch.mesh.geometry.dispose();
                batch.mesh.material.dispose();
            }
            emptyMap.dispose();
        }
    };
}
