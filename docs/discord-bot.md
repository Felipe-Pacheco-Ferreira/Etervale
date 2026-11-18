# Discord bot

The Discord bot of the Avalon server in `Bot/PristonTale.DiscordBot`: a .NET worker that runs on the game server's
machine, answers slash commands (worlds, rankings, events, news, game-master commands), relays the game's chat to Discord
and Discord's public channel to the game, announces worlds going down or up, news and events, and posts support tickets
for game masters to answer. It reuses the website's library (`Web/PristonTale.Web`: `SiteData`, `ClassTitles`,
`SiteText`), the shared database through `PristonTale.Persistence.*`, the support tickets' storage (`SupportTickets`) and
the game server's activity snapshots, and talks to the game server through the server's bot link.

## Read this when

- You add or change a slash command, an announcement, a relay or the ticket flow of the bot.
- You change the game server's bot link (`BotLink:*`), the remote game-master commands or the activity snapshots.
- You set the bot up, run it or check it against a running server and site.

## Code map

| Path | Responsibility |
| --- | --- |
| `Bot/PristonTale.DiscordBot/Program.cs`, `BotCommandLine.cs` | Settings from `discord-bot-settings.json` and the command line, `--check`, the host |
| `BotSettings.cs` | Every setting, its default and the checks that stop the start |
| `BotServices.cs` | Database (`WebHosting.AddDatabase`), `SiteData`, `ClassTitles`, `SupportTickets`, the HTTP clients, `BotQueries` |
| `BotQueries.cs` | The answers of `/status`, `/ranking`, `/eventos`, `/noticias` and `/gm`, shared with `--check` |
| `BotTexts.cs` | Every Portuguese text, escaping of the game's words, packing into 2000-character messages |
| `SlashCommands.cs`, `GameMasterActions.cs` | The commands registered in the guild; `/gm` subcommands and the game line each runs; level from roles |
| `Watches.cs` | `WorldWatch` (down/up after two agreeing readings), `EventWatch` (events and boss calls starting), `NewsWatch` |
| `BotState.cs` | `discord-bot-state.json` beside the executable: relay positions, announced news, each ticket's post, thread and the messages already in it |
| `Sources/SiteClient.cs` | `/api/worlds`, each world's `/status`, `/api/client/news` |
| `Sources/GameLinkClient.cs` | The game server's bot link |
| `Sources/ActivityCursor.cs` | New lines of an activity snapshot, by run (`StreamId`) and `Sequence` |
| `DiscordBotService.cs`, `.Watch.cs`, `.Tickets.cs` | The Discord client: commands, Discord-to-game relay, the watch loop, tickets |
| `BotChecks.cs` | `--check`: one command's answer printed to the console without Discord |
| `Server/PristonTale.Server/Net/BotLinkService.cs`, `BotLinkSettings.cs` | The game server's bot link |
| `Shared/PristonTale.Application/GameMasters/RemoteGameMaster.cs`, `GameMasterCommands.HandleRemoteAsync` | Game-master commands from outside the world |
| `Shared/PristonTale.Application/Chat/ExternalChat.cs` | Lines from Discord on the game's trade channel |
| `Shared/PristonTale.Contracts/Social/BotLinkMessages.cs` | The bot link's paths and bodies |

## What it does

