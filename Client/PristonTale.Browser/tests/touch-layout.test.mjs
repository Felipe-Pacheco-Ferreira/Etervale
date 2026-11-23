import test from 'node:test';
import assert from 'node:assert/strict';
import { layoutParts, maximumSize, maximumTransparency, minimumSize, placement, readLayout, writeLayout } from '../wwwroot/touch-layout.js';

test('a placement keeps a button within its size and transparency, and a frame never scales or fades', () => {
    assert.deepEqual(placement({ x: 0.1, y: -0.2, size: 500, transparency: 99 }, true), { x: 0.1, y: -0.2, size: maximumSize, transparency: maximumTransparency });
    assert.deepEqual(placement({ size: 10, transparency: -5 }, true), { x: 0, y: 0, size: minimumSize, transparency: 0 });
    assert.deepEqual(placement({ x: 0.3, y: 0.4, size: 150, transparency: 50 }, false), { x: 0.3, y: 0.4, size: 100, transparency: 0 });
    assert.deepEqual(placement({ x: 9, y: Number.NaN }, false), { x: 2, y: 0, size: 100, transparency: 0 });
});

test('the saved layout reads every part back, drops unknown ones and survives broken text', () => {
    const text = writeLayout({ player: { x: 0.05, y: 0.1, size: 100, transparency: 0 }, 'skill-2': { x: -0.1, y: 0, size: 120, transparency: 30 } });
    const layout = readLayout(text.replace('"parts":{', '"parts":{"ghost":{"x":1},'));
    assert.deepEqual(Object.keys(layout), layoutParts.map(part => part.key));
    assert.deepEqual(layout.player, { x: 0.05, y: 0.1, size: 100, transparency: 0 });
    assert.deepEqual(layout['skill-2'], { x: -0.1, y: 0, size: 120, transparency: 30 });
    assert.deepEqual(readLayout('{oops').attack, { x: 0, y: 0, size: 100, transparency: 0 });
    assert.equal(new Set(layoutParts.map(part => part.key)).size, layoutParts.length, 'two parts share a key');
});
