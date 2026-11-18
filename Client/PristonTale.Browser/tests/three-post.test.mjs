import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { ExternalCamera } from '../wwwroot/three-camera.js';
import { internalPostMode } from '../wwwroot/adaptive-resolution.js';
import { createThreePost, postOptions } from '../wwwroot/three-post.js';

function harness(options) {
    const calls = [];
    const renderer = { coordinateSystem: THREE.WebGPUCoordinateSystem, toneMapping: THREE.NoToneMapping,
        outputColorSpace: THREE.LinearSRGBColorSpace,
        setRenderTarget(target) { calls.push(['target', target]); },
        setViewport(...values) { calls.push(['viewport', ...values]); },
        setScissorTest(value) { calls.push(['scissor', value]); },
        setMRT(value) { calls.push(['mrt', value]); },
        render(mesh) { calls.push(['quad', mesh, this.outputColorSpace]); },
        setClearColor(...values) { calls.push(['background', ...values]); },
        clear(...values) { calls.push(['clear', ...values]); } };
    const camera = new ExternalCamera(renderer.coordinateSystem);
    camera.projectionMatrix.makePerspective(-1, 1, 1, -1, 1, 20000, THREE.WebGPUCoordinateSystem);
    camera.updateProjectionMatrix();
    return { calls, camera, post: createThreePost(renderer, options) };
}

test('post options preserve requested controls and declare temporal support honestly', () => {
    assert.deepEqual(postOptions({ antiAliasing: 'Msaa', multiSampleCount: 4, ambientOcclusion: true, screenSpaceReflections: true }).samples, 4);
    assert.equal(postOptions({ antiAliasing: 'Msaa', ambientOcclusion: true }).ambientOcclusion, false);
    assert.equal(postOptions({ antiAliasing: 'Msaa', screenSpaceReflections: true }).screenSpaceReflections, false);
    assert.equal(postOptions({ renderScale: 0.75 }).renderScale, 0.75);
    assert.equal(postOptions({ renderScale: NaN }).renderScale, 1);
    assert.equal(postOptions({ exposureCompensation: 90 }).exposureCompensation, 3);
    const { post, camera } = harness();
    post.beginScene(null, camera, null, { antiAliasing: 'Taa' });
    assert.equal(post.effective.antiAliasing, 'off');
    assert.match(post.capabilities.unsupported.temporalAntiAliasing, /velocity/);
    post.active = false;
    post.dispose();
});

test('world post target retains HDR, sampled depth and actual roughness for reflections', () => {
    const { post, camera } = harness();
    post.beginFrame(1280, 720);
    post.beginScene(null, camera, { background: [0.1, 0.2, 0.3, 1] }, {
        renderScale: 0.75, ambientOcclusion: true, screenSpaceReflections: true, bloom: 'High', sharpening: 0.6
    });
    assert.equal(post.target.width, 960);
    assert.equal(post.target.height, 540);
    assert.equal(post.target.texture.type, THREE.HalfFloatType);
    assert.equal(post.target.textures[1].name, 'normalRoughness');
    assert.ok(post.target.depthTexture.isDepthTexture);
    assert.ok(post.mrt.isMRTNode);
    assert.deepEqual(post.effects.map(node => node.constructor.name), ['GTAONode', 'DenoiseNode', 'SSRNode', 'BloomNode', 'SharpenNode']);
    assert.equal(post.camera.near, 1);
    assert.ok(Math.abs(post.camera.far - 20000) < 0.1);
    post.active = false;
    post.dispose();
});

test('scene facades share one HDR target and explicit composition finishes before HUD', () => {
    const { post, camera, calls } = harness();
    const settings = { bloom: 'Low' };
    post.beginFrame(1280, 720);
    const target = post.beginScene(null, camera, null, settings);
    const pipeline = post.pipeline;
    const second = post.beginScene(null, camera, null, settings);
    assert.equal(second, target);
    assert.equal(calls.filter(call => call[0] === 'clear').length, 1);
    post.finishScene();
    assert.deepEqual(calls.at(-1), ['mrt', null]);
    post.pipeline.render = () => calls.push(['compose']);
    post.compose();
    assert.equal(post.active, false);
    assert.deepEqual(calls.at(-1), ['compose']);
    assert.ok(calls.some(call => call[0] === 'target' && call[1] === null));
    assert.ok(calls.some(call => call[0] === 'viewport' && call[3] === 1280 && call[4] === 720));
    post.beginFrame(1280, 720);
    assert.equal(post.beginScene(null, camera, null, settings), target);
    assert.equal(post.pipeline, pipeline);
    post.active = false;
    post.dispose();
});

