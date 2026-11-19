using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Application.GameMasters;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Application.Settings;
using System.Globalization;
using System.Text.Json;
using Dapper;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Abstractions;
using PristonTale.Contracts.Social;
using PristonTale.Web.Hosting;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PainelGmModel(IAccountRepository accounts, IGameMasterRepository gameMasters,
    ICharacterRepository characters, ServerStatusMonitor monitor, SiteData site,
    NewsFeed news, IServerSettingsRepository settings, WebSettings webSettings, TimeProvider time,
    IDbConnectionFactory database, IWorldDataRepository worldData, ILogger<PainelGmModel> logger) : PageModel
{
    public string Section { get; private set; } = "visao";
    public int Level { get; private set; }
    public bool CanReadAudit => Level == GameMasterLevel.Administrator;
    public IReadOnlyList<GmAuditRecord> Audit { get; private set; } = [];
    public string AuditOrigin { get; private set; } = "";
    public static bool IsSiteAction(GmAuditRecord row) => row.Command.StartsWith("web: ", StringComparison.Ordinal);
    public bool CanLookupAccounts => Level >= GameMasterLevel.Moderator;
    public bool Searched { get; private set; }
    public sealed record CharacterMatch(string Account, string Character);
    public IReadOnlyList<CharacterMatch> MatchingAccounts { get; private set; } = [];
    public (long Id, string Name, int Level, bool Blocked, bool Tester)? FoundAccount { get; private set; }
    public IReadOnlyList<CharacterRecord> FoundCharacters { get; private set; } = [];
    public ServerStatus? Status => monitor.Current;
    public PublicGameActivity? Activity { get; private set; }
    public PublicGameActivity? Whispers { get; private set; }
    public WorldTotals Totals { get; private set; } = new(0, 0);
    [TempData]
    public string? Feedback { get; set; }
    public int ExperiencePercent { get; private set; }

    private Task<AccountRecord?> ReadIssuerAsync(CancellationToken ct) =>
        User.Identity?.Name is { } name ? accounts.FindByUserIdAsync(name, ct) : Task.FromResult<AccountRecord?>(null);

    public async Task<IActionResult> OnGetAsync(string? conta, string? areaGm, string? origem, CancellationToken ct)
    {
        if (await ReadIssuerAsync(ct) is not { IsBlocked: false } account ||
            account.GmLevel is < GameMasterLevel.Guide or > GameMasterLevel.Highest)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Level = account.GmLevel;
        Section = areaGm switch
        {
            "contas" when CanLookupAccounts => "contas",
            "noticias" or "xp" or "auditoria" or "correios" when CanReadAudit => areaGm,
            null when CanLookupAccounts && !string.IsNullOrWhiteSpace(conta) => "contas",
            _ => "visao"
        };
        if (Section == "visao")
        {
            Totals = await site.TotalsAsync(ct);
            Activity = await ReadActivityAsync(webSettings.GameServer.PublicActivityPath, ct);
            if (CanReadAudit)
            {
                Whispers = await ReadActivityAsync(webSettings.GameServer.WhisperActivityPath, ct);
            }
        }

        if (Section == "contas" && conta?.Trim() is { Length: > 0 and <= 32 } query)
        {
            Searched = true;
            var target = await accounts.FindByUserIdAsync(query, ct);
            if (target is null)
            {
                await using var connection = await database.OpenAsync(ct);
                var prefix = query.ToLowerInvariant().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
                var matches = await connection.QueryAsync<CharacterMatch>(new CommandDefinition(
                    """
                    SELECT a.user_id AS Account, MIN(c.name) AS Character FROM characters c JOIN accounts a ON a.id = c.account_id
                    WHERE LOWER(c.name) LIKE @prefix ESCAPE '!' AND a.gm_level < @level
                    GROUP BY a.user_id ORDER BY a.user_id LIMIT 20
                    """, new { prefix, level = Level }, cancellationToken: ct));
                MatchingAccounts = matches.ToArray();
                if (MatchingAccounts.Count == 1)
                {
                    target = await accounts.FindByUserIdAsync(MatchingAccounts[0].Account, ct);
                }
            }

            if (target is not null && target.GmLevel < Level)
            {
                FoundAccount = (target.Id, target.UserId, target.GmLevel, target.IsBlocked, target.IsTester);
                FoundCharacters = await characters.ListByAccountAsync(target.Id, ct);
            }
        }
        if (Section == "auditoria")
        {
            AuditOrigin = origem is "site" or "jogo" ? origem : "";
            var recent = await gameMasters.RecentAuditAsync(50, ct);
            Audit = AuditOrigin switch
            {
                "site" => recent.Where(IsSiteAction).ToArray(),
                "jogo" => recent.Where(row => !IsSiteAction(row)).ToArray(),
                _ => recent
            };
        }
        if (Section == "xp")
        {
            ExperiencePercent = (await site.SettingsAsync(ct)).World.ExperienceEventPercent;
        }

        return Page();
    }

    private async Task<PublicGameActivity?> ReadActivityAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(WebSettings.BesideExecutable(path), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var snapshot = await JsonSerializer.DeserializeAsync<PublicGameActivity>(stream, cancellationToken: ct);
            return snapshot is not null && time.GetUtcNow() - snapshot.UpdatedAt < TimeSpan.FromSeconds(20) ? snapshot : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(exception, "Game activity unavailable");
            return null;
        }
    }

    public async Task<IActionResult> OnPostNoticiaAsync(string? title, string? summary, string? body, NewsKind kind, CancellationToken ct)
    {
        if (await ReadIssuerAsync(ct) is not { IsBlocked: false, GmLevel: GameMasterLevel.Administrator } issuer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (title?.Trim() is not { Length: > 0 and <= 120 } headline ||
            summary?.Trim() is not { Length: > 0 and <= 300 } excerpt ||
            body?.Trim() is not { Length: > 0 and <= 8000 } content ||
            !Enum.IsDefined(kind))
        {
            Feedback = "Preencha título, resumo e texto dentro dos limites indicados.";
            return RedirectToPage(new { areaGm = "noticias" });
        }

        try
        {
            news.Publish(new NewsItem(DateOnly.FromDateTime(DateTime.Today), headline, excerpt,
                content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), Kind: kind));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not publish a news post");
            Feedback = "Falha ao gravar a notícia.";
            return RedirectToPage(new { areaGm = "noticias" });
        }

        await RecordAsync(issuer, "web: notícia", headline, CancellationToken.None);
        Feedback = "Notícia publicada.";
        return RedirectToPage(new { areaGm = "noticias" });
    }

    public async Task<IActionResult> OnPostXpAsync(int? percent, CancellationToken ct)
    {
        if (await ReadIssuerAsync(ct) is not { IsBlocked: false, GmLevel: GameMasterLevel.Administrator } issuer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        const string key = "experience.event_percent";
        var value = percent?.ToString(CultureInfo.InvariantCulture);
        if (value is null || ServerSettingCatalog.Find(key)?.Apply(ServerSettings.Default, value) is null)
        {
            Feedback = "Informe uma porcentagem de XP entre 0 e 100.000.";
            return RedirectToPage(new { areaGm = "xp" });
        }

        await settings.SeedAsync(ServerSettingCatalog.Seeds, ct);
        if (!await settings.SetAsync(key, value, ct))
        {
            Feedback = "Falha ao salvar a configuração de XP.";
            return RedirectToPage(new { areaGm = "xp" });
        }

        await RecordAsync(issuer, "web: evento de XP", value, CancellationToken.None);
        Feedback = "XP salvo. O servidor aplica o valor ao recarregar as configurações.";
        return RedirectToPage(new { areaGm = "xp" });
    }

    public async Task<IActionResult> OnPostBloqueioAsync(long accountId, bool blocked, string? reason, CancellationToken ct)
    {
        if (await ReadIssuerAsync(ct) is not { IsBlocked: false, GmLevel: GameMasterLevel.Administrator } issuer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!ModelState.IsValid || accountId <= 0 || reason?.Trim() is not { Length: > 0 and <= 160 } explanation)
        {
            Feedback = "Informe o motivo da alteração.";
            return RedirectToPage(new { areaGm = "contas" });
        }

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var target = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            UPDATE accounts SET is_blocked = @blocked
            WHERE id = @accountId AND id <> @issuerId AND gm_level < @GmLevel AND is_blocked <> @blocked
              AND EXISTS (SELECT 1 FROM accounts WHERE id = @issuerId AND NOT is_blocked AND gm_level = @GmLevel)
            RETURNING user_id
            """, new { accountId, blocked, issuerId = issuer.Id, issuer.GmLevel }, transaction, cancellationToken: ct));
        if (target is null)
        {
            Feedback = "A conta não pôde ser alterada ou já está nessa situação.";
            return RedirectToPage(new { areaGm = "contas" });
        }

        var action = blocked ? "web: bloquear conta" : "web: desbloquear conta";
        var result = $"{target} (#{accountId}) · {explanation}";
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO gm_audit (account_id, user_id, character_name, gm_level, command, required_level, accepted, result, created_at)
            VALUES (@Id, @UserId, '', @GmLevel, @action, @GmLevel, @accepted, @result, @now)
            """, new { issuer.Id, issuer.UserId, issuer.GmLevel, action, accepted = true, result, now = time.GetUtcNow().UtcDateTime },
            transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        Feedback = blocked ? "Conta bloqueada para novos logins." : "Conta desbloqueada.";
        return RedirectToPage(new { areaGm = "contas", conta = target });
    }

    public async Task<IActionResult> OnPostCorreioAsync(string? recipient, string? subject, string? body, bool everyone,
        string? itemCode, int? itemCount, long? gold, CancellationToken ct)
    {
        if (await ReadIssuerAsync(ct) is not { IsBlocked: false, GmLevel: GameMasterLevel.Administrator } issuer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var destination = recipient?.Trim() ?? "";
        if (!ModelState.IsValid || subject?.Trim() is not { Length: > 0 and <= 120 } title ||
            body?.Trim() is not { Length: > 0 and <= 8000 } message ||
            (everyone ? destination.Length != 0 : destination.Length is 0 or > 32))
        {
            Feedback = "Preencha a mensagem e selecione todos os jogadores ou informe uma conta.";
            return RedirectToPage(new { areaGm = "correios" });
        }

        var codes = (itemCode ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var count = itemCount ?? 0;
        var coins = gold ?? 0;
        if (coins is < 0 or > 100_000_000 || count is < 0 or > 100 || codes.Length > 10 ||
            (codes.Length == 0) != (count == 0) || codes.Any(code => code.Length > 64))
        {
            Feedback = "Confira o código, a quantidade e o ouro da recompensa.";
            return RedirectToPage(new { areaGm = "correios" });
        }

        var itemKeys = new List<string>();
        if (count > 0)
        {
            var templates = await worldData.GetAllItemTemplatesAsync(ct: ct);
            foreach (var code in codes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var itemKey = templates.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))?.Code;
                if (itemKey is null)
                {
                    Feedback = $"O código {code} não existe no catálogo.";
                    return RedirectToPage(new { areaGm = "correios" });
                }

                itemKeys.Add(itemKey);
            }
        }

        var target = everyone ? null : await accounts.FindByUserIdAsync(destination, ct);
        if (!everyone && target is null)
        {
            Feedback = "Conta não encontrada.";
            return RedirectToPage(new { areaGm = "correios" });
        }

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO game_mail (sender_account_id, recipient_account_id, subject, body, item_key, item_count, gold, created_at)
            VALUES (@sender, @recipient, @title, @message, NULL, 0, @coins, @now) RETURNING id
            """, new { sender = issuer.Id, recipient = target?.Id, title, message, coins, now }, transaction, cancellationToken: ct));
        foreach (var itemKey in itemKeys)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO game_mail_items (mail_id, item_key, quantity) VALUES (@id, @itemKey, @count)",
                new { id, itemKey, count }, transaction, cancellationToken: ct));
        }

        var attachments = itemKeys.Count == 0 ? "sem item" : string.Join("; ", itemKeys.Select(key => $"{key} ×{count}"));
        var result = $"#{id} · {target?.UserId ?? "Todos"} · {title} · {attachments} · {coins} ouro";
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO gm_audit (account_id, user_id, character_name, gm_level, command, required_level, accepted, result, created_at)
            VALUES (@Id, @UserId, '', @GmLevel, 'web: correio', @GmLevel, @accepted, @result, @now)
            """, new { issuer.Id, issuer.UserId, issuer.GmLevel, accepted = true, result, now },
            transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        Feedback = $"Mensagem #{id} registrada.";
        return RedirectToPage(new { areaGm = "correios" });
    }

    private Task RecordAsync(AccountRecord issuer, string action, string result, CancellationToken ct) =>
        gameMasters.AppendAuditAsync(new GmAuditEntry(issuer.Id, issuer.UserId, "", issuer.GmLevel,
            action, GameMasterLevel.Administrator, true, result), ct);
}
