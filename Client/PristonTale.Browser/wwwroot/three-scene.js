import * as THREE from './vendor/three/three.webgpu.js';
import { createNativeBatches } from './three-native.js';
import { createRainBatches } from './three-rain.js';
import { ExternalCamera } from './three-camera.js';
import { createThreePost } from './three-post.js';
import { createNativeDisplayEncoder } from './three-display.js';
import { buildStaticStageBatches } from './three-batching.js';
import { buildTextureArrayStageBatches, createStaticArrayObserverPolicy, textureArrayLimits } from './three-texture-arrays.js';
import { createStageBundleCache } from './three-bundles.js';
import { createThreeProfiler, ThreePhase } from './three-profile.js';
import { createShadowRefresh } from './three-shadow-refresh.js';
import { createStageBounds } from './three-stage-bounds.js';
import { createZeroPointLighting, zeroPointLightMode } from './three-zero-lighting.js';
import { validateAdaptiveBudget, internalPostMode } from './adaptive-resolution.js';
import { attribute as vertexAttribute, materialColor, positionLocal, positionView, normalLocal, normalView, renderGroup, uniform, texture, uv, vec2, vec3, vec4, fog, mrt, output, If } from './vendor/three/three.tsl.js';

const identity = new THREE.Matrix4();
const toWebGlDepth = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2, -1, 0, 0, 0, 1);
const fromReversedDepth = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 1, 0, 0, 0, 1);
const defaultAmbient = [0.55, 0.55, 0.55];
const defaultGround = [0.3, 0.3, 0.3];
const defaultSun = [0.7, 0.7, 0.7];
const defaultDirection = [0.3, -1, 0.2];
const zero = [0, 0, 0];
const sceneAnisotropy = { VeryLow: 1, Low: 2, Medium: 4, High: 8, Ultra: 16 };

function textureOffset(form, milliseconds) {
    const tick = milliseconds >>> 6;
    if (form === 4) return (tick & 255) / 256;
    if (form >= 6 && form <= 14) return (tick & 255) / 256 * (form - 4);
    if (form >= 15 && form <= 18) {
        const mask = 65535 >>> (22 - form);
        return (tick & mask) / mask;
    }
    return 0;
}

function attribute(geometry, name, values, size, ArrayType = Float32Array) {
    if (values?.length) geometry.setAttribute(name, new THREE.BufferAttribute(new ArrayType(values), size));
}

function projection(camera, values, renderer) {
    camera.projectionMatrix.fromArray(values);
    // The shared scene camera uses reversed [0,1] depth; Three materials use conventional depth.
    camera.projectionMatrix.premultiply(fromReversedDepth);
    // WebGL's clip volume uses [-1,1].
    if (renderer.coordinateSystem === THREE.WebGLCoordinateSystem) camera.projectionMatrix.premultiply(toWebGlDepth);
    camera.updateProjectionMatrix();
}

function palette(skeleton, rows) {
    if (!rows || rows.length !== skeleton.bones.length * 12) throw new Error('Invalid scene bone palette.');
    for (let index = 0; index < skeleton.bones.length; index++) {
        const at = index * 12;
        skeleton.bones[index].matrixWorld.set(
            rows[at], rows[at + 1], rows[at + 2], rows[at + 3],
            rows[at + 4], rows[at + 5], rows[at + 6], rows[at + 7],
            rows[at + 8], rows[at + 9], rows[at + 10], rows[at + 11],
            0, 0, 0, 1);
    }
    skeleton.update();
}

function skeletonFor(count) {
    const bones = Array.from({ length: count }, () => {
        const bone = new THREE.Bone();
        bone.matrixAutoUpdate = false;
        bone.matrixWorldAutoUpdate = false;
        return bone;
    });
    return new THREE.Skeleton(bones, bones.map(() => new THREE.Matrix4()));
}

function memoryView(slot, name, Type, buffer, pointer, length) {
    let saved = slot[name];
    if (!saved || saved.buffer !== buffer || saved.pointer !== pointer || saved.length !== length) {
        saved = slot[name] = { buffer, pointer, length, view: new Type(buffer, pointer, length) };
    }
    return saved.view;
}

function configureBlend(material, blend, premultiplied = false) {
    material.transparent = blend !== 'opaque';
    material.blending = material.transparent ? THREE.CustomBlending : THREE.NoBlending;
    material.blendEquation = THREE.AddEquation;
    material.blendSrc = premultiplied ? THREE.OneFactor : THREE.SrcAlphaFactor;
    material.blendDst = blend === 'additive' ? THREE.OneFactor : THREE.OneMinusSrcAlphaFactor;
    material.blendEquationAlpha = THREE.AddEquation;
    material.blendSrcAlpha = THREE.OneFactor;
    material.blendDstAlpha = THREE.OneMinusSrcAlphaFactor;
}

function materialBlend(material, source, faded = false) {
    configureBlend(material, faded ? 'alpha' : source.blend ?? 'opaque', source.premultiplied ?? false);
    if (faded) return;
    if (source.blendType >= 2 && source.blendType <= 6) {
        material.transparent = true;
        material.blending = THREE.CustomBlending;
        material.blendSrc = source.blendType === 3 || source.blendType === 6 ? THREE.ZeroFactor
            : source.blendType === 4 ? THREE.SrcAlphaFactor : THREE.SrcColorFactor;
        material.blendDst = source.blendType === 2 || source.blendType === 6 ? THREE.OneMinusSrcColorFactor
            : source.blendType === 3 ? THREE.SrcColorFactor : THREE.OneFactor;
    }
}

export async function createThreeGraphics(canvas, options = {}) {
    const pointLightMode = zeroPointLightMode(options.zeroPointLights);
    const postMode = internalPostMode(options.internalPost);
    const fusedMode = internalPostMode(options.fusedSharpen);
    const renderer = options.renderer ?? new THREE.WebGPURenderer({ canvas, alpha: false, antialias: false,
        forceWebGL: options.forceWebGL ?? false, trackTimestamp: options.profile === true && options.gpuTiming === true });
    if (!options.renderer) await renderer.init();
    renderer.autoClear = false;
    renderer.sortObjects = true;
    renderer.outputColorSpace = THREE.LinearSRGBColorSpace;
    renderer.setPixelRatio(1);
    const graphics = new ThreeGraphics(renderer, canvas);
    graphics.profiler = createThreeProfiler(renderer, options.profile === true, undefined, options.gpuTiming === true);
    graphics.internalPostRequested = postMode === true || (postMode === 'auto' && renderer.backend?.isWebGPUBackend === true);
    graphics.fusedSharpenRequested = fusedMode === true || (fusedMode === 'auto' && renderer.backend?.isWebGPUBackend === true);
    graphics.post = options.post ?? (options.renderer ? null : createThreePost(renderer, {
        internalPost: graphics.internalPostRequested, fusedSharpen: graphics.fusedSharpenRequested }));
    graphics.display = options.display ?? (options.renderer ? null : createNativeDisplayEncoder(renderer));
    graphics.capabilities = graphics.post?.capabilities ?? { ambientOcclusion: false, screenSpaceReflections: false,
        bloom: false, antiAliasing: ['Off'], unsupported: {} };
    canvas.dataset.renderer = renderer.backend?.isWebGPUBackend ? 'Three.js WebGPU' : 'Three.js WebGL2';
    graphics.errors = new Set();
    const context = renderer.getContext?.();
    graphics.textureArraysRequested = options.textureArrays === true;
    graphics.textureArrayLayers = Math.min(textureArrayLimits.maxLayers, renderer.backend?.device?.limits?.maxTextureArrayLayers
        ?? (context?.getParameter && context.MAX_ARRAY_TEXTURE_LAYERS !== undefined
            ? context.getParameter(context.MAX_ARRAY_TEXTURE_LAYERS) : 0));
    graphics.textureArraysEnabled = graphics.textureArraysRequested && graphics.textureArrayLayers >= 2;
    graphics.cutoutArraysRequested = options.profile === true && options.cutoutArrays === true;
    graphics.stageSkinCullingRequested = options.stageSkinCulling === true;
    if (pointLightMode === true || (pointLightMode === 'auto' && renderer.backend?.isWebGPUBackend === true)) {
        graphics.zeroPointLights = createZeroPointLighting(THREE, { uniform, renderGroup, vec3, If });
        renderer.lighting?.dispose();
        renderer.lighting = graphics.zeroPointLights;
    }
    graphics.bundleCache = options.profile === true && options.renderBundles === true ? createStageBundleCache(THREE, renderer, true) : null;
    graphics.staticArrayObservers = options.profile === true && options.staticArrayObservers === true ? createStaticArrayObserverPolicy(THREE) : null;
    graphics.adapterInfo = renderer.backend?.device?.adapterInfo ?? {
        vendor: context?.getParameter ? context.getParameter(context.VENDOR) : '',
        description: context?.getParameter ? context.getParameter(context.RENDERER) : canvas.dataset.renderer
    };
    const error = value => {
        const message = value.message ?? String(value);
        if (graphics.errors.size < 40) graphics.errors.add(message);
        canvas.dataset.rendererErrors = JSON.stringify([...graphics.errors]);
        canvas.dataset.webgpuErrors = canvas.dataset.rendererErrors;
        console.error('Three.js:', message);
        options.onError?.(value);
    };
    renderer.onError = error;
    renderer.onDeviceLost = value => {
        canvas.dataset.rendererState = 'lost';
        error(value);
        void graphics.dispose().catch(error);
    };
    return graphics;
}

