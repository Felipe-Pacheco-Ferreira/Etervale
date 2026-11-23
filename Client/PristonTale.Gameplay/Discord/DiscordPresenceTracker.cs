namespace PristonTale.Gameplay.Discord;

// Decides on the game thread when Discord needs a new activity: the player's status, the shown language, the switch
// or the application changed. The elapsed time starts when a character enters the world and restarts at every entry
// (a character switch passes through the menu). Nothing is allocated unless something changed.
public sealed class DiscordPresenceTracker
{
    private bool _started;
    private bool _enabled;
    private string _applicationId = string.Empty;
    private string _language = string.Empty;
    private PlayingStatus _status;
    private long _enteredUnixSeconds;

    // True when a request must go to Discord: an activity to show, or Activity null (presence switched off or no
    // application) to clear it.
    public bool TryChange(bool enabled, string applicationId, string language, PlayingStatus status, long nowUnixSeconds, out DiscordPresenceRequest request)
    {
        applicationId ??= string.Empty;
        enabled &= applicationId.Length > 0;
        if (status.InWorld && !_status.InWorld)
        {
            _enteredUnixSeconds = nowUnixSeconds;
        }

        var changed = !_started
            || enabled != _enabled
            || !string.Equals(applicationId, _applicationId, StringComparison.Ordinal)
            || !string.Equals(language, _language, StringComparison.Ordinal)
            || status != _status;
        var wasEnabled = _started && _enabled;
        _started = true;
        _enabled = enabled;
        _applicationId = applicationId;
        _language = language;
        _status = status;
        if (!changed || (!enabled && !wasEnabled))
        {
            request = null;
            return false;
        }

        request = enabled
            ? new DiscordPresenceRequest(applicationId, DiscordActivity.For(status, status.InWorld ? _enteredUnixSeconds : null))
            : DiscordPresenceRequest.Clear;
        return true;
    }
}

// Activity null with an empty application closes the connection, which clears the presence.
public sealed record DiscordPresenceRequest(string ApplicationId, DiscordActivity Activity)
{
    public static DiscordPresenceRequest Clear { get; } = new(string.Empty, null);
}
