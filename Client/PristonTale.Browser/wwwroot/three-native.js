import { ExternalCamera } from './three-camera.js';
import { Fn, If, attribute, materialColor, mrt, neutralToneMapping, sRGBTransferOETF, uniform, vec4 } from './vendor/three/three.tsl.js';

// Exact MonoGame blend tuples (colour source/destination/equation, then alpha).
// The last number selects the auxiliary RGB; auxiliary alpha is always zero.
const auxiliaryBlendStates = [
    [0, 5, 0, 0, 5, 0, 0], [4, 5, 0, 4, 5, 0, 0], [4, 0, 0, 4, 0, 0, 0],
    [1, 2, 0, 1, 0, 0, 1], [1, 3, 0, 1, 0, 0, 0], [0, 0, 0, 1, 0, 0, 0],
    [2, 3, 0, 0, 5, 0, 0], [1, 2, 0, 0, 5, 0, 1], [2, 0, 0, 0, 5, 0, 0], [1, 3, 0, 0, 5, 0, 0]
];

function auxiliaryBlend(rawBlend, blend) {
    if (!rawBlend) return blend === 'opaque' ? -1 : 0;
    for (const state of auxiliaryBlendStates) {
        let same = true;
        for (let index = 0; index < 6; index++) if (rawBlend[index] !== state[index]) { same = false; break; }
        if (same) return state[6];
    }
    return -1;
}

