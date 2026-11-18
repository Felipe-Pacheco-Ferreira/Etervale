import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { patchWebGlCanvasClear, patchWebGpuBundleOrder } from './vendor-three-patches.mjs';

const source = new URL('./node_modules/three/', import.meta.url);
const destination = new URL('./wwwroot/vendor/three/', import.meta.url);
const manifest = JSON.parse(await readFile(new URL('./package.json', import.meta.url), 'utf8'));
const installed = JSON.parse(await readFile(new URL('package.json', source), 'utf8'));
if (installed.version !== manifest.dependencies.three) {
    throw new Error(`Expected Three ${manifest.dependencies.three}, found ${installed.version}.`);
}

await mkdir(destination, { recursive: true });
for (const name of ['three.core.js', 'three.tsl.js', 'three.webgpu.js']) {
    const input = new URL(`build/${name}`, source);
    if (name === 'three.webgpu.js') {
        const code = patchWebGpuBundleOrder(patchWebGlCanvasClear(await readFile(input, 'utf8'), installed.version), installed.version);
        await writeFile(new URL(name, destination), code);
    } else {
        await copyFile(input, new URL(name, destination));
    }
}
await copyFile(new URL('LICENSE', source), new URL('LICENSE', destination));
await copyFile(new URL('package.json', source), new URL('package.json', destination));
const addonRoot = new URL('examples/jsm/', source);
const addonDestination = new URL('addons/', destination);
const visited = new Set();
async function selfHostSmaaTextures(code, outputFile) {
    const images = [...code.matchAll(/(\b_get(Area|Search)Texture\(\)\s*\{\s*return\s*)'data:image\/png;base64,([A-Za-z0-9+/=]+)'/g)];
    if (images.length !== 2) throw new Error('The pinned SMAA lookup texture layout changed.');
    for (const [expression, prefix, kind, encoded] of images) {
        const png = Buffer.from(encoded, 'base64');
        if (!png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) {
            throw new Error(`Invalid official SMAA ${kind} PNG.`);
        }
        const filename = `smaa-${kind.toLowerCase()}.png`;
        await writeFile(new URL(filename, outputFile), png);
        // The play page allows same-origin images, so lookup textures cannot retain their embedded data URLs.
        code = code.replace(expression, `${prefix}new URL('./${filename}', import.meta.url).href`);
    }
    return code;
}
async function vendorAddon(relative) {
    if (visited.has(relative)) return;
    visited.add(relative);
    const sourceFile = new URL(relative, addonRoot);
    const outputFile = new URL(relative, addonDestination);
    let code = await readFile(sourceFile, 'utf8');
    const imports = [...code.matchAll(/^\s*(?:import|export)\s+(?:[^'"]*?\s+from\s*)?['"]([^'"]+)['"]/gm)];
    for (const [, specifier] of imports) {
        if (specifier.startsWith('.')) {
            const dependency = path.posix.normalize(path.posix.join(path.posix.dirname(relative), specifier));
            if (dependency.startsWith('../')) throw new Error(`Addon import escaped its root: ${dependency}`);
            await vendorAddon(dependency);
        } else if (specifier === 'three/webgpu' || specifier === 'three/tsl') {
            const build = new URL(specifier.endsWith('/tsl') ? 'three.tsl.js' : 'three.webgpu.js', destination);
            let rewritten = path.relative(path.dirname(fileURLToPath(outputFile)), fileURLToPath(build)).replaceAll(path.sep, '/');
            if (!rewritten.startsWith('.')) rewritten = './' + rewritten;
            code = code.replaceAll(`'${specifier}'`, `'${rewritten}'`).replaceAll(`"${specifier}"`, `"${rewritten}"`);
        } else {
            throw new Error(`Unmapped Three addon import: ${specifier}`);
        }
    }
    await mkdir(new URL('./', outputFile), { recursive: true });
    if (relative === 'tsl/display/SMAANode.js') code = await selfHostSmaaTextures(code, outputFile);
    await writeFile(outputFile, code);
}
for (const name of ['GTAONode', 'SSRNode', 'BloomNode', 'FXAANode', 'SMAANode', 'DenoiseNode', 'SharpenNode']) {
    await vendorAddon(`tsl/display/${name}.js`);
}
console.info(`Vendored Three ${installed.version}: WebGPU with WebGL2 fallback.`);
