import { buildStaticStageBatches, descriptorKey, geometryKey } from './three-batching.js';

const pageLimit = 16 * 1024 * 1024;
const totalLimit = 64 * 1024 * 1024;
const layerLimit = 128;
export const textureArrayLimits = Object.freeze({ pageBytes: pageLimit, totalBytes: totalLimit, maxLayers: layerLimit });

function mipBytes(width, height, mipmaps) {
    let bytes = width * height * 4;
    if (mipmaps) {
        while (width > 1 || height > 1) {
            width = Math.max(1, Math.floor(width / 2));
            height = Math.max(1, Math.floor(height / 2));
            bytes += width * height * 4;
        }
    }
    return bytes;
}

function textureInfo(THREE, id, resources) {
    const resource = resources.get(id);
    const texture = resource?.texture;
    if (!Number.isInteger(id) || id >= 0 || !resource?.sceneImmutable || resource.renderTarget || resource.target
        || resource.mutable || resource.format !== 0 || !texture?.isDataTexture || texture.isDataArrayTexture
        || texture.format !== THREE.RGBAFormat || texture.type !== THREE.UnsignedByteType || texture.flipY || texture.mapping !== THREE.UVMapping
        || texture.mipmaps.length || texture.isVideoTexture || texture.isRenderTargetTexture
        || (resource.encoding !== 0 && resource.encoding !== 1)
        || texture.colorSpace !== (resource.encoding ? THREE.SRGBColorSpace : THREE.NoColorSpace)) return null;
    const { width, height } = resource;
    const data = texture.image?.data;
    if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0
        || texture.image.width !== width || texture.image.height !== height || !(data instanceof Uint8Array)
        || data.length !== width * height * 4) return null;
    let hasAlpha = false;
    for (let at = 3; at < data.length; at += 4) if (data[at] !== 255) { hasAlpha = true; break; }
    const policy = { width, height, encoding: resource.encoding, colorSpace: texture.colorSpace,
        wrapS: texture.wrapS, wrapT: texture.wrapT, magFilter: texture.magFilter, minFilter: texture.minFilter,
        anisotropy: texture.anisotropy, generateMipmaps: texture.generateMipmaps, flipY: texture.flipY,
        premultiplyAlpha: texture.premultiplyAlpha, unpackAlignment: texture.unpackAlignment,
        offset: texture.offset.toArray(), repeat: texture.repeat.toArray(), center: texture.center.toArray(),
        rotation: texture.rotation, matrixAutoUpdate: texture.matrixAutoUpdate, matrix: texture.matrix.elements.slice(),
        channel: texture.channel, compareFunction: texture.compareFunction ?? null, internalFormat: texture.internalFormat };
    const key = descriptorKey(policy);
    return key === null ? null : { id, resource, texture, data, hasAlpha, key,
        bytes: mipBytes(width, height, texture.generateMipmaps) };
}

function candidate(THREE, entry, resources, textures, allowCutouts) {
    const source = entry.descriptor?.material;
    const material = entry.material;
    const cutout = allowCutouts && material?.alphaTest > 0 && material.alphaTest === source?.alphaCutoff;
    if (!source || entry.descriptor.boneCount || entry.descriptor.isSky || !entry.geometry?.isBufferGeometry
        || entry.geometry.getAttribute('textureLayer') || !material || Array.isArray(material)
        || material.transparent || material.blending !== THREE.NoBlending || (material.alphaTest !== 0 && !cutout) || material.alphaHash
        || !material.depthWrite || material.opacity !== 1 || (source.alphaCutoff && !cutout) || source.texture2Id
        || (source.blend && source.blend !== 'opaque') || source.windX || source.windZ || source.blink
        || source.uvForm || source.uvForm2 || source.textureIds?.length > 1
        || (source.textureIds?.length === 1 && source.textureIds[0] !== source.textureId)
        || (source.opacity !== undefined && source.opacity !== 1)
        || (source.color?.[3] !== undefined && source.color[3] !== 1) || geometryKey(THREE, entry.geometry) === null) return null;
    let info = textures.get(source.textureId);
    if (info === undefined) {
        info = textureInfo(THREE, source.textureId, resources);
        textures.set(source.textureId, info);
    }
    if (!info || (info.hasAlpha && !cutout)) return null;
    const descriptor = { ...entry.descriptor, material: { ...source, textureId: 0, textureIds: [] } };
    const renderPolicy = {};
    for (const name of ['type', 'side', 'alphaTest', 'depthTest', 'depthWrite', 'depthFunc', 'colorWrite', 'vertexColors', 'premultipliedAlpha',
        'dithering', 'alphaToCoverage', 'forceSinglePass', 'flatShading', 'fog', 'toneMapped', 'polygonOffset',
        'polygonOffsetFactor', 'polygonOffsetUnits']) {
        if (material[name] !== undefined) renderPolicy[name] = material[name];
    }
    const key = descriptorKey({ material: descriptor.material, render: renderPolicy });
    return key === null ? null : { entry, descriptor, texture: info, key: key + '|' + info.key };
}

