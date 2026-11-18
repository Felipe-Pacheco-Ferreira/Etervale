import * as THREE from './vendor/three/three.webgpu.js';
import { mrt, output, normalView, roughness, float, vec4, texture, renderOutput, rtt, convertToTexture } from './vendor/three/three.tsl.js';
import { ao } from './vendor/three/addons/tsl/display/GTAONode.js';
import { denoise } from './vendor/three/addons/tsl/display/DenoiseNode.js';
import { ssr } from './vendor/three/addons/tsl/display/SSRNode.js';
import { bloom } from './vendor/three/addons/tsl/display/BloomNode.js';
import { fxaa } from './vendor/three/addons/tsl/display/FXAANode.js';
import SMAANode, { smaa } from './vendor/three/addons/tsl/display/SMAANode.js';
import { sharpen } from './vendor/three/addons/tsl/display/SharpenNode.js';
import { ExternalCamera } from './three-camera.js';
import { fusedSharpen } from './three-sharpen.js';

const unsupported = Object.freeze({
    temporalAntiAliasing: 'Temporal AA requires a velocity and jitter pass controlled by the scene renderer.',
    autoExposure: 'Histogram exposure is not part of the current Three pipeline.',
    colorGradingLut: 'Custom 3D LUT import is not part of the current Three pipeline.',
    volumetricLight: 'Volumetric shafts require the sun shadow texture bridge.',
    contactShadows: 'Screen-space contact shadow tracing is not part of the current Three pipeline.'
});
const defaultSettings = Object.freeze({});

function finite(value, fallback, low, high) {
    return Number.isFinite(value) ? Math.min(high, Math.max(low, value)) : fallback;
}

export function postOptions(settings = {}) {
    const antiAliasing = String(settings.antiAliasing ?? 'Off').toLowerCase();
    const multisampled = antiAliasing === 'msaa';
    const samples = multisampled ? (settings.multiSampleCount >= 4 ? 4 : 2) : 0;
    const quality = String(settings.quality ?? 'High').toLowerCase();
    return {
        renderScale: finite(settings.renderScale, 1, 0.5, 2),
        antiAliasing: ['off', 'fxaa', 'smaa', 'msaa', 'taa'].includes(antiAliasing) ? antiAliasing : 'off',
        samples,
        ambientOcclusion: !!settings.ambientOcclusion && !multisampled,
        screenSpaceReflections: !!settings.screenSpaceReflections && !multisampled,
        bloom: String(settings.bloom ?? 'Off').toLowerCase(),
        exposureCompensation: finite(settings.exposureCompensation, 0, -3, 3),
        tonemapper: String(settings.tonemapper ?? 'PbrNeutral').toLowerCase(),
        sharpening: finite(settings.sharpening, 0, 0, 1),
        highQuality: quality === 'ultra' || quality === 'high'
    };
}

const internalPostOwners = new WeakMap();

class InternalSMAA extends SMAANode {
    constructor(input, post) {
        super(convertToTexture(input));
        internalPostOwners.set(this, post);
    }

    setSize(width, height) {
        const scale = internalPostOwners.get(this)?.internalPostScale ?? 1;
        super.setSize(Math.max(1, Math.floor(width * scale)), Math.max(1, Math.floor(height * scale)));
    }
}

export function createThreePost(renderer, options = {}) {
    return new ThreePost(renderer, options.internalPost === true, options.fusedSharpen === true);
}

class PostComposition {
    constructor(renderer, outputNode) {
        this.renderer = renderer;
        this.outputNode = outputNode;
        const material = new THREE.NodeMaterial();
        material.name = 'Noxius post composition';
        material.fragmentNode = outputNode;
        material.toneMapped = false;
        material.depthTest = false;
        material.depthWrite = false;
        this.quad = new THREE.QuadMesh(material);
    }

    render() {
        this.quad.render(this.renderer);
    }

    dispose() {
        this.quad.material.dispose();
    }
}

