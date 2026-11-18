using System.Globalization;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Chat;

// The chat on the server. A spoken line reaches the players near the speaker with the speaker's name in front; lines
// that start with a slash are commands: whispers, the trade and clan channels, whisper refusal, the boss minute, a party
// request by name, then the game-master commands. A slash line is never spoken, nor is a yahoo word, which cheers instead. Runs on the caller's thread; positions come from the tick.
public sealed class ChatAppService
{
    private static readonly GameText WhisperRefused = GameText.Of(GameTextId.WhisperUnable);
    private readonly IPartyRoster _parties;
    private readonly IClanRoster _clans;

    public ChatAppService(ChatDirectory directory, WorldServer world, GameMasterCommands gameMasters, IPartyRoster? parties = null, IClanRoster? clans = null)
    {
        Directory = directory;
        World = world;
        GameMasters = gameMasters;
        _parties = parties ?? world.Parties;
        _clans = clans ?? NoClans.Instance;
    }

    public ChatDirectory Directory { get; }
    public WorldServer World { get; }
    public GameMasterCommands GameMasters { get; }
    public Func<long> NowMs { get; init; } = static () => Environment.TickCount64;

    public Support.SupportTickets? Tickets { get; init; }

    // Made on the first ticket command, once Tickets and NowMs are set.
    private TicketChat? _ticketChat;

    private async Task<bool> HandleTicketAsync(ChatMember member, string text)
    {
        await LazyInitializer.EnsureInitialized(ref _ticketChat, () => new TicketChat(Tickets, NowMs)).HandleAsync(member, text);
        return true;
    }

    public Func<long, Task<IReadOnlyList<string>>>? ReadDistributorAsync { get; init; }
    public Func<long, Task<IReadOnlyList<string>>>? ReadMailAsync { get; init; }
    public Func<long, long, int, Task<IReadOnlyList<string>>>? OpenMailAsync { get; init; }
    public Func<PlayerSession, long, Task<string>>? ClaimMailAsync { get; init; }
    public Action<string, string, string>? ObserveDeliveredWhisper { get; init; }
    public Action<string, string>? ObserveGroupChat { get; init; }

    // The client's flood rules, checked again on lines that reach other players. A party, trade or clan line counts
    // toward the rapid-line rule only, as the client counts its channel lines; commands and the yahoo word are free.
    private bool Floods(ChatMember member, string line, bool channel) =>
        member.Flood.Check(line, channel, whisper: false, NowMs()) != ChatFloodVerdict.Allowed;

    // A line typed in the chat input.
    public async Task SayAsync(ulong entityId, string text)
    {
        if (!Directory.TryGet(entityId, out var member))
        {
            return;
        }

        if (text.StartsWith("/voice ", StringComparison.Ordinal))
        {
            await RouteVoiceAsync(member, text[7..]);
            return;
        }

        text = ChatRules.WithoutRoute(ChatRules.ClipToCapacity(ChatCommandText.Clean(text)));
        if (text.Length == 0)
        {
            return;
        }

        if (YahooRules.IsWord(text))
        {
            try
            {
                await World.YahooAsync(entityId, key: false);
            }
            catch (WorldBusyException)
            {
                // A full tick queue drops the cheer, as it drops a spoken line.
            }

            return;
        }

        if (!ChatRules.IsCommand(text))
        {
            await SpeakAsync(member, text);
            return;
        }

        if (Simulation.Characters.HelpPets.Commands.Any(command => ChatCommandText.Match(command, text) > 0))
        {
            try
            {
                await World.ToggleHelpPetAsync(entityId);
            }
            catch (WorldBusyException)
            {
                // A full tick queue drops the command, as it drops a spoken line.
            }

            return;
        }

        if (!await PlayerCommandAsync(member, text))
        {
            await GameMasters.HandleAsync(member, text);
        }
    }