function arrayTexture(THREE, textures) {
    const first = textures[0];
    const data = new Uint8Array(first.data.length * textures.length);
    for (let index = 0; index < textures.length; index++) data.set(textures[index].data, index * first.data.length);
    const result = new THREE.DataArrayTexture(data, first.resource.width, first.resource.height, textures.length);
    for (const key of ['wrapS', 'wrapT', 'magFilter', 'minFilter', 'anisotropy', 'generateMipmaps', 'colorSpace',
        'flipY', 'premultiplyAlpha', 'unpackAlignment', 'rotation', 'matrixAutoUpdate', 'channel', 'compareFunction', 'internalFormat']) {
        result[key] = first.texture[key];
    }
    result.offset.copy(first.texture.offset);
    result.repeat.copy(first.texture.repeat);
    result.center.copy(first.texture.center);
    result.matrix.copy(first.texture.matrix);
    result.needsUpdate = true;
    return result;
}

// Input geometry, textures and materials remain owned by their existing stage resources.
export function buildTextureArrayStageBatches(THREE, entries, textureResources, {
    chunkSize = 4096, maxPageBytes = pageLimit, maxLayers = layerLimit, maxTotalBytes = totalLimit, createMaterial, allowCutouts = false
} = {}) {
    if (!Number.isFinite(chunkSize) || chunkSize <= 0 || !Number.isSafeInteger(maxPageBytes) || maxPageBytes < 0
        || !Number.isSafeInteger(maxTotalBytes) || maxTotalBytes < 0 || !Number.isInteger(maxLayers) || maxLayers < 0
        || typeof createMaterial !== 'function') throw new RangeError('Invalid texture-array stage options.');
    maxPageBytes = Math.min(pageLimit, maxPageBytes);
    maxTotalBytes = Math.min(totalLimit, maxTotalBytes);
    maxLayers = Math.min(layerLimit, maxLayers);
    const partitions = new Map(), textures = new Map(), originals = new Map();
    const sourceMaterials = new Set(entries.map(entry => entry.material));
    const batchedSourceIds = new Set();
    const unbatched = [], batches = [], pages = [], materials = new Set();
    let bytes = 0, eligibleObjects = 0;
    for (const entry of entries) {
        if (originals.has(entry.id)) throw new Error('Texture-array stage mesh IDs must be unique.');
        originals.set(entry.id, entry);
        const item = candidate(THREE, entry, textureResources, textures, allowCutouts === true);
        if (!item) {
            unbatched.push(entry);
            continue;
        }
        eligibleObjects++;
        let partition = partitions.get(item.key);
        if (!partition) partitions.set(item.key, partition = new Map());
        let group = partition.get(item.texture.id);
        if (!group) partition.set(item.texture.id, group = { texture: item.texture, items: [] });
        group.items.push(item);
    }
    function dispose() {
        for (const batch of batches) batch.geometry.dispose();
        for (const material of materials) material.dispose();
        for (const page of pages) page.dispose();
    }
    try {
        for (const partition of partitions.values()) {
            const groups = [...partition.values()];
            const layerBytes = groups[0].texture.bytes;
            const capacity = Math.min(maxLayers, Math.floor(maxPageBytes / layerBytes));
            for (let start = 0; start < groups.length;) {
                const remaining = Math.min(capacity, Math.floor((maxTotalBytes - bytes) / layerBytes));
                const selected = groups.slice(start, start + Math.max(1, remaining));
                start += selected.length;
                if (remaining < 2 || selected.length < 2) {
                    for (const group of selected) for (const item of group.items) unbatched.push(item.entry);
                    continue;
                }
                const clones = [];
                let merged;
                try {
                    for (let layer = 0; layer < selected.length; layer++) {
                        for (const item of selected[layer].items) {
                            const geometry = item.entry.geometry.clone();
                            const position = geometry.getAttribute('position');
                            if (position) geometry.setAttribute('textureLayer', new THREE.Float32BufferAttribute(new Float32Array(position.count).fill(layer), 1));
                            clones.push({ ...item.entry, descriptor: item.descriptor, geometry });
                        }
                    }
                    merged = buildStaticStageBatches(THREE, clones, { chunkSize });
                } finally {
                    for (const clone of clones) clone.geometry.dispose();
                }
                for (const original of merged.unbatched) unbatched.push(originals.get(original.id));
                if (!merged.batches.length) continue;
                for (const batch of merged.batches) batches.push(batch);
                const page = arrayTexture(THREE, selected.map(group => group.texture));
                pages.push(page);
                bytes += layerBytes * selected.length;
                for (const batch of merged.batches) {
                    batch.material = createMaterial(batch.descriptor, page, batch.geometry);
                    if (!batch.material?.isMaterial || sourceMaterials.has(batch.material)) {
                        throw new Error('Texture-array material factory must return an independently owned material.');
                    }
                    materials.add(batch.material);
                    batch.textureIds = selected.map(group => group.texture.id);
                    for (const id of batch.meshIds) batchedSourceIds.add(id);
                }
            }
        }
    } catch (error) {
        dispose();
        throw error;
    }
    let disposed = false;
    return { batches, unbatched, bytes, stats: { inputObjects: entries.length, outputObjects: batches.length + unbatched.length,
        eligibleObjects, groups: partitions.size, batchedSources: batchedSourceIds.size, batches: batches.length, pages: pages.length,
        layers: pages.reduce((sum, page) => sum + page.image.depth, 0), bytes },
    dispose() {
        if (disposed) return;
        disposed = true;
        dispose();
    } };
}

