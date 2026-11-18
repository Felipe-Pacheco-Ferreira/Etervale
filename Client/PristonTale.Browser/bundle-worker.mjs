import { build, version as esbuildVersion } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { transformWorkerSmaa } from './bundle-worker-transform.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, 'wwwroot');
const output = resolve(root, 'three-worker-bundle.js');
const manifest = JSON.parse(await readFile(resolve(here, 'package.json'), 'utf8'));
const three = JSON.parse(await readFile(resolve(here, 'node_modules/three/package.json'), 'utf8'));
const expectedEsbuild = '0.28.2';
if (esbuildVersion !== expectedEsbuild || manifest.devDependencies.esbuild !== expectedEsbuild
    || three.version !== '0.187.0' || manifest.dependencies.three !== three.version) {
    throw new Error('Review the Worker build before changing its pinned esbuild or Three version.');
}
const entry = await readFile(resolve(root, 'three-worker-entry.js'), 'utf8');
if (!entry.includes('globalThis.__noxiusWorkerSmaaLookups')) throw new Error('Worker entry must initialize the preloaded SMAA resource contract.');
const hash = value => createHash('sha256').update(value).digest('hex');
let smaa;
const result = await build({
    absWorkingDir: root, entryPoints: [resolve(root, 'three-worker-entry.js')], outfile: output,
    bundle: true, format: 'esm', platform: 'browser', target: 'es2022', tsconfigRaw: {}, minify: true,
    sourcemap: false, legalComments: 'external', metafile: true, write: false,
    plugins: [{ name: 'worker-preloaded-smaa', setup(context) {
        context.onLoad({ filter: /[\\/]addons[\\/]tsl[\\/]display[\\/]SMAANode\.js$/ }, async args => {
            if (smaa) throw new Error('SMAA loaded more than once.');
            const original = await readFile(args.path, 'utf8');
            const transformed = transformWorkerSmaa(original, three.version);
            smaa = { sourceSha256: hash(original), transformedSha256: hash(transformed) };
            return { contents: transformed, loader: 'js', resolveDir: dirname(args.path) };
        });
    } }]
});
if (!smaa) throw new Error('The actual Worker dependency graph did not include SMAA.');
for (const value of Object.values(result.metafile.outputs)) {
    if (value.imports.length) throw new Error('The Worker bundle still needs runtime imports.');
}
const inputs = [];
for (const name of Object.keys(result.metafile.inputs).sort()) {
    const file = resolve(root, name);
    const path = relative(root, file).replaceAll('\\', '/');
    if (path.startsWith('../')) throw new Error('A Worker dependency escaped the browser source directory.');
    const data = await readFile(file);
    inputs.push({ path, bytes: data.length, sha256: hash(data) });
}
const outputs = [];
for (const file of result.outputFiles) {
    const path = relative(root, file.path).replaceAll('\\', '/');
    if (!['three-worker-bundle.js', 'three-worker-bundle.js.LEGAL.txt'].includes(path)) throw new Error('Unexpected Worker output.');
    outputs.push({ path, bytes: file.contents.length, sha256: hash(file.contents) });
}
if (!outputs.some(value => value.path.endsWith('.LEGAL.txt'))) throw new Error('The Worker legal notices were not emitted.');
const license = await readFile(resolve(root, 'vendor/three/LICENSE'));
const provenance = { format: 'self-contained-esm', esbuild: esbuildVersion, three: three.version, imports: 0,
    source: 'https://www.npmjs.com/package/three/v/0.187.0', license: { path: 'vendor/three/LICENSE', sha256: hash(license) },
    smaa, inputs, outputs };
for (const file of result.outputFiles) await writeFile(file.path, file.contents);
await writeFile(resolve(root, 'three-worker-bundle.manifest.json'), JSON.stringify(provenance, null, 2) + '\n');
console.info(JSON.stringify({ output: 'wwwroot/three-worker-bundle.js', inputs: inputs.length, imports: 0, outputs }));