test('dynamic resolution resizes existing world and half-resolution effects without rebuilding saved settings', () => {
    const { post, camera, calls } = harness();
    const settings = { renderScale: 1, ambientOcclusion: true, screenSpaceReflections: true, bloom: 'High' };
    post.beginFrame(1280, 720); post.beginScene(null, camera, null, settings);
    const pipeline = post.pipeline, target = post.target, cache = post.aoCache, options = post.options;
    const effects = [post.aoEffect, post.reflectionEffect, post.bloomEffect];
    const projection = post.camera.projectionMatrix.elements.slice();
    post.compose();
    assert.equal(post.setDynamicResolutionScale(0.75), 0.75);
    post.beginFrame(1280, 720); post.beginScene(null, camera, null, settings);
    assert.equal(post.pipeline, pipeline); assert.equal(post.target, target); assert.equal(post.aoCache, cache); assert.equal(post.options, options);
    assert.deepEqual([post.aoEffect, post.reflectionEffect, post.bloomEffect], effects);
    assert.deepEqual([target.width, target.height], [960, 540]);
    assert.deepEqual([cache.renderTarget.width, cache.renderTarget.height], [480, 270]);
    assert.equal(post.aoEffect.resolutionScale, 0.375); assert.equal(post.reflectionEffect.resolutionScale, 0.375);
    assert.equal(post.bloomEffect.getResolutionScale(), 0.375);
    assert.deepEqual(post.camera.projectionMatrix.elements, projection);
    assert.equal(options.renderScale, 1); assert.equal(settings.renderScale, 1);
    assert.equal(post.effective.renderScale, 0.75); assert.equal(post.effective.configuredRenderScale, 1);
    assert.equal(post.effective.dynamicResolutionScaleCap, 0.75);
    post.compose();
    assert.ok(calls.some(call => call[0] === 'viewport' && call[3] === 1280 && call[4] === 720));
    post.setDynamicResolutionScale(null); post.beginScene(null, camera, null, settings);
    assert.equal(post.pipeline, pipeline); assert.deepEqual([target.width, target.height], [1280, 720]);
    assert.deepEqual([cache.renderTarget.width, cache.renderTarget.height], [640, 360]);
    assert.equal(post.aoEffect.resolutionScale, 0.5); assert.equal(post.reflectionEffect.resolutionScale, 0.5);
    assert.equal(post.bloomEffect.getResolutionScale(), 0.5); assert.equal(post.effective.dynamicResolutionScaleCap, null);
    post.active = false; post.dispose();
});

test('dynamic resolution caps the configured scale and cannot change a partially drawn frame', () => {
    const { post, camera } = harness();
    const settings = { renderScale: 0.75 };
    post.setDynamicResolutionScale(1); post.beginFrame(1280, 720); post.beginScene(null, camera, null, settings);
    assert.equal(post.scale, 0.75); assert.deepEqual([post.target.width, post.target.height], [960, 540]);
    const target = post.target;
    post.setDynamicResolutionScale(0.5);
    assert.equal(post.beginScene(null, camera, null, settings), target);
    assert.equal(post.scale, 0.75); assert.equal(post.effective.renderScale, 0.75); assert.equal(target.width, 960);
    post.compose(); post.beginScene(null, camera, null, settings);
    assert.equal(post.scale, 0.5); assert.deepEqual([target.width, target.height], [640, 360]);
    assert.equal(post.effective.configuredRenderScale, 0.75); assert.equal(settings.renderScale, 0.75);
    for (const invalid of [undefined, NaN, Infinity, 0.49, 2.01, '0.5']) assert.throws(() => post.setDynamicResolutionScale(invalid), RangeError);
    post.active = false; post.dispose();
});

test('saved feature changes alone rebuild the pipeline and reapply the transient scale to recreated effects', () => {
    const { post, camera } = harness();
    const high = { renderScale: 1, ambientOcclusion: true, screenSpaceReflections: true, bloom: 'High' };
    post.setDynamicResolutionScale(0.6); post.beginFrame(1280, 720); post.beginScene(null, camera, null, high);
    const pipeline = post.pipeline; post.compose();
    post.beginScene(null, camera, null, { renderScale: 1 });
    assert.notEqual(post.pipeline, pipeline); assert.equal(post.scale, 0.6);
    assert.equal(post.aoEffect, null); assert.equal(post.reflectionEffect, null); assert.equal(post.bloomEffect, null);
    post.compose(); post.beginScene(null, camera, null, high);
    assert.equal(post.aoEffect.resolutionScale, 0.3); assert.equal(post.reflectionEffect.resolutionScale, 0.3);
    assert.equal(post.bloomEffect.getResolutionScale(), 0.3); assert.equal(post.aoScale, 0.3);
    assert.deepEqual([post.aoCache.renderTarget.width, post.aoCache.renderTarget.height], [384, 216]);
    post.active = false; post.dispose();
});

