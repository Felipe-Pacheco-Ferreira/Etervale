import test from 'node:test';
import assert from 'node:assert/strict';
import { clampZoom, labelFits, mapScale, mapTables, nearestFirst, routeDots } from '../wwwroot/touch-map.js';
import { format } from '../wwwroot/touch-dom.js';

test('the whole field fits the view at zoom 1 and zooming multiplies it, held between 1 and 8', () => {
    assert.equal(mapScale(800, 400, 2000, 1000, 1), 0.4);
    assert.equal(mapScale(800, 400, 1000, 2000, 1), 0.2, 'the taller side decides');
    assert.equal(mapScale(800, 400, 2000, 1000, 2.5), 1);
    assert.deepEqual([clampZoom(0.5), clampZoom(3), clampZoom(20)], [1, 3, 8]);
});

test('the route is dotted from the player through every point still ahead, ending on the last', () => {
    const dots = routeDots({ x: 0, y: 0 }, [300, 0, 300, 50], 96);
    assert.deepEqual(dots.slice(0, 3), [{ x: 100, y: 0 }, { x: 200, y: 0 }, { x: 300, y: 0 }]);
    assert.deepEqual(dots.at(-1), { x: 300, y: 50 }, 'a stretch shorter than the spacing still gets its dot');
    assert.deepEqual(routeDots({ x: 5, y: 5 }, []), []);
});

test('the side lists the NPCs nearest first and a name is left out where it would cover another', () => {
    const npcs = [{ name: 'Far', x: 900, y: 0 }, { name: 'Near', x: 10, y: 0 }, { name: 'Mid', x: 0, y: 300 }];
    assert.deepEqual(nearestFirst(npcs, { x: 0, y: 0 }).map(npc => npc.name), ['Near', 'Mid', 'Far']);
    assert.equal(npcs[0].name, 'Far', 'the game\'s list is not reordered');
    const placed = [{ left: 0, right: 50, top: 0, bottom: 10 }];
    assert.equal(labelFits(placed, { left: 40, right: 90, top: 5, bottom: 15 }), false);
    assert.equal(labelFits(placed, { left: 60, right: 90, top: 0, bottom: 10 }), true);
});

test('the map\'s lines fill their numbered blanks in every language', () => {
    const words = mapTables.map['pt-BR'];
    assert.equal(format(words['hunt-in'], 'Hopy', 2, 10, 'Floresta 1'), 'Hopy  2/10 em Floresta 1');
    assert.equal(format(words.guide, 'Skadi'), 'Guia: Skadi');
    assert.equal(format(mapTables.map.en.exit, 'Forest 1'), 'Way to Forest 1');
});