    // A party line: every other member of the speaker's party receives it.
    public Task PartyAsync(ulong entityId, string text)
    {
        if (!Directory.TryGet(entityId, out var member))
        {
            return Task.CompletedTask;
        }

        text = ChatRules.ClipToCapacity(ChatCommandText.Clean(text));
        if (text.Length == 0 || Floods(member, text, channel: true))
        {
            return Task.CompletedTask;
        }

        var line = new ChatLine(ChatKind.Party, entityId, ChatRules.Shorten(ChatRules.Compose(member.Name, text)));
        ObserveGroupChat?.Invoke("party", line.Text);
        foreach (var id in _parties.MembersOf(entityId))
        {
            if (id != entityId && Directory.TryGet(id, out var hearer))
            {
                hearer.Link.Deliver(line);
            }
        }

        return Task.CompletedTask;
    }

    // Called with each heartbeat: a standing notice the player has not received yet goes out now.
    private sealed class EventNoticeState
    {
        public int ExperienceEventPercent;
        public required WorldServerOptions Previous;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ChatMember, EventNoticeState> _eventNotices = new();

    public void Heartbeat(ulong entityId)
    {
        if (!Directory.TryGet(entityId, out var member))
        {
            return;
        }

        var options = World.Options;
        var state = _eventNotices.GetValue(member, _ => new EventNoticeState
        {
            ExperienceEventPercent = options.ExperienceEventPercent,
            Previous = options
        });
        if (state.ExperienceEventPercent != options.ExperienceEventPercent)
        {
            state.ExperienceEventPercent = options.ExperienceEventPercent;
            member.Link.Deliver(new ChatLine(ChatKind.CalendarEvent, 0, state.ExperienceEventPercent.ToString(CultureInfo.InvariantCulture)));
        }

        if (state.Previous.ExperienceMultiplier != options.ExperienceMultiplier)
        {

            member.Link.Deliver(new ChatLine(ChatKind.GlobalBuff, 0, options.ExperienceMultiplier.ToString(CultureInfo.InvariantCulture)));
        }

        if (state.Previous.CrystalEvent != options.CrystalEvent)
        {
            member.Link.Deliver(new ChatLine(ChatKind.CalendarEvent, 0, "crystals:" + options.CrystalEvent));
        }

        if (state.Previous.ItemEvents != options.ItemEvents)
        {
            member.Link.Deliver(new ChatLine(ChatKind.CalendarEvent, 0, "drops"));
        }

        if (state.Previous.Aging != options.Aging)
        {
            member.Link.Deliver(new ChatLine(ChatKind.GlobalBuff, 0, "aging"));
        }

        if (state.Previous.Mixing != options.Mixing)
        {
            member.Link.Deliver(new ChatLine(ChatKind.GlobalBuff, 0, "mixing"));
        }

        state.Previous = options;

        if (Directory.TryTakeNotice(member, out var text))
        {
            member.Link.Deliver(new ChatLine(ChatKind.Notice, 0, ChatRules.Notice(text)));
        }
    }

    private sealed class VoicePeer
    {
        public int Listening;
        public long NextFrame;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ChatMember, VoicePeer> _voicePeers = new();

    private async Task RouteVoiceAsync(ChatMember sender, string payload)
    {
        var peer = _voicePeers.GetValue(sender, static _ => new VoicePeer());
        if (payload.StartsWith("listen ", StringComparison.Ordinal))
        {
            if (int.TryParse(payload.AsSpan(7), out var mask))
            {
                Volatile.Write(ref peer.Listening, mask & VoiceFormat.ChannelsMask);
            }
            return;
        }

        var now = NowMs();
        if (payload.Length != 2 + ((VoiceFormat.FrameBytes + 2) / 3) * 4 ||
            payload[1] != ' ' || payload[0] is < '0' or > '3' || now < peer.NextFrame)
        {
            return;
        }

        peer.NextFrame = now + 15;
        var audio = new byte[VoiceFormat.FrameBytes];
        if (!Convert.TryFromBase64Chars(payload.AsSpan(2), audio, out var size) || size != audio.Length)
        {
            return;
        }

        var channel = (VoiceChannel)(payload[0] - '0');
        var clan = channel == VoiceChannel.Clan ? _clans.ClanOf(sender.EntityId) : 0;
        if (channel == VoiceChannel.Clan && clan == 0)
        {
            return;
        }

        try
        {
            IEnumerable<ulong> recipients = channel switch
            {
                VoiceChannel.Party => _parties.MembersOf(sender.EntityId).ToArray(),
                VoiceChannel.Clan => Directory.Members.Where(member => _clans.ClanOf(member.EntityId) == clan).Select(member => member.EntityId).ToArray(),
                VoiceChannel.Map => await World.VoiceMapPeersAsync(sender.EntityId),
                _ => await World.PlayersNearAsync(sender.EntityId, ChatRules.AreaRangeSquared)
            };

            var frame = new ChatLine(ChatKind.Voice, sender.EntityId, payload);
            foreach (var recipient in recipients)
            {
                if (recipient != sender.EntityId && Directory.TryGet(recipient, out var target) &&
                    _voicePeers.TryGetValue(target, out var listener) && (Volatile.Read(ref listener.Listening) & (1 << (int)channel)) != 0)
                {
                    target.Link.Deliver(frame);
                }
            }
        }
        catch (WorldBusyException)
        {
            // Drop the voice frame instead of delaying world updates.
        }
    }

