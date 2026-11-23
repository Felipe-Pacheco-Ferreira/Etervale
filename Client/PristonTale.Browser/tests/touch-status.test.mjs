import test from 'node:test';
import assert from 'node:assert/strict';
import { attributes, combatTiles, plan, statusTables } from '../wwwroot/touch-status.js';
import { nextRank, skillsTables } from '../wwwroot/touch-skills.js';

test('points are planned within the free ones and taken back only where planned', () => {
    let planned = [0, 0, 0, 0, 0];
    planned = plan(planned, 0, 1, 2);
    planned = plan(planned, 3, 1, 2);
    assert.deepEqual(planned, [1, 0, 0, 1, 0]);
    assert.deepEqual(plan(planned, 1, 1, 2), planned, 'a point past the free ones was planned');
    assert.deepEqual(plan(planned, 2, -1, 2), planned, 'a point was taken back where none was planned');
    assert.deepEqual(plan(planned, 0, -1, 2), [0, 0, 0, 1, 0]);
});

test('the attributes follow the game order and every one has a name, a meaning and an emblem', () => {
    const words = statusTables.status['pt-BR'];
    assert.deepEqual(attributes.map(attribute => attribute.key), ['strength', 'spirit', 'talent', 'agility', 'health']);
    for (const attribute of attributes) {
        assert.ok(words[attribute.key] && words[attribute.key + '-effect'] && attribute.sign.startsWith('M'), attribute.key);
    }
});

test('the combat tiles show what planned points add only where they add something', () => {
    const combat = { damageMin: 10, damageMax: 20, attackRating: 100, defence: 50, absorption: 4, critical: 5, block: 0, moveSpeed: 1, skillPoints: 2 };
    const gains = { damageMin: 2, damageMax: 3, attackRating: 0, defence: 4, absorption: 0, moveSpeed: 0 };
    const shown = combatTiles.map(([key, value, gain]) => [key, value(combat), gain(gains)]);
    assert.deepEqual(shown[0], ['attack', '10 – 20', '+2 – +3']);
    assert.deepEqual(shown[1], ['attack-rating', '100', null]);
    assert.deepEqual(shown[2], ['defence', '50', '+4']);
    assert.deepEqual(shown[4], ['critical', '5%', null]);
});

test('the skill sheet says what the next rank needs or why it cannot be trained', () => {
    const words = skillsTables.skills['pt-BR'];
    const say = key => words[key];
    const sheet = { rank: 2, price: 1500, missing: 0, elite: false, levelFor: 14, tierTitle: 'Cavaleiro', previous: 'Golpe' };
    assert.equal(nextRank({ ...sheet, check: 'Ready' }, say, 'pt-BR'), 'Rank 3: 1.500 de ouro e 1 ponto.');
    assert.equal(nextRank({ ...sheet, check: 'Ready', missing: 200 }, say, 'pt-BR'), 'Rank 3: faltam 200 de ouro.');
    assert.equal(nextRank({ ...sheet, check: 'Ready', elite: true }, say, 'pt-BR'), 'Rank 3: 1.500 de ouro e 1 ponto de elite.');
    assert.equal(nextRank({ ...sheet, check: 'NeedsLevel' }, say, 'pt-BR'), 'Rank 3 no nível 14.');
    assert.equal(nextRank({ ...sheet, check: 'TierLocked' }, say, 'pt-BR'), 'Abre ao virar Cavaleiro.');
    assert.equal(nextRank({ ...sheet, check: 'NoPoints', elite: true }, say, 'pt-BR'), 'Sem pontos de elite.');
});
