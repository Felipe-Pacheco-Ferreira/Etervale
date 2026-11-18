// The .NET spans are valid only during the imported call. Reusable copies also survive WASM memory growth.
class SpanBuffer {
    read(source, ArrayType = Float32Array) {
        const length = source.byteLength;
        if (!this.storage || this.storage.byteLength < length) {
            this.storage = new ArrayBuffer(Math.max(64, length, (this.storage?.byteLength ?? 0) * 2));
            this.views = new Map();
        }
        let view = this.views.get(length);
        if (!view) {
            view = { bytes: new Uint8Array(this.storage, 0, length), floats: new Float32Array(this.storage, 0, length / 4),
                ints: new Int32Array(this.storage, 0, length / 4) };
            this.views.set(length, view);
        }
        if (source.copyTo) source.copyTo(view.bytes);
        else view.bytes.set(new Uint8Array(source.buffer ?? source, source.byteOffset ?? 0, length));
        return ArrayType === Int32Array ? view.ints : view.floats;
    }
}

function boneRange(cache, values, start, count) {
    if (cache.buffer !== values.buffer) {
        cache.buffer = values.buffer;
        cache.offsets = new Map();
    }
    let counts = cache.offsets.get(start);
    if (!counts) cache.offsets.set(start, counts = new Map());
    let view = counts.get(count);
    if (!view) {
        view = new Float32Array(values.buffer, values.byteOffset + start * 4, count);
        counts.set(count, view);
    }
    return view;
}

function frameState() {
    return { view: new Float32Array(16), projection: new Float32Array(16), ambient: new Float32Array(3),
        cameraPosition: new Float32Array(3), cameraTarget: new Float32Array(3),
        groundAmbient: new Float32Array(3), sunDirection: new Float32Array(3), sunColor: new Float32Array(3),
        background: new Float32Array(4), pointLights: [], pointPool: [], fog: {} };
}

function copy(destination, source, offset) {
    for (let index = 0; index < destination.length; index++) destination[index] = source[offset + index];
}

function unpackFrame(frame, values) {
    if (values.length < 68) throw new Error('Invalid packed scene frame.');
    copy(frame.view, values, 0);
    copy(frame.projection, values, 16);
    copy(frame.cameraPosition, values, 32);
    copy(frame.cameraTarget, values, 35);
    copy(frame.ambient, values, 38);
    copy(frame.groundAmbient, values, 41);
    copy(frame.sunDirection, values, 44);
    copy(frame.sunColor, values, 47);
    copy(frame.background, values, 50);
    frame.fog.mode = values[54];
    frame.fog.start = values[55];
    frame.fog.density = values[56];
    frame.fog.maxOpacity = values[57];
    frame.fog.color ??= new Float32Array(3);
    copy(frame.fog.color, values, 60);
    frame.timeSeconds = values[63];
    frame.deltaSeconds = values[64];
    frame.underground = !!values[65];
    frame.shadowsEnabled = !!values[66];
    const count = values[67];
    if (!Number.isInteger(count) || count < 0 || values.length !== 68 + count * 8) throw new Error('Invalid packed point lights.');
    frame.pointLights.length = count;
    for (let index = 0; index < count; index++) {
        const point = frame.pointPool[index] ??= { position: new Float32Array(3), color: new Float32Array(3) };
        const at = 68 + index * 8;
        copy(point.position, values, at);
        point.range = values[at + 3];
        copy(point.color, values, at + 4);
        point.intensity = values[at + 7];
        frame.pointLights[index] = point;
    }
}

function unpackPortrait(frame, values) {
    if (values.length !== 55) throw new Error('Invalid packed portrait frame.');
    copy(frame.view, values, 0);
    copy(frame.projection, values, 16);
    copy(frame.cameraPosition, values, 32);
    copy(frame.sunDirection, values, 35);
    copy(frame.sunColor, values, 38);
    copy(frame.ambient, values, 41);
    copy(frame.groundAmbient, values, 44);
    const rim = frame.pointPool[0] ??= { position: new Float32Array(3), color: new Float32Array(3) };
    copy(rim.position, values, 47);
    copy(rim.color, values, 50);
    rim.range = values[53];
    rim.intensity = 1;
    frame.pointLights.length = 1;
    frame.pointLights[0] = rim;
    frame.fog.mode = 0;
    frame.exposure = values[54];
}

