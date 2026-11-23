import test from 'node:test';
import assert from 'node:assert/strict';
import { advance, lessons, nextLesson, readDone } from '../wwwroot/touch-tutorial.js';

const base = { alive: true, idle: true, piece: '', potion: '', beltEmpty: false, hunting: false, hostile: false, statPoints: 0, attacking: false };
const context = (state, extra = {}) => ({ state: { ...base, ...state }, statusSeen: false, open: () => false, pressed: () => false,
    visible: () => false, planned: () => false, spent: () => false, ...extra });
const lesson = id => lessons.find(entry => entry.id === id);

test('the lessons come in order, each once, and none while the automatic quest runs', () => {
    assert.equal(nextLesson([], context({}))?.id, 'hud');
    assert.equal(nextLesson(['hud'], context({ piece: 'a' }))?.id, 'equip');
    assert.equal(nextLesson(['hud'], context({ idle: false, piece: 'a' })), null);
    assert.equal(nextLesson(['hud', 'equip', 'belt', 'auto', 'attack'], context({ idle: false, statPoints: 3 }))?.id, 'points',
        'the points lesson did not wait only for a moment out of a fight');
    assert.equal(nextLesson(['hud', 'equip', 'belt', 'auto', 'attack'], context({ statPoints: 3, attacking: true })), null);
});

test('a step moves on once its goal is met, and a lesson that lost its window starts over', () => {
    const equip = lesson('equip');
    assert.equal(advance(equip, 0, context({ piece: 'a' })), 0);
    assert.equal(advance(equip, 0, context({ piece: 'a' }, { open: () => true })), 1);
    assert.equal(advance(equip, 1, context({ piece: 'a' }, { open: () => true, pressed: () => true })), 2);
    assert.equal(advance(equip, 2, context({ piece: 'a' }, { open: () => false })), 0, 'closing the bag midway did not start over');
    assert.equal(advance(equip, 2, context({ piece: '' }, { open: () => true })), 3, 'wearing every piece did not end the lesson');
    assert.equal(advance(lesson('hud'), 0, context({})), 0, 'a step that only explains moved on without Entendi');
});

test('the lessons shown are read back only from the current round', () => {
    assert.deepEqual(readDone(JSON.stringify({ round: 1, done: ['hud', 3, 'belt'] })), ['hud', 'belt']);
    assert.deepEqual(readDone(JSON.stringify({ round: 0, done: ['hud'] })), []);
    assert.deepEqual(readDone('{bad'), []);
});
