# 022. WebGPU effects and drawing in the browser runtime

Part: processor, runtime.

## Need

The browser runtime ([021](021-webgpu-backend.md)) cleared and presented the canvas but drew nothing: WebGPU takes
shaders only as WGSL, the effect processor wrote SPIR-V, and the backend had no buffers, textures or pipelines. The
client's effects are HLSL for shader model 6 with MonoGame's effect syntax; they should stay one source for Vulkan and
the browser.

## Change

Processor:

- `ShaderProfile.WebGpu.cs`: a WebGPU profile (MGFX profile 81), which `SpirvEffectProcessor` uses for MGCB's `Web`
  platform (`DesktopVK` keeps the Vulkan profile). Each shader goes through the Vulkan profile's DXC build, which gives
  the effect its reflection and the binding header the runtime reads, and the code after the header is WGSL that Slang
  compiles from the same preprocessed HLSL. Slang's bindings follow the registers, all in bind group 0: b0 at the
  stage's base, t registers at +32, s registers at +48, u registers at +64, the pixel stage's base at 128. WebGPU's clip
  space and framebuffer have Direct3D's orientation, so the WGSL keeps HLSL's Y (DXC's Vulkan build flips it).
- Vertex inputs: the header gains, after the descriptor bindings, the number of vertex inputs and each one's semantic
  usage, index and WGSL location. Slang's reflection does not always give the locations its WGSL uses, so each WGSL
  input field (`<HLSL field>_<n>`) is matched by name to the reflection's field and its semantic. WGSL has no matrix
  vertex inputs; such an input is a build error.
- `ShaderProfile.Vulkan.cs`: the semantic table moved into `UsageOf`, shared with the WebGPU profile, and the code
  after the header is written by a virtual `WriteShaderCode`.
- Atomics: Slang turns no HLSL `Interlocked*` call into WGSL (neither on `groupshared` variables nor on storage
  buffers; forcing it gives `atomicMin` on a plain `u32`, which browsers refuse), only its own `Atomic<uint>`, which
  DXC does not know. The effect preprocessor resolves every `#if` before both compilers see the text, so a shader cannot
  branch on the compiler. Instead an effect writes its atomics as the `MGFX_ATOMIC_*` macros (`_UINT`, `_LOAD`, `_STORE`,
  `_ADD`, `_MIN`, `_MAX`, `_OR`), which the effect preprocessor leaves alone, and both profiles put a prelude before the
  HLSL they hand over (`VulkanShaderProfile.AtomicsPrelude`) that each compiler expands with its own preprocessor: DXC as
  `uint` and the `Interlocked` functions, Slang (`__SLANG__`) as `Atomic<uint>` and its methods, which become WGSL's
  `atomic<u32>`. An effect without these macros compiles for Vulkan byte for byte as before the prelude.
- `Slang.cs` finds Slang next to the compiler assembly; `fetch-slang.sh` / `fetch-slang.ps1` fetch the pinned release
  (2026.22, SHA-256 per host) into `pipeline/tools/slang`, which the compiler project copies to its output.
- `StockEffects/`: compiles the browser runtime's stock effects (`runtime/src/webgpu/effects/*.fx`, Etervale's own
  sources; `SpriteEffect.fx` for `SpriteBatch`) into `*.wgpu.mgfxo.h`, which `MGG_WebGPU.cpp` embeds.

Runtime (`src/webgpu/`, split into `MGG_WebGPU.cpp`, `MGG_WebGPU_Resources.cpp`, `MGG_WebGPU_Pipelines.cpp` and the
shared `MGG_WebGPU.h`):

- Commands are recorded into one command encoder per submission, in render passes opened when a draw or a clear needs
  one. WebGPU has no clear inside a pass, so a clear ends the open pass and becomes the load operation of the next one
  (a clear that nothing draws after runs in a pass of its own before the targets change or the frame is presented).
