# Android client

The Android prototype in `Client/PristonTale.Android`: a login form, the character selection,  the
world entry in Ricarten and Pillai. It shares the desktop client's network library (`Client/PristonTale.Client.Net`) and the shared rules, but not its engine:
The desktop renders through MonoGame on Vulkan, the Android client draws with OpenGL ES 2 from data baked by `ptgltf`.
A desktop screen therefore reaches Android as a port, and this document is the method and the contract of those ports.

## Read this when

- You change the Android login, character selection or Ricarten scene.
- You bring another desktop screen to Android, or change a desktop screen that Android already has.
- You change what `ptgltf` bakes for Android, or the files the APK packages.
- You debug a difference between the desktop and Android versions of a screen.

## Code map

| Path | Responsibility |
|---|---|
| `Client/PristonTale.Android/MainActivity.cs` | The activity: login form, `ConnectAsync` (login, character selection, field session), NPC panels, pause and resume forwarding |
| `Client/PristonTale.Android/CharacterSelect/CharacterSelection.cs` | UI-thread host of the selection: the `GLSurfaceView`, touch to pointer, the hidden text field and keyboard for the name |
| `Client/PristonTale.Android/CharacterSelect/CharSelectScreen.cs` | Port of the desktop `CharacterSelectScreen`: steps, checks, server calls; the GL renderer callbacks; one `Update` and one `Draw` a frame |
| `Client/PristonTale.Android/CharacterSelect/CharSelectScene.cs` | Port of `CharacterSelectScene`: roster and class lineup on the stage, the chosen class's walk, picking, highlights, the face portrait |
| `Client/PristonTale.Android/CharacterSelect/CharSelectView.cs` | Port of `CharacterSelectView`: the 2D art and text, same places and order |
| `Client/PristonTale.Android/CharacterSelect/CharSelectCamera.cs` | Port of `CharacterSelectCamera`: the rail, the sway, projection of points to the viewport |
| `Client/PristonTale.Android/CharacterSelect/CharSelectFlames.cs` | The brazier flames (the desktop `vfx.charselect.beam` effect) |
| `Client/PristonTale.Android/CharacterSelect/CharSelectAudio.cs` | Port of `LoginAudio` for this screen: music, loops, cues |
| `Client/PristonTale.Android/CharacterSelect/CharSelectGraphics.cs` | Every GL call of the selection: stage, vertex-animated actors, billboards, sprites, text, textures |
| `Client/PristonTale.Android/CharacterSelect/CharSelectSceneData.cs`, `StageFloor.cs` | The selection's baked stage; floor height under a point of it |
| `Client/PristonTale.Android/CharacterSelect/CharSelectText.cs` | The desktop's Brazilian Portuguese texts by the desktop's keys |
| `Client/PristonTale.Android/Ricarten*.cs`, `MobileScene.cs`, `MovementJoystick.cs`, `NpcSpeechBubble.cs` | The Field scene: scene reader, renderer, view, camera, session, joystick, NPC speech |
| `Shared/PristonTale.Assets/CharacterSelectStage.cs` | Staging shared with the desktop: standing places, camera rail and sway, walk speeds, glow, flames, portrait framing |
| `Shared/PristonTale.Assets/CharacterSelectLayout.cs` | 2D layout shared with the desktop: every rectangle and button position for a viewport size |
| `Shared/PristonTale.Assets/CharacterSelectArt.cs`, `CharacterSelectSounds.cs` | Art and sound file names, class lineups, the list of files to package |
| `Shared/PristonTale.Assets/WorldLoadingArt.cs`, `MobileActorFormat.cs` | The loading pictures' directory; the baked files' magics, clip order and character file names |
| `tools/PristonTale.GltfExport/MobileCharacterSelectBake.cs`, `MobileCharacterSelectSceneBake.cs` | `ptgltf --mobile-char-select`: art to PNG, sounds, font, the 3D scene file |
| `tools/PristonTale.GltfExport/MobileSceneBake.cs`, `MobileActorWriter.cs` | `ptgltf --mobile-scene`: a field's scene file; `Model` bakes any exported glTF to vertex frames; the shared writers |
| `Client/PristonTale.Android/RicartenResources/` | What the APK packages: `ricarten.zip`, `ricarten.smd`, `ChatBox/`, `CharSelect/` (loose art, sounds, font), `charselect.zip` (selection stage, music, character files), `Loading/` (loading pictures) |

## Flow

`MainActivity.ConnectAsync` logs in (`GameServerConnection.LoginAsync`, language `pt-BR`), then
`SelectCharacterAsync` opens the selection and waits for it. The selection ends with a character that entered the world
(`CharSelectEntered`: the summary and the server's `EnterWorldOutcome`) or with nothing when the player pressed Exit,
which returns to the login form. The selected character's server field opens Ricarten or Pillai. A character in another field returns to the login form
with a message. New Morion characters start in Pillai.

## Porting a desktop screen

The selection is the model for any further screen. The rules that made it match the desktop:

1. **Same structure, same names.** One Android file per desktop file, the type named after it (`CharacterSelectScreen`
   to `CharSelectScreen`, and so on), members under the desktop's names, methods in the desktop's order. A change to the
   desktop screen is then made to the Android one by reading the two side by side.
2. **Same loop.** The desktop screen runs `Update` then `Draw` each frame and polls its server calls as tasks. The
   Android screen does exactly that on the GL thread (`GLSurfaceView.IRenderer.OnDrawFrame`). The UI thread only hands
   it input through small thread-safe setters (`SetPointer`, `Press`, `SetTypedName`) and receives requests through
   `ICharSelectHost`.
3. **One copy of every number.** A value both clients need lives in `Shared/PristonTale.Assets` (staging, layout, file
   names) and both read it. Before adding a constant to an Android file, move the desktop's there and point the desktop
   at it. What stays in the Android files is draw code and the values only Android has (near and far planes, texture
   limits).
4. **Same viewport.** The desktop lays 2D art out in window pixels around an 800 x 600 design. Android uses a virtual
   viewport `CharacterSelectLayout.DesignHeight` high and as wide as the surface's aspect makes it (1333 x 600 on a
   20:9 phone), and scales it to the surface. Every layout number is then the desktop's, and the screen looks like the
   desktop client in a window of that shape. Text is rasterised at surface resolution, so it stays sharp.
