using PristonTale.Assets;

namespace PristonTale.Mobile;

internal sealed record WorldArrival(byte[][] Textures, ActorPart Part, float OffsetY, float FadeIn, float FadeOut)
{
    public const string ModelAsset = "world-arrival.bin";
    public const string SoundAsset = "fieldstart.wav";
    public const int Clip = 0;

    public static WorldArrival Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        BakedReader.Magic(reader, MobileActorFormat.WorldArrivalMagic);
        var offsetY = reader.ReadSingle();
        var fadeIn = reader.ReadSingle();
        var fadeOut = reader.ReadSingle();
        return new WorldArrival(BakedReader.Textures(reader), BakedReader.Part(reader), offsetY, fadeIn, fadeOut);
    }
}
