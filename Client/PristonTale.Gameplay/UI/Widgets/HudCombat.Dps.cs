using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Combat;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The party's damage per second on the target, under the target frame (ClientSettings.ShowPartyDps).
public sealed partial class HudCombat
{
    private static readonly Color DpsColor = new(255, 220, 140);
    private const int DpsGap = 4;
    private readonly PartyDpsMeter _dps = new();
    private int _dpsValue = -1;
    private string _dpsText = "";

    private void ObserveDps(CombatEventInfo combat)
    {
        if (combat.Kind == CombatEventKind.Hit && _self != 0 && (combat.AttackerId == _self || InParty(combat.AttackerId)))
        {
            _dps.Record(combat.TargetId, combat.Damage, Environment.TickCount64);
        }
    }

    private void DrawDps(HudPainter painter, Rectangle box, ulong target)
    {
        var dps = _dps.Dps(target, Environment.TickCount64);
        if (dps <= 0)
        {
            return;
        }

        if (dps != _dpsValue)
        {
            _dpsValue = dps;
            _dpsText = "DPS " + dps.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        }

        // Beside the frame: under it lie the life bar and, for a player, its buttons.
        painter.Text(_dpsText, box.Right + DpsGap, box.Y + (box.Height - painter.TextHeight) / 2, DpsColor);
    }
}
