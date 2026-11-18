import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../wwwroot/main.js', import.meta.url), 'utf8');
const start = source.indexOf('function configureWeatherDiagnostic('), end = source.indexOf('\nfunction enableCanvasPicking(', start);
assert.ok(start >= 0 && end > start);
const configure = new Function('URLSearchParams', 'console', source.slice(start, end) + '\nreturn configureWeatherDiagnostic;')(URLSearchParams, { info() {} });

test('real startup enables full rain only with both exact diagnostic query values and clears every normal visit', () => {
    for (const [query, expected] of [['', false], ['?profile', false], ['?profile=0&qa-weather=rain-full', false],
        ['?profile=1', false], ['?qa-weather=rain-full', false], ['?profile=1&qa-weather=rain', false],
        ['?profile=1&qa-weather=RAIN-FULL', false], ['?profile=1&qa-weather=rain-full', true]]) {
        const calls = [], host = { SetDiagnosticFullRain(enabled) { calls.push(enabled); return true; } };
        assert.equal(configure(host, query), expected);
        assert.deepEqual(calls, [expected]);
    }
    assert.ok(source.indexOf('if (profileFrames) host.EnableFrameProfiling();') < source.indexOf('        configureWeatherDiagnostic(host);'));
});

test('a refused full rain request fails explicitly instead of claiming a successful wet benchmark', () => {
    const host = { SetDiagnosticFullRain() { return false; } };
    assert.throws(() => configure(host, '?profile=1&qa-weather=rain-full'), /frame profiling/);
    assert.equal(configure(host, ''), false);
});
