const supportedVersion = '0.187.0';
const canvasClear = '\t\t\tif ( descriptor.textures === null ) {\n\n\t\t\t\tif ( color ) gl.clearColor( clearColor.r, clearColor.g, clearColor.b, clearColor.a );';

export function patchWebGlCanvasClear(source, version) {
    if (version !== supportedVersion) throw new Error(`Review the WebGL canvas clear patch before upgrading Three ${supportedVersion} to ${version}.`);
    if (source.split(canvasClear).length !== 2) throw new Error('The pinned WebGL canvas clear source pattern must occur exactly once.');
    return source.replace(canvasClear, '\t\t\tif ( descriptor.textures === null ) {\n\n'
        + '\t\t\t\t// Explicit canvas clears must leave the previously bound offscreen framebuffer.\n'
        + '\t\t\t\tif ( setFrameBuffer ) this.state.bindFramebuffer( gl.FRAMEBUFFER, null );\n\n'
        + '\t\t\t\tif ( color ) gl.clearColor( clearColor.r, clearColor.g, clearColor.b, clearColor.a );');
}

const deferredBundle = '\t\trenderContextData.renderBundles.push( this.get( bundle ).bundleGPU );';

export function patchWebGpuBundleOrder(source, version) {
    if (version !== supportedVersion) throw new Error(`Review the WebGPU bundle order patch before upgrading Three ${supportedVersion} to ${version}.`);
    if (source.includes('// Preserve Renderer ordering: opaque bundles') || source.split(deferredBundle).length !== 2) throw new Error('The pinned WebGPU bundle submission source pattern must occur exactly once.');
    return source.replace(deferredBundle,
        '\t\tif ( this._isRenderCameraDepthArray( renderContext ) === true ) {\n\n'
        + '\t\t\t// Layered targets retain the upstream per-layer finalization path.\n'
        + deferredBundle.replace('\t\t', '\t\t\t') + '\n\n'
        + '\t\t} else {\n\n'
        + '\t\t\t// Preserve Renderer ordering: opaque bundles must precede normal transparent draws.\n'
        + '\t\t\trenderContextData.currentPass.executeBundles( [ this.get( bundle ).bundleGPU ] );\n'
        + '\t\t\t// WebGPU executeBundles clears pipeline, bindings and vertex/index buffer state.\n'
        + '\t\t\trenderContextData.currentSets = { attributes: {}, bindingGroups: [], pipeline: null, index: null };\n\n'
        + '\t\t}');
}