export function createNativeBatches(THREE, renderer, texture, toWebGlDepth) {
    const scene = new THREE.Scene();
    const camera = new ExternalCamera(renderer.coordinateSystem);
    const batches = [];
    let next = 0;
    const linearColor = new THREE.Color();
    const blendFactors = [THREE.OneFactor, THREE.ZeroFactor, THREE.SrcColorFactor, THREE.OneMinusSrcColorFactor,
        THREE.SrcAlphaFactor, THREE.OneMinusSrcAlphaFactor, THREE.DstColorFactor, THREE.OneMinusDstColorFactor,
        THREE.DstAlphaFactor, THREE.OneMinusDstAlphaFactor, THREE.ConstantColorFactor, THREE.OneMinusConstantColorFactor,
        THREE.SrcAlphaSaturateFactor];
    const blendEquations = [THREE.AddEquation, THREE.SubtractEquation, THREE.ReverseSubtractEquation, THREE.MinEquation, THREE.MaxEquation];
    const depthFunctions = [THREE.AlwaysDepth, THREE.NeverDepth, THREE.LessDepth, THREE.LessEqualDepth,
        THREE.EqualDepth, THREE.LessEqualDepth, THREE.LessDepth, THREE.NotEqualDepth];
    const reversedDepth = new THREE.Matrix4().set(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 1, 0, 0, 0, 1);
    const defaultSampler = [1, 1, 0, 1];
    const addressModes = [THREE.RepeatWrapping, THREE.ClampToEdgeWrapping, THREE.MirroredRepeatWrapping, THREE.ClampToEdgeWrapping];

    function create() {
        const material = new THREE.MeshBasicNodeMaterial({ side: THREE.DoubleSide, toneMapped: false });
        material.forceSinglePass = true;
        const pixel = { kind: uniform(1), tint: uniform(new THREE.Vector4(1, 1, 1, 1)), exposure: uniform(1),
            display: uniform(1), mode: uniform(0), hdr: uniform(0) };
        const clip = uniform(new THREE.Matrix4());
        // This is an already combined MVP, not an affine model matrix. Keep its
        // homogeneous W through the vertex stage for perspective effects.
        material.vertexNode = clip.mul(vec4(attribute('position', 'vec3'), 1));
        material.fragmentNode = Fn(() => {
            const source = materialColor.mul(attribute('color', 'vec4'));
            const rgb = source.rgb.toVar();
            const alpha = source.a.toVar();
            If(pixel.kind.equal(2), () => {
                rgb.mulAssign(pixel.tint.rgb);
                alpha.mulAssign(pixel.tint.a);
            });
            If(pixel.kind.equal(3), () => {
                If(pixel.mode.greaterThan(0.5).and(pixel.mode.lessThan(2.5)), () => {
                    rgb.assign(source.rgb.saturate());
                    alpha.assign(1);
                }).ElseIf(pixel.mode.greaterThan(2.5), () => {
                    rgb.mulAssign(source.rgb.saturate());
                    alpha.assign(1);
                });
            });
            If(pixel.kind.greaterThan(1.5).and(pixel.kind.equal(2).or(pixel.mode.lessThan(0.5)).or(pixel.mode.greaterThan(2.5))), () => {
                If(pixel.hdr.greaterThan(0.5).or(pixel.display.lessThan(0.5)), () => {
                    rgb.divAssign(pixel.exposure.max(0.00001));
                }).Else(() => {
                    rgb.assign(sRGBTransferOETF(neutralToneMapping(rgb, 1)));
                });
            });
            return vec4(rgb.min(60000), alpha);
        })();
        const fragment = material.fragmentNode;
        // A custom fragment bypasses automatic MRT output. These neutral values
        // preserve the world normal/roughness under the exact native blend.
        const mrtFragments = [mrt({ output: fragment, normalRoughness: vec4(0) }),
            mrt({ output: fragment, normalRoughness: vec4(1, 1, 1, 0) })];
        material.transparent = true;
        material.blending = THREE.CustomBlending;
        material.blendEquation = THREE.AddEquation;
        material.blendSrc = THREE.OneFactor;
        material.blendDst = THREE.OneMinusSrcAlphaFactor;
        material.blendEquationAlpha = THREE.AddEquation;
        material.blendSrcAlpha = THREE.OneFactor;
        material.blendDstAlpha = THREE.OneMinusSrcAlphaFactor;
        const mesh = new THREE.Mesh(new THREE.BufferGeometry(), material);
        mesh.matrixAutoUpdate = false;
        mesh.matrixWorldAutoUpdate = false;
        mesh.frustumCulled = false;
        return { mesh, pixel, clip, fragment, mrtFragments, mrtMode: -1, mrtNeutral: 0,
            shaderKind: 1, textureId: 0, capacity: 0, indexCapacity: 0, scissor: new Int32Array(4), viewport: new Int32Array(4),
            sampler: new Int32Array(4), clipped: false, hasViewport: false };
    }

    function prepareState(batch, blend, depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare) {
        const material = batch.mesh.material;
        material.depthTest = !!depthTest;
        material.depthWrite = !!depthWrite;
        material.depthFunc = depthFunctions[depthCompare] ?? THREE.LessEqualDepth;
        material.blending = blend === 'opaque' ? THREE.NoBlending : THREE.CustomBlending;
        material.blendSrc = THREE.OneFactor;
        material.blendDst = blend === 'additive' ? THREE.OneFactor : THREE.OneMinusSrcAlphaFactor;
        material.blendEquation = THREE.AddEquation;
        material.blendSrcAlpha = THREE.OneFactor;
        material.blendDstAlpha = THREE.OneMinusSrcAlphaFactor;
        material.blendEquationAlpha = THREE.AddEquation;
        if (rawBlend) {
            if (rawBlend.length !== 6) throw new Error('Invalid native blend state.');
            for (let index = 0; index < 6; index++) {
                if (index % 3 === 2 ? !blendEquations[rawBlend[index]] : !blendFactors[rawBlend[index]]) {
                    throw new Error('Unsupported native blend state.');
                }
            }
            material.blending = THREE.CustomBlending;
            material.blendSrc = blendFactors[rawBlend[0]];
            material.blendDst = blendFactors[rawBlend[1]];
            material.blendEquation = blendEquations[rawBlend[2]];
            material.blendSrcAlpha = blendFactors[rawBlend[3]];
            material.blendDstAlpha = blendFactors[rawBlend[4]];
            material.blendEquationAlpha = blendEquations[rawBlend[5]];
        }
        if (blendFactor) {
            material.blendColor.fromArray(blendFactor);
            material.blendAlpha = blendFactor[3];
        }
        batch.clipped = !!scissor;
        if (scissor) batch.scissor.set(scissor);
        batch.hasViewport = !!viewport;
        if (viewport) batch.viewport.set(viewport);
    }

    function prepare(positions, uvs, colors, indices, textureId, matrix, blend, depthTest, depthWrite, scissor, viewport,
        colorsAreSrgb, rawBlend, blendFactor, depthCompare, sampler, shaderKind, pixelParams) {
        if (positions.length % 3 || matrix.length !== 16) throw new Error('Invalid native triangle geometry.');
        const count = positions.length / 3;
        if (uvs?.length && uvs.length !== count * 2 || colors?.length && colors.length !== count * 4) {
            throw new Error('Native vertex channels have different lengths.');
        }
        const batch = batches[next] ??= create();
        next++;
        const mesh = batch.mesh;
        const indexCount = indices?.length ?? 0;
        if (batch.capacity < count || batch.indexCapacity < indexCount) {
            batch.capacity = Math.max(count, batch.capacity * 2, 4);
            batch.indexCapacity = Math.max(indexCount, batch.indexCapacity * 2, 6);
            mesh.geometry.dispose();
            const geometry = mesh.geometry = new THREE.BufferGeometry();
            geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(batch.capacity * 3), 3).setUsage(THREE.DynamicDrawUsage));
            geometry.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(batch.capacity * 2), 2).setUsage(THREE.DynamicDrawUsage));
            geometry.setAttribute('color', new THREE.BufferAttribute(new Float32Array(batch.capacity * 4), 4).setUsage(THREE.DynamicDrawUsage));
            batch.index = new THREE.BufferAttribute(new Uint32Array(batch.indexCapacity), 1).setUsage(THREE.DynamicDrawUsage);
        }
        const geometry = mesh.geometry;
        const position = geometry.getAttribute('position');
        position.array.set(positions);
        position.needsUpdate = true;
        const uv = geometry.getAttribute('uv');
        if (uvs?.length) uv.array.set(uvs);
        else uv.array.fill(0, 0, count * 2);
        uv.needsUpdate = true;
        const color = geometry.getAttribute('color');
        if (colors?.length) color.array.set(colors);
        else color.array.fill(1, 0, count * 4);
        if ((shaderKind ?? 1) === 2 && colorsAreSrgb && colors?.length) {
            for (let index = 0; index < count * 4; index += 4) {
                linearColor.setRGB(colors[index], colors[index + 1], colors[index + 2], THREE.SRGBColorSpace);
                color.array[index] = linearColor.r;
                color.array[index + 1] = linearColor.g;
                color.array[index + 2] = linearColor.b;
            }
        }
        color.needsUpdate = true;
        if (indexCount) {
            geometry.setIndex(batch.index);
            batch.index.array.set(indices);
            batch.index.needsUpdate = true;
        } else {
            geometry.setIndex(null);
        }
        geometry.setDrawRange(0, indexCount || count);
        batch.clip.value.fromArray(matrix);
        if (depthCompare === 5 || depthCompare === 6) batch.clip.value.premultiply(reversedDepth);
        if (renderer.coordinateSystem === THREE.WebGLCoordinateSystem) batch.clip.value.premultiply(toWebGlDepth);
        prepareState(batch, blend, depthTest, depthWrite, scissor, viewport, rawBlend, blendFactor, depthCompare);
        batch.mrtNeutral = auxiliaryBlend(rawBlend, blend);
        batch.textureId = textureId;
        batch.sampler.set(sampler ?? defaultSampler);
        batch.shaderKind = shaderKind ?? 1;
        batch.pixel.kind.value = batch.shaderKind;
        if (batch.shaderKind === 2 && pixelParams) {
            batch.pixel.tint.value.fromArray(pixelParams);
            batch.pixel.exposure.value = pixelParams[4];
            batch.pixel.display.value = pixelParams[5];
            batch.pixel.mode.value = 0;
        } else if (batch.shaderKind === 3 && pixelParams) {
            batch.pixel.exposure.value = pixelParams[0];
            batch.pixel.display.value = pixelParams[1];
            batch.pixel.mode.value = pixelParams[3];
        }
        return batch;
    }

    function sameClip(a, b) {
        if (a.clipped !== b.clipped) return false;
        for (let index = 0; index < 4; index++) if (a.sampler[index] !== b.sampler[index]) return false;
        if (a.hasViewport !== b.hasViewport) return false;
        if (a.hasViewport) for (let index = 0; index < 4; index++) if (a.viewport[index] !== b.viewport[index]) return false;
        if (!a.clipped) return true;
        for (let index = 0; index < 4; index++) if (a.scissor[index] !== b.scissor[index]) return false;
        return true;
    }

    function setClip(first, scale = 1) {
        const target = renderer.getRenderTarget?.();
        if (first.hasViewport) {
            renderer.setViewport(first.viewport[0] * scale, first.viewport[1] * scale, first.viewport[2] * scale, first.viewport[3] * scale);
            if (target) target.viewport.set(first.viewport[0] * scale, first.viewport[1] * scale, first.viewport[2] * scale, first.viewport[3] * scale);
        }
        renderer.setScissorTest(first.clipped);
        if (target) target.scissorTest = first.clipped;
        if (first.clipped) {
            renderer.setScissor(first.scissor[0] * scale, first.scissor[1] * scale, first.scissor[2] * scale, first.scissor[3] * scale);
            if (target) target.scissor.set(first.scissor[0] * scale, first.scissor[1] * scale, first.scissor[2] * scale, first.scissor[3] * scale);
        }
    }

    function render(commands, start, end, hdr = false, scale = 1) {
        const attachments = renderer.getRenderTarget?.()?.textures;
        let hasMrt = false;
        if (attachments) for (let index = 0; index < attachments.length; index++) {
            if (attachments[index].name === 'normalRoughness') { hasMrt = true; break; }
        }
        if (hasMrt) for (let index = start; index < end; index++) {
            if (commands[index].batch.mrtNeutral < 0) throw new Error('Unsupported native blend for world normal/roughness target.');
        }
        scene.clear();
        setClip(commands[start].batch, scale);
        for (let index = start; index < end; index++) {
            const batch = commands[index].batch;
            const mrtMode = hasMrt ? batch.mrtNeutral : -1;
            if (mrtMode !== batch.mrtMode) {
                batch.mrtMode = mrtMode;
                batch.mesh.material.fragmentNode = hasMrt ? batch.mrtFragments[mrtMode] : batch.fragment;
                batch.mesh.material.needsUpdate = true;
            }
            batch.pixel.hdr.value = hdr ? 1 : 0;
            const map = texture(batch.textureId);
            if (map) {
                const sampler = batch.sampler;
                const wrapS = addressModes[sampler[0]];
                const wrapT = addressModes[sampler[1]];
                const min = sampler[2] === 1 || sampler[2] === 4 || sampler[2] >= 7 ? THREE.NearestFilter : THREE.LinearFilter;
                const mag = sampler[2] === 1 || sampler[2] === 4 || sampler[2] === 5 || sampler[2] === 6 ? THREE.NearestFilter : THREE.LinearFilter;
                const anisotropy = sampler[2] === 2 ? Math.max(1, Math.min(sampler[3], renderer.getMaxAnisotropy?.() ?? 1)) : 1;
                if (map.wrapS !== wrapS || map.wrapT !== wrapT || map.minFilter !== min || map.magFilter !== mag || map.anisotropy !== anisotropy) {
                    map.wrapS = wrapS;
                    map.wrapT = wrapT;
                    map.minFilter = min;
                    map.magFilter = mag;
                    map.anisotropy = anisotropy;
                    if (map.isRenderTargetTexture) {
                        // A target version bump recreates its GPU image, invalidating the cached framebuffer attachment.
                        // WebGPU updates samplers independently; WebGL needs only its texture parameters refreshed.
                        const backend = renderer.backend;
                        if (backend?.isWebGLBackend) {
                            const data = backend.get(map);
                            if (data.textureGPU) {
                                backend.state.bindTexture(data.glTextureType, data.textureGPU);
                                backend.textureUtils.setTextureParameters(data.glTextureType, map);
                            }
                        }
                    } else {
                        map.needsUpdate = true;
                    }
                }
            }
            if (!!batch.mesh.material.map !== !!map) batch.mesh.material.needsUpdate = true;
            batch.mesh.material.map = map;
            batch.mesh.renderOrder = index - start;
            scene.add(batch.mesh);
        }
        renderer.render(scene, camera);
    }

    return {
        beginFrame() { next = 0; },
        prepare,
        prepareState,
        setClip,
        sameClip,
        render,
        dispose() {
            for (const batch of batches) {
                batch.mesh.geometry.dispose();
                batch.mesh.material.dispose();
            }
        }
    };
}
