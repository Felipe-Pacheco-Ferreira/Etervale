export function descriptorKey(value) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return JSON.stringify(value);
    if (typeof value === 'number') return Number.isFinite(value) ? JSON.stringify(value) : null;
    if (Array.isArray(value)) {
        const items = value.map(descriptorKey);
        return items.includes(null) ? null : '[' + items.join(',') + ']';
    }
    if (typeof value !== 'object' || Object.getPrototypeOf(value) !== Object.prototype) return null;
    const items = [];
    for (const key of Object.keys(value).sort()) {
        const item = descriptorKey(value[key]);
        if (item === null) return null;
        items.push(JSON.stringify(key) + ':' + item);
    }
    return '{' + items.join(',') + '}';
}

export function geometryKey(THREE, geometry) {
    const position = geometry.getAttribute('position');
    if (!position || position.itemSize !== 3 || position.normalized || position.count === 0 || geometry.isInstancedBufferGeometry
        || geometry.groups.length || Object.keys(geometry.morphAttributes).length
        || geometry.drawRange.start !== 0 || geometry.drawRange.count !== Infinity) return null;
    const attributes = [];
    for (const name of Object.keys(geometry.attributes).sort()) {
        const attribute = geometry.getAttribute(name);
        if (name === 'skinIndex' || name === 'skinWeight' || attribute.isInterleavedBufferAttribute || attribute.isFloat16BufferAttribute
            || attribute.isInstancedBufferAttribute || attribute.usage !== THREE.StaticDrawUsage
            || attribute.count !== position.count || !ArrayBuffer.isView(attribute.array)) return null;
        attributes.push([name, attribute.array.constructor.name, attribute.itemSize, attribute.normalized, attribute.gpuType]);
    }
    const index = geometry.getIndex();
    const count = index?.count ?? position.count;
    if (count === 0 || count % 3 !== 0 || (index && (index.itemSize !== 1 || index.normalized || index.usage !== THREE.StaticDrawUsage))) return null;
    for (let at = 0; at < position.array.length; at++) {
        if (!Number.isFinite(position.array[at])) return null;
    }
    if (index) {
        for (let at = 0; at < index.count; at++) {
            const vertex = index.array[at];
            if (!Number.isInteger(vertex) || vertex < 0 || vertex >= position.count) return null;
        }
    }
    return JSON.stringify(attributes);
}

function entryKey(THREE, entry) {
    const descriptor = entry.descriptor;
    const source = descriptor?.material ?? {};
    const material = entry.material;
    if (!descriptor || descriptor.boneCount || descriptor.isSky || !material || Array.isArray(material)
        || material.transparent || material.blending !== THREE.NoBlending || !material.depthWrite || material.opacity !== 1
        || (source.blend && source.blend !== 'opaque') || source.windX || source.windZ || source.blink
        || source.textureIds?.length > 1 || source.uvForm || source.uvForm2
        || (source.opacity !== undefined && source.opacity !== 1) || (source.color?.[3] !== undefined && source.color[3] !== 1)) return null;
    const policy = descriptorKey(source);
    const attributes = geometryKey(THREE, entry.geometry);
    if (policy === null || attributes === null) return null;
    return policy + '|' + attributes + '|' + [material.side, material.alphaTest, material.depthFunc,
        material.polygonOffset, material.polygonOffsetFactor, material.polygonOffsetUnits, material.colorWrite].join(',');
}

