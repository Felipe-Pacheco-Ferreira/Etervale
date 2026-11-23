import test from 'node:test';
import assert from 'node:assert/strict';
import { memberCommands, partyTables, playerButtons, requestText } from '../wwwroot/touch-party.js';

const say = key => partyTables.party.en[key];

test('the reached player row keeps the game order and highlights the party invite', () => {
    const buttons = playerButtons({ buttons: ['Trade', 'Party', 'Friend', 'Clan', 'Duel'] }, say);
    assert.deepEqual(buttons.map(entry => entry.action),
        ['touch-party:player:Trade', 'touch-party:player:Party', 'touch-party:player:Friend', 'touch-party:player:Clan', 'touch-party:player:Duel']);
    assert.deepEqual(buttons.filter(entry => entry.prominent).map(entry => entry.label), ['Invite to party']);
    assert.deepEqual(playerButtons({ buttons: [] }, say), []);
});

test('a member offers the leader its commands and anyone leaving, with the id kept as text', () => {
    const member = { id: '18446744073709551615', name: 'Ayla', level: 40 };
    assert.deepEqual(memberCommands({ leads: true }, member, say).map(entry => entry.action), [
        'touch-party:command:Delegate:18446744073709551615', 'touch-party:command:Kick:18446744073709551615',
        'touch-party:command:Disband:0', 'touch-party:command:Leave:0']);
    assert.deepEqual(memberCommands({ leads: false }, member, say).map(entry => entry.label), ['Leave the party']);
});

test('a request shows the game line, and the page line for a trade', () => {
    assert.deepEqual(requestText({ kind: 'Duel', line: 'challenges you to a duel.' }, say),
        { heading: 'Duel challenge', line: 'challenges you to a duel.' });
    assert.deepEqual(requestText({ kind: 'Trade', line: '' }, say), { heading: 'Trade request', line: 'Wants to trade with you.' });
});
