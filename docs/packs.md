# Asset packs

The Android APK carries Ricarten, Pillai, Forest 1 to 3 and Forever Fall 1 to 4 with their monsters (`build.ps1`,
`package-adventurer-assets.ps1`) and the normal and event crystal summon models (`ptpack --summon-models`). Other fields
and monsters come as asset packs that the phone downloads from the website the first time it needs them. The whole game data would make an APK of more than 4 GB, which Android does not
install reliably. The packs hold only the files the client reads, about 940 MB for all 53 fields and 412 monster and
NPC models.

## Read this when

- You add or change a field, a stage object, a monster model or a minimap, and a phone must get it.
- You build or publish the packs, or change where the APK downloads them from.
- You change how the phone stores game files (`VulkanAssets`, `VulkanPacks`).

## Code map

| Path | Responsibility |
| --- | --- |
| `Shared/PristonTale.Assets/PackManifest.cs` | `packs.json`: every pack (id, size, SHA-256, files), each field's packs, the model-to-pack map; pack ids and model keys |
| `Client/PristonTale.Engine3D/Rendering/PristonAssetDependencies.cs` | The files a stage or an actor reads, by the renderer's own readers, material selection and texture resolution (`ptdump` uses it too) |
| `tools/PristonTale.PackBuild/` (`ptpack`), `tools/pack-build.ps1`/`.sh` | Builds the downloaded packs and `packs.json`, or the APK summon archive, from the game data and the world database |
| `tools/PristonTale.GltfExport/MobileMinimapBake.cs` | A field's minimap image and bounds (`ptgltf --mobile-minimap` and the minimap packs) |
| `Web/PristonTale.Web/Services/PackFiles.cs` | Serves `/packs/packs.json` and `/packs/<id>.zip` from `Downloads:PacksPath`, by name only, with range requests |
| `Client/PristonTale.Client.Net/Packs/PackStore.cs` | On the device: reads the manifest, tells what is missing, downloads (resuming), checks the SHA-256, unpacks |
| `Client/PristonTale.VulkanAndroid/VulkanPacks.cs` | The phone's packs: the field gate, quiet model and minimap downloads, the download card's state |
| `Client/PristonTale.VulkanAndroid/VulkanDownloadCard.cs` | The card over the game while a field downloads: progress, Cancelar, Tentar de novo |
| `Client/PristonTale.VulkanAndroid/VulkanAssets.cs` | The phone's asset folder, kept by every version; the APK archives unpacked over it |
| `Client/PristonTale.Gameplay/World/GameScene.Fields.cs` (`FieldAssetsReady`, `ModelAssets`), `Remote/RemoteEntityManager.cs` | The hooks through which a field or a model waits for its files |

## The packs

`ptpack <assets> <world.db> <output>` writes one zip per pack and `packs.json`:

- **`field-<field id>`:** the field's stage, its stage objects and every texture they draw
  (`PristonAssetDependencies.StageFiles`). This is what a field waits for before it loads.
- **`model-<model path>`:** a monster or NPC model. It holds the model info, the motion model info it links to, the
  mesh, the skeleton, the textures of the objects it draws, and the same for its sub model
  (`PristonAssetDependencies.ActorFiles`). Every model of the database's `monsters` (`model` and `event_model`) and
  `npcs` tables gets one, so an event form no field lists can still be fetched. The NPC packs mostly find their files
  already in place (the APK carries `char/npc`).
- **`minimap-<field id>`:** `minimaps/<field id>-minimap.png` and `.bin` (`MobileMinimapBake`), for the 48 fields with
  an image in `Field/map`.

Each field in `packs.json` lists:

- `packs`: its stage pack;
- `monsters`: the packs of the monsters and bosses that spawn there, from `field_monster_spawns` and `field_bosses`;
- `minimap` and `minimapPack`.

`models` maps each model key (`PackManifest.ModelKey`: forward slashes, lower case, `.inx`) to its pack.

Zip entries are the files' asset-relative paths in the data's own letter case. Every entry carries the same time, so a
pack rebuilt from unchanged files has the same SHA-256 and phones do not download it again.

A model or texture the game data lacks is left out and counted in the summary line (4 models and 308 textures today; the
PC does not draw them either). A field the tool cannot read fails the build (exit code 1), because a field missing from
`packs.json` would never be downloaded.

## On the phone

