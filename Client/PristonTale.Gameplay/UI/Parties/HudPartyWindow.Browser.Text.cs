using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Parties;

public sealed partial class HudPartyWindow
{
    private readonly record struct DirectoryText(string Count, string Detail);

    private const int SocialTextCacheLimit = 512;
    private static readonly string[] PortuguesePartyCommands = ["Líder", "Dissolver", "Expulsar", "Sair"];
    private static readonly string[] EnglishPartyCommands = ["Leader", "Disband", "Kick", "Leave"];
    private readonly Dictionary<int, string> _socialLevelLabels = new();
    private readonly Dictionary<(PlayerClass Class, int Level), string> _socialClanLabels = new();
    private readonly Dictionary<PartyDirectoryEntry, DirectoryText> _directoryLabels = new();
    private readonly Dictionary<(string Text, int Width, float Scale), string> _socialFittedText = new();
    private string _socialTextLanguage;
    private string _socialNameText = "";
    private int _socialPartyCount = -1;
    private string _socialPartyTitle = "";
    private HudSocialProfile _profileTextSource;
    private string _profileClassLine = "";
    private string _profileLevelLine = "";
    private string _profileClanLine = "";
    private Func<int, string> _directoryFieldResolver;
    private HudPainter _socialFitPainter;
    private float _socialFitScale;
    private Func<string, float> _socialFitMeasure;

    private void RefreshSocialTextLanguage()
    {
        if (_socialTextLanguage == ClientText.Language)
        {
            return;
        }

        _socialTextLanguage = ClientText.Language;
        _socialClanLabels.Clear();
        _directoryLabels.Clear();
        _socialFittedText.Clear();
        _socialPartyCount = -1;
        _profileTextSource = null;
    }

    private string SocialPartyTitle(int count)
    {
        if (_socialPartyCount != count)
        {
            _socialPartyCount = count;
            _socialPartyTitle = (ClientText.Language == "pt-BR" ? "Grupo   " : "Party   ") + count + "/" + PartyLimits.MaxMembers;
        }

        return _socialPartyTitle;
    }

    private string SocialLevelLabel(int level)
    {
        if (!_socialLevelLabels.TryGetValue(level, out var text))
        {
            text = $"Lv. {level}";
            _socialLevelLabels.Add(level, text);
        }

        return text;
    }

    private string SocialClanLabel(PlayerClass playerClass, int level)
    {
        var key = (playerClass, level);
        if (!_socialClanLabels.TryGetValue(key, out var text))
        {
            text = $"{DataText.ClassTitle(playerClass, 0)} · {level}";
            _socialClanLabels.Add(key, text);
        }

        return text;
    }

    private DirectoryText DirectoryLabel(PartyDirectoryEntry entry)
    {
        if (_directoryFieldResolver != FieldName)
        {
            _directoryFieldResolver = FieldName;
            _directoryLabels.Clear();
        }

        if (!_directoryLabels.TryGetValue(entry, out var text))
        {
            if (_directoryLabels.Count >= SocialTextCacheLimit)
            {
                _directoryLabels.Clear();
            }

            var prefix = ClientText.Language == "pt-BR" ? "Nível médio " : "Average level ";
            text = new DirectoryText($"{entry.MemberCount}/{PartyLimits.MaxMembers}",
                prefix + entry.AverageLevel + " · " + FieldName(entry.FieldId));
            _directoryLabels.Add(entry, text);
        }

        return text;
    }

    private void RefreshProfileText()
    {
        if (_profileTextSource == _socialProfile)
        {
            return;
        }

        _profileTextSource = _socialProfile;
        var portuguese = ClientText.Language == "pt-BR";
        _profileClassLine = (portuguese ? "Classe   " : "Class   ") + _socialProfile.ClassName;
        _profileLevelLine = (portuguese ? "Nível   " : "Level   ") + _socialProfile.Level;
        _profileClanLine = (portuguese ? "Clã   " : "Clan   ") + _socialProfile.ClanName;
    }

    private PartyDirectoryEntry SelectedDirectoryEntry()
    {
        for (var index = 0; index < Directory.Count; index++)
        {
            if (Directory[index].LeaderId == _directoryLeader)
            {
                return Directory[index];
            }
        }

        return null;
    }

    private string FitSocial(HudPainter painter, string text, int width, float scale)
    {
        if (_socialFitPainter != painter)
        {
            _socialFitPainter = painter;
            _socialFittedText.Clear();
        }

        var key = (text, width, scale);
        if (!_socialFittedText.TryGetValue(key, out var fitted))
        {
            if (_socialFittedText.Count >= SocialTextCacheLimit)
            {
                _socialFittedText.Clear();
            }

            _socialFitScale = scale;
            _socialFitMeasure ??= MeasureSocialFit;
            fitted = HudTextLayout.Fit(text, width, _socialFitMeasure);
            _socialFittedText.Add(key, fitted);
        }

        return fitted;
    }

    private float MeasureSocialFit(string text) => _socialFitPainter.MeasureAt(text, _socialFitScale);
}
