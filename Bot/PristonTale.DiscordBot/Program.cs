using PristonTale.DiscordBot;

// PristonTale.DiscordBot [--settings <file>] [--check <command> [arguments]] [--Section:Key=value ...]
// Settings come from discord-bot-settings.json beside the executable (or --settings) and the command line only.
var options = BotCommandLine.Parse(args);
if (options.Error is { } error)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(BotCommandLine.Usage);
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(options.SettingsPath, optional: false)
    .AddCommandLine(options.Overrides)
    .Build();
var settings = configuration.Get<BotSettings>() ?? new BotSettings();
var problems = settings.Problems(connecting: options.Check.Count == 0);
if (problems.Count > 0)
{
    foreach (var problem in problems)
    {
        Console.Error.WriteLine(problem);
    }

    return 2;
}

var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
builder.Logging.AddConfiguration(configuration.GetSection("Logging"));
builder.Logging.AddSimpleConsole(static console => console.SingleLine = true);
BotServices.Add(builder.Services, settings);
if (options.Check.Count > 0)
{
    using var checkHost = builder.Build();
    return await BotChecks.RunAsync(checkHost.Services, options.Check, Console.Out);
}

builder.Services.AddHostedService<DiscordBotService>();
using var host = builder.Build();
await host.RunAsync();
return 0;
