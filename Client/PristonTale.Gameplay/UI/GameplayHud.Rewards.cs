using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    private RewardEventsInfo _rewardBoard = RewardEventsInfo.None;
    private bool _rewardsOpen;
    private string _rewardMessage = "";
    private long _rewardDeadline;
    internal Func<RewardEventsAction, RewardTrack, int, Task> SendRewards { get; set; }

    private static Rectangle RewardsBounds(HudLayout layout) =>
        layout.PlaceOverlay(new Point((layout.Width - 440) / 2, 110), 440, 310);

    private static Rectangle RewardRow(Rectangle box, int index) => new(box.X + 14, box.Y + 50 + index * 36, box.Width - 28, 31);
    private static Rectangle RewardClose(Rectangle box) => new(box.Right - 34, box.Y + 9, 24, 24);

    private void ObserveRewards(PresenceUpdate update)
    {
        if (update.Rewards is { } board)
        {
            _rewardBoard = board;
        }
        if (update.RewardResult is { } result)
        {
            _rewardDeadline = 0;
            _rewardMessage = ClientText.Format(result.Message);
        }
    }

    private void OpenRewards()
    {
        _rewardsOpen = true;
        _rewardMessage = "";
        _ = SendRewards?.Invoke(RewardEventsAction.Open, default, 0);
    }

    private bool CloseRewards()
    {
        var open = _rewardsOpen;
        _rewardsOpen = false;
        return open;
    }

    private bool RewardsCapture(HudLayout layout, Point point) => _rewardsOpen && RewardsBounds(layout).Contains(point);

    private bool ClickRewards(HudLayout layout, Point point)
    {
        if (!RewardsCapture(layout, point))
        {
            return false;
        }

        var box = RewardsBounds(layout);
        if (RewardClose(box).Contains(point))
        {
            return CloseRewards();
        }

        for (var index = 0; index < Math.Min(6, _rewardBoard.Tracks.Count); index++)
        {
            var track = _rewardBoard.Tracks[index];
            if (RewardRow(box, index).Contains(point) && track.Ready > 0 && Environment.TickCount64 >= _rewardDeadline)
            {
                _rewardDeadline = Environment.TickCount64 + 8000;
                _ = SendRewards?.Invoke(RewardEventsAction.ClaimAll, track.Track, 0);
                break;
            }
        }

        return true;
    }

    private void DrawRewards(HudLayout layout)
    {
        if (!_rewardsOpen)
        {
            return;
        }

        var box = RewardsBounds(layout);
        Panel(box, 25, 20);
        Panel(RewardClose(box), 55, 41);
        _painter.Text("X", box.Right - 27, box.Y + 13, Color.Wheat);
        _painter.Text(ClientText.Get("Rewards.Title"), box.X + 15, box.Y + 15, Color.Gold);
        for (var index = 0; index < Math.Min(6, _rewardBoard.Tracks.Count); index++)
        {
            var track = _rewardBoard.Tracks[index];
            var row = RewardRow(box, index);
            Panel(row, 42, 32);
            _painter.Text($"{track.Track}  {track.Reached}/{track.Entries.Count}", row.X + 8, row.Y + 8, Color.Wheat);
            if (track.Ready > 0)
            {
                var claim = ClientText.Format("Rewards.Claim", track.Ready);
                _painter.TextCentered(claim, row.Right - 113, row.Right - 5, row.Y + 8, Color.Gold);
            }
        }

        _painter.Text(_painter.Fit(_rewardMessage, box.Width - 28), box.X + 14, box.Bottom - 26, Color.Wheat);

        void Panel(Rectangle bounds, int red, int green)
        {
            _painter.ColorBox(bounds, 151, 111, 54, 255);
            bounds.Inflate(-1, -1);
            _painter.ColorBox(bounds, red, green, 15, 248);
        }
    }
}
