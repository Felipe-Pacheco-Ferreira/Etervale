# Three.js browser presentation

The browser shares the existing C# game state, motion evaluation, picking, network and UI logic. Its graphics output
uses Three.js with WebGPU or WebGL 2. A browser without WebGPU must be able to enter the same client without creating
a MonoGame WebGPU device.

## Boundary

`PristonRenderer` accepts an `IExternalSceneRenderer`. The browser installs its factory before creating any screen.
The external path sends mesh, material and texture resources once, keeps the original CPU animation and attachment
logic, and sends packed camera, light and pose changes. The desktop constructor keeps its existing Vulkan path.

`web/MGG_Three.cpp` implements the existing native graphics ABI as a CPU resource adapter. Existing `SpriteBatch` HUD
and VFX draws become ordered textured triangle batches consumed by the same Three.js renderer. Render-target changes,
clears, portraits, texture updates and draw calls retain their order. The adapter uses the existing MGFX container for
sprite/VFX parameter binding; it does not compile WGSL or create a second graphics device.

`BrowserRuntime.props` selects this adapter when `EtervaleThreeRenderer` is true. Other projects, including
`WebGpuRepro`, retain the WebGPU backend. The exported ABI and runtime version remain unchanged.

## Data and lifetime

- CPU buffers preserve strided and partial writes. Resource IDs are live native texture pointers; browser scene
  textures use negative IDs so the two namespaces cannot collide.
- Native draw matrices undo the effect parameter's HLSL storage transpose before being sent to Three. Projection
  conversion from the game's zero-to-one depth range happens in the WebGL 2 presentation path.
- Native packed vertex colours retain their normalized raw channels and packed-format flag; float colours remain
  unchanged. The native shader kind determines their display or linear interpretation. Blend factors, viewport and
  scissor state are carried with each native batch.
- Texture creation carries a separate storage encoding flag: `Color`, `Alpha8`, `Bgr32` and `Bgra32` use raw UNORM
  bytes; explicit `ColorSRgb`, `Bgr32SRgb` and `Bgra32SRgb` formats request sRGB storage. The same flag applies to render
  targets. This metadata changes neither premultiplied upload bytes, BGRA swizzling, alpha expansion nor CPU readback.
  Clear colours also reach the JavaScript adapter unchanged.
- JavaScript copies transient WASM views into reusable resource/frame storage. Views are recreated after memory grows.
  GPU resources are disposed only after earlier commands that reference them have been rendered.
- Render-target textures are allocated on first use. Unused legacy post-processing targets do not allocate GPU images.
- When the module's graphics adapter declares `workerRendering`, the browser platform updates logical window dimensions
  and resize events without resizing the transferred HTML canvas. Ordered renderer frames own the OffscreenCanvas size.
- `Single` textures retain signed Float32 bytes and declare format 13 to the JavaScript adapter. Rain's toroidal
  occlusion map therefore uses a single-channel float texture with integer texel loads, without colour conversion,
  filtering or mipmaps.

Rain has a separate native submission path for the authored drop and splash vertex signatures. Both Vector4 channels,
original indices, the transposed-back view/projection matrix, all vertex/pixel constants and blend/depth/clip/target
state cross the boundary unchanged. The Three rain shader evaluates seeded streaks on the GPU; C# continues producing
splash quads and occlusion heights. Sprite, Unlit and VFX submissions keep their existing contract. An unsupported
shader still stops explicitly, reporting only its bounded constant-buffer identifier instead of dumping shader code.

Static RainDrops use ordered geometry registration, range-only draw and destruction commands. Eligibility requires
non-dynamic vertex and index buffers belonging to the device and exactly the two Vector4 input channels at offsets
0 and 16 in a 32-byte stream. The cache key includes both buffer identities and write revisions, the vertex/base
offset and index format; intensity changes only the index range. Every buffer write, including partial NoOverwrite
and storage writes, invalidates dependent resources. Buffer or device disposal retires them before IDs can become
stale. IDs are monotonically allocated unsigned integers rather than recycled native pointers.

The browser copies complete geometry only at registration. Resource-owned range views share static GPU attributes
and retain independent near/far draw ranges. Views remain owned until resource retirement because Three disposes
shared attribute storage when a view geometry is disposed. Dynamic drops, alternate layouts and splashes retain
the transient path. Worker packets carry registrations and destruction in the same ordered queue as draws; warmed
static draws contain only identity, range and uniforms. Profiling exposes cumulative registration bytes, creations,
draws, destruction and live resources as `rainGeometry`.

These are internal JavaScript presentation commands, requiring a matched native/worker/browser package. No exported
MonoGame function or argument changes, so the native interface remains version 7. The fork-authored `web/` sources
are documented here and are not duplicated into the upstream runtime patch.

Native compute shaders are not part of the adapter. Accidentally routing an old compute draw through it raises an
explicit error instead of silently dropping the pass. New Three rendering features belong in the scene renderer.

## Verification

The adapter is checked with Emscripten's C++17 compiler and warnings as errors. Headless external-renderer tests cover
the production mesh/pose path, shared resources, portraits, settings and disposal; warmed draw callbacks allocate no
managed memory. JavaScript regressions cover packed WASM resources, world bone palettes, ordered portrait targets,
partial texture updates and depth conversion. The production-linked native recorder also exercises both compiled Rain
vertex shaders, complete Vector4 payloads, near/far index ranges, float occlusion uploads and invalid-input refusals.
Browser GPU checks exercise both WebGPU and WebGL 2, followed by the
normal login, character creation and world-entry flows.
