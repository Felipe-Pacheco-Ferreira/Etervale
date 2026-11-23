import test from 'node:test';
import assert from 'node:assert/strict';
import { pickerRows } from '../wwwroot/touch-skill-picker.js';

test('the skill list puts a skill on the pressed button and marks the one already on it', () => {
    const rows = pickerRows([{ index: 3, name: 'Raving', picture: 'a', button: 1 }, { index: 7, name: 'Impact', picture: '', button: -1 }], 1);
    assert.deepEqual(rows.map(row => [row.action, row.current, row.holder]),
        [['touch-skill-button:1:3', true, 1], ['touch-skill-button:1:7', false, -1]]);
    assert.deepEqual(pickerRows([], 2), []);
});
