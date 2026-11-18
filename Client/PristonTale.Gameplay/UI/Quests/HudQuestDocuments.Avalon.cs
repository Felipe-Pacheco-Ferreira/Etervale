using PristonTale.Assets;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Quests;

public sealed partial class HudQuestDocuments
{
    private readonly Dictionary<(string Language, string Path), bool> _avalonDocumentExists = new();
    private static readonly Dictionary<string, (string[] Portuguese, string[] English)> AvalonDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        [BoardPath("Avalon/MeetSkadi.msg")] = (
            ["Encontre a Aventureira Skadi em Ricarten.", "", "Ela apresenta os equipamentos iniciais e a primeira caçada da Jornada do aventureiro."],
            ["Find Adventurer Skadi in Ricarten.", "", "She introduces your starter equipment and the Adventurer Journey's first hunt."]),
        [BoardPath("Avalon/MeetJin.msg")] = (
            ["Encontre o Aventureiro Jin em Pillai.", "", "Ele apresenta os equipamentos iniciais e a primeira caçada da Jornada do aventureiro."],
            ["Find Adventurer Jin in Pillai.", "", "He introduces your starter equipment and the Adventurer Journey's first hunt."]),
        [BoardPath("Avalon/Preparation.msg")] = (
            ["Equipe a arma inicial e coloque uma poção no cinto.", "", "Volte ao guia da Jornada para receber as instruções da primeira caçada.", "", "Esta etapa concede experiência, 500 Gold e 15 poções pequenas de vida."],
            ["Equip your starter weapon and place a potion in your belt.", "", "Return to your Journey guide for the first hunt's instructions.", "", "This step grants experience, 500 Gold and 15 small life potions."]),
        [BoardPath("Avalon/SkadiHunt.msg")] = (
            ["Derrote 3 Hopys na Floresta do Início.", "", "Continue a caçada até obter o cristal solicitado e entregue-o à Aventureira Skadi em Ricarten.", "", "A entrega concede experiência e 300 Gold."],
            ["Defeat 3 Hopys in the starting forest.", "", "Continue hunting until you obtain the requested crystal, then deliver it to Adventurer Skadi in Ricarten.", "", "The delivery grants experience and 300 Gold."]),
        [BoardPath("Avalon/JinHunt.msg")] = (
            ["Derrote 3 Rabies na floresta próxima de Pillai.", "", "Continue a caçada até obter o cristal solicitado e entregue-o ao Aventureiro Jin em Pillai.", "", "A entrega concede experiência e 300 Gold."],
            ["Defeat 3 Rabies in the forest outside Pillai.", "", "Continue hunting until you obtain the requested crystal, then deliver it to Adventurer Jin in Pillai.", "", "The delivery grants experience and 300 Gold."]),
        [BoardPath("Avalon/Finished.msg")] = (
            ["Você concluiu a Jornada do aventureiro.", "", "A preparação e a primeira caçada foram concluídas com o guia da sua tribo."],
            ["You completed the Adventurer Journey.", "", "Your preparation and first hunt with your tribe's guide are complete."]),
        [DialogPath("avalon_journey_start")] = (
            ["Bem-vindo à Jornada do aventureiro.", "", "Receba seus equipamentos iniciais, equipe a arma e coloque uma poção no cinto. Fale comigo novamente quando estiver pronto."],
            ["Welcome to the Adventurer Journey.", "", "Receive your starter equipment, equip your weapon and place a potion in your belt. Talk to me again when you are ready."]),
        [DialogPath("avalon_journey_bag")] = (
            ["Equipe a arma inicial e coloque pelo menos uma poção no cinto antes de continuar.", "", "Se faltar uma peça do equipamento inicial, fale comigo para recebê-la."],
            ["Equip your starter weapon and put at least one potion in your belt before continuing.", "", "If a starter equipment piece is missing, talk to me to receive it."]),
        [DialogPath("avalon_journey_hunt_skadi")] = (
            ["Sua primeira caçada acontece na floresta ao norte de Ricarten.", "", "Derrote 3 Hopys, obtenha o cristal solicitado e volte para falar comigo."],
            ["Your first hunt takes place in the forest north of Ricarten.", "", "Defeat 3 Hopys, obtain the requested crystal and return to talk to me."]),
        [DialogPath("avalon_journey_hunt_jin")] = (
            ["Sua primeira caçada acontece na floresta próxima de Pillai.", "", "Derrote 3 Rabies, obtenha o cristal solicitado e volte para falar comigo."],
            ["Your first hunt takes place in the forest near Pillai.", "", "Defeat 3 Rabies, obtain the requested crystal and return to talk to me."]),
        [DialogPath("avalon_journey_collect")] = (
            ["Complete a caçada e traga o cristal solicitado.", "", "Depois de derrotar os três monstros, continue caçando até recolher o cristal."],
            ["Complete the hunt and bring the requested crystal.", "", "After defeating the three monsters, keep hunting until you collect the crystal."]),
        [DialogPath("avalon_journey_finish")] = (
            ["Você concluiu sua primeira caçada.", "", "Entregue o cristal para receber experiência e 300 Gold e concluir a Jornada do aventureiro."],
            ["You completed your first hunt.", "", "Deliver the crystal to receive experience and 300 Gold and complete the Adventurer Journey."])
    };

    private string[] AvalonFallback(string relativePath)
    {
        var key = relativePath.Replace('\\', '/');
        if (!AvalonDocuments.TryGetValue(key, out var document))
        {
            return null;
        }

        var languageKey = (ClientText.Language, key);
        if (!_avalonDocumentExists.TryGetValue(languageKey, out var exists))
        {
            exists = AssetFiles.Exists(Path.Combine(assetRoot, key))
                || File.Exists(Path.Combine(HudTextFiles.DefaultTranslationRoot, ClientText.Language, key));
            _avalonDocumentExists[languageKey] = exists;
        }

        return exists ? null : ClientText.Language == "pt-BR" ? document.Portuguese : document.English;
    }

    internal static string AvalonObjective(string path)
    {
        var portuguese = ClientText.Language == "pt-BR";
        if (path.EndsWith("/MeetSkadi.msg", StringComparison.OrdinalIgnoreCase))
        {
            return portuguese ? "Fale com Skadi em Ricarten." : "Talk to Skadi in Ricarten.";
        }
        if (path.EndsWith("/MeetJin.msg", StringComparison.OrdinalIgnoreCase))
        {
            return portuguese ? "Fale com Jin em Pillai." : "Talk to Jin in Pillai.";
        }
        if (path.EndsWith("/Preparation.msg", StringComparison.OrdinalIgnoreCase))
        {
            return portuguese ? "Equipe a arma e uma poção no cinto." : "Equip a weapon and belt potion.";
        }
        return portuguese ? "Siga as instruções da missão." : "Follow the quest instructions.";
    }

    internal static string AvalonReward(string path)
    {
        var portuguese = ClientText.Language == "pt-BR";
        if (path.EndsWith("/Preparation.msg", StringComparison.OrdinalIgnoreCase))
        {
            return portuguese ? "EXP, 500 Gold e 15 poções." : "EXP, 500 Gold and 15 potions.";
        }
        if (path.EndsWith("/SkadiHunt.msg", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/JinHunt.msg", StringComparison.OrdinalIgnoreCase))
        {
            return "EXP + 300 Gold";
        }
        if (path.EndsWith("/MeetSkadi.msg", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/MeetJin.msg", StringComparison.OrdinalIgnoreCase))
        {
            return portuguese ? "Equipamento inicial e experiência." : "Starter equipment and experience.";
        }
        return portuguese ? "Consulte a descrição da missão." : "See the quest description.";
    }
}
