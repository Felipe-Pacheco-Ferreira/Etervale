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

test('a five-second warmup and three slow windows come before the first step, which slows shadows and keeps the scale', () => {
    const { controller, run } = fixture();
    run(7, 30);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0);
    run(1, 30);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 30);
    assert.equal(controller.cap, 1);
    assert.equal(controller.target, 60);
});

test('sustained 58.9 completed FPS slows both shadow steps before lowering the scale', () => {
    const { controller, run } = fixture(0.85);
    run(8, 58.9);
    assert.deepEqual([controller.scale, controller.shadowHz], [0.85, 30]);
    run(3, 58.9);
    assert.deepEqual([controller.scale, controller.shadowHz], [0.85, 15]);
    run(3, 58.9);
    assert.deepEqual([controller.scale, controller.shadowHz], [0.8, 15]);
    assert.equal(controller.cap, 0.85);
});

test('real 59.94 completed FPS and a deliberate 30 FPS limit retain the selected budget', () => {
    for (const [target, measured] of [[0, 59.94], [30, 29.94]]) {
        const { controller, run } = fixture(1, target);
        assert.equal(run(60, measured), 0);
        assert.equal(controller.scale, 1);
        assert.equal(controller.shadowHz, 0);
    }
});

test('persistent pressure reaches 15 Hz shadows and the scale floor but never crosses it', () => {
    const { controller, run } = fixture();
    run(40, 40);
    assert.equal(controller.scale, 0.5);
    assert.equal(controller.shadowHz, 15);
    run(10, 0);
    assert.equal(controller.scale, 0.5);
    assert.equal(controller.cap, 1);
});

test('both shadow steps come before any scale step, and recovery restores the scale before the shadows', () => {
    const { controller, run } = fixture();
    const seen = [];
    for (let second = 0; second < 30; second++) {
        run(1, 40);
        const last = seen.at(-1);
        if (!last || last[0] !== controller.scale || last[1] !== controller.shadowHz) seen.push([controller.scale, controller.shadowHz]);
    }
    assert.deepEqual(seen.slice(0, 3), [[1, 0], [1, 30], [1, 15]]);
    assert.ok(seen.at(-1)[0] < 1, 'the scale never dropped');
    while (controller.scale < 1) {
        run(6, 60);
        assert.equal(controller.shadowHz, 15, 'shadows recovered while the scale was still reduced');
    }
    run(6, 60);
    assert.equal(controller.shadowHz, 30);
    run(6, 60);
    assert.equal(controller.shadowHz, 0);
});

test('a throttled page (window behind another one) is not measured as a slow GPU', () => {
    const { controller, run } = fixture();
    run(60, 1);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0);
    run(6, 60);
    assert.equal(controller.scale, 1);
});

test('hidden/loading windows freeze quality and require warmup again', () => {
    const { controller, run } = fixture();
    run(20, 40);
    const budget = [controller.scale, controller.shadowHz];
    run(20, 1, false);
    assert.deepEqual([controller.scale, controller.shadowHz], budget);
    run(4, 30);
    assert.deepEqual([controller.scale, controller.shadowHz], budget);
});

test('disabling adaptation or returning to the courtyard restores the saved cap without persisting any effective value', () => {
    const { controller, run, change } = fixture(0.9);
    run(16, 30);
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

test('a deliberate 30 FPS limit is the target rather than a signal to degrade quality', () => {
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
    run(30, 30);
    assert.ok(controller.scale < 1);
    assert.equal(controller.update(30100, 903, true, 1, 0, 1280, 720, true, true, 1), true);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0);
});

test('recovery probes are gradual and bounded by the saved cap', () => {
    const { controller, run } = fixture(0.75);
    run(20, 40);
    const reduced = controller.scale;
    assert.ok(reduced < 0.75);
    run(5, 59.9);
    assert.equal(controller.scale, reduced);
    run(2, 59.9);
    assert.equal(controller.scale, Math.min(0.75, Math.round((reduced + 0.05) * 20) / 20));
    run(180, 59.9);
    assert.equal(controller.scale, 0.75);
    assert.equal(controller.shadowHz, 0);
});

test('the classic light holds the saved scale under pressure and only slows the shadows', () => {
    const controller = createAdaptiveResolution();
    let time = 0, frames = 0;
    const step = (seconds, fps) => {
        for (let index = 0; index < seconds * 10; index++) {
            time += 100; frames += fps / 10;
            controller.update(time, frames, true, 1, 0, 1280, 720, true, true, 0, true);
        }
    };
    controller.update(time, frames, true, 1, 0, 1280, 720, true, true, 0, true);
    step(30, 40);
    assert.equal(controller.scale, 1, 'the classic light lowered the 3D resolution');
    assert.equal(controller.shadowHz, 15);
    step(40, 60);
    assert.equal(controller.scale, 1);
    assert.equal(controller.shadowHz, 0, 'shadows did not recover once frames had room');
});
