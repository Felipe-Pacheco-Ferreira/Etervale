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
into the HUD's current render target. A portrait moves its actors, for that draw only, into a studio scene of its own
with a fixed light layout (sky and ground fill, key and rim lights). Drawing it in the world scene changed the world's
light count twice a frame, and Three rebuilt every material of both draws: about 50 ms a frame while a target showed. Sky mesh IDs are sent only when a profile changes; camera height and crossfade
use scalar updates. Scene disposal releases its JS meshes, materials and textures without creating a MonoGame 3D device.

| Path | Responsibility |
| --- | --- |
| `Client/PristonTale.Browser/PristonTale.Browser.csproj` | `Microsoft.NET.Sdk.WebAssembly` app (`browser-wasm`, no Blazor) referencing `Gameplay`, with the fork's browser runtime linked in (`BrowserRuntime.props`, needs the `wasm-tools` workload); published ahead-of-time compiled ([AOT publish](#aot-publish)); its publish copies the login's files beside the client and lists them in `bootstrap.txt` (`EtervaleBootstrapFiles`) |
| `Client/PristonTale.Browser/BrowserHost.cs` | `[JSExport]` bridge for the page: `PrepareAsync` (fetch the files), `Start` (endpoint, language), `Frame` (one `Game.Tick`), `SubmitLogin` (the page's form); `reportLogin` back to the page |
| `Client/PristonTale.Browser/BrowserHost.Settings.cs` | `[JSExport]` bridge for the page's Settings panel: the settings document in and out, presets, shortcut keys ([Entry page](#entry-page)) |
| `Client/PristonTale.Browser/BrowserGame.cs` | Variable-step host for the shared screen lifecycle, one Update/Draw per dispatched animation callback, starting from saved settings |
| `Client/PristonTale.Browser/BrowserExternalSceneRenderer*.cs`, `BrowserSceneTextures.cs` | Resource and packed-frame bridge; CPU texture decoding and authoritative C# pose updates |
| `Client/PristonTale.Browser/wwwroot/three-scene.js`, `three-bridge.js`, `three-native.js`, `three-rain.js`, `three-display.js`, `three-camera.js`, `three-post.js`, `three-batching.js`, `three-profile.js` | Scene, packed bridge, native and rain batches, explicit display encoding, supplied camera projections, HDR/post composition, spatial static batching and opt-in CPU submission diagnostics |
| `Client/PristonTale.Browser/package.json`, `pnpm-lock.yaml`, `vendor-three.mjs`, `wwwroot/vendor/three/` | Pinned Three release and self-hosted official ES bundles with MIT license |
| `Client/PristonTale.Browser/BootstrapFiles.cs` | Fetches the files `bootstrap.txt` lists into the runtime's in-memory file system, where `ContentManager` and `AssetFiles` read them; the world wave comes from the world packs when they are valid |
| `Client/PristonTale.Browser/BrowserSoundFiles.cs`, `Shared/PristonTale.Assets/WebSoundIndex.cs` | The sound files listed by `audio.txt`, downloaded one at a time when the game first reads each ([Sound](#sound)) |
| `Client/PristonTale.Browser/wwwroot/web-audio.js` | Web Audio behind the runtime's audio voices (`noxiusAudio`): buffers, voices, the context started by the first gesture ([Sound](#sound)) |
| `Shared/PristonTale.Assets/WebAssetPacks*.cs` | World pack format, bootstrap parsing, index and archive checks, and the browser's pack reader ([World packs](#world-packs)) |
| `Client/PristonTale.Browser/Content/Content.mgcb` | UI font and compatible effect metadata built for MGCB's Web platform; actual scene shaders are Three node materials |
| `Client/PristonTale.Browser/Program.cs` | Empty entry point; marks the assembly browser-only |
| `Client/PristonTale.Browser/wwwroot/main.js` | Initializes Three, installs the scene module and native graphics adapter, starts .NET, drives `BrowserHost` from requestAnimationFrame and runs the page controls |
| `Client/PristonTale.Browser/wwwroot/settings.js` | The gear's Settings panel: tabs and cards built from a row table, saved in `localStorage` |
| `Client/PristonTale.Browser/wwwroot/settings-options.js` | The panel's rows (each bound to a `client-settings.json` field), presets, shadow and texture choices |
| `Client/PristonTale.Browser/wwwroot/settings-effects.js` | What settings do on the page: full screen, leave prompt, grey screen while dead, low-life border, death notification, hidden HUD icons |
| `Client/PristonTale.Browser/wwwroot/news.js` | News & Support in the Settings panel's style: the feed's posts, the tickets the game keeps, the community links |
| `Client/PristonTale.Browser/wwwroot/touch-hud.js`, `touch-quests.js`, `touch-map.js`, `touch-bag.js`, `touch-status.js`, `touch-skills.js`, `touch-npc.js`, `touch-feed.js`, `touch-party.js`, `touch-restart.js`, `touch-rewards.js`, `touch-skill-picker.js`, `touch-layout.js`, `touch-tutorial.js`, `touch-hunt.js`, `auto-hunt-items.tsv`, `touch-dom.js`, `hud-art.js`, `BrowserHost.Touch.cs`, `Client/PristonTale.Gameplay/UI/GameplayHud.Touch.cs`, `GameplayHud.TouchQuests.cs`, `GameplayHud.TouchMap.cs`, `GameplayHud.TouchBag.cs`, `GameplayHud.TouchStatus.cs`, `GameplayHud.TouchSkills.cs`, `GameplayHud.TouchNpc.cs`, `GameplayHud.TouchFeed.cs`, `GameplayHud.TouchParty.cs`, `GameplayHud.TouchRestart.cs`, `GameplayHud.TouchRewards.cs`, `GameplayHud.TouchTutorial.cs`, `GameplayHud.TouchHunt.cs`, `World/FreeHuntSession*.cs`, `Combat/FreeHunt*.cs`, `Screens/Gameplay/GameplayScreen.FreeHunt.cs`, `UI/Widgets/HudCombatFeed.cs`, `Input/TouchSkillSlots.cs`, `UI/Inventory/HudItemActions.cs`, `TouchPortraitPlacement.cs`, `Screens/Gameplay/GameplayScreen.Touch.cs` | The touch HUD of phones and tablets: the Android client's player frame, bottom HUD, quest card and journal, field map, bag, status and skills windows, NPC plate, combat feed, party, death plate, Eventos & Recompensas, the skill list of a long press, the HUD editor, the first-time tutorials, the free auto-hunt and art on page canvases, fed by the game's touch state, with the player's 3D head drawn by the game ([Entry page](#entry-page)) |
| `Client/PristonTale.Browser/wwwroot/draggable.js` | Drags a page window by its header within the viewport; a double click on the header or Settings' reset puts it back |
| `Client/PristonTale.Browser/publish.ps1`, `publish.sh` | Release publish into the website's `Play:ClientPath` folder, replacing the previous build and its world packs |
| `Shared/PristonTale.Protocol/Transport/WebSocketTransport.cs` | Frames over a WebSocket, client and server side ([protocol.md](protocol.md#websocket-transport)) |
| `Server/PristonTale.Server/Net/GameTcpServer.WebSocket.cs` | The game server's WebSocket edge ([server.md](server.md#websocket-edge)) |
| `Web/PristonTale.Web/Pages/Jogar.cshtml`, `wwwroot/css/play.css`, `Services/BrowserClient.cs` | The entry page (`/`) and the `/play` files ([website.md](website.md)) |
| `monogame/runtime/src/webgpu/MGG_WebGPU.cpp` | The fork's WebGPU graphics backend: the same exports as the Vulkan one ([monogame/issues/021](../monogame/issues/021-webgpu-backend.md)) |
| `monogame/runtime/src/web/MGG_Three.cpp` | CPU MonoGame resource and HUD/effect adapter for the browser; forwards rendering to Three instead of owning a second graphics device |
| `monogame/runtime/src/web/` | The browser platform (the page's canvas as the window, its events), audio over the page's Web Audio (`MGA_web.cpp`), storage without a device, `mgruntime.cpp` (the browser runtime as one translation unit) and `BrowserRuntime.props` (links it into a WebAssembly project) |
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

3. `BrowserHost.PrepareAsync` fetches the opening's files from `bootstrap.txt` into the in-memory file system; of the
   languages' own interface fonts (`Content/Fonts/UI-<code>.xnb`, `UiFonts`, each in a world-pack chunk of its own:
   `WebAssetPacks.IsLanguageFont`) only the page language's, as the game
   draws that language only.
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
  and also toggles the music. Entering character selection stops the opening music; the game's own sound starts there
  ([Sound](#sound)).
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
  to the character list right after (`FetchRemainingAsync`), and the first field's when a character enters. The world
  wave is split by `DeferredWorldFiles` (Gameplay): other class families' full motion banks (about 140 MB), other
  fields' minimaps and art the world does not draw (guide map, character-select stage, opening images except the chat
  bubble frames) download after the field's first frame. A remote player whose bank is still downloading is prepared
  once it lands (`PlayerActorComposer` waits for `DeferredWorldFiles.Pending` and reads again); a neighbouring field's
  minimap that is not there yet stays undrawn until the minimap next changes field or neighbour. The roster
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
  FPS uses acknowledged render-count deltas, and ping measures that world's status response. News, hunt, and the More
  menu dispatch to the live HUD controls. URL-backed shortcuts, and Tower Defender until the game offers it, are
  disabled until configured. The shortcuts carry colour emoji icons; until the pointer is on them only their plate and
  edge fade, so labels and icons keep their colour. News & Support carries a red count of news not seen plus answered
  tickets not read (`BrowserHost.NewsUnread`, the game's `HudNews.Unread`, read every second). A box beside the one under the pointer names it and says what it opens (`world-hud.js` tips). A pill at the top
  centre shows the players online the server reports in its heartbeat (`BrowserHost.OnlinePlayers`, read every second),
  hidden while unknown or when Settings hide it; the desktop and Android HUD draw the same pill on the canvas. The game canvas keeps the inventory,
  character, skills, minimap, chat and other gameplay windows. The web HUD centers the legacy bottom bar and chat
  and opens the same settings dialog from the gear, the system menu and the settings binding.
- News & Support is a page window in the Settings panel's style (`news.js`, markup in `Jogar.cshtml`, styles in
  `settings.css`). The game opens it (`HudNews.PageOpen`, the page's `openNews`) and draws nothing of it on the canvas.
  The News tab reads `/api/client/news` itself: each post is a card tinted by its kind with its line and badge
  (Novidade, Evento, Melhoria),
  the title, the day and author, then the summary and body, `- ` lines as bullets and `**text**` bold, and ends
  with the team's small signature in its corner ("Equipe Noxius", the site's emblem and the community's Discord, which
  asks before it opens). The ticket tabs
  read `BrowserHost.NewsState` every half second and act through `NewsAction`; the game keeps the conversation with
  the server (`HudNews.Page.cs`). The Discord, Instagram and X buttons ask "Abrir link?" and ABRIR or Enter opens the
  link in a new tab.
- Social uses a centered browser panel with Group, Friends, Clan, Blocked and Recent tabs. Name entry captures
  keyboard input before chat and gameplay shortcuts. Contact actions use the saved contact lists, whispers open the
  chat composer, and profiles show known presence, party and clan information. Group creation, admission policy,
  search and joining use the authoritative party commands and directory. The page's language, settings and
  fullscreen bar is hidden while playing and returns on leaving the world.
- Quests uses Journey, Daily, Monthly and Completed tabs with the real journal and reward board. Daily claims keep
  server validation. Tracker visibility is shared with Settings. Journey navigation uses the current map's collision
  route planner and stops on manual movement, death or a field change. A destination in another map reports the
  required map. The monthly page stays empty until a monthly challenge exists.
- At the standard Ricarten arrival point, the browser starts with a tighter field of view and a steeper camera
  pitch to frame the carpet, lamps and approach to the stairs. The server's position is shared. Mouse camera controls
  take effect once the browser has received pointer movement over the canvas.
- **Phones and tablets.** A tap on the canvas (a touch lifted without a drag) reaches the game as a primary mouse click
  at that point (`main.js`), so the screens and the canvas windows answer it. In the world, the touch HUD replaces
  mouse play when Settings' interface type asks for it (`interfaceMode`, [client-settings.md](client-settings.md);
  `Auto` on a device without a fine pointer, `touchOnlyDevice` in `touch-hud.js`). It is the Android client's:
  - The page reports every touch on the canvas (`BrowserHost.ReportTouch`, fractions of the canvas, kept in landing
    order by `BrowserTouchFingers`), and `GameplayScreen.Touch.cs` reads them with the Android client's gestures
    (`TouchOrbit` in `Client/PristonTale.Gameplay/Input`): a finger landing at the lower left is the joystick
    (`GameScene.TouchMovement`), drawn on the canvas where it landed (`TouchJoystick`); one finger dragged elsewhere
    turns the camera and two pinch its distance (`GameScene.TouchCameraOffset`). A tap selects a monster instead of
    attacking it (`GameScene.SelectCombatTargetOnTap`), as on the phone.
  - The page draws the Android client's bottom HUD over the canvas (`touch-hud.js`, markup in `Jogar.cshtml`, styles
    in `play.css`) with its art: `hud-art.js` draws on 2D canvases what the Android HUD draws (`VulkanHudArt`,
    `VulkanSkillBorder`, the glyphs of `HudIcon`), so the orbs, the attack button's crossed swords, the tier borders,
    the gauge and the badges are the phone's. The places are the Android client's, in hundredths of the screen's short
    side from the wheel's corner (8 px from the right, 20 px from the bottom): the attack button, four skill buttons on
    an arc around it and the potion belt stacked at the wheel's left; Menu and Bolsa as 58 px orbs at the lower left;
    the experience bar (18 px, a tick every tenth of the level, the level and percentage) along the bottom edge.
    The buttons call `RunWorldAction`: `touch-attack` attacks the selected monster or else the nearest one,
    `touch-skill:<n>` casts that button's skill at the same target (`GameScene.CastSkillAt`; a self or ally skill
    needs none), `touch-belt:<n>` drinks that belt box's potion, `touch-run` switches running. Every 250 ms the page
    reads `TouchState`: the player frame's values, the share of the level's experience, each skill button's skill
    (name, picture file, tier, rank against `SkillLearning.MaxRank`, cooldown left and its length), each belt box's
    potion (name, count, picture file), the items that came into the bag since it was last open and the rewards ready
    to claim. The pictures are the original skill buttons and item sprites: the page asks for each file once with
    `TouchPicture` (only files the state named), which the game decodes from the downloaded assets, and asks again
    after 3 s while the file is not there yet. A skill shows its picture (its abbreviated name while there is none),
    its tier's border with ten rank marks and the gold orbit at the highest rank; a recovering skill shows its seconds
    under a shade that turns away, counted on the page's clock between two states. A skill on the action bar's first
    four slots holds that button; the others fill with the learned skills in order (`TouchSkillSlots`, the Android
    action wheel's rule).
  - The player frame (the Android client's `VulkanPlayerFrame`) sits at the top-left corner, 0.66 by 0.241 of the
    short side: the plate with the crest, the breathing aura and a round window, the level medallion (its stone
    climbing with the level against `ExperienceTable.MaximumLevel`, burning at the highest), a red dot while there are
    attribute points, the name with the title the player shows at its right on a plaque edged and lettered in its
    rarity's colour, a gold diamond at each end and a passing highlight (`titlePlaque`, from the HUD's `Titles`), the
    life, mana and stamina gauges with their values (eased, with a trail and a sweeping
    light) and, at the highest level, the class title (`DataText.ClassTitle`). The game draws the player's 3D head in
    the window (`GameplayScreen.Touch.cs`: a `HudTargetPortrait` of the player rendered before the world and drawn
    after the HUD at `TouchPortraitPlacement`, whose fractions the touch state hands to the page). A tap opens the
    character's status. The server, FPS and ping line moves under the shortcuts at the top right as the Android
    client's small monospace readout, without the server's name, the ping green under 80 ms, yellow under 200 and red
    above (`latencyGrade`).
  - The target frame (the Android client's `VulkanTargetFrame`) is centred at the top, 0.7 of the short side wide:
    the plate with a round window at its left end (crimson gems for what can be fought, blue for an NPC), the name
    and level, and for what can be fought the life gauge with the percentage left (eased, with a trail; a new target
    starts full). Its target is the monster the player attacks, or else the one picked (`GameplayScreen.Touch.cs` sets
    `TouchTargetId`; `HudCombat.TryGetTargetState` gives its name, level and life). The game draws the target's 3D head
    in the window (`TouchPortraits.Target`, framed by `HudCombat.TryGetPortraitSubject`, at
    `TouchPortraitPlacement.TargetBounds`, whose fractions the touch state hands to the page). The online count and
    the shortcuts sit under it, so a phone lying down keeps them apart.
  - The chat is the Android client's: a 44 px orb 4 px under the player frame and the last five lines 52 px under the
    frame, each in its channel's colour with the speaker's name brighter and bold (`chatLineParts`, the Android
    `VulkanChatText` colours). The orb opens the chat window on a HUD plate: "Mensagens na tela" with Mostrando/Ocultas
    (kept in `localStorage` as `avalon.touchChatFeed`), every line the chat keeps in a recessed well, and a field with
    Enviar. The page reads the newest lines of the All tab with `BrowserHost.TouchChat` (5, or 64 while the window is
    open) when the log's `Added` count grows, and sends a line with `TouchChatSend`, which goes through the chat
    window's own Enter (`HudChat.SubmitText`: commands, whispers, the flood rule, the input limit). The window keeps
    the game's keys while it has the keyboard.
  - Auto-caça is the third orb beside Menu and Bolsa, as in the Android client (the top shortcut is hidden with the
    touch HUD): a tap opens the auto-hunt panel, or pauses the hunt while it runs; a 500 ms press opens the panel.
    While hunting the orb reads Pausar in gold (`WorldAutoHuntActive`, `GameplayHud.AnyHuntRunning`) and a green
    "Auto-caça · Ligado" chip stands above the orbs, for the free hunt while its Mostrar o quadro option is on
    (`WorldHuntStatusShown`, `GameplayHud.HuntStatusShown`). With the touch HUD the orb runs the Android client's free
    auto-hunt (`FreeHuntSession`); the PC HUD keeps the quest hunt. The free hunt:
    - starts where it is switched on and fights the living wild monsters within its radius that the filters accept
      (`FreeHuntSettings.AcceptsMonster`), quest, boss and ranged attackers first, a monster attacking the player
      even past the radius, others' claimed monsters left alone;
    - gives up a target that makes no progress, and walks back to the start along a planned route
      (`GameScene.PlanRouteAsync`), moving the start where it is stuck;
    - casts the chosen skills in their order (`GameScene.TryCastQuestSkill`), an area skill first when enough monsters
      stand in its radius, each above its mana floor, a buff only when about to end, and swaps to the shield set for a
      shield-only buff;
    - drinks the smallest potion that reaches each threshold (`HuntPotionSelection`, from the server's potency ranges,
      `PotionPotency`), critical life first, and refills the belt;
    - releases crystal pets, sets how they choose prey (`CrystalPetTargeting`), and uses Force Orbs and premiums;
    - picks up drops by the loot rules (`FreeHuntSettings.LootPriority`, `GameScene.PickUpForHunt`) within its loot
      radius, gold only for a minute after a refused pickup;
    - stops or returns to town on death, no party, a full bag or no life potion, as its safety options say;
    - pauses `ResumeMs` after the player's own joystick, tap, Atacar or skill; and stops on a warp, a map change, the
      quest hunt or the automatic quest, the PC HUD or a page in the background.

    Atacar and an enemy skill with no marked target take the nearest wild monster within the hunt radius. Every
    command is the manual one, and the server checks every target, item use and pickup.
  - The orb opens the free hunt's panel (`touch-hunt.js`, the Android client's `VulkanFreeHuntPanel`): the tabs Caça,
    Habilidades, Poções, Segurança, Coleta, Ganhos, Grupo and Log two by two on the left; a bar with the state, Iniciar
    or Pausar and ↻ (every option back to its default); and the tab's cards. Coleta searches the loot catalog
    (`wwwroot/auto-hunt-items.tsv`), which the game classifies once (`BrowserHost.ClassifyHuntItems`). Ganhos shows
    the session's rates, kills, DPS and collected items. Grupo shows the members' damage, kills, experience and deaths
    as the server counts them (`PartyHunt`). Log filters and searches the 300 latest entries, which the page words in
    its language from their kind and reason. The page reads `BrowserHost.TouchHunt` (with `full`, the options and
    what they offer) and `TouchHuntLog`. It sends the options back whole as JSON (`SetFreeHuntSettings`, checked as an
    imported code is, `FreeHuntProfile.TryRead`) and its buttons as `touch-hunt:<action>`. Caça copies the profile code
    (`ETERVALE-HUNT-1:`, the Android client's format, `FreeHuntProfileCode`) and imports one
    (`ImportFreeHuntProfile`), which pauses the hunt. The options are kept per character, or shared with the account
    on this browser (`FreeHuntProfileStore`, `ClientSettings.FreeHuntProfiles`); the totals and log are kept per
    character (`FreeHuntRecords`), saved every 30 seconds when they change and as the panel closes
    (`settings.persist`).
  - Menu opens the Android client's menu plate above it: the character's name, Inventário, Missões, Eventos (with the
    rewards to claim, also counted on the Menu orb), Habilidades, Corrida, Correios, Personagem, Calendário,
    Configurações (the gear's panel), then every shortcut that the PC layout keeps under Mais (Mais is hidden with the
    touch HUD) and Trocar personagem. The Bolsa orb counts the items new in the bag. The Android menu's Editar HUD,
    Mapas baixados and Sair do jogo have no browser counterpart. The quality line is hidden with the touch HUD, since
    it would cover the experience bar.
  - The quest card and journal are the Android client's (`VulkanQuestTracker`, `VulkanQuestJournal`; `touch-quests.js`,
    markup in `Jogar.cshtml`, styles in `play.css`). The card is 172 px wide at the top right, 5 layout pixels under the
    canvas minimap (the touch state's `top`, a fraction of the screen's height): the step in serif gold (Jornada do
    Aventureiro for the journey), the hunt's count on a red bar, a status line and the button that starts or pauses the
    automatic quest; a count met gives it a breathing gold border. A tap folds it to the title (kept in `localStorage`
    as `avalon.touchQuestFolded`); Settings' quest tracker switch hides it. Every 250 ms the page reads
    `BrowserHost.TouchQuest` (`GameplayHud.TouchQuests.cs`): the step, journey flag, monster, count, timer left, the
    step's own line when it has no hunt, whether the guide or quest hunt runs and whether either could start. The
    automatic quest is the browser's own: Ir (`touch-quest-go`, `TouchQuestGo`) pauses a running guide or quest hunt,
    else starts the journey's guide where the quest has a destination, else the quest hunt. Opening the quest window
    (Missões, its key) while the touch HUD is in use counts a request in the state instead (`TouchJournalRequested`),
    and the page opens the journal: a HUD plate with the Em andamento and Concluídas pill tabs (and an "Auto-missão
    ativa" chip while it runs), a card per entry (the current step first, with its gauge drawn by `hud-art.js`) and the
    chosen entry in a recessed card: the objectives as a checklist (the steps so far, the hunt with its gauge or the
    step's line, the timer), the description's paragraphs (`HudQuestDocuments.Paragraphs`, "Carregando…" while the file
    loads), the reward as a gold chip (`HudQuestDocuments.AvalonReward`) and, on the current step, Ir · auto-missão or
    Pausar auto-missão. The journal reads the entries (`TouchQuest(true)`) only while it is open. The daily rewards stay
    under Eventos.
  - The minimap and field map are the Android client's (`MobileMinimap`, `VulkanWorldMap`). The minimap stays on the
    canvas, which has the field's map and moves every frame, but with the touch HUD it sits 10 layout pixels from the
    upper right corner and is drawn as the Android one (`HudMinimap.Draw` with `HudLayout.Touch`): a dark backdrop, no
    title or frame art, the journey's hunting area in red while its hunt counts (`TribeRoute.HuntAreaIn`,
    `GameplayHud.JourneyHuntArea`), the quest guide's route as gold dots (`GameplayScreen` hands its points to
    `GameplayHud.QuestRoute`), a gold edge and a gold arrow on the heading; the NPC, party, guide and Battle Royale
    marks stay. A tap on it counts a request (`ClickTouchMap`) and the page opens the field map (`touch-map.js`): a HUD
    plate over the whole screen with the field's name, the field's map picture (`TouchPicture` of `HudArt.FieldMap`)
    that pans by dragging and zooms by pinching, the wheel or + and − (1 to 8 times the whole field; it opens at 2 on
    the player and ◎ centres it again at 2.5 at least), and over it the route's dots, the journey's hunting area with
    its count, or the way out towards the hunting field, the tribe's guide as a gold star, every NPC in view with its
    name where it covers no other name, and the player's arrow. The side lists the objective (with the count and the
    hunting field), the NPCs nearest first (★ for the guide) and a legend. While it is open the page reads
    `BrowserHost.TouchMap(true)` every 250 ms (`GameplayHud.TouchMap.cs`): the map's size and every mark in the map's
    units, x east and y south of its top left corner (`HudMinimap.MapPoint`); closed, only the request count.
  - The bag is the Android client's character window (`VulkanInventoryPanel`; `touch-bag.js`). Opening the inventory
    (Bolsa, Inventário in the menu, its key) while the touch HUD is in use counts a request (`GameplayHud.ToggleInventory`)
    and the page opens a HUD plate: the ten equipment wells around a faint figure with gold and a weight gauge under
    them (red when overweight), the 12 x 6 bag as a field of wells with each item's picture at its footprint (`TouchPicture`
    of its icon), its upgrade at the top left and count at the bottom right, and the potion belt under it. A well's edge
    takes the item's tint (unique amber, enhanced blue, red when the class cannot use it), gold when chosen and glowing
    at the highest upgrade. A chosen item opens its sheet: picture, name, kind and rank chips, "cannot use", the
    maturing gauge, the desktop tooltip's lines (`HudItemDetailTooltip.Lines`) as banded rows, and its buttons, which
    `HudItemActions.Offered` decides: Guardar na bolsa for a belt potion, Beber and Cinto 1–3 for a bag potion, Usar for
    a usable item, Equipar or Retirar for gear. A button runs `touch-bag:<action>:<item>[:<box>]` through
    `HudInventoryController.Run`, which sends only what the item offers and one operation at a time (the buttons are
    disabled while one is pending); Usar closes the bag, since a usable item's confirmation is a canvas box. While it is
    open the page reads `BrowserHost.TouchBag(true, <chosen item>)` every 250 ms, which also counts the bag's items as
    seen for the Bolsa button's badge. Shops, the warehouse and other services still open the canvas inventory beside
    them. `touch-dom.js` holds the pieces the journal, map and bag share (elements, chips, sections, gauges, `format`).
  - The status and skills windows are the Android client's (`VulkanStatusPanel`, `VulkanSkillsPanel`; `touch-status.js`,
    `touch-skills.js`). Personagem or Habilidades in the menu, their keys and a skill master's offer count a request
    (`GameplayHud.ToggleStatus`, `ToggleSkills`, `OpenSkillMaster`) and the page opens the window. The status window
    shows the level medallion (`hud-art.js`), class, experience and vitals; the five attributes with their emblems, what
    each raises, a bar against the strongest and "+"/"−" that plan free points on the page (the class's damage
    attribute, `CharacterClassRegistry.DamageAttribute`, marked Dano); the combat tiles with what the planned points add
    (`AttributeGains`), and Desfazer and Confirmar. The page reads `BrowserHost.TouchStatus(true, <plan>)` while it is
    open; Confirmar runs `touch-points:<five amounts>` (`GameplayHud.TryReadPoints` checks them against the free
    points), and the game sends one `AllocateAttribute` per attribute, each after the last answered (`SendTouchPoints`,
    each update), stopping at a refusal; the answers go to the chat as before. The skills window has a pill tab per
    class tier, the tier's skills as cards (button picture in its tier's ring, name, rank, the touch button holding it,
    "+" when `HudSkillsPanel.CanTrain` allows the next rank and a skill master was talked to) and the chosen skill's
    sheet (description, cost, `HudSkillDetails.Lines`, what the next rank needs from `SkillLearning.Check`, and buttons
    1–4 that put a skill on a touch skill button through `HudSkillBindings.Assign`). "+" runs
    `touch-train:<index>`, a `LearnSkill` asked of that skill master. `BrowserHost.TouchSkills(true, <tier>, <chosen>)`
    feeds it while it is open.
  - A long press (500 ms) on a skill button opens the Android client's list to choose its skill (`touch-skill-picker.js`),
    as a tap on an empty button does. Empty buttons are drawn dimmed but take taps; only death turns them off. The
    list is a HUD plate in the middle of the screen with each skill a button may take (picture, name and the button
    holding it, the one on this button highlighted). A tap runs `touch-skill-button:<slot>:<index>`, as the skills
    window's buttons 1–4 do, and the button that held the skill gives it up (`HudSkillBindings.Assign`).
    `BrowserHost.TouchSkillChoices()` hands out the list. Which skills a button may take is
    `TouchSkillSlots.Eligible`: learned, cast with a mouse button and not passive, as the server lets a skill be cast.
    The list, the skills window's buttons and the action that assigns all check it.
  - The NPC plate is the Android client's (`VulkanNpcPanel`; `touch-npc.js`): a HUD plate at the bottom, centred
    between the Menu and Bolsa buttons and the action wheel (over the HUD when that gap is under 300 px), with the
    name as a section heading, the line in a recessed card that scrolls past 84 px and the buttons along the bottom,
    the first highlighted. It shows, in the canvas's order, the yes/no box after a quest dialog (detail and question,
    `HudQuestDialog.Question`) or offering a skill reset; else the quest dialog (title, the NPC it answers to, the
    document's paragraphs from `HudQuestDocuments.Paragraphs`, a reward list's choices, a reward view's arrows, and
    OK worded by what it leads to, with Fechar beside it when OK answers the quest); else the NPC's dialog
    (`HudNpcPanel.Said`) with its service buttons, or a teleporter's destinations (`HudNpcPanel.DestinationLabel`),
    and Fechar. The clan, castle and Bellatra windows still replace the NPC's dialog on the canvas. The page reads
    `BrowserHost.TouchNpc()` with the other touch state and answers with `touch-npc:<action>[:<value>]`
    (`GameplayHud.TryReadNpcAction`: service, destination, close, ok, choose, browse, yes, no), run as the canvas
    buttons run (`OpenNpcService`, `ChooseNpcDestination`, `CloseNpc`, the quest controller). These answers pass
    `RunWorldAction` ahead of its modal check, since they answer the modal dialogs themselves. The Android plate's live
    3D portrait of the NPC is not drawn.
  - The combat feed is the Android client's (`VulkanCombatFeed`; `touch-feed.js`): a canvas at the right above the
    action wheel, 60% by 30% of the screen's height, where each kill, experience and gold gained within a level, level
    reached and quest count grown rises and fades over 3.2 seconds, at most five at once, under a counter of the kills
    since entering the world that flares at each one. `HudCombatFeed` keeps the newest eight lines as data (kind,
    amount, count, name) from the character (`GameplayHud.Update`), the quest view and each kill (`HudCombat` raises
    `MonsterKilled` when the player's own hit kills a monster); the page reads `BrowserHost.TouchFeed()` with the
    other touch state, words the lines past the count it last saw and draws them with `hud-art.js`.
  - The party is the Android client's (`VulkanPartyHud`; `touch-party.js`). While a reached player is the target
    (`ShowsPlayerButtons`), a row of buttons under the target frame offers what the canvas offers there: Trocar,
    Convidar p/ grupo (highlighted), Amigo, Clã for a recruiter beside a player without a clan (`ClanButton`) and Duelo.
    The other members' plates (170 by 40 px, a crown before the leader's name, the level and the life gauge) stand
    under the player frame. A tap opens the party's commands on a plate in the middle of the screen: Passar
    liderança, Expulsar do grupo and Desfazer o grupo for the leader, and Sair do grupo for anyone. A party, clan,
    trade or duel request shows on that plate with Recusar and Aceitar, the one the canvas would draw on top. The page
    reads `BrowserHost.TouchParty()` with the other touch state; member ids travel as text, being 64-bit. It answers
    with `touch-party:player:<button>`, `touch-party:command:<command>:<member>` or `touch-party:yes|no:<kind>`
    (`GameplayHud.TryReadPartyAction`). These run as the canvas's buttons run (`UsePlayerButton`, `ChallengeReached`,
    `AnswerClan`), and the game checks a command against who leads. While the touch HUD is in use, forming a party does
    not open the PC's party window, and Amigo adds the friend without opening the PC's friends page.
  - The death plate is the Android client's (`VulkanRestartPanel`; `touch-restart.js`): a HUD plate in the middle of
    the screen, shown while the restart window would be (`HudRestartPanel.Open`, after `OpenDelayMs`). It says what the
    death costs (the offer's experience and gold shares, the Resurrection Scroll that waives them, or none), then
    Recomeçar no mapa (dark where the field restart is closed) and Recomeçar na cidade (highlighted), and the seconds
    before the town is chosen by itself (`HudRestartPanel.SecondsLeft`). It offers no exit, as the Android plate does
    not. The page reads `BrowserHost.TouchRestart()` and answers with `touch-restart:Field|Town`
    (`GameplayHud.TryReadRestartAction`), which `HudRestartPanel.Pick` runs as a canvas click would, ahead of
    `RunWorldAction`'s modal check, as the canvas takes it ahead of the NPC and quest dialogs.
  - Eventos & Recompensas is the Android client's (`VulkanRewardsPanel`; `touch-rewards.js`): the menu's Eventos opens
    it in place of the canvas's window (`OpenRewards` counts the request, as the quest journal's). The window has these
    parts:
    - The reward tracks as tabs in a column on the left, each with a red badge of what it has ready.
    - A strip with the time to the track's end or reset (counted down since the board arrived) and its progress.
    - The track's entries:
      - the welcome days as cards side by side, the last one larger, a ready day claimed with a tap;
      - the weekly and daily goals as rows, each with its count and a Receber button;
      - the level race as rows beside a diamond with the level.
    - Each entry's item rewards in wells with their pictures, and gold.
    - A footer with what is ready, the track's note and Receber tudo.
    - A ? button that swaps the entries for the track's help.

    The page reads `BrowserHost.TouchRewards()` and, while the window is open, asks the server again every five
    seconds with `touch-rewards:open:<track>`. It claims with `touch-rewards:claim:<track>:<entry>` or
    `touch-rewards:all:<track>` (`GameplayHud.TryReadRewardsAction`). These go one at a time through `ClaimRewards`,
    shared with the canvas window. The server's answer to a claim takes the note's place in the footer, where the
    Android client shows a toast.
  - The Settings, News & Support and auto-hunt windows drag by their headers with a finger (`draggable.js`).
  - A phone or tablet plays lying down, as a mobile game (`touch-hud.js`, `root.dataset.mobile`): the first touch on
    the page asks for full screen and locks landscape (`enterLandscape`; browsers that refuse either, such as iOS
    Safari, keep the page as it is), and while the device stands a plate asks to turn it ("Gire o celular", with
    "Continuar em pé" to play upright anyway).
  - The first world entry on such a device, while the interface type is still `Auto`, asks "Como você quer jogar?":
    HUD de celular (`Touch`) or HUD de PC (`Pc`). The answer is saved as the Settings panel saves
    (`settings.setInterfaceMode`), so the question does not return; Escape leaves it for the next entry.
  - The page installs as an app ("Adicionar à tela inicial") from the site's manifest (`/manifest.webmanifest`,
    [website.md](website.md)): it opens full screen in landscape.
  - Editar HUD in the menu opens the Android client's HUD editor (`HudLayoutEditor`; `touch-layout.js`). The screen
    dims, each shown part of the HUD is outlined with its name, and a finger drags the smallest part under it, kept on
    the screen. A round button also gets Tamanho (65–160%) and Transparência (0–80%) sliders on the editor's panel,
    which drags by its title. Salvar keeps the layout, Padrão puts every part back, and Cancelar or Escape drops the
    changes. The parts are the player and target frames, chat, chat history, Menu, Bolsa, Auto-caça, quest card,
    Atacar, the four skill buttons, the three belt potions, the combat feed, the experience bar and the party plates.
    Each moves by a CSS `translate` in short sides (`--touch-unit`), and a button also scales and fades. The layout is
    kept in this browser (`localStorage`, `etervale.touch-layout`), and the game hears where the frames went
    (`BrowserHost.SetTouchFrameShift`, `TouchPortraitPlacement`) to draw the 3D heads in them. The minimap, which the
    game draws on the canvas, does not move, and the target frame and party plates can only be moved while shown.
    The Android menu's Mapas baixados and Sair do jogo have no browser entry: the site fetches the map packs itself
    and a page cannot close its tab.
  - The first-time tutorials are the Android client's (`VulkanTutorial`; `touch-tutorial.js`). The first time
    something is needed, the screen dims around the button to press, a gold ring pulses round it and a card explains
    what to do. Only that button answers taps: a tap in its window is passed to it, and the rest of the screen waits. A
    step moves on by itself once its goal is met, or with Entendi on a step that only explains; Pular tutorial ends the
    lesson. The lessons, in order:
    - the HUD on arrival: the player frame, the status (done once the status window opened), the quest card, Atacar
      and Menu;
    - wearing the starter kit: Bolsa, the piece's tile, Equipar;
    - a potion on the belt: Bolsa, the potion's tile, Cinto 1;
    - the automatic quest: the quest card's button;
    - the first attack: Atacar while a hunt has no kill yet and the target can be fought;
    - spending attribute points: the portrait, the free points, the main attribute's "+" and Confirmar.

    None starts while the automatic quest runs, except the points, which wait only for a moment out of a fight. A
    lesson whose window closed midway starts over. Each lesson shows once, kept in this browser (`localStorage`,
    `etervale.touch-tutorials`, with a round that brings them back when they change), and Rever tutorial in the menu
    shows them all again. The game tells when lessons start and steps end (`BrowserHost.TouchTutorial`: alive, idle,
    the starter piece (`TouchStarterPiece`), the bag potion and empty belt, a hunt not begun, a target that can be
    fought, the points and whether the player attacks). The page finds its targets by the bag's `data-item` and
    `data-run`, the status window's `touch-status-free` and main row, and the quest card's auto button. The coach is a
    popover, so it stands over the bag and status windows. Unlike the Android lesson, the quest card is not folded
    before pointing at Atacar.
  - Not ported yet: the PC's party window with its friends and clan pages, which the Android client lacks; the browser
    keeps the desktop HUD on the canvas for it, at its own scale. What
    the touch HUD replaces is left out of the canvas while it is in use (`WebWorldHud.TouchActive`: the page reports
    fingers and the interface type asks for it): the bottom bar with its orbs, menu buttons and toggles, the action
    bar, the belt, the skill sockets, the chat window, the target frame, the quest tracker and the quest window, the
    inventory, status and skills windows when the menu or their keys open them, the NPC and quest dialogs and the
    quest yes/no boxes, the buttons under a reached player's portrait, the party, clan, trade and duel request boxes,
    the restart window and the Eventos & Recompensas window, both drawn and taking clicks, so a tap there reaches the world. The canvas minimap moves to the upper right
    corner, as on the Android HUD, since the skill wheel holds the lower right one (`HudLayout.Touch`,
    `HudMinimap.Box`).
    Canvas windows take taps but not drags.
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
  less than 500 ms after the first enters appearance selection through the same path as Next; it does not
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
- The page's words: `page-language.js` lists the languages (`pageLanguages`, each named in itself; the picker is built
  from it). Portuguese and English are written beside each phrase, as `say(pt, en)` or as a table per language
  (`login-texts.js`, `heroes.js`, `touch-hud.js`, `world-hud.js`); every other language is a catalog in
  `languages/<code>.js` with its phrases keyed by their English words and its tables by name. `word` and `table` pick
  the shown language, then the catalog, then English. `page-language.test.mjs` checks that each catalog words every
  phrase the page makes and has every table in the English shape. A new language: add its catalog to `catalogs` and
  `pageLanguages`, beside `ClientSettings.SupportedLanguages`; the page's own fonts draw every script, so Chinese needs
  nothing more here.
