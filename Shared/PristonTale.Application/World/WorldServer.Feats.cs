using System.Globalization;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

// The fields' feats (a boss slain, a player killed by another) named in Portuguese for the public activity
// (ChatDirectory.RecordFeat, wired by the server's activity snapshot), on the world's tick.
public sealed partial class WorldServer
{
    private const string FeatLanguage = "pt-BR";
    private static readonly CultureInfo FeatCulture = CultureInfo.GetCultureInfo(FeatLanguage);

    public Action<GameFeat>? FeatRecorded { get; set; }

    private void RaiseFeat(FieldFeat feat)
    {
        if (FeatRecorded is not { } record)
        {
            return;
        }

        var data = _localizedData.Get(FeatLanguage, _worldData.Current);
        var players = new[] { feat.Killer }.Concat(feat.Party).Select(Describe).ToArray();
        var victim = feat.Victim is { } killed ? Describe(killed) : null;
        if (feat.Boss is { } boss)
        {
            var name = data.Monsters.TryGet(boss.Code, out var translated) ? translated.Name : boss.Name;
            var drops = feat.Drops.Where(static drop => drop.Kind != DropKind.Nothing).Select(drop => DropName(data, drop)).ToArray();
            record(new GameFeat(GameFeat.Boss, FieldName(feat.FieldId), name, boss.Combat.Level, players, null, drops));
        }
        else
        {
            record(new GameFeat(GameFeat.Pvp, FieldName(feat.FieldId), victim?.Name ?? "", victim?.Level ?? 0, players, victim, []));
        }
    }

    private static FeatPlayer Describe(WorldPlayer player) =>
        new(player.Character.Name, player.Level, player.Character.Class.Class, player.Character.Tier, player.Clan?.Name ?? "");

    private static string DropName(WorldData data, RolledDrop drop)
    {
        if (drop.Kind == DropKind.Gold)
        {
            return drop.Amount.ToString("N0", FeatCulture) + " de ouro";
        }

        var name = data.Items.TryGet(drop.ItemCode, out var item) ? item.Name : drop.ItemCode.ToString(CultureInfo.InvariantCulture);
        return drop.Amount > 1 ? name + " x" + drop.Amount.ToString(CultureInfo.InvariantCulture) : name;
    }
}