| Feature | Where it reads | Discord |
| --- | --- | --- |
| `/status` | `Site:BaseUrl` `/api/worlds`, then each world's `/status` (5 s timeout) | Each world, online or down, `players of capacity online`; the bot's custom status shows the total |
| `/ranking [classe]` | `SiteData.TopAsync` (the site's ranking: no game masters, no blocked accounts, cached 60 s) | Top 10 with class title, level and clan |
| `/eventos` | `SiteData.CalendarAsync` (settings table and boss hours, as `/eventos` on the site) | Running now, next 24 hours, next three boss calls |
| `/noticias` | `/api/client/news` | The three latest posts |
| `/gm <ação>` | The bot link | Ephemeral answer; see Game masters |
| Public chat | `Activity:PublicPath` (spoken `local`, `trade`, standing `notice` lines) | `Channels:PublicChat`, `**[Troca]** Name: text`, batched every `PollSeconds` |
| Private chat | `Activity:PrivatePath` (`whisper`, `party`, `clan`) | `Channels:PrivateChat`, a channel only game masters see |
| Discord to game | Messages in `Channels:PublicChat` | Sent to every player on the trade channel as `[Discord] Name: text` (`Watch:RelayToGame`); a ⚠️ reaction when the server refused or did not answer |
| Announcements | Worlds every `StatusSeconds`, events and boss calls every `StatusSeconds`, news every `NewsSeconds` | `Channels:Announcements` |
| Tickets | `Tickets:Path` (the same folder as the site's `Site:SupportTicketsPath` and the server's `SupportTickets:Path`) | The channel of its category (`Discord:TicketChannels`, else `Channels:Tickets`): a post per ticket and a thread with the conversation ([Support tickets](#support-tickets)) |

Rules:

- Every message is sent with mentions off, and the game's words are escaped, so a line from the game never pings or
  formats.
- The first activity snapshot after the bot starts is backlog and is skipped; a snapshot of a new server run
  (`StreamId`) is read from its first line. Positions are kept in the state file, so a restarted bot neither repeats nor
  loses lines. The server keeps the last 100 lines of each snapshot and writes it every second: more than 100 lines in
  one second lose the oldest. The bot leaves out the trade lines it sent (`ExternalChat.IsFrom`).
- A Discord message for the game becomes one line: line breaks turn into spaces, the author is cut to 24 characters and
  the text to the chat input's 78 (`ExternalChat`). The game draws only its own font's characters; others show as boxes.
- Each Discord author passes the players' flood rules before a line reaches the game; a refused line gets the ⚠️ reaction.
  Discord's slowmode on the public channel keeps the channel itself calm.
- A send to Discord that fails (an outage, a deleted post) is tried again: chat positions and announced news are saved
  only after Discord took the messages, and one ticket that cannot be posted or updated does not hold the others; a
  ticket whose post was deleted is posted again while open.
- World announcements need two readings in a row; the first reading after the start, events already running and the
  news already published when the state file has none are not announced.

## Support tickets

A ticket is a conversation (`SupportTickets`, [chat-and-gm.md](chat-and-gm.md#support-tickets)). The bot follows every
ticket in the queue every `PollSeconds`:

- A ticket not closed and not followed yet is posted in its category's channel: number, category, subject, who opened
  it (account and character), account id, state and first message, with **Fechar ticket**. A thread `#1234 Subject`
  opens under the post (archived after a week without messages); the attachment goes to the thread when within 10 MB.
- Every later message, from the game, the site or the site's answer page, is posted in the thread once
  (`TicketPost.Posted` keeps each message's time and author).
- A game master's message in the thread becomes a staff message of the ticket under the member's display name (✅);
  ⛔ for a member below `Tickets:MinGmLevel`, ⚠️ for a closed ticket. The bot does not post it back.
- **Fechar ticket** closes it as the staff; the post shows the state and **Transcript** (the conversation as a `.txt`
  only the clicking member sees), **Reabrir** (open again, for the player too) and **Deletar** (removes the post and
  thread from Discord; the ticket stays closed in the player's history and is not posted again). Until deleted, a closed
  ticket keeps its post and its thread, locked. A ticket the player cancels shows the same way.
- The post's header follows the ticket: an edit by the player, an answer, a closing or reopening change it in place.
- Ticket actions are recorded in `gm_audit` as `discord: ticket reply|close|reopen|delete <id>` under `discord:<member id>`
  and need `Tickets:MinGmLevel`.

## Game masters

A member's game-master level is the highest level among its roles in `Discord:GameMasterRoles` (role id to level 1-4);
`/gm` is also hidden from members without Manage Messages. The bot sends the member's level with each command, and the
game server caps it at `BotLink:MaxGmLevel` (Moderator by default) before `GameMasterCommands` checks the level the
command needs. Every attempt is written to `gm_audit` with account 0, the user id `discord:<member id>` and the member's
display name.

| `/gm` | Game line | Level |
| --- | --- | --- |
| `aviso texto` | `/>texto` | Moderator |
| `aviso-fixo texto` | `/>>texto` | Moderator |
| `kick personagem` | `/kick personagem` | Moderator |
| `personagem nome` | `/username nome` | Guide |
| `conta conta` | `/userid conta` | Guide |
| `limite [numero]` | `/user_limit [n]` | Moderator |
| `bloqueios` | `/show_blackuser` | Guide |
| `liberar conta` | `/free_blackuser conta` | Guide |
| `limpar-bloqueios` | `/reset_blackuser` | Guide |
| `hora` | `/localtime` | Guide |
| `boss` | `/get_bosstime` | Guide |
| `desligar` | `/shutdown` | Moderator |

Only commands marked `Remote` in `GameMasterCommands.Table` run from the bot; the others need the issuer's character in
the world and answer "needs a character" (`GmCommandOutcome.NeedsWorld`). The replies are the game's English lines.

## Game server's bot link

Off unless `BotLink:Enabled`. `BotLinkService` starts a Kestrel listener of its own (`WebApplication.CreateEmptyBuilder`,
no configuration source) at `BotLink:Url`, which must be `http://` on a loopback address. Every request carries
`Authorization: Bearer <BotLink:Key>` (at least 32 characters, compared in constant time), else 401; only `POST` is
served (405), bodies are at most 4 KB.

| Path | Body | Answer |
| --- | --- | --- |
| `POST /bot/chat` | `{ "author", "text" }` | 204 after `ExternalChat.Relay`; 400 when nothing printable is left; 429 when the author broke the players' flood rules (`ExternalChatFlood`: the same line within 5 s, or more than 10 lines less than 2 s apart freeze the author for 7 s) |
| `POST /bot/command` | `{ "userId", "name", "level", "line" }` | `{ "outcome": "Unknown" \| "NeedsWorld" \| "Denied" \| "Done", "replies": [...] }` |

| Server setting | Default | Meaning |
| --- | --- | --- |
| `BotLink:Enabled` | false | Starts the link |
| `BotLink:Url` | `http://127.0.0.1:10011` | Loopback listen address |
| `BotLink:Key` | empty | The shared secret; the bot's `GameLink:Key` |
| `BotLink:MaxGmLevel` | 2 | Highest level a bot request acts with |
| `PublicActivity:Path`, `PublicActivity:WhisperPath` | empty | The activity snapshots the bot relays ([website.md](website.md)) |
| `SupportTickets:Path` | empty | The tickets' folder, shared with the site and the bot |

## Settings

`discord-bot-settings.json` beside the executable (copy `discord-bot-settings.example.json`), or another file with
`--settings <file>`; any value can be given on the command line as `--Section:Key=value`. The bot reads no environment
variable. Keep the token and the link key in that file on the server only: it is ignored by git and must never be
committed or pasted anywhere.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Discord:Token` | empty | The bot's token (Discord Developer Portal, Bot) |
| `Discord:GuildId` | 0 | The Discord server where the commands are registered |
| `Discord:Channels:Announcements`, `PublicChat`, `PrivateChat`, `Tickets` | 0 | Channel ids; 0 turns that feature off |
| `Discord:TicketChannels` | `{}` | Ticket category (`Bug`, `Melhoria`, `Outro`) to its channel id; a category without one goes to `Channels:Tickets` |
| `Discord:GameMasterRoles` | `{}` | Role id to game-master level (1-4) |
| `Site:BaseUrl` | `https://localhost:6443` | The website (https) |
| `Database:Provider`, `SqlitePath`, `Postgres`, `MaxPoolSize` | Sqlite, `data/pristontale.db` | The game server's database, as the site's `Database:*`; the SQLite file must exist |
| `GameLink:Url`, `GameLink:Key` | empty | The server's `BotLink:Url` and `BotLink:Key`; empty turns off `/gm` and Discord to game |
| `Activity:PublicPath`, `Activity:PrivatePath` | empty | The server's `PublicActivity:Path` and `PublicActivity:WhisperPath` |
| `Tickets:Path`, `Tickets:MinGmLevel` | empty, 1 | The tickets' folder; the level that may answer, close, reopen and delete |
| `Watch:PollSeconds`, `StatusSeconds`, `NewsSeconds` | 5, 30, 120 | Loop periods (at least 1, 5 and 30) |
| `Watch:RelayToGame` | true | Discord's public channel goes to the game |
| `Logging:LogLevel:Default` | Information | Console log level |

Relative paths resolve beside the bot's executable, not the server's or the site's: give the activity and ticket paths as absolute
paths (the same files the server writes). The SQLite path resolves like the site's (`DataPath.Resolve`).

## Set up and run

1. Discord Developer Portal: create an application and its bot, copy the token, and turn on the **Message Content**
   intent (needed to relay the public channel to the game). Invite the bot with the scopes `bot` and
   `applications.commands` and the permissions View Channels, Send Messages, Attach Files, Add Reactions, Read Message
   History, Create Public Threads, Send Messages in Threads and Manage Threads (permissions integer `326417615936`).
2. Create the channels; make `PrivateChat` and `Tickets` visible to game masters only. Copy the channel, role and
   server ids (Discord's developer mode, right click, Copy ID).
3. Game server settings: `BotLink:Enabled=true`, a random `BotLink:Key` of 32 or more characters, and the activity and
   tickets paths shared with the site.
4. Build and configure the bot, from the repository root:

```bash
dotnet build Bot/PristonTale.DiscordBot -c Release -m:1 -warnaserror
```

   Copy `Bot/PristonTale.DiscordBot/bin/Release/net10.0/discord-bot-settings.example.json` to
   `discord-bot-settings.json` in the same folder and fill it in.
5. Check each answer without Discord, then run it (the same commands on Windows, Linux and macOS):

```bash
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check status
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check ranking magician
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check eventos
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check noticias
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check tickets
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check chat
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build -- --check gm "/localtime" 2
dotnet run --project Bot/PristonTale.DiscordBot -c Release --no-build
```

   `--check gm` runs a real game-master command as `discord:check` at the level given (Moderator by default); it is
   audited like any other. For a deployment, `dotnet publish Bot/PristonTale.DiscordBot -c Release -o <folder>` and run
   the executable there under the system's service manager.

The bot's folder also holds the website library's files (`wwwroot/`, `content/`, `web-settings.json`); the bot reads
only `content/class-titles.pt-BR.resx` of them.

## How to

- Add a slash command: a constant and a builder in `SlashCommands`, a case in `DiscordBotService.HandleCommandAsync`
  and `BotChecks`, its answer in `BotQueries`/`BotTexts`, a case in `DiscordBotTests` and a row in What it does.
- Add a `/gm` action: mark the game command `Remote` in `GameMasterCommands.Table` only when it uses nothing of the
  issuer's character, add a row to `GameMasterActions.All` and to the table above; `DiscordBotTests` checks every action
  runs a remote command.
- Relay another chat channel: record it in the server's activity snapshot (`ChatDirectory`, `WhisperActivity`) and name
  it in `BotTexts.ChannelName`.

## Pitfalls

- Without the Message Content intent Discord sends empty messages: nothing reaches the game.
- Slash commands are registered in `Discord:GuildId` when the bot connects; a changed command shows after Discord
  refreshes the client (seconds for a guild).
- The bot posts in the server's time zone; it must run on the game server's machine (or in its zone) like the site.
- A notice sent with `/>` reaches the players but is not in the activity snapshot; a standing notice (`/>>`) is.
- Deleting `discord-bot-state.json` posts every ticket not closed again (closed ones lose their post) and skips the chat
  backlog.

## Related

- [website.md](website.md): the site's APIs, `SiteData`, the activity snapshots and the tickets' folder.
- [server.md](server.md): the game server's settings and hosted services.
- [chat-and-gm.md](chat-and-gm.md): chat routing, game-master levels and the audit trail.
- [testing.md](testing.md): `DiscordBotTests`.
