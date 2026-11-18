function bindBounds(descriptor) {
    const { positions, boneIndices, boneCount } = descriptor;
    if (!Number.isInteger(boneCount) || boneCount <= 0 || boneCount > 65536 || !positions?.length
        || positions.length % 3 || boneIndices?.length !== positions.length / 3) return null;
    const boxes = new Map();
    for (let vertex = 0; vertex < boneIndices.length; vertex++) {
        const bone = boneIndices[vertex], at = vertex * 3;
        const x = positions[at], y = positions[at + 1], z = positions[at + 2];
        if (!Number.isInteger(bone) || bone < 0 || bone >= boneCount
            || !Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return null;
        let box = boxes.get(bone);
        if (!box) { box = [x, y, z, x, y, z]; boxes.set(bone, box); }
        else {
            box[0] = Math.min(box[0], x); box[1] = Math.min(box[1], y); box[2] = Math.min(box[2], z);
            box[3] = Math.max(box[3], x); box[4] = Math.max(box[4], y); box[5] = Math.max(box[5], z);
        }
    }
    const packed = new Float64Array(boxes.size * 7);
    let at = 0;
    for (const [bone, box] of boxes) {
        packed[at++] = bone;
        for (let axis = 0; axis < 3; axis++) packed[at++] = (box[axis] + box[axis + 3]) * 0.5;
        for (let axis = 0; axis < 3; axis++) packed[at++] = (box[axis + 3] - box[axis]) * 0.5;
    }
    return packed;
}

export function createStageBounds(THREE, descriptor) {
    return new StageBounds(THREE, descriptor);
}

class StageBounds {
    constructor(THREE, descriptor) {
        this.sphere = new THREE.Sphere();
        this.owners = new Set();
        this.boxes = bindBounds(descriptor);
        this.boneCount = descriptor.boneCount;
        this.windRadius = Math.hypot(descriptor.material?.windX ?? 0, descriptor.material?.windZ ?? 0);
        this.eligible = !!this.boxes && Number.isFinite(this.windRadius);
        this.valid = false;
        this.disposed = false;
    }

    attach(object) {
        if (this.disposed) throw new Error('Animated scenery bounds are disposed.');
        this.owners.add(object);
        object.boundingSphere = this.sphere;
        object.frustumCulled = this.valid;
    }

    detach(object) {
        this.owners.delete(object);
        if (object.boundingSphere === this.sphere) object.boundingSphere = null;
        object.frustumCulled = false;
    }

    setValid(valid) {
        if (this.valid === valid) return valid;
        this.valid = valid;
        for (const object of this.owners) object.frustumCulled = valid;
        return valid;
    }

    update(rows) {
        if (this.disposed || !this.eligible || rows?.length !== this.boneCount * 12) return this.setValid(false);
        for (let at = 0; at < rows.length; at++) if (!Number.isFinite(rows[at])) return this.setValid(false);
        let minX = Infinity, minY = Infinity, minZ = Infinity, maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
        for (let at = 0; at < this.boxes.length; at += 7) {
            const row = this.boxes[at] * 12;
            const x = this.boxes[at + 1], y = this.boxes[at + 2], z = this.boxes[at + 3];
            const ex = this.boxes[at + 4], ey = this.boxes[at + 5], ez = this.boxes[at + 6];
            const cx = rows[row] * x + rows[row + 1] * y + rows[row + 2] * z + rows[row + 3];
            const cy = rows[row + 4] * x + rows[row + 5] * y + rows[row + 6] * z + rows[row + 7];
            const cz = rows[row + 8] * x + rows[row + 9] * y + rows[row + 10] * z + rows[row + 11];
            // abs(A) times a bind box's half-extents is the same conservative affine box as its eight corners.
            const dx = Math.abs(rows[row]) * ex + Math.abs(rows[row + 1]) * ey + Math.abs(rows[row + 2]) * ez;
            const dy = Math.abs(rows[row + 4]) * ex + Math.abs(rows[row + 5]) * ey + Math.abs(rows[row + 6]) * ez;
            const dz = Math.abs(rows[row + 8]) * ex + Math.abs(rows[row + 9]) * ey + Math.abs(rows[row + 10]) * ez;
            minX = Math.min(minX, cx - dx); maxX = Math.max(maxX, cx + dx);
            minY = Math.min(minY, cy - dy); maxY = Math.max(maxY, cy + dy);
            minZ = Math.min(minZ, cz - dz); maxZ = Math.max(maxZ, cz + dz);
        }
        const extentX = maxX - minX, extentY = maxY - minY, extentZ = maxZ - minZ;
        const magnitude = Math.max(Math.abs(minX), Math.abs(minY), Math.abs(minZ), Math.abs(maxX), Math.abs(maxY), Math.abs(maxZ));
        // Wind runs after skinning; float rounding is padded at the published world-coordinate magnitude.
        const radius = Math.hypot(extentX, extentY, extentZ) * 0.5 + this.windRadius + 0.001 + magnitude * 0.000001;
        if (!Number.isFinite(radius)) return this.setValid(false);
        this.sphere.center.set(minX + extentX * 0.5, minY + extentY * 0.5, minZ + extentZ * 0.5);
        this.sphere.radius = radius;
        return this.setValid(true);
    }

    dispose() {
        for (const object of this.owners) {
            if (object.boundingSphere === this.sphere) object.boundingSphere = null;
            object.frustumCulled = false;
        }
        this.owners.clear();
        this.boxes = null;
        this.valid = false;
        this.disposed = true;
    }
}
