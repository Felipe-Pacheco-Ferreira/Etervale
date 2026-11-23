using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI;

internal enum TouchRewardsAction
{
    Open,
    Claim,
    All
}

// The touch HUD's Eventos & Recompensas, drawn by the page as the Android client's (touch-rewards.js): the reward tracks
// as tabs with what each has ready, the time left and the progress, the track's entries and claiming one or all. The menu
// opens it in place of the canvas's window (OpenRewards counts the request); the page asks the server again while open.
public sealed partial class GameplayHud
{
    private const string TouchRewardsPrefix = "touch-rewards:";
    private int _touchRewardsRequests;

    internal string TouchRewards()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("requests", _touchRewardsRequests);
            json.WriteString("message", _rewardMessage);
            json.WriteBoolean("claiming", Environment.TickCount64 < _rewardDeadline);
            var elapsedSeconds = (long)((_clock.Elapsed.TotalMilliseconds - _rewardReceivedMs) / 1000);
            json.WriteStartArray("tracks");
            foreach (var track in _rewardBoard.Tracks)
            {
                json.WriteStartObject();
                json.WriteString("track", track.Track.ToString());
                json.WriteNumber("minLevel", track.MinLevel);
                // Counted down since the board arrived; NoDeadline stays as it is.
                json.WriteNumber("secondsLeft", track.SecondsLeft == RewardTrackView.NoDeadline ? RewardTrackView.NoDeadline : Math.Max(0, track.SecondsLeft - elapsedSeconds));
                json.WriteNumber("ready", track.Ready);
                json.WriteNumber("reached", track.Reached);
                json.WriteStartArray("entries");
                foreach (var entry in track.Entries)
                {
                    json.WriteStartObject();
                    json.WriteString("goal", entry.Goal.ToString());
                    json.WriteString("state", entry.State.ToString());
                    json.WriteNumber("progress", entry.Progress);
                    json.WriteNumber("target", entry.Target);
                    json.WriteNumber("gold", entry.Gold);
                    json.WriteStartArray("items");
                    foreach (var item in entry.Items)
                    {
                        json.WriteStartObject();
                        json.WriteString("name", item.Name ?? "");
                        json.WriteNumber("count", item.Count);
                        json.WriteString("picture", _icons.TryPath(ItemCodes.KeyOf(item.Code), out var sprite) ? TouchPicturePath(sprite) : "");
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // "touch-rewards:open:<track>" (ask for the board again), "touch-rewards:claim:<track>:<entry>" and
    // "touch-rewards:all:<track>"; the track by its name.
    internal static bool TryReadRewardsAction(string action, out TouchRewardsAction kind, out RewardTrack track, out int index)
    {
        kind = default;
        track = default;
        index = 0;
        if (action is null || !action.StartsWith(TouchRewardsPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = action[TouchRewardsPrefix.Length..].Split(':');
        if (parts.Length < 2 || !TryParseName(parts[0], out kind) || !TryParseName(parts[1], out track))
        {
            return false;
        }

        return kind == TouchRewardsAction.Claim ? parts.Length == 3 && int.TryParse(parts[2], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out index) : parts.Length == 2;
    }

    private bool RunTouchRewards(string action)
    {
        if (!action.StartsWith(TouchRewardsPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadRewardsAction(action, out var kind, out var track, out var index))
        {
            return true;
        }

        var shown = _rewardBoard.Find(track);
        switch (kind)
        {
            case TouchRewardsAction.Open:
                _ = SendRewards?.Invoke(RewardEventsAction.Open, track, 0);
                break;
            case TouchRewardsAction.Claim when shown is not null && index < shown.Entries.Count && shown.Entries[index].State == RewardEntryState.Ready:
                ClaimRewards(RewardEventsAction.Claim, track, index);
                break;
            case TouchRewardsAction.All when shown is { Ready: > 0 }:
                ClaimRewards(RewardEventsAction.ClaimAll, track, 0);
                break;
        }

        return true;
    }
}
