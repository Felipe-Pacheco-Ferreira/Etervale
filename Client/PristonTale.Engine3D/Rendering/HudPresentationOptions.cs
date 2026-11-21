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

// Which items a party member picked up the chat tells: every item, only unique and set items, or none.
[JsonConverter(typeof(JsonStringEnumConverter<PartyLootAlerts>))]
public enum PartyLootAlerts
{
    All,
    Rare,
    Off
}

// Where the opening of Bellatra's ticket sales shows: in the chat and the notice strip at the top, in the chat, or nowhere.
[JsonConverter(typeof(JsonStringEnumConverter<BellatraOpeningNotice>))]
public enum BellatraOpeningNotice
{
    Both,
    Chat,
    Off
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
