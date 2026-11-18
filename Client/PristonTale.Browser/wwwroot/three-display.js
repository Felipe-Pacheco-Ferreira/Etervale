import * as THREE from './vendor/three/three.webgpu.js';
import { neutralToneMapping, sRGBTransferOETF, texture, uniform, vec4 } from './vendor/three/three.tsl.js';

export function createNativeDisplayEncoder(renderer) {
    return new NativeDisplayEncoder(renderer);
}

class NativeDisplayEncoder {
    constructor(renderer) {
        this.renderer = renderer;
        this.target = new THREE.RenderTarget(1, 1, { type: THREE.HalfFloatType, depthBuffer: true, stencilBuffer: false });
        this.target.texture.name = 'NoxiusPortraitRadiance';
        this.target.texture.colorSpace = THREE.LinearSRGBColorSpace;
        this.exposure = uniform(1);
        const color = texture(this.target.texture);
        // Transparent HDR composition is premultiplied; the display transform acts on its straight radiance.
        const radiance = color.rgb.div(color.a.max(0.000001)).max(0);
        const encoded = sRGBTransferOETF(neutralToneMapping(radiance, this.exposure).saturate());
        const material = new THREE.NodeMaterial();
        material.name = 'Noxius portrait display encoding';
        material.fragmentNode = vec4(encoded.mul(color.a), color.a);
        material.toneMapped = false;
        material.depthTest = false;
        material.depthWrite = false;
        material.transparent = true;
        material.blending = THREE.CustomBlending;
        material.blendEquation = THREE.AddEquation;
        material.blendSrc = THREE.OneFactor;
        material.blendDst = THREE.OneMinusSrcAlphaFactor;
        material.blendEquationAlpha = THREE.AddEquation;
        material.blendSrcAlpha = THREE.OneFactor;
        material.blendDstAlpha = THREE.OneMinusSrcAlphaFactor;
        this.quad = new THREE.QuadMesh(material);
        this.canvasViewport = new THREE.Vector4();
        this.canvasScissor = new THREE.Vector4();
        this.outputViewport = new THREE.Vector4();
        this.outputScissor = new THREE.Vector4();
        this.clearColor = new THREE.Color();
        this.active = false;
    }

    begin(outputTarget, width, height) {
        if (this.active) throw new Error('Portrait display encoding is already active.');
        const renderer = this.renderer;
        this.outputTarget = outputTarget;
        this.previousTarget = renderer.getRenderTarget();
        this.previousMrt = renderer.getMRT();
        renderer.getViewport(this.canvasViewport);
        renderer.getScissor(this.canvasScissor);
        this.canvasScissorTest = renderer.getScissorTest();
        renderer.getClearColor(this.clearColor);
        this.clearAlpha = renderer.getClearAlpha();
        if (outputTarget) {
            this.outputViewport.copy(outputTarget.viewport);
            this.outputScissor.copy(outputTarget.scissor);
            this.outputScissorTest = outputTarget.scissorTest;
        } else {
            this.outputViewport.copy(this.canvasViewport);
            this.outputScissor.copy(this.canvasScissor);
            this.outputScissorTest = this.canvasScissorTest;
        }
        this.target.setSize(Math.max(1, Math.floor(width)), Math.max(1, Math.floor(height)));
        this.target.viewport.set(0, 0, this.target.width, this.target.height);
        this.target.scissorTest = false;
        renderer.setMRT(null);
        renderer.setRenderTarget(this.target);
        renderer.setViewport(0, 0, this.target.width, this.target.height);
        renderer.setScissorTest(false);
        renderer.setClearColor(0, 0);
        renderer.clear(true, true, false);
        this.target.viewport.copy(this.outputViewport);
        this.target.scissor.copy(this.outputScissor);
        this.target.scissorTest = this.outputScissorTest;
        renderer.setViewport(this.outputViewport);
        renderer.setScissor(this.outputScissor);
        renderer.setScissorTest(this.outputScissorTest);
        this.active = true;
        return this.target;
    }

    compose(outputTarget, exposure = 1) {
        if (!this.active || outputTarget !== this.outputTarget) throw new Error('Portrait display target does not match its active pass.');
        const renderer = this.renderer;
        this.exposure.value = Number.isFinite(exposure) && exposure > 0 ? exposure : 1;
        try {
            renderer.setMRT(null);
            renderer.setRenderTarget(outputTarget);
            if (outputTarget) {
                outputTarget.viewport.set(0, 0, outputTarget.width, outputTarget.height);
                outputTarget.scissor.copy(this.outputScissor);
                outputTarget.scissorTest = this.outputScissorTest;
            }
            renderer.setViewport(0, 0, this.target.width, this.target.height);
            renderer.setScissor(this.outputScissor);
            renderer.setScissorTest(this.outputScissorTest);
            this.quad.render(renderer);
        } finally {
            if (outputTarget) {
                outputTarget.viewport.copy(this.outputViewport);
                outputTarget.scissor.copy(this.outputScissor);
                outputTarget.scissorTest = this.outputScissorTest;
            }
            renderer.setRenderTarget(this.previousTarget);
            renderer.setMRT(this.previousMrt);
            renderer.setViewport(this.canvasViewport);
            renderer.setScissor(this.canvasScissor);
            renderer.setScissorTest(this.canvasScissorTest);
            renderer.setClearColor(this.clearColor, this.clearAlpha);
            this.active = false;
        }
    }

    dispose() {
        this.quad.material.dispose();
        this.target.dispose();
    }
}