5. **Same render space.** The stage's own coordinates are (x, y, z); the desktop renders their mirror (x, y, -z). The
   baked files hold the stage's own coordinates, so the Android selection draws everything through
   `Matrix.CreateScale(1, 1, -1)` and from there uses the desktop's matrices unchanged: a slot (x, z) stands at
   (x, floor, -z), yaw 0 faces the camera, `Matrix.CreateLookAt` and `CreatePerspectiveFieldOfView` are the desktop's.
   (The Ricarten scene predates this and draws the stage's own coordinates directly.)
6. **Bake what the engine would compute.** Android has no skeletons, materials or effect system. `ptgltf` exports each
   model through the desktop's own loaders to glTF, samples it into vertex frames, and writes them with the textures.
   Motion selection is the desktop's (`PristonMotionSelection`), so the clips are the ones the desktop plays.

## Character selection

The steps, the camera, the standing places, the walk of the chosen class, the highlights, the art, the texts and the
sounds are the desktop's, described in [client.md](client.md) (`CharacterSelectScreen`). What is specific to Android:

### Touch

Touch stands for the mouse, with one rule: **the finger is the pointer wherever it rests or last rested, and lifting it
without a drag is the left press.** A drag is a move past the system touch slop.

| Desktop | Android |
|---|---|
| Move the mouse over a character, tribe mark, class or face (hover) | Rest the finger on it; the hover stays after the finger lifts |
| Left press | Lift the finger where it went down |
| Double press within `DoublePressSeconds` (0.35 s) | Two such taps on the same character |
| Type the name | The on-screen keyboard, opened when the face step starts; a tap on the name reopens it |

So a tribe's or a class's description shows while the finger rests on it, and the tap that follows chooses it. The name
field takes the desktop's characters (ASCII letters and digits, `.`, `-`, `_`) up to `CharacterNames.MaxLength`. The
keyboard covers the lower part of the scene instead of resizing it (`SoftInput.AdjustNothing`).

### Classes on demand

The selection reads a class's character file ("Baked files") off the GL thread the first time the screen needs the
class, and uploads one read class a frame (`CharSelectScreen.PrepareNextClass`). The roster and the lineup appear when
their classes are uploaded, as the desktop waits for its actor preparation. The GL objects belong to the surface's
context: `CharSelectGraphics` is rebuilt in `OnSurfaceCreated` and the classes upload again. The model of the
character that enters the world is handed to the world (`CharSelectEntered.Model`), which draws the same class and
face.

### Differences from the desktop

Known, and each a candidate for work:

| Desktop | Android | Why |
|---|---|---|
| A character wears its armour and its hair, in the selection and in the world | It wears its class's default body with its saved face | Only default bodies are baked; baking every armour is not affordable as vertex frames |
| The renderer's lighting, shadows and post-processing | Texture times the ambient colour (`CharacterSelectStage.Ambient`) plus the glow | No lit pipeline in OpenGL ES 2 here |
| Stage materials animate (scrolling and flip-book textures) | Stage textures are still | The bake takes one frame of the stage |
| Flames from the effect system | `CharSelectFlames`: the same numbers as a steady stream | No effect system |
| The face portrait's own lighting | The ambient colour | As above |
| The clan line shows the clan's mark | The clan's name only | The mark is generated by desktop UI code |

Kept on purpose: where two characters' pick boxes overlap, the farther one wins, as on the desktop (its depth buffer is
reversed and the pick keeps the smaller depth).

## Entering the world

After selection, the Android client freezes the last selection frame while it reads the field's collision mesh,
baked scene and arrival animation off the UI thread. The map replaces that frame as soon as it draws, without a
loading picture or a fade. NPC models are uploaded as they first appear, one a frame. An error reading the field
or drawing its first frame restores the login form.

The first server spawn for the local avatar triggers the desktop's arrival animation and its restart sound once
per entry session. The Android renderer draws the original animated mesh with additive blending and the authored
offset and fades. The dynamic light that accompanies the mesh on desktop requires a lit field renderer.

## Ricarten and Pillai