- **One folder:** `NoBackupFilesDir/assets` holds the APK's archives (unpacked once per `VulkanAssets.CacheVersion`)
  and every pack. `packs-installed.json` names each installed pack with its SHA-256. A pack also counts as installed
  when every file it lists is already there at its size, which is how the fields the APK carries are known.
- **Manifest:** `packs.json` is read at start (`VulkanPacks.PrepareAsync`, 20 s at most, again 30 s after a failure) and
  kept in the folder for when the website cannot be reached. It is kept only once it parses and has the expected version.
- **Fields:**
  - World entry, the streaming of neighbouring fields and a held warp all ask `VulkanPacks.FieldReady`
    (`GameScene.FieldAssetsReady`).
  - A field whose stage file is on the phone loads at once, with or without the website: the fields the APK carries
    and those downloaded before. Its packs are then brought up to date quietly, once per run: a newer build of its
    stage pack, its monsters' packs.
  - Any other field waits for its stage pack. The world waits on a black screen, or the warp is held as for any field
    still loading.
  - The download card shows the field, the megabytes and Cancelar. A failed download is tried again after 8 s. A
    cancelled one waits for Tentar de novo.
  - With no manifest, or a field the manifest does not know, the card says so and the field does not load. Such a field
    is never loaded without its files.
- **Models:** a monster or NPC whose model is not installed waits for its pack like for its model's preparation
  (`RemoteEntityManager.ModelAssets`). It then appears.
- **Downloads:**
  - One pack installs at a time (`PackStore.InstallAsync`), so a field's stage waits at most for the one quiet pack
    under way.
  - They go to `.packs/<id>-<first 16 hex of its SHA-256>.part`. An interrupted one resumes with a range request. A part
    of an earlier build of the pack is deleted, never resumed.
  - A download that gets no answer or no bytes for 30 s stops and is tried again.
  - A file whose size or SHA-256 differs from the manifest is deleted and fetched again.
  - Entries are unpacked over the folder. One whose path would leave the folder stops the install.
  - Free space for the zip, a copy and the unpacked files is checked first ("Pouco espaço livre").
- **New APK:** a new `CacheVersion` unpacks the APK archives over the folder and deletes `packs-installed.json`. Each
  pack is then checked again by its files' sizes.
- **Trust:**
  - The pack address must be `https` (a loopback test server excepted). Downloads use the address's own pin,
    `?cert=<SHA-256 of the website's certificate>`, checked like the game connection's (`ServerCertificatePolicy`).
  - An address that cannot be used is logged, and the phone then plays the fields it has.

## How to

### Build and publish the packs

```bash
tools/pack-build.sh <assets> <server data>/pristontale.db <site>/packs
```

The database is the game server's (the site's `Database:SqlitePath`); it is opened read-only. Copy the output into the
site's `Downloads:PacksPath` folder (default `packs` beside the site's executable). The site serves the new files at
once.

### Serve them to phones

The site must be reachable from the phones and trusted by the pin in the APK:

1. Point the site's HTTPS endpoint at the game server's certificate, so one pin covers both. In `web-settings.json`, set
   `"Kestrel": { "Endpoints": { "Https": { "Url": "https://0.0.0.0:5443", "Certificate": { "Path": "<server data>/server-certificate.pfx" } } } }`.
2. Open the site's HTTPS port in the firewall of the machine.
3. Build the APK with `-PackAddress 'https://<host>:5443/packs/?cert=<certificate SHA-256>'`
   ([android.md](android.md), "Build the APK"). The SHA-256 is the one the server logs at start.

`Security:PackRequestsPerMinute` (600) limits the requests under `/packs` per address, apart from the pages' limit.

### Add a field or a monster

Add it to the catalog or the database as usual, rebuild the packs and copy them to the site. Phones fetch it the first
time they need it. Nothing in the APK changes.

## Pitfalls

- A pack keeps its id when rebuilt. Phones notice the new SHA-256 in `packs.json` and fetch it again, replacing its files.
- Files several packs share are unpacked by each. They are the same bytes, so the order does not matter.
- Without a pack address in the APK (`EtervalePackAddress`), it enters only the fields whose stages it carries.
- The site's limits count per address. Many phones behind one address share them.
- A server warp to a field whose stage cannot be downloaded (website down, phone offline) holds the player, as a warp
  to any field still loading does, until the download works. There is no fallback to town yet.
- There is no menu yet to see, delete or download every pack at once.

## Related

- [android.md](android.md): the APK, its archives and the HUD.
- [website.md](website.md): the site that serves the packs.
- [tools.md](tools.md): `ptpack` and `ptgltf`.
