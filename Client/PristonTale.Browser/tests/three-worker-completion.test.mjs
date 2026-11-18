import test from 'node:test';
import assert from 'node:assert/strict';
import { createGpuCompletionFence } from '../wwwroot/three-worker-completion.js';

function webgpu() {
    const jobs = [];
    const renderer = { backend: { isWebGPUBackend: true, device: { queue: { onSubmittedWorkDone() {
        return new Promise((resolve, reject) => jobs.push({ resolve, reject }));
    } } } } };
    return { renderer, jobs };
}

function webgl(states = [1, 2]) {
    const timers = new Map(), deleted = [], calls = [];
    let next = 0;
    const gl = { SYNC_GPU_COMMANDS_COMPLETE: 10, TIMEOUT_EXPIRED: 1, CONDITION_SATISFIED: 2, ALREADY_SIGNALED: 3, WAIT_FAILED: 4,
        fenceSync(condition, flags) { calls.push(['fence', condition, flags]); return {}; },
        flush() { calls.push(['flush']); }, finish() { assert.fail('blocking finish must not be called'); },
        clientWaitSync(sync, flags, timeout) { calls.push(['wait', flags, timeout]); return states.shift() ?? 1; },
        deleteSync(sync) { deleted.push(sync); }, isContextLost() { return false; } };
    const tracker = createGpuCompletionFence({ getContext: () => gl }, {
        schedule(callback, delay) { calls.push(['schedule', delay]); timers.set(++next, callback); return next; },
        cancel(id) { timers.delete(id); }, clock: () => 1234 });
    return { gl, tracker, calls, deleted, timers, poll() {
        const [id, callback] = timers.entries().next().value;
        timers.delete(id); callback();
    } };
}

test('WebGPU completion uses the actual queue fence, remains asynchronous and has independent promises', async () => {
    const f = webgpu(), tracker = createGpuCompletionFence(f.renderer, { clock: () => 6789 });
    const results = [tracker.afterSubmit(), tracker.afterSubmit(), tracker.afterSubmit()];
    assert.equal(f.jobs.length, 3);
    assert.equal(tracker.pendingCount, 3);
    let completed = false;
    results[0].then(() => { completed = true; });
    await Promise.resolve(); assert.equal(completed, false);
    f.jobs[0].resolve(); assert.equal(await results[0], 6789);
    assert.equal(tracker.pendingCount, 2);
    f.jobs[1].resolve(); f.jobs[2].resolve(); await Promise.all(results);
    assert.equal(tracker.pendingCount, 0);
    tracker.dispose();
    await assert.rejects(tracker.afterSubmit(), /disposed/);
});

test('WebGPU rejection and disposal settle every pending fence without late resolution or rejection leaks', async () => {
    const f = webgpu(), tracker = createGpuCompletionFence(f.renderer);
    const first = tracker.afterSubmit(), second = tracker.afterSubmit();
    const rejected = assert.rejects(first, /device lost/);
    f.jobs[0].reject(Error('device lost')); await rejected;
    const cancelled = assert.rejects(second, /disposed/);
    tracker.dispose(); await cancelled;
    f.jobs[1].resolve(); await Promise.resolve();
    assert.equal(tracker.pendingCount, 0);
});

test('WebGL fences flush and poll clientWaitSync with zero timeout, then delete exactly once', async () => {
    const f = webgl(), result = f.tracker.afterSubmit();
    assert.deepEqual(f.calls.slice(0, 4), [['fence', 10, 0], ['flush'], ['wait', 0, 0], ['schedule', 1]]);
    assert.equal(f.tracker.pendingCount, 1);
    assert.equal(f.deleted.length, 0);
    f.poll(); assert.equal(await result, 1234);
    assert.equal(f.deleted.length, 1);
    assert.equal(f.timers.size, 0);
    assert.equal(f.tracker.pendingCount, 0);
    f.tracker.dispose(); assert.equal(f.deleted.length, 1);
});

test('WebGL failed/context-lost/null fences reject explicitly and disposal cancels polling and deletes syncs', async () => {
    const failed = webgl([4]);
    await assert.rejects(failed.tracker.afterSubmit(), /fence failed/);
    assert.equal(failed.deleted.length, 1);
    const lost = webgl(); lost.gl.isContextLost = () => true;
    await assert.rejects(lost.tracker.afterSubmit(), /context was lost/);
    assert.equal(lost.deleted.length, 1);
    const absent = webgl(); absent.gl.fenceSync = () => null;
    await assert.rejects(absent.tracker.afterSubmit(), /could not be created/);
    const cancelled = webgl(), result = cancelled.tracker.afterSubmit();
    const callback = cancelled.timers.values().next().value;
    const refusal = assert.rejects(result, /disposed/);
    cancelled.tracker.dispose(); await refusal;
    assert.equal(cancelled.deleted.length, 1);
    assert.equal(cancelled.timers.size, 0);
    const count = cancelled.calls.length; callback();
    assert.equal(cancelled.calls.length, count);
    assert.throws(() => createGpuCompletionFence({}), /no nonblocking GPU completion API/);
});