class ThreePost {
    constructor(renderer, internalPostRequested, fusedSharpenRequested) {
        this.renderer = renderer;
        this.width = 1;
        this.height = 1;
        this.active = false;
        this.target = null;
        this.mrt = null;
        this.scale = 1;
        this.outputTarget = null;
        this.camera = new ExternalCamera(renderer.coordinateSystem);
        this.effects = [];
        this.aoCache = null;
        this.aoScale = 1;
        this.aoEffect = null;
        this.reflectionEffect = null;
        this.bloomEffect = null;
        this.appliedPassScale = null;
        this.dynamicResolutionScaleCap = null;
        this.internalPostRequested = internalPostRequested;
        this.internalPostEnabled = internalPostRequested;
        this.internalPostScale = 1;
        this.internalRTTs = [];
        this.fullPostInput = null;
        this.internalSMAA = null;
        this.fusedSharpenRequested = fusedSharpenRequested;
        this.fusedSharpenCompositions = 0;
        this.appliedPostWidth = 0;
        this.appliedPostHeight = 0;
        this.settingCache = new WeakMap();
        this.clearColor = new THREE.Color();
        this.capabilities = { ambientOcclusion: true, screenSpaceReflections: true, bloom: true,
            antiAliasing: ['Off', 'Fxaa', 'Smaa', 'Msaa'], unsupported };
    }

    beginFrame(width, height) {
        if (this.active) this.compose();
        this.width = Math.max(1, Math.round(width));
        this.height = Math.max(1, Math.round(height));
    }

    setDynamicResolutionScale(scale) {
        if (scale !== null && (!Number.isFinite(scale) || scale < 0.5 || scale > 2)) {
            throw new RangeError('Dynamic resolution scale must be null or between 0.5 and 2.');
        }
        this.dynamicResolutionScaleCap = scale;
        return scale;
    }

    setInternalPostEnabled(enabled) {
        if (typeof enabled !== 'boolean') throw new TypeError('Internal post requires a boolean.');
        if (enabled && !this.internalPostRequested) throw new Error('Internal post requires an enabled renderer configuration.');
        if (this.active) throw new Error('Apply internal post changes between composed frames.');
        if (this.internalPostEnabled !== enabled) {
            this.internalPostEnabled = enabled;
            this.appliedPassScale = null;
        }
        return this.internalPostEnabled;
    }

    getInternalPostProfile() {
        return { requested: this.internalPostRequested, enabled: this.internalPostEnabled,
            scale: this.internalPostScale, width: this.effective?.postProcessingWidth ?? null,
            height: this.effective?.postProcessingHeight ?? null, automaticInputs: this.internalRTTs.length,
            measurement: 'Conservative internal post footprints; display tone/upscale, RCAS and composition remain full size' };
    }

    getFusedSharpenProfile() {
        return { requested: this.fusedSharpenRequested, enabled: this.effective?.fusedSharpen ?? false,
            compositions: this.fusedSharpenCompositions };
    }

    resizeEffects(scale) {
        if (this.appliedPassScale === scale && (!this.internalPostRequested
            || (this.appliedPostWidth === this.width && this.appliedPostHeight === this.height))) return;
        if (this.internalPostRequested) {
            this.internalPostScale = this.internalPostEnabled ? Math.max(Math.min(1, scale), 1 / this.width, 1 / this.height) : 1;
            for (const node of this.internalRTTs) node.setResolutionScale(this.internalPostScale);
            this.appliedPostWidth = this.width;
            this.appliedPostHeight = this.height;
        }
        const halfScale = scale * 0.5;
        if (this.aoEffect) this.aoEffect.resolutionScale = halfScale;
        if (this.reflectionEffect) this.reflectionEffect.resolutionScale = halfScale;
        this.bloomEffect?.setResolutionScale(halfScale);
        if (this.aoCache) this.aoScale = halfScale;
        this.appliedPassScale = scale;
    }

    copyCamera(camera) {
        this.camera.projectionMatrix.copy(camera.projectionMatrix);
        this.camera.matrixWorld.copy(camera.matrixWorld);
        this.camera.matrixWorldInverse.copy(camera.matrixWorldInverse);
        this.camera.position.setFromMatrixPosition(camera.matrixWorld);
        this.camera.updateProjectionMatrix();
    }