export function createPackedImports(graphics) {
    const scenes = new Map();
    function scene(id) {
        const state = scenes.get(id);
        if (!state) throw new Error(`Unknown packed scene ${id}.`);
        return state;
    }
    function flushScenePacked(id, actorMeta, actorStates, actorBones, meshMeta, meshBones) {
        const state = scene(id);
        const meta = state.actorMeta.read(actorMeta, Int32Array);
        const values = state.actorStates.read(actorStates);
        const bones = state.actorBones.read(actorBones);
        if (meta.length % 4 || values.length !== meta.length / 4 * 24) throw new Error('Invalid packed actors.');
        for (let index = 0; index < meta.length / 4; index++) {
            const at = index * 24;
            const actor = state.actor;
            actor.visible = !!values[at];
            copy(actor.colorAdd, values, at + 1);
            copy(actor.outline, values, at + 4);
            copy(actor.tint, values, at + 8);
            actor.opacity = values[at + 11];
            actor.castsShadow = !!values[at + 12];
            actor.wobble = !!values[at + 13];
            actor.scale = values[at + 14];
            actor.floorLight = values[at + 15];
            copy(actor.boundsMin, values, at + 16);
            copy(actor.boundsMax, values, at + 19);
            actor.overlayTextureId = values[at + 22];
            actor.overlayForm = values[at + 23];
            const start = meta[index * 4 + 1];
            const count = meta[index * 4 + 2];
            if (start < 0 || count < 0 || start + count > bones.length) throw new Error('Invalid packed actor bones.');
            graphics.updateActor(id, meta[index * 4], actor, boneRange(state.actorRanges, bones, start, count));
        }
        const meshes = state.meshMeta.read(meshMeta, Int32Array);
        const meshPalette = state.meshBones.read(meshBones);
        if (meshes.length % 3) throw new Error('Invalid packed animated meshes.');
        for (let index = 0; index < meshes.length; index += 3) {
            const start = meshes[index + 1];
            const count = meshes[index + 2];
            if (start < 0 || count < 0 || start + count > meshPalette.length) throw new Error('Invalid packed mesh bones.');
            graphics.updateMeshPose(id, meshes[index], boneRange(state.meshRanges, meshPalette, start, count));
        }
    }
    return {
        createScene(id) {
            graphics.createScene(id);
            scenes.set(id, { frame: frameState(), portrait: frameState(), frameBytes: new SpanBuffer(), portraitBytes: new SpanBuffer(),
                actorMeta: new SpanBuffer(), actorStates: new SpanBuffer(), actorBones: new SpanBuffer(),
                meshMeta: new SpanBuffer(), meshBones: new SpanBuffer(), textures: new Set(),
                actorRanges: {}, meshRanges: {},
                actor: { colorAdd: new Float32Array(3), outline: new Float32Array(4), tint: new Float32Array(3),
                    boundsMin: new Float32Array(3), boundsMax: new Float32Array(3) } });
        },
        configureScene(id, json) { graphics.configureScene(id, JSON.parse(json)); },
        createSceneTexture(id, textureId, width, height, rgba) {
            const data = new Uint8Array(rgba.byteLength);
            if (rgba.copyTo) rgba.copyTo(data);
            else data.set(rgba);
            graphics.createTexture(textureId, width, height, data);
            graphics.configureSceneTexture(id, textureId);
            scene(id).textures.add(textureId);
        },
        createMesh(sceneId, id, json, vertices, indices) {
            const descriptor = JSON.parse(json);
            const source = new SpanBuffer().read(vertices);
            const index = new SpanBuffer().read(indices, Int32Array);
            if (descriptor.vertexStride !== 17 || source.length !== descriptor.vertexCount * 17) throw new Error('Invalid packed scene mesh.');
            const channels = { positions: new Float32Array(descriptor.vertexCount * 3), normals: new Float32Array(descriptor.vertexCount * 3),
                colors: new Float32Array(descriptor.vertexCount * 4), uv: new Float32Array(descriptor.vertexCount * 2),
                uv1: new Float32Array(descriptor.vertexCount * 2), uv2: new Float32Array(descriptor.vertexCount * 2),
                boneIndices: descriptor.boneCount ? new Float32Array(descriptor.vertexCount) : null,
                indices: index, material: descriptor.material, boneCount: descriptor.boneCount, isSky: descriptor.isSky };
            for (let vertex = 0; vertex < descriptor.vertexCount; vertex++) {
                const at = vertex * 17;
                for (let component = 0; component < 3; component++) {
                    channels.positions[vertex * 3 + component] = source[at + component];
                    channels.normals[vertex * 3 + component] = source[at + 3 + component];
                }
                for (let component = 0; component < 4; component++) channels.colors[vertex * 4 + component] = source[at + 6 + component];
                for (let component = 0; component < 2; component++) {
                    channels.uv[vertex * 2 + component] = source[at + 10 + component];
                    channels.uv1[vertex * 2 + component] = source[at + 12 + component];
                    channels.uv2[vertex * 2 + component] = source[at + 14 + component];
                }
                if (channels.boneIndices) channels.boneIndices[vertex] = source[at + 16];
            }
            graphics.createMesh(sceneId, id, channels);
        },
        removeMesh(sceneId, id) { graphics.removeMesh(sceneId, id); },
        createStage(sceneId, id, meshIds) { graphics.createStage(sceneId, id, meshIds); },
        removeStage(sceneId, id) { graphics.removeStage(sceneId, id); },
        setSkyLayers(sceneId, currentIds, previousIds, previousOpacity, height, brightness) {
            graphics.setSkyLayers(sceneId, currentIds, previousIds, previousOpacity, height, brightness);
        },
        setSkyParameters(sceneId, previousOpacity, height, brightness) {
            graphics.setSkyParameters(sceneId, previousOpacity, height, brightness);
        },
        createActor(sceneId, id, meshIds, boneCount) { graphics.createActor(sceneId, id, meshIds, boneCount); },
        removeActor(sceneId, id) { graphics.removeActor(sceneId, id); },
        flushScenePacked,
        drawFramePacked(id, bytes, actorMeta, actorStates, actorBones, meshMeta, meshBones) {
            graphics.flush();
            const state = scene(id);
            flushScenePacked(id, actorMeta, actorStates, actorBones, meshMeta, meshBones);
            unpackFrame(state.frame, state.frameBytes.read(bytes));
            graphics.drawScene(id, state.frame);
        },
        drawPortrait(id, actorIds, bytes) {
            graphics.flush();
            const state = scene(id);
            unpackPortrait(state.portrait, state.portraitBytes.read(bytes));
            graphics.drawPortrait(id, actorIds, state.portrait);
        },
        composeScene(id) { graphics.composeScene(id); },
        disposeScene(id) {
            const state = scenes.get(id);
            graphics.disposeScene(id);
            if (state) for (const textureId of state.textures) graphics.destroyTexture(textureId);
            scenes.delete(id);
        }
    };
}