class ThreeGraphics {
    constructor(renderer, canvas) {
        this.renderer = renderer;
        this.canvas = canvas;
        this.textures = new Map();
        this.scenes = new Map();
        this.commands = [];
        this.commandCount = 0;
        this.targetId = 0;
        this.memorySlots = [];
        this.memoryNext = 0;
        this.width = 1;
        this.height = 1;
        this.native = createNativeBatches(THREE, renderer, id => this.texture(id), toWebGlDepth);
        this.rain = createRainBatches(THREE, renderer, id => this.texture(id), this.native, toWebGlDepth);
        this.stats = { frames: 0, sceneDraws: 0, nativeDraws: 0 };
        this.post = null;
        this.profiler = null;
        this.textureArrayBytes = 0;
        this.textureArrayDirtyScenes = new Set();
        this.bundleCache = null;
        this.dynamicShadowHz = 0;
        this.zeroPointLights = null;
    }

    beginFrame(width, height) {
        this.flush();
        width = Math.max(1, Math.floor(width));
        height = Math.max(1, Math.floor(height));
        const pixelRatio = this.renderer.getPixelRatio?.() ?? 1;
        const resizedExternally = typeof this.canvas.width === 'number' && typeof this.canvas.height === 'number'
            && (this.canvas.width !== Math.floor(width * pixelRatio) || this.canvas.height !== Math.floor(height * pixelRatio));
        if (width !== this.width || height !== this.height || resizedExternally) {
            this.bundleCache?.invalidate();
            for (const resource of this.scenes.values()) resource.shadowRefresh.invalidate();
            this.width = width;
            this.height = height;
            this.renderer.setSize(width, height, false);
        }
        this.targetId = 0;
        this.memoryNext = 0;
        this.native.beginFrame();
        this.rain.beginFrame();
        this.post?.beginFrame(width, height);
        this.profiler?.beginFrame();
    }

    command(kind) {
        const index = this.commandCount++;
        const command = this.commands[index] ??= { camera: new ExternalCamera(this.renderer.coordinateSystem), color: new THREE.Color() };
        command.kind = kind;
        command.targetId = this.targetId;
        return command;
    }

    clear(r, g, b, a = 1, clearDepth = true, clearColor = true) {
        const command = this.command('clear');
        command.color.setRGB(r, g, b);
        command.alpha = a;
        command.clearDepth = clearDepth;
        command.clearColor = clearColor;
    }

    setRenderTarget(id = 0) {
        if (id && !this.textures.has(id)) throw new Error(`Unknown render target ${id}.`);
        this.targetId = id;
    }

