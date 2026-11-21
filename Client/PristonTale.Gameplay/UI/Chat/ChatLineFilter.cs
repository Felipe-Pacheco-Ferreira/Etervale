using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Chat;

// The chat lines the settings keep out of the chat and the screen. Streamer mode hides the whispers, or every line
// people wrote (whispers, party, clan, trade and what players say aloud); the server's and the game's notices stay.
public static class ChatLineFilter
{
    public static bool Hides(ClientSettings settings, ChatLineInfo line) =>
        (settings.HideTradeChat && line.Kind == ChatKind.Trade) ||
        (settings.HideCalendarNotices && line.Kind == ChatKind.CalendarEvent) ||
        (settings.HideGlobalBuffNotices && line.Kind == ChatKind.GlobalBuff) ||
        (settings.HideEventRewardNotices && line.Line.Id == GameTextId.RewardClaimed) ||
        (settings.HideClanMembershipNotices && line.Line.Id is GameTextId.ClanJoined or GameTextId.ClanLeft) ||
        (settings.HideBellatraResults && line.Line.Id is GameTextId.BellatraOut or GameTextId.BellatraCleared) ||
        (settings.HideBellatraBossNotices && line.Line.Id == GameTextId.BellatraBossDown) ||
        (settings.HideBellatraTeamNotices && line.Line.Id is GameTextId.BellatraQuake or GameTextId.BellatraStun
            or GameTextId.BellatraFreeze or GameTextId.BellatraRabbit or GameTextId.BellatraStygian
            or GameTextId.BellatraGuardian or GameTextId.BellatraPoints or GameTextId.BellatraSealIdle) ||
        (settings.HideCastleNotices && line.Kind == ChatKind.Notice &&
         line.Line.Id is GameTextId.CastleTaken or GameTextId.SiegeInAnHour or GameTextId.SiegeInHalfAnHour or GameTextId.SiegeLeague
             or GameTextId.SiegeOver or GameTextId.SiegeRestartsSoon or GameTextId.SiegeStarted) ||
        (settings.HideBossNotices && line.Line.Id == GameTextId.BossTime) ||
        (settings.HideAgingNotices && line.Line.Id == GameTextId.AgingNotice) ||
        (settings.HideLevelUpNotices && line.Line.Id == GameTextId.LevelNotice) ||
        (settings.HideDuelNotices && line.Line.Id is GameTextId.DuelNotice or GameTextId.DuelDrawNotice) ||
        (settings.HideArenaNotices && line.Line.Id is GameTextId.ArenaOpen or GameTextId.ArenaNotice or GameTextId.ArenaDrawNotice) ||
        (settings.HideRoyaleNotices && line.Line.Id is GameTextId.RoyaleOpen or GameTextId.RoyaleCancelled or GameTextId.RoyaleStarted or GameTextId.RoyaleWinner) ||
        (settings.HideTitleNotices && line.Line.Id is GameTextId.TitleGained or GameTextId.TitleLost) ||
        (settings.HideBossRouletteNotices && line.Line.Id == GameTextId.BossRouletteWinner) ||
        (settings.PartyLootAlerts != PartyLootAlerts.All && line.Line.Id == GameTextId.PartyLoot) ||
        (settings.PartyLootAlerts == PartyLootAlerts.Off && line.Line.Id == GameTextId.PartyRareLoot) ||
        (settings.BellatraOpeningNotice == BellatraOpeningNotice.Off && line.Line.Id == GameTextId.BellatraOpening) ||
        (settings.StreamerMode && (line.Kind == ChatKind.Whisper || settings.StreamerHidesWholeChat && (Written(line.Kind) && line.Line.IsNone || line.SpeakerId != 0)));

    // A shown line that also goes to the notice strip at the top of the screen.
    public static bool OnScreen(ClientSettings settings, ChatLineInfo line) =>
        settings.BellatraOpeningNotice == BellatraOpeningNotice.Both && line.Line.Id == GameTextId.BellatraOpening;

    // A line a person wrote, as opposed to a notice of the server or the game.
    public static bool Written(ChatKind kind) => kind is ChatKind.Whisper or ChatKind.Party or ChatKind.Clan or ChatKind.Trade or ChatKind.Player;
}
