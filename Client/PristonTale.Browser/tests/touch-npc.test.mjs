import test from 'node:test';
import assert from 'node:assert/strict';
import { npcTables, placePlate, plateButtons } from '../wwwroot/touch-npc.js';

const say = key => npcTables.npc.en[key];
const actions = buttons => buttons.map(button => button.action);

test("an NPC's services become buttons, the first highlighted, an unavailable one off, and Close comes last", () => {
    const buttons = plateButtons({ kind: 'npc', destinations: false, buttons: [{ label: 'Shop', available: true }, { label: 'Aging', available: false }] }, say);
    assert.deepEqual(actions(buttons), ['touch-npc:service:0', 'touch-npc:service:1', 'touch-npc:close']);
    assert.deepEqual(buttons.map(button => button.prominent), [true, false, false]);
    assert.deepEqual(buttons.map(button => button.disabled), [false, true, false]);
    const destinations = plateButtons({ kind: 'npc', destinations: true, buttons: [{ label: 'Ricarten', available: true }] }, say);
    assert.deepEqual(actions(destinations), ['touch-npc:destination:0', 'touch-npc:close']);
});

test("a quest dialog's OK is worded by what it leads to, and closing unanswered is offered only when OK answers", () => {
    const quest = followup => ({ kind: 'quest', followup, choices: [], pending: false });
    const proceed = plateButtons(quest('Proceed'), say);
    assert.deepEqual(proceed.map(button => button.label), ['Continue', 'Close']);
    assert.deepEqual(actions(proceed), ['touch-npc:ok', 'touch-npc:close']);
    assert.deepEqual(plateButtons(quest('Close'), say).map(button => button.label), ['OK']);
    const list = plateButtons({ ...quest('ChooseFromList'), choices: ['Sword', 'Shield'], pending: true }, say);
    assert.deepEqual(actions(list), ['touch-npc:choose:0', 'touch-npc:choose:1', 'touch-npc:ok']);
    assert.deepEqual(list.map(button => button.disabled), [true, true, false], 'a choice stayed on while the answer was in flight');
});

test('a yes/no box offers yes first and no', () => {
    assert.deepEqual(actions(plateButtons({ kind: 'confirm', pending: false }, say)), ['touch-npc:yes', 'touch-npc:no']);
    assert.deepEqual(plateButtons({ kind: '' }, say), []);
});

test('the plate sits centred in the gap between the HUD sides, or centred over the HUD when the gap is too narrow', () => {
    assert.deepEqual(placePlate(1000, 150, 300), { left: 162 + (526 - 420) / 2, width: 420 });
    assert.deepEqual(placePlate(800, 150, 300), { left: 162, width: 326 });
    assert.deepEqual(placePlate(600, 150, 300), { left: 90, width: 420 });
});