    private async Task SpeakAsync(ChatMember member, string text)
    {
        if (Floods(member, text, channel: false))
        {
            return;
        }

        var line = new ChatLine(ChatKind.Player, member.EntityId, ChatRules.Shorten(ChatRules.Compose(member.Name, text)));
        ulong[] near;
        try
        {
            near = await World.PlayersNearAsync(member.EntityId, ChatRules.AreaRangeSquared);
        }
        catch (WorldBusyException)
        {
            return;
        }

        Directory.RecordLocalMessage(line.Text);
        foreach (var id in near)
        {
            if (Directory.TryGet(id, out var hearer))
            {
                hearer.Link.Deliver(line);
            }
        }
    }

    private Task<bool> PlayerCommandAsync(ChatMember member, string text)
    {
        if (text.Equals("/bonusglobais", StringComparison.OrdinalIgnoreCase))
        {
            if (!Floods(member, text, channel: true))
            {
                var world = World.Options;
                var values = FormattableString.Invariant($"GlobalBonus> {world.ExperienceMultiplier} {world.ExperienceEventPercent} {world.Aging.PricePercent} {world.Mixing.PricePercent}");
                member.Link.Deliver(ChatLine.Reply(values));
            }

            return Task.FromResult(true);
        }

        if (TicketChat.Handles(text))
        {
            return HandleTicketAsync(member, text);
        }

        const string claim = "/resgatarcorreio ";
        if (ClaimMailAsync is not null && text.StartsWith(claim, StringComparison.OrdinalIgnoreCase))
        {
            return ClaimMailRewardAsync(member, text[claim.Length..]);
        }

        if (ReadDistributorAsync is not null && text == "/distribuidor")
        {
            return ShowMailAsync(member, distributor: true);
        }

        if (ReadMailAsync is not null && string.Equals(text, "/correio", StringComparison.OrdinalIgnoreCase))
        {
            return ShowMailAsync(member);
        }

        if (OpenMailAsync is not null && text.StartsWith("/correio ", StringComparison.OrdinalIgnoreCase))
        {
            return ShowMailAsync(member, text[9..]);
        }

        if (ChatCommandText.Match("/bosstime", text) > 0)
        {
            member.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.BossTime, World.BossMinute.ToString("00", CultureInfo.InvariantCulture))));
            return Task.FromResult(true);
        }

        if (ChatCommandText.Match("/TRADE>", text) is var trade and > 0)
        {
            var message = ChatCommandText.Rest(text, trade);
            if (message.Length > 0 && !Floods(member, message, channel: true))
            {
                Directory.Broadcast(new ChatLine(ChatKind.Trade, 0, ChatRules.ClipToCapacity(ChatRules.Compose(member.Name, message))));
            }

            return Task.FromResult(true);
        }

        if (ChatRules.IsWhisper(text))
        {
            Whisper(member, text);
            return Task.FromResult(true);
        }

