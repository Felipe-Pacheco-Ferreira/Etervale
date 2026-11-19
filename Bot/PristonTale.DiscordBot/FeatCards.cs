using System.Text;
using PristonTale.Contracts.Social;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// A Discord card (embed) for a feat of the game: a boss slain or a player killed by another.
public sealed record BotCard(string Title, string Description, IReadOnlyList<BotCardField> Fields, uint Color, string Footer);

public sealed record BotCardField(string Name, string Value, bool Inline);

public static class FeatCards
{
    public const uint BossColor = 0xC0392B;
    public const uint PvpColor = 0xE67E22;
    private const int FieldLimit = 1024;

    // A level gap that makes a kill worth calling out.
    private const int UpsetLevels = 10;

    private static readonly string[] BossLines =
    [
        "A terra tremeu, e **{0}** caiu diante de {1}!",
        "Depois de uma batalha feroz, {1} derrubou **{0}**!",
        "**{0}** não resistiu ao ataque de {1}!",
        "Os bardos já cantam: {1} venceu **{0}**!"
    ];

    private static readonly string[] PvpLines =
    [
        "**{0}** venceu o duelo contra **{1}**.",
        "**{1}** caiu pelas mãos de **{0}**.",
        "Sem piedade: **{0}** derrubou **{1}**.",
        "**{0}** mostrou quem manda e abateu **{1}**."
    ];

    public static BotCard For(GameFeat feat, string server, ClassTitles titles, long sequence) => feat.Kind == GameFeat.Boss
        ? Boss(feat, server, titles, sequence)
        : Pvp(feat, server, titles, sequence);

    private static BotCard Boss(GameFeat feat, string server, ClassTitles titles, long sequence)
    {
        var killer = feat.Players[0];
        var party = feat.Players.Skip(1).ToArray();
        var who = party.Length == 0 ? $"**{BotTexts.Escape(killer.Name)}**" : $"o grupo de **{BotTexts.Escape(killer.Name)}**";
        var fields = new List<BotCardField>
        {
            new("⚔️ Golpe final", Player(killer, titles), true),
            new("🗺️ Local", BotTexts.Escape(feat.Field), true),
            new("👹 Chefe", $"{BotTexts.Escape(feat.Target)} · nv. {feat.TargetLevel}", true)
        };
        if (party.Length > 0)
        {
            fields.Add(new BotCardField($"🛡️ Grupo ({feat.Players.Length})", List(party.Select(member => Player(member, titles))), false));
        }

        fields.Add(new BotCardField("💰 Drops", feat.Drops.Length == 0 ? "Nada caiu desta vez." : List(feat.Drops.Select(static drop => "• " + BotTexts.Escape(drop))), false));
        var line = string.Format(SiteText.Culture, BossLines[sequence % BossLines.Length], BotTexts.Escape(feat.Target), who);
        return new BotCard($"☠️ {feat.Target} foi derrotado!", line, fields, BossColor, Footer(server, "Chefes"));
    }

    private static BotCard Pvp(GameFeat feat, string server, ClassTitles titles, long sequence)
    {
        var killer = feat.Players[0];
        var victim = feat.Victim ?? killer;
        var line = string.Format(SiteText.Culture, PvpLines[sequence % PvpLines.Length], BotTexts.Escape(killer.Name), BotTexts.Escape(victim.Name));
        if (victim.Level - killer.Level >= UpsetLevels)
        {
            line += $"\n🔥 Uma vitória improvável: **{victim.Level - killer.Level} níveis** de diferença!";
        }

        var fields = new List<BotCardField>
        {
            new("🗡️ Vencedor", Player(killer, titles), true),
            new("💀 Derrotado", Player(victim, titles), true),
            new("🗺️ Local", BotTexts.Escape(feat.Field), true)
        };
        return new BotCard($"⚔️ {killer.Name} abateu {victim.Name}", line, fields, PvpColor, Footer(server, "PvP"));
    }

    // "Name\nClass title · nv. 120\n[Clan]".
    private static string Player(FeatPlayer player, ClassTitles titles)
    {
        var text = new StringBuilder().Append("**").Append(BotTexts.Escape(player.Name)).Append("**\n")
            .Append(titles.Of(player.Class, player.Tier)).Append(" · nv. ").Append(player.Level);
        if (player.Clan.Length > 0)
        {
            text.Append("\n🏰 ").Append(BotTexts.Escape(player.Clan));
        }

        return text.ToString();
    }

    private static string List(IEnumerable<string> lines) => BotTexts.Cut(string.Join("\n", lines), FieldLimit);

    private static string Footer(string server, string feed) => server.Length == 0 ? feed : server + " · " + feed;
}