Entering either town opens an OpenGL ES scene with the desktop stage's meshes, vertex colours and textures. The player
is the class and face chosen in the selection (the class's character file), with the stand, walk and run a village
selects on the desktop; the walk cycle advances by distance. NPCs with a baked model are drawn as that model, every
monsters without a baked actor as markers. Remote players use the server's class, face, facing and movement
state to draw their class's default body and face. Equipment and combat motions are pending. The landscape view uses a steep follow camera with a narrow field of
view and frames the avatar below centre. The initial camera distance is 264 world units, with pinch zoom. The left thumbstick moves relative to the screen. The client steps movement
locally every 20 ms, reports position every 200 ms during continuous movement, and applies server corrections.

The project packages the original stage for collision (`ricarten.smd`) and the baked scene
(`Mobile/ricarten-scene.bin` inside `ricarten.zip`). To bake it again, export the stage (`village-2.smd`) and each NPC
model as a GLB with `ptgltf` (an NPC's file named by its template number), then run
`ptgltf --mobile-scene <stage.glb> <ricarten-scene.bin> [npc-glb-directory]`.

Pillai packages the desktop terrain and collision as `World/pillai.smd` and `World/pillai-scene.bin` under
`RicartenResources`. The bake uses the same field export command and the terrain referenced by `PillaiField`.
Pillai's NPC models come from the town's template IDs and model paths in the world catalog. Export their compiled
model descriptors as GLBs with `ptgltf --asset-root assets`, named by template ID. Export every object in
`PillaiField.Definition.StageObjectSmds` with `ptgltf --stage-object` to keep the desktop's loop timing. Pass the two
directories after the stage and output paths in `--mobile-scene`. Animated scenery uses the field's coordinates,
is uploaded with the terrain, and loops independently by clip length. The play channel remains open when the server sends a warp between packaged fields. The destination loads using the server position and the presence update that carried the warp. Movement reports identify the current field.

The two towns loop their desktop music at the desktop field volume. The source tracks mapped to
`BGM_CODE_TOWN2` and `BGM_CODE_FILAI` are encoded as Vorbis in `World/ricarten-music.ogg` and
`World/pillai-music.ogg`. The activity pauses and resumes the field track with the scene.

The arrival data and the lowercase WAV ship in the same resource directory. Normalize the WAV with
`ptgltf --mobile-wave <source.wav> <fieldstart.wav>`, which uses the desktop decoder to write a standard RIFF header
without changing the PCM. Android's media player requires the standard header. Audio preparation and playback failures
are logged under `Etervale.World` and leave the field playable. Export the effect's model as a GLB
with the default model animation settings. Run
`ptgltf --mobile-world-arrival <model.glb> <Progression.vfx.json> <world-arrival.bin>` with the desktop's authored
progression effect definitions. The bake takes the mesh playback and fade parameters from that definition.

## Walking portals

Pillai and Forever Fall 1 are connected by their authored walking warp gates. The Android client checks the
same gate radius, height, level and delay as the desktop. It sends the selected exit to the server and opens
the destination only on the server's warp confirmation. Wing stones and unbundled destinations are excluded.
The button below the minimap in each of these fields traces a route to the authored gate and enters it on arrival. The guide currently covers this direct pair. Forever Fall 1 packages its terrain, collision and forest track. Its separate scenery and monster models are pending.

## Quest arena

Field 32 uses the quest arena stage for geometry and collision and the desktop arena track in Vorbis. Its terrain
is packaged in `World/quest-arena.smd` and `World/quest-arena-scene.bin`. During export, use the shared `fw.bmp`
texture from the Bellatra stage when it is absent beside the arena stage. The client accepts server warps into
the arena and back to a packaged town. Arena opponents currently use the field's entity marker.

## NPC speech

A successful NPC interaction displays the server's original lines below an animated head-and-shoulders portrait.
The field renderer draws the NPC's baked stand clip into a small viewport over the map. NPCs without a baked model
still show the subtitle. The panel closes after five seconds or is replaced by the next interaction.

## Quest journal and combat input

The minimap uses the desktop field image and the stage's authored map rectangle. It stays north-up and centers
on the player's position with a heading arrow. Bake each map with
`ptgltf --mobile-minimap <stage.smd> <map.tga> <output-prefix>`. The PNG is the original art converted for Android,
and the binary file holds four little-endian float32 world coordinates (left, top, right, bottom).
The duel arena hides the minimap. Tap a point on the minimap to request an in-field route. The shared floor and wall
queries check the path off the UI thread. The avatar follows the route using normal movement reports, with yellow
markers at upcoming waypoints. The joystick, a server correction or an obstructed step cancels it. Auto-caça follows an active hunt with a positive server quest target code. The client selects a visible living
monster with the same code, follows a walkable route when out of range, and sends normal attack requests in range.
Auto-caça requires a server build that sends target codes in quest journals and monster descriptions.
The joystick and the stop button cancel the mode. Hunts without a single target code, searching beyond visible
monsters, cross-field route planning and minimap entity markers are pending.

On Android 11 and later the game hides the system bars in landscape, with swipe access to transient bars.
The field HUD places an animated portrait of the selected character, name, level, compact vital bars and recent chat at the upper left, the target at the top center, and the minimap
and movement modes at the upper right. Experience spans the bottom edge, with quest and exit controls at the lower left
and attack at the lower right. The inline chat composer supports local and party messages. Nearby player speech uses the
desktop chat frame and follows the speaker above their head. NPC dialogue shows an animated portrait, the name, and the spoken text in a side panel.

Nearby player and NPC names are projected above their models. Gold-framed icon controls distinguish combat, chat, travel and shortcuts. Potion slots show the stack count and retain item names for accessibility.
The field HUD displays the owner's life, mana, stamina and experience toward the next level. The quest button
shows the active hunt's target and count. A manual attack selects a target and shows its name and life while
it remains present and alive. All three panels read the normal server presence updates. The Chat button opens the current field session transcript and sends nearby or party messages through the same play channel as the desktop.

The field session consumes the server's quest journal updates. Missões opens a two-column panel with running and completed entries on the left and the objective details on the right,
using the original Windows-1252 board descriptions packaged from `image/party/party_DATA` under `QuestJournal`.
Paths in that directory are lowercase. Running entries show the server's current kill count and timer snapshot.

The bottom bar has the three server-owned potion belt boxes and a learned-skill shortcut. Tap the skill to cast
it, or hold it to change the selection. Enemy skills use the current monster target. The server validates every
cast and potion use, with potion use carrying the owner inventory revision.

Atacar selects the closest living, unsummoned monster in the current presence view and sends a single attack
through the existing play channel. The server checks the target and range. The journal is read-only at this stage.
Quest NPC interactions use the server's Talk, Proceed, Accept, ChangeClass and ChooseReward actions. The dialogs read
Windows-1252 documents from the desktop quest assets packaged as lowercase paths under QuestDialogs. The server
validates each follow-up by quest code, step and NPC. The server may also send a quest prompt directly after a duel. Android opens its original document and sends the response without an NPC ID. Combat presentation,
mounts and objective-wide navigation remain pending.

## Player appearance in the field

The Android field reads equipment and pet identifiers from player presence, including the local player.
The appearance catalog maps class and armour key to an animated body. The character's existing head and
face remain separate. Starter weapons and the starter shield use the desktop item meshes and sampled
skeleton attachments for stand, walk and run. The helper pets and Phoenix use their desktop idle and
flight ranges and follow the owner. Unpackaged equipment falls back to the class body or no attachment.
Weapon-specific motion selection, special dual attachments and pet combat effects still use the desktop
presentation only.

Rebuild the packaged appearance from the desktop assets with:

```sh
dotnet run --project tools/PristonTale.GltfExport -c Release -- --mobile-appearance assets Client/PristonTale.Android/RicartenResources/Appearance DA101 DA102 DA103 DA104 DA105 DA201 DA202 DA203
```

## Baked files

All little-endian, written by `tools/PristonTale.GltfExport` and read by `BakedReader`. Positions are the stage's own
coordinates (see "Same render space").

```text
Textures  int32 count, then per texture: int32 length, PNG or JPEG bytes
Stage     int32 batchCount, then per batch: int32 texture (-1 none), int32 blend, Floats
          8 floats a vertex (position, colour, uv), 3 vertices a triangle; blend: 0 opaque, 1 cut-out, 2 blended
Part      int32 batchCount, then per batch: int32 texture, Floats uv (2 a vertex)
          int32 clipCount, then per clip: float32 seconds, int32 frameCount,
          then per frame, per batch: Floats positions (3 a vertex)
          a clip the model lacks has 0 seconds and 0 frames
Floats    int32 count, count x float32

CharSelect/scene.bin      int32 MobileActorFormat.SelectSceneMagic, Textures, Stage
Characters/<class>.bin    int32 MobileActorFormat.CharacterMagic, Textures, Part body, int32 faceCount, faceCount x Part
<field>-scene.bin         int32 MobileActorFormat.FieldSceneMagic, Textures, Stage,
                          int32 npcCount, then per NPC: uint32 template, Part
                          with FieldSceneryMagic instead: the same data followed by
                          int32 sceneryCount, then one Part with one clip per scenery model
world-arrival.bin        int32 MobileActorFormat.WorldArrivalMagic, float32 offsetY,
                         float32 fadeIn, float32 fadeOut, Textures, Part (one animation clip)
```

- A part's clips are in the order of `MobileActorFormat`: stand, walk, run, and for a character the selection's
  relaxed stand (the motion at the first character motion record). An NPC has the first three and may lack walk or run.
- A character file is one class (`MobileActorFormat.CharacterFile`): its default body and `CharacterFace.FaceCount`
  heads. A head's clips are sampled at its body's frame counts and carry its body's length, so any face plays on the
  body with one clip time.
- Frames are 12 a second (2 to 60 a clip for a character, 2 to 24 for an NPC); the renderer blends between neighbours
  (`GlActorPart`).
- The selection's stage is `CharacterSelectStage.StagePath`, unlit: its lighting is in the vertex colours.
- `ptgltf --mobile-scene-upgrade <old.bin> <new.bin>` rewrites a field scene of the previous format, which repeated
  colour and texture coordinates in every frame and carried one fixed player model.

## Continuing on another machine

Everything the client and the tools need is in the repository except:

- the game data (`assets/`, the archive linked in README.md) and the SQLite database and TLS certificate under `data/`;
- the APK signing keystore. An APK installs over the one on the phone only when it is signed with the same key, so copy
  the keystore privately (never into the repository) and pass it to `Client/PristonTale.VulkanAndroid/package-release.ps1` (or `.sh`);
- the Vulkan runtime libraries under `Client/PristonTale.VulkanAndroid/obj/vulkan`, rebuilt by `build.ps1` (NDK
  28.2.13676358, CMake 3.22.1, Ninja 1.13.1, both `arm64-v8a` and `x86_64`), which also stages the world archives.

Then: build the runtime and archives with `build.ps1`, bump the version in the project file, run `package-release.ps1`
with the keystore, the SDK and JDK paths and the server address, and publish the server with
`dotnet publish Server/PristonTale.Server -c Release -o <folder>`.

## How to

### Build the APK

Needs the .NET SDK with the `android` workload, an Android SDK and a JDK. From the repository root:

```bash
dotnet build Client/PristonTale.Android -c Release -m:1 -warnaserror \
  -p:AndroidSdkDirectory=<android-sdk> -p:JavaSdkDirectory=<jdk>
```

The signed package is `org.etervale.android.prototype-Signed.apk` under
`Client/PristonTale.Android/bin/Release/net10.0-android/`. `-m:1` keeps the two runtime identifiers from linking at
once, which needs several gigabytes of free memory; stop the game server and other large programs if the linker runs
out of memory.

### Release a new APK

Every APK handed out carries the next version, so two builds are never confused:

1. In `Client/PristonTale.Android/PristonTale.Android.csproj`, add 1 to `ApplicationVersion` and set
   `ApplicationDisplayVersion` to `0.<that number>` (19 and `0.19`, then 20 and `0.20`). Android refuses to install a
   package over one with a higher `ApplicationVersion`.
2. Build the APK (above). The login form's title shows the version.
3. The file to hand out is `Etervale-<version>.apk` beside the build's output (the `NameReleasePackage` target
   copies the signed package under that name).
4. Check the package before handing it out: its version (`aapt2 dump badging <apk>`) and that the files a change
   added are inside it (`assets/...` in the archive listing).
5. Update this document for what the version changed.

### Rebuild the selection's packaged data
After a change to the stage, a class's default body, faces or motions, or the screen's art or sounds:

```bash
dotnet build tools/PristonTale.GltfExport -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.GltfExport -c Release --no-build -- --mobile-char-select assets \
  Client/PristonTale.Android/RicartenResources/CharSelect Client/PristonTale.Android/RicartenResources/charselect.zip
dotnet run --project tools/PristonTale.GltfExport -c Release --no-build -- --mobile-loading assets \
  Client/PristonTale.Android/RicartenResources/Loading
```

### Add art or a sound to the selection

1. Add its file name to `CharacterSelectArt` (and to `Files()`) or `CharacterSelectSounds`, and use the constant on
   the desktop.
2. Rebuild the packaged data. Art is read by its source name (`CharSelectGraphics.Art`), a sound by its path
   (`CharSelectAudio.Play`).

### Change the selection on both clients

1. Staging or layout: change `CharacterSelectStage` or `CharacterSelectLayout`; both clients follow.
2. Behaviour: change the desktop method, then the Android method of the same name.
3. Drawing: change `CharacterSelectView`, then `CharSelectView` at the same place.
4. Update [client.md](client.md) for behaviour and this document for what is Android's own.

### Check the scene without a device

Nothing in the repository runs the Android client off a device. The baked scene can still be checked: read
`scene.bin` with `CharSelectSceneData`, place the stage and the actors with the matrices of `CharSelectScene` and
`CharSelectCamera`, and rasterise them in a scratch program. The staging then shows as the desktop's: characters upright
on the floor, facing the camera, the stage unmirrored.

## Pitfalls

- A sound must be packaged uncompressed to be played from the APK (`AssetManager.OpenFd`). The packager stores `.wav`
  uncompressed and compresses `.WAV`; `CharacterSelectSounds.PackagedName` lower-cases the extension.
- All packaged art shares one directory: two source files of the same name in different directories would collide
  (covered by the regression case on `CharacterSelectArt.Files`).
- A new directory under `RicartenResources/` needs its own `AndroidAsset` line in the project file, or the build
  succeeds without its files. After a file is only renamed, the packager can keep the old package: delete
  `obj/Release/net10.0-android/android/bin/*.apk` and build again.
- `AndroidAsset` items with wildcards are evaluated before `ExtractRicartenResources` unzips anything, so a file inside
  an archive needs its own `AndroidAsset` line; loose files under `RicartenResources/` can use a wildcard.
- Textures that are not a power of two cannot repeat in OpenGL ES 2; the shader wraps them (`uFract`) and they get no
  mip-maps.
- The screen runs on the GL thread and Android views on the UI thread. Touch a view only through
  `Activity.RunOnUiThread`, and the screen's state only through its setters.
- The projection keeps depth in 0 to 1 (`System.Numerics`) while OpenGL clips -1 to 1: it works, with half the depth
  range. Keep the near plane far (20 units here) on the 16-bit depth buffer.
- `ClientText.pt-BR.resx` is linked into the Android assembly; a key missing from it shows as the key.

The Android world follows the server clock, using the shared day, night and evening glow hours to shade the sky and world geometry.
The stage SMD supplies lamp positions, radii and colours. The six nearest ground lights illuminate terrain, scenery and
actors at night. Server weather controls overcast lighting and rain density and wind, subject to the same field rain
eligibility as desktop. Rain streaks project from world coordinates, stop at the upper collision surface and produce
splashes. Collision traces hide rain behind scenery, shelter reduces rain volume, and storms add flashes and thunder. Remote player walk and run playback uses planar speed
between presence samples and the shared locomotion rates, with the desktop playback range.
The pace button switches between walking and running. The joystick sets direction at the selected pace, using the same
movement speed, stamina and weight rules as the desktop client.

## Related

- [client.md](client.md): the desktop screens the Android ones follow.
- [tools.md](tools.md): `ptgltf` and its Android modes.
- [protocol.md](protocol.md): login, character commands and world entry.

## Vulkan Android client

`Client/PristonTale.VulkanAndroid` hosts the desktop character selection and gameplay scene through
MonoGame Native on Android. It uses the existing Android HUD views over the Vulkan surface. Touch orbit and
movement feed the desktop camera and locomotion code, and the desktop presence stream supplies actors,
equipment, pets, weather, the server clock, and effects.

Vulkan Android uses a closer initial world orbit and a shorter selection-camera rail. The character selection and
creation action buttons use 1.8 times the desktop size, with hit targets scaled to match. Selection taps use the
normalized SDL touch position mapped to the rendered viewport. A character answers a tap in a pick box 2.5 times the
desktop's (`CharacterSelectScreen.PickScale`), reaching past its feet and head by the extra width; where these wider
boxes overlap, the character whose feet are nearest the tap's column wins. The character information card uses larger art and text. A roster tap selects the character and Confirm requests world entry. The enlarged card stays centered below the character.
The client prepares shared sky assets before selection and uses the server's spawn coordinates when loading the field. The world HUD uses an edge-anchored mobile layout and shows
the local player's live portrait beside the status meters, a chat icon, and the quest journal with an Auto-caça action. Tap a world target to interact; dragging or pinching controls the camera.
Quest NPCs and server-opened dialogs use the original desktop quest documents and the shared quest commands.
Vulkan Android uses the desktop overhead name plates and shows quest text, NPC speech and choices in a side panel while the world remains visible.
The panel draws the live 3D portrait for an NPC opened by touch. A stationary tap in the joystick region still picks the world.
The Vulkan world uses the desktop High preset with +0.45 stops of exposure compensation for the mobile display.
Auto-caça tracks a visible living monster with the active objective's quest code using the desktop attack trace.
Moving the joystick or finishing the objective stops the mode. The server must publish quest target codes. The lower dock opens the menu, quest journal and read-only bag. The action bar uses the three potion belt slots and a configurable skill shortcut. Attack selects the nearest visible monster. The minimap follows the player.
The Vulkan package is currently limited to Ricarten, so hunts in other fields require a larger asset package.

For a Windows build, use PowerShell 7 and install the .NET 10 Android workload, Android SDK and NDK, JDK, CMake and Ninja.
Fetch the pinned SDL2 and FAudio dependencies using the runtime build instructions, supply the original
`assets/` directory, and build the Windows client in Release to compile its Vulkan content. Run
`Client/PristonTale.VulkanAndroid/build.ps1` with `-AndroidSdk`, `-Ndk` and `-JavaSdk`. Use `-CMake` and `-Ninja` for explicit tool paths, and `-AssetRoot` and `-DesktopContent` to use existing source and content directories.
On Windows, if Ninja runs a child command but never returns in a restricted shell, verify it in a regular PowerShell session and pass the working executable with `-Ninja`.
Use `-SkipNativeBuild` to package existing ARM64 and x86_64 libraries from `obj/vulkan` after compiling them separately.
The signed APK is copied to Etervale-<version>.apk in the Vulkan Android project's Release output. The asset package covers Ricarten,
the character selection, player classes and city NPCs. Original game assets are supplied separately.
Use forward slashes for entry paths in the asset archive so the same archive extracts correctly on Android.
For on-device quest and Auto-caça verification, connect to a running server that publishes quest target codes
and select a character with a hunt objective in the packaged field.

Pass `-ServerAddress 'host:10009?cert=<server SHA-256>'` to embed the endpoint in the APK. The address field is hidden
when the APK includes an endpoint. Builds without a packaged address expose the field and reuse the last successful endpoint.

For a USB device, forward the game port with "adb reverse tcp:10009 tcp:10009" and connect to
"127.0.0.1:10009?cert=<server SHA-256>". Verify a TLS connection before testing login and gameplay.
For large local APKs, "adb install --no-streaming --no-incremental <apk>" uses a push before installation.

On Windows, if the server can load the certificate and bind the port but closes TLS handshakes in a
restricted shell, run it from a regular PowerShell session and verify the presented certificate fingerprint.
Android starts extracting the asset archive on a background thread at launch and reuses the cache on later launches.
Priston Avalon artwork and an animated spinner cover asset preparation and the SDL surface until the first game frame. The SDL overlay is attached after the activity has installed its surface. Once the first frame is rendered, the splash stays dismissed for the lifetime of the app process.
A packaged endpoint opens the shared desktop opening cinematic and login panel once the archive is ready.
Touch reveals the panel. Tapping either credential field opens or reopens the Android keyboard for that field. Tapping the game surface outside the fields dismisses it without clearing the input. The panel scales for
landscape touch targets. Builds without a packaged endpoint use the native Android form to collect an address.
The SDL Vulkan loader is opened before MonoGame enumerates graphics adapters. Startup errors are displayed on
return to the native launcher. A separate opening archive contains the cinematic, its music and the interface atlas.

Vulkan 0.22 uses the animated opening as the credential screen for packaged endpoints. If SDL exits, the launcher offers an explicit reopen action. NPC dialogue displays the server-provided speaker name below the animated portrait and keeps the quest title and dialogue in the subtitle card.

Vulkan 0.23 releases the opening atlas and presents a frame after disposing character selection before loading the world. The world starts at Medium quality with the existing exposure compensation. Android logs record field load start and completion.

Vulkan 0.24 extracts packaged game resources into Android's private no-backup files directory. Android can evict individual cache files, leaving the extraction marker in place while required assets such as the opening audio are missing.

On the Android test device, Vulkan 0.24 was killed in the foreground with signal 9 (2.6 GB PSS in Android's exit record). A subsequent process reached 3.2 GB PSS during the opening cinematic, with approximately 2.2 GB attributed to the native heap. The exit record did not identify the source of the signal.

Vulkan 0.25 uses the Low rendering preset and a 1024-pixel shadow atlas for the animated opening and world. After the opening closes, Vulkan presents a frame before allocating character selection resources, matching the existing handoff from selection to the world.

The Vulkan backend creates Android descriptor pools on demand in batches of 256 sets per shader. The previous eager allocation created 16,384 sets for every shader. Additional pools grow with demand and remain owned by the shader until its deferred destruction.

Vulkan 0.25 device validation: the animated opening rendered at approximately 0.8 GB PSS after warming up. A player entered Ricarten and walked with the touch joystick for about 30 seconds across several inputs, ending near Adventurer Skadi. The process stayed on the same PID, with approximately 1.0 GB PSS and 0.29 GB native heap PSS after the walk. The NPC portrait and dialogue rendered in-world.

The Vulkan HUD opens an in-world navigation menu and a side inventory with Bag, Equipped, Belt and Quest tabs backed by the owner's live inventory snapshot. The quest tracker and journal open the latest active step and display the server's hunt counter and timer only for that step.

Vulkan 0.26 uses the Priston Avalon launcher name and original gold shield icon, with launcher artwork supplied at five Android densities. The in-game menu shows the installed APK version.

Vulkan 0.27 uses an adaptive launcher icon so Android applies the launcher shape. Density-specific fallback icons use transparent, rounded corners.

Vulkan 0.27 presents an in-world menu with a character header, version, two rows of icon shortcuts and a separate exit action. Exiting closes the game task. Packaged builds dismiss the launcher when SDL returns and report a startup failure with a toast.

Vulkan 0.28 distinguishes the launcher's first OnResume from its return after SDL, using the intervening OnPause. With assets already extracted, SDL can be requested during OnCreate, before the first OnResume.

Vulkan 0.29 connects the inventory to server-validated equip, unequip, belt placement and belt return operations. Commands use the last authoritative inventory revision and the desktop default slot rules. The Vulkan world triggers the shared entry effect once using the entity ID from the server; the restart sound can wait briefly for its existing asynchronous load. The world shows before the effect library has finished loading, so `GameScene.PlayInitialArrival`, called every frame, waits until the pillar of light (`vfx.player.restart`) is registered; before 0.43 it fired on the first frame and the effect was dropped.

Vulkan 0.30 adds the server-side Adventurer Journey. Skadi (Tempskron) and Jin (Morion) verify a weapon and a filled belt slot before starting the three-kill hunt. The server checks a newly acquired Hopy Crystal at turn-in and pays 100 XP once. The journal gives directions to the original hunting fields and Auto Hunt tracks visible monsters of the active quest. The supplemental archive adds Pillai, the starting hunting fields and their monster models. In manual mode a tap selects a target; Attack uses that target or the nearest living monster when none is selected.

The 0.30 APK was signed and installed over 0.29 using ADB. Keep the same application ID and signing key for in-place upgrades, and increment the application version code for every new APK. The 0.30 server was deployed on port 10009 with the existing SQLite database and TLS certificate. Both Adventurer Journey branches passed the focused simulation test. Device validation of the full quest flow and automatic route guidance remains pending.

Device walkthrough on 0.30 confirmed entry in Ricarten, Skadi's accepted quest and the Preparation journal entry. The current character had no potion in the bag, so the hunt remained gated. The initial guide objective before the first NPC conversation, the Morion walkthrough, collection, turn-in and manual/automatic combat are still open for device validation.

Vulkan 0.31 publishes a welcome objective in the initial quest view for characters eligible for the Adventurer Journey. In the home town the HUD points toward Skadi or Jin and indicates when the guide is close. The first conversation advances the quest and gives one small HP potion directly to the active bag page. Existing 0.30 characters in Preparation can receive the same one-time tutorial potion by talking to the guide. A blocked grant can be retried after making space.

Rewards are adapted from the public Zephira quest catalog (https://clandragonballz.com.br/wiki/missoes). Welcome pays 15% of level-one XP. The combined preparation step pays 30% of level-one XP, 15 small HP potions and 500 gold. The first hunt pays 20% of level-one XP and 300 gold. The Android bag displays the actual 12-by-6 inventory layout with item footprints, category glyphs and tap-to-select item actions.

Device check confirmed that 0.31 entered Ricarten on the updated server and displayed the 12-by-6 bag grid with the three existing 2-by-2 crystal items. The walkthrough ended in Preparation before the potion grant.

### Automatic quest

Vulkan 0.32 plays the Adventurer Journey by itself (`QuestNavigator`, run once a frame on the game thread from
`RicartenProbe.Update`). It waits for the player: the quest card's Iniciar auto-missão button starts it (the card points
to the guide meanwhile), and the journal's auto-hunt action does too. The joystick pauses it.

- **Route data.** `AdventurerJourneyRoute` (`Shared/PristonTale.Simulation/Quests`) holds, per tribe, the guide's
  name and place, the hunting field, the exits between them (Ricarten and Forest 1 meet at a seam; Pillai and Forever
  Fall 1 at warp gates) and the hunting field's spawn points nearest the entry. The server names the guide from the
  same table. Points are a field's own planar (x, z).
- **Steps** (`QuestNavigator.Plan`, from the quest view):
  - Welcome objective or state 0: go to the guide and talk.
  - State 1: equip a weapon from the bag and put a potion in the belt when missing (the same inventory commands the
    bag uses), then talk to the guide.
  - State 2 with kills left: travel to the hunting field and hunt the quest's monster (`QuestCode`), walking from one
    spawn point to the next while none is in view and picking up drops within reach.
  - State 2 with the kills done: the guide takes a Hopy Crystal gathered after the hunt began. The navigator keeps the
    crystal count it saw while hunting; when the guide answers without moving the quest on, it hunts and gathers again
    (`VulkanWorldSession.KeepHunting` keeps Auto Hunt running past the kill count).
- **Walking.** Routes come from `GroundRoute` (`Client/PristonTale.Gameplay/World`): an A* over 32-unit cells, searched
  off the game thread over a snapshot of the loaded fields (`GameScene.PlanRouteAsync`; their collision meshes are
  read-only). Cells are told apart by height (`Storey`), so stairs and bridges are their own places. Each leg is a walk
  `GameScene.WalksTo` accepts, step by 12-unit step:
  - the wall slide at body height, looking 1.25 radii ahead, must not turn the step aside;
  - the floor is sampled every unit (`FollowsFloor`). The player samples it two to four units apart, depending on the
    frame rate, and stops at a rise of more than `StepUp` between two samples, wherever they fall; so the route holds
    every rise between any two of its samples up to four units apart under `RouteStepUp` (one under `StepUp`), and
    every drop over two units under `GroundFall.SnapDistance` (a larger one plays the fall);
  - the wall slide traces only the line the body's centre walks, so each step is also traced sideways and along two
    parallel lines on each side out to `RouteBody` (0.75 radii), and the floor at those offsets must not rise more
    than `RouteStepUp` or fall more than `SnapDistance` (`Crowded`, `Uneven`): lamp posts, railings, a stair's side,
    a railing's base and kerbs lower than the slide's trace height, and the edge of a drop, are all kept clear.
  A leg that only passes with `RouteNarrowBody` (0.3 radii, still more than the player strays from a leg) of side room
  is cramped and costs 2.5 times its length, so the route keeps to open ground and takes a narrow stair or gate only
  where there is no other way. Every unit the feet rise or fall on a leg adds four to its cost, so the route walks
  around hedges and planters it could step onto and takes stairs only as the way up. Standing NPCs in view
  (`VulkanWorldSession.NpcPoints`, but those near the goal) are kept two body radii from every leg. Only a leg of at
  most 48 units out of the player's own place skips the side room and the NPCs, so a player against a wall or beside
  an NPC can leave. The search weighs the distance left 1.5 times the way walked, so it heads for the goal. The route
  is shortened to the fewest legs, a shortcut kept only when it is walkable, climbs no more than the legs it replaces
  and is cramped only where one of them was. A goal outside the loaded fields (an exit into a field still streaming)
  gives a route to the nearest reachable point, the search ending 6000 cells after it last came nearer, and the route
  is searched again from there. A start near the goal counts as arrived only when the leg to it is walkable, so a goal
  below a parapet is not.
  `GameScene.PlanRoute` then walks the route frame by frame with the player's own movement (`WalkRoute`: what
  `UpdatePlayerInput` does at a run, 60 frames a second: `DestinationMovement`, the wall slide with the body's radius,
  the floor). Where that walk stops or falls, the place is avoided like an NPC and the search runs again, up to four
  times, so a route handed to the player is one its movement walks to the end.
  The player walks to each point of the route to within `GameScene.RoutePointReach` (one unit; `GameScene.WalkTo`, not
  a tap's body radius), so it walks the legs the search checked. A walk that has not moved for `StuckSeconds` (one
  second) searches again from where it stands. A search that finds no leg leaves the player standing, says so on the
  quest card and tries again two seconds later: the player never walks blind at the goal. Every start and pause of the
  automatic quest drops the route, so a resume plans afresh from where the player stands.
  `dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --inspect-adventurer-route <assets>` walks the
  whole Adventurer Journey of both tribes over the town's and the hunting field's collision: from a grid around the
  guide to the guide, the guide to the exit, then every hunt point in turn; each route is walked with `WalkRoute` and
  every stop is printed. In 0.45 all 155 Tempskron and 148 Morion routes found are walked to the end; searches take
  under 1.5 s on a desktop but for two of about 300 (2.8 s at most). Starts that find no route are on roofs, in closed
  yards and in ditches.
  The player follows the route to within `TalkReach` of the guide, and only then approaches it
  with `GameScene.ApproachNpc` (whose own walk is a straight line), and only from where that straight walk is clear
  (`GameScene.WalksStraightTo`, the route's leg check at the body's own width): near the guide but on a terrace above it
  or behind a wall, the route goes on to the guide itself, down the stairs or around the wall. Within `WaypointReach`
  the approach starts whatever the check says. It goes for drops with `GameScene.PickUpNear`, each as the matching tap.
- **Fighting.** A monster within `DefendRange` of the player is fought before the quest's own, and a belt potion is
  drunk under 45% life. The game's attack walks straight at its target; when that leaves the player standing for
  `AttackStuckSeconds` out of reach, the navigator drops the attack, walks to the target along a route and attacks again.
- **Minimap.** The route shows as yellow dots on the field's minimap (`MobileMinimap.SetRoute`); Ricarten, Pillai and
  Forever Fall 1 have one.
- **Dialogs.** While it runs, a quest dialog shows for `AutoAnswerMilliseconds` and answers its first choice.
- **HUD.** `VulkanQuestTracker` replaces the old quest card: the journey's title, the objective, a kill bar, what the
  automatic quest is doing (or the compass to the guide while paused) and the Auto-missão button.
- **Starter weapon.** The guide's first talk, and any talk in Preparation, gives the class's beginner weapon
  (`ClassProfile.BeginnerWeapon`) to a character that carries no weapon at all, so the step it asks for can be done.

### Character window (0.34)

`VulkanInventoryPanel` is one window over a dimmed screen: the equipment slots around the character on the left
(rings and amulet, weapon, armour and shield, gloves, bracelet and boots, sheltom), the 12 x 6 bag with each item's own
sprite at its footprint (`VulkanItemIcons`: the desktop's `ItemIconFiles` sprites, decoded off the UI thread), the potion
belt, gold and weight in the header, and the chosen item's sheet (values from `ItemDetailView`) with its actions: equip,
remove, belt or store. Every action is the inventory command the server validates.

Device runs: 0.32 walked SatroX to Skadi, readied the belt, advanced to the hunt and reached Forest 1, where it hunted
Hopy; the route of that build cut through walls, which 0.33 replaced with `GroundRoute`. On 0.38 the route reached the
terrace above Skadi and then ran into its parapet: within `TalkReach` on the plane, the navigator approached in a
straight line across the drop; 0.39 checks that walk first (above). 0.39 still struck planters and a lamp post on the
way, cutting from one route point to the next 40 units early; 0.40 walks to each point. 0.40 still grazed railings
beside the stairs: its legs were checked along the centre line only. 0.41 adds the side traces. 0.41 still ran into
walls in two cases: a search with no way walked a straight line at the goal (off a ledge into the ditch under a bridge,
too), and a resume walked on the route planned before the pause. 0.42 removes both. 0.42 walked over a hedge and into
a standing NPC; 0.43 makes climbing costly and keeps clear of NPCs. 0.43 still stopped at stair corners and turned too
early: the frame-by-frame walk of the probe showed 48 of 173 routes stopping or falling, from low structures beside the
line, drops up to 32 units, the four-unit arrival and floor samples four units apart; 0.44 fixes each and none stops.
On 0.44 the hunt's way to Forest 1 took long to plan and then ran into things: the stricter legs made the roomy search
fail and sweep the whole town before the narrow one ran, and Pillai's steep ramps stopped walks the route had passed.
0.45 runs one search with cramped legs costed, samples the floor every unit with the four-unit window above, and walks
every route before handing it out. Not verified on the device yet: the 0.45 walk and approach,
the warp gates of Pillai, the crystal pickup and the turn-in, and the 0.34 character window.
