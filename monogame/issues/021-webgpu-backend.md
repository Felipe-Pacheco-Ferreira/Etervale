# 021. A browser runtime: WebGPU graphics, a canvas platform, WebAssembly build

Part: runtime, framework.

## Need

Etervale runs in a browser as a third client ([../../docs/web-client.md](../../docs/web-client.md)). The renderer, the
HUD and the screens should stay one implementation for every client, so the browser needs the fork itself, not a second
engine. The runtime only had a Vulkan backend on SDL2 and FAudio, built as a desktop or Android shared library;
browsers have no Vulkan, the .NET WebAssembly runtime links native code statically, and a page owns its frame loop.

## Change

The runtime gets a browser build next to the Vulkan one, with the same exports and interface version:

- `src/webgpu/MGG_WebGPU.cpp`: the graphics backend over WebGPU, through Emscripten's `webgpu.h`. The page creates the
  WebGPU device before the .NET runtime starts (device creation is asynchronous in a browser) and hands it over as
  `Module.preinitializedWebGPUDevice`; the backend takes it with `emscripten_webgpu_get_device` and draws into the canvas
  the window handle names (a swapchain on a canvas surface, presented when control returns to the browser).
  `MGG_GraphicsDevice_GetNativeHandles` reports the device and its queue.
- `src/web/MGP_web.cpp`: the platform, in place of SDL2. The window is the page's canvas (`#canvas`), its native handle
  the canvas selector. The canvas's drawing size follows its laid-out size times the device pixel ratio, capped by
  the page's `maxPixelRatio` on `Module.noxiusThreeGraphics` when set (a window resize event refits it); a change is
  queued as `WindowResized`, and window focus as `WindowGainedFocus` / `WindowLostFocus`. Keys, mouse buttons, moves
  and the wheel reach the canvas (keys while it has the focus, which the page gives it with a tabindex): a key event's
  `keyCode` is the Windows virtual-key code the XNA keys share, apart from the two-sided modifiers (told apart by the
  location) and the punctuation codes Firefox numbers differently; the one character a key types (its `key`, without
  Control or Meta, AltGr allowed) is queued as `TextInput`; Backspace, Tab, Enter, Escape and Delete carry their control character as the
  desktop platform's do; coordinates are scaled from CSS to drawing pixels; a button still held when the pointer leaves
  the canvas is released. The keys the game takes (typed characters, those control keys, Space, the arrows) keep their
  browser action from running; shortcuts with Control or Meta and the function keys keep theirs. Text composition
  (IME) is not handled yet. Touch is not the platform's: the page reads the canvas's touches and hands them to the
  game itself, as the Android client reads SDL's (Etervale's `ITouchFingers`). `MGP_Platform_Create` asks for the
  asynchronous run behaviour, `MGP_Platform_GetPlatform` reports `WebGL` (MonoGame's browser platform) and
  `MGP_Platform_GetGraphicsBackend` the new `MGGraphicsBackend::WebGPU` (`include/api_enums.h`; `GraphicsBackend.WebGPU`
  in the framework).
- `src/web/MGA_web.cpp` and `src/web/MG_Storage_web.cpp`: silent audio voices (they keep the state the framework reads
  back and consume every buffer at once; issue 025 replaced them with Web Audio) and an XNA storage API without a
  device, in place of FAudio and the desktop file system.
- `src/web/mgruntime.cpp`: the browser build as one translation unit (the common sources, the web platform, the WebGPU
  backend). The .NET WebAssembly SDK resolves a `DllImport` by the file name of the native file it links, so the
  framework's `DllImport("mgruntime")` needs a file of that name. `src/web/BrowserRuntime.props` links it into a
  `Microsoft.NET.Sdk.WebAssembly` project: `WasmBuildNative`, the file as a `NativeFileReference`, `-sUSE_WEBGPU=1` and
  the include paths. The SDK's Emscripten compiles it at the native link, so such a project needs the `wasm-tools`
  workload; no separate Emscripten install or build script is involved.
- `src/include/mg_etervale.h`: the interface version (`MG_ETERVALE_RUNTIME_VERSION`, still 7), shared by both
  backends; it was a define in `vulkan/MGG_Vulkan.cpp`.
- `src/common/MGM.cpp`: WebAssembly has neither SSE nor NEON, so the browser build decodes MP3 with minimp3's portable
  code (`MINIMP3_NO_SIMD`); the desktop and Android builds keep `MINIMP3_ONLY_SIMD`.

The framework (`Platform/Native/GamePlatform.Native.cs`): under the asynchronous run behaviour, which only the browser
runtime asks for, `Game.Run` returns after initialization and the page calls `Game.Tick` on each animation frame;
`StartRunLoop` shows the window, and `BeforeUpdate` polls the platform's events and runs the queued main-thread work,
which `RunLoop` does on desktop.

This change gives the backend the device, the canvas swapchain, `Clear` and `Present`; resources, pipelines and draws
are [022](022-webgpu-effects.md). Timestamps, frame capture and pipeline statistics report themselves unavailable (the
device does not request WebGPU's optional timestamp feature).

## Test

`runtime/repro/WebGpuRepro`: the fork's framework and the browser runtime published for WebAssembly; a `Game` whose
`Draw` clears the canvas. The page lists four checks: the runtime reports the WebGPU backend, `Game.Run` returned
(asynchronous run behaviour), the back buffer has the canvas's drawing size (checked again after resizing the window),
and `Draw` ran on animation frames. Chromium with WebGPU on Windows: all pass, the canvas shows the clear colour, the
console stays empty, and resizing the viewport to 500×700 CSS pixels at a device pixel ratio of 2 gives a 1000×980
canvas and back buffer.

The input is exercised by the browser client (`Client/PristonTale.Browser`), whose login screen takes clicks, Tab,
typed characters, Backspace and Enter on the canvas ([../../docs/web-client.md](../../docs/web-client.md)).
