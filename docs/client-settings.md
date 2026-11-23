# Client settings

Every field of the client's `client-settings.json`: defaults, valid values, validation and fallback, quality presets,
the in-game Settings panel, and how a change reaches the display, the renderer and the audio. Code:
`Client/PristonTale.Engine3D/Rendering/ClientSettings.cs` and `IO/ClientSettingsStore.cs`, applied by
`Client/PristonTale/Display/`.

## Read this when

- Adding, renaming or removing a client option.
- A user's settings file is ignored ("using defaults") or a value silently resets.
- Changing a quality preset or the Settings panel rows.
- Investigating why a display change reverted after 15 seconds.
- Running a benchmark or capture with a specific settings file (`--settings`, `--benchmark-render`).

## Code map

| Path | Responsibility |
|---|---|
| [`ClientSettings.cs`](../Client/PristonTale.Engine3D/Rendering/ClientSettings.cs) | The record: fields and defaults, `Validate`, `WithQuality` (presets), `HasSameRendering`, `HasSameDisplay`, `FrameLimit`. |
| `Client/PristonTale.Engine3D/Rendering/GraphicsOptions.cs` | Enums `GraphicsQuality`, `AntiAliasingMode`, `TonemapperMode`, `BloomQuality`, `ShadowFilterMode` (serialized by name). |
| `Client/PristonTale.Engine3D/Rendering/DistanceFogMode.cs` | `DistanceFogMode` and its converter (accepts older booleans). |
| `Client/PristonTale.Engine3D/Rendering/PristonFrameContext.cs` | `PristonDebugView`; `PristonDistanceFog.For` (fog mode to fog parameters). |
| `Client/PristonTale.Engine3D/IO/ClientSettingsStore.cs` | Load, version check, read-only protection of unreadable files, asynchronous atomic save. |
| `Client/PristonTale.Engine3D/Rendering/PresentationSettings.cs` | Shared state between the UI and the display controller: `Requested`, confirmation flags, `ApplyRendering` and the `RenderingChanged` event. |
| `Client/PristonTale/Display/PresentationController.cs` | Applies requested settings at the frame boundary, display preview and 15-second confirmation, rollback, frame pacing, save on accept. |
| `Client/PristonTale/Display/DisplayController.cs` | Window mode, back-buffer size and vertical sync through the Vulkan platform. |
| `Client/PristonTale.Engine3D/Rendering/WindowPlacement.cs` | Where the windowed window goes: centred on the primary monitor, or its saved position while the title bar stays on screen (`DisplayController` applies it and maximizes a window that was). |
| `Client/PristonTale/Display/DisplayValidationProbe.cs` | `--validate-display REPORT.json`: cycles display configurations and checks pacing. |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSettingsPanel.cs` | The in-game Settings panel (system menu, entry Settings): rows, clicks and labels. |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSettingsPanel.Drawing.cs` | How the panel is drawn: icon tabs, two titled cards, switches, drop-down value boxes, footer buttons. |
| `Client/PristonTale.Gameplay/UI/DisplayConfirmation.cs` | Confirm (Enter or button) / revert (Escape or button) prompt of a display change. |
| `Client/PristonTale.Engine3D/Rendering/PristonRenderer.cs` (`ApplyQuality`) | Reads the rendering fields when `RenderingChanged` fires. |
| `tests/PristonTale.Tests/ClientSettingsTests.cs`, `PhotorealTests.cs` | Validation, older fog booleans, persistence, invalid documents, pacing; preset contents. |

## File location and lifecycle

- Path: `client-settings.json` beside the client executable (`AppContext.BaseDirectory`); the game never stores
  settings in a per-user profile folder. `PristonTale --settings PATH` uses another file (`Client/PristonTale/Program.cs`).
  A missing file is created with every key at its default.
