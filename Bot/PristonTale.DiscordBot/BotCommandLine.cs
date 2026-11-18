namespace PristonTale.DiscordBot;

// The bot's own arguments; every other argument is a setting (--Section:Key=value) for the configuration.
public sealed record BotCommandLine(string SettingsPath, IReadOnlyList<string> Check, string[] Overrides, string? Error = null)
{
    public const string Usage =
        "PristonTale.DiscordBot [--settings <file>] [--check status|ranking [class]|eventos|noticias|tickets|chat|gm \"<line>\" [level]] " +
        "[--Section:Key=value ...]";

    public static BotCommandLine Parse(IReadOnlyList<string> args)
    {
        var settingsPath = BotSettings.BesideExecutable(BotSettings.FileName);
        var check = new List<string>();
        var overrides = new List<string>();
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--settings" when index + 1 < args.Count:
                    settingsPath = Path.GetFullPath(args[++index]);
                    break;
                case "--settings":
                    return new BotCommandLine(settingsPath, [], [], "--settings needs a file.");
                case "--check":
                    // The command and its arguments run up to the next option.
                    while (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        check.Add(args[++index]);
                    }

                    if (check.Count == 0)
                    {
                        return new BotCommandLine(settingsPath, [], [], "--check needs a command.");
                    }

                    break;
                default:
                    overrides.Add(args[index]);
                    break;
            }
        }

        return new BotCommandLine(settingsPath, check, overrides.ToArray());
    }
}
