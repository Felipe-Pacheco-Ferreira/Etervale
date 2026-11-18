import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../wwwroot/main.js', import.meta.url), 'utf8');
const start = source.indexOf('function enableCanvasPicking(');
const end = source.indexOf('// Three owns rendering', start);
assert.ok(start >= 0 && end > start);
const picking = source.slice(start, end);

function harness(profile) {
    const events = new Map();
    const windowEvents = new Map();
    const commands = [];
    const logs = [];
    const panel = { hidden: false, dataset: { screen: 'class' } };
    const canvas = { addEventListener: (name, action) => events.set(name, action),
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 500 }),
        focus() {}, setPointerCapture() {}, dispatchEvent(event) { events.get(event.type)?.(event); } };
    const host = { PickOnStage: (...values) => commands.push(['pick', ...values]),
        CharacterCommand: (...values) => commands.push(['command', ...values]) };
    const initialize = new Function('canvas', 'root', 'host', 'window', 'requestAnimationFrame', 'MouseEvent', 'console',
        `${picking}; return enableCanvasPicking;`)(canvas, { querySelector: () => panel }, host,
        { addEventListener: (name, action) => windowEvents.set(name, action) }, action => action(),
        class { constructor(type, values) { Object.assign(this, values, { type }); } }, { info: line => logs.push(line) });
    initialize(profile);
    const dispatch = (type, values = {}) => {
        const event = { type, button: 0, isPrimary: true, pointerId: 1, pointerType: 'mouse',
            clientX: 200, clientY: 200, timeStamp: 1000, detail: 1, ...values };
        (events.get(type) ?? windowEvents.get(type))?.(event);
    };
    return { dispatch, commands, logs, events };
}

function pair(target) {
    for (const [timeStamp, detail] of [[1000, 1], [1200, 2]]) {
        target.dispatch('pointerdown', { timeStamp, detail });
        target.dispatch('pointerup', { timeStamp: timeStamp + 1, detail });
        target.dispatch('click', { timeStamp: timeStamp + 2, detail });
    }
    target.dispatch('dblclick', { timeStamp: 1203, detail: 2 });
}

test('actual stage input profiling leaves the queued picks and event times unchanged', () => {
    const regular = harness(false);
    const profiled = harness(true);
    pair(regular);
    pair(profiled);
    assert.deepEqual(profiled.commands, regular.commands);
    assert.deepEqual(regular.commands, [['pick', 'class', 0.2, 0.4, 1.002], ['pick', 'class', 0.2, 0.4, 1.202]]);
    assert.equal(regular.logs.length, 0);
    assert.equal(regular.events.has('dblclick'), false);
    const records = profiled.logs.map(line => JSON.parse(line.slice('[StageInput] '.length)));
    const submitted = records.filter(record => record.action === 'submitted');
    assert.equal(submitted[1].detail, 2);
    assert.equal(submitted[1].deltaMs, 200);
    assert.equal(records.filter(record => record.action === 'dblclick').length, 1);
    assert.equal(profiled.commands.length, 2, 'the diagnostic native dblclick listener submitted another pick');
});

test('actual stage drag diagnostics retain reset and mouse-click suppression', () => {
    const target = harness(true);
    target.dispatch('pointerdown');
    target.dispatch('pointermove', { clientX: 250, timeStamp: 1050 });
    target.dispatch('pointerup', { clientX: 250, timeStamp: 1100 });
    target.dispatch('click', { clientX: 250, timeStamp: 1102 });
    assert.deepEqual(target.commands, [['command', 'ResetStagePick', -1, '']]);
    const records = target.logs.map(line => JSON.parse(line.slice('[StageInput] '.length)));
    assert.ok(records.some(record => record.action === 'reset' && record.reason === 'drag'));
    assert.ok(records.some(record => record.action === 'ignored' && record.reason === 'suppressed'));
});
