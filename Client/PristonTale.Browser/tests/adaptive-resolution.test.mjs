import test from 'node:test';
import assert from 'node:assert/strict';
import { createAdaptiveResolution } from '../wwwroot/adaptive-resolution.js';

function fixture(cap = 1, maxFps = 0) {
    const controller = createAdaptiveResolution();
    let time = 0, frames = 0;
    const run = (seconds, fps, eligible = true) => {
        let changes = 0;
        for (let index = 0; index < seconds * 10; index++) {
            time += 100; frames += fps / 10;
            if (controller.update(time, frames, true, cap, maxFps, 1280, 720, true, eligible)) changes++;
        }
        return changes;
    };
    controller.update(time, frames, true, cap, maxFps, 1280, 720, true, true);
    return { controller, run, change(enabled, nextCap = cap, world = true) {
        return controller.update(time, frames, enabled, nextCap, maxFps, 1280, 720, world, true);
    } };
}

test('completed-frame rate needs warmup and two slow windows before reducing a saved cap', () => {
    const { controller, run } = fixture();
    run(3, 30);
    assert.equal(controller.scale, 1);
    run(1, 30);
    assert.equal(controller.scale, 0.85);
    assert.equal(controller.cap, 1);
    assert.equal(controller.target, 60);
});

test('sustained 58.9 completed FPS reduces the budget after two windows instead of remaining below the 60 FPS target indefinitely', () => {
    const { controller, run } = fixture(0.85);
    run(3, 58.9);
    assert.equal(controller.scale, 0.85, 'one slow window cannot lower the saved budget');
    run(1, 58.9);
    assert.equal(controller.scale, 0.8);
    assert.equal(controller.cap, 0.85);
    assert.equal(controller.shadowHz, 0);
});

test('real 59.94 completed FPS and a deliberate 30 FPS limit retain the selected budget', () => {
    for (const [target, measured] of [[0, 59.94], [30, 29.94]]) {
        const { controller, run } = fixture(1, target);
        assert.equal(run(60, measured), 0);
        assert.equal(controller.scale, 1);
        assert.equal(controller.shadowHz, 0);
    }
});

test('persistent pressure may request visible30Hz shadows but never disables effects or crosses the scale floor', () => {
    const { controller, run } = fixture();
    run(30, 40);
    assert.equal(controller.scale, 0.5);
    assert.equal(controller.shadowHz, 30);
    run(10, 0);
    assert.equal(controller.scale, 0.5);
    assert.equal(controller.cap, 1);
});

test('hidden/loading windows freeze quality and require warmup again', () => {
    const { controller, run } = fixture();
    run(8, 40);
    const scale = controller.scale;
    run(20, 1, false);
    assert.equal(controller.scale, scale);
    run(2, 30);
    assert.equal(controller.scale, scale);
});

test('disabling adaptation or returning to the courtyard restores the saved cap without persisting any effective value', () => {
    const { controller, run, change } = fixture(0.9);
    run(12, 30);
    assert.ok(controller.scale < 0.9);
    assert.equal(change(false), true);
    assert.equal(controller.scale, null);
    assert.equal(controller.shadowHz, 0);
    assert.equal(controller.cap, 0.9);
    change(true);
    assert.equal(controller.scale, 0.9);
    assert.equal(change(true, 0.9, false), true);
    assert.equal(controller.scale, null);
});

test('a deliberate30FPS limit is the target rather than a signal to degrade quality', () => {
    const { controller, run } = fixture(1, 30);
    run(30, 29.9);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0);
});

test('a suspended animation callback is discarded rather than measured as sustained low FPS', () => {
    const controller = createAdaptiveResolution();
    const update = (time, frames) => controller.update(time, frames, true, 1, 0, 1280, 720, true, true);
    update(0, 0);
    for (let time = 100; time <= 5000; time += 100) update(time, time * 0.06);
    assert.equal(update(30000, 301), false);
    for (let time = 30100; time <= 32000; time += 100) update(time, 301 + (time - 30000) * 0.03);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0);
});

test('applying another preset at the same scale starts a fresh budget rather than retaining the old degradation', () => {
    const { controller, run } = fixture();
    run(20, 30);
    assert.equal(controller.scale, 0.5);
    assert.equal(controller.update(20100, 603, true, 1, 0, 1280, 720, true, true, 1), true);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0);
});

test('recovery probes are gradual and bounded by the saved cap', () => {
    const { controller, run } = fixture(0.75);
    run(8, 40);
    const reduced = controller.scale;
    run(10, 59.9);
    assert.equal(controller.scale, reduced);
    run(8, 59.9);
    assert.equal(controller.scale, Math.min(0.75, Math.round((reduced + 0.05) * 20) / 20));
    run(180, 59.9);
    assert.equal(controller.scale, 0.75);
    assert.equal(controller.shadowHz, 0);
});
