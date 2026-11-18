import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { transformWorkerSmaa } from '../bundle-worker-transform.mjs';

const source = await readFile(new URL('../wwwroot/vendor/three/addons/tsl/display/SMAANode.js', import.meta.url), 'utf8');
test('Worker bundle replaces only pinned SMAA image creation, keeping official shader and texture policy', () => {
    const transformed = transformWorkerSmaa(source, '0.187.0');
    assert.equal((transformed.match(/__noxiusWorkerSmaaLookups/g) ?? []).length, 2);
    assert.ok(!transformed.includes('new Image()')); assert.ok(!transformed.includes('.onload'));
    for (const policy of ['minFilter = LinearFilter', 'magFilter = NearestFilter', 'minFilter = NearestFilter', 'flipY = false']) {
        assert.equal(transformed.split(policy).length, source.split(policy).length);
    }
    const marker = '\t\tthis._invSize = uniform( new Vector2() );';
    const normalized = source.replaceAll('\r\n', '\n');
    assert.equal(transformed.slice(transformed.indexOf(marker)), normalized.slice(normalized.indexOf(marker)));
    assert.ok(source.includes('new Image()'));
});
test('Worker bundle refuses changed versions and ambiguous or changed SMAA source blocks', () => {
    assert.throws(() => transformWorkerSmaa(source, '0.188.0'), /Review/);
    assert.throws(() => transformWorkerSmaa(source.replace('new Image()', 'new Image(1)'), '0.187.0'), /changed/);
    assert.throws(() => transformWorkerSmaa(source + '\n\t\tconst scope = this;\n', '0.187.0'), /changed/);
});
