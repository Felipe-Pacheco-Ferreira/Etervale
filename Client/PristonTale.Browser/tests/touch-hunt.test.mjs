import test from 'node:test';
import assert from 'node:assert/strict';
import { addTerm, gainReadings, huntTables, logMatches, logText, removeTerm, searchCatalog, splitTerms } from '../wwwroot/touch-hunt.js';

const say = key => huntTables.hunt.en[key];

test('the log words each entry by its kind and reason, and filters hunt, deaths and party lines', () => {
    assert.equal(logText({ kind: 'Started', reason: 'Manual', name: '' }, say), 'Auto-hunt started');
    assert.equal(logText({ kind: 'Stopped', reason: 'FullBag', name: '' }, say), 'Auto-hunt off · Full bag');
    assert.equal(logText({ kind: 'Death', reason: 'Manual', name: 'Hopy' }, say), 'Character died · Hopy');
    assert.equal(logText({ kind: 'Death', reason: 'Manual', name: '' }, say), 'Character died');
    assert.equal(logText({ kind: 'Left', reason: 'Manual', name: 'Ayla' }, say), 'Ayla left the party');
    const kinds = ['Started', 'Stopped', 'Death', 'Joined', 'Left'].map(kind => ({ kind }));
    assert.deepEqual([0, 1, 2, 3].map(category => kinds.filter(entry => logMatches(entry, category)).map(entry => entry.kind)),
        [['Started', 'Stopped', 'Death', 'Joined', 'Left'], ['Started', 'Stopped'], ['Death'], ['Joined', 'Left']]);
});

test('a loot list adds a term once, case aside, and takes it away', () => {
    assert.equal(addTerm(' #PL101 , @Runes ', '#pl101'), '#PL101,@Runes');
    assert.equal(addTerm('#PL101', '@MP'), '#PL101,@MP');
    assert.equal(removeTerm('#PL101,@MP', '@mp'), '#PL101');
    assert.deepEqual(splitTerms(''), []);
});

test('the catalog search keeps the category, matches code or accentless name and leaves out chosen items', () => {
    const catalog = [
        { code: 'PL101', name: 'Poção Pequena de Vida', category: 'Potions' },
        { code: 'PM101', name: 'Poção de Mana', category: 'Potions' },
        { code: 'WA101', name: 'Machado de Pedra', category: 'Weapons' }
    ];
    assert.deepEqual(searchCatalog(catalog, 'pocao', 'Potions', '').map(item => item.code), ['PL101', 'PM101']);
    assert.deepEqual(searchCatalog(catalog, 'wa1', '', '').map(item => item.code), ['WA101']);
    assert.deepEqual(searchCatalog(catalog, '', 'Potions', '#pl101').map(item => item.code), ['PM101']);
});

test('the gains tab rates by hour, shows the level progress and estimates the next level', () => {
    const totals = { Seconds: 1800, Experience: 5000, ExperienceLost: 0, Gold: 900, GoldSpent: 100, Damage: 12000, Kills: 30, Deaths: 1,
        Levels: 0, HP: 4, MP: 2, RES: 0, DroppedGold: 300, Collected: 7, RecentExperiencePerHour: 0, RecentDamagePerSecond: 6.66, RecentKills: 5,
        Items: [{}, {}] };
    const values = gainReadings(totals, { level: 20, experience: 1500, levelStart: 1000, nextLevel: 11000 }, say, 'en');
    assert.equal(values['session-time'], 'Session 30m00s');
    assert.equal(values['exp-hour'], '10,000');
    assert.equal(values.progress, '5.0%');
    assert.equal(values['next-level'], '1.0 h', 'the 9,500 EXP left at 10,000 an hour');
    assert.equal(values.balance, '800');
    assert.equal(values.dps, '6.7');
    assert.equal(values['exp-recent'], '—');
    assert.equal(values.kinds, '2');
});