test('dynamic world resolution leaves AA, display and HUD composition at the full output size', () => {
    const savedImage = globalThis.Image; globalThis.Image = class {};
    try {
        const { post, camera, calls } = harness();
        const settings = { renderScale: 1, ambientOcclusion: true, bloom: 'High', antiAliasing: 'Smaa', sharpening: 0.6 };
        post.beginFrame(1280, 720); post.beginScene(null, camera, null, settings);
        const automatic = new Set();
        post.pipeline.outputNode.traverse(node => { if (node.isRTTNode && node.autoResize) automatic.add(node); });
        const smaa = post.effects.find(node => node.constructor.name === 'SMAANode');
        post.compose(); post.setDynamicResolutionScale(0.5); post.beginScene(null, camera, null, settings);
        assert.equal(post.effects.find(node => node.constructor.name === 'SMAANode'), smaa);
        assert.equal(automatic.size, 3);
        for (const node of automatic) assert.equal(node.getResolutionScale(), 1);
        assert.deepEqual([post.target.width, post.target.height], [640, 360]);
        assert.deepEqual([post.aoCache.renderTarget.width, post.aoCache.renderTarget.height], [320, 180]);
        post.compose();
        assert.deepEqual(calls.filter(call => call[0] === 'viewport').at(-1), ['viewport', 0, 0, 1280, 720]);
        post.dispose();
    } finally {
        if (savedImage) globalThis.Image = savedImage; else delete globalThis.Image;
    }
});

test('implicit post intermediates omit unused depth while world, shadow and portrait targets retain their depth and ownership', () => {
    const savedImage = globalThis.Image; globalThis.Image = class {};
    const shadowTarget = new THREE.RenderTarget(64, 64, { depthBuffer: true });
    const portraitTarget = new THREE.RenderTarget(32, 32, { depthBuffer: true });
    try {
        const { post, camera } = harness();
        const scene = new THREE.Scene(), sun = new THREE.DirectionalLight(); sun.shadow.map = shadowTarget; scene.add(sun);
        let unrelatedDisposals = 0;
        shadowTarget.addEventListener('dispose', () => unrelatedDisposals++);
        portraitTarget.addEventListener('dispose', () => unrelatedDisposals++);
        const settings = { renderScale: 1, ambientOcclusion: true, screenSpaceReflections: true, bloom: 'High', antiAliasing: 'Smaa', sharpening: 0.6 };
        post.beginFrame(1280, 720); post.beginScene(scene, camera, null, settings, portraitTarget);
        const automatic = new Set();
        post.pipeline.outputNode.traverse(node => { if (node.isRTTNode && node.autoResize) automatic.add(node); });
        assert.equal(automatic.size, 3);
        const originals = [...automatic].map(node => [node, node.renderTarget, node.renderTarget.texture]);
        for (const [node, target] of originals) {
            assert.equal(target.depthBuffer, false); assert.equal(target.depthTexture, null);
            assert.equal(target.texture.type, THREE.HalfFloatType); assert.equal(target.texture.format, THREE.RGBAFormat);
            assert.equal(node.getResolutionScale(), 1);
        }
        assert.equal(post.aoCache.renderTarget.depthBuffer, false);
        assert.equal(post.target.depthBuffer, true); assert.ok(post.target.depthTexture.isDepthTexture);
        const world = post.target, pipeline = post.pipeline;
        post.compose(); post.setDynamicResolutionScale(0.5); post.beginFrame(1026, 891);
        post.beginScene(scene, camera, null, settings, portraitTarget);
        assert.equal(post.target, world); assert.equal(post.pipeline, pipeline);
        assert.deepEqual([world.width, world.height], [513, 446]);
        for (const [node, target, texture] of originals) {
            node.setSize(1026, 891);
            assert.equal(node.renderTarget, target); assert.equal(target.texture, texture);
            assert.equal(target.depthBuffer, false); assert.deepEqual([target.width, target.height], [1026, 891]);
        }
        assert.equal(sun.shadow.map, shadowTarget); assert.equal(shadowTarget.depthBuffer, true);
        assert.equal(post.outputTarget, portraitTarget); assert.equal(portraitTarget.depthBuffer, true);
        post.active = false; post.dispose();
        assert.equal(unrelatedDisposals, 0);
    } finally {
        shadowTarget.dispose(); portraitTarget.dispose();
        if (savedImage) globalThis.Image = savedImage; else delete globalThis.Image;
    }
});

