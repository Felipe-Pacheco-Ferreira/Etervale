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

The field HUD uses compact, dark cards with gold trim. Status and chat stay at the upper left, with the minimap
at the upper right, the quest tracker and target at the upper centre, and the action controls around the lower
and right edges of the play area. Quest and chat details open on demand.

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

## Related

- [client.md](client.md): the desktop screens the Android ones follow.
- [tools.md](tools.md): `ptgltf` and its Android modes.
- [protocol.md](protocol.md): login, character commands and world entry.