function mergeBucket(THREE, bucket) {
    const parts = [...bucket.parts.values()];
    const first = parts[0].record.entry;
    let vertexCount = 0;
    let indexCount = 0;
    for (const part of parts) {
        part.vertices = [];
        part.remap = new Map();
        for (const vertex of part.indices) {
            if (!part.remap.has(vertex)) {
                part.remap.set(vertex, vertexCount++);
                part.vertices.push(vertex);
            }
        }
        indexCount += part.indices.length;
    }
    const geometry = new THREE.BufferGeometry();
    for (const name of Object.keys(first.geometry.attributes)) {
        const original = first.geometry.getAttribute(name);
        const values = new original.array.constructor(vertexCount * original.itemSize);
        let offset = 0;
        for (const part of parts) {
            const attribute = part.record.entry.geometry.getAttribute(name);
            for (const vertex of part.vertices) {
                const start = vertex * attribute.itemSize;
                for (let component = 0; component < attribute.itemSize; component++) values[offset++] = attribute.array[start + component];
            }
        }
        const attribute = new THREE.BufferAttribute(values, original.itemSize, original.normalized);
        attribute.gpuType = original.gpuType;
        attribute.name = original.name;
        attribute.setUsage(original.usage);
        geometry.setAttribute(name, attribute);
    }
    const indices = new Uint32Array(indexCount);
    let offset = 0;
    for (const part of parts) {
        for (const vertex of part.indices) indices[offset++] = part.remap.get(vertex);
    }
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));
    geometry.computeBoundingBox();
    geometry.computeBoundingSphere();
    return { geometry, material: first.material, descriptor: first.descriptor, meshIds: parts.map(part => part.record.entry.id) };
}

// Entries are immutable, world-baked stage resources. Only the returned merged geometries belong to this helper.
export function buildStaticStageBatches(THREE, entries, { chunkSize = 1024 } = {}) {
    if (!Number.isFinite(chunkSize) || chunkSize <= 0) throw new RangeError('Static stage chunk size must be positive.');
    const partitions = new Map();
    const records = [];
    const unbatched = [];
    for (const entry of entries) {
        const key = entryKey(THREE, entry);
        if (key === null) {
            unbatched.push(entry);
            continue;
        }
        const record = { entry, buckets: [], component: null };
        records.push(record);
        let chunks = partitions.get(key);
        if (!chunks) partitions.set(key, chunks = new Map());
        const position = entry.geometry.getAttribute('position').array;
        const indices = entry.geometry.getIndex()?.array;
        const count = indices?.length ?? position.length / 3;
        for (let at = 0; at < count; at += 3) {
            const a = indices ? indices[at] : at;
            const b = indices ? indices[at + 1] : at + 1;
            const c = indices ? indices[at + 2] : at + 2;
            const x = Math.floor((position[a * 3] + position[b * 3] + position[c * 3]) / 3 / chunkSize);
            const z = Math.floor((position[a * 3 + 2] + position[b * 3 + 2] + position[c * 3 + 2]) / 3 / chunkSize);
            const chunkKey = x + ',' + z;
            let bucket = chunks.get(chunkKey);
            if (!bucket) {
                chunks.set(chunkKey, bucket = { parts: new Map() });
            }
            let part = bucket.parts.get(record);
            if (!part) {
                bucket.parts.set(record, part = { record, indices: [] });
                record.buckets.push(bucket);
            }
            part.indices.push(a, b, c);
        }
    }
    const batches = [];
    try {
        for (const record of records) {
            if (record.component) continue;
            const component = { records: [], buckets: new Set() };
            record.component = component;
            const pending = [record];
            while (pending.length) {
                const current = pending.pop();
                component.records.push(current);
                for (const bucket of current.buckets) {
                    if (component.buckets.has(bucket)) continue;
                    component.buckets.add(bucket);
                    for (const related of bucket.parts.keys()) {
                        if (related.component) continue;
                        related.component = component;
                        pending.push(related);
                    }
                }
            }
            // A wide source must not turn into many draw calls merely to gain finer culling.
            // Decide per connected spatial component so useful local merges still survive.
            if (component.buckets.size >= component.records.length) {
                for (const original of component.records) unbatched.push(original.entry);
            } else {
                for (const bucket of component.buckets) batches.push(mergeBucket(THREE, bucket));
            }
        }
    } catch (error) {
        for (const batch of batches) batch.geometry.dispose();
        throw error;
    }
    let disposed = false;
    return { batches, unbatched, dispose() {
        if (disposed) return;
        disposed = true;
        for (const batch of batches) batch.geometry.dispose();
    } };
}