- The gear opens the Settings panel (`settings.js`, with rows in `settings-options.js` and styling in the site's
  `css/settings.css`). Vídeo includes the texture quality, quality presets, WebGPU adapter details, scene render scale,
  the FPS cap, the window mode, effects and interface, name and damage-number options. The cap paces the browser's
  animation callbacks after the first four frames. Áudio holds the music and the sound effects, each with its switch
  and volume ([Sound](#sound)). Teclas groups the game bindings with per-row and global reset. Outros includes
  language, game, camera, streamer mode, window resets, chat notifications, ground items and Noxius-specific settings.
  Every row edits a `client-settings.json` field (checked by `SettingsOptionsTests`) or runs an action; a row the
  renderer cannot draw (`getCapabilities().unsupported`) is not shown, and a card left without rows is not either. The
  finishing effects of "Gráficos aprimorados" are disabled under the classic light and the fog distance without fog.
  A row with a "?" opens its explanation below it on a click and closes it on the next. Its header drags the panel within the viewport (`draggable.js`), as it does the auto-hunt panel and News
  & Support. The panel edits the
  `client-settings.json` document ([client-settings.md](client-settings.md)) and leaves the rules to the game:
  `BrowserHost.Settings` hands out the current document, `SettingsWithQuality` applies a preset (`WithQuality`),
  `Shortcuts`, `BindShortcut`, `ResetShortcut` and `RestoreShortcuts` use `HudSettingsPanel`'s shortcut list and rebinding (a refused
  key returns an empty string), "Redefinir interface" puts the page's windows back and takes the draft from
  `ResetInterface` (`ClientSettings.WithInterfaceReset`: HUD scale, the chat's place, classic interface, move chat and
  tutorial tips), "Modo PvP: ligar / desligar" asks the server to switch the PvP mode as K does (`SwitchPvp`,
  false outside the game; the answer comes in the chat, [application.md](application.md#pvp-mode)), and `SaveSettings` validates the document, sets `PresentationSettings.Requested` and
  applies its rendering fields at once, keeping the page's language. Changing a preset field in the panel sets the
  quality to Custom, as the game's own panel does. The page keeps the selected browser preset label under
  `avalon.settings.quality`. The saved document is kept in `localStorage` (`avalon.settings`)
  and handed to the game with `LoadSettings` before `Start`; without one the game starts at the Low preset. Leaving the
  page stores the game's current document, so changes made in the game's own panel are kept. The music settings
  also apply to the page's opening music.
  `settings-effects.js` applies what the page itself does: "Modo janela" off enters the browser's full screen on Save
  (a user gesture); "Confirmar antes de fechar" asks before leaving while `BrowserHost.PlayerLife` reports the world;
  every 250 ms it reads the life (-1 outside the world, 0 dead, else the fraction) to grey the canvas while dead,
  pulse a red border under 30 % of life and, with the tab hidden, show a system notification of the death (permission
  asked on Save). It hides the corner shortcuts (`hideHudIcons`) and the page's world HUD while F12 hides the game's
  (`BrowserHost.HudHidden`). Before `Start`, the page resolves "Texturas" (Auto: Very light up to 2 GB of
  `navigator.deviceMemory`, Light up to 4 GB, else Original) and calls `BrowserHost.SetTextureQuality`, which shrinks
  the scene textures as they decode (`TextureDownscale`); a change applies on the next load.
  Before the runtime has loaded, the panel
  shows a waiting line that is replaced by the controls when the runtime is ready. The fullscreen button covers the
  whole page, including the panels.
  The enabled "Resolução dinâmica" switch edits the persisted `dynamicResolution` preference without changing the
  selected preset. The renderer reports transient state through
  `settings.setDynamicResolutionState({ enabled, scale, width, height, shadowUpdateRate })`;
  while the actual enabled scale is below the
  saved render-scale cap, the world quality badge appends its actual 3D dimensions and percentage, such as
  `Alto · 3D 1024×576 (80%, dinâmica)`. Restoring the cap or disabling the reported state removes that suffix.
  An optional actual shadow cadence below the saved rate appends `· sombras 30 Hz` (or 15 Hz), including when scale has returned
  to its cap. An absent/zero cadence or the preset's existing cadence adds nothing. These reports do not save settings
  or mark the preset Custom, and repeated identical labels do not refresh the HUD.
- The desktop and Android clients never set `WebLoginOverlay.Report` and keep `LoginPanel`.

## Sound

The game plays its music and sound effects in the browser as on the desktop, through the same `PristonAudioLibrary`,
`FieldAudioController`, `NpcAudioController`, `CombatSounds`, `SkillSoundBank` and `LoginAudio`
([client.md](client.md#audio)). Only the files' arrival and the output differ.

- Output: the fork's browser runtime implements MonoGame's audio voices over the page's Web Audio
  ([monogame/issues/025](../monogame/issues/025-web-audio.md)). `main.js` passes `createWebAudio()` (`web-audio.js`)
  to the .NET module as `noxiusAudio`: sound buffers become `AudioBuffer`s, voices are a buffer source through a gain
  and a stereo panner. The `AudioContext` starts at the page's first pointer, key or touch (the autoplay policy); the
  login form needs one, so the game's sound is running by character selection. Until then a one-shot sound is dropped
  and a loop waits.
- Files: the sound files (about 2 600 `.wav` and `.bgm`, 300 MB) are not in `bootstrap.txt`, a download wave or a
  world pack. The publish lists them in `audio.txt` (`WebSoundIndex`, same "path, tab, SHA-256" lines, all under
  `assets/`), which `BootstrapFiles.FetchAsync` reads beside the opening's files and hands to `BrowserSoundFiles`
  (`SoundFiles.Use`). Folder listings come from that index, and each file is downloaded the first time the game reads
  it, four at a time, as `path?v=<hash>` (cached for good like the other assets). Without `audio.txt` the game runs
  silent.
- Loading: with that on-demand source `PristonAudioLibrary` decodes nothing at start. It starts downloading the short
  cues (item, quest, portal, whisper, rain, thunder and character-screen cues); music, ambience, Bellatra and NPC sounds
  are fetched when first asked for, and the controllers ask again on each update until the file is there, so a field's
  music starts a few seconds after the field. The page keeps playing the opening music (`Intro.wav`) itself, so the
  game's library leaves it out. Skill, weapon, footstep and monster sounds use `SkillSoundBank`'s background read as
  on the desktop, with the download before the read.
- Settings: "Ativar música", its volume, "Ativar efeitos sonoros" and its volume act on Save, without a reload. The
  login and character screens ignore them, as the original's did.
- Measured cost: a sound decodes once into float samples in the page's memory (an 8-bit stereo music file takes four
  times its size). The first play of a field's music converts it on the game thread, a hitch of a few tens of
  milliseconds once per music.

## World packs

The world wave is about four thousand files. When the client folder holds world packs, `FetchWorldAssetsAsync`
fetches them as a few dozen ZIP chunks instead of one request per file (`WebAssetPacks.TryFetchAsync` in
`Shared/PristonTale.Assets`); `tools/PristonTale.WebAssetPacks` writes them
([tools.md](tools.md#webassetpacks-ptwebpacks)) and the publish scripts run it.

- `world-packs.json` names the SHA-256 of the exact `bootstrap.txt` bytes it was built from and lists every bootstrap
  file once, with the bootstrap's spelling, size, hash and chunk. Chunks live at `world-packs/<SHA-256>.zip` and are
  requested with `?v=<SHA-256>`, so the site caches them for good like the per-file assets.
- Only the opening's downloads stay per file; packs are read only when a character enters the world. The reader takes
  the chunks holding a missing world file, two at a time, and extracts only the missing files. A chunk whose files are all
  present is not requested. Progress starts from the files already present and rises by one per written file.
- Each chunk is checked whole (size and hash) before it is opened, then its entries against the index (exact names,
  count, sizes). Every file is written to a temporary name and moved into place only once its listed size arrived. Files
  are not hashed again in the browser: the chunk's hash covers them, the generator inflated each entry against its
  bootstrap hash, and the browser's managed hashing is slow. The reader hands the page back after every 12 ms of work,
  shared by both chunk workers; browsers hold a chained timer for at least 4 ms, so shorter slices idle the download.
- An absent, stale or malformed index, a bootstrap with unsafe or case-colliding paths, a failed request or any failed
  check stops both workers and returns to the per-file downloads, which skip the files already written and keep the bar
  where it was. A cancellation by the caller is passed on, never turned into that fallback.
- ZIP chunks cost a few percent more transfer than the per-file Brotli/gzip sidecars; they save thousands of requests.

## AOT publish

The publish compiles the game ahead of time to WebAssembly (`RunAOTCompilation`, unless `-p:EtervaleAot=false`).
Interpreted, a field's C# `Update` and `Draw` alone took about 19 ms per frame at p95 and held the field below 60 FPS;
compiled, they take about 8–11 ms, and the field's world packs are written faster. The native module grows from about
3 MB to 32 MB (6.5 MB with Brotli), and the publish takes 15–35 minutes.

- AOT needs the trimmer. `TrimMode` `partial` trims only the framework's assemblies; the game's and MonoGame's stay
  whole, since they create types by reflection (content readers, JSON).
- The game reads its JSON by reflection, which trimming turns off: `JsonSerializerIsReflectionEnabledByDefault` keeps
  it on and the trim analysis warnings are suppressed. Moving those reads to source-generated JSON removes both.
- `WasmStripILAfterAOT` is off: stripping fails on the framework's facade assemblies, which have no compiled methods.

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
Three consecutive windows below 98.5 percent of the target take one step; the target is 60 FPS or a deliberately
lower foreground limit. The first two steps cap shadow refresh at 30 Hz, then at 15 Hz, while retaining the selected
shadow-map size, filter and enabled effects; only then do further steps lower the 3D scale, down to 50 percent. A
field is usually bound by the render worker's CPU, where the shadow pass adds about 2 ms and 180 draw calls to the
frames that redraw it, while a lower scale saves little and blurs the whole image (on a Vega 8 iGPU Baixo and Médio
hold 60 FPS at 100 percent). This changes shadow motion cadence and is reported in the quality badge; it does not
claim identical temporal quality. Under the classic light (`classicLighting`) the scale stays at the saved cap;
pressure only takes the two shadow steps. A window under 10 FPS is the browser throttling an occluded window, not
GPU pressure: it is discarded and measurement warms up again. Six windows at or above the healthy threshold allow one
recovery probe, in reverse order: the scale first, then 30 Hz and the saved shadow rate. The warmup after a change
is five seconds, so the shader compilation of a new preset or map is not measured. After the initial loader
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
phase and bridge counters. Its cumulative `animationPoses` counts root pose refreshes/holds and exact raw-pose cache
hits/misses, so shared skeleton reuse can be verified separately from FPS. The courtyard cache never rounds sample
time; world and native renderers keep it disabled. Profiling is disabled by default.

The world HUD shows the applied graphics-quality preset at the bottom-left edge, including the effective preset
when Automatic is known. It uses the gear's saved UI choice rather than an internal Custom classification caused
by capability normalization. Saving a manual graphics change updates the badge to Custom immediately; cancelling
a draft does not change it. The badge shares world-HUD visibility and never captures pointer input.
Visible roster, tribe, class and appearance panels also show FPS at the top-left and the same quality label above
the bottom-left back button. These character-screen badges reset their sample window on stage/visibility changes,
use the existing render-count source, and show an unavailable mark rather than counting logic ticks when that source
is absent. They stay hidden during map loading and do not activate world controls, ping requests or auto-hunt queries.
The same opt-in records `[StageInput]` and `[StagePick]` diagnostics for character selection: browser event times and
click details, normalized coordinates, actor hits, pending gesture state, resets and discarded commands. Confirmation
uses the browser-only `WebStageClickTracker.DoublePressSeconds` limit (strictly below 500 ms) and the same selected
actor on both clicks. Native and Android retain their existing 350 ms policy. Browser event timestamps, rather than
the frame that drains the queue, determine the interval.

The browser maps authored texture layers, material blend modes, animation frames and UV scrolling, floor lighting,
actor colour adjustments, fades, overlays, outlines and distortion to Three materials. The sky uses the authored
geometry and profile crossfade. Directional shadows use one conventional-depth map at the requested size, with the
native per-frame eligibility rule: enabled preference, nonzero directional illumination and an above-ground scene.
The authored moon remains a shadow-casting directional light at night.
The explicit Compose callback applies the Three HDR/post chain after the world and linear effects, before native HUD
sprites. It supports render scale, GTAO, reflections (traced on alternate frames by default; with
`sceneryOnlyReflections` the scene pass adds a `reflectColor` target that actor materials, given
`sceneryReflectionMrt` only around the world draw, leave black, so the trace sees the scenery alone), bloom, FXAA, SMAA, MSAA, exposure compensation, tone mapping and
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
- After switching a publish between AOT and interpreted (`-p:EtervaleAot=false`), delete the project's `bin` and `obj`,
  or publish with its own `--artifacts-path`: the native link's interop tables are cached there, and tables built from
  the other kind of assemblies stop the runtime at start with "runtime and class libraries are out of sync". The trimmer
  also bakes the JSON reflection switch into the trimmed framework, so an incremental publish that only changes it keeps
  the old value.
- Keys reach the game as `keydown`/`keyup` on the focused canvas. Text inserted without key events (paste, a password
  manager's fill, the soft keyboard of a phone, an IME) does not reach the game's own text fields (chat) yet; the login is
  the page's HTML form, so it is not affected.
- The browser runtime's songs decode on a `std::thread` (`common/MGM.cpp`), which a single-threaded WebAssembly build
  cannot start, so `Song` and `MediaPlayer` do not play there; the game plays its music as `SoundEffect`s.
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
- The site sends precompressed variants for indexed `assets/` and `Content/` files and for the fingerprinted
  `_framework` files, from the `.br` and `.gz` the publish writes beside them ([website.md](website.md)). The loader
  `dotnet.js`, the page's scripts and everything else under `/play` go out as stored. A publish that drops those
  sidecars sends the AOT native module raw: 32 MB instead of 6.5 MB.

## Related

- [protocol.md](protocol.md#websocket-transport): the WebSocket transport.
- [server.md](server.md#websocket-edge): the edge's settings, caps and layouts.
- [website.md](website.md): the play page, its policy and the `Play:*` settings.
- [client.md](client.md): `GameServerConnection` and the desktop client the browser will share.
- [android.md](android.md): the Vulkan Android client and its asset packages.
