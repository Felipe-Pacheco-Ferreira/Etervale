using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Duels;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.UI;

// Duels (DuelRules): the challenge box (the party request's art and place), the Duel button under a reached player's
// portrait, which sends "/duelo name" as the chat would, and while the local player duels a line at the top of the screen
// with the opponent and the time left, red with the seconds before it loses while it stands out of the arena.
public sealed partial class GameplayHud
{
    private const int DuelLineY = 96;
    private static readonly Color DuelColor = new(255, 220, 140);
    private static readonly Color DuelOutsideColor = new(255, 90, 70);
    private readonly HudRequestBox _duelRequest = new(HudPartyArt.Request, DuelRules.InviteMs);
    private DuelInfo _duelShown;
    private string _duelLine;
    private DuelRequestInfo _duelAsked;
    private double _duelAskedMs;

    // Answers challenges; set by the screen.
    public IDuelClient Duels { get; set; }

    // The local player's running duel, null for none.
    public DuelInfo Duel { get; private set; }

    public void ObserveDuel(PresenceUpdate update)
    {
        if (update.Duel is { } duel)
        {
            Duel = duel.Running ? duel : null;
        }

        if (update.DuelRequest is { } request)
        {
            (_duelAsked, _duelAskedMs) = (request, _clock.Elapsed.TotalMilliseconds);
        }
    }

    // A challenge that came while a service window was open waits for it to close, within its answer time; the box closes
    // when that time ends, as the server forgets the challenge then.
    private void UpdateDuelRequest()
    {
        var nowMs = _clock.Elapsed.TotalMilliseconds;
        if (_duelAsked is { } request && !ServiceWindowOpen(blockingOnly: false))
        {
            _duelAsked = null;
            if (nowMs - _duelAskedMs < DuelRules.InviteMs)
            {
                _duelRequest.Show(request.FromId, request.FromName, ClientText.Get("Duel.Request"), _duelAskedMs);
            }
        }

        _duelRequest.Update(nowMs);
    }

    private bool ClickDuel(HudLayout layout, Point pointer)
    {
        var from = _duelRequest.FromId;
        if (!TouchHudShown && _duelRequest.Click(pointer) is { } answer)
        {
            Duels?.AnswerDuelAsync(from, answer);
            return true;
        }

        return ShowsCanvasPlayerButtons && HudPlayerButtons.DuelButton(HudCombat.TargetFrame(layout)).Contains(pointer) && ChallengeReached();
    }

    // The Duel button, from the canvas or the touch HUD's row: false when the reached player's name is not known.
    private bool ChallengeReached()
    {
        if (Combat.PlayerName(ButtonsPlayerId) is not { } name)
        {
            return false;
        }

        _chat.Send?.Invoke("/duelo " + name, false);
        PlayerButtonsUsed?.Invoke();
        return true;
    }

    private bool CapturesDuel(HudLayout layout, Point pointer) =>
        !TouchHudShown && _duelRequest.Captures(pointer) || ShowsCanvasPlayerButtons && HudPlayerButtons.DuelButton(HudCombat.TargetFrame(layout)).Contains(pointer);

    private void DrawDuel(HudLayout layout)
    {
        if (Duel is not { } duel)
        {
            return;
        }

        // Formatted once per shown second, not every frame.
        if (!ReferenceEquals(duel, _duelShown))
        {
            _duelShown = duel;
            _duelLine = duel.OutsideSecondsLeft > 0
                ? ClientText.Format("Duel.Outside", duel.OpponentName, duel.OutsideSecondsLeft)
                : ClientText.Format("Duel.Status", duel.OpponentName, duel.SecondsLeft / 60, (duel.SecondsLeft % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        }

        _painter.TextCentered(_duelLine, 0, layout.Width, DuelLineY, duel.OutsideSecondsLeft > 0 ? DuelOutsideColor : DuelColor);
    }
}
