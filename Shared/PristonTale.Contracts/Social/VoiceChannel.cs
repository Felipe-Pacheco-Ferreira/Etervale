namespace PristonTale.Contracts.Social;

public enum VoiceChannel : byte
{
    Nearby, Party, Clan, Map
}

public static class VoiceFormat
{
    public const int SampleRate = 16000;
    public const int FrameBytes = 640;
    public const int ChannelsMask = 15;
}
