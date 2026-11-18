# Web client

The browser client in `Client/PristonTale.Browser`: Etervale running on .NET WebAssembly inside the website's
entry page (`/`), next to the desktop and Android clients. Three.js draws the live scenes, native HUD and effects into
one full-window canvas through WebGPU, with WebGL2 fallback. C# retains the screens, animation timelines, collision,
input, gameplay and `GameServerConnection`; HTML supplies the login and character controls ([Entry page](#entry-page)).

## Read this when

- You change the browser client, its page on the website, or how the site serves it.
- You change the WebSocket transport or the game server's WebSocket edge.
- You change renderer capabilities, browser materials, asset waves or GPU verification.

## Code map

The browser installs `BrowserExternalSceneRenderer` through `PristonRenderer.ExternalSceneRendererFactory` before
constructing a screen. Meshes, texture layers and decoded RGBA textures cross `noxius-three-scene` once per resource;
negative texture IDs separate scene resources from the native HUD adapter. Reusable byte spans carry Float32 camera,
lighting, actor appearance and dirty WORLD bone palettes. JavaScript copies every borrowed span before returning;
the C# game retains animation and input authority. Portraits flush pending poses before drawing all actor attachments
into the HUD's current render target. Sky mesh IDs are sent only when a profile changes; camera height and crossfade
use scalar updates. Scene disposal releases its JS meshes, materials and textures without creating a MonoGame 3D device.

| Path | Responsibility |
| --- | --- |
| `Client/PristonTale.Browser/PristonTale.Browser.csproj` | `Microsoft.NET.Sdk.WebAssembly` app (`browser-wasm`, no Blazor) referencing `Gameplay`, with the fork's browser runtime linked in (`BrowserRuntime.props`, needs the `wasm-tools` workload); untrimmed; its publish copies the login's files beside the client and lists them in `bootstrap.txt` (`EtervaleBootstrapFiles`) |
| `Client/PristonTale.Browser/BrowserHost.cs` | `[JSExport]` bridge for the page: `PrepareAsync` (fetch the files), `Start` (endpoint, language), `Frame` (one `Game.Tick`), `SubmitLogin` (the page's form); `reportLogin` back to the page |
| `Client/PristonTale.Browser/BrowserHost.Settings.cs` | `[JSExport]` bridge for the page's Settings panel: the settings document in and out, presets, shortcut keys ([Entry page](#entry-page)) |
| `Client/PristonTale.Browser/BrowserGame.cs` | Variable-step host for the shared screen lifecycle, one Update/Draw per dispatched animation callback, starting from saved settings |
| `Client/PristonTale.Browser/BrowserExternalSceneRenderer*.cs`, `BrowserSceneTextures.cs` | Resource and packed-frame bridge; CPU texture decoding and authoritative C# pose updates |
| `Client/PristonTale.Browser/wwwroot/three-scene.js`, `three-bridge.js`, `three-native.js`, `three-rain.js`, `three-display.js`, `three-camera.js`, `three-post.js`, `three-batching.js`, `three-profile.js` | Scene, packed bridge, native and rain batches, explicit display encoding, supplied camera projections, HDR/post composition, spatial static batching and opt-in CPU submission diagnostics |
| `Client/PristonTale.Browser/package.json`, `pnpm-lock.yaml`, `vendor-three.mjs`, `wwwroot/vendor/three/` | Pinned Three release and self-hosted official ES bundles with MIT license |
| `Client/PristonTale.Browser/BootstrapFiles.cs` | Fetches the files `bootstrap.txt` lists into the runtime's in-memory file system, where `ContentManager` and `AssetFiles` read them |
| `Client/PristonTale.Browser/Content/Content.mgcb` | UI font and compatible effect metadata built for MGCB's Web platform; actual scene shaders are Three node materials |
| `Client/PristonTale.Browser/Program.cs` | Empty entry point; marks the assembly browser-only |
| `Client/PristonTale.Browser/wwwroot/main.js` | Initializes Three, installs the scene module and native graphics adapter, starts .NET, drives `BrowserHost` from requestAnimationFrame and runs the page controls |
| `Client/PristonTale.Browser/wwwroot/settings.js` | The gear's Settings panel: tabs and cards built from a row table, saved in `localStorage` |
| `Client/PristonTale.Browser/publish.ps1`, `publish.sh` | Release publish into the website's `Play:ClientPath` folder, replacing the previous build |
| `Shared/PristonTale.Protocol/Transport/WebSocketTransport.cs` | Frames over a WebSocket, client and server side ([protocol.md](protocol.md#websocket-transport)) |
| `Server/PristonTale.Server/Net/GameTcpServer.WebSocket.cs` | The game server's WebSocket edge ([server.md](server.md#websocket-edge)) |
| `Web/PristonTale.Web/Pages/Jogar.cshtml`, `wwwroot/css/play.css`, `Services/BrowserClient.cs` | The entry page (`/`) and the `/play` files ([website.md](website.md)) |
| `monogame/runtime/src/webgpu/MGG_WebGPU.cpp` | The fork's WebGPU graphics backend: the same exports as the Vulkan one ([monogame/issues/021](../monogame/issues/021-webgpu-backend.md)) |
| `monogame/runtime/src/web/MGG_Three.cpp` | CPU MonoGame resource and HUD/effect adapter for the browser; forwards rendering to Three instead of owning a second graphics device |
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
2. `main.js` initializes the pinned Three WebGPURenderer, which selects WebGPU or WebGL2. It passes the adapter as
   `noxiusThreeGraphics` to .NET's module configuration, registers `noxius-three-scene`, and reads `BrowserHost` from the
   assembly exports. `?renderer=webgl2` selects the real WebGL2 backend for compatibility verification, without browser flags.
   The browser publish includes the original minimap frame, markers, field images and titles.

   Bootstrap downloads retry transient network and server errors up to twice.

3. `BrowserHost.PrepareAsync` fetches the opening's files from `bootstrap.txt` into the in-memory file system.
   `Start` installs the external renderer strategy and creates `BrowserGame`; the page calls `Frame` from its paced
   animation callback. The host disables fixed-step catch-up and inactive sleeping because the browser owns pacing.
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
  roster pose. Returning from gameplay creates that same courtyard directly at the roster pose after the old world
  releases its resources; it does not load the original desktop temple or replay the opening flight. The characters
  appear in succession with a golden floor sigil, light and sparks. The browser frames
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
  opening downloads finish and the login form has painted, background requests fetch both tribes' starter models.
  The publish generates compact motion banks for preview poses and idle, walk and run clips. The web host sends those
  banks with Brotli, and the field still fetches the full banks. Once login succeeds, the courtyard prepares and
  uploads one downloaded class at a time, reusing the same graphics resources when the player selects a tribe. The chosen class's
  other two faces are prepared as it runs forward.
  The sky builds a profile the first time it is drawn (`PristonSkyLibrary.LoadBootstrap(preloadAll: false)`), so the login
  downloads and decodes only the opening's sky. Downloads for other sky profiles wait until the first field loads.
- **Enter the world** hides the roster controls and pulls the live courtyard camera back while the browser exports
  map-loading progress through `reportWorldLoading`. The bar advances with completed asset downloads and then with
  the server and scene initialization milestones. It displays the destination name once the server confirms the field.
  The first completed gameplay frame dismisses the overlay. A failed entry returns to the roster with its error.
- The in-world HTML shortcuts appear after the first gameplay frame. The world label uses the selected server,
  FPS counts dispatched game frames, and ping measures that world's status response. News, hunt, and the More menu
  dispatch to the live HUD controls. URL-backed shortcuts are disabled until configured. The game canvas keeps the inventory,
  character, skills, minimap, chat and other gameplay windows. The web HUD centers the legacy bottom bar and chat
  and opens the same settings dialog from the gear, the system menu and the settings binding.
- Social uses a centered browser panel with Group, Friends, Clan, Blocked and Recent tabs. Name entry captures
  keyboard input before chat and gameplay shortcuts. Contact actions use the saved contact lists, whispers open the
  chat composer, and profiles show known presence, party and clan information. Group creation, admission policy,
  search and joining use the authoritative party commands and directory. The original language, settings and
  fullscreen controls move into the More menu's Interface section while playing and return on leaving the world.
- Quests uses Journey, Daily, Monthly and Completed tabs with the real journal and reward board. Daily claims keep
  server validation. Tracker visibility is shared with Settings. Journey navigation uses the current map's collision
  route planner and stops on manual movement, death or a field change. A destination in another map reports the
  required map. The monthly page stays empty until a monthly challenge exists.
- At the standard Ricarten arrival point, the browser starts with a tighter field of view and a steeper camera
  pitch to frame the carpet, lamps and approach to the stairs. The server's position is shared. Mouse camera controls
  take effect once the browser has received pointer movement over the canvas.
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
  pick a roster character or a class. The browser queues both kinds of stage click in canvas coordinates with their input-event
  timestamps and picks the closest visible actor bounds under the pointer. A second click on the same selected class
  within the normal double-press interval enters appearance selection through the same path as Next; it does not
  submit a character. In the roster, the same gesture enters the selected character's world. Each queued pick keeps
  its screen identity, so a class click cannot confirm a roster character. Event times remain distinct when a slow
  frame drains several clicks together. Pointer drags, cancelled gestures, missed picks and other controls reset the gesture. The browser skips
  `CharacterSelectView`. The roster reads the class, face and
  equipped item keys from the character summaries. The page fetches the corresponding TGA and BMP art on demand,
  decoding the protected headers and drawing the results on canvases.
  With `?profile`, `[StageInput]` logs record browser click detail, event time, submitted-pick interval, native double-click
  recognition and drag/cancel suppression. Matching `[StagePick]` logs record the queued stage/time, hit and prior
  selection, pending-gesture interval, confirmation, phase transitions and reset or busy-discard reason. These diagnostics
  do not submit another pick or change the gesture window; they omit account, character name and text-input contents.
- "Lembrar conta" keeps the account name (never the password) in the browser's `localStorage` (`avalon.account`); the
  language picker keeps `avalon.language`. The game takes its language at `Start`, so changing it afterwards reloads the
  page.
- The gear opens the Settings panel (`settings.js`, with rows in `settings-options.js` and styling in the site's
  `css/settings.css`). Vídeo includes texture status, quality presets, WebGPU adapter details, scene render scale,
  the FPS cap and interface options. The cap paces the browser's animation callbacks after the first four frames.
  Áudio groups music, game effects, voice, and microphone controls. Teclas groups the game bindings with per-row and
  global reset. Outros includes language, game, camera and notification options plus Noxius-specific settings.
  Unavailable controls are disabled. The panel edits the
  `client-settings.json` document ([client-settings.md](client-settings.md)) and leaves the rules to the game:
  `BrowserHost.Settings` hands out the current document, `SettingsWithQuality` applies a preset (`WithQuality`),
  `Shortcuts`, `BindShortcut`, `ResetShortcut` and `RestoreShortcuts` use `HudSettingsPanel`'s shortcut list and rebinding (a refused
  key returns an empty string), and `SaveSettings` validates the document, sets `PresentationSettings.Requested` and
  applies its rendering fields at once, keeping the page's language. Changing a preset field in the panel sets the
  quality to Custom, as the game's own panel does. The page keeps the selected browser preset label under
  `avalon.settings.quality`. The saved document is kept in `localStorage` (`avalon.settings`)
  and handed to the game with `LoadSettings` before `Start`; without one the game starts at the Low preset. Leaving the
  page stores the game's current document, so changes made in the game's own panel are kept. Game audio is still
  silent in the browser; the music settings apply to the opening music only. Before the runtime has loaded, the panel
  shows a waiting line that is replaced by the controls when the runtime is ready. The fullscreen button covers the
  whole page, including the panels.
  The enabled "Resolução dinâmica" switch edits the persisted `dynamicResolution` preference without changing the
  selected preset. The renderer reports transient state through
  `settings.setDynamicResolutionState({ enabled, scale, width, height, shadowUpdateRate })`;
  while the actual enabled scale is below the
  saved render-scale cap, the world quality badge appends its actual 3D dimensions and percentage, such as
  `Alto · 3D 1024×576 (80%, dinâmica)`. Restoring the cap or disabling the reported state removes that suffix.
  An optional actual shadow cadence below the saved rate appends `· sombras 30 Hz`, including when scale has returned
  to its cap. An absent/zero cadence or the preset's existing cadence adds nothing. These reports do not save settings
  or mark the preset Custom, and repeated identical labels do not refresh the HUD.
- The desktop and Android clients never set `WebLoginOverlay.Report` and keep `LoginPanel`.

## Run it

On a development machine (Windows shown; the `.sh` script takes the same folder):

1. Trust the ASP.NET development certificate once: `dotnet dev-certs https --trust`. The website and the WebSocket edge
   both use it on `localhost`.
2. Start the game server with the edge enabled and the site's origin allowed:

   ```bash
   dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Network:WebSocket:Enabled=true --Network:WebSocket:Url=https://localhost:10010 --Network:WebSocket:AllowedOrigins:0=https://localhost:5443
   ```

3. Build the self-contained rendering worker, then publish the client into the site's folder
   (`bin/Release/net10.0/play` for a site run from the repository):

   ```bash
   pnpm --dir Client/PristonTale.Browser install --frozen-lockfile --ignore-scripts
   pnpm --dir Client/PristonTale.Browser build:worker
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
| Graphics | Pinned Three WebGPURenderer with WebGL2 fallback, plus the fork's CPU resource adapter | Keeps C# screens, networking, input and HUD actions while Three owns every actual scene/UI draw |
| Materials | Three node materials for indexed scene geometry and native textured/coloured batches | Supports both browser backends without interpreting desktop shaders as JavaScript game logic; native clients keep HLSL/Vulkan |
| Network | WebSocket edge in the game server itself | Same session, limits and logs; the player's real address is counted (a relay inside the site would show every player as the site's address and trip the per-address limits) |
| Assets | The per-field packs the Android client is getting, served by the website and kept in the browser's Cache Storage or OPFS; a pack is fetched before its field loads and mounted in memory for `AssetFiles` | 6 GB of raw assets cannot load at once; the browser shares the Android pack format instead of a second one |
| Hosting | The website (`/`, `/play`) | One origin and certificate for the page, its files and (behind the proxy) the socket |

The browser consumes the existing decoded CPU meshes and C# animation palettes. It does not require a parallel glTF
game export, new collision rules, a second HUD state model or a replacement protocol implementation.

## Renderer verification

The browser entry point uses Three for the complete scene and the native UI, rather than a cosmetic background.
The C# game advances animation, attachments and input once per dispatched callback. The renderer receives immutable
resources and reusable packed updates; it does not open another network session or simulate a second game.

Install the exact package with `pnpm install --frozen-lockfile --ignore-scripts` in `Client/PristonTale.Browser`, then
run `pnpm build:renderer` when deliberately updating the checked-in official Three bundles. The application loads all
modules from `/play`; no CDN, inline import map, extra CSP script origin or third-party runtime request is needed.
Decoded RGBA DataTextures avoid browser image decoding of the authored BMP/TGA files and blob image URLs.

Run `pnpm build:worker` after renderer source changes and before any browser `dotnet publish` or publish script.
The pinned esbuild development dependency bundles the actual worker entry, bridge, completion fence, scene driver
and Three into `wwwroot/three-worker-bundle.js`. The generated bundle, legal-notice file and hash manifest are ignored
by Git. The build refuses external runtime imports or unreviewed Three/esbuild versions. Its manifest records the
input/output hashes and retained Three MIT license; the checked-in vendor modules remain unchanged.

`three-worker-assets.js` decodes the two same-origin official SMAA PNGs on the parent page before canvas transfer,
with colour conversion, premultiplication and orientation conversion disabled. Its 20-second deadline covers both
fetch and bitmap decoding; partial or late images close on failure. The worker receives transferable ImageBitmaps
and retains them until final disposal, including quality changes. Only the generated worker's SMAA constructor uses
these preloaded images, through a source-pattern-guarded build transform. The normal main-thread addon keeps its
image loading path. Thus the worker needs no further module, image or network request under its existing CSP.

Vendoring retains the package version and MIT license and applies one guarded WebGL backend correction: an explicit
canvas clear binds the default framebuffer before clearing, so it cannot erase a previously bound portrait target.
Pass-internal clears (`setFrameBuffer=false`) and offscreen/MRT clears retain their framebuffer ownership. The patch refuses a
different package version or source pattern until reviewed; its production-backend regressions run in the JavaScript
suite. SMAA's two official lookup PNGs are extracted unchanged into same-origin files during the same vendoring step.

Run the production JavaScript regressions with:

```bash
node --test Client/PristonTale.Browser/tests/*.test.mjs
```

They cover world-space bone palettes, state-only dirty updates, opacity recovery, texture overlays, command and
portrait-target ordering, asymmetric target clipping, raw Sprite and sRGB Unlit vertex colours, linear VFX,
multiplicative blending, reversed depth, nonzero camera coordinates, Float32 rain occlusion and grow-only span staging.
They also build the rain and display node graphs, preserve native texture formats and check the guarded vendor patch.
The native memory adapter reuses views by draw slot; rain counts share whole-heap views until WASM memory grows.
Static RainDrops register their complete seed, corner and index channels once per native buffer revision. Near and
far draws then carry independent index ranges and uniforms, also in worker mode; splashes keep their dynamic path.
Buffer writes and disposal retire dependent resources in draw order. With profiling enabled, `rainGeometry` reports
cumulative registration bytes, creations, draws, destruction and live resources; unchanged registration bytes over
moving rain frames confirm that geometry is retained rather than uploaded again.

GPU checks must exercise both the automatic backend and `?renderer=webgl2`. Read the canvas `data-renderer`,
`data-renderer-errors` and `data-webgpu-errors` fields; the latter retains the existing diagnostics hook for both
backends. Device loss disposes the graphics resources. Read pixels asynchronously from a Three render target in a
probe; do not use synchronous MonoGame GetData for GPU validation.

With profiling enabled, the driver records CPU submission time for scene, portrait, native and post phases in
preallocated rings. `graphics.getProfile()` snapshots interval P50/P95, Three passes/draws/triangles, observable output
quads and resource counts. The output-blit subtotal counts public render calls named `Output Color Transform`; private
backend passes may bypass that hook. These measure CPU work and submitted rendering, not GPU execution duration.

`?profile=1&qa-weather=rain-full` requests a transient full-density rain benchmark for that browser client. Both
query values must match exactly. The typed host setter refuses activation before frame profiling is enabled.
The instance belongs to the client and follows its gameplay screens; normal startup explicitly clears it and
leaving a world clears its applied/drawn state. Reentry may reapply the still-explicit request. It never changes
`ServerWeather`, sends a weather command or persists settings. Weather disabled in settings and fields that do
not take rain still suppress it. The normal weather presentation, cloud easing, lighting, fog, occlusion map,
18,000 near drops, 10,000 far drops and splash update/draw path remain active; the diagnostic requests Rain at
intensity 255 with fixed zero wind and no synthetic lightning. Wait until `FrameProfile.weatherDiagnostic`
reports `applied: true`, `rain: 1` and `drawnRain: 1` before measuring a wet run. `drawnRain` is recorded only after
the real rain draw and is cleared on the next weather update, while server kind/intensity remain separately visible.

`?profile=1&gpu-timing=1` additionally enables the pinned renderer's supported timestamp-query backend before device
initialization. Its default `sampled` mode writes timestamps for one whole application frame in ten, leaving the
backend's tracking disabled on the other frames. The separate `gpu` profile resolves that sample asynchronously,
with at most one request in flight. Each result is the render-pass duration sum for the last backend frame returned
by Three, not a ten-frame average or the CPU submission duration. Request/completion application-frame labels do not
identify the exact GPU frame. `graphics.setGpuTimingMode('off'|'sampled'|'continuous')` changes the diagnostic policy
between frames: `off` disables both writes and resolves, while `continuous` writes every frame and resolves every
tenth frame for an explicit instrumentation-cost comparison. The profile reports mode, tracked frames and sampled
frames separately. Mode changes clear current samples; pending results from an older mode or reporting interval
cannot populate the new interval or its last result. Unsupported backends do not poll, and a resolver failure stops
polling with one bounded error. Disposal restores the backend's original tracking option. Timing-enabled and
timing-disabled performance must be compared in the same scene because query writes themselves can affect GPU cost.

`adaptive-resolution.js` controls the transient browser render budget without modifying the saved preset or settings.
It observes one-second completed-frame windows in worker mode, or dispatched draw windows on the main renderer.
Two consecutive windows below 98.5 percent of the target lower the 3D scale in bounded steps, down to 50 percent.
The target is 60 FPS or a deliberately lower foreground limit. At 70 percent scale or below, continued pressure may
cap shadow refresh at 30 Hz while retaining the selected shadow-map size, filter and enabled effects. This changes
shadow motion cadence and is reported in the quality badge; it does not claim identical temporal quality.
Fifteen windows at or above that same healthy threshold allow one small recovery probe. After the initial loader
is hidden and four real render frames are counted (GPU completions in worker mode), adaptation applies to opening,
login, roster, creation and world scenes. Map loading and a pending first-world GPU fence suspend adaptation until the
existing ready acknowledgment; hidden pages and the settings dialog suspend measurement and require a new warmup.
Crossing between the world and courtyard restores the saved scale and shadow cadence and starts a new warmup;
creation-step and name reports alone do not reset the budget. Disabling adaptation restores the saved budget.
These controls do not guarantee a particular frame rate at the minimum budget.

`ThreePost.setDynamicResolutionScale` resizes the existing HDR target and AO/SSR/Bloom resources at the next scene
draw; it does not change the pipeline cache key or recreate the effect graph. Without the internal-post policy,
SMAA/FXAA, final output and native HUD retain display resolution. Capabilities distinguish configured scale from
effective scale and the adaptive cap.

Normal visits select internal post automatically on the actually initialized WebGPU backend. WebGL2 retains the
original display-size post chain; `?internal-post=0` explicitly keeps that chain on either backend. The explicit
`?profile=1&internal-post=1` option forces the internal chain, including WebGL2, and enables diagnostic hot toggles.
The generic renderer API defaults to disabled and accepts `false`, `true` or `'auto'`; the worker preserves that mode
until its actual backend resolves it and acknowledges the selected capability. Internal post scales automatic inputs
and SMAA to the internal world footprint. It keeps the display tone/upscale input, RCAS and final composition at
display size; AO's cached conversion remains unchanged. Local SMAA sizing and public RTT resolution scales reuse the existing
pipeline, targets and official lookup images on resize or the diagnostic "Pós original"/"Pós interno" toggle.
At 100% and with supersampling above 100%, these post footprints stay at display size. At reduced world scales,
the sample footprints intentionally differ from the normal display-size post chain; this is a deliberate
quality/performance tradeoff, not a pixel-neutral reduced-scale optimization. Normal visits do not enable profiling,
GPU timestamp queries, capture or benchmark controls.
The worker applies ordered toggles between application frames and publishes `internalPost` and actual
`postProcessingWidth`/`postProcessingHeight` only through the matching GPU-completed frame metadata. Native HUD
composition follows this chain at full canvas resolution, and portraits keep their own target sizes.

Normal visits also select fused sharpening on the actually initialized WebGPU backend. WebGL2 retains the pinned
SharpenNode; `?fused-sharpen=0` keeps that node on either backend, and `?profile=1&fused-sharpen=1` forces fusion
for diagnostic comparisons, including WebGL2. The generic API defaults to disabled and accepts the same pure
`false`/`true`/`'auto'` policy as internal post, without enabling profiling. This reload-only choice evaluates the
same five-tap RCAS operation in final composition instead of writing and then sampling a separate full-display
sharpening target. It preserves that target's half-float store rounding with public TSL half packing/unpacking;
tone/upscale input, strength, denoise, alpha and native HUD remain unchanged. `three-sharpen.js` retains the
upstream MIT notice. The immutable policy has no hot setter or worker opcode. `effective.fusedSharpen` is true only after a composition actually uses sharpening, and the worker
commits it with its GPU-completed frame. Profile data separates the requested flag, committed use and cumulative
CPU-submitted composition count; that count is not an FPS or GPU-completion measurement.

The worker adapter is exported by `three-worker.js`, with its binary codec in `three-worker-protocol.js`
and dispatcher in `three-worker-entry.js`. Normal visits use it when Worker and OffscreenCanvas are available;
`?render-worker=0` keeps rendering on the page. Initialization failure retains the existing canvas-replacement
and main-thread Three fallback. Device failure after the game starts requires recovery rather than silent migration.
`createWorkerGraphics` transfers
an OffscreenCanvas and awaits acknowledged capabilities, with a 20-second initialization limit and worker termination
on failure. `createWorkerPackedImports` sends the same decoded-resource
and packed frame contracts to the actual scene bridge in the worker. Native draws copy only their explicit used CPU
ranges into standalone arenas, preserving order, vertex channels, texture formats and target transitions. WASM memory
is never transferred. Four transferable frame slots bound queued frames by default; `?frame-slots=3` retains
the three-slot alternative. Both use the same GPU-completion ownership and report the selected bound in worker
statistics. The additional buffer can increase queue latency; a larger bound is not a performance guarantee.
Normal visits do not enable profiling, GPU timestamps, diagnostic logs or frame capture.
The page requests automatic exact-zero point-light evaluation on the actual WebGPU backend; WebGL2 keeps ordinary
lighting. `?zero-point-lights=0` disables it, while `?profile=1&zero-point-lights=1` enables the diagnostic override on
either backend and exposes the existing original/optimized light controls. This policy preserves the chosen lamps,
their intensity/range and all quality options; it does not infer performance from the selected renderer name.
The caller must wait for `canAdvanceFrame`
before `host.Frame`, and `firstFrame` resolves only after GPU completion. `canBeginFrame` remains the stricter native
begin predicate: initialization or resource work may already have opened a frame which the next host tick must finish,
rather than deadlock waiting for its own unsent GPU work. Resources outside a frame use
the same ordered stream, with `flushResources` as an asynchronous barrier.

`three-worker-completion.js` fences each frame after submission. WebGPU uses `queue.onSubmittedWorkDone`; WebGL2 uses
`fenceSync`, `flush` and zero-timeout `clientWaitSync` polling, deleting the sync on completion or cancellation. These
promises do not block the ordered worker handler, so later frames and resources can be encoded while earlier GPU
work runs. Buffer ownership returns only after its GPU fence and any explicitly requested capture complete. Disposal
cancels outstanding fences, rejects abandoned waiters and closes owned lookup resources without publishing false ACKs.

The main loop keeps a single animation callback or pending slot wait. Opening loading ends after four acknowledged
GPU-completed frames. World readiness is fenced after `host.Frame` queues its newly drawn map frame, using
`waitForSubmittedFrame` with the last queued frame ID; a cancelled or replaced loading attempt invalidates that
completion. Despite that method's name, it waits for GPU completion, not CPU submission. Thus the loading panel and
ready milestone wait for the actual map frame, not an earlier opening frame. The world HUD uses acknowledged
GPU-completed-count deltas per second rather than logic ticks. Replacing a canvas
rebinds its pointer handlers and future focus actions without retaining listeners on the old canvas.

Worker capability getters and `getProfile` return acknowledged snapshots. `requestProfile` obtains a fresh interval
asynchronously; submission and GPU-completion timestamps, counters and cadence are separate from main-thread ticks
and do not claim physical display presentation. Render-control promises acknowledge applied changes. Snapshot revisions
are assigned when metadata is captured; delayed frame completions cannot overwrite a newer control or profile
snapshot, while their completed frame counters and buffer ownership still advance. Effective scale, dimensions and
adaptive shadow cadence have a separate completed-frame revision: general profile/control ACKs retain the last
GPU-completed values, and a delayed older frame cannot replace them. Changed render state is copied before yielding;
unchanged frames reuse that copy. Automatic adaptive controls do not reset profiling windows.
With explicit profiling, `workerTiming` reports total packet CPU time including decode and native preparation,
queued-to-worker-start latency, paired submission-to-GPU-completion wall latency, pending completions and proxy slot
waits. GPU wait includes queue execution and browser notification; it is not shader-only duration. Normal visits do
not collect these timing aggregates. The explicit profile-only
`captureNextFrame` diagnostic allows one pending bitmap: the worker starts its canvas snapshot immediately after
present, before yielding, and transfers ownership to the caller, which closes it. Normal rendering performs no
capture or GPU readback. Transferred SMAA lookup bitmaps remain owned through quality changes and close after final
renderer disposal or failed initialization. A worker bundle must include its renderer dependencies and lookup
initialization rather than rely on DOM image loading or unrestricted worker imports.

`?profile=1` also emits one `[MapLoadProfile]` JSON record per world-entry attempt. Seven milestones (0, 80, 85, 90,
95, 99 and ready/100) use the same monotonic browser clock, separating downloads, server acknowledgement, scene
initialization and the first completed gameplay draw. An optional Resource Timing observer aggregates request counts,
transferred/encoded/decoded bytes and zero-transfer responses with a body, without retaining resource URLs. That cache
count is inferred from browser timings; responses without size information have a separate count. Cancellation ends
the sample without a ready milestone. The observer disconnects at completion and is absent during normal visits.

The experimental CacheStorage reader is disabled on normal visits: no cache lookup, open, extra manifest request or
fetch wrapper is installed. It requires both `?profile=1&asset-cache=1`, before `PrepareAsync`, and handles only
same-origin GETs listed in the current bootstrap manifest under `assets/` or `Content/`, with exactly the matching
64-digit SHA `v` query. That diagnostic mode reads an existing public cache; writes additionally require
`asset-cache-fill=1`. Both reads and fills can increase load time through storage and stream contention, so their
timings must be compared with the normal HTTP path. APIs, authentication,
framework files, cross-origin traffic, ranges, conditional requests, SRI checks, bodies and explicit no-store requests
retain the original fetch path. Only nonredirected basic 200 public responses are stored; cached responses keep their original
headers and response metadata. Signal-bound cached streams remain cancellable during body consumption and detach
listeners at EOF or cancellation. Cache failures preserve normal downloads. Quota failures disable writes while existing
entries remain readable; no persistence permission or unrelated cache deletion is used. Background clone writes are
bounded, and their completion is not part of readiness. No dependency is omitted; overflow skips only that optional write.
`MapLoadProfile.assetCache` reports per-attempt hits, misses and errors separately from Resource Timing. Its
`hitHeaderBytes` measures declared representation lengths, not decoded body sizes; unknown lengths are counted rather
than inferred. Profiling also emits cumulative `AssetCacheProfile` snapshots without reading extra response bodies.

Static opaque texture-array batching is requested by default; `?texture-arrays=0` restores ordinary stage batching.
Backends without array-layer support retain ordinary resources. Compatible sources are grouped after stage batching, with a
strictly smaller draw-object count, unchanged texture dimensions and sampling, and a 64 MiB aggregate page budget
including mipmaps. The profile's `textureArrays` census reports original and baseline objects, eligible objects,
compatible groups, output objects, pages, layers and allocated bytes. A flat per-triangle layer selector retains each
diffuse image; by default cutouts and animated materials keep their ordinary resources. Quality changes retain each preset's
anisotropy. Pixel changes and removals invalidate derived pages and restore the ordinary objects; available resources
are repacked before the next scene draw. No page-building work runs on unchanged frames.
`?profile=1` also shows a performance-test panel with grouping on/off buttons. Switching rebuilds the same scene
resources in place, preserving stage transforms and visibility, then resets the managed and JavaScript profile
windows. Both samples therefore use the same connection, camera, weather and graphics settings.

`?profile=1&cutout-arrays=1` additionally admits immutable, nonblended alpha-tested stage materials into texture-array
batching. It requires ordinary array batching and backend layer support; `texture-arrays=0` disables both paths.
The material cutoff, colour, surface policy and sampling remain separate grouping keys. Pages retain the original
RGBA alpha bytes, and both colour and shadow passes sample the same layer alpha and cutoff. Blended, animated,
skinned, layered and fractional-opacity sources retain ordinary resources. The original page, layer, global-memory
and strict object-reduction limits still apply. `textureArrays.cutoutsRequested` and `cutoutsEnabled` identify this
reload-only diagnostic mode; normal opaque arrays continue rejecting textures with nonopaque alpha bytes.

Explicit profiles also include `stageMaterials`: unique retained stage objects classified by actual opacity,
skinning, alpha test and double-sided transparent policy, including the subset requiring two passes. Actors and sky
layers are excluded; spatial bundle wrappers do not duplicate counts. The immutable single-texture cutout subset
excludes skinning, layers, animated UVs, wind, blinking and fractional material opacity. Its texture and sampler-group
counts inspect the current RGBA bytes and sampling state, scanning each candidate texture once per profile request.
These are diagnostic candidates, not a promise that transparent ordering permits batching. Normal visits skip the census.

`?profile=1&render-bundles=1` opts into WebGPU command caching for immutable opaque and cutout stage meshes. Spatial
`BundleGroup` chunks are culled independently for every main and shadow camera; wind remains a GPU uniform update.
Actors, transparency, animated texture bindings and dynamic geometry retain normal submission. WebGL2 keeps normal
groups. `graphics.setRenderBundlesEnabled()` toggles the same stage resources for diagnostics. Texture publication,
quality, target resize and light/shadow layout changes invalidate recorded commands. The stage keeps a flat list of
owned drawable objects for quality updates, disposal and resource replacement. The profile labels Three's counters as
`encodedDrawCalls` and `encodedTriangles` while caching is enabled: bundle replay still performs the GPU draws, so
lower CPU encoding counts must not be presented as a reduction in actual GPU geometry.

`?profile=1&observer-cache=1` opts into a separate static array-material observer experiment; normal visits keep
Three's observer unchanged. Only the owned, opaque array factory can register its known node graph. The public
`NodeMaterial.setupObserver()` hook supplies a `NodeMaterialObserver` subclass; no private observer flags or vendor
code are changed. Standard comparisons still track material, model, geometry, target and array-texture changes,
including static bundles. Camera, light and fog bindings retain their shared refreshes. Unrecognized nodes, actors,
wind and disabled diagnostics fall back to the original observer. `graphics.setStaticArrayObserversEnabled()` switches
the same resources in place. `staticArrayObservers` profile counters distinguish full/shared/no refreshes and fallback
calls; they describe CPU refresh decisions, not GPU timings or a measured frame-rate gain.

`BrowserHost.EnableFrameProfiling()` enables the managed diagnostics. `FrameProfile()` returns JSON with separate
Update/Draw P95, presentation and pacing timings. Its scene-bridge P95 and cumulative time/call counts cover only the
synchronous scene, portrait, compose and sky imports. Those calls are already part of managed Update/Draw work; the
native HUD adapter and asynchronous GPU execution are outside that bridge subtotal. Resetting profiling creates fresh
phase and bridge counters. Profiling is disabled by default.

The world HUD shows the applied graphics-quality preset at the bottom-left edge, including the effective preset
when Automatic is known. It uses the gear's saved UI choice rather than an internal Custom classification caused
by capability normalization. Saving a manual graphics change updates the badge to Custom immediately; cancelling
a draft does not change it. The badge shares world-HUD visibility and never captures pointer input.
The same opt-in records `[StageInput]` and `[StagePick]` diagnostics for character selection: browser event times and
click details, normalized coordinates, actor hits, pending gesture state, resets and discarded commands. Confirmation
uses the shared `CharacterSelectStage.DoublePressSeconds` limit and the same selected actor on both clicks.

The browser maps authored texture layers, material blend modes, animation frames and UV scrolling, floor lighting,
actor colour adjustments, fades, overlays, outlines and distortion to Three materials. The sky uses the authored
geometry and profile crossfade. Directional shadows use one conventional-depth map at the requested size, with the
native per-frame eligibility rule: enabled preference, nonzero directional illumination and an above-ground scene.
The authored moon remains a shadow-casting directional light at night.
The explicit Compose callback applies the Three HDR/post chain after the world and linear effects, before native HUD
sprites. It supports render scale, GTAO, reflections, bloom, FXAA, SMAA, MSAA, exposure compensation, tone mapping and
sharpening. SMAA lookup images are extracted from the pinned official addon as same-origin PNGs, preserving the site's
image policy. TAA, histogram auto exposure, contact shadows, custom LUTs and volumetric shafts remain unavailable and
are declared by `graphics.getCapabilities()`. Cascaded/lamp shadow atlases and glTF actors are separate capabilities.

## Retained direct WebGPU backend

The fork's direct WebGPU backend remains in `monogame/runtime/src/webgpu/` alongside the browser CPU adapter.
`monogame/runtime/repro/WebGpuRepro` verifies its raw graphics exports, shader reflection, buffers, textures,
compute, resize and resource lifetimes independently of the Three entry point. Its page supplies its own WebGPU
device as `preinitializedWebGPUDevice`.

Changes to the shared HLSL still build for both MGCB Web and DesktopVK profiles. The direct backend's Web shaders
must pass browser compilation and validation, and Vulkan changes retain the existing `--verify-shaders` check.
This retained backend does not supply a WebGL2 fallback or run concurrently behind the Three canvas.

## Pitfalls

- A portrait render target keeps the same texture identity and version while its native sampler switches between
  point and linear filtering. Changing a target texture's `needsUpdate` recreates its GPU image and invalidates cached
  framebuffer attachments. WebGPU sampler bindings update separately; the WebGL adapter refreshes sampler parameters
  without replacing the image. Targets are created as targets from their first texture lookup.
- A WebGPU buffer or texture referenced by the pending command encoder must survive until submission. Destruction
  flushes the current submission when it used the resource, including the old user vertex and index buffers replaced
  when SpriteBatch grows for a larger interface window.
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
- The page installs `noxiusThreeGraphics` and the `noxius-three-scene` imports before creating the C# game. The direct
  WebGPU repro uses its separate preinitialized device contract. A hidden tab may receive no animation callbacks.
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
- The site compresses eligible `/play` responses with ASP.NET response compression. Versioned asset requests and
  fingerprinted framework resources retain their cache policy.

## Related

- [protocol.md](protocol.md#websocket-transport): the WebSocket transport.
- [server.md](server.md#websocket-edge): the edge's settings, caps and layouts.
- [website.md](website.md): the play page, its policy and the `Play:*` settings.
- [client.md](client.md): `GameServerConnection` and the desktop client the browser will share.
- [android.md](android.md): the Vulkan Android client and its asset packages.

Touch taps on the browser canvas are translated to primary mouse clicks for the shared game screens.
