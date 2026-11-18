function eligible(THREE, object) {
    const material = object.material;
    const source = material?.userData.source;
    if (!object.isMesh || object.isSkinnedMesh || object.visible === false || !source || Array.isArray(material)
        || material.userData.actor || material.transparent || material.blending !== THREE.NoBlending || !material.depthWrite
        || material.opacity !== 1 || source.opacity < 1 || source.blink || source.textureIds?.length > 1
        || material.map?.isRenderTargetTexture || object.matrixAutoUpdate || object.matrixWorldAutoUpdate
        || object.renderOrder !== 0 || object.geometry.isInstancedBufferGeometry
        || Object.keys(object.geometry.morphAttributes).length) return false;
    for (const attribute of Object.values(object.geometry.attributes)) {
        if (attribute.usage !== THREE.StaticDrawUsage || attribute.isInstancedBufferAttribute) return false;
    }
    return !object.geometry.index || object.geometry.index.usage === THREE.StaticDrawUsage;
}

// The source stage still owns every mesh, material and geometry. Chunks only cache command submission.
export function createStageBundleCache(THREE, renderer, requested = false, { chunkSize = 1024 } = {}) {
    if (!Number.isFinite(chunkSize) || chunkSize <= 0) throw new RangeError('Render-bundle chunk size must be positive.');
    const previousPipeline = renderer.drawingPipeline;
    const supported = renderer.backend?.isWebGPUBackend === true && typeof renderer.backend.beginBundle === 'function'
        && (!previousPipeline || previousPipeline.constructor === THREE.StandardDrawingPipeline);
    const stages = new Map();
    const sphere = new THREE.Sphere();
    let enabled = requested && supported;
    class BundlePipeline extends THREE.StandardDrawingPipeline {
        project(object, camera, groupOrder, renderList, clippingContext) {
            const chunk = object.userData?.stageBundleChunk;
            if (chunk && !this._frustum.intersectsSphere(chunk.bounds)) return;
            super.project(object, camera, groupOrder, renderList, clippingContext);
        }
    }
    const pipeline = requested && supported ? new BundlePipeline() : null;
    if (pipeline) renderer.drawingPipeline = pipeline;

    function flatten(record) {
        for (const chunk of record.chunks) {
            // RenderObjects can retain a reference to a former bundle when they return to normal submission.
            chunk.static = false;
            chunk.needsUpdate = true;
            record.stage.remove(chunk);
        }
        for (const object of record.objects) record.stage.add(object);
        record.chunks.length = 0;
        record.meshes = 0;
    }

    function build(record) {
        if (!enabled) return;
        const cells = new Map();
        const worldBox = new THREE.Box3();
        const size = new THREE.Vector3();
        for (const object of record.objects) {
            if (!eligible(THREE, object)) continue;
            const geometry = object.geometry;
            if (!geometry.boundingBox) geometry.computeBoundingBox();
            if (!geometry.boundingBox || geometry.boundingBox.isEmpty()) continue;
            worldBox.copy(geometry.boundingBox).applyMatrix4(object.matrixWorld);
            const source = object.material.userData.source;
            const windX = Math.abs(source.windX ?? 0), windZ = Math.abs(source.windZ ?? 0);
            worldBox.min.x -= windX; worldBox.max.x += windX;
            worldBox.min.z -= windZ; worldBox.max.z += windZ;
            if (![worldBox.min.x, worldBox.min.y, worldBox.min.z, worldBox.max.x, worldBox.max.y, worldBox.max.z].every(Number.isFinite)) continue;
            worldBox.getBoundingSphere(sphere);
            worldBox.getSize(size);
            const cell = Math.max(size.x, size.z) > chunkSize * 2 ? 'wide-' + object.id
                : Math.floor(sphere.center.x / chunkSize) + ',' + Math.floor(sphere.center.z / chunkSize);
            const key = cell + '|' + object.layers.mask;
            let chunk = cells.get(key);
            if (!chunk) {
                chunk = new THREE.BundleGroup();
                chunk.name = record.stage.name + '-bundle-' + key;
                chunk.renderOrder = record.stage.renderOrder;
                chunk.layers.mask = object.layers.mask;
                chunk.userData.stageBundleChunk = { bounds: new THREE.Sphere(), box: new THREE.Box3() };
                cells.set(key, chunk);
                record.chunks.push(chunk);
                record.stage.add(chunk);
            }
            chunk.userData.stageBundleChunk.box.union(worldBox);
            chunk.add(object);
            record.meshes++;
        }
        for (const chunk of record.chunks) {
            chunk.userData.stageBundleChunk.box.getBoundingSphere(chunk.userData.stageBundleChunk.bounds);
        }
    }

    return {
        requested, supported,
        get enabled() { return enabled; },
        register(stage) {
            this.unregister(stage);
            const record = { stage, objects: stage.userData.renderObjects ?? Array.from(stage.children), chunks: [], meshes: 0,
                lights: -1, shadows: null, renderOrder: stage.renderOrder };
            stages.set(stage, record);
            build(record);
        },
        unregister(stage) {
            const record = stages.get(stage);
            if (!record) return;
            flatten(record);
            stages.delete(stage);
        },
        invalidate(stage = null) {
            if (stage) {
                const record = stages.get(stage);
                if (record) for (const chunk of record.chunks) chunk.needsUpdate = true;
            } else {
                for (const record of stages.values()) for (const chunk of record.chunks) chunk.needsUpdate = true;
            }
        },
        prepare(stage, lightCount, shadows) {
            const record = stages.get(stage);
            if (!record) return;
            if (record.lights !== lightCount || record.shadows !== shadows || record.renderOrder !== stage.renderOrder) {
                for (const chunk of record.chunks) {
                    chunk.renderOrder = stage.renderOrder;
                    chunk.needsUpdate = true;
                }
                record.lights = lightCount;
                record.shadows = shadows;
                record.renderOrder = stage.renderOrder;
            }
        },
        setEnabled(value) {
            value = !!value && requested && supported;
            if (value === enabled) return enabled;
            enabled = value;
            for (const record of stages.values()) {
                flatten(record);
                build(record);
            }
            return enabled;
        },
        snapshot() {
            let chunks = 0, meshes = 0;
            for (const record of stages.values()) { chunks += record.chunks.length; meshes += record.meshes; }
            return { requested, supported, enabled, stages: stages.size, chunks, meshes, chunkSize,
                counterMeaning: 'Three counters measure newly encoded draw commands; cached GPU bundle replay is not included.' };
        },
        dispose() {
            for (const record of stages.values()) flatten(record);
            stages.clear();
            if (pipeline && renderer.drawingPipeline === pipeline) renderer.drawingPipeline = previousPipeline;
        }
    };
}