    beginScene(scene, camera, frame, settings, outputTarget = null) {
        if (this.active) {
            this.renderer.setRenderTarget(this.target);
            this.renderer.setMRT(this.mrt);
            return this.target;
        }
        settings ??= defaultSettings;
        this.copyCamera(camera);
        let configuration = this.settingCache.get(settings);
        if (!configuration) {
            const options = postOptions(settings);
            configuration = { options, key: JSON.stringify(options) };
            this.settingCache.set(settings, configuration);
        }
        const { options, key } = configuration;
        if (key !== this.key) {
            this.releasePipeline();
            this.options = options;
            this.key = key;
            this.build();
        }
        this.scale = Math.min(options.renderScale, this.dynamicResolutionScaleCap ?? options.renderScale);
        this.resizeEffects(this.scale);
        this.effective.renderScale = this.scale;
        this.effective.configuredRenderScale = options.renderScale;
        this.effective.dynamicResolutionScaleCap = this.dynamicResolutionScaleCap;
        this.outputTarget = outputTarget;
        this.target.setSize(Math.max(1, Math.round(this.width * this.scale)), Math.max(1, Math.round(this.height * this.scale)));
        this.effective.renderWidth = this.target.width;
        this.effective.renderHeight = this.target.height;
        if (this.aoCache) {
            const width = Math.max(1, Math.round(this.width * this.aoScale));
            const height = Math.max(1, Math.round(this.height * this.aoScale));
            if (this.aoCache.renderTarget.width !== width || this.aoCache.renderTarget.height !== height) this.aoCache.setSize(width, height);
        }
        this.target.viewport.set(0, 0, this.target.width, this.target.height);
        this.target.scissorTest = false;
        this.renderer.setRenderTarget(this.target);
        this.renderer.setViewport(0, 0, this.target.width, this.target.height);
        this.renderer.setScissorTest(false);
        this.renderer.setMRT(this.mrt);
        this.renderer.setClearColor(frame?.background ? this.clearColor.fromArray(frame.background) : 0, frame?.background?.[3] ?? 1);
        this.renderer.clear(true, true, false);
        this.active = true;
        return this.target;
    }

    finishScene() {
        this.renderer.setMRT(null);
    }

    build() {
        const renderer = this.renderer;
        const options = this.options;
        const normals = options.screenSpaceReflections;
        this.target = new THREE.RenderTarget(1, 1, { type: THREE.HalfFloatType, depthBuffer: true, stencilBuffer: false,
            count: normals ? 2 : 1, samples: options.samples });
        this.target.texture.name = 'output';
        this.target.texture.colorSpace = THREE.LinearSRGBColorSpace;
        this.target.depthTexture = new THREE.DepthTexture(1, 1, THREE.FloatType);
        this.target.depthTexture.name = 'Noxius world depth';
        this.mrt = null;
        let normalTexture = null;
        if (normals) {
            this.target.textures[1].name = 'normalRoughness';
            this.mrt = mrt({ output, normalRoughness: vec4(normalView, roughness) });
            normalTexture = texture(this.target.textures[1]);
        }
        const color = texture(this.target.texture);
        const depth = texture(this.target.depthTexture);
        let result = color;
        if (options.ambientOcclusion) {
            const occlusion = ao(depth, normalTexture, this.camera);
            this.aoEffect = occlusion;
            occlusion.radius.value = 80;
            occlusion.thickness.value = 20;
            occlusion.samples.value = options.highQuality ? 12 : 8;
            occlusion.resolutionScale = options.renderScale * 0.5;
            const filtered = denoise(occlusion, depth, normalTexture, this.camera);
            filtered.radius.value = 3;
            filtered.depthPhi.value = 40;
            this.effects.push(occlusion, filtered);
            // Bloom and AA sample one filtered AO texture instead of repeating the denoise function in each pass.
            this.aoCache = rtt(filtered, 1, 1, { depthBuffer: false });
            this.aoCache.name = 'NoxiusDenoisedAO';
            this.aoScale = occlusion.resolutionScale;
            result = vec4(result.rgb.mul(this.aoCache.r), result.a);
        }
        if (normals) {
            const reflections = ssr(color, depth, normalTexture, {
                camera: this.camera, metalnessNode: float(0), roughnessNode: normalTexture.a, reflectNonMetals: true, binaryRefine: options.highQuality
            });
            this.reflectionEffect = reflections;
            reflections.resolutionScale = options.renderScale * 0.5;
            reflections.quality.value = options.highQuality ? 0.4 : 0.25;
            reflections.maxDistance.value = 1500;
            reflections.thickness.value = 8;
            reflections.intensity.value = 0.35;
            reflections.screenEdgeFadeBlack = true;
            this.effects.push(reflections);
            result = vec4(result.rgb.add(reflections.rgb), result.a);
        }
        if (options.bloom !== 'off') {
            const glow = bloom(result, options.bloom === 'high' ? 0.05 : 0.03, 0.35, 1);
            this.bloomEffect = glow;
            glow.setResolutionScale(options.renderScale * 0.5);
            this.effects.push(glow);
            result = vec4(result.rgb.add(glow.rgb), result.a);
        }
        result = vec4(result.rgb.mul(2 ** options.exposureCompensation), result.a);
        if (options.antiAliasing === 'smaa') {
            const antialiased = this.internalPostRequested ? new InternalSMAA(result, this) : smaa(result);
            this.internalSMAA = this.internalPostRequested ? antialiased : null;
            this.effects.push(antialiased);
            result = antialiased;
        }
        const tonemapper = options.tonemapper === 'agx' ? THREE.AgXToneMapping
            : options.tonemapper === 'aces' ? THREE.ACESFilmicToneMapping : THREE.NeutralToneMapping;
        result = renderOutput(result, tonemapper, THREE.SRGBColorSpace);
        if (options.antiAliasing === 'fxaa') {
            const antialiased = fxaa(result);
            this.effects.push(antialiased);
            result = antialiased;
        }
        if (options.sharpening > 0) {
            const sharpened = this.fusedSharpenRequested ? fusedSharpen(result, 2 * (1 - options.sharpening), true)
                : sharpen(result, 2 * (1 - options.sharpening), true);
            if (this.internalPostRequested) this.fullPostInput = sharpened.textureNode;
            this.effects.push(sharpened);
            result = sharpened;
        }
        // Each automatic intermediate contains one fullscreen colour draw; sampled scene depth belongs to the world target.
        const scaled = new Set();
        result.traverse(node => {
            if (this.internalPostRequested && node.isRTTNode && node.autoResize && node !== this.fullPostInput && !scaled.has(node)) {
                scaled.add(node);
                this.internalRTTs.push(node);
            }
            if (node.isRTTNode && node.autoResize && node.renderTarget.depthTexture === null) node.renderTarget.depthBuffer = false;
        });
        // World and native UI share an encoded UNORM output; the renderer adds no implicit output transfer.
        this.pipeline = new PostComposition(renderer, result);
        this.effective = { ...options, antiAliasing: options.antiAliasing === 'taa' ? 'off' : options.antiAliasing,
            temporalAntiAliasing: false, unsupported };
    }

