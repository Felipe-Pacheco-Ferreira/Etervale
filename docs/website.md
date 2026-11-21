# Website

The public website of the game server in `Web/PristonTale.Web`: an ASP.NET Core Razor Pages application, served only
over HTTPS, in Brazilian Portuguese, with the Android HUD's look ([hud-design.md](hud-design.md)). It shares the game
server's database and the account, calendar and ranking code of `Shared/PristonTale.Application` and `Persistence/`.

## Read this when

- You change a page of the site, its look or its texts.
- You change how accounts register or sign in, or a security measure of the site (headers, cookies, limits).
- You run the site locally, deploy it, publish a news post or a new Android package.
- You change a contract the site reads: the accounts or characters tables, the settings table, the event calendar.

## Code map

| Path | Responsibility |
| --- | --- |
| [Program.cs](../Web/PristonTale.Web/Program.cs) | Builds the host from `web-settings.json` beside the executable and the command line only, then `AddPristonWeb` / `UsePristonWeb` |
| `Hosting/WebHosting.cs` | Every service registration and the request pipeline: database, data protection keys, cookie authentication, anti-forgery, rate limits, HSTS, HTTPS redirection, forwarded headers, static files, the Android download endpoint |
| `Hosting/WebSettings.cs` | The settings records, their defaults and validation (a value out of range stops the start) |
| `Hosting/SecurityHeaders.cs` | The headers every response carries (CSP, X-Frame-Options, ...), and the play page's own policy |
| `Hosting/AccountSession.cs` | The session cookie: claims, options, and the check of each request against the stored account |
| `Services/ServerStatusMonitor.cs` | Background TCP probe of the game server; pages read its last result |
| `Services/SiteData.cs` | Cached reads for the pages: totals, rankings, the settings table, the event calendar |
| `Services/SiteCalendar.cs` | The calendar as the site shows it: events, the next boss calls, each boss's hours |
| `Services/ClassTitles.cs` | Class titles in Portuguese from the client's `DataText.pt-BR.resx`, copied as `content/class-titles.pt-BR.resx` |
| `Services/NewsFeed.cs` | The posts of `content/news.json`, read again when the file changes |
| `Services/AndroidPackage.cs` | The APK of `Downloads:AndroidApkPath` with its size and SHA-256 |
| `Services/BrowserClient.cs` | The published browser client of `Play:ClientPath`, served under `/play`, and whether it is offered ([web-client.md](web-client.md)) |
| `Services/RegistrationGate.cs` | Registrations per address and window |
| `Services/SiteText.cs` | Portuguese words for events and drops, number, date and time formats, the level medallion's stone |
| `Pages/` | `Jogar` (the entry, `/`), `Index` (the portal, `/inicio`), `Noticias`, `Download`, `Rankings`, `Eventos`, `Entrar`, `Cadastro`, `Conta`, `Sair`, `Erro`, and `Shared/_Layout` |
| `wwwroot/` | `css/site.css` (the portal), `css/play.css` (the entry page), `css/settings.css` (its Settings panel) and the approved Noxius Tale images (`img/`) |
| `content/news.json` | The news posts |
| `content/termos.txt` | The rules and terms of use the registration shows and asks to accept (`Services/TermsOfUse.cs`): one block per line, the title, the game's name, the notice starting with "LEITURA IMPORTANTE.", numbered section headings in capitals and the clauses |
| `web-settings.json` | Settings beside the executable |

Shared code it uses: `AccountAppService` (credential check and registration), `LoginThrottle`, `AccountRules`
(Contracts), `EventCalendarRules` and `EventSchedule`/`CalendarPages` (calendar), `ServerSettingCatalog.Read` (the
settings table as the server reads it), `IRankingRepository`, `IAccountRepository`, `ICharacterRepository`,
`IClanRepository`.

## Pages

