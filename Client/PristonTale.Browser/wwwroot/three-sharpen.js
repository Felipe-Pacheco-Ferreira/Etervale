/*!
 * RCAS arithmetic adapted from Three.js r187 SharpenNode.
 *
 * The MIT License
 * Copyright © 2010-2026 three.js authors
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
import { Node } from './vendor/three/three.webgpu.js';
import { Fn, float, vec3, vec4, ivec2, int, uv, floor, abs, max, min, exp2, nodeObject,
    textureSize, textureLoad, convertToTexture, packHalf2x16, unpackHalf2x16 } from './vendor/three/three.tsl.js';

class FusedSharpenNode extends Node {
    static get type() { return 'NoxiusFusedSharpenNode'; }

    constructor(input, sharpness, denoise) {
        super('vec4');
        this.textureNode = convertToTexture(input);
        this.sharpness = nodeObject(sharpness);
        this.denoise = nodeObject(denoise);
    }

    setup(builder) {
        const textureNode = this.textureNode;
        const inputTex = textureNode.value;
        const rcas = Fn(() => {
            const targetUV = uv();
            const texSize = textureSize(textureLoad(inputTex));
            const p = ivec2(int(floor(targetUV.x.mul(texSize.x))), int(floor(targetUV.y.mul(texSize.y)))).toConst();
            const e = textureLoad(inputTex, p);
            const b = textureLoad(inputTex, p.add(ivec2(0, -1)));
            const d = textureLoad(inputTex, p.add(ivec2(-1, 0)));
            const f = textureLoad(inputTex, p.add(ivec2(1, 0)));
            const h = textureLoad(inputTex, p.add(ivec2(0, 1)));
            const luma = value => value.g.add(value.b.add(value.r).mul(0.5));
            const bL = luma(b), dL = luma(d), eL = luma(e), fL = luma(f), hL = luma(h);
            const con = exp2(this.sharpness.negate()).toConst();
            const mn4 = min(min(b.rgb, d.rgb), min(f.rgb, h.rgb)).toConst();
            const mx4 = max(max(b.rgb, d.rgb), max(f.rgb, h.rgb)).toConst();
            const limit = float(0.25 - 1.0 / 16.0).toConst();
            const hitMin = min(mn4, e.rgb).div(mx4.mul(4.0)).toConst();
            const hitMax = vec3(1.0).sub(max(mx4, e.rgb)).div(mn4.mul(4.0).sub(4.0)).toConst();
            const lobeRGB = max(hitMin.negate(), hitMax).toConst();
            const lobe = max(limit.negate(), min(max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b)), float(0.0))).mul(con).toConst();
            const nz = bL.add(dL).add(fL).add(hL).mul(0.25).sub(eL).toConst();
            const nzRange = max(max(bL, dL), max(eL, max(fL, hL))).sub(min(min(bL, dL), min(eL, min(fL, hL)))).toConst();
            const nzFactor = float(1.0).sub(abs(nz).div(max(nzRange, float(1.0 / 65536.0))).saturate().mul(0.5)).toConst();
            const effectiveLobe = this.denoise.equal(true).select(lobe.mul(nzFactor), lobe).toConst();
            const result = b.rgb.add(d.rgb).add(f.rgb).add(h.rgb).mul(effectiveLobe).add(e.rgb)
                .div(effectiveLobe.mul(4.0).add(1.0)).toConst();
            const rgba = vec4(result, e.a).toConst();
            // Preserve the removed RGBA16F target's store rounding before display conversion.
            return vec4(unpackHalf2x16(packHalf2x16(rgba.rg)), unpackHalf2x16(packHalf2x16(rgba.ba)));
        });
        builder.getNodeProperties(this).textureNode = textureNode;
        return rcas();
    }
}

export const fusedSharpen = (input, sharpness = 0.2, denoise = false) => new FusedSharpenNode(input, sharpness, denoise);
