using Discord;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// The bot's slash commands, registered in the guild when the bot connects.
public static class SlashCommands
{
    public const string Status = "status";
    public const string Ranking = "ranking";
    public const string Events = "eventos";
    public const string News = "noticias";
    public const string GameMaster = "gm";
    public const string ClassOption = "classe";

    public static ApplicationCommandProperties[] Build(ClassTitles titles)
    {
        var classes = new SlashCommandOptionBuilder()
            .WithName(ClassOption)
            .WithDescription("Só os personagens de uma classe")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(false);
        foreach (var playerClass in ClassTitles.All)
        {
            classes.AddChoice(titles.Of(playerClass, 0), ClassTitles.Slug(playerClass));
        }

        var gameMaster = new SlashCommandBuilder()
            .WithName(GameMaster)
            .WithDescription("Comandos de game master no servidor de jogo")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages);
        foreach (var action in GameMasterActions.All)
        {
            var sub = new SlashCommandOptionBuilder()
                .WithName(action.Name)
                .WithDescription(action.Description)
                .WithType(ApplicationCommandOptionType.SubCommand);
            if (action.Argument is { } argument)
            {
                sub.AddOption(argument, ApplicationCommandOptionType.String, action.ArgumentDescription ?? argument, isRequired: action.ArgumentRequired);
            }

            gameMaster.AddOption(sub);
        }

        return
        [
            new SlashCommandBuilder().WithName(Status).WithDescription("Mundos no ar e jogadores online").Build(),
            new SlashCommandBuilder().WithName(Ranking).WithDescription("Os personagens mais altos").AddOption(classes).Build(),
            new SlashCommandBuilder().WithName(Events).WithDescription("Eventos agora, nas próximas 24 horas e chefes de campo").Build(),
            new SlashCommandBuilder().WithName(News).WithDescription("As últimas notícias do site").Build(),
            gameMaster.Build()
        ];
    }
}