        if (ChatCommandText.Match("/disable_whisper", text) > 0)
        {
            member.WhisperRefused = true;
            member.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.WhispersDenied)));
            return Task.FromResult(true);
        }

        if (ChatCommandText.Match("/enable_whisper", text) > 0)
        {
            member.WhisperRefused = false;
            member.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.WhispersAccepted)));
            return Task.FromResult(true);
        }

        if (PartyRequestLength(text) is var party and > 0)
        {
            return InviteToPartyAsync(member, text, party);
        }

        var clan = _clans.ClanOf(member.EntityId);
        if (clan != 0 && ChatCommandText.Match("/CLAN>", text) is var clanLength and > 0)
        {
            var message = ChatCommandText.Rest(text, clanLength);
            if (message.Length > 0 && !Floods(member, message, channel: true))
            {
                var line = new ChatLine(ChatKind.Clan, 0, ChatRules.ClipToCapacity(member.Name + "> " + message));
                ObserveGroupChat?.Invoke("clan", line.Text);
                foreach (var other in Directory.Members)
                {
                    if (_clans.ClanOf(other.EntityId) == clan)
                    {
                        other.Link.Deliver(line);
                    }
                }
            }

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private async Task<bool> ClaimMailRewardAsync(ChatMember member, string argument)
    {
        if (long.TryParse(argument, out var id) && id > 0 && !Floods(member, "/resgatarcorreio", channel: true))
        {
            member.Link.Deliver(ChatLine.Reply($"Correio resgate> {id} {await ClaimMailAsync!(member.Session, id)}"));
        }

        return true;
    }

    private async Task<bool> ShowMailAsync(ChatMember member, string? arguments = null, bool distributor = false)
    {
        if (Floods(member, "/correio", channel: true))
        {
            return true;
        }

        IReadOnlyList<string> lines;
        if (arguments is null)
        {
            lines = await (distributor ? ReadDistributorAsync! : ReadMailAsync!)(member.AccountId);
        }
        else
        {
            var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var page = 1;
            if (parts.Length is < 1 or > 2 || !long.TryParse(parts[0], out var id) || id <= 0 ||
                (parts.Length == 2 && (!int.TryParse(parts[1], out page) || page is < 1 or > 20)))
            {
                return true;
            }

            lines = await OpenMailAsync!(member.AccountId, id, page);
        }

        foreach (var line in lines)
        {
            member.Link.Deliver(ChatLine.Reply(ChatRules.ClipToCapacity(line)));
        }

        return true;
    }

    private static int PartyRequestLength(string text) => Math.Max(ChatCommandText.Match("//party", text), ChatCommandText.Match("//PARTY", text));

    // "//party name": the original's party request, also what the button under a player's portrait sends.
    private async Task<bool> InviteToPartyAsync(ChatMember member, string text, int length)
    {
        var name = ChatCommandText.ReadWord(text, ref length);
        if (name.Length == 0)
        {
            return true;
        }

        try
        {
            Directory.Deliver(await World.InviteToPartyByNameAsync(member.Session, name));
        }
        catch (WorldBusyException)
        {
        }

        return true;
    }

    // "/:name text" or "/;name text": the sender sees "To> name: text", the receiver "From> sender: text". A receiver who
    // refuses whispers or ignores the sender, or a missing text, gets the sender a server line instead.
    private void Whisper(ChatMember sender, string text)
    {
        var index = 2;
        var name = ChatCommandText.ReadWord(text, ref index);
        var message = text[index..];
        var target = name.Length == 0 ? null : Directory.FindByName(name);
        if (target is null)
        {
            if (name.Length > 0)
            {
                sender.Link.Deliver(ChatLine.Reply(Simulation.Parties.PartyMessages.NotFound(name)));
            }

            return;
        }

        if (target.Contacts.Ignores(sender.Name))
        {
            sender.Link.Deliver(ChatLine.Reply(ContactMessages.WhisperRefused));
            return;
        }

        if (target.WhisperRefused)
        {
            sender.Link.Deliver(ChatLine.Reply(WhisperRefused));
            return;
        }

        var probe = 0;
        if (ChatCommandText.ReadWord(message, ref probe).Length == 0)
        {
            sender.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.PlayerOnline, name)));
            return;
        }

        sender.Link.Deliver(new ChatLine(ChatKind.Whisper, 0, ChatRules.ClipToCapacity("To> " + name + ": " + message)));
        target.Link.Deliver(new ChatLine(ChatKind.Whisper, 0, ChatRules.ClipToCapacity("From> " + sender.Name + ": " + message)));
        ObserveDeliveredWhisper?.Invoke(sender.Name, target.Name, message.Trim());
    }
}
