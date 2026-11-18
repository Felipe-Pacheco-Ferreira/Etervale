using PristonTale.Application.GameMasters;

namespace PristonTale.DiscordBot;

// One /gm subcommand: its Portuguese name, the game-master command it runs and its argument, if any. The game server
// decides the level each needs and refuses commands that need a character in the world (GameMasterCommands).
public sealed record GameMasterAction(string Name, string Description, string Command, string Separator = " ", string? Argument = null,
    string? ArgumentDescription = null, bool ArgumentRequired = true)
{
    public string Line(string? argument) => string.IsNullOrWhiteSpace(argument) ? Command : Command + Separator + argument.Trim();
}

public static class GameMasterActions
{
    public static IReadOnlyList<GameMasterAction> All { get; } =
    [
        new("aviso", "Aviso para todos os jogadores agora", "/>", "", "texto", "O aviso"),
        new("aviso-fixo", "Aviso que cada jogador no mundo recebe uma vez", "/>>", "", "texto", "O aviso"),
        new("kick", "Desconecta um personagem", "/kick", Argument: "personagem", ArgumentDescription: "Nome do personagem"),
        new("personagem", "Conta, endereço e tempo online de um personagem", "/username", Argument: "personagem",
            ArgumentDescription: "Nome do personagem"),
        new("conta", "Personagem online de uma conta", "/userid", Argument: "conta", ArgumentDescription: "Nome da conta"),
        new("limite", "Mostra ou muda o limite de jogadores (0 sem limite)", "/user_limit", Argument: "numero",
            ArgumentDescription: "Novo limite", ArgumentRequired: false),
        new("bloqueios", "Contas com o login bloqueado por senhas erradas", "/show_blackuser"),
        new("liberar", "Libera o login bloqueado de uma conta", "/free_blackuser", Argument: "conta", ArgumentDescription: "Nome da conta"),
        new("limpar-bloqueios", "Libera todos os logins bloqueados", "/reset_blackuser"),
        new("hora", "Relógio do servidor", "/localtime"),
        new("boss", "Minuto em que os chefes nascem hoje", "/get_bosstime"),
        new("desligar", "Começa a contagem de 8 minutos e desliga o servidor", "/shutdown")
    ];

    public static GameMasterAction? Find(string name) => All.FirstOrDefault(action => action.Name == name);

    // The highest game-master level among the member's roles (GameMasterRoles), 0 for none.
    public static int LevelOf(IEnumerable<ulong> roles, IReadOnlyDictionary<string, int> levels)
    {
        var level = GameMasterLevel.Player;
        foreach (var role in roles)
        {
            if (levels.TryGetValue(role.ToString(System.Globalization.CultureInfo.InvariantCulture), out var granted))
            {
                level = Math.Max(level, granted);
            }
        }

        return level;
    }
}
