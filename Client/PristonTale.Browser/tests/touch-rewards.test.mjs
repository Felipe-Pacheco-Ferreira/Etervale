import test from 'node:test';
import assert from 'node:assert/strict';
import { restartTables, restartView } from '../wwwroot/touch-restart.js';
import { goalWords, readyText, rewardsClock, rewardsHelp, rewardsNote, rewardsTables, stateChip } from '../wwwroot/touch-rewards.js';

const sayRestart = key => restartTables.restart.en[key];
const say = key => rewardsTables.rewards.en[key];

test('the death plate words the cost, keeps a closed field off and counts down to town', () => {
    const view = restartView({ known: true, scroll: false, fieldOpen: true, experience: '18446744073709551615', gold: '1200', secondsLeft: 7 },
        sayRestart, 'en');
    assert.equal(view.cost, 'Death penalty: -18,446,744,073,709,551,615 EXP and -1,200 gold.');
    assert.deepEqual(view.choices.map(choice => [choice.action, choice.disabled, choice.prominent]),
        [['touch-restart:Field', false, false], ['touch-restart:Town', false, true]]);
    assert.equal(view.countdown, 'Back to town by itself in 7 s');
    const closed = restartView({ known: true, scroll: false, fieldOpen: false, experience: '0', gold: '0', secondsLeft: 20 }, sayRestart, 'en');
    assert.equal(closed.cost, 'No death penalty at your level.');
    assert.deepEqual([closed.choices[0].disabled, closed.choices[0].hint], [true, 'Not enough EXP to stay on the map']);
    const scroll = restartView({ known: true, scroll: true, fieldOpen: true, experience: '9', gold: '9', secondsLeft: 1 }, sayRestart, 'en');
    assert.deepEqual([scroll.cost, scroll.choices[0].hint], ['The Resurrection Scroll will be used: no penalty.', 'Right here, no penalty']);
    assert.equal(restartView({ known: false, fieldOpen: false, secondsLeft: 3 }, sayRestart, 'en').cost, '');
});

test('the rewards clock tells days, hours or minutes, an end or a reset, or no deadline', () => {
    assert.equal(rewardsClock({ track: 'Welcome', secondsLeft: 2 * 86400 + 3 * 3600 }, say), '⏱  Ends in 2d 3h');
    assert.equal(rewardsClock({ track: 'Daily', secondsLeft: 3 * 3600 + 25 * 60 }, say), '⏱  Resets in 3h 25min');
    assert.equal(rewardsClock({ track: 'Weekly', secondsLeft: 59 }, say), '⏱  Resets in 0min');
    assert.equal(rewardsClock({ track: 'LevelRace', secondsLeft: -1 }, say), '∞  No deadline');
});

test('the rewards window words its counts, notes, help, goals and entry states', () => {
    assert.deepEqual([0, 1, 4].map(ready => readyText(ready, say)), ['Nothing ready to claim now.', '1 reward ready', '4 rewards ready']);
    assert.equal(rewardsNote({ track: 'Daily', minLevel: 20 }, say), 'Resets every day at 00:00. The tasks count from level 20.');
    assert.equal(rewardsHelp('Weekly', say), rewardsHelp('Daily', say));
    assert.notEqual(rewardsHelp('Welcome', say), rewardsHelp('LevelRace', say));
    assert.deepEqual(goalWords('Daily', { goal: 'MonsterKills', target: 1500 }, say, 'en'),
        { title: 'Daily hunt', text: 'Defeat 1,500 monsters on any map.' });
    assert.equal(goalWords('Weekly', { goal: 'MonsterKills', target: 1 }, say, 'en').title, 'Tireless hunter');
    assert.deepEqual(stateChip('Ready', 'Claim', say), { text: '★ Claim', kind: 'reward' });
    assert.deepEqual(stateChip('Claimed', 'Claim', say), { text: '✔ Claimed', kind: 'done' });
});