    createTexture(id, width, height, rgba = null, renderTarget = false, encoding = id < 0 ? 1 : 0, format = 0) {
        this.flush();
        if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0) throw new Error('Invalid texture size.');
        this.destroyTexture(id);
        if (encoding !== 0 && encoding !== 1) throw new Error('Unsupported native texture encoding.');
        if (format === 13 && encoding !== 0) throw new Error('Single textures require raw linear encoding.');
        this.textures.set(id, { width, height, texture: null, target: null, renderTarget, encoding, format });
        if (rgba) this.updateTexture(id, 0, 0, width, height, rgba);
    }

    texture(id) {
        if (!id) return null;
        const resource = this.textures.get(id);
        if (!resource) throw new Error(`Unknown scene texture ${id}.`);
        if (resource.renderTarget) return this.target(id).texture;
        if (!resource.texture) {
            const single = resource.format === 13;
            const pixels = single ? new Float32Array(resource.width * resource.height) : new Uint8Array(resource.width * resource.height * 4);
            const texture = new THREE.DataTexture(pixels, resource.width, resource.height,
                single ? THREE.RedFormat : THREE.RGBAFormat, single ? THREE.FloatType : THREE.UnsignedByteType);
            resource.uploadBytes = single ? new Uint8Array(pixels.buffer) : pixels;
            texture.colorSpace = resource.encoding ? THREE.SRGBColorSpace : THREE.NoColorSpace;
            texture.magFilter = single ? THREE.NearestFilter : THREE.LinearFilter;
            texture.minFilter = single ? THREE.NearestFilter : THREE.LinearFilter;
            texture.generateMipmaps = false;
            texture.needsUpdate = true;
            resource.texture = texture;
        }
        return resource.texture;
    }

    updateTexture(id, x, y, width, height, rgba) {
        this.flush();
        const resource = this.textures.get(id);
        if (!resource || resource.renderTarget || resource.target) throw new Error(`Texture ${id} cannot receive a pixel upload.`);
        if (x < 0 || y < 0 || x + width > resource.width || y + height > resource.height || rgba.length !== width * height * 4) {
            throw new Error(`Invalid texture upload ${id}.`);
        }
        this.invalidateTextureArrays(id);
        if (id < 0) this.bundleCache?.invalidate();
        const texture = this.texture(id);
        const destination = resource.uploadBytes;
        if (x === 0 && width === resource.width) {
            destination.set(rgba, y * resource.width * 4);
        } else {
            for (let row = 0; row < height; row++) {
                const at = ((y + row) * resource.width + x) * 4;
                for (let column = 0; column < width * 4; column++) destination[at + column] = rgba[row * width * 4 + column];
            }
        }
        texture.needsUpdate = true;
    }

    destroyTexture(id) {
        this.flush();
        const resource = this.textures.get(id);
        if (!resource) return;
        this.invalidateTextureArrays(id);
        if (id < 0) this.bundleCache?.invalidate();
        if (resource.target) resource.target.dispose();
        else resource.texture?.dispose();
        this.textures.delete(id);
    }

    target(id) {
        if (!id) return null;
        const resource = this.textures.get(id);
        if (!resource) throw new Error(`Unknown render target ${id}.`);
        if (!resource.target) {
            resource.texture?.dispose();
            resource.target = new THREE.RenderTarget(resource.width, resource.height, {
                type: resource.format === 13 ? THREE.FloatType : THREE.UnsignedByteType,
                format: resource.format === 13 ? THREE.RedFormat : THREE.RGBAFormat, depthBuffer: true, stencilBuffer: false
            });
            resource.target.texture.colorSpace = resource.encoding ? THREE.SRGBColorSpace : THREE.NoColorSpace;
            resource.texture = resource.target.texture;
            resource.renderTarget = true;
        }
        return resource.target;
    }

    drawTriangles(positions, uvs, colors, indices, textureId, matrix, blend = 'alpha', depthTest = false,
        depthWrite = false, scissor = null, targetTextureId = this.targetId, viewport = null, colorsAreSrgb = false,
        rawBlend = null, blendFactor = null, depthCompare = 3, sampler = null, shaderKind = 1, pixelParams = null) {
        const command = this.command('native');
        command.targetId = targetTextureId;
        command.batch = this.native.prepare(positions, uvs, colors, indices, textureId, matrix, blend, depthTest, depthWrite,
            scissor, viewport, colorsAreSrgb, rawBlend, blendFactor, depthCompare, sampler, shaderKind, pixelParams);
    }

    drawNativeMemory(buffer, positions, uvs, colors, vertexCount, indices, indexCount, textureId, matrix, blend,
        depthTest, depthWrite, scissor, viewport, targetId, colorsAreSrgb, rawBlend, blendFactor, depthCompare = 3, sampler = 0,
        shaderKind = 1, pixelParams = 0) {
        const slot = this.memorySlots[this.memoryNext] ??= {};
        this.memoryNext++;
        this.drawTriangles(memoryView(slot, 'positions', Float32Array, buffer, positions, vertexCount * 3),
            memoryView(slot, 'uvs', Float32Array, buffer, uvs, vertexCount * 2),
            memoryView(slot, 'colors', Float32Array, buffer, colors, vertexCount * 4),
            memoryView(slot, 'indices', Uint32Array, buffer, indices, indexCount), textureId,
            memoryView(slot, 'matrix', Float32Array, buffer, matrix, 16),
            blend === 2 ? 'additive' : blend === 1 ? 'alpha' : 'opaque', depthTest, depthWrite,
            scissor ? memoryView(slot, 'scissor', Int32Array, buffer, scissor, 4) : null, targetId,
            memoryView(slot, 'viewport', Int32Array, buffer, viewport, 4), colorsAreSrgb,
            rawBlend ? memoryView(slot, 'blend', Int32Array, buffer, rawBlend, 6) : null,
            blendFactor ? memoryView(slot, 'factor', Float32Array, buffer, blendFactor, 4) : null, depthCompare,
            sampler ? memoryView(slot, 'sampler', Int32Array, buffer, sampler, 4) : null, shaderKind,
            pixelParams ? memoryView(slot, 'pixel', Float32Array, buffer, pixelParams, 8) : null);
    }

    drawRain(positions, shapes, indices, textureId, matrix, vertexParams, pixelParams, kind,
        depthTest = true, depthWrite = false, scissor = null, viewport = null, targetId = this.targetId,
        rawBlend = null, blendFactor = null, depthCompare = 5, range = null) {
        const command = this.command('rain');
        command.targetId = targetId;
        command.batch = this.rain.prepare(positions, shapes, indices, textureId, matrix, vertexParams, pixelParams, kind,
            depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare, range);
    }

    createRainGeometryMemory(id, buffer, positions, shapes, vertexCount, indices, indexCount) {
        this.flush();
        this.rain.createGeometry(id, new Float32Array(buffer, positions, vertexCount * 4),
            new Float32Array(buffer, shapes, vertexCount * 4), new Uint32Array(buffer, indices, indexCount));
    }

    destroyRainGeometry(id) {
        this.flush();
        this.rain.destroyGeometry(id);
    }

    drawRainGeometryMemory(id, start, count, buffer, textureId, matrix, vertexParams, pixelParams,
        depthTest, depthWrite, scissor, viewport, targetId, rawBlend, blendFactor, depthCompare) {
        const slot = this.memorySlots[this.memoryNext] ??= {};
        this.memoryNext++;
        const batch = this.rain.prepareGeometry(id, start, count, textureId,
            memoryView(slot, 'rainMatrix', Float32Array, buffer, matrix, 16),
            memoryView(slot, 'rainVertex', Float32Array, buffer, vertexParams, 24),
            memoryView(slot, 'rainPixel', Float32Array, buffer, pixelParams, 12), depthTest, depthWrite,
            scissor ? memoryView(slot, 'rainScissor', Int32Array, buffer, scissor, 4) : null,
            viewport ? memoryView(slot, 'rainViewport', Int32Array, buffer, viewport, 4) : null,
            rawBlend ? memoryView(slot, 'rainBlend', Int32Array, buffer, rawBlend, 6) : null,
            blendFactor ? memoryView(slot, 'rainFactor', Float32Array, buffer, blendFactor, 4) : null, depthCompare);
        const command = this.command('rain');
        command.targetId = targetId;
        command.batch = batch;
    }

    drawRainMemory(buffer, positions, shapes, vertexCount, indices, indexCount, textureId, matrix, vertexParams, pixelParams,
        kind, depthTest, depthWrite, scissor, viewport, targetId, rawBlend, blendFactor, depthCompare) {
        const slot = this.memorySlots[this.memoryNext] ??= {};
        this.memoryNext++;
        const range = slot.rainRange ??= {};
        if (range.buffer !== buffer) {
            range.buffer = buffer;
            range.floats = new Float32Array(buffer);
            range.indices = new Uint32Array(buffer);
        }
        range.positionOffset = positions / 4;
        range.shapeOffset = shapes / 4;
        range.indexOffset = indices / 4;
        range.vertexCount = vertexCount;
        range.indexCount = indexCount;
        this.drawRain(range.floats, range.floats, range.indices, textureId,
            memoryView(slot, 'rainMatrix', Float32Array, buffer, matrix, 16),
            memoryView(slot, 'rainVertex', Float32Array, buffer, vertexParams, 24),
            memoryView(slot, 'rainPixel', Float32Array, buffer, pixelParams, 12), kind, depthTest, depthWrite,
            scissor ? memoryView(slot, 'rainScissor', Int32Array, buffer, scissor, 4) : null,
            viewport ? memoryView(slot, 'rainViewport', Int32Array, buffer, viewport, 4) : null, targetId,
            rawBlend ? memoryView(slot, 'rainBlend', Int32Array, buffer, rawBlend, 6) : null,
            blendFactor ? memoryView(slot, 'rainFactor', Float32Array, buffer, blendFactor, 4) : null, depthCompare, range);
    }

    createScene(id) {
        this.disposeScene(id);
        const scene = new THREE.Scene();
        const ambient = new THREE.HemisphereLight(0xffffff, 0xffffff, 1);
        const sun = new THREE.DirectionalLight(0xffffff, 1);
        const skyScene = new THREE.Scene();
        const sky = [new THREE.Group(), new THREE.Group()];
        sky[0].renderOrder = -10002;
        sky[1].renderOrder = -10001;
        sky[0].visible = false;
        sky[1].visible = false;
        const fogState = { start: uniform(0).setGroup(renderGroup), density: uniform(0).setGroup(renderGroup),
            opacity: uniform(0).setGroup(renderGroup), color: uniform(new THREE.Color()).setGroup(renderGroup) };
        scene.fogNode = fog(fogState.color, positionView.z.negate().sub(fogState.start).max(0)
            .mul(fogState.density).negate().exp().oneMinus().mul(fogState.opacity));
        scene.add(ambient, sun, sun.target);
        skyScene.add(sky[0], sky[1]);
        this.scenes.set(id, { scene, ambient, sun, skyScene, sky, skyObjects: [new Map(), new Map()], skyHeight: 0, skyBrightness: 1,
            skyMrt: mrt({ output, normalRoughness: vec4(0, 0, 0, 1) }), activeSkyMrt: null,
            lastWorldCamera: this.profiler ? new ExternalCamera(this.renderer.coordinateSystem) : null, hasWorldCamera: false,
            fogState, meshes: new Map(), stages: new Map(), actors: new Map(), points: [], textureIds: new Set(), settings: {}, shadowRefresh: createShadowRefresh() });
    }

    scene(id) {
        const scene = this.scenes.get(id);
        if (!scene) throw new Error(`Unknown scene ${id}.`);
        return scene;
    }

    getCapabilities() {
        return { backend: this.canvas.dataset.renderer, ...this.capabilities, effective: this.post?.effective ?? null,
            sunShadows: true, cascadedShadows: false, lampShadows: false, mipmappedSceneTextures: true,
              gpuTimestamps: false, gltfActors: false, dynamicResolution: true, shadowUpdateRate: true,
              internalPost: this.internalPostRequested,
              fusedSharpen: this.fusedSharpenRequested,
              unsupported: { ...this.capabilities.unsupported,
                  proceduralSky: 'This renderer uses the authored animated sky layers.' } };
    }

    getProfile() {
        const profile = this.profiler?.snapshot();
        if (!profile) return null;
        profile.rainGeometry = this.rain.getStats();
        profile.internalPost = this.post?.getInternalPostProfile?.() ?? null;
        profile.fusedSharpen = this.post?.getFusedSharpenProfile?.() ?? null;
        profile.zeroPointLights = this.zeroPointLights?.snapshot() ?? null;
        const lighting = { activePointLights: 0, maxScenePointLights: 0, allocatedPointLights: 0, lastWorldPointLights: 0 };
        for (const resource of this.scenes.values()) {
            let active = 0;
            for (const light of resource.points) if (light.visible) active++;
            lighting.activePointLights += active;
            lighting.maxScenePointLights = Math.max(lighting.maxScenePointLights, active);
            lighting.allocatedPointLights += resource.points.length;
            lighting.lastWorldPointLights += resource.lastWorldPointLightCount ?? 0;
        }
        profile.lighting = lighting;
        const arrays = { requested: this.textureArraysRequested, enabled: this.textureArraysEnabled,
            cutoutsRequested: this.cutoutArraysRequested, cutoutsEnabled: this.cutoutArraysRequested && this.textureArraysEnabled,
            maxLayers: this.textureArrayLayers, budgetBytes: textureArrayLimits.totalBytes, bytes: this.textureArrayBytes,
            originalObjects: 0, baselineObjects: 0, eligibleObjects: 0, groups: 0, outputObjects: 0, pages: 0, layers: 0 };
        for (const resource of this.scenes.values()) for (const group of resource.stages.values()) {
            arrays.originalObjects += group.userData.meshIds.length;
            arrays.baselineObjects += group.userData.baselineObjectCount;
            arrays.outputObjects += group.userData.renderObjects?.length ?? group.children.length;
            const stats = group.userData.textureArrayBatches?.stats;
            if (!stats) continue;
            arrays.eligibleObjects += stats.eligibleObjects;
            arrays.groups += stats.groups;
            arrays.pages += stats.pages;
            arrays.layers += stats.layers;
        }
        profile.textureArrays = arrays;
        const stageMaterials = { objects: 0, opaque: 0, transparent: 0, doubleSideTransparent: 0,
            doubleSideTransparentTwoPass: 0, skinned: 0, alphaCutout: 0, immutableSingleTextureCutout: 0,
            immutableSingleTextureCutoutTransparent: 0, immutableSingleTextureCutoutDoubleSide: 0,
            cutoutTextures: 0, cutoutTexturesWithAlpha: 0, cutoutSamplerGroups: 0 };
        const objects = new Set(), cutoutTextures = new Set(), samplerGroups = new Set();
        for (const resource of this.scenes.values()) for (const group of resource.stages.values()) {
            for (const object of group.userData.renderObjects ?? group.children) {
                if (!object.isMesh || objects.has(object)
                    || resource.meshes.get(object.userData.sourceMeshId)?.descriptor.isSky) continue;
                objects.add(object);
                stageMaterials.objects++;
                const materials = Array.isArray(object.material) ? object.material : [object.material];
                const transparent = materials.some(material => material.transparent);
                stageMaterials[transparent ? 'transparent' : 'opaque']++;
                if (materials.some(material => material.transparent && material.side === THREE.DoubleSide)) stageMaterials.doubleSideTransparent++;
                if (materials.some(material => material.transparent && material.side === THREE.DoubleSide && !material.forceSinglePass)) {
                    stageMaterials.doubleSideTransparentTwoPass++;
                }
                if (object.isSkinnedMesh) stageMaterials.skinned++;
                if (materials.some(material => material.alphaTest > 0)) stageMaterials.alphaCutout++;
                if (materials.length !== 1 || object.isSkinnedMesh || object.geometry.getAttribute('skinIndex')) continue;
                const material = materials[0], source = material.userData.source;
                const id = source?.textureId, entry = this.textures.get(id), map = entry?.texture;
                const data = map?.image?.data;
                if (!(material.alphaTest > 0) || material.opacity !== 1 || !material.depthWrite || !source
                    || source.texture2Id || source.textureIds?.length > 1 || source.windX || source.windZ || source.uvForm || source.uvForm2 || source.blink
                    || (source.textureIds?.length === 1 && source.textureIds[0] !== id)
                    || !(id < 0) || !entry?.sceneImmutable || entry.mutable || entry.renderTarget || entry.target || entry.format !== 0
                    || material.map !== map || !map?.isDataTexture || map.isDataArrayTexture || map.format !== THREE.RGBAFormat
                    || map.type !== THREE.UnsignedByteType || !(data instanceof Uint8Array) || data.length !== entry.width * entry.height * 4) continue;
                stageMaterials.immutableSingleTextureCutout++;
                if (material.transparent) stageMaterials.immutableSingleTextureCutoutTransparent++;
                if (material.side === THREE.DoubleSide) stageMaterials.immutableSingleTextureCutoutDoubleSide++;
                if (cutoutTextures.has(id)) continue;
                let alpha = false;
                for (let at = 3; at < data.length; at += 4) if (data[at] !== 255) { alpha = true; break; }
                cutoutTextures.add(id);
                if (alpha) stageMaterials.cutoutTexturesWithAlpha++;
                samplerGroups.add(JSON.stringify([entry.width, entry.height, entry.encoding, map.colorSpace, map.wrapS, map.wrapT,
                    map.minFilter, map.magFilter, map.anisotropy, map.generateMipmaps, map.flipY, map.premultiplyAlpha, map.channel,
                    map.unpackAlignment, map.compareFunction, map.internalFormat, map.mapping,
                    map.offset.toArray(), map.repeat.toArray(), map.center.toArray(), map.rotation, map.matrixAutoUpdate, map.matrix.elements]));
            }
        }
        stageMaterials.cutoutTextures = cutoutTextures.size;
        stageMaterials.cutoutSamplerGroups = samplerGroups.size;
        profile.stageMaterials = stageMaterials;
        const stageCulling = { requested: this.stageSkinCullingRequested, eligibleSkinned: 0, validBounds: 0,
            insideMain: 0, outsideMain: 0, insideShadow: 0, outsideShadow: 0, invalidFallback: 0 };
        const counted = new Set();
        for (const resource of this.scenes.values()) {
            const camera = resource.hasWorldCamera ? resource.lastWorldCamera : null, shadowCamera = resource.sun.shadow.camera;
            const main = camera ? new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(
                camera.projectionMatrix, camera.matrixWorldInverse), camera.coordinateSystem) : null;
            const shadow = camera && resource.sun.castShadow ? new THREE.Frustum().setFromProjectionMatrix(
                new THREE.Matrix4().multiplyMatrices(shadowCamera.projectionMatrix, shadowCamera.matrixWorldInverse), shadowCamera.coordinateSystem) : null;
            for (const group of resource.stages.values()) for (const object of group.userData.renderObjects ?? group.children) {
                const mesh = resource.meshes.get(object.userData.sourceMeshId);
                if (!object.isSkinnedMesh || !mesh || mesh.descriptor.isSky || counted.has(object)) continue;
                counted.add(object);
                stageCulling.eligibleSkinned++;
                if (!mesh.stageBounds?.valid || !object.frustumCulled) { stageCulling.invalidFallback++; continue; }
                stageCulling.validBounds++;
                if (main) stageCulling[object.intersectsFrustum(main) ? 'insideMain' : 'outsideMain']++;
                if (shadow && object.castShadow) stageCulling[object.intersectsFrustum(shadow) ? 'insideShadow' : 'outsideShadow']++;
            }
        }
        profile.stageCulling = stageCulling;
        profile.staticArrayObservers = this.staticArrayObservers?.snapshot() ?? null;
        profile.shadowRefresh = Array.from(this.scenes.values(), resource => resource.shadowRefresh.snapshot());
        if (this.bundleCache) {
            profile.renderBundles = this.bundleCache.snapshot();
            if (this.bundleCache.enabled) {
                profile.renders.encodedDrawCalls = profile.renders.drawCalls;
                profile.renders.encodedTriangles = profile.renders.triangles;
                delete profile.renders.drawCalls;
                delete profile.renders.triangles;
            }
        }
        return profile;
    }

    setInternalPostEnabled(enabled) {
        return this.post?.setInternalPostEnabled(enabled) ?? false;
    }

    setGpuTimingMode(mode) {
        return this.profiler?.setGpuTimingMode(mode) ?? 'off';
    }

    setZeroPointLightsEnabled(enabled) {
        this.flush();
        return this.zeroPointLights?.setEnabled(enabled) ?? false;
    }

    setDynamicResolutionScale(scale, shadowHz = 0) {
        validateAdaptiveBudget(scale, shadowHz);
        this.flush();
        this.post?.setDynamicResolutionScale(scale);
        if (this.dynamicShadowHz !== shadowHz) for (const resource of this.scenes.values()) resource.shadowRefresh.invalidate();
        this.dynamicShadowHz = shadowHz;
    }

    setRenderBundlesEnabled(enabled) {
        this.flush();
        return this.bundleCache?.setEnabled(enabled) ?? false;
    }

    setStaticArrayObserversEnabled(enabled) {
        this.flush();
        return this.staticArrayObservers?.setEnabled(enabled) ?? false;
    }

    setTextureArraysEnabled(enabled) {
        if (!this.textureArraysRequested || this.textureArrayLayers < 2) return this.textureArraysEnabled;
        enabled = !!enabled;
        if (enabled === this.textureArraysEnabled) return enabled;
        this.flush();
        const stages = [];
        for (const [sceneId, resource] of this.scenes) for (const [stageId, group] of resource.stages) {
            stages.push({ sceneId, stageId, meshIds: group.userData.meshIds, position: group.position.clone(),
                quaternion: group.quaternion.clone(), scale: group.scale.clone(), matrix: group.matrix.clone(),
                matrixWorld: group.matrixWorld.clone(), matrixAutoUpdate: group.matrixAutoUpdate,
                matrixWorldAutoUpdate: group.matrixWorldAutoUpdate, matrixWorldNeedsUpdate: group.matrixWorldNeedsUpdate,
                visible: group.visible, renderOrder: group.renderOrder, layers: group.layers.mask,
                dirty: group.userData.textureArrayDirty });
        }
        this.textureArraysEnabled = enabled;
        this.textureArrayDirtyScenes.clear();
        for (const state of stages) {
            this.createStage(state.sceneId, state.stageId, state.meshIds);
            const group = this.scene(state.sceneId).stages.get(state.stageId);
            group.position.copy(state.position);
            group.quaternion.copy(state.quaternion);
            group.scale.copy(state.scale);
            group.matrix.copy(state.matrix);
            group.matrixWorld.copy(state.matrixWorld);
            group.matrixAutoUpdate = state.matrixAutoUpdate;
            group.matrixWorldAutoUpdate = state.matrixWorldAutoUpdate;
            group.matrixWorldNeedsUpdate = state.matrixWorldNeedsUpdate;
            group.visible = state.visible;
            group.renderOrder = state.renderOrder;
            group.layers.mask = state.layers;
            if (state.dirty) {
                group.userData.textureArrayDirty = true;
                if (enabled) this.textureArrayDirtyScenes.add(state.sceneId);
            }
        }
        return enabled;
    }

    composePost() {
        if (!this.post?.active) return;
        const started = this.profiler?.start();
        this.post.compose();
        this.profiler?.endPhase(ThreePhase.post, started);
    }

    configureScene(id, settings) {
        const resource = this.scene(id);
        resource.settings = settings;
        resource.shadowRefresh.invalidate();
        for (const textureId of resource.textureIds) this.configureSceneTexture(id, textureId);
        for (const group of resource.stages.values()) {
            this.bundleCache?.invalidate(group);
        }
        for (const group of resource.stages.values()) for (const object of group.userData.renderObjects ?? group.children) {
            const map = object.material.userData.arrayTexture;
            if (!map) continue;
            const anisotropy = Math.min(sceneAnisotropy[settings.quality] ?? 4, this.renderer.getMaxAnisotropy?.() ?? 1);
            if (map.anisotropy !== anisotropy) {
                map.anisotropy = anisotropy;
                map.needsUpdate = true;
            }
        }
        const size = settings.shadowMapSize ?? 0;
        resource.sun.castShadow = size > 0;
        if (size > 0 && resource.sun.shadow.mapSize.width !== size) {
            resource.sun.shadow.map?.dispose();
            resource.sun.shadow.map = null;
            resource.sun.shadow.mapSize.set(size, size);
        }
        const camera = resource.sun.shadow.camera;
        camera.left = -1800;
        camera.right = 1800;
        camera.top = 1800;
        camera.bottom = -1800;
        camera.near = 1;
        camera.far = 7000;
        camera.updateProjectionMatrix();
        resource.sun.shadow.normalBias = 0.6;
        resource.sun.shadow.bias = -0.0001;
    }

    configureSceneTexture(sceneId, textureId) {
        const resource = this.scene(sceneId);
        const entry = this.textures.get(textureId);
        if (textureId < 0 && entry && !entry.renderTarget && entry.encoding === 1 && entry.format === 0) entry.sceneImmutable = true;
        const map = this.texture(textureId);
        map.wrapS = THREE.RepeatWrapping;
        map.wrapT = THREE.RepeatWrapping;
        map.generateMipmaps = true;
        map.minFilter = THREE.LinearMipmapLinearFilter;
        map.magFilter = THREE.LinearFilter;
        map.anisotropy = Math.min(sceneAnisotropy[resource.settings.quality] ?? 4, this.renderer.getMaxAnisotropy?.() ?? 1);
        map.needsUpdate = true;
        resource.textureIds.add(textureId);
        if (textureId < 0) this.bundleCache?.invalidate();
    }

    createMesh(sceneId, id, descriptor) {
        this.flush();
        const resource = this.scene(sceneId);
        resource.shadowRefresh.invalidate();
        const stages = [];
        if (resource.meshes.has(id)) {
            for (const [stageId, group] of resource.stages) {
                if (group.userData.meshIds.includes(id)) stages.push([stageId, group.userData.meshIds]);
            }
            for (const [stageId] of stages) this.removeStage(sceneId, stageId);
        }
        this.removeMesh(sceneId, id);
        const geometry = new THREE.BufferGeometry();
        attribute(geometry, 'position', descriptor.positions, 3);
        attribute(geometry, 'normal', descriptor.normals, 3);
        attribute(geometry, 'color', descriptor.colors, 4);
        const color = geometry.getAttribute('color');
        if (color) {
            const linear = new THREE.Color();
            for (let index = 0; index < color.array.length; index += 4) {
                linear.setRGB(color.array[index], color.array[index + 1], color.array[index + 2], THREE.SRGBColorSpace);
                color.array[index] = linear.r;
                color.array[index + 1] = linear.g;
                color.array[index + 2] = linear.b;
            }
        }
        attribute(geometry, 'uv', descriptor.uv0 ?? descriptor.uv, 2);
        attribute(geometry, 'uv1', descriptor.uv1, 2);
        attribute(geometry, 'uv2', descriptor.uv2, 2);
        if (descriptor.indices?.length) geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(descriptor.indices), 1));
        if (descriptor.boneIndices?.length) {
            const joints = new Uint16Array(descriptor.boneIndices.length * 4);
            const weights = new Float32Array(joints.length);
            for (let index = 0; index < descriptor.boneIndices.length; index++) {
                joints[index * 4] = descriptor.boneIndices[index];
                weights[index * 4] = 1;
            }
            geometry.setAttribute('skinIndex', new THREE.BufferAttribute(joints, 4));
            geometry.setAttribute('skinWeight', new THREE.BufferAttribute(weights, 4));
        }
        if (!geometry.getAttribute('normal')) geometry.computeVertexNormals();
        geometry.computeBoundingSphere();
        geometry.computeBoundingBox();
        resource.meshes.set(id, { geometry, descriptor, material: this.material(descriptor.material ?? {}, geometry), scenery: null, stageBounds: null });
        for (const [stageId, meshIds] of stages) this.createStage(sceneId, stageId, meshIds);
    }

    material(source, geometry, actor = false) {
        const material = source.unlit ? new THREE.MeshBasicNodeMaterial() : new THREE.MeshStandardNodeMaterial();
        const clock = uniform(0);
        const floorLight = actor ? uniform(1) : 1;
        const uvOffset = uniform(new THREE.Vector2());
        const layerOffset = uniform(new THREE.Vector2());
        const state = { clock, uvOffset, layerOffset };
        if (actor) {
            const actorLight = uniform(new THREE.Vector3());
            const colorAdd = uniform(new THREE.Vector3());
            const wobble = uniform(0);
            const phase = positionLocal.mul(2 * Math.PI / 32).add(clock);
            material.positionNode = positionLocal.add(vec3(phase.x.sin(), phase.y.sin(), phase.z.cos()).mul(wobble));
            const selectionRim = normalView.dot(positionView.negate().normalize()).saturate().oneMinus().pow(3);
            material.emissiveNode = materialColor.rgb.mul(floorLight).mul(actorLight).add(colorAdd.mul(selectionRim).mul(0.6));
            material.colorNode = vec4(materialColor.rgb.mul(floorLight), materialColor.a);
            state.actorLight = actorLight;
            state.colorAdd = colorAdd;
            state.wobble = wobble;
            state.floorLight = floorLight;
        }
        if (source.windX || source.windZ) {
            const min = geometry.boundingBox.min.y;
            const height = Math.max(1, geometry.boundingBox.max.y - min);
            const weight = positionLocal.y.sub(min).div(height).saturate();
            material.positionNode = (material.positionNode ?? positionLocal).add(vec3(clock.mul(0.45).add(positionLocal.z.mul(0.006)).sin().mul(source.windX ?? 0),
                0, clock.mul(0.5).add(positionLocal.x.mul(0.006)).sin().mul(source.windZ ?? 0)).mul(weight));
        }
        material.userData.source = source;
        material.userData.state = state;
        material.userData.actor = actor;
        material.vertexColors = !!geometry.getAttribute('color');
        material.map = this.texture(source.textureId);
        const reflection = positionView.normalize().reflect(normalView);
        const reflectionUv = vec2(reflection.x.mul(0.5).add(0.5), reflection.y.mul(-0.5).add(0.5));
        if (material.map && source.uvForm) {
            const base = texture(material.map, source.uvForm === 5 ? reflectionUv : uv().add(uvOffset));
            material.userData.baseTextureNode = base;
            material.colorNode = vec4(base.rgb.mul(uniform(material.color)).mul(floorLight), base.a);
        }
        if (source.texture2Id) {
            const layer = texture(this.texture(source.texture2Id), source.uvForm2 === 5 ? reflectionUv : uv(1).add(layerOffset))
                .rgb.mul(source.layerStrength ?? 1);
            if (source.layerMode === 'add') {
                material.colorNode = vec4(materialColor.rgb.add(layer).saturate().mul(floorLight), materialColor.a);
            } else if (source.layerMode === 'reflection') {
                material.emissiveNode = material.emissiveNode ? material.emissiveNode.add(layer) : layer;
            } else if (source.layerMode !== 'none') {
                material.colorNode = vec4(materialColor.rgb.mul(layer).mul(floorLight), materialColor.a);
            }
        }
        if (source.color) {
            material.color.fromArray(source.color);
            material.opacity = source.color[3] ?? 1;
        }
        material.side = source.doubleSided ? THREE.DoubleSide : THREE.FrontSide;
        material.alphaTest = Math.max(0, source.alphaCutoff ?? 0);
        material.opacity = source.opacity ?? material.opacity;
        material.roughness = source.roughness ?? 0.85;
        material.metalness = source.metallic ?? 0;
        material.depthWrite = source.depthWrite ?? source.blend !== 'additive';
        material.toneMapped = false;
        materialBlend(material, source);
        material.userData.baseColorNode = material.colorNode;
        this.zeroPointLights?.brand(material);
        return material;
    }

    objectFor(mesh, skeleton = null, actor = false) {
        const material = actor || skeleton ? this.material(mesh.descriptor.material ?? {}, mesh.geometry, actor) : mesh.material;
        const object = skeleton ? new THREE.SkinnedMesh(mesh.geometry, material) : new THREE.Mesh(mesh.geometry, material);
        object.matrixAutoUpdate = false;
        object.matrixWorldAutoUpdate = false;
        object.receiveShadow = !mesh.descriptor.isSky;
        object.castShadow = !mesh.descriptor.isSky && mesh.material.depthWrite;
        if (skeleton) {
            object.bindMode = THREE.DetachedBindMode;
            object.bind(skeleton, identity);
            object.frustumCulled = false;
            if (this.stageSkinCullingRequested && !actor && !mesh.descriptor.isSky) {
                mesh.stageBounds ??= createStageBounds(THREE, mesh.descriptor);
                mesh.stageBounds.attach(object);
            }
        }
        return object;
    }

    arrayMaterial(descriptor, array, geometry) {
        const material = this.material(descriptor.material, geometry);
        const layer = vertexAttribute('textureLayer', 'float').toVarying('noxiusTextureLayer').setInterpolation('flat');
        const sample = texture(array, uv()).depth(layer);
        material.colorNode = vec4(sample.rgb.mul(uniform(material.color)), sample.a);
        material.userData.baseColorNode = material.colorNode;
        material.userData.arrayTexture = array;
        this.staticArrayObservers?.brand(material);
        return material;
    }

    createStage(sceneId, id, meshIds, allowArrays = true) {
        this.flush();
        const resource = this.scene(sceneId);
        resource.shadowRefresh.invalidate();
        this.removeStage(sceneId, id);
        const group = new THREE.Group();
        group.name = `stage-${id}`;
        group.userData.meshIds = Array.from(meshIds);
        const entries = [];
        for (const meshId of meshIds) {
            const mesh = resource.meshes.get(meshId);
            if (!mesh) throw new Error(`Unknown stage mesh ${meshId}.`);
            entries.push({ id: meshId, geometry: mesh.geometry, material: mesh.material, descriptor: mesh.descriptor });
        }
        const staticBatches = buildStaticStageBatches(THREE, entries);
        group.userData.staticBatches = staticBatches;
        group.userData.baselineObjectCount = staticBatches.batches.length + staticBatches.unbatched.length;
        let remaining;
        if (this.textureArraysEnabled && allowArrays) {
            const baseline = [];
            for (const batch of staticBatches.batches) baseline.push({ id: baseline.length, ...batch });
            for (const entry of staticBatches.unbatched) baseline.push({ ...entry, id: baseline.length, sourceMeshId: entry.id });
            const arrays = buildTextureArrayStageBatches(THREE, baseline, this.textures, { chunkSize: 4096,
                allowCutouts: this.cutoutArraysRequested,
                maxLayers: this.textureArrayLayers, maxPageBytes: textureArrayLimits.pageBytes,
                maxTotalBytes: Math.max(0, textureArrayLimits.totalBytes - this.textureArrayBytes),
                createMaterial: (descriptor, array, geometry) => this.arrayMaterial(descriptor, array, geometry) });
            group.userData.textureArrayBatches = arrays;
            this.textureArrayBytes += arrays.bytes;
            for (const batch of arrays.batches) group.add(this.objectFor(batch));
            remaining = arrays.unbatched;
        } else {
            for (const batch of staticBatches.batches) group.add(this.objectFor(batch));
            remaining = staticBatches.unbatched;
        }
        for (const entry of remaining) {
            const sourceMeshId = this.textureArraysEnabled && allowArrays ? entry.sourceMeshId : entry.id;
            if (sourceMeshId === undefined) {
                group.add(this.objectFor(entry));
                continue;
            }
            const mesh = resource.meshes.get(sourceMeshId);
            let skeleton = null;
            if (mesh.descriptor.boneCount) skeleton = mesh.skeleton ??= skeletonFor(mesh.descriptor.boneCount);
            const object = this.objectFor(mesh, skeleton);
            object.userData.sourceMeshId = sourceMeshId;
            mesh.scenery = object;
            group.add(object);
        }
        resource.stages.set(id, group);
        resource.scene.add(group);
        group.userData.renderObjects = Array.from(group.children);
        this.bundleCache?.register(group);
    }

    removeStage(sceneId, id) {
        this.flush();
        const resource = this.scene(sceneId);
        const group = resource.stages.get(id);
        if (group) {
            resource.shadowRefresh.invalidate();
            this.bundleCache?.unregister(group);
            resource.scene.remove(group);
            const arrays = group.userData.textureArrayBatches;
            if (arrays) {
                this.textureArrayBytes -= arrays.bytes;
                arrays.dispose();
            }
            group.userData.staticBatches?.dispose();
            for (const object of group.userData.renderObjects ?? group.children) {
                if (object.userData.sourceMeshId === undefined) continue;
                const mesh = resource.meshes.get(object.userData.sourceMeshId);
                mesh?.stageBounds?.detach(object);
                if (object.material !== mesh?.material) object.material.dispose();
                if (mesh?.scenery === object) mesh.scenery = null;
            }
        }
        resource.stages.delete(id);
    }

    invalidateTextureArrays(textureId) {
        if (textureId >= 0 || !this.textureArraysEnabled || !this.textureArrayBytes) return;
        const affected = [];
        for (const [sceneId, resource] of this.scenes) for (const [stageId, group] of resource.stages) {
            const arrays = group.userData.textureArrayBatches;
            if (!arrays?.bytes) continue;
            let uses = false;
            for (const batch of arrays.batches) if (batch.textureIds.includes(textureId)) { uses = true; break; }
            if (uses) affected.push([sceneId, stageId, group.userData.meshIds]);
        }
        for (const [sceneId, stageId, meshIds] of affected) {
            this.createStage(sceneId, stageId, meshIds, false);
            this.scene(sceneId).stages.get(stageId).userData.textureArrayDirty = true;
            this.textureArrayDirtyScenes.add(sceneId);
        }
    }

    rebuildTextureArrays(sceneId) {
        if (!this.textureArrayDirtyScenes.has(sceneId)) return;
        const resource = this.scene(sceneId);
        const ready = [];
        let pending = false;
        for (const [stageId, group] of resource.stages) {
            if (!group.userData.textureArrayDirty) continue;
            let available = true;
            for (const meshId of group.userData.meshIds) {
                const source = resource.meshes.get(meshId).descriptor.material ?? {};
                if (source.textureId && !this.textures.has(source.textureId)) { available = false; break; }
            }
            if (available) ready.push([stageId, group.userData.meshIds]);
            else pending = true;
        }
        for (const [stageId, meshIds] of ready) this.createStage(sceneId, stageId, meshIds);
        if (!pending) this.textureArrayDirtyScenes.delete(sceneId);
    }

    updateMeshPose(sceneId, id, boneRows) {
        this.flush();
        const mesh = this.scene(sceneId).meshes.get(id);
        if (!mesh) throw new Error(`Unknown animated mesh ${id}.`);
        if (mesh.skeleton) {
            mesh.stageBounds?.update(boneRows);
            palette(mesh.skeleton, boneRows);
        }
    }

    createActor(sceneId, id, meshIds, boneCount) {
        this.flush();
        const resource = this.scene(sceneId);
        resource.shadowRefresh.invalidate();
        this.removeActor(sceneId, id);
        const skeleton = boneCount ? skeletonFor(boneCount) : null;
        const group = new THREE.Group();
        const outlines = [];
        let windRadius = 0;
        for (const meshId of meshIds) {
            const mesh = resource.meshes.get(meshId);
            if (!mesh) throw new Error(`Unknown actor mesh ${meshId}.`);
            const object = this.objectFor(mesh, skeleton, true);
            const source = mesh.descriptor.material ?? {};
            windRadius = Math.max(windRadius, Math.hypot(source.windX ?? 0, source.windZ ?? 0));
            group.add(object);
            const outline = new THREE.MeshBasicNodeMaterial({ color: 0xffd479, side: THREE.BackSide, depthWrite: false,
                transparent: true, toneMapped: false, fog: false });
            const width = uniform(0);
            outline.positionNode = object.material.positionNode.add(normalLocal.mul(width));
            outline.userData.outlineWidth = width;
            const shell = skeleton ? new THREE.SkinnedMesh(mesh.geometry, outline) : new THREE.Mesh(mesh.geometry, outline);
            if (skeleton) {
                shell.bindMode = THREE.DetachedBindMode;
                shell.bind(skeleton, identity);
            }
            shell.matrixAutoUpdate = false;
            shell.matrixWorldAutoUpdate = false;
            shell.frustumCulled = false;
            shell.visible = false;
            shell.renderOrder = 10000;
            shell.userData.outline = true;
            outlines.push(shell);
            group.add(shell);
        }
        group.name = `actor-${id}`;
        resource.actors.set(id, { group, skeleton, outlines, windRadius, sphere: new THREE.Sphere() });
        resource.scene.add(group);
    }

    updateActor(sceneId, id, state, boneRows) {
        this.flush();
        const actor = this.scene(sceneId).actors.get(id);
        if (!actor) throw new Error(`Unknown actor ${id}.`);
        actor.group.visible = state.visible;
        if (actor.skeleton && boneRows?.length) palette(actor.skeleton, boneRows);
        this.updateActorBounds(actor, state);
        for (const object of actor.group.children) {
            if (object.userData.outline) continue;
            const material = object.material;
            const source = material.userData.source;
            material.opacity = (source.opacity ?? 1) * (state.opacity ?? 1);
            const uniforms = material.userData.state;
            if (state.tint) uniforms.actorLight.value.fromArray(state.tint);
            if (state.colorAdd) uniforms.colorAdd.value.fromArray(state.colorAdd);
            uniforms.floorLight.value = state.floorLight ?? 1;
            uniforms.wobble.value = state.wobble ? 2 : 0;
            this.actorOverlay(material, state.overlayTextureId ?? 0, state.overlayForm ?? 0);
            object.castShadow = state.castsShadow ?? true;
            const faded = object.material.opacity < 1;
            if (material.userData.faded !== faded) {
                material.userData.faded = faded;
                materialBlend(material, source, faded);
                material.depthWrite = !faded && (source.depthWrite ?? source.blend !== 'additive');
                material.needsUpdate = true;
            }
        }
        for (const shell of actor.outlines) {
            const strength = state.outline?.[3] ?? 0;
            shell.visible = strength > 0 && (state.opacity ?? 1) > 0;
            if (!shell.visible) continue;
            shell.material.color.setRGB(state.outline[0], state.outline[1], state.outline[2], THREE.SRGBColorSpace);
            shell.material.opacity = state.opacity ?? 1;
            shell.material.userData.outlineWidth.value = strength * 2.5;
        }
    }

    updateActorBounds(actor, state) {
        const min = state.boundsMin;
        const max = state.boundsMax;
        if (!min && !max) return;
        let valid = !!min && !!max;
        for (let index = 0; index < 3 && valid; index++) {
            valid = Number.isFinite(min[index]) && Number.isFinite(max[index]) && max[index] >= min[index];
        }
        if (valid) {
            const x = max[0] - min[0];
            const y = max[1] - min[1];
            const z = max[2] - min[2];
            const radius = Math.hypot(x, y, z) * 0.5;
            valid = radius > 0;
            if (valid) {
                actor.sphere.center.set((min[0] + max[0]) * 0.5, (min[1] + max[1]) * 0.5, (min[2] + max[2]) * 0.5);
                actor.sphere.radius = radius + actor.windRadius + (state.wobble ? Math.sqrt(12) : 0)
                    + Math.max(0, state.outline?.[3] ?? 0) * 2.5;
            }
        }
        // Palettes and bounds already contain world transforms. Three tests this sphere separately for each camera.
        for (const object of actor.group.children) {
            object.boundingSphere = actor.sphere;
            object.frustumCulled = valid;
        }
    }

    actorOverlay(material, textureId, form) {
        const state = material.userData.state;
        if (textureId) {
            if (!state.overlay) {
                const offset = uniform(new THREE.Vector2());
                state.overlay = texture(this.texture(textureId), uv().add(offset));
                state.overlayOffset = offset;
                state.overlayNode = vec4(material.userData.baseColorNode.rgb.add(state.overlay.rgb).saturate(), material.userData.baseColorNode.a);
            }
            state.overlay.value = this.texture(textureId);
            state.overlayForm = form;
            if (!material.userData.overlayActive) {
                material.colorNode = state.overlayNode;
                material.userData.overlayActive = true;
                material.needsUpdate = true;
            }
        } else if (material.userData.overlayActive) {
            material.colorNode = material.userData.baseColorNode;
            material.userData.overlayActive = false;
            material.needsUpdate = true;
        }
    }

    setSkyLayers(sceneId, currentIds, previousIds, previousOpacity, height, brightness) {
        this.flush();
        const resource = this.scene(sceneId);
        resource.skyHeight = height;
        resource.skyBrightness = brightness;
        for (let layer = 0; layer < 2; layer++) {
            const ids = layer ? previousIds : currentIds;
            resource.sky[layer].userData.hasLayers = ids.length > 0;
            resource.sky[layer].visible = ids.length > 0 && (!layer || previousOpacity > 0);
            const objects = resource.skyObjects[layer];
            for (const [id, object] of objects) object.visible = ids.includes(id);
            for (const id of ids) {
                let object = objects.get(id);
                if (!object) {
                    const mesh = resource.meshes.get(id);
                    if (!mesh) throw new Error(`Unknown sky mesh ${id}.`);
                    object = new THREE.Mesh(mesh.geometry, this.material({ ...mesh.descriptor.material, unlit: true }, mesh.geometry));
                    object.material.depthTest = false;
                    object.material.depthWrite = false;
                    object.material.fog = false;
                    object.material.mrtNode = resource.activeSkyMrt;
                    object.frustumCulled = false;
                    object.renderOrder = layer ? -10001 : -10002;
                    objects.set(id, object);
                    resource.sky[layer].add(object);
                }
                object.visible = true;
                object.material.color.setScalar(brightness);
                object.material.opacity = (object.material.userData.source.opacity ?? 1) * (layer ? previousOpacity : 1);
                if (layer) materialBlend(object.material, object.material.userData.source, true);
            }
        }
    }

    setSkyParameters(sceneId, previousOpacity, height, brightness) {
        this.flush();
        const resource = this.scene(sceneId);
        resource.skyHeight = height;
        resource.skyBrightness = brightness;
        for (let layer = 0; layer < 2; layer++) {
            resource.sky[layer].visible = resource.sky[layer].userData.hasLayers && (!layer || previousOpacity > 0);
            for (const object of resource.skyObjects[layer].values()) {
                object.material.color.setScalar(brightness);
                object.material.opacity = (object.material.userData.source.opacity ?? 1) * (layer ? previousOpacity : 1);
            }
        }
    }

    removeActor(sceneId, id) {
        this.flush();
        const resource = this.scene(sceneId);
        const actor = resource.actors.get(id);
        if (!actor) return;
        resource.shadowRefresh.invalidate();
        resource.scene.remove(actor.group);
        for (const object of actor.group.children) object.material.dispose();
        actor.skeleton?.dispose();
        resource.actors.delete(id);
    }

    removeMesh(sceneId, id) {
        this.flush();
        const resource = this.scene(sceneId);
        const mesh = resource.meshes.get(id);
        if (!mesh) return;
        resource.shadowRefresh.invalidate();
        const stages = [];
        for (const [stageId, group] of resource.stages) {
            if (group.userData.meshIds.includes(id)) stages.push([stageId, group.userData.meshIds.filter(meshId => meshId !== id)]);
        }
        for (const [stageId, meshIds] of stages) this.createStage(sceneId, stageId, meshIds);
        mesh.stageBounds?.dispose();
        mesh.scenery?.removeFromParent();
        if (mesh.scenery?.material !== mesh.material) mesh.scenery?.material.dispose();
        for (const objects of resource.skyObjects) {
            const object = objects.get(id);
            if (!object) continue;
            object.removeFromParent();
            object.material.dispose();
            objects.delete(id);
        }
        mesh.geometry.dispose();
        mesh.material.dispose();
        mesh.skeleton?.dispose();
        resource.meshes.delete(id);
    }

    drawScene(sceneId, frame) {
        this.rebuildTextureArrays(sceneId);
        const command = this.command('scene');
        command.sceneId = sceneId;
        command.frame = frame;
        command.actorIds = null;
        command.camera.matrixWorldInverse.fromArray(frame.view);
        command.camera.matrixWorld.copy(command.camera.matrixWorldInverse).invert();
        projection(command.camera, frame.projection, this.renderer);
    }

    drawPortrait(sceneId, actorIds, frame) {
        this.drawScene(sceneId, frame);
        this.commands[this.commandCount - 1].actorIds = actorIds;
    }

    composeScene(sceneId) {
        const command = this.command('compose');
        command.sceneId = sceneId;
    }

    light(resource, frame) {
        resource.ambient.color.fromArray(frame.ambient ?? defaultAmbient);
        resource.ambient.groundColor.fromArray(frame.groundAmbient ?? frame.ambient ?? defaultGround);
        resource.sun.color.fromArray(frame.sunColor ?? defaultSun);
        const direction = frame.sunDirection ?? defaultDirection;
        resource.sun.target.position.fromArray(frame.cameraTarget ?? zero);
        resource.sun.position.copy(resource.sun.target.position);
        resource.sun.position.x -= direction[0] * 3000;
        resource.sun.position.y -= direction[1] * 3000;
        resource.sun.position.z -= direction[2] * 3000;
        for (const group of resource.sky) {
            if (frame.cameraPosition) group.position.fromArray(frame.cameraPosition);
            group.position.y -= resource.skyHeight;
        }
        resource.fogState.start.value = frame.fog?.start ?? 0;
        resource.fogState.density.value = frame.fog?.density ?? 0;
        resource.fogState.opacity.value = frame.fog?.mode ? frame.fog.maxOpacity : 0;
        if (frame.fog?.color) resource.fogState.color.value.fromArray(frame.fog.color);
        if (frame.fog?.color && frame.fog.color[0] === 0 && frame.fog.color[1] === 0 && frame.fog.color[2] === 0 && frame.background) {
            resource.fogState.color.value.fromArray(frame.background);
        }
        for (const mesh of resource.meshes.values()) {
            this.animateMaterial(mesh.material, frame.timeSeconds ?? 0);
            if (mesh.scenery && mesh.scenery.material !== mesh.material) this.animateMaterial(mesh.scenery.material, frame.timeSeconds ?? 0);
        }
        for (const actor of resource.actors.values()) for (const object of actor.group.children) {
            if (!object.userData.outline) this.animateMaterial(object.material, frame.timeSeconds ?? 0);
        }
        for (const objects of resource.skyObjects) for (const object of objects.values()) this.animateMaterial(object.material, frame.timeSeconds ?? 0);
        const points = frame.pointLights ?? [];
        for (let index = 0; index < points.length; index++) {
            const point = points[index];
            let light = resource.points[index];
            if (!light) {
                light = new THREE.PointLight();
                resource.points.push(light);
                resource.scene.add(light);
            }
            light.visible = true;
            light.position.fromArray(point.position);
            light.color.fromArray(point.color);
            light.intensity = (point.intensity ?? 1) * (resource.settings.lightIntensityScale ?? 1);
            light.distance = point.range ?? 0;
            light.decay = 2;
        }
        for (let index = points.length; index < resource.points.length; index++) resource.points[index].visible = false;
    }

    animateMaterial(material, seconds) {
        const source = material.userData.source;
        const state = material.userData.state;
        state.clock.value = seconds;
        state.uvOffset.value.x = textureOffset(source.uvForm ?? 0, Math.floor(seconds * 1000));
        state.layerOffset.value.x = textureOffset(source.uvForm2 ?? 0, Math.floor(seconds * 1000));
        if (state.overlayOffset) state.overlayOffset.value.x = textureOffset(state.overlayForm, Math.floor(seconds * 1000));
        if (source.textureIds?.length) {
            const frame = source.animationFrame === 0x100
                ? ((Math.floor(seconds * 1000) >>> Math.min(source.shiftFrameSpeed, 31)) & source.frameMask) : source.animationFrame ?? 0;
            material.map = this.texture(source.textureIds[frame % source.textureIds.length]);
            if (material.userData.baseTextureNode) material.userData.baseTextureNode.value = material.map;
        }
    }

    flush() {
        if (!this.commandCount) return;
        const renderer = this.renderer;
        for (let index = 0; index < this.commandCount; index++) {
            const command = this.commands[index];
            const target = this.target(command.targetId);
            renderer.setRenderTarget(target);
            renderer.setViewport(0, 0, target?.width ?? this.width, target?.height ?? this.height);
            renderer.setScissorTest(false);
            if (target) {
                target.viewport.set(0, 0, target.width, target.height);
                target.scissorTest = false;
            }
            if (command.kind === 'clear') {
                renderer.setClearColor(command.color, command.alpha);
                renderer.clear(command.clearColor, command.clearDepth, false);
            } else if (command.kind === 'compose') {
                this.composePost();
            } else if (command.kind === 'native' || command.kind === 'rain') {
                const started = this.profiler?.start();
                const hdr = !command.targetId && this.post?.active;
                const painter = command.kind === 'rain' ? this.rain : this.native;
                if (hdr) renderer.setRenderTarget(this.post.target);
                let end = index + 1;
                while (end < this.commandCount && this.commands[end].kind === command.kind && this.commands[end].targetId === command.targetId
                    && painter.sameClip(command.batch, this.commands[end].batch)) end++;
                painter.render(this.commands, index, end, hdr, hdr ? this.post.scale : 1);
                this.profiler?.endPhase(ThreePhase.native, started, end - index);
                this.stats.nativeDraws += end - index;
                index = end - 1;
            } else {
                const started = this.profiler?.start();
                const resource = this.scene(command.sceneId);
                if (!command.actorIds && resource.lastWorldCamera) {
                    resource.lastWorldCamera.projectionMatrix.copy(command.camera.projectionMatrix);
                    resource.lastWorldCamera.matrixWorldInverse.copy(command.camera.matrixWorldInverse);
                    resource.lastWorldPointLightCount = command.frame.pointLights?.length ?? 0;
                    resource.hasWorldCamera = true;
                }
                if (!command.actorIds && this.bundleCache) {
                    for (const stage of resource.stages.values()) {
                        this.bundleCache.prepare(stage, command.frame.pointLights?.length ?? 0,
                            !!command.frame.shadowsEnabled && resource.sun.castShadow);
                    }
                }
                if (renderer.shadowMap) {
                    renderer.shadowMap.enabled = !command.actorIds && command.frame.shadowsEnabled && resource.sun.castShadow;
                    renderer.shadowMap.type = THREE.PCFShadowMap;
                }
                this.light(resource, command.frame);
                const configuredShadowHz = resource.settings.shadowUpdateRate ?? 0;
                const shadowHz = this.dynamicShadowHz > 0
                    ? (configuredShadowHz > 0 ? Math.min(configuredShadowHz, this.dynamicShadowHz) : this.dynamicShadowHz) : configuredShadowHz;
                if (!command.actorIds) resource.shadowRefresh.update(resource.sun, command.frame, shadowHz, !!renderer.shadowMap?.enabled);
                const postTarget = !command.targetId && !command.actorIds
                    ? this.post?.beginScene(resource.scene, command.camera, command.frame, resource.settings, null) : null;
                if (postTarget && this.post.effective) this.post.effective.dynamicShadowUpdateRate = this.dynamicShadowHz > 0
                    && (configuredShadowHz === 0 || this.dynamicShadowHz < configuredShadowHz) && renderer.shadowMap?.enabled ? shadowHz : 0;
                if (postTarget) renderer.setRenderTarget(postTarget);
                // Sky alpha layers must finish before either the world's opaque or transparent queues.
                // A negative renderOrder in the world scene cannot cross Three's separate material queues.
                if (!command.actorIds && (resource.sky[0].visible || resource.sky[1].visible)) {
                    const skyMrt = renderer.getMRT?.()?.has('normalRoughness') ? resource.skyMrt : null;
                    if (skyMrt !== resource.activeSkyMrt) {
                        resource.activeSkyMrt = skyMrt;
                        for (const objects of resource.skyObjects) for (const object of objects.values()) {
                            object.material.mrtNode = skyMrt;
                            object.material.needsUpdate = true;
                        }
                    }
                    renderer.render(resource.skyScene, command.camera);
                }
                if (command.actorIds) {
                    resource.shadowRefresh.invalidate();
                    for (const group of resource.stages.values()) group.visible = false;
                    for (const [id, actor] of resource.actors) {
                        actor.wasVisible = actor.group.visible;
                        actor.group.visible = command.actorIds.includes(id);
                    }
                }
                if (command.actorIds && this.display) {
                    this.display.begin(target, target?.width ?? this.width, target?.height ?? this.height);
                    try {
                        renderer.render(resource.scene, command.camera);
                    } finally {
                        this.display.compose(target, command.frame.exposure ?? 1);
                    }
                } else {
                    renderer.render(resource.scene, command.camera);
                }
                if (postTarget) this.post.finishScene();
                this.stats.sceneDraws++;
                if (command.actorIds) {
                    for (const group of resource.stages.values()) group.visible = true;
                    for (const actor of resource.actors.values()) actor.group.visible = actor.wasVisible;
                }
                this.profiler?.endPhase(command.actorIds ? ThreePhase.portrait : ThreePhase.scene, started);
            }
        }
        this.commandCount = 0;
    }

    present() {
        this.flush();
        this.composePost();
        this.renderer.setRenderTarget(null);
        this.renderer.setScissorTest(false);
        this.stats.frames++;
        this.profiler?.endFrame();
    }

    disposeScene(id) {
        this.flush();
        const resource = this.scenes.get(id);
        if (!resource) return;
        for (const actorId of resource.actors.keys()) this.removeActor(id, actorId);
        for (const stageId of resource.stages.keys()) this.removeStage(id, stageId);
        resource.sun.shadow.dispose();
        for (const meshId of resource.meshes.keys()) this.removeMesh(id, meshId);
        this.scenes.delete(id);
        this.textureArrayDirtyScenes.delete(id);
    }

    async dispose() {
        this.flush();
        for (const id of this.scenes.keys()) this.disposeScene(id);
        for (const id of this.textures.keys()) this.destroyTexture(id);
        this.native.dispose();
        this.rain.dispose();
        this.post?.dispose();
        this.display?.dispose();
        this.profiler?.dispose();
        this.bundleCache?.dispose();
        await this.renderer.dispose();
    }
}
