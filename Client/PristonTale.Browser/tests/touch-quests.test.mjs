import test from 'node:test';
import assert from 'node:assert/strict';
import { clock, journalOrder, objectives, questCard, questTables } from '../wwwroot/touch-quests.js';

const words = questTables.quest['pt-BR'];
const say = key => words[key];
const hunt = { step: 'A caçada', journey: false, target: 'Hopy', kills: 2, killsNeeded: 3, timerMs: 0, objective: '', hunting: false,
    guiding: false, canHunt: true, canGuide: false };

test('the card names the step, counts the hunt on its bar and offers the automatic quest while it has work', () => {
    const card = questCard(hunt, say);
    assert.equal(card.title, 'A caçada');
    assert.equal(card.objective, '', 'the step repeats the title');
    assert.deepEqual(card.bar, { label: 'Hopy  2/3', share: 2 / 3 });
    assert.equal(card.met, false);
    assert.equal(card.button, 'Iniciar auto-missão');
    assert.equal(card.status, 'Toque em Iniciar auto-missão para continuar');
});

test('the card wears the gold border once the count is met and pauses while the hunt or the guide runs', () => {
    assert.equal(questCard({ ...hunt, kills: 3 }, say).met, true);
    const hunting = questCard({ ...hunt, hunting: true }, say);
    assert.equal(hunting.button, 'Pausar auto-missão');
    assert.equal(hunting.status, 'Caçando Hopy');
    assert.equal(questCard({ ...hunt, killsNeeded: 0, canHunt: false, guiding: true }, say).status, 'A caminho do destino');
});

test('the journey keeps its own title over the step, and no quest reads as such without a button', () => {
    const journey = questCard({ ...hunt, journey: true, killsNeeded: 0, canHunt: false, canGuide: true }, say);
    assert.deepEqual([journey.title, journey.objective, journey.bar], ['Jornada do Aventureiro', 'A caçada', null]);
    const none = questCard({ ...hunt, step: '', killsNeeded: 0, canHunt: false }, say);
    assert.deepEqual([none.title, none.objective, none.button, none.status], ['Missões', 'Nenhuma missão ativa', '', '']);
});

test('the journal lists the current step first while running and the newest finished quest first otherwise', () => {
    const entries = [{ title: 'Um' }, { title: 'Dois' }, { title: 'Três' }];
    assert.deepEqual(journalOrder(entries, true).map(card => [card.entry.title, card.current]), [['Três', true], ['Dois', false], ['Um', false]]);
    assert.deepEqual(journalOrder(entries, false).map(card => [card.index, card.current]), [[0, false], [1, false], [2, false]]);
});

test('the current step checks off the steps so far, then the hunt with its count and the timer', () => {
    const entries = [{ title: 'Um' }, { title: 'Dois' }];
    const list = objectives({ ...hunt, timerMs: 3725000 }, entries, 1, true, say);
    assert.deepEqual(list, [
        { text: 'Um', done: true },
        { text: 'Dois', done: false },
        { text: 'Derrote Hopy', done: false, gauge: [2, 3] },
        { text: 'Tempo restante  01:02:05', done: false }
    ]);
    assert.deepEqual(objectives({ ...hunt, killsNeeded: 0, objective: 'Fale com Skadi.' }, entries, 1, true, say).at(-1),
        { text: 'Fale com Skadi.', done: false }, 'a step without a hunt shows its own line');
    assert.deepEqual(objectives(hunt, entries, 0, true, say), [{ text: 'Um', done: true }], 'an earlier step is done');
    assert.equal(clock(-5), '00:00:00');
});