- Buffers keep a copy of their contents (strided writes, read-back of CPU data). Queue writes run before the next
  submission, so writing a vertex or index buffer or a texture that recorded commands still read submits them first;
  a NoOverwrite write does not. Constant buffers live only in their copy: each draw copies the changed ones into a 4 MB
  uniform ring at 256-byte offsets, bound with dynamic offsets, and the ring restarts after each submission.
- Textures and render targets (one attachment view per array layer, an optional depth texture), the framework's
  whole-level region convention (a 0x0 region is the whole mip, as in the Vulkan runtime), samplers (XNA filters,
  address modes without border colour, comparison, anisotropy), blend, depth-stencil and rasterizer states.
- A bind group layout and pipeline layout per vertex and pixel shader pair, from both shaders' headers; a render pipeline
  per state combination (shaders, input layout, states, topology, strip index format, target formats); a bind group per
  set of bound resources. Unbound texture slots read a white fallback texture.
- `GraphicsDevice_GetCaps` reports shader profile 81, so the framework loads only effects built for WebGPU.
- Compute: `Dispatch` and `DispatchIndirect` run in a compute pass of their own, after the open render pass (with a
  pending clear run first, since the shader may read the targets); a compute pipeline per compute shader, made at its
  first dispatch, with the stage's own constants, textures and samplers. WebGPU orders the passes' reads and writes, so
  no barrier is recorded. Storage buffers, bound by `SetShaderBuffer`, take part in draws and dispatches: read-only ones
  in the t registers, writable ones in the u registers; they may also hold indirect group counts. A storage write
  (`SetStorageData`) submits the recorded commands first when they use the buffer, as other writes do; its copy is what
  the CPU wrote, not what a shader wrote.

Not yet: storage images (`RWTexture2D`: WGSL declares a storage texture's exact format, which HLSL's
`RWTexture2D<float4>` does not give, and no client shader writes one), multisampling, read-back of textures and the
back buffer (an asynchronous map a synchronous export cannot wait for), occlusion queries, BC textures (the device does not request
`texture-compression-bc`), formats without a core WebGPU equivalent (16-bit packed colours, 16-bit unorm).

`src/web/BrowserRuntime.props`: the SDK relinks when a linked file changes, and `mgruntime.cpp` includes the other
sources, so they are added to the inputs of its native link (`_WasmLinkDependencies`); without it, a change to an
included file was not compiled. `src/web/mgruntime.cpp` undefines the `min` and `max` macros the decoders define.

## Test

- `runtime/repro/WebGpuRepro`: besides the checks of 021, `SpriteBatch` draws a 2x2 texture (four colours, point
  sampled) over the cleared canvas, and the page reads the canvas at the centre of each quadrant and on the background.
  A compute check (`Content/ComputeCheck.fx`, built for MGCB's `Web` platform and embedded in the assembly): four groups
  of 64 threads count themselves with workgroup and storage atomics, a second dispatch compares the total with 256 and
  writes a verdict colour into a storage buffer, and a quad drawn after the dispatches reads it from a read-only storage
  buffer in its pixel shader (black without the dispatches, red on a wrong count). Chromium with WebGPU on Windows: all
  ten checks pass, and a validation error scope around the frames stays empty.
- The client's 18 effects build for both platforms. For `DesktopVK`, the 13 whose source did not change compile byte for
  byte as before the prelude; `LightCull` and `PostExposure` (now on the atomics macros, the histogram a
  `RWStructuredBuffer` instead of a `RWByteAddressBuffer`) and the three surface effects (the frame block's padding
  moved before `EmissionScale`, see below) changed. For `Web`, the WGSL of all 84 shaders compiles in Chromium
  (`createShaderModule`, `getCompilationInfo`) without an error.
- WGSL's uniform layout is stricter than HLSL's packing: a `vec3` starts on 16 bytes and a `vec2` on 8, where HLSL puts
  a `float3` right after a `float` in the same register. The surface's frame block had one such field (`float
  EmissionScale; float3 FrameReserved;`); a uniform field below its WGSL alignment is refused when the browser compiles
  the shader, not when Slang does.
