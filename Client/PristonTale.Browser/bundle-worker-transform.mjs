import { workerSmaaLookupDimensions } from './wwwroot/three-worker-assets.js';

const supportedThreeVersion = '0.187.0';

function replaceOnce(source, before, after, label) {
    const first = source.indexOf(before);
    if (first < 0 || source.indexOf(before, first + before.length) >= 0) throw new Error(`Pinned SMAA ${label} changed.`);
    return source.slice(0, first) + after + source.slice(first + before.length);
}

export function transformWorkerSmaa(source, version) {
    if (version !== supportedThreeVersion) throw new Error('Review the Worker SMAA transform before changing Three versions.');
    source = source.replaceAll('\r\n', '\n');
    source = replaceOnce(source, '\t\tconst scope = this;\n', '', 'scope');
    for (const [name, [width, height]] of Object.entries(workerSmaaLookupDimensions)) {
        const capitalized = name[0].toUpperCase() + name.slice(1);
        const before = `\t\tconst ${name}TextureImage = new Image();\n\t\t${name}TextureImage.src = this._get${capitalized}Texture();\n\t\t${name}TextureImage.onload = function () {\n\n\t\t\t// assigning data to HTMLImageElement.src is asynchronous (see #15162)\n\t\t\tscope._${name}Texture.needsUpdate = true;\n\n\t\t};`;
        const after = `\t\tconst ${name}TextureImage = globalThis.__noxiusWorkerSmaaLookups?.${name};\n\t\tif ( ! ${name}TextureImage || ${name}TextureImage.width !== ${width} || ${name}TextureImage.height !== ${height} ) {\n\n\t\t\tthrow new Error( 'Missing preloaded Worker SMAA ${name} lookup.' );\n\n\t\t}`;
        source = replaceOnce(source, before, after, `${name} image constructor`);
        source = replaceOnce(source, `\t\tthis._${name}Texture.image = ${name}TextureImage;`,
            `\t\tthis._${name}Texture.image = ${name}TextureImage;\n\t\tthis._${name}Texture.needsUpdate = true;`, `${name} upload`);
    }
    return source;
}