export function createStaticArrayObserverPolicy(THREE) {
    const brands = new WeakMap();
    const textureProperty = 'noxiusObserverArrayTexture';
    const counts = { full: 0, shared: 0, none: 0, fallback: 0 };
    let enabled = true;
    let branded = 0;

    function approved(material, record, object, context) {
        const source = material?.userData.source;
        if (!record || !record.live || material.colorNode !== record.root || material.userData.arrayTexture !== record.texture
            || material[textureProperty] !== record.texture || material.userData.actor || !source
            || source.windX || source.windZ || source.uvForm || source.uvForm2 || source.blink || source.texture2Id
            || source.textureIds?.length > 1 || material.map || material.transparent || material.alphaTest !== 0
            || material.blending !== THREE.NoBlending || !material.depthWrite || material.opacity !== 1
            || object?.isSkinnedMesh || object?.isInstancedMesh || object?.geometry?.isInstancedBufferGeometry
            || context?.modelViewMatrix || context?.modelNormalViewMatrix || context?.getAO || context?.getShadow || context?.getGI) return false;
        for (const key of record.nodeSlots) {
            if (key !== 'colorNode' && material[key]?.isNode) return false;
        }
        return true;
    }

    class StaticArrayObserver extends THREE.NodeMaterialObserver {
        constructor(builder) {
            super(builder);
            this.original = new THREE.NodeMaterialObserver(builder);
            this.context = builder.context;
            // The standard comparison code also observes this otherwise hidden array sampler.
            this.refreshUniforms = [...this.refreshUniforms, textureProperty];
        }

        containsNode(builder) {
            return approved(builder.material, brands.get(builder.material), builder.object, builder.context)
                ? false : super.containsNode(builder);
        }

        needsRefresh(renderObject, frame) {
            const material = renderObject.material;
            if (!enabled || !approved(material, brands.get(material), renderObject.object, this.context)) {
                counts.fallback++;
                return this.original.needsRefresh(renderObject, frame);
            }
            let result = super.needsRefresh(renderObject, frame);
            // Static bundles normally skip comparisons; keep the standard mutable-state guards.
            if (result !== THREE.RenderObjectRefreshType.FULL
                && (renderObject.object.static === true || renderObject.bundle?.static === true)) {
                const lights = this.getLights(renderObject.lightsNode, frame.renderId);
                if (!this.equals(renderObject, lights, frame.renderId)) result = THREE.RenderObjectRefreshType.FULL;
            }
            if (result === THREE.RenderObjectRefreshType.FULL) counts.full++;
            else if (result === THREE.RenderObjectRefreshType.SHARED) counts.shared++;
            else counts.none++;
            return result;
        }
    }

    return {
        get enabled() { return enabled; },
        setEnabled(value) { enabled = !!value; return enabled; },
        // Called only by the owned array-material factory before the first compilation.
        brand(material) {
            if (brands.has(material)) return true;
            if (material.constructor !== THREE.MeshBasicNodeMaterial && material.constructor !== THREE.MeshStandardNodeMaterial) return false;
            const texture = material.userData?.arrayTexture;
            if (!texture?.isDataArrayTexture || !material.colorNode || material.userData.baseColorNode !== material.colorNode) return false;
            const nodeSlots = [];
            for (const key in material) if (key.endsWith('Node') || material[key]?.isNode) nodeSlots.push(key);
            const record = { root: material.colorNode, texture, nodeSlots, live: true };
            material[textureProperty] = texture;
            if (!approved(material, record)) { delete material[textureProperty]; return false; }
            brands.set(material, record);
            branded++;
            material.setupObserver = builder => new StaticArrayObserver(builder);
            material.addEventListener('dispose', () => {
                if (record.live) { record.live = false; branded--; }
            });
            return true;
        },
        snapshot(reset = true) {
            const result = { requested: true, enabled, branded, ...counts };
            if (reset) { counts.full = 0; counts.shared = 0; counts.none = 0; counts.fallback = 0; }
            return result;
        }
    };
}
