using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Mobile;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;

// The class's skills, a HUD window (VulkanPanel) opened from the menu or by talking to a skill master: a pill tab per
// class tier (the class's title at it), the tier's skills as cards (the desktop's skill button, gray until learned, the
// name, the rank, the action wheel button holding it and a "+" when the next rank can be trained) and the chosen skill's
// sheet: its description, cost and numbers at its rank, what the next rank needs and the wheel buttons to put it on. The
// free skill and elite points are chips beside the tabs. A rank is the skill master's command
// (ProgressionAction.LearnSkill) the server validates: near the town's skill master, the points, the level and the gold;
// the window shows its refusal and redraws from the owner state that follows.
internal sealed class VulkanSkillsPanel
{
    private const int PlusSize = 38;
    private const int IconSize = 46;

    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly VulkanWorldSession _session;
    private readonly VulkanItemIcons _icons;
    private readonly VulkanActionWheel _wheel;
    private readonly PlayerClass _class;
    private readonly IReadOnlyList<Skill> _skills;
    private VulkanPanel _panel;
    private LinearLayout _tabs;
    private TextView _points;
    private TextView _elite;
    private LinearLayout _cards;
    private LinearLayout _sheet;
    private TextView _note;
    private OwnerState _owner;
    private int _tier;
    private int _chosen;
    private bool _pending;
    private long _expectedRevision;
    private string _answer;
    private bool _answerOk;

