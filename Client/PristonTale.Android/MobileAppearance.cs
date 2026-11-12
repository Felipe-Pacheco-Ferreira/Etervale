using System.Numerics;
using PristonTale.Contracts.Characters;
using System.Collections.Concurrent;
using Android.Content.Res;
using PristonTale.Client.Net.Presence;

namespace PristonTale.Mobile;

internal sealed class MobileAppearance(AssetManager assets)
{
    private readonly ConcurrentDictionary<string, Task<BakedPart?>> _loads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GlActorPart> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Task<Dictionary<string, string>>> _catalog = new(() => Task.Run(() => ReadCatalog(assets)));

    private readonly ConcurrentDictionary<PlayerClass, Task<SocketTracks>> _sockets = new();
    private readonly ConcurrentDictionary<string, Task<SocketTracks?>> _armorSockets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Task<Dictionary<string, GearEntry>>> _gear = new(() => Task.Run(() => ReadGear(assets)));

    public void Prepare(PlayerAppearanceInfo appearance)
    {
        if (!string.IsNullOrEmpty(appearance.ArmorKey))
        {
            var key = ArmorKey(appearance);
            _loads.GetOrAdd(key, _ => LoadArmorAsync(key));
        }
        if (!string.IsNullOrEmpty(appearance.WeaponKey) || !string.IsNullOrEmpty(appearance.OffHandKey))
        {
            _sockets.GetOrAdd(appearance.Class, job => Task.Run(() => SocketTracks.Read(assets, job)));
            if (!string.IsNullOrEmpty(appearance.ArmorKey))
            {
                var key = ArmorKey(appearance);
                _armorSockets.GetOrAdd(key, LoadArmorSocketsAsync);
            }
            PrepareGear(appearance.WeaponKey);
            PrepareGear(appearance.OffHandKey);
        }
        if (appearance.HelperPet is >= 1 and <= 4)
        {
            PreparePet(appearance.HelperPet);
        }
        if (appearance.Phoenix)
        {
            PreparePet(5);
        }
    }

    public GlActorPart? Armor(PlayerAppearanceInfo appearance, GlTexture white) => GetPart(ArmorKey(appearance), white);
    public GlActorPart? Pet(int id, GlTexture white) => GetPart($"pet-{id}", white);
    public bool TryGetGear(PlayerAppearanceInfo appearance, bool offHand, int clip, float phase, GlTexture white,
        out GlActorPart? part, out Matrix4x4 socket)
    {
        part = null;
        socket = default;
        var key = offHand ? appearance.OffHandKey : appearance.WeaponKey;
        if (string.IsNullOrEmpty(key) || !_gear.IsValueCreated || !_gear.Value.IsCompletedSuccessfully
            || !_gear.Value.Result.TryGetValue(key, out var gear)
            || !_sockets.TryGetValue(appearance.Class, out var tracks) || !tracks.IsCompletedSuccessfully)
        {
            return false;
        }

        var bone = offHand ? "Bip01 L Forearm" : appearance.InVillage ? gear.VillageBone : gear.HandBone;
        var poses = tracks.Result;
        var armorKey = ArmorKey(appearance);
        if (_parts.ContainsKey(armorKey) && _armorSockets.TryGetValue(armorKey, out var armorTracks))
        {
            if (!armorTracks.IsCompleted)
            {
                return false;
            }
            if (armorTracks.IsCompletedSuccessfully && armorTracks.Result is { } armored)
            {
                poses = armored;
            }
        }
        if (!poses.TrySample(bone, clip, phase, out socket))
        {
            return false;
        }
        part = GetPart("gear:" + key, white);
        return part is not null;
    }

    public void ResetGraphics() => _parts.Clear();

    private void PreparePet(int id)
    {
        var name = $"pet-{id}";
        _loads.GetOrAdd(name, key => Task.Run<BakedPart?>(() => ReadPart(key + ".bin")));
    }