test('quality changes dispose HDR resources and rebuild only the requested effects', () => {
    const { post, camera } = harness();
    post.beginScene(null, camera, null, { ambientOcclusion: true, screenSpaceReflections: true });
    const old = post.target;
    let disposals = 0;
    old.addEventListener('dispose', () => disposals++);
    post.active = false;
    post.beginScene(null, camera, null, { antiAliasing: 'Msaa', multiSampleCount: 4 });
    assert.equal(disposals, 1);
    assert.notEqual(post.target, old);
    assert.equal(post.target.samples, 4);
    assert.equal(post.target.textures.length, 1);
    assert.equal(post.mrt, null);
    assert.equal(post.effects.length, 0);
    post.active = false;
    post.dispose();
});

test('post composition shares the raw encoded output path used by the following native HUD', () => {
    const { post, camera, calls } = harness();
    post.beginScene(null, camera, null, {});
    post.compose();
    const draw = calls.find(call => call[0] === 'quad');
    assert.equal(draw[2], THREE.LinearSRGBColorSpace);
    assert.equal(post.renderer.outputColorSpace, THREE.LinearSRGBColorSpace);
    assert.equal(draw[1].material.depthTest, false);
    assert.equal(draw[1].material.depthWrite, false);
    assert.equal(draw[1].material.toneMapped, false);
    post.dispose();
});

test('FXAA and SMAA construct distinct official pipelines', () => {
    const { post, camera } = harness();
    post.beginScene(null, camera, null, { antiAliasing: 'Fxaa' });
    assert.equal(post.effects[0].constructor.name, 'FXAANode');
    post.active = false;
    const browserImage = globalThis.Image;
    globalThis.Image = class { set src(value) { this.source = value; } };
    try {
        post.beginScene(null, camera, null, { antiAliasing: 'Smaa' });
        assert.equal(post.effects[0].constructor.name, 'SMAANode');
        post.active = false;
        post.dispose();
    } finally {
        if (browserImage) globalThis.Image = browserImage;
        else delete globalThis.Image;
    }
});

