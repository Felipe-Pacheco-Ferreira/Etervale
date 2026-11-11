using System.Text.Json;
using PristonTale.Assets;
using PristonTale.Contracts.Units;

namespace PristonTale.GltfExport;

internal static class MobileWorldArrivalBake
{
    public static void Write(string modelPath, string definitionPath, string outputPath)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(definitionPath));
        var definition = source.RootElement.EnumerateArray().Single(effect =>
            effect.GetProperty("name").GetString() == "vfx.player.restart");
        var mesh = definition.GetProperty("parts").EnumerateArray().Single();
        var start = mesh.GetProperty("modelStart").GetInt32();
        var count = mesh.GetProperty("modelFrames").GetInt32();
        var lifetime = mesh.GetProperty("lifetime")[0].GetSingle();
        var speed = mesh.GetProperty("modelSpeed").GetSingle();

        const string animation = "Animation";
        var textures = new List<byte[]>();
        var model = new MobileSceneBake.Model(modelPath, textures);
        var duration = model.Duration(animation) ?? throw new InvalidDataException("The arrival model has no animation.");
        var frames = new float[count][][];
        for (var frame = 0; frame < count; frame++)
        {
            var phase = Math.Min(1f, (start + frame * lifetime * speed / count) * GameUnits.FrameMs / (1000f * duration));
            frames[frame] = model.Bake(animation, phase).Select(batch => MobileActorWriter.Channel(batch.Vertices, 0, 3)).ToArray();
        }

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.Write(MobileActorFormat.WorldArrivalMagic);
        writer.Write(mesh.GetProperty("offset")[1].GetSingle());
        writer.Write(mesh.GetProperty("fadeIn").GetSingle());
        writer.Write(mesh.GetProperty("fadeOut").GetSingle());
        MobileActorWriter.WriteTextures(writer, textures);
        MobileActorWriter.WritePart(writer, new MobilePart(MobileActorWriter.Batches(model, animation), [new MobileClip(lifetime, frames)]));
    }
}