    private void PrepareGear(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _loads.GetOrAdd("gear:" + key, _ => LoadGearAsync(key));
        }
    }

    private async Task<BakedPart?> LoadGearAsync(string key)
    {
        var catalog = await _gear.Value.ConfigureAwait(false);
        return catalog.TryGetValue(key, out var entry) ? ReadPart(entry.File) : null;
    }

    private async Task<SocketTracks?> LoadArmorSocketsAsync(string key)
    {
        var catalog = await _catalog.Value.ConfigureAwait(false);
        return catalog.TryGetValue(key, out var file)
            ? SocketTracks.Read(assets, Path.ChangeExtension(file, ".sockets.bin"))
            : null;
    }

    private async Task<BakedPart?> LoadArmorAsync(string key)
    {
        var catalog = await _catalog.Value.ConfigureAwait(false);
        return catalog.TryGetValue(key, out var file) ? ReadPart(file) : null;
    }

    private GlActorPart? GetPart(string key, GlTexture white)
    {
        if (_parts.TryGetValue(key, out var part))
        {
            return part;
        }
        if (!_loads.TryGetValue(key, out var load) || !load.IsCompletedSuccessfully || load.Result is not { } baked)
        {
            return null;
        }

        var textures = new GlTexture[baked.Textures.Length];
        for (var index = 0; index < textures.Length; index++)
        {
            textures[index] = GlUpload.Texture(baked.Textures[index], 256, world: false);
        }
        part = new GlActorPart(baked.Part, textures, white);
        _parts.Add(key, part);
        return part;
    }

    private static string ArmorKey(PlayerAppearanceInfo appearance) => $"{(int)appearance.Class}:{appearance.ArmorKey}";

    private static Dictionary<string, string> ReadCatalog(AssetManager manager)
    {
        var catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = manager.Open("Appearance/armor.tsv");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length == 3)
            {
                catalog[$"{fields[0]}:{fields[1]}"] = fields[2];
            }
        }
        return catalog;
    }

    private static Dictionary<string, GearEntry> ReadGear(AssetManager manager)
    {
        var catalog = new Dictionary<string, GearEntry>(StringComparer.OrdinalIgnoreCase);
        using var stream = manager.Open("Appearance/gear.tsv");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length == 4)
            {
                catalog[fields[0]] = new GearEntry(fields[1], fields[2], fields[3]);
            }
        }
        return catalog;
    }

    private sealed record GearEntry(string File, string HandBone, string VillageBone);

    private sealed record SocketTracks(Dictionary<string, int> Bones, int Samples, Matrix4x4[] Poses)
    {
        public static SocketTracks Read(AssetManager assets, PlayerClass job) => Read(assets, $"sockets-{(int)job}.bin");

        public static SocketTracks Read(AssetManager assets, string file)
        {
            using var stream = assets.Open("Appearance/" + file);
            using var reader = new BinaryReader(new BufferedStream(stream, BakedReader.BufferSize));
            var boneCount = BakedReader.Count(reader, 256);
            var bones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < boneCount; index++)
            {
                bones.Add(reader.ReadString(), index);
            }

            var samples = BakedReader.Count(reader, 128);
            var poses = new Matrix4x4[boneCount * samples * 3];
            for (var index = 0; index < poses.Length; index++)
            {
                poses[index] = new Matrix4x4(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            return new SocketTracks(bones, samples, poses);
        }

        public bool TrySample(string bone, int clip, float phase, out Matrix4x4 pose)
        {
            pose = default;
            if (clip is < 0 or >= 3 || Samples == 0 || !Bones.TryGetValue(bone, out var index))
            {
                return false;
            }
            var frame = phase * Samples;
            var current = Math.Min((int)frame, Samples - 1);
            var next = (current + 1) % Samples;
            var start = clip * Samples;
            pose = Matrix4x4.Lerp(Poses[(start + current) * Bones.Count + index], Poses[(start + next) * Bones.Count + index], frame - current);
            return true;
        }
    }

    private BakedPart ReadPart(string file)
    {
        using var stream = assets.Open("Appearance/" + file);
        using var reader = new BinaryReader(new BufferedStream(stream, BakedReader.BufferSize));
        return new BakedPart(BakedReader.Textures(reader), BakedReader.Part(reader));
    }

    private sealed record BakedPart(byte[][] Textures, ActorPart Part);
}
