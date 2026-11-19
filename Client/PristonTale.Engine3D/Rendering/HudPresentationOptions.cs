using System.Text.Json.Serialization;

namespace PristonTale.Engine3D.Rendering;

// The colour of the local player's own name over the character (ClientSettings.ShowOwnName).
[JsonConverter(typeof(JsonStringEnumConverter<NameColor>))]
public enum NameColor
{
    White,
    Gold,
    Green,
    Blue,
    Pink,
    Orange
}

// The floating damage numbers' size: Normal is the original's.
[JsonConverter(typeof(JsonStringEnumConverter<DamageNumberSize>))]
public enum DamageNumberSize
{
    Small,
    Normal,
    Large,
    Huge
}
