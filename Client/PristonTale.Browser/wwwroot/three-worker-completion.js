const clock = () => performance.timeOrigin + performance.now();

export function createGpuCompletionFence(renderer, options = {}) {
    const queue = renderer.backend?.isWebGPUBackend ? renderer.backend.device?.queue : null;
    const gl = queue ? null : renderer.getContext?.();
    if (!queue?.onSubmittedWorkDone && !gl?.fenceSync) throw new Error('The renderer has no nonblocking GPU completion API.');
    const schedule = options.schedule ?? ((callback, delay) => setTimeout(callback, delay));
    const cancel = options.cancel ?? (timer => clearTimeout(timer));
    const now = options.clock ?? clock;
    const pending = new Set();
    let disposed = false;

    return {
        get pendingCount() { return pending.size; },
        afterSubmit() {
            if (disposed) return Promise.reject(new Error('GPU completion fence is disposed.'));
            let resolve, reject;
            const promise = new Promise((done, fail) => { resolve = done; reject = fail; });
            const job = { sync: null, timer: null, active: true, finish(error) {
                if (!job.active) return;
                job.active = false;
                pending.delete(job);
                if (job.timer !== null) cancel(job.timer);
                if (job.sync) {
                    try { gl.deleteSync(job.sync); } catch (deleteError) { error ??= deleteError; }
                }
                if (error) reject(error instanceof Error ? error : new Error(String(error)));
                else resolve(now());
            } };
            pending.add(job);
            try {
                if (queue) {
                    Promise.resolve(queue.onSubmittedWorkDone()).then(() => job.finish(), error => job.finish(error));
                } else {
                    job.sync = gl.fenceSync(gl.SYNC_GPU_COMMANDS_COMPLETE, 0);
                    if (!job.sync) throw new Error('WebGL GPU completion fence could not be created.');
                    gl.flush();
                    const poll = () => {
                        if (!job.active) return;
                        try {
                            if (gl.isContextLost?.()) throw new Error('WebGL context was lost before GPU completion.');
                            const state = gl.clientWaitSync(job.sync, 0, 0);
                            if (state === gl.ALREADY_SIGNALED || state === gl.CONDITION_SATISFIED) job.finish();
                            else if (state === gl.TIMEOUT_EXPIRED) job.timer = schedule(poll, 1);
                            else throw new Error('WebGL GPU completion fence failed.');
                        } catch (error) { job.finish(error); }
                    };
                    poll();
                }
            } catch (error) { job.finish(error); }
            return promise;
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            for (const job of pending) job.finish(new Error('GPU completion fence is disposed.'));
        }
    };
}
