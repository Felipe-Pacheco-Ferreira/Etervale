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
    public const string DevNote = "devnota";
    public const string TitleOption = "titulo";
    public const string TextOption = "texto";
    public const string ImageOption = "imagem";
    public const int DevNoteImages = 4;
    public const string Maintenance = "manutencao";
    public const string ActiveOption = "ativa";
    public const string ReasonOption = "motivo";
    public const int ReasonLimit = 200;
    public const string Purge = "limparchat";
    public const string ChannelOption = "canal";
    public const string CountOption = "quantidade";
    public const int PurgeLimit = 1000;

    // The image options of /devnota: imagem1 to imagem4.
    public static IEnumerable<string> ImageOptions => Enumerable.Range(1, DevNoteImages).Select(static index => ImageOption + index);

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

        var devNote = new SlashCommandBuilder()
            .WithName(DevNote)
            .WithDescription("Publica uma nota de desenvolvimento com prints no canal de desenvolvimento")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .AddOption(TitleOption, ApplicationCommandOptionType.String, "Título da nota, ex.: Novo sistema: Chat do Discord", isRequired: true, maxLength: BotTexts.DevNoteTitleLimit)
            .AddOption(TextOption, ApplicationCommandOptionType.String, "O que mudou; \\n quebra a linha", isRequired: true, maxLength: BotTexts.DevNoteTextLimit);
        foreach (var image in ImageOptions)
        {
            devNote.AddOption(image, ApplicationCommandOptionType.Attachment, "Print (imagem)", isRequired: false);
        }

        var maintenance = new SlashCommandBuilder()
            .WithName(Maintenance)
            .WithDescription("Liga ou desliga o aviso de manutenção (status amarelo)")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .AddOption(ActiveOption, ApplicationCommandOptionType.Boolean, "True liga a manutenção, False encerra", isRequired: true)
            .AddOption(ReasonOption, ApplicationCommandOptionType.String, "O que está sendo atualizado (opcional)", isRequired: false, maxLength: ReasonLimit);

        var purge = new SlashCommandBuilder()
            .WithName(Purge)
            .WithDescription("Apaga as mensagens de um canal (pede confirmação)")
            .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
            .AddOption(ChannelOption, ApplicationCommandOptionType.Channel, "O canal a limpar (o atual quando vazio)", isRequired: false,
                channelTypes: [ChannelType.Text, ChannelType.News])
            .AddOption(CountOption, ApplicationCommandOptionType.Integer, $"Quantas mensagens, das mais novas (até {PurgeLimit}; vazio: todas até {PurgeLimit})",
                isRequired: false, minValue: 1, maxValue: PurgeLimit);

        return
        [
            devNote.Build(),
            maintenance.Build(),
            purge.Build(),
            new SlashCommandBuilder().WithName(Status).WithDescription("Mundos no ar e jogadores online").Build(),
            new SlashCommandBuilder().WithName(Ranking).WithDescription("Os personagens mais altos").AddOption(classes).Build(),
            new SlashCommandBuilder().WithName(Events).WithDescription("Eventos agora, nas próximas 24 horas e chefes de campo").Build(),
            new SlashCommandBuilder().WithName(News).WithDescription("As últimas notícias do site").Build(),
            gameMaster.Build()
        ];
    }
}