test('SMAA lookup images use same-origin URLs and retain the exact official PNG bytes', async () => {
    const original = await readFile(new URL('../node_modules/three/examples/jsm/tsl/display/SMAANode.js', import.meta.url), 'utf8');
    const vendored = await readFile(new URL('../wwwroot/vendor/three/addons/tsl/display/SMAANode.js', import.meta.url), 'utf8');
    assert.ok(!vendored.includes('data:image/'));
    for (const kind of ['Area', 'Search']) {
        const embedded = original.match(new RegExp(`_get${kind}Texture\\(\\)\\s*\\{\\s*return 'data:image/png;base64,([^']+)'`));
        assert.ok(embedded, `official ${kind} lookup was not found`);
        const filename = `smaa-${kind.toLowerCase()}.png`;
        assert.ok(vendored.includes(`new URL('./${filename}', import.meta.url).href`));
        const actual = await readFile(new URL(`../wwwroot/vendor/three/addons/tsl/display/${filename}`, import.meta.url));
        assert.deepEqual(actual, Buffer.from(embedded[1], 'base64'));
        assert.deepEqual([...actual.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
    }
});

test('the actual SSR tracing and cached AO materials build WGSL for the authored surfaces', () => {
    const { post, camera } = harness();
    post.beginScene(null, camera, null, { screenSpaceReflections: true, ambientOcclusion: true });
    const renderer = new THREE.WebGPURenderer({ canvas: {
        width: 1280, height: 720, style: {}, addEventListener() {}, removeEventListener() {}
    } });
    renderer.hasFeature = () => false;
    renderer._bindings = undefined;
    post.pipeline.renderer = renderer;
    const build = quad => {
        const builder = renderer.backend.createNodeBuilder(quad, renderer);
        builder.camera = quad.camera;
        builder.scene = new THREE.Scene();
        // This checks the real TSL-to-WGSL build without creating a device or native bind groups.
        builder.sortBindingGroups = () => {};
        builder.build();
        return builder.fragmentShader;
    };
    assert.ok(build(post.pipeline.quad).length > 1000);
    const reflection = post.effects.find(effect => effect.constructor.name === 'SSRNode');
    assert.ok(build(new THREE.QuadMesh(reflection._ssrMaterial)).length > 1000);
    assert.ok(build(post.aoCache._quadMesh).length > 1000);
    post.active = false;
    post.dispose();
});

test('bloom and AA reuse one denoised AO cache at the unchanged AO resolution', context => {
    const browserImage = globalThis.Image;
    globalThis.Image = class { set src(value) { this.source = value; } };
    context.after(() => {
        if (browserImage) globalThis.Image = browserImage;
        else delete globalThis.Image;
    });
    const { post, camera } = harness();
    const settings = { quality: 'Medium', ambientOcclusion: true, bloom: 'High', antiAliasing: 'Smaa', sharpening: 0.4 };
    post.beginFrame(1280, 720);
    post.beginScene(null, camera, null, settings);
    const cache = post.aoCache;
    assert.match(cache.name, /^[A-Za-z_]\w*$/);
    const occlusion = post.effects.find(effect => effect.constructor.name === 'GTAONode');
    const filtered = post.effects.find(effect => effect.constructor.name === 'DenoiseNode');
    assert.equal(cache.node, filtered);
    assert.equal(filtered.textureNode.node, occlusion);
    assert.equal(filtered.textureNode.getResolutionScale(), 1);
    assert.equal(cache.updateBeforeType, 'frame');
    assert.equal(cache.autoUpdate, true);
    assert.equal(cache.renderTarget.width, 640);
    assert.equal(cache.renderTarget.height, 360);
    assert.equal(cache.renderTarget.depthBuffer, false);
    assert.equal(occlusion.resolutionScale, 0.5);
    assert.equal(occlusion.samples.value, 8);
    assert.equal(filtered.radius.value, 3);
    assert.equal(filtered.depthPhi.value, 40);
    let disposals = 0;
    cache.renderTarget.addEventListener('dispose', () => disposals++);
    post.active = false;
    post.beginFrame(1279, 719);
    post.beginScene(null, camera, null, settings);
    assert.equal(post.aoCache, cache);
    assert.equal(cache.renderTarget.width, Math.round(1279 * occlusion.resolutionScale));
    assert.equal(cache.renderTarget.height, Math.round(719 * occlusion.resolutionScale));
    post.active = false;
    post.beginScene(null, camera, null, { ...settings, renderScale: 0.75 });
    assert.equal(disposals, 1);
    assert.equal(post.aoCache.renderTarget.width, Math.round(1279 * 0.75 * 0.5));
    assert.equal(post.aoCache.renderTarget.height, Math.round(719 * 0.75 * 0.5));
    assert.equal(post.options.ambientOcclusion, true);
    assert.equal(post.options.bloom, 'high');
    assert.equal(post.options.antiAliasing, 'smaa');
    post.active = false;
    post.dispose();
});


test('unspecified generic post graphs retain original nodes and refuse accidental activation', () => {
    const { post, camera } = harness();
    post.beginFrame(1280, 720); post.beginScene(null, camera, null, { renderScale: .5, sharpening: .4 });
    assert.equal(post.internalPostRequested, false); assert.equal(post.internalPostEnabled, false);
    assert.equal(post.internalRTTs.length, 0); assert.equal(post.effective.internalPost, undefined);
    assert.throws(() => post.setInternalPostEnabled(true), /renderer configuration/);
    post.compose(); assert.equal(post.effective.postProcessingWidth, undefined);
    assert.equal(post.setInternalPostEnabled(false), false);
    post.dispose();
});

test('internal post reuses conservative RTT/SMAA targets across hot toggles, odd resizes and supersampling', context => {
    const image = globalThis.Image; globalThis.Image = class {};
    context.after(() => { if (image) globalThis.Image = image; else delete globalThis.Image; });
    const { post, camera, calls } = harness({ internalPost: true });
    const settings = { renderScale: 1, ambientOcclusion: true, screenSpaceReflections: true, bloom: 'High', antiAliasing: 'Smaa', sharpening: .6 };
    post.beginFrame(1281, 721); post.beginScene(null, camera, null, settings);
    const pipeline = post.pipeline, target = post.target, cache = post.aoCache, effects = post.effects.slice();
    const smaa = post.internalSMAA, images = [smaa._areaTexture.image, smaa._searchTexture.image];
    assert.equal(post.internalRTTs.length, 2);
    const targets = post.internalRTTs.map(node => node.renderTarget);
    const full = post.fullPostInput;
    assert.ok(full.isRTTNode); assert.equal(full.getResolutionScale(), 1);
    post.compose();
    for (const [enabled, scale, width, height] of [[true, .75, 1281, 721], [false, .75, 1281, 721],
        [true, .5, 641, 361], [true, 1, 641, 361], [false, 1, 1281, 721], [true, 2, 1281, 721]]) {
        post.setInternalPostEnabled(enabled); post.setDynamicResolutionScale(scale);
        post.beginFrame(width, height); post.beginScene(null, camera, null, scale === 2 ? { ...settings, renderScale: 2 } : settings);
        const expected = enabled ? Math.min(scale, 1) : 1;
        const activeSmaa = post.internalSMAA;
        activeSmaa.setSize(width, height);
        assert.deepEqual([activeSmaa._renderTargetBlend.width, activeSmaa._renderTargetBlend.height], [Math.floor(width * expected), Math.floor(height * expected)]);
        for (const node of post.internalRTTs) assert.equal(node.getResolutionScale(), expected);
        assert.equal(full.getResolutionScale(), 1, 'display tone/upscale and RCAS input stays full sized');
        if (scale !== 2) {
            assert.equal(post.pipeline, pipeline); assert.equal(post.target, target); assert.equal(post.aoCache, cache);
            assert.deepEqual(post.effects, effects); assert.deepEqual(post.internalRTTs.map(node => node.renderTarget), targets);
            assert.deepEqual([smaa._areaTexture.image, smaa._searchTexture.image], images);
        }
        assert.throws(() => post.setInternalPostEnabled(!enabled), /between composed frames/);
        post.compose();
        assert.equal(post.effective.internalPost, enabled);
        assert.deepEqual([post.effective.postProcessingWidth, post.effective.postProcessingHeight],
            [Math.floor(width * expected), Math.floor(height * expected)]);
        assert.deepEqual(calls.filter(call => call[0] === 'viewport').at(-1), ['viewport', 0, 0, width, height]);
    }
    for (const invalid of [null, 1, 'true', undefined]) assert.throws(() => post.setInternalPostEnabled(invalid), TypeError);
    post.dispose(); assert.equal(post.internalRTTs.length, 0); assert.equal(post.internalSMAA, null);
});


test('internal post policy accepts only explicit booleans or auto and leaves unspecified generic callers disabled', () => {
    for (const mode of [false, true, 'auto']) assert.equal(internalPostMode(mode), mode);
    assert.equal(internalPostMode(undefined), false);
    for (const mode of [null, 0, 1, 'true', 'false', '', {}, []]) assert.throws(() => internalPostMode(mode), TypeError);
});

test('immutable fused sharpening keeps the full tone input and reports use only after actual composition', () => {
    const { post, camera } = harness({ internalPost: true, fusedSharpen: true });
    const settings = { renderScale: .5, sharpening: .6 };
    post.beginFrame(1280, 720); post.beginScene(null, camera, null, settings);
    const effect = post.effects.at(-1), pipeline = post.pipeline;
    assert.equal(effect.constructor.name, 'FusedSharpenNode');
    assert.equal(post.fullPostInput, effect.textureNode);
    assert.equal(post.fullPostInput.getResolutionScale(), 1);
    assert.equal(post.effective.fusedSharpen, undefined);
    assert.equal(post.setFusedSharpenEnabled, undefined, 'there is no graph mutation control');
    post.compose();
    assert.deepEqual(post.getFusedSharpenProfile(), { requested: true, enabled: true, compositions: 1 });
    post.setDynamicResolutionScale(.75); post.beginFrame(1281, 721); post.beginScene(null, camera, null, settings);
    assert.equal(post.pipeline, pipeline); assert.equal(post.effects.at(-1), effect);
    post.compose();
    assert.equal(post.getFusedSharpenProfile().compositions, 2);
    post.beginScene(null, camera, null, { ...settings, sharpening: 0 }); post.compose();
    assert.deepEqual(post.getFusedSharpenProfile(), { requested: true, enabled: false, compositions: 2 });
    assert.equal(post.effects.length, 0);
    post.dispose();
    const normal = harness();
    normal.post.beginScene(null, normal.camera, null, settings); normal.post.compose();
    assert.equal(normal.post.effects.at(-1).constructor.name, 'SharpenNode');
    assert.deepEqual(normal.post.getFusedSharpenProfile(), { requested: false, enabled: false, compositions: 0 });
    normal.post.dispose();
});