- The browser client has no file: its page keeps the same document in `localStorage` and passes it through
  `ClientSettingsStore.Serialize` and `Parse` (same JSON rules; `Parse` returns null for a document `Load` would reject)
  ([web-client.md](web-client.md#entry-page)).
- JSON: camelCase property names, case-sensitive, indented on save. Enums are written and read by name.
- Load (`ClientSettingsStore.Load`, on the main thread before the window is created):
  - no file: defaults, and the defaults are queued for saving;
  - `version` other than 1, malformed JSON, `null`, or **any unknown property** (`UnmappedMemberHandling.Disallow`, so a
    typo such as `maxFPS` counts): defaults are used, the error is logged (the panel says "Settings need
    attention"), and the store turns read-only so the user's file is never overwritten in that session;
  - otherwise the document is deserialized (missing fields take their defaults) and `Validate` replaces each invalid
    field with its default, logging `[Settings] Invalid presentation values replaced with defaults.`
- Save: only when a change is accepted (`PresentationController.Accept`). A bounded channel (capacity 1, drop oldest)
  feeds a background writer that writes a temporary file and moves it over the target; the latest snapshot wins and is
  drained on shutdown. Save errors are logged and shown, never thrown into the game loop.
- A file is not rewritten just because it lacks fields or holds invalid values; it is rewritten at the next accepted
  change.

## Fields

"Panel" marks fields the in-game Settings panel edits; every other field is edited in the file. "Display" fields
(`HasSameDisplay`) recreate the swap chain and need confirmation; "Rendering" fields (`HasSameRendering`) reach the
renderer through `PresentationSettings.ApplyRendering` and apply at once.

### Display, audio, pacing and gameplay

| Field | Type, default | Valid values (else default) | Panel | Effect |
|---|---|---|---|---|
| `version` | int, `1` | must be 1 | | Any other value: defaults and a read-only store. |
| `serverAddress` | string, `"localhost"` | not blank | | The game server, read at startup (`Game1.ResolveServerAddress`, `TlsTransport.ParseAddress`): `host`, `host:port` or `tcp://host:port`, a host name or an IP (IPv6 in brackets), port 10009 when left out; `?cert=<sha-256 hex>` pins the server certificate (needed for a remote server whose certificate the system does not trust, such as the self-signed one). An address that does not parse is logged and the client connects to `tcp://localhost:10009`. A change needs a restart. |
| `language` | string, `"en"` | `en`, `pt-BR`, `es`, `fil`, `vi`, `zh-CN`, `ja` | yes | The selected client language. Unsupported values fall back to English. The `.resx` gameplay catalog uses this value; the selected code is sent to the server on the next login. |
| `monitor` | int, `-1` | >= -1 | yes | -1 current. The Vulkan platform offers the primary monitor only; a value above 0 logs a warning and uses the primary monitor. Display. |
| `windowWidth`, `windowHeight` | int, `1280`, `720` | 800-16384, 600-16384 | yes (resolution) | Windowed size (the window is resizable; a manual resize is accepted and saved, except while maximized: the restored size is kept). Fullscreen picks the closest primary-monitor mode; borderless uses the desktop size. Display. |
| `windowX`, `windowY`, `windowMaximized` | int or null, null; bool, `false` | any | no (saved on exit) | Where the windowed window was left: its client area's top-left in desktop coordinates (MonoGame's `Window.Position`) while not maximized, and whether it was maximized. The first windowed apply restores the position while the title bar lands on the primary monitor (otherwise, or without a position, the window opens centred on it with its title bar on screen), then maximizes the window if it was. Later applies centre a window that no longer fits. A platform that places windows itself (a Wayland session) may ignore the position. Not display fields: moving or maximizing the window applies nothing. |
| `windowMode` | `Windowed` (default), `Borderless`, `Fullscreen` | defined enum | yes | Fullscreen is an exclusive mode switch. Display. |
| `refreshRate` | int, `0` | 0-1000 | yes (fullscreen only) | 0 desktop frequency. The platform switches at the desktop frequency only; a nonzero value logs a warning. Display. |
| `verticalSync` | bool, `false` | | yes | Applied without a display reset. |
| `musicEnabled`, `soundEnabled` | bool, `true`, `true` | | yes (BGM, Audio; web: Áudio, "Ativar música", "Ativar efeitos sonoros") | Read by `PristonAudioLibrary.ApplySettings` every update; the login and character screens ignore them. `soundEnabled` also mutes skill, character, weapon and footstep sounds. In the browser `musicEnabled` also governs the page's opening music. |
| `musicVolumePercent`, `soundVolumePercent` | int, `100`, `100` | 0-100 (clamped) | web (Áudio, "Volume") | The music's and the sound effects' volume, read with the switches above; the effects volume scales every world sound, skills and combat included. In the browser the music volume also sets the opening music's. |
| `maxFps` | int, `0` | 0 or 15-1000 | yes: 0, 30, 60, 90, 120, 144, 165, 240, 360 | Foreground frame limit; 0 unlimited. Paced by `PresentationController.Pace` (sleeps in 1-4 ms steps). |
| `backgroundMaxFps` | int, `30` | 0 or 15-1000 | yes (same list) | Limit while the window is inactive: `min(maxFps, backgroundMaxFps)`, or `backgroundMaxFps` when `maxFps` is 0; 0 means "use `maxFps`". |
| `uiScale` | float, `1` | 0.75-1.5 | web ("Escala do HUD") | Multiplies the HUD's scale, the largest integer that fits the 800x600 design (`HudLayout.Create`), never past the scale at which the design still fits. A fractional scale draws the HUD art with linear filtering instead of point sampling. The desktop panel cycles 75, 90, 100, 110, 125 and 150 % ("HUD scale", `HudSettingsPanel.NextUiScale`). |
| `showMinimap` | bool, `true` | | yes (web) | Shared by the gear, the minimap button and its shortcut. A field that hides the map takes precedence. |
| `interfaceMode` | `Auto` (default), `Pc`, `Touch` | defined enum | yes (web: Outros, Interface) | The HUD for mouse and keyboard or for touch (`ClientSettings.UsesTouch`). `Auto` uses the touch HUD on a browser device without a fine pointer (a phone or tablet). Only the browser reads it; the Android client is always touch. |
| `dungeonCamera` | `Free` (default), `TopDown` | defined enum | yes (Other, Camera; web: Outros, Câmera, "Câmera em dungeons"); Shift+Z in game | In a dungeon (`FieldDef.Underground`) only, `TopDown` sets `PristonCamera.TopDown`: the camera looks down at 80 degrees from 600 units, follows the player and never orbits or tilts; the wheel (60 units a notch, reversed by `invertCameraScroll`) and the zoom keys move it between the field's nearest and farthest distance. Camera collision still holds it under a ceiling. Elsewhere, and in action fields, the camera is unchanged. The remappable Shift+Z shortcut flips the saved value (`HudSettingsPanel.WithDungeonCameraSwitched`). |
| `alwaysShowRareGroundLabels` | bool, `false` | | yes (web) | Shows names of nearby rare drops that pass the loot filter. Labels remain clickable. |
| `classicTargetHealthBar` | bool, `false` | | yes (Target HP) | `false`: the target frame always shows the HP bar with level and life percentage. `true`: the bar, without numbers, appears only after you attack a monster at full life. Either way the frame shows a live 3D portrait of the target's head, its name and, for monsters, the race icon. |
| `classicNamePlates` | bool, `false` | | yes (Name plates) | `false`: the name boxes over monsters and NPCs (rounded, in the speech bubble's colours) hold smaller text, a monster's life bar shows once it was clicked or attacked, and NPCs get no hover outline. `true`: the same box, the HUD text size, the bar while the cursor is on the monster, and a green NPC outline. |
| `levelGuide` | bool, `true` | | yes (the guide window's On/Off) | On reaching a level the game guide has a page for, a blinking notice offers its summary. |
| `realisticSky` | bool, `true` | | yes (Sky) | The physical sky (sun, moon, stars, aurora, rainbow, clouds) instead of the original's painted domes ([rendering.md](rendering.md#realistic-sky)). |
| `cloudQuality` | `Off`, `Low`, `Medium` (default), `High` | others: the default | yes (Clouds) | The realistic sky's volumetric clouds and their shadows: off, or their hemisphere rendered at 768 x 192, 1024 x 256 or 1536 x 384 (High costs about 2.7 ms a frame on a GTX 1050 Ti at 1920 x 1080); presets Low and Medium take `Low`, High `Medium`, Ultra `High`. |
| `textureQuality` | `Auto` (default), `Original`, `Light`, `VeryLight` | defined enum | web ("Texturas") | Browser only: the scene textures shrink to half (`Light`) or a quarter (`VeryLight`) of each side as they decode, never below 32 pixels, averaging colour by alpha (`TextureDownscale`); interface art keeps its size. `Auto` is resolved by the page from the device's memory; the choice applies on the next load. |
| `confirmClose`, `grayscaleOnDeath`, `lowHealthWarning`, `notifyOnDeath` | bool, `true`, `true`, `true`, `false` | | web | Browser page only (`settings-effects.js`): the prompt before leaving while in the world, the grey canvas while dead, a red border pulsing under 30 % of life, a system notification of the death while the tab is hidden. |
| `hideOtherPlayers` | bool, `false` | | web | Players outside the party are not drawn (`PristonRenderer.SetActorFiltered`, apart from the gameplay's own hiding), nor their skill effects and costume wings; they have no name box, speech bubble or shop sign (`GameScene.Overhead` returns none) and cannot be clicked. |
| `hidePlayerNames`, `hideNpcNames`, `hideMonsterNames` | bool, `false` | | web ("Nomes") | That kind's name boxes show only under the cursor or for the clicked target (`NamePlateFilter`); party members always show. |
| `showOwnName`, `ownNameColor` | bool, `false`; `White` (default), `Gold`, `Green`, `Blue`, `Pink`, `Orange` | defined enum | web | The local player's name box over its character, in that colour, without a life bar. |
| `persistentCharacterInfo` | bool, `false` | | web; Backspace in game | Every player's name box shows, not only the hovered one's. The Backspace shortcut flips the saved value. |
| `damageNumberSize` | `Small`, `Normal` (default), `Large`, `Huge` | defined enum | web | Scales the floating numbers by 0.75, 1, 1.35 or 1.75 (`FloatingCombatText.ScaleOf`). |
| `showOtherPlayersDamage` | bool, `true` | | web | Hits in view between other entities float their numbers too, smaller and paler (`FloatingTextKind.OtherDamage`). |
| `spreadDamageNumbers` | bool, `false` | | web | Numbers land up to 40 units apart on both axes instead of a narrow column. |
| `showElementIcon`, `colorDamageByElement` | bool, `true`, `false` | | web ("Ícone do elemento", "Colorir número pelo elemento") | A hit with an element (its combat event's `Element`, [protocol.md](protocol.md#owner-inventory-and-combat-layouts)) floats the element's symbol right of its number, cut from the status window's resistance boxes and tinted with the element's colour (fire, ice, lightning, poison; earth, water and wind have no box there); the second paints the number in that colour (`DamageElementStyle`). Physical hits keep their look. |
| `hidePotionHeals` | bool, `false` | | web | A life potion floats the life it restored over the local player: the life gained over the life before the update that carried the potion, within 1.5 s (`PotionHealWatch`); the server reports only that a potion took effect. |
| `hideSkillEffects` | bool, `false` | | web ("Forces, habilidades e emotes", inverted) | Other players' skill effects are not drawn unless aimed at the local player (`OtherPlayerEffects.HiddenBySwitch`). |
| `hideStealthEffect` | bool, `false` | | web | A stealthed (Vanish, Vague) character keeps its full opacity (`ActorLookBook.HideStealth`). |
| `monsterDissolve` | bool, `true` | | web ("Efeito de dissolver") | Every corpse fades out once it has dimmed fully, at the rate bodies of light fade at once (`MonsterPresentation.CorpseFade`); off, ordinary corpses stay. |
| `chatFilter` | bool, `false` | | web ("Censura de chat") | Swear words, matched whole and ignoring case and accents, in what players write show as asterisks; the speaker's name before ": " is kept (`ChatCensor.MaskMessage`). |
| `chatTranslateTo` | string, `""` | `""`, `game` or a supported language | yes (Other, "Translate chat to"; web: Outros, Idioma, "Traduzir chat para"), only while the server translates | The language the server translates other players' lines to: none, the game's language (`ClientSettings.ChatTranslationTarget`) or a chosen one. The client sends it on entering the world and when it changes (`GameScene.UpdateChatTranslation`); each translated line follows its original, marked "(translated)". |
| `streamerMode`, `streamerHidesWholeChat` | bool, `false`, `false` | | web | Streamer mode hides whispers, or every written line (whisper, party, clan, trade, speech and its bubbles); notices stay (`ChatLineFilter`). |
| `hideBossNotices` | bool, `false` | | web | Hides the boss time notice (`GameTextId.BossTime`). The other notice switches (`hideTradeChat` ... `hideCastleNotices`) are applied by the same rule. |
| `hideAgingNotices`, `hideLevelUpNotices` | bool, `false`, `false` | | web ("Aging de outros jogadores", "Level up de outros jogadores", inverted) | Hide the server's notices of another player's high aging (`GameTextId.AgingNotice`) and milestone level (`LevelNotice`); the server sends them only from the limits of its settings table ([application.md](application.md#server-notices)). |
| `hideDuelNotices` | bool, `false` | | web ("Duelos de outros jogadores", inverted) | Hide other players' duel results in the chat (`GameTextId.DuelNotice`, `DuelDrawNotice`), which the server sends to the field's other players ([application.md](application.md#duels)); a duellist's own result always shows. |
| `hideArenaNotices` | bool, `false` | | web ("Arenas: abertura e resultados", inverted) | Hide the arena's opening and other players' match results in the chat (`GameTextId.ArenaOpen`, `ArenaNotice`, `ArenaDrawNotice`), which the server sends to every player ([application.md](application.md#arena)); a player's own lines always show. |
| `hideRoyaleNotices` | bool, `false` | | web ("Battle Royale", inverted) | Hide the Battle Royale's registration, start, cancellation and winner in the chat (`GameTextId.RoyaleOpen`, `RoyaleStarted`, `RoyaleCancelled`, `RoyaleWinner`), which the server sends to every player ([application.md](application.md#battle-royale)); a player's own lines always show. |
| `hideTitleNotices` | bool, `false` | | web ("Títulos ganhos e perdidos", inverted) | Hide the titles other players earned or lost in the chat (`GameTextId.TitleGained`, `TitleLost`), which the server sends to every player for legendary and position titles ([application.md](application.md#titles)); a title the player gets or loses is still told to it (`TitleEarned`, `TitleTaken`). |
| `hideBossRouletteNotices` | bool, `false` | | web ("Vencedores da Roleta de boss", inverted) | Hide the boss roulette's winners in the chat (`GameTextId.BossRouletteWinner`), which the server sends to every player ([application.md](application.md#boss-roulette)); the winner's own prize line (`BossRouletteWon`) always shows. |
| `partyLootAlerts` | `All`, `Rare` (default), `Off` | defined enum | web ("Alertas de loot do grupo") | Which monster drops a party member picked up the party chat tells: every item (`PartyLoot` and `PartyRareLoot`), only unique and set items (`PartyRareLoot`), or none (`ChatLineFilter`). |
| `bellatraOpeningNotice` | `Both` (default), `Chat`, `Off` | defined enum | web ("Aviso de abertura do Bellatra") | The notice that Bellatra's tickets are on sale (`BellatraOpening`): in the chat and the notice strip at the top of the screen (`ChatLineFilter.OnScreen`), in the chat only, or hidden. |
| `prioritizePlayers` | bool, `false` | | web | A player under the cursor wins over a nearer monster when clicking or hovering (`RemoteEntityManager.TryPick`); NPCs keep the nearest-wins rule. |
| `runByDefault` | bool, `true` | | web | Each world entry starts running; R switches to walking until the next entry. |
| `centeredInterface` | bool, `true` | | web | Browser only: the bottom bar centred under a wide screen (`HudLayout.CenteredBar`); off, at the left. |
| `classicInterface` | bool, `false` | | web (Vídeo, Interface, "Interface clássica") | Browser only: the HUD drawn as on the desktop, with the original art, instead of the browser's own look (`HudLayout.BrowserLook` clears `HudLayout.Browser`): the chat window, the party and quest windows, the system menu and the action bar's place. The page's corner shortcuts keep `hideHudIcons`. The desktop draws the classic HUD always and has no row for it. |
| `moveChat`, `chatOffsetX`, `chatOffsetY` | bool, `false`; int, `0`, `0` | offsets -16384 to 16384 (clamped) | yes (Video, "Move chat"; web: Vídeo, Interface, "Mover chat") | The chat window, with its tabs, buttons and input line, sits `chatOffsetX`/`chatOffsetY` HUD design pixels from its own place (`HudChatWindow.Offset`), held on the screen in both of its places (`HudChatWindow.ClampOffset`). With `moveChat` on, a press on the open window's log (not on a tab, a button or a whisper line) drags it, and the place is saved when the button is let go (`GameplayHud.UpdateChatPlace`); off, the window stays where it was left. "Reset interface" puts it back. |
| `tutorialTips`, `tutorialTipsClosed` | bool, `true`; string array, `[]` | | yes (Other, "Tutorial tips"; web: Outros, Missão guiada, "Dicas do tutorial") | While a character is at most level 20, the first tip of `HudTutorialTips.Tips` it has not closed shows in a guide box at the top of the screen: moving, attacking, inventory, character, skills, potions, chat and settings, the shortcut keys as the player bound them. "Got it" adds `character/tip` to `tutorialTipsClosed` (the newest 512 are kept) and the next tip follows; "Turn off tips" clears `tutorialTips`. The box waits under dialogs, the settings, the restart window and the level guide's summary. |
| `freeHuntProfiles`, `freeHuntRecords` | string maps, `{}` | | web (the touch HUD's Auto-caça panel) | Browser only: the free auto-hunt's options as transfer codes (`ETERVALE-HUNT-1:`, `FreeHuntProfile`), by `character:<name>` or, when a character shares them with its account, `account:<SHA-256 of server and account>` (`FreeHuntProfileStore`); and each character's hunt totals and latest 300 log entries as JSON (`HuntRecord`), by character name, saved at most every 30 s. An unreadable entry is ignored. ([web-client.md](web-client.md)) |
| `hideHudIcons` | bool, `false` | | web | Browser page only: hides the corner shortcuts (News, Auto-hunt, More). |
| `showPartyDps` | bool, `true` | | web | Beside the target frame, the damage per second the local player and its party dealt to the target over the last 5 s, at least over a second (`PartyDpsMeter`). |
| `minimapGuide` | bool, `true` | | web | A gold marker on the minimap at the Adventurer's Journey destination in the current field, held to the box's edge while beyond the view (`HudMinimap.GuideOffset`). |
| `weatherEnabled` | bool, `true` | | yes (Weather) | The weather the server sends: clouds, rain, splashes, lightning, the rain and thunder sounds ([client.md](client.md#weather)). `false` keeps the sky clear and the field ambience on (the ice fields' snow included), as the original's rain switch did. |

### Rendering

| Field | Type, default | Valid values (else default) | Panel | Effect |
|---|---|---|---|---|
| `renderScale` | float, `1` | 0.5-1 | web | Saved cap for the internal 3D scene resolution; the exact scale when dynamic resolution is disabled. The final output, HUD and input keep the display's resolution. |
| `dynamicResolution` | bool, `true` | | web | Allows the browser to adapt the transient 3D resolution below `renderScale`. The effective scale is not saved and the chosen preset's effects remain selected. Quality presets preserve this independent choice. Desktop stores the field but does not adapt resolution. |
| `maxPixelRatio` | float, `2` | 1, 1.5, 2 | web ("Resolução máxima") | Browser only: the most drawing pixels per CSS pixel. The canvas uses the lower of this and the display's `devicePixelRatio`, so on a display at 100 % scaling every value draws the same; on a dense display (Windows scaling above 100 %, retina) a lower value draws fewer pixels. Presets: Low 1, Medium 1.5, High and Ultra 2. The desktop renderer ignores it. |
| `anisotropicFilter` | int, `16` | 1, 2, 4, 8, 16 | web ("Filtro anisotrópico") | Browser only: texture samples along slanted surfaces such as the ground seen at a distance; 1 turns it off. Presets: Low 4, Medium 8, High and Ultra 16; the panel's "Muito baixo" uses 2. Capped by the GPU's limit. The desktop renderer always uses 16. |
| `quality` | `High` (default), `Low`, `Medium`, `Ultra`, `Custom` | defined enum | yes (Graphics quality) | Label of the preset the fields came from. The panel applies a preset with `WithQuality`; editing any preset field in the panel sets `Custom`. Loading a file never applies a preset. |
| `antiAliasing` | `Taa` (default), `Off`, `Fxaa`, `Smaa`, `Msaa` | defined enum | yes | `Taa`: jittered projection, motion vectors (skinned actors from their previous pose), history resolve. `Msaa`: multisampled HDR scene, no depth pre-pass. `Fxaa`/`Smaa`: 1x filter on the 8-bit display image. |
| `multiSampleCount` | int, `4` | 0, 2, 4, 8 | label only | Samples of the HDR scene with `antiAliasing` `Msaa` only; the renderer clamps to 2-4 (`PristonRenderer.ApplyQuality`). Also a Display field: a change goes through confirmation. |
| `autoExposure` | bool, `true` | | | GPU histogram and adaptation; `false` holds the light model's incident reading. |
| `exposureCompensation` | float stops, `0` | finite, -3 to 3 | | Added to the automatic or fixed exposure. |
| `tonemapper` | `PbrNeutral` (default), `AgX`, `Aces` | defined enum | | Display transform of the final pass. |
| `bloom` | `High` (default), `Low`, `Off` | defined enum | | Bloom pyramid mixed at 5 % (High) or 3 % (Low). |
| `colorGrading` | bool, `true` | | | Applies the 3D LUT grade after the display transform. In the browser, under the default light: a warm, golden grade with a little more saturation and contrast ("Cores (LUT)"). |
| `toneMapping` | bool, `true` | | | Browser only, default light: the `tonemapper` curve; `false` sends the light to the display as it is. |
| `vignette` | bool, `true` | | | Browser only, default light: darker corners. |
| `filmGrain` | bool, `false` | | | Browser only, default light: a faint moving grain. |
| `bloomIntensityPercent` | int, `100` | 0-200 | | Browser only: the bloom's strength. |
| `colorGradingLut` | string, `""` | `null` becomes `""` | | Path of a `.cube` file (any size, display-referred sRGB input) replacing the built-in grade. Relative paths resolve against the process working directory; an unreadable or malformed file logs a warning and keeps the built-in grade. |
| `sharpening` | float, `0.6` | finite, 0-1 | | Contrast-adaptive sharpening in the final pass: 0 turns it off; above 0 the peak weight runs from -1/8 to -1/5 (FidelityFX CAS's range). With `Taa`, 0.4 is added (capped at 1) to restore the detail the temporal average softens. |
| `localLightLimit` | int, `32` | 8, 16, 32 | | Local lights shaded per pixel: transient combat lights first (at most 8), then the stage lamps nearest the camera focus. |
| `shadowsEnabled` | bool, `true` | | yes (Shadows) | All shadows: sun cascades and lamp cube shadows. |
| `actorShadowsOnly` | bool, `false` | | web ("Sombras: Simples") | Browser only: the scenery casts no sun shadow, characters still do (`stageShadows` in `three-scene.js`). The browser draws one sun shadow map, so its other shadow choices are the map size: High 4096, Medium 2048, Low 1024. Presets set it to `false`. |
| `fogDistance` | int, `2208` | 1000-4000 | web ("Distância da névoa") | Where the distance fog starts, in world units (`PristonDistanceFog.For(mode, start)`); the default is the original's clip start. |
| `shadowMapSize` | int, `4096` | 1024, 2048, 4096, 8192 | yes (Sun shadow) | Size of the sun's cascade atlas (a 32-bit float depth surface). If allocation fails the renderer halves the size down to 1024. |
| `shadowCascades` | int, `4` | 1-4 | | Stabilized cascades over 9000 units of view depth; from 2 cascades on the atlas is a 2x2 grid. |
| `shadowFilter` | `Pcss` (default), `Pcf` | defined enum | | PCSS widens the penumbra with the blocker distance; the moon always uses PCF. |
| `shadowUpdateRate` | int Hz, `60` | 0, 30, 60, 120 | yes (Shadow animation: 30, 60, 120, every frame) | Sun cascade refit and refresh rate; 0 every frame. Far cascades refresh less often; at night the rate is halved (at least 30 Hz). The two near cascades redraw every frame regardless, so characters cast from their current pose. |
| `lampShadowCount` | int, `4` | 0, 2, 4 | | Stage lamps nearest the focus whose cube shadows actors and animated props cast, while the lamps are lit (sun below about 4 degrees). |
| `lampShadowTileSize` | int, `512` | 256, 512, 1024 | | Cube-face size of each lamp shadow. |
| `ambientOcclusion` | bool, `true` | | | Half-resolution GTAO from the depth pre-pass (not with MSAA). |
| `contactShadows` | bool, `true` | | | Screen-space contact shadows toward the sun (by day only, not by moonlight; not with MSAA). |
| `imageBasedLighting` | bool, `true` | | | Sky-dome irradiance (spherical harmonics) and prefiltered reflections, refreshed every 250 ms; `false`: hemispherical sky/ground ambient. |
| `classicLighting` | bool, `false` (browser: `true`) | | | Browser only, the settings panel's "Iluminação": the original look, the texture times the stage's baked vertex light under an even ambient, a soft key, dimmer nights, quieter lamps and no tonemapper, grade, vignette, grain or bloom, at the saved render scale (the adaptive budget only slows shadows). `false`, "Padrão (novo)": the same even base with a strong key in the sun's colour, finished by the "Gráficos aprimorados" settings. A browser document saved without the key starts classic. The desktop renderer ignores it. |
| `screenSpaceReflections` | bool, `false` | | | Screen-space reflections of smooth surfaces (water, ice) over the sky reflection; needs the depth pre-pass; skipped when no smooth surface is visible. |
| `sceneryOnlyReflections` | bool, `false` | | web ("Reflexo na água: Só cenário") | Browser only: with `screenSpaceReflections`, the reflections trace a colour target that characters, monsters and their outlines write black into, so the water mirrors the scenery and not the actors. The desktop renderer ignores it. |
| `reflectionsEveryOtherFrame` | bool, `true` | | web ("Reflexo a cada 2 quadros") | Browser only: the screen-space reflections are traced on alternate frames and kept in between, halving their cost; a turning camera sees them one frame late. |
| `characterQuality` | `VeryHigh`, `High`, `Medium`, `Low`; `High` | | web ("Qualidade dos personagens") | How often a crowd's characters sample their pose (`CrowdAnimationLod`): Very high every frame while on screen; High every frame within 400 units of the camera's focus, every second to 900, every fourth beyond; Medium the same intervals from 250 and 600 units; Low from 250 and 600 units at every fourth and eighth frame. Off screen: every eighth frame (sixteenth on Low). The portrait's actor samples every frame. |
| `outlineGlow` | bool, `true` | | web ("Brilho de contorno") | The hover outline around the character, monster or NPC under the cursor (`GameScene.UpdateOutline`). |
| `agingMixGlow` | bool, `true` | | web ("Brilho de aging e mix") | The coloured glow and mix overlay of held weapons and shields with aging, mix or a unique glow (`GameScene.Hold`); class-up orbits stay. |
| `limitOtherPlayerEffects` | bool, `true` | | web ("Limitar efeitos de outros jogadores") | Other players' skill effects farther than 1200 units from the camera are not drawn, and at most 48 of theirs nearer it are live (`OtherPlayerEffects`). The local player's, its target's and monsters' effects, and effects aimed at the local player or its target, always show. |
| `volumetricLight` | bool, `true` | | | Sunlight scattered by a thin height fog, with shadow shafts, at quarter resolution up to the distance-fog start; by day only, needs the depth pre-pass and sun shadows. |
| `distanceFog` | `Atmospheric` (default), `Classic`, `Off`; older `true`/`false` | defined enum | | `Atmospheric`: from 2208 units the surface fades toward the captured sky, at most 85 %, with forward scattering toward the sun. `Classic` is accepted for old files and draws exactly as `Atmospheric`. `true` reads as `Atmospheric`, `false` as `Off`; saving writes the name. |
| `calibrationSpheres` | bool, `false` | | | Look development: an 18 % grey and a chrome sphere beside the camera focus. |
| `debugView` | `Shaded` (default), `Albedo`, `VertexColour`, `Normals`, `Lighting`, `Overdraw`, `ExposureFalseColour`, `Roughness`, `Metallic`, `AmbientOcclusion`, `Specular` | defined enum | | Development views. `AmbientOcclusion`: occlusion in red, contact shadows in green. `Specular`: specular light alone. `ExposureFalseColour`: EV100 false colour in the final pass. `Overdraw`: additive, no sky, pre-pass or TAA. |

`Validate` checks enums with `Enum.IsDefined`, numbers against the lists above and floats with `float.IsFinite`. The
booleans have no validation. An unknown enum name is a JSON error (whole file ignored), not a per-field fallback.

Client interface text is stored in `Client/PristonTale.Gameplay/Localization/ClientText.resx` (English neutral resources)
and culture-specific satellite resources such as `ClientText.pt-BR.resx`. To add a language, register its canonical BCP 47
code in `ClientSettings.SupportedLanguages` and `CatalogLanguages.Supported` (with a migration adding its `languages` row,
as `M046CatalogSpanish`), add `ClientText.<code>.resx`, `DataText.<code>.resx`, `Texts/<code>/` (the same files as
`Texts/pt-BR`), the importer's `Translations/<code>.json`, a `Language.<code>` phrase and the page's catalog
(`languages/<code>.js`, [web-client.md](web-client.md)). The tests below hold every language other than English to the
same completeness as Brazilian Portuguese. The UI font draws Latin-1, Latin Extended-A and the Vietnamese letters
(`tools/ui/build_font.py`, checked by `FontCoverageTests`); a script it lacks (Chinese, Japanese) is drawn with the language's own
font (`UiFonts`: `Fonts/UI-<code>`, which every text drawer reads as `UiFonts.Current`, the shown language's), and the
Latin-script languages name such a language in Latin letters (`Language.zh-CN` is "Chinês (simplificado)" and `Language.ja` "Japonês" in Portuguese).
Chinese and Japanese wrap between characters (`HudTextLayout.Pieces`) and settings rows split at a full-width colon too.
Lines the server or the simulation has for the player travel as a `GameText` (a `GameTextId` and its arguments, see
[protocol](protocol.md#conventions)); the client words each by its `Game.<id>` phrase. `LocalizationTests` checks that
both files hold the same phrases with the same placeholders, and that every phrase the client asks for or builds from a
value (`GameTextId` included) exists.
The game data's texts (skill names and descriptions, class titles, quest window titles, castle item effects) are the data itself in English; `DataText.<language>.resx`
translates them by key (`Skill.<code>.Name`, `Skill.<code>.Description`, `Class.<PlayerClass>.<tier>`, `Quest.<title>`, `Castle.<key>.Effect`), and a text a
language lacks shows as the data words it. `LocalizationTests` checks that every skill, class title and quest title has its translation
and that every translation names game data.
The data's text files (help, level guide, helper pet, quest dialogs and quest window descriptions) are read from a translation
beside the executable when the shown language has one: `Texts/<language>/<the file's path under the assets>`, in UTF-8
(`HudTextFiles`; the repository keeps them in `Client/PristonTale/Texts`). A file without a translation reads the
asset. English has translations too, `Texts/en`, for the quest dialogs the assets hold in Portuguese (the level 130 to
172 chains and some events). `HelpGuideTests` checks that the Brazilian Portuguese texts translate the help, the level
guide's subjects, the helper pet, the quest dialogs and every quest window description, and translate nothing else, and
that the English texts are only translated quest dialogs.

## In-game Settings panel

`HudSettingsPanel` rows, in order: Window, Monitor, Resolution, Frequency, VSync, BGM, Audio, Maximum FPS, Background
FPS, Anti-aliasing, Sun shadow, Graphics quality, Shadow animation, Shadows, Target HP, Name plates, Weather, Sky,
Clouds, Language. Click cycles
forward, Shift+click backward. Resolution offers only the sizes the primary monitor reports (at least 800x600,
`HudSettingsPanel.Resolutions`): any of them in fullscreen, those that fit on the desktop below the window's title bar
when windowed, and none when borderless (it covers the desktop). Graphics quality cycles Low, Medium, High, Ultra;
from `Custom` it goes to Low (forward) or Ultra (backward). Anti-aliasing, Sun shadow, Shadow animation and Shadows set
`quality` to `Custom`. Rows are inert while a display change awaits confirmation.

A row whose label ends in on or off is drawn as a switch, any other "Name: value" row as the name with the value in a
drop-down box, and a row with no value as a button. Every row still reacts to a click anywhere on it.

## Apply and confirm

`PresentationController.UpdateBoundary` runs once per frame on the main thread:

1. If `Requested` differs from the applied settings and no confirmation is pending, it validates them.
2. Display fields changed (`HasSameDisplay` false): `DisplayController.Apply` resizes or switches the window, then a
   15-second confirmation starts. Enter or the confirm button accepts; Escape, the revert button or the timeout
   restores the last accepted settings.
3. Otherwise the change is accepted immediately and saved.
4. Rendering fields go to `PresentationSettings.ApplyRendering`, which fires `RenderingChanged` only when
   `HasSameRendering` is false; `PristonRenderer.ApplyQuality` then reallocates the shadow atlas if its size changed,
   reconfigures cascades and lamp shadows and copies the post options.
5. A failing apply (`InvalidOperationException`, `ArgumentException`, `NoSuitableGraphicsDeviceException`) rolls back to
   the accepted settings (defaults on the first apply) and shows the error.

The saved display applies at start without confirmation: it was confirmed when it was chosen. A first apply that fails
falls back to the default windowed settings.

## Quality presets

`WithQuality` sets only the rendering fields below; display, audio, gameplay, `multiSampleCount`, `imageBasedLighting`,
`calibrationSpheres`, `distanceFog`, `debugView`, `exposureCompensation`, `tonemapper` and `colorGradingLut` keep their
values. The defaults equal the High preset (checked by `PhotorealTests`).

| Field | Low | Medium | High (default) | Ultra |
|---|---|---|---|---|
| `antiAliasing` | `Off` | `Smaa` | `Taa` | `Taa` |
| `sharpening` | 0 | 0.4 | 0.6 | 0.6 |
| `shadowMapSize` | 2048 | 2048 | 4096 | 4096 |
| `shadowCascades`, `shadowFilter` | 2, `Pcf` | 3, `Pcf` | 4, `Pcss` | 4, `Pcss` |
| `shadowUpdateRate` | 30 | 60 | 60 | 0 (every frame) |
| `ambientOcclusion`, `contactShadows` | off, off | on, off | on, on | on, on |
| `lampShadowCount` x `lampShadowTileSize` | 0 x 512 | 2 x 512 | 4 x 512 | 4 x 1024 |
| `bloom` | `Off` | `High` | `High` | `High` |
| `localLightLimit` | 8 | 16 | 32 | 32 |
| `volumetricLight` | off | off | on | on |
| `screenSpaceReflections` | off | off | off | on |
| `characterQuality` | `Low` | `Medium` | `High` | `VeryHigh` |
| `shadowsEnabled`, `autoExposure`, `colorGrading` | on | on | on | on |

Reference GPU frame totals, measured with `--benchmark-render` at 1920x1080 on a GTX 1050 Ti in Ricarten at noon and in
the lamp-lit street at night, cascades refreshed at 60 Hz: Low about 2.6 ms (day) and 3.4 ms (night; FXAA would add
about 0.7 ms), Medium about 3.6 ms and 4.8 ms, High about 5.7 ms and 5.7 ms; Ultra costs High plus the every-frame
cascades and the reflections. At 60 frames per second or less every frame refreshes the cascades (about 1 ms more on
High). Re-measure after renderer changes; see [testing.md](testing.md) for the benchmark arguments.

## Debug keys (not saved)

Anywhere, `F9` shows or hides the diagnostics at the top left, as the original's debug display
(`GameClient.Diagnostics`: the frame rate and timing and the screen's status); the window title stays "Etervale". In the
gameplay screen (`GameplayScreen.UpdateDebugKeys`): `Ctrl+F3` adds the render statistics to those diagnostics, `Ctrl+F5`
toggles the renderer's shadows, `Ctrl+F10` cycles `debugView`, `,` and `.` move the time of day by one hour, `P` pauses
or resumes the day cycle. These change the live renderer only; the file keeps its values.

## How to

- **Add a setting:** add an `init` property with its default to `ClientSettings` (a comment only if its meaning is not
  obvious); add its range to `Validate`; if the renderer reads it, add it to `HasSameRendering` and read it in
  `PristonRenderer.ApplyQuality` (or `PristonPostSettings.From` for post options); if it changes the swap chain, add it
  to `HasSameDisplay` and `DisplayController.Apply`; decide whether `WithQuality` sets it per preset; add a panel row in
  `HudSettingsPanel` (`_labels` length, `Click`, `RefreshLabels`, and the box height `SizeH` if needed) and, for the
  browser, a row in `Client/PristonTale.Browser/wwwroot/settings-options.js` (`SettingsOptionsTests` checks that every
  row's field exists); extend
  `ClientSettingsTests` (validation, round trip) and `PhotorealTests` for presets; update this document. Existing files
  without the field load with the default, so no version bump is needed.
- **Rename or remove a field:** existing files with the old name then fail to load (unknown property) and the user gets
  defaults. Keep accepting the old shape with a converter (as `DistanceFogModeJsonConverter` does for booleans) or bump
  `ClientSettings.CurrentVersion` with a migration in `ClientSettingsStore.Load`.
- **Change a preset:** edit `ClientSettings.WithQuality`, keep the defaults equal to High (or update `PhotorealTests`),
  re-measure with `--benchmark-render` and update the preset table here.

## Pitfalls

- One unknown or misspelled property makes the whole file unusable for that session (defaults, read-only). Field names
  are case-sensitive camelCase.
- Editing `quality` in the file has no effect on the other fields; presets apply only through the panel.
- `multiSampleCount` is ignored unless `antiAliasing` is `Msaa`, and 8 runs as 4; yet changing it triggers the display
  confirmation because it belongs to `HasSameDisplay`.
- MSAA disables the depth pre-pass, so ambient occlusion, contact shadows, screen-space reflections, volumetric light,
  soft particles and light-tile depth bounds are all off with `Msaa`.
- `Classic` distance fog is not a separate look: `PristonDistanceFog.For` maps it to `Atmospheric`.
- A changed `colorGradingLut` is read and sampled on the thread pool (`PristonPostProcessor.EnsureLut`); the previous
  grade stays on screen until the new LUT is uploaded, a frame or more later.
- `ClientSettings` lives in Engine3D but holds gameplay (`classicTargetHealthBar`) and audio fields; keep the record
  free of Gameplay types so Engine3D does not depend on Gameplay.

## Related

- [rendering.md](rendering.md): what each rendering option switches in the frame graph.
- [shaders.md](shaders.md): the passes behind the options.
- [client.md](client.md): screens, HUD and the system menu.
- [testing.md](testing.md): `--benchmark-render`, `--validate-display` and the settings tests.