| Address | Page | Reads |
| --- | --- | --- |
| `/` | The game itself, filling the window (`Jogar`, no site layout). The page shows loading progress while the browser client starts, then plays the live opening flight with the login panel and a bar for language, site menu and full screen. "Em breve" while the client or its endpoint is missing ([web-client.md](web-client.md#entry-page)). | `Play:*` |
| `/inicio` | The portal: hero art, server status, accounts and characters, experience rate, Android version, the latest news, the next events and the top five | Everything below |
| /api/client/news | Latest 20 news posts as JSON for the game client (date, title, summary, body, author, kind) | content/news.json |
| `/api/worlds` | The worlds the entry page offers after the login: name, region, kind (`Normal`/`AntiKs`), `endpoint` (wss) and `status` (the edge's `https://host:port/status`) | `Play:Worlds` (or `Play:GameEndpoint` alone as the world "Noxius") |
| `/manifest.webmanifest` | The web app manifest: "Adicionar à tela inicial" opens `/` full screen and in landscape, with `img/icon-192.png` and `img/icon-512.png` (`WebAppManifest`) | `Site:Name` |
| `/api/play-session` | GET identifies the remembered browser account. Same-origin HTTPS POST verifies credentials and sets a separate encrypted, HTTP-only cookie for 14 days. DELETE forgets the account in this browser. | `BrowserSessionApi`, `AccountSession` |
| `POST /api/play-session/ticket` | Exchanges a valid browser cookie for a single-use game login ticket that expires after 45 seconds. The website and accepting game world must share the account database. | `BrowserSessionApi`, `browser_login_tickets` |
| `POST /api/play-session/attachment/<ticket>` | Same-origin multipart upload of one PNG, JPEG, WebP, MP4 or WebM file of up to 20 MB to a ticket of the play session's account, one per ticket; the game's News & Support window sends it. | `BrowserSessionApi`, `SupportTickets` |
| `POST /api/conta` | The entry page's "Criar conta" (`Hosting/RegistrationApi.cs`): JSON from the site's own origin (else 403) with account, e-mail, password and its confirmation, optional referral code, accepted terms and the Turnstile token when configured; answers `{ ok, field, message }`. Counts against `FormPostsPerMinute` and `RegistrationsPerIp`; the account keeps its e-mail (lower case, one per account) and referral code (migration 44) | `AccountAppService.RegisterAsync`, `content/termos.txt` |
| /noticias | Every news post, newest first | `content/news.json` |
| `/download` | The APK's version, size, date and SHA-256, the download button and how to install | `Downloads:*` |
| `/download/android` | The APK itself (`application/vnd.android.package-archive`, range requests) | `Downloads:AndroidApkPath` |
| `/jogar` | Permanent redirect to `/`, the former address of the play page | |
| `/play/*` | The browser client's files, including `main.js`, `_framework/`, the assets listed by `bootstrap.txt`, and the sounds listed by `audio.txt` (`.wav`, and the music's `.bgm` as `application/octet-stream`) served on demand. Hashed files are cached for a year and the loader and entry script are revalidated. | `Play:ClientPath` |
| `/rankings`, `/rankings?classe=<class>` | The 50 highest characters, of all classes or one (`ClassTitles.Slug`, the `PlayerClass` name in lower case), then "Arena da semana": the current week's 10 best of each arena level band with their points, wins and losses (`SiteData.ArenaAsync`, the week in the site's time zone, which is the game server's; [application.md](application.md#arena)) | `IRankingRepository.TopAsync`, `IArenaRepository.TopAsync` |
| `/eventos` | Events running now, the next 24 hours, the field bosses (next calls and each boss's hours) and the next days | `SiteData.CalendarAsync` |
| `/cadastro` | Registration; signs the new account in | `AccountAppService.RegisterAsync` |
| `/entrar` | Sign-in with the game's account and password; `?voltar=` returns to a local address only | `AccountAppService.CheckAsync` |
| `/conta` | The signed-in account's characters: class title, level medallion, experience to the next level, clan, field, gold, last play | `ICharacterRepository`, `IClanRepository` |
| `/sair` | Sign-out (a form post) | |
| `/erro/{code}` | Error and status pages (404, 400 expired form, 429, 500) | |

On the entry page, "Esqueci o ID" and "Esqueci a senha" open dialogs over the login. The forms display an unavailable
message on submission until email delivery is connected to the recovery flow.

Public browser BMP, TGA, SMD, SMB and XNB files negotiate Brotli or gzip without changing their decoded bytes or URLs.
An optional `browser-compressed-assets.json` in `Play:ClientPath` indexes precompressed `.br` and `.gz` sidecars.
The site uses a sidecar only when the request's `v` equals its indexed raw SHA-256; responses keep the original MIME
type, vary by `Accept-Encoding` and use representation-specific ETags. HEAD and conditional requests use the same
representation. Range requests use the original file. Missing variants fall back to the normal static file pipeline,
which keeps the original bytes, validators and range handling without dynamic re-encoding. No compressed variants are
applied to account or support APIs. The index is read once when the site starts; publish the complete verified sidecar
set and index before starting that site instance. Variant size is checked per request; decoded-byte and compressed-SHA
integrity is verified by the offline generator and immutable staged-file inventory rather than hashing files on every request.
When accepted encodings have equal quality, the smaller indexed representation is selected.

Fingerprinted browser runtime files (`/play/_framework/*` except the `dotnet.js` loader) use the `.br` and `.gz` the
browser publish writes beside them, with the same negotiation, the original MIME type (`application/wasm` keeps
streaming compilation), `Vary: Accept-Encoding`, the immutable cache and a validator made of the file name and encoding.
A fingerprinted name never changes content, so its sidecars need no index. Ranges and missing sidecars get the raw file.

Data shown and its sources:

- Rankings leave out the characters of game masters (`gm_level > 0`) and of blocked accounts; ties go to experience,
  then to the older character. Lists and totals are cached for 60 s.
- The calendar is `EventSchedule` over the settings table (`ServerSettingCatalog.Read`: Bellatra, crystal, experience
  and item events, castle mode and siege day and hour) and the catalog's boss hours (`GetAllFieldSpawnsAsync`, monster
  names in `pt-BR`), cached for 60 s (the bosses for an hour). Today's boss minute is drawn by the game server and not
  stored, so the site shows each call's hour only (`BossMinuteKnown = false`); a siege running now is not stored either,
  so the site shows the scheduled one. Hours are in the site's time zone, which must be the game server's.
- Field names come from `WorldCatalog.FieldName` (English); class titles from the client's Portuguese texts.
- Server status: a TCP connect to `GameServer:Host`/`Port` every `ProbeSeconds` (3 s timeout), closed before any TLS
  handshake; the state and since when.
- The site writes only new accounts (`accounts`). It never migrates or creates a database: a missing SQLite file stops
  the start, so a wrong path cannot leave the site on an empty database.

## Settings

`web-settings.json` beside the executable, then the command line (`--Section:Key=value`). Environment variables are not
read (AGENTS.md): the host's configuration sources are cleared and replaced by these two, and the environment is always
`Production`. Relative paths of `Site:KeysPath` and `Downloads:AndroidApkPath` resolve against the executable's
directory; `Database:SqlitePath` resolves like the server's (`DataPath.Resolve`: the solution root when the site runs
from a build of the repository, else the executable's directory).

| Key | Default | Meaning |
| --- | --- | --- |
| `Site:Name` | `Noxius` | Name in titles and accessible site text; header and footer use the Noxius Tale wordmark. |
| `Site:HttpsPort` | 443 (5443 in the file) | Port the HTTP endpoint redirects to. |
| `Site:HstsDays` | 365 | `Strict-Transport-Security` max-age, with subdomains. Not sent to `localhost`. |
| `Site:KeysPath` | `keys` | Directory of the data protection keys that encrypt the session and form cookies. |
| `Site:KnownProxies` | `[]` | Addresses of reverse proxies whose `X-Forwarded-For`/`-Proto` are believed (loopback always is). |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres`. |
| `Database:SqlitePath` | `data/pristontale.db` | The game server's SQLite file; must exist. |
| `Database:Postgres` | empty | Connection string for `Postgres`. |
| `Database:MaxPoolSize` | 10 | PostgreSQL connections at most. |
| `GameServer:Host`, `Port` | `127.0.0.1`, 10009 | Where the status probe connects. |
| `GameServer:ProbeSeconds` | 30 | Probe period, at least 5. |
| `Downloads:AndroidApkPath` | `downloads/priston-avalon.apk` | The package offered; the page says "em breve" while it is missing. |
| `Downloads:AndroidVersion` | empty (`0.57` in the file) | Shown on the pages and in the download's file name. |
| `Play:ClientPath` | `play` | Folder of the published browser client, relative to the executable (created when missing). |
| `Play:GameEndpoint` | empty | The game server's WebSocket edge, `wss://` only (`wss://host:port/game`; the page is HTTPS, where browsers refuse plain `ws`); empty keeps `/` on "em breve". It and each world's endpoint (with the same host and port over `https`, for `/status`) are the only origins the play page may connect to. |
| `Play:Worlds` | `[]` | The worlds of "Escolha o mundo": `Name`, `Region` (default `Brasil`), `Kind` (`Normal` or `AntiKs`) and `Endpoint` (`wss://`, required); empty offers `GameEndpoint` as one world named "Noxius". |
| `Security:LoginFailuresPerAccount`, `LoginFailuresPerIp`, `LoginFailureWindowSeconds` | 5, 20, 300 | `LoginThrottle` limits of the sign-in form. |
| `Security:RegistrationsPerIp`, `RegistrationWindowMinutes` | 3, 60 | Registrations one address may attempt per window. |
| `Security:RequestsPerMinute` | 120 | Requests per address and minute, any page (static files excluded). |
| `Security:FormPostsPerMinute` | 10 | Posts of the sign-in and registration forms per address and minute. |
| `Security:SessionMinutes` | 120 | Session lifetime, sliding; 5 to 10080. |
| `Security:TurnstileSiteKey`, `Security:TurnstileSecret` | empty | Cloudflare Turnstile on "Criar conta": the widget shows and `/api/conta` checks its token with Cloudflare only when both are set; the play page then also allows `https://challenges.cloudflare.com` for scripts and frames. Keep the secret in the settings file beside the executable. |
| `Kestrel:Endpoints:*` | `http://localhost:5080`, `https://localhost:5443` | Standard Kestrel endpoints; give the HTTPS one a `Certificate` (`Path`, `Password`) in production. |
| `Kestrel:Limits:MaxRequestBodySize` | 65536 | Largest request body. |
| `Logging:LogLevel:*` | `Information` | Standard .NET logging filter. |

## Security measures

- **HTTPS only.** Plain HTTP answers `308` to the HTTPS port; `Strict-Transport-Security` (one year, subdomains) on
  every HTTPS response outside `localhost`; the CSP adds `upgrade-insecure-requests`. Kestrel sends no `Server` header.
- **Passwords.** Hashed with the game's `PasswordHasher` (PBKDF2-SHA256, 100 000 iterations, 16-byte salt) by
  `AccountAppService.RegisterAsync`; verified in constant time by `CheckAsync`, which also verifies a hash for unknown
  accounts so timing does not tell which names exist. The password is checked before the account's state. Password
  fields never echo a value.
- **Account rules.** `AccountRules`: names of 4 to 16 ASCII letters and digits from a letter, unique in any letter case;
  passwords of 8 to 64 characters, no control characters, within the login message's 128 UTF-8 bytes, not the name.
- **Sessions.** One encrypted, HTTP-only, `Secure`, `SameSite=Strict` cookie (`__Host-pa-session`) with the account's
  id, name and a stamp (16 bytes of the SHA-256 of the stored hash, never the hash); 120 minutes sliding. Every request
  checks it against the stored account (`AccountSession.Matches`): a blocked account, a new password or another account
  ends the session. Sign-out is a form post.
- **CSRF.** Razor Pages validate the anti-forgery token on every post; its cookie (`__Host-pa-form`) is HTTP-only,
  `Secure`, `SameSite=Strict`. A post without a valid token answers 400.
- **Limits.** `LoginThrottle` counts every sign-in attempt before the hash is computed (posts sent at once cannot all
  pass before the first is counted) and refuses after 5 attempts on an account or 20 from an address in 5 minutes; a
  success clears the account's count. 10 form posts per address and minute; 3 registrations per address and hour; 120
  requests per address and minute; a hidden field turns away form-filling bots; request bodies of at most 64 KiB. A
  refused request answers 429. An "address" is an IPv4 address or an IPv6 /64 network (`WebHosting.LimitKey`), since one
  subscriber usually holds a whole /64.
- **Output encoding.** Every value is written through Razor's HTML encoding; no raw HTML is written. News posts are
  plain text.
- **SQL.** Typed Dapper queries with parameters only (`CommandDefinition`), in the persistence projects.
- **Headers.** `Content-Security-Policy: default-src 'none'; img-src 'self'; style-src 'self'; font-src 'self';
  manifest-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; upgrade-insecure-requests` (every
  page but `/` runs no script, and none needs an inline style), `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: same-origin`, `Permissions-Policy` denying camera, microphone, location, payment and USB,
  `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy: same-origin`. Account and form pages are `no-store`.
- **Play page.** `/` alone (exactly; no other path) gets `default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'
  <origin of Play:GameEndpoint>` plus the shared directives (`SecurityHeaders.PlayPolicy`): the site's own module
  scripts and WebAssembly, and fetches only from the site and the game endpoint. No inline script: the endpoint reaches
  the script in a `data-endpoint` attribute. The game server checks the page's origin itself
  (`Network:WebSocket:AllowedOrigins`).
- **Redirects.** `?voltar=` after sign-in is followed only when it is a local address.
- **Secrets.** None in the repository: the certificate, its password, the PostgreSQL connection string and the keys
  directory live beside the executable on the host.
- **Errors.** Production error pages only; no exception detail reaches the browser.

## Run locally

```bash
dotnet build Web/PristonTale.Web/PristonTale.Web.csproj -c Release -m:1 -warnaserror
dotnet run --project Web/PristonTale.Web -c Release --no-build -- --Database:SqlitePath=<copy-of-the-database.db>
```

Open `https://localhost:5443`. Without a configured certificate Kestrel uses the ASP.NET Core development certificate
(`dotnet dev-certs https`); a browser warns until that certificate is trusted on the machine. Test against a copy of the
database: the site writes the accounts it registers.

## Deploy

1. Publish: `dotnet publish Web/PristonTale.Web/PristonTale.Web.csproj -c Release -o <site-directory>`. The output holds
   `web-settings.json`, `content/` and `wwwroot/`.
2. Edit `<site-directory>/web-settings.json`: `Database:SqlitePath` as an absolute path to the server's database (or
   `Postgres`), `GameServer`, `Downloads`, `Site:HttpsPort`, and the Kestrel endpoints.
3. TLS, one of:
   - Kestrel serves the public ports: `"Https": { "Url": "https://0.0.0.0:443", "Certificate": { "Path": "<pfx>",
     "Password": "<password>" } }` and `"Http": { "Url": "http://0.0.0.0:80" }` for the redirect. Renew the certificate
     (for example from Let's Encrypt) and restart.
   - A reverse proxy (Caddy, nginx, IIS) terminates TLS and forwards to the site's HTTPS endpoint on loopback; list the
     proxy's address in `Site:KnownProxies` when it is not on the same machine.
4. Keep `keys/` (or `Site:KeysPath`) on persistent storage readable only by the site's account: it decrypts the cookies.
   Losing it signs everyone out.
5. Run the site as a service (Windows service, systemd unit, or a scheduled start) with its working directory anywhere:
   it finds its files beside the executable.
6. The site's account needs read and write access to the database file and its directory (SQLite writes `-wal` and
   `-shm` there).

## How to

- **Publish a news post:** add an object to `content/news.json` beside the executable (`date` as `yyyy-MM-dd`, `title`,
  `summary`, `body` as a list of paragraphs, optional `author` shown by the game, optional `kind` `News`, `Event`
  or `Improvement`, the game's Novidade, Evento or Melhoria badge, `News` when missing; a post without a title is left
  out, a missing summary or body is empty). The GM panel's News area publishes a post of any of the three kinds. The Discord bot adds the repository's development notes here
  ([discord-bot.md](discord-bot.md#development-notes)).
  The site reads the file again when it changes; a file that does not parse keeps the old posts and logs an error.
- **Publish a new Android version:** copy the new APK beside the old one and rename it over `Downloads:AndroidApkPath`
  (a rename, so the SHA-256 is never taken from a half-copied file), and set `Downloads:AndroidVersion` (restart for the
  version; the size and SHA-256 follow the file at once, computed by the first download page that reads it).
- **Publish the browser client:** `Client/PristonTale.Browser/publish.ps1 -Target <site-directory>/play` (or
  `publish.sh <site-directory>/play`), set `Play:GameEndpoint`, and enable the game server's WebSocket edge with the
  site's origin in `Network:WebSocket:AllowedOrigins` ([web-client.md](web-client.md#run-it)).
- **Add a page:** a Razor page under `Pages/` with its link in `Shared/_NavLinks.cshtml`; styles in
  `wwwroot/css/site.css` only (the CSP refuses inline styles and scripts).
- **Change a limit:** the `Security:*` settings; the account rules are `AccountRules` and change the game's rules too.

## Pitfalls

- The site's clock zone must be the game server's: the calendar's hours are local hours of the machine the site runs on.
- Field names in the calendar and the account page are the catalog's English names; there are no Portuguese field names
  yet.
- The calendar's event titles and the drop names are written again in `SiteText`; the Android client keeps its own copy
  in `VulkanEventCalendar`. A change to one belongs in the other.
- The status probe shows that the game server accepts connections, not that logins work.
- The site and the game server keep their own `LoginThrottle`: an account gets its failure budget on each. Two sites
  behind a load balancer need the same keys directory and share neither the throttle nor the rate limits.
- Registrations of one site run one at a time, so two cases of one name cannot both be created; the database's unique
  name is exact, so a second process creating accounts (the account tool, another site) can still race a name that
  differs only in case.

## Related

- [application.md](application.md): `AccountAppService`, `LoginThrottle`, the event calendar.
- [persistence.md](persistence.md): `IRankingRepository`, `IAccountRepository`.
- [server.md](server.md): the game server's own login throttle and settings table.
- [hud-design.md](hud-design.md): the look the site follows.

The account page links to /conta/gm for game-master accounts. The panel checks the current account level on each request. Administrators can read the last 50 game-master command audit records.

Moderators and higher can look up accounts below their level and see the characters on those accounts. The GM dashboard also shows population totals and the latest server probe.

The GM panel shows saved character details. Administrators can publish plain-text news and save the XP event percentage. The game server reads that value at its next settings reload.

Set GameServer:PublicActivityPath and the game server's PublicActivity:Path to the same private JSON file. The server writes a snapshot every second. The GM overview discards snapshots older than 20 seconds. The public stream holds spoken (`local`), trade and standing notice lines; the game server's `PublicActivity:WhisperPath` holds whispers, party and clan lines. The Discord bot relays both files and uses the site's library (`SiteData`, `ClassTitles`, `SiteText`, `WebHosting.AddDatabase`) for its rankings and events ([discord-bot.md](discord-bot.md)).
The website stores support tickets under Site:SupportTicketsPath, beside the executable by default; the game server (SupportTickets:Path) and the Discord bot (Tickets:Path) must use the same folder. /suporte shows each ticket's conversation; /conta/gm/atendimento answers and closes tickets ([chat-and-gm.md](chat-and-gm.md#support-tickets)).
