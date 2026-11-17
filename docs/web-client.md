# Web client

The browser client in `Client/PristonTale.Browser`: Etervale running on .NET WebAssembly inside the website's
entry page (`/`), next to the desktop and Android clients. The MonoGame fork draws the game into the page's
full-window canvas through WebGPU, with the page's own login panel over it ([Entry page](#entry-page)); it logs in over a WebSocket with the same
`GameServerConnection` as the other clients and then lists the account's characters. The 3D scenes follow the
[Roadmap](#roadmap).

## Read this when

- You change the browser client, its page on the website, or how the site serves it.
- You change the WebSocket transport or the game server's WebSocket edge.
- You work on a step of the roadmap below (WebGPU backend, shaders, field packs in the browser).

## Code map

| Path | Responsibility |
| --- | --- |
| `Client/PristonTale.Browser/PristonTale.Browser.csproj` | `Microsoft.NET.Sdk.WebAssembly` app (`browser-wasm`, no Blazor) referencing `Gameplay`, with the fork's browser runtime linked in (`BrowserRuntime.props`, needs the `wasm-tools` workload); untrimmed; its publish copies the login's files beside the client and lists them in `bootstrap.txt` (`EtervaleBootstrapFiles`) |
| `Client/PristonTale.Browser/BrowserHost.cs` | `[JSExport]` bridge for the page: `PrepareAsync` (fetch the files), `Start` (endpoint, language), `Frame` (one `Game.Tick`), `SubmitLogin` (the page's form); `reportLogin` back to the page |
| `Client/PristonTale.Browser/BrowserGame.cs` | WebGPU host for the shared desktop screen lifecycle, using the Low preset |
| `Client/PristonTale.Browser/BootstrapFiles.cs` | Fetches the files `bootstrap.txt` lists into the runtime's in-memory file system, where `ContentManager` and `AssetFiles` read them |
| `Client/PristonTale.Browser/Content/Content.mgcb` | The desktop client's content sources built for MGCB's `Web` platform (the UI font so far) |
| `Client/PristonTale.Browser/Program.cs` | Empty entry point; marks the assembly browser-only |
| `Client/PristonTale.Browser/wwwroot/main.js` | ES module the page loads: creates the WebGPU device, starts the .NET runtime, drives `BrowserHost` from `requestAnimationFrame`, runs the login form, the language picker and the progress line |
| `Client/PristonTale.Browser/publish.ps1`, `publish.sh` | Release publish into the website's `Play:ClientPath` folder, replacing the previous build |
| `Shared/PristonTale.Protocol/Transport/WebSocketTransport.cs` | Frames over a WebSocket, client and server side ([protocol.md](protocol.md#websocket-transport)) |
| `Server/PristonTale.Server/Net/GameTcpServer.WebSocket.cs` | The game server's WebSocket edge ([server.md](server.md#websocket-edge)) |
| `Web/PristonTale.Web/Pages/Jogar.cshtml`, `wwwroot/css/play.css`, `Services/BrowserClient.cs` | The entry page (`/`) and the `/play` files ([website.md](website.md)) |
| `monogame/runtime/src/webgpu/MGG_WebGPU.cpp` | The fork's WebGPU graphics backend: the same exports as the Vulkan one ([monogame/issues/021](../monogame/issues/021-webgpu-backend.md)) |
| `monogame/runtime/src/web/` | The browser platform (the page's canvas as the window, its events), silent audio, storage without a device, `mgruntime.cpp` (the browser runtime as one translation unit) and `BrowserRuntime.props` (links it into a WebAssembly project) |
| `monogame/runtime/repro/WebGpuRepro/` | Verification program: the fork's framework and the browser runtime clearing a canvas through WebGPU, drawing a texture with `SpriteBatch` and running a compute check (`Content/ComputeCheck.fx`), with its checks and canvas samples on the page |
| `monogame/pipeline/MonoGame.Effect.Compiler/Effect/ShaderProfile.WebGpu.cs`, `fetch-slang.sh`/`.ps1`, `StockEffects/` | The effect processor's WebGPU profile (WGSL by Slang over the Vulkan build's reflection, MGCB platform `Web`), the pinned Slang it needs, and the browser runtime's stock effects ([monogame/issues/022](../monogame/issues/022-webgpu-effects.md)) |

## How it fits together

```text
browser ──https──▶ website  / (page, canvas, CSP) + /play/* (main.js, _framework/*.wasm, bootstrap.txt, Content/, assets/)
   │
   └──wss──▶ game server WebSocket edge (Network:WebSocket) ──▶ FrameConnection ──▶ ClientSession
```

1. `/` renders the canvas (`#canvas`, the runtime's window) over the whole window, without the site's layout, with `data-endpoint` set to
   `Play:GameEndpoint`, and loads `/play/main.js` as a module. Its content security policy allows the site's scripts,
   WebAssembly and a connection to that endpoint's origin only.
2. `main.js` creates the WebGPU device (a browser without WebGPU gets a message), then the .NET runtime with the device
   as `preinitializedWebGPUDevice`, and takes `BrowserHost` from the assembly exports. The runtime, the assemblies and
   the native module are about 24 MB (uncompressed, untrimmed, AOT-less).
   The browser publish includes the original minimap frame, markers, field images and titles.

   Bootstrap downloads retry transient network and server errors up to twice.

3. `BrowserHost.PrepareAsync` fetches the opening's files from `bootstrap.txt` into the in-memory file system.
   `Start` creates and runs `BrowserGame`, and the page calls `Frame` on each animation frame.
4. The login screen runs the desktop `OpeningScreen` and `OpeningLoginController` behind the page's form
   ([Entry page](#entry-page)). Its `GameServerConnection` for the `wss://` address picks `WebSocketTransport` from
   the scheme; `ClientWebSocket` is the browser's own WebSocket, so TLS and the certificate check are the browser's.
5. The game server's edge checks the page's `Origin`, applies the TCP caps and runs the same `ClientSession`.

The .NET runtime in the browser runs on one thread: every `await` continues on the page's event loop, and a blocking
wait on an unfinished task throws. `GameServerConnection` and `FrameConnection` only await, so they work unchanged.

## Entry page

The site's address (`/`) is the game: the canvas fills the window, and the page's HTML panels sit over it.

- The page shows progress while the browser initializes WebGPU and loads the opening scene. The game canvas then plays
  the live flight from the start. The start phases are logged in the console (`[Partida]` from the page, `[Startup]`
  and `[Opening]` from the game).
- The HTML login leaves the desktop login atlas out of the browser build and startup. The page tries to play the
  original `Intro.wav` through HTML audio during loading. The speaker button starts playback when autoplay is blocked
  and also toggles the music. Entering character selection stops the opening music.
- The roster's models and arrival effect begin fetching one second after the live scene starts, four at a time. Other
  assets are fetched when entering the world; the scene's own files carry their hash in `bootstrap.txt`
  and are asked for as `?v=<hash>`, which the site caches for good, so a reload reads them from the browser.
- The browser opening and character screens share the mountain sanctuary built by `WebArrivalLandscape`. The
  camera descends to the stone courtyard while the login appears and continues across the same moonlit terrain when the game server
  accepts the login. Character selection takes ownership of the same renderer, camera, stage and flame effects at the
  roster pose. The characters appear in succession with a golden floor sigil, light and sparks. The browser frames
  the lead character closer in the roster. In creation, the chosen tribe's classes appear with the same golden effect.
  The first class is selected when the lineup appears. Choosing a different class moves it up a step with the gold
  floor marker as the previous class returns to its slot. The chosen class runs up to the appearance and name step
  while the rest fade away. A mouse drag turns the preview model. Going back restores the lineup, which stays visible
  between the two tribe cards if the player backs up another step. On success, the new character appears with the
  golden effect in the roster. The opening manifest loads the sanctuary's textures, flame frames and night sky
  before the first frame.
- The files come in waves (`BootstrapFiles`): the login scene's before the game starts (`PrepareAsync`), everything up
  to the character list right after (`FetchRemainingAsync`), and the first field's when a character enters. The roster
  fetches additional starter models for classes on the account through `character-previews.txt`. Once the initial
  opening downloads finish, idle background requests warm both tribes' starter models. The selected tribe shares
  those requests. The web host compresses the large character motion banks with Brotli, and the class lineup prepares
  only the default face for each model. In the browser preview, the skeleton reader decodes only the bind pose and
  keys used by the idle, walk and run motions. The chosen class's other two faces are prepared as it runs forward.
  The sky builds a profile the first time it is drawn (`PristonSkyLibrary.LoadBootstrap(preloadAll: false)`), so the login
  downloads and decodes only the opening's sky. Downloads for other sky profiles wait until the first field loads.
- The login panel ("Bem-vindo") is HTML, not `LoginPanel`: real form fields, so paste, password managers and phone
  keyboards work. `BrowserHost.Start` sets `WebLoginOverlay.Report`; while it is set, `OpeningScreen` takes the login at
  once and `OpeningLoginController` neither updates nor draws `LoginPanel`. The controller reports `form` (ready, or
  failed with the localized reason), `busy` (connecting) and `done` (logged in: the page hides its panels and gives the
  canvas the focus). A new `OpeningScreen` (back from the character list) reports `form` again and the panel returns.
- After the account and password the same panel changes to "Escolha o mundo": the worlds of `/api/worlds` (the site's
  `Play:Worlds`, [website.md](website.md)), each with the load its game server's edge reports at `GET /status`
  (`{ players, capacity }`, readable from the allowed origins only; the request's time is the ping shown). Choosing a
  world calls `SubmitLogin(account, password, endpoint)`: the controller switches to that server
  (`ScreenContext.SelectServer`) and logs in there. A refused login returns to the form with its reason.
- "Manter conectado" opts in to a 14-day browser session. On a reload, "Contas recentes" shows the valid session and
  lets the player continue to world selection without an entered password. The ticket is requested only when the
  client can send it. "Esquecer conta" clears this browser's session and its remembered name.
- A world chosen before the game and the files up to the character list are ready waits in the page and goes in then.
- The character list and creation are the page's too (`heroes.js`): `CharacterSelectScreen.Web.cs` reports its state as
  JSON (`reportCharacters`) whenever it changes and takes the page's `CharacterCommand(kind, index, text)` (select,
  enter, delete, new character, tribe, class, next, face, create with the name, back, exit); clicks on the 3D stage
  still pick a character or a class, and `CharacterSelectView` is not drawn. The roster reads the class, face and
  equipped item keys from the character summaries. The page fetches the corresponding TGA and BMP art on demand,
  decoding the protected headers and drawing the results on canvases.
- "Lembrar conta" keeps the account name (never the password) in the browser's `localStorage` (`avalon.account`); the
  language picker keeps `avalon.language`. The game takes its language at `Start`, so changing it afterwards reloads the
  page.
- The gear opens the site's menu (portal at `/inicio`, news, rankings, events, download, account, support); the other
  button toggles full screen for the whole page, panels included.
- The desktop and Android clients never set `WebLoginOverlay.Report` and keep `LoginPanel`.

## Run it

On a development machine (Windows shown; the `.sh` script takes the same folder):

1. Trust the ASP.NET development certificate once: `dotnet dev-certs https --trust`. The website and the WebSocket edge
   both use it on `localhost`.
2. Start the game server with the edge enabled and the site's origin allowed:

   ```bash
   dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Network:WebSocket:Enabled=true --Network:WebSocket:Url=https://localhost:10010 --Network:WebSocket:AllowedOrigins:0=https://localhost:5443
   ```

3. Publish the client into the site's folder (`bin/Release/net10.0/play` for a site run from the repository):

   ```bash
   pwsh Client/PristonTale.Browser/publish.ps1 -Target Web/PristonTale.Web/bin/Release/net10.0/play
   ```

4. Start the site with the endpoint set, then open `https://localhost:5443/`:

   ```bash
   dotnet run --project Web/PristonTale.Web -c Release --no-build -- --Database:SqlitePath=<copy-of-the-database.db> --Play:GameEndpoint=wss://localhost:10010/game
   ```

In production the site's reverse proxy also forwards `wss://<site host>/game` to the edge on loopback (`Url` on
`http://127.0.0.1:10010`, the proxy in `KnownProxies`), so the page and the socket share the site's certificate.

## Chosen stack

| Layer | Choice | Why |
| --- | --- | --- |
| Runtime | .NET 10 WebAssembly (`Microsoft.NET.Sdk.WebAssembly`), no Blazor UI | Runs `Client.Net`, `Protocol`, `Simulation`, and later Gameplay and Engine3D, as they are |
| Graphics | A WebGPU backend for the MonoGame fork: the native runtime compiled by the Emscripten of the `wasm-tools` workload, a WebGPU implementation beside the Vulkan one, a canvas platform in place of SDL2 (Emscripten's `html5.h` events), linked into `dotnet.native.wasm` | Keeps one renderer, one HUD and one set of screens. WebGPU has compute shaders and storage buffers, which the renderer needs (`LightCull`, `PostExposure`, `Environment`); WebGL2 has neither |
| Shaders | The existing HLSL compiled to WGSL by Slang at content build time, in the effect processor's WebGPU profile (MGCB platform `Web`), with the reflection of the Vulkan build | One shader source and one reflection; no runtime translation in the browser. Slang has official builds for the three systems (naga and Tint, which convert SPIR-V, have none) |
| Network | WebSocket edge in the game server itself | Same session, limits and logs; the player's real address is counted (a relay inside the site would show every player as the site's address and trip the per-address limits) |
| Assets | The per-field packs the Android client is getting, served by the website and kept in the browser's Cache Storage or OPFS; a pack is fetched before its field loads and mounted in memory for `AssetFiles` | 6 GB of raw assets cannot load at once; the browser shares the Android pack format instead of a second one |
| Hosting | The website (`/`, `/play`) | One origin and certificate for the page, its files and (behind the proxy) the socket |

Rejected: a WebGL2 MonoGame-compatible port (KNI and similar) needs shader model 2/3 effects and has no compute, so the
renderer and every shader would be written again outside the fork; a JavaScript engine (three.js, Babylon.js) over glTF
exports would be a second renderer, HUD and UI.

## Roadmap

| Phase | Goal | Work |
| --- | --- | --- |
| 0 (done) | Login in the browser | WebSocket transport and edge, the browser project, the play page and `/play` on the site |
| 1 (in progress) | The fork draws in a canvas | Done: the runtime built for the browser with the `wasm-tools` workload, the canvas platform, the WebGPU device created in JavaScript before the runtime starts, the canvas swapchain, `Clear` and `Present`, one `Game.Tick` per `requestAnimationFrame`; the effect processor's WebGPU profile (WGSL by Slang); buffers, textures, samplers, states, pipelines and draws in the backend; `SpriteBatch` drawing a texture (`WebGpuRepro`); keyboard, mouse and wheel on the canvas; the UI font built for the `Web` platform and the HUD atlas fetched from the site; the desktop login (`OpeningLoginController`, `LoginPanel`) drawn and used in `/jogar`, then the account's characters as text. Next: phase 2 |
| 2 (in progress) | The opening and login screens | Done: the client's 18 effects build for `Web` and their 84 shaders compile in the browser (atomics through the processor's `MGFX_ATOMIC_*` macros); compute dispatches and storage buffers in the backend. Next: the opening's files (stage, sky, effects) fetched for the browser; Engine3D's passes on WebGPU at the Low preset; every `Task.Run`, `.Result` and `.Wait()` on the opening's path made asynchronous or prepared ahead |
| 3 | Enter Ricarten | Field packs from the site, cached in the browser, mounted for `AssetFiles`; a download screen with progress; compressed textures (BC on desktop, ASTC or ETC2 on phones when the adapter has them) |
| 4 | The whole game | Audio through Web Audio, touch and text composition (IME, paste), field changes, memory and frame-time measurements, a trimmed and compressed download, production behind the proxy |

## Continuing phase 2

Where the work stands and the order of what comes next, so a contributor can pick it up from the code alone.

What the backend has and lacks, against what Engine3D asks of it:

| Engine3D needs | WebGPU backend today |
| --- | --- |
| Draws, render targets (HalfVector4, R32F, Depth32Float), samplers, states | Yes (issue 022) |
| Compute (`LightCull`, `PostExposure`, `Environment` irradiance), structured buffers | Yes: dispatch, indirect dispatch, read-only and writable storage buffers |
| Storage images (`RWTexture2D`) | No; no client shader writes one |
| MSAA (`Msaa` mode) | No: caps report one sample, so the Low preset's TAA/FXAA paths are the ones to run |
| GPU timestamps (`GpuTimestampPool`), frame capture, pipeline statistics | Report themselves unavailable; the profiler must tolerate it |
| Texture and back-buffer read-back | No (asynchronous in WebGPU); no frame path may read back |
| BC textures | No (`texture-compression-bc` not requested) |
| Songs (`Song`) | Silent: decoding needs a thread |

Next steps, in order; each ends with something checkable in the browser:

1. **The opening's files in the browser.** `OpeningCinematic` reads `StartImage/Opening/` (stage, scenery, NPC models
   and skeletons, the camera path: about 51 MB), and `GameClient.LoadContent` also loads the sky (`sky/`, about 7 MB),
   the effect library (part of `Effect/`), the cursor and the audio. Extend `EtervaleBootstrapFiles` (or the list it
   writes) with exactly the files the opening reads; the safest list comes from recording `AssetFiles` reads while the
   desktop client shows the opening, not from whole folders. The field packs of phase 3 replace the list later. Check:
   `BrowserHost.PrepareAsync` writes them and `AssetFiles.Exists` finds each one in the browser.
2. **The client's effects in the browser content.** Add the 18 shader blocks of `Client/PristonTale/Content/Content.mgcb`
   to `Client/PristonTale.Browser/Content/Content.mgcb` (same sources, `Web` platform) so they reach `bootstrap.txt`.
   Check: each loads with `Content.Load<Effect>` in the browser.
3. **Remove blocking waits from the opening's path.** The browser runs .NET on one thread: `Task.Run` still works (its
   work runs later on the same thread), but `.Result`, `.Wait()` or `GetAwaiter().GetResult()` on an unfinished task
   throws. Engine3D, Gameplay and Effects have about 40 such calls and 24 files with `Task.Run`; only those the opening
   reaches need changing first (a `.Result` behind `IsCompleted`, as in `OpeningLoginController`, is safe). Prefer
   preparing data before the frame (as `HudAtlas` does) over new synchronous paths.
4. **Validate the shared screens on WebGPU.** The browser creates the same GameClient as the desktop, using the Low preset.
   Confirm the opening renders, its speaker button plays the opening music, and login reaches the shared character selection screen.
5. **Fix what the renderer trips on**, pass by pass, using the frame graph in [rendering.md](rendering.md#frame-graph):
   a WebGPU validation error names the pass (wrap frames in `pushErrorScope('validation')` from the page, as
   `WebGpuRepro`'s checks do). Every fix that belongs to the fork goes into `monogame/` with its issue note.
6. **Compare** a capture of the opening in the browser with the desktop client's at the same preset.

How the current state was verified, and how to check a change the same way:

- `monogame/runtime/repro/WebGpuRepro`: ten checks on the page (backend, run behaviour, canvas size, frames, sprite
  colours, and the compute check). Publish it, serve `wwwroot` on `http://localhost`, open it in Chromium.
- Every shader of the client compiling for the browser: build the effects for `Web`, take the WGSL out of each built
  effect, and compile it on the page's device with `device.createShaderModule` and `getCompilationInfo`. Slang
  accepting a shader does not mean the browser does (the uniform alignment pitfall below); all 84 pass today.
- The Vulkan side of a shader change: build the effects for `DesktopVK` before and after and compare the files; an
  effect whose source did not change must stay byte for byte the same. A changed effect needs the regression runner's
  `--verify-shaders <content>` on a machine with the desktop runtime, and a capture.

## Pitfalls

- The browser accepts no certificate pin and no self-signed certificate on `wss://`: the edge needs a certificate the
  browser trusts (the site's, through the proxy; the development certificate on `localhost`). `ws://` is refused by
  `TlsTransport.ParseAddress` for anything but a server on the same machine.
- A page origin missing from `Network:WebSocket:AllowedOrigins` gets 403 at the upgrade; the browser only reports a
  failed connection.
- WebAssembly memory is 32-bit (at most 4 GB, far less on phones), and .NET in the browser has no 64-bit memory: only
  the current field's pack and the shared pack can stay loaded.
- `System.Net.Sockets` and `SslStream` throw in the browser, and the file system is the runtime's in-memory one, empty
  until `BootstrapFiles` writes the listed files and lost when the page closes. Code the browser client reaches must go
  through `GameServerConnection`'s transports and read only those files or, from phase 3, the mounted packs.
- `Client/PristonTale.Browser` imports `BrowserRuntime.props`, so it, and with it `PristonTale.sln`, needs the
  `wasm-tools` workload: `dotnet workload install wasm-tools`. That command also updates the other workloads already
  installed (Android) to the newest version of the SDK band, so rebuild the Android client after it. Its publish also
  needs the game data's `assets/` folder, including the font and opening scene.
- The browser client is published untrimmed: the game and MonoGame create types by reflection (content readers, JSON),
  which the trimmer reports as errors. After a publish that ran the trimmer, delete the project's `bin` and `obj`: the
  native link's interop tables are cached there, and tables built from trimmed assemblies stop the runtime at start with
  "runtime and class libraries are out of sync".
- Keys reach the game as `keydown`/`keyup` on the focused canvas. Text inserted without key events (paste, a password
  manager's fill, the soft keyboard of a phone, an IME) does not reach the game's own text fields (chat) yet; the login is
  the page's HTML form, so it is not affected.
- The browser runtime's songs decode on a `std::thread` (`common/MGM.cpp`), which a single-threaded WebAssembly build
  cannot start: `Song` playback waits for the Web Audio stage (phase 4).
- The page must create the WebGPU device and pass it as `preinitializedWebGPUDevice` to `dotnet.withModuleConfig`
  before the runtime starts; without it `GraphicsDevice` creation fails. A page in a hidden tab or pane gets no animation
  frames, so the game stops ticking until it is shown.
- Effects for the browser are built for MGCB's `Web` platform, which needs Slang: `monogame/pipeline/fetch-slang.sh`
  (or `.ps1`) once per machine. The browser runtime refuses effects built for `DesktopVK` (shader profile 81 against 80).
- Shader rules the WebGPU build adds ([shaders.md](shaders.md#conventions)): atomics only through the processor's
  `MGFX_ATOMIC_*` macros; a `float3` starts a 16-byte register of a constant buffer (the browser refuses a uniform
  `vec3` off 16 bytes when it compiles the shader, after a content build that succeeded); a `for` variable's name is not
  reused later in the function.
- The MGCB build task runs `dotnet` from the `PATH`, so a build with a private SDK needs that SDK first on the `PATH`.
- The browser runtime cannot read textures or the canvas back (`GetData` returns zeros): WebGPU maps buffers
  asynchronously, which a synchronous MonoGame call cannot wait for. A check of what was drawn reads the canvas from
  JavaScript, in the task that drew it, as `WebGpuRepro` does.
- The site serves `/play` files uncompressed; the publish's `.br` and `.gz` siblings are not used yet. Behind a
  reverse proxy, let the proxy compress.

## Related

- [protocol.md](protocol.md#websocket-transport): the WebSocket transport.
- [server.md](server.md#websocket-edge): the edge's settings, caps and layouts.
- [website.md](website.md): the play page, its policy and the `Play:*` settings.
- [client.md](client.md): `GameServerConnection` and the desktop client the browser will share.
- [android.md](android.md): the Vulkan Android client and its asset packages.

Touch taps on the browser canvas are translated to primary mouse clicks for the shared game screens.