    compose() {
        if (!this.active) return;
        const renderer = this.renderer;
        this.finishScene();
        renderer.setRenderTarget(this.outputTarget);
        if (this.outputTarget) {
            this.outputTarget.viewport.set(0, 0, this.outputTarget.width, this.outputTarget.height);
            this.outputTarget.scissorTest = false;
        }
        renderer.setViewport(0, 0, this.outputTarget?.width ?? this.width, this.outputTarget?.height ?? this.height);
        renderer.setScissorTest(false);
        this.pipeline.render();
        this.effective.fusedSharpen = this.fusedSharpenRequested && this.options.sharpening > 0;
        if (this.effective.fusedSharpen) this.fusedSharpenCompositions++;
        if (this.internalPostRequested) {
            const target = this.internalSMAA?._renderTargetBlend ?? this.internalRTTs[0]?.renderTarget;
            this.effective.internalPost = this.internalPostEnabled;
            this.effective.postProcessingWidth = target?.width ?? this.width;
            this.effective.postProcessingHeight = target?.height ?? this.height;
        }
        this.active = false;
    }

    releasePipeline() {
        const released = new Set();
        this.pipeline?.outputNode.traverse(node => {
            if (node.isRTTNode && !released.has(node)) {
                released.add(node);
                node.dispose();
            }
        });
        this.pipeline?.dispose();
        for (const effect of this.effects) effect.dispose?.();
        this.effects.length = 0;
        this.target?.dispose();
        this.target = null;
        this.aoCache = null;
        this.aoEffect = null;
        this.reflectionEffect = null;
        this.bloomEffect = null;
        this.appliedPassScale = null;
        this.internalRTTs.length = 0;
        this.fullPostInput = null;
        this.internalSMAA = null;
        this.aoScale = 1;
    }

    dispose() {
        if (this.active) this.compose();
        this.releasePipeline();
    }
}
