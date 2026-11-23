import test from 'node:test';
import assert from 'node:assert/strict';
import { feedTables, feedText, newLines } from '../wwwroot/touch-feed.js';

const say = key => feedTables.feed.en[key];

test('each feed line is worded by its kind, with amounts in the language', () => {
    assert.equal(feedText({ kind: 'Kill', name: 'Hopy' }, say, 'en'), 'Hopy defeated');
    assert.equal(feedText({ kind: 'Kill', name: '' }, say, 'en'), 'Monster defeated');
    assert.equal(feedText({ kind: 'Experience', amount: 12345 }, say, 'en'), '+12,345 EXP');
    assert.equal(feedText({ kind: 'Gold', amount: 80 }, say, 'en'), '+80 gold');
    assert.equal(feedText({ kind: 'Level', amount: 12 }, say, 'en'), 'Level 12!');
    assert.equal(feedText({ kind: 'Quest', name: 'Hopy', count: 3, needed: 10 }, say, 'en'), 'Hopy  3/10');
    assert.equal(feedText({ kind: 'Unknown' }, say, 'en'), '');
});

test('the feed takes only the lines past the count it saw, and starts empty', () => {
    const state = { added: 3, lines: [{ sequence: 1 }, { sequence: 2 }, { sequence: 3 }] };
    assert.deepEqual(newLines(state, -1), [], 'the first look replayed old lines');
    assert.deepEqual(newLines(state, 1).map(line => line.sequence), [2, 3]);
    assert.deepEqual(newLines(state, 3), []);
});
