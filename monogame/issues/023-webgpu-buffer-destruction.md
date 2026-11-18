# 023. WebGPU buffer destruction during dynamic-buffer growth

Part: runtime.

## Symptom

The first opening of a larger HUD panel reports "Buffer used in submit while destroyed".
The frame continues, and later openings reuse the larger buffers.

## Cause

SpriteBatch draws through the framework's user vertex and index buffers. When a later draw needs more space, the
framework disposes the previous buffer. The WebGPU backend still has commands using that buffer in its current
command encoder, but MGG_Buffer_Destroy destroyed it immediately. Submission then encounters the destroyed buffer.

## Change

MGG_Buffer_Destroy submits pending commands before destroying a buffer whose usedSubmission matches the device's
current submission. This uses MGWGPU_Flush, as the existing resource-upload path does. A buffer unused in the current
submission needs no flush. WebGPU retains allocations needed by previously submitted work until it completes.
[WebGPU buffer destruction](https://www.w3.org/TR/webgpu/#buffer-destruction)

The exports and runtime interface version stay the same.

## Test

runtime/repro/WebGpuRepro first draws one sprite, then draws four vertices with 6006 indices so only the user index
buffer grows beyond its initial 6000-index capacity. A following batch of 1024 offscreen sprites then grows the vertex
buffer while the preceding draw still uses it. Both replacements happen before the frame's normal presentation.
The page reports that the growth draws ran and accumulates uncaptured WebGPU validation errors alongside the existing
canvas-colour and compute checks.

Publish the repro in Release and serve its wwwroot on localhost. Its growth and validation checks must pass along
with the existing samples. In the client, open Social and Quests for the first time after a reload and verify that
both panels render and the WebGPU error list remains empty.
