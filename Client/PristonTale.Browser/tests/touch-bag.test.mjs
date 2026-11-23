import test from 'node:test';
import assert from 'node:assert/strict';
import { bagTables, cellsUsed, doll, figureMargin, sheetButtons, sheetRow, slotPitch, slotSize } from '../wwwroot/touch-bag.js';

const words = bagTables.bag['pt-BR'];
const say = key => words[key] ?? key;

test('the figure holds the ten equipment slots in three columns and four rows, none overlapping', () => {
    assert.equal(doll.length, 10);
    assert.equal(new Set(doll.map(([slot]) => slot)).size, 10);
    for (const [slot] of doll) assert.ok(words[slot], slot + ' has no name');
    const boxes = doll.map(([, column, row]) => [figureMargin + column * slotPitch, figureMargin + row * slotPitch]);
    for (let a = 0; a < boxes.length; a++) {
        for (let b = a + 1; b < boxes.length; b++) {
            const apart = Math.abs(boxes[a][0] - boxes[b][0]) >= slotSize || Math.abs(boxes[a][1] - boxes[b][1]) >= slotSize;
            assert.ok(apart, doll[a][0] + ' covers ' + doll[b][0]);
        }
    }
});

test('the bag counts the cells its items cover and leaves out what is worn or on the belt', () => {
    assert.equal(cellsUsed([{ width: 2, height: 3 }, { width: 1, height: 1 }, { slot: 'Weapon' }, { box: 0 }]), 7);
});

test('a tooltip line splits into its label and value, and a line without a colon stays whole', () => {
    assert.deepEqual(sheetRow('Poder de ataque:  10-20'), { label: 'Poder de ataque', value: '10-20' });
    assert.deepEqual(sheetRow('Especialidade'), { label: 'Especialidade', value: '' });
});

test('the sheet offers one belt button per box, runs each action on its item and closes for an item used', () => {
    const buttons = sheetButtons({ id: 'abc', actions: ['Drink', 'BeltPut'] }, 3, say);
    assert.deepEqual(buttons.map(button => button.label), ['Beber', 'Cinto 1', 'Cinto 2', 'Cinto 3']);
    assert.equal(buttons[0].run, 'touch-bag:Drink:abc');
    assert.equal(buttons[3].run, 'touch-bag:BeltPut:abc:2');
    const used = sheetButtons({ id: 'x', actions: ['Use', 'Equip'] }, 3, say);
    assert.deepEqual(used.map(button => [button.label, Boolean(button.closes)]), [['Usar', true], ['Equipar', false]]);
});