    public VulkanSkillsPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session, VulkanItemIcons icons, VulkanActionWheel wheel)
    {
        _activity = activity;
        _overlay = overlay;
        _session = session;
        _icons = icons;
        _wheel = wheel;
        _class = session.Character.Class;
        _skills = ClassSkillCatalog.For(_class);
        icons.Loaded += () =>
        {
            if (_panel is not null)
            {
                Render();
            }
        };
    }

    // The skill master the player last talked to (its entity), the NPC a rank is asked of; 0 before any.
    public ulong Master { get; set; }

    public bool IsOpen => _panel is not null;

    public void Open()
    {
        if (_panel is not null || _session.Owner is not { } owner)
        {
            return;
        }

        _owner = owner;
        _answer = null;
        _tier = Math.Clamp(owner.Tier, 0, PlayerCharacter.LastTier);
        _chosen = _tier * SkillLearning.SkillsPerTier;
        Build();
        Render();
    }

    public void Update(OwnerState state)
    {
        var changed = _owner is null || !ReferenceEquals(_owner.Skills, state.Skills) || _owner.SkillPoints != state.SkillPoints
            || _owner.EliteSkillPoints != state.EliteSkillPoints || _owner.Money != state.Money || _owner.Level != state.Level || _owner.Tier != state.Tier
            || _owner.ProgressionRevision != state.ProgressionRevision;
        _owner = state;
        if (_panel is not null && changed)
        {
            Render();
        }
    }

    private void Build()
    {
        _panel = new VulkanPanel(_activity, _overlay, "Habilidades", 820, 392);
        _panel.Closed += () => _panel = null;
        var top = new LinearLayout(_activity);
        top.SetGravity(GravityFlags.CenterVertical);
        var tabScroll = new HorizontalScrollView(_activity) { HorizontalScrollBarEnabled = false };
        _tabs = new LinearLayout(_activity);
        tabScroll.AddView(_tabs);
        top.AddView(tabScroll, new LinearLayout.LayoutParams(0, -2, 1));
        _points = VulkanPanelArt.Chip(_activity, "", VulkanHudArt.EdgeLight);
        top.AddView(_points, new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(8) });
        _elite = VulkanPanelArt.Chip(_activity, "", VulkanPanelArt.Done);
        top.AddView(_elite, new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(6) });
        _panel.Content.AddView(top);

        var body = new LinearLayout(_activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(10) });
        var cardScroll = new ScrollView(_activity) { VerticalScrollBarEnabled = false };
        _cards = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        cardScroll.AddView(_cards);
        body.AddView(cardScroll, new LinearLayout.LayoutParams(0, -1, 1) { RightMargin = Dp(14) });

        var right = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var sheetScroll = new ScrollView(_activity) { VerticalScrollBarEnabled = false };
        _sheet = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        sheetScroll.AddView(_sheet);
        right.AddView(sheetScroll, new LinearLayout.LayoutParams(-1, 0, 1));
        _note = VulkanPanel.Body(_activity, "", 12, VulkanPanel.Muted);
        _note.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        _note.Background = VulkanPanelArt.Inset(_activity);
        right.AddView(_note, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        body.AddView(right, new LinearLayout.LayoutParams(0, -1, 1.15f));
    }

    private void Render()
    {
        if (_panel is null || _owner is not { } owner)
        {
            return;
        }

        _points.Text = owner.SkillPoints == 1 ? "1 ponto" : $"{owner.SkillPoints} pontos";
        _points.Alpha = owner.SkillPoints > 0 ? 1f : .45f;
        _elite.Text = owner.EliteSkillPoints == 1 ? "1 ponto de elite" : $"{owner.EliteSkillPoints} pontos de elite";
        _elite.Visibility = owner.EliteSkillPoints > 0 || SkillLearning.OpenedBy(owner.Tier) > SkillLearning.EliteStart ? ViewStates.Visible : ViewStates.Gone;
        RenderTabs(owner);
        RenderCards(owner);
        RenderSheet(owner);
        if (_answer is not null)
        {
            _note.Text = _answer;
            _note.SetTextColor(_answerOk ? VulkanPanelArt.Done : VulkanPanelArt.Warning);
        }
        else
        {
            _note.Text = Master == 0
                ? "Para aprender ou treinar, fale com o Mestre de Habilidades da sua cidade e toque em + perto dele."
                : "Toque em + para treinar. Toque numa habilidade para ver os detalhes e pô-la num botão de ação.";
            _note.SetTextColor(VulkanPanel.Muted);
        }
    }

    // A tab per class tier; the tiers the class has not reached are dimmed but can be looked at.
    private void RenderTabs(OwnerState owner)
    {
        _tabs.RemoveAllViews();
        for (var tier = 0; tier <= PlayerCharacter.LastTier && tier * SkillLearning.SkillsPerTier < _skills.Count; tier++)
        {
            var shown = tier;
            var tab = VulkanPanelArt.Tab(_activity, DataText.ClassTitle(_class, tier), tier == _tier);
            tab.Alpha = tier <= owner.Tier ? 1f : .5f;
            tab.Click += (_, _) =>
            {
                _tier = shown;
                _chosen = shown * SkillLearning.SkillsPerTier;
                Render();
            };
            _tabs.AddView(tab, new LinearLayout.LayoutParams(-2, Dp(32)) { RightMargin = Dp(8) });
        }
    }

    private void RenderCards(OwnerState owner)
    {
        _cards.RemoveAllViews();
        var first = _tier * SkillLearning.SkillsPerTier;
        for (var index = first; index < Math.Min(_skills.Count, first + SkillLearning.SkillsPerTier); index++)
        {
            _cards.AddView(Card(owner, index), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(5) });
        }
    }

    // A skill: its button in a well, name, rank and kind, the wheel button holding it, and its "+".
    private View Card(OwnerState owner, int index)
    {
        var skill = _skills[index];
        var rank = owner.SkillRank(skill.Code);
        var card = new LinearLayout(_activity) { Clickable = true, Background = VulkanPanelArt.Inset(_activity, lit: index == _chosen) };
        card.SetGravity(GravityFlags.CenterVertical);
        card.SetPadding(Dp(6), Dp(4), Dp(6), Dp(4));
        card.Click += (_, _) =>
        {
            _chosen = index;
            Render();
        };

        var well = new ImageView(_activity);
        well.SetScaleType(ImageView.ScaleType.FitCenter!);
        well.SetPadding(Dp(3), Dp(3), Dp(3), Dp(3));
        well.Background = new VulkanPanelArt.WellDrawable(Dp(6), Dp(1), rank > 0 ? VulkanHudArt.EdgeLight : Color.Transparent, selected: false);
        if (skill.Code != 0 && _icons.Picture(HudArt.SkillButton(_class, skill.IconKey, gray: rank == 0)) is { } icon)
        {
            well.SetImageBitmap(icon);
        }
        else
        {
            well.SetImageDrawable(new HudIcon(HudGlyph.Skill, Dp(22)));
        }

        card.AddView(well, new LinearLayout.LayoutParams(Dp(IconSize), Dp(IconSize)) { RightMargin = Dp(8) });
        var text = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var name = VulkanPanel.Text(_activity, DataText.SkillName(skill), 14, rank > 0 ? MobileHudTheme.Ink : VulkanPanel.Muted, serif: true);
        name.SetSingleLine(true);
        name.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        text.AddView(name);
        var line = VulkanPanel.Body(_activity, skill.Code == 0 ? ClientText.Get("SkillWindow.Unavailable") : $"{RankText(rank)}  ·  {Kind(skill)}", 11,
            VulkanPanel.Muted);
        line.SetSingleLine(true);
        line.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        text.AddView(line, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(2) });
        card.AddView(text, new LinearLayout.LayoutParams(0, -2, 1));

        if (WheelSlot(skill.Code) is var slot and >= 0)
        {
            card.AddView(VulkanPanelArt.Chip(_activity, $"Botão {slot + 1}", VulkanHudArt.EdgeLight), new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(6) });
        }

        if (Check(owner, index) == SkillTrainingCheck.Ready)
        {
            var plus = new VulkanPointButton(_activity) { ContentDescription = $"Treinar {DataText.SkillName(skill)}" };
            plus.Enabled = Sendable(owner) && owner.Money >= SkillLearning.Price(index, rank);
            plus.Click += async (_, _) => await TrainAsync(index);
            card.AddView(plus, new LinearLayout.LayoutParams(Dp(PlusSize), Dp(PlusSize)) { LeftMargin = Dp(6) });
        }

        return card;
    }

    // The chosen skill: its heading with the rank, description, cost and numbers, what the next rank needs and the wheel.
    private void RenderSheet(OwnerState owner)
    {
        _sheet.RemoveAllViews();
        if (_chosen < 0 || _chosen >= _skills.Count)
        {
            return;
        }

        var skill = _skills[_chosen];
        var rank = owner.SkillRank(skill.Code);
        _sheet.AddView(VulkanPanelArt.Section(_activity, DataText.SkillName(skill), VulkanPanelArt.Chip(_activity, RankText(rank),
            rank > 0 ? VulkanHudArt.EdgeLight : VulkanPanel.Muted)));
        if (skill.Code == 0)
        {
            AddLine(ClientText.Get("SkillWindow.Unavailable"), 13, VulkanPanel.Muted, 6);
            return;
        }

        AddLine(DataText.SkillDescription(skill.Code, skill.Description), 13, MobileHudTheme.Ink, 6);
        var shownRank = Math.Max(1, rank);
        AddLine($"{Kind(skill)}  ·  {HudSkillsPanel.CostLine(skill, shownRank)}", 11, VulkanPanel.Muted, 6);
        if (SkillRuleCatalog.TryGet(skill.Code, out var rule))
        {
            foreach (var detail in HudSkillDetails.Lines(rule, shownRank))
            {
                AddLine(detail, 12, MobileHudTheme.Ink, 2);
            }
        }

        var check = Check(owner, _chosen);
        AddLine(NextRank(owner, _chosen, check), 12, check == SkillTrainingCheck.Ready ? VulkanHudArt.EdgeLight : VulkanPanel.Muted, 8);
        if (rank > 0 && skill.MouseUse != SkillMouseUse.None && rule is not null)
        {
            _sheet.AddView(VulkanPanelArt.Section(_activity, "Botões de ação"), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(10) });
            var row = new LinearLayout(_activity);
            for (var slot = 0; slot < _wheel.Skills.Count; slot++)
            {
                var button = slot;
                var place = MobileHudTheme.Button(_activity, $"{slot + 1}", prominent: _wheel.SkillOn(slot) == skill.Code);
                place.ContentDescription = $"Pôr no botão {slot + 1}";
                place.Click += (_, _) =>
                {
                    _wheel.Assign(button, skill.Code);
                    _answer = $"{DataText.SkillName(skill)} no botão {button + 1}.";
                    _answerOk = true;
                    Render();
                };
                row.AddView(place, new LinearLayout.LayoutParams(0, Dp(40), 1) { RightMargin = Dp(6) });
            }

            _sheet.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        }
        else if (rank > 0 && skill.MouseUse == SkillMouseUse.None)
        {
            AddLine("Passiva: vale sempre, sem botão.", 12, VulkanPanel.Muted, 8);
        }
    }

    private void AddLine(string text, float size, Color color, int top) =>
        _sheet.AddView(VulkanPanel.Body(_activity, text, size, color), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(top) });

    // What the next rank takes, or why it cannot be trained yet.
    private string NextRank(OwnerState owner, int index, SkillTrainingCheck check)
    {
        var skill = _skills[index];
        var rank = owner.SkillRank(skill.Code);
        return check switch
        {
            SkillTrainingCheck.Ready => owner.Money >= SkillLearning.Price(index, rank)
                ? $"Rank {rank + 1}: {SkillLearning.Price(index, rank):N0} de ouro e 1 ponto{(SkillLearning.UsesElitePoints(index) ? " de elite" : "")}."
                : $"Rank {rank + 1}: faltam {SkillLearning.Price(index, rank) - owner.Money:N0} de ouro.",
            SkillTrainingCheck.TierLocked => $"Abre ao virar {DataText.ClassTitle(_class, index / SkillLearning.SkillsPerTier)}.",
            SkillTrainingCheck.MaxRank => "Rank máximo.",
            SkillTrainingCheck.NeedsPrevious => $"Aprenda antes {DataText.SkillName(_skills[index - 1])}.",
            SkillTrainingCheck.NeedsLevel => $"Rank {rank + 1} no nível {SkillLearning.LevelFor(skill, rank)}.",
            SkillTrainingCheck.NoPoints => SkillLearning.UsesElitePoints(index) ? "Sem pontos de elite." : "Sem pontos de habilidade: suba de nível.",
            _ => ClientText.Get("SkillWindow.Unavailable")
        };
    }

    // One rank of the skill, asked of the last skill master; the server refuses when the player is not near one.
    private async Task TrainAsync(int index)
    {
        if (_owner is not { } owner || !Sendable(owner) || _session.Play is not { } play)
        {
            return;
        }

        _pending = true;
        _answer = null;
        Render();
        var skill = _skills[index];
        var command = new ProgressionCommand(Guid.NewGuid(), owner.ProgressionRevision, ProgressionAction.LearnSkill, SkillCode: skill.Code,
            NpcEntityId: Master);
        var answer = await VulkanProgression.ApplyAsync(play, command, ClientText.Get("Game.ProgressionRefused"));
        _expectedRevision = answer.Revision;
        _answerOk = answer.Ok;
        _answer = answer.Ok ? $"{DataText.SkillName(skill)}: rank {owner.SkillRank(skill.Code) + 1}." : answer.Refusal;
        _pending = false;
        Render();
    }

    // No rank in flight, and the owner state of the last answer arrived.
    private bool Sendable(OwnerState owner) => !_pending && owner.ProgressionRevision >= _expectedRevision;

    private SkillTrainingCheck Check(OwnerState owner, int index) =>
        SkillLearning.Check(_skills, index, owner.SkillRank, owner.Level, owner.Tier, owner.SkillPoints, owner.EliteSkillPoints);

    // The action wheel button holding the skill, or -1.
    private int WheelSlot(uint code)
    {
        for (var slot = 0; slot < _wheel.Skills.Count; slot++)
        {
            if (code != 0 && _wheel.SkillOn(slot) == code)
            {
                return slot;
            }
        }

        return -1;
    }

    private static string RankText(int rank) => rank > 0 ? $"Rank {rank}/{SkillLearning.MaxRank}" : "Não aprendida";

    private static string Kind(Skill skill) => skill.MouseUse == SkillMouseUse.None ? "Passiva" : "Ativa";

    private int Dp(int value) => VulkanPanelArt.Dp(_activity, value);
}
