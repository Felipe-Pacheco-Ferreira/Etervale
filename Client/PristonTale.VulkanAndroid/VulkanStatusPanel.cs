using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using Path = Android.Graphics.Path;

// The character's status, a HUD window (VulkanPanel) opened from the player frame: on the left the level medallion, the
// class, experience and the vitals as gauges; in the middle the five attributes, each with its emblem, what it raises, a
// glowing bar against the strongest one and a "+" that spends one free point on it (the class's damage attribute marked);
// on the right the combat values as tiles and a line for tips and the server's answers. A point is a progression command
// the server validates (ProgressionAction.AllocateAttribute); the window redraws from the owner state that follows.
internal sealed class VulkanStatusPanel
{
    private const int PointButtonSize = 40;

    private static readonly CharacterClassRegistry Classes = new();

    // The attributes in the order of CharacterAttribute: name, what they raise, their gauge and their value.
    private static readonly (CharacterAttribute Attribute, string Name, string Effect, VulkanHudArt.Gauge Gauge, Func<OwnerState, int> Value)[] Attributes =
    [
        (CharacterAttribute.Strength, "Força", "Dano corpo a corpo, absorção, carga",
            new VulkanHudArt.Gauge(Color.Rgb(240, 112, 78), Color.Rgb(136, 32, 18)), static owner => owner.Strength),
        (CharacterAttribute.Spirit, "Espírito", "Mana, dano mágico",
            new VulkanHudArt.Gauge(Color.Rgb(120, 172, 255), Color.Rgb(30, 64, 160)), static owner => owner.Spirit),
        (CharacterAttribute.Talent, "Talento", "Precisão, defesa, absorção",
            new VulkanHudArt.Gauge(Color.Rgb(204, 150, 255), Color.Rgb(84, 36, 150)), static owner => owner.Talent),
        (CharacterAttribute.Agility, "Agilidade", "Precisão, defesa, dano à distância",
            new VulkanHudArt.Gauge(Color.Rgb(146, 222, 110), Color.Rgb(36, 112, 40)), static owner => owner.Agility),
        (CharacterAttribute.Health, "Vitalidade", "Vida, estamina, carga",
            new VulkanHudArt.Gauge(Color.Rgb(255, 196, 92), Color.Rgb(160, 92, 16)), static owner => owner.Health)
    ];

    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly VulkanWorldSession _session;
    // The combat values, shown two to a row: label and value.
    private static readonly (string Label, Func<OwnerState, string> Value)[] CombatValues =
    [
        ("Ataque", static owner => $"{owner.DamageMin} – {owner.DamageMax}"),
        ("Precisão", static owner => owner.AttackRating.ToString()),
        ("Defesa", static owner => owner.Defence.ToString()),
        ("Absorção", static owner => owner.Absorption.ToString()),
        ("Crítico", static owner => $"{owner.CriticalChance}%"),
        ("Bloqueio", static owner => $"{owner.BlockChance}%"),
        ("Movimento", static owner => owner.MoveSpeed.ToString()),
        ("Pts. de habilidade", static owner => owner.SkillPoints.ToString())
    ];

    private readonly int[] _shown = new int[Attributes.Length];
    private readonly int[] _amounts = new int[Attributes.Length];
    private readonly TextView[] _values = new TextView[Attributes.Length];
    private readonly VulkanGaugeView[] _bars = new VulkanGaugeView[Attributes.Length];
    private readonly VulkanPointButton[] _plus = new VulkanPointButton[Attributes.Length];
    private readonly VulkanPointButton[] _minus = new VulkanPointButton[Attributes.Length];
    // The points planned on each attribute: + and - move them, nothing reaches the server until Confirmar.
    private readonly int[] _planned = new int[Attributes.Length];
    private Button _confirm;
    private Button _undo;
    private readonly TextView[] _combat = new TextView[CombatValues.Length];
    private VulkanPanel _panel;
    private LevelMedallion _medallion;
    private TextView _title;
    private TextView _rank;
    private TextView _points;
    private TextView _note;
    private VulkanGaugeView _experience;
    private VulkanGaugeView _life;
    private VulkanGaugeView _mana;
    private VulkanGaugeView _stamina;
    private VulkanGaugeView _weight;
    private OwnerState _owner;
    private bool _pending;
    private long _expectedRevision;
    private string _answer;
    private bool _answerOk;

    public VulkanStatusPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session)
    {
        _activity = activity;
        _overlay = overlay;
        _session = session;
        Main = Classes.DamageAttribute(session.Character.Class);
        var title = ClassProfiles.Of(session.Character.Class).TitleAt(session.Character.Tier);
        Advice = Main switch
        {
            CharacterAttribute.Spirit => $"Como {title}, Espírito aumenta seu dano mágico e sua mana. Vitalidade dá vida; Talento e Agilidade, precisão "
                + "e defesa; Força, carga.",
            CharacterAttribute.Agility => $"Como {title}, Agilidade aumenta seu dano e sua precisão. Vitalidade dá vida; Força e Talento somam dano "
                + "e absorção; Espírito, mana.",
            _ => $"Como {title}, Força aumenta seu dano. Vitalidade dá vida e estamina; Talento e Agilidade, precisão e defesa; Espírito, mana."
        };
    }

    public bool IsOpen => _panel is not null;

    // The points spent from this window since it last opened, for the tutorial.
    public int Spent { get; private set; }

    // The class's damage attribute (CharacterClassRegistry.DamageAttribute), its name and what each attribute does for the
    // class, for the tutorial.
    public CharacterAttribute Main { get; }

    public string MainName => Attributes[(int)Main].Name;

    public string Advice { get; }

    // The free points counter, or null while the window is closed.
    public View PointsView => _panel is null ? null : _points;

    // The "+" of an attribute, or null while the window is closed.
    public View PlusOf(CharacterAttribute attribute) => _panel is null ? null : _plus[(int)attribute];

    public void Open()
    {
        if (_panel is not null || _session.Owner is null)
        {
            return;
        }

        _owner = _session.Owner;
        Spent = 0;
        Array.Clear(_planned);
        _answer = null;
        Array.Fill(_shown, -1);
        Build();
        Render();
    }

    public void Update(OwnerState state)
    {
        _owner = state;
        if (_panel is not null)
        {
            Render();
        }
    }

    private void Build()
    {
        _panel = new VulkanPanel(_activity, _overlay, _session.Character.Name, 800, 360);
        _panel.Closed += () => _panel = null;
        var body = new LinearLayout(_activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, -1));
        body.AddView(BuildHero(), new LinearLayout.LayoutParams(Dp(188), -1) { RightMargin = Dp(14) });
        body.AddView(BuildAttributes(), new LinearLayout.LayoutParams(0, -1, 1.25f) { RightMargin = Dp(14) });
        body.AddView(BuildCombat(), new LinearLayout.LayoutParams(0, -1, 1));
    }

    // The medallion with the class and level beside it, experience, and the vitals with the carried weight.
    private View BuildHero()
    {
        var hero = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var identity = new LinearLayout(_activity);
        identity.SetGravity(GravityFlags.CenterVertical);
        _medallion = new LevelMedallion(_activity);
        identity.AddView(_medallion, new LinearLayout.LayoutParams(Dp(62), Dp(62)) { RightMargin = Dp(8) });
        var names = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        _title = VulkanPanel.Text(_activity, "", 16, MobileHudTheme.Gold, serif: true);
        names.AddView(_title);
        _rank = VulkanPanel.Body(_activity, "", 12, VulkanPanel.Muted);
        names.AddView(_rank, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(3) });
        identity.AddView(names, new LinearLayout.LayoutParams(0, -2, 1));
        hero.AddView(identity);
        _experience = Gauge(hero, 16, 8);
        hero.AddView(VulkanPanelArt.Section(_activity, "Vitais"), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });
        _life = Gauge(hero, 18, 6);
        _mana = Gauge(hero, 18, 5);
        _stamina = Gauge(hero, 18, 5);
        _weight = Gauge(hero, 14, 8);
        return hero;

        VulkanGaugeView Gauge(LinearLayout parent, int height, int top)
        {
            var gauge = new VulkanGaugeView(_activity, eased: true);
            parent.AddView(gauge, new LinearLayout.LayoutParams(-1, Dp(height)) { TopMargin = Dp(top) });
            return gauge;
        }
    }

    // The attributes' rows under their heading, whose note is the free points counter; they scroll if the window is short.
    private View BuildAttributes()
    {
        var column = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        _points = VulkanPanelArt.Chip(_activity, "", VulkanHudArt.EdgeLight);
        column.AddView(VulkanPanelArt.Section(_activity, "Atributos", _points));
        var scroll = new ScrollView(_activity) { VerticalScrollBarEnabled = false };
        var rows = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        scroll.AddView(rows);
        column.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(6) });
        for (var index = 0; index < Attributes.Length; index++)
        {
            rows.AddView(AttributeRow(index), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(3) });
        }

        return column;
    }

    // An attribute: its emblem; its name (with a chip on the class's damage attribute), value, bar and what it raises; its "+".
    private View AttributeRow(int index)
    {
        var (attribute, name, effect, gauge, _) = Attributes[index];
        var main = attribute == Main;
        var row = new LinearLayout(_activity) { Background = VulkanPanelArt.Inset(_activity, lit: main) };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(6), Dp(2), Dp(4), Dp(2));
        row.AddView(new AttributeEmblem(_activity, attribute, gauge), new LinearLayout.LayoutParams(Dp(30), Dp(30)) { RightMargin = Dp(8) });

        var text = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var line = new LinearLayout(_activity);
        line.SetGravity(GravityFlags.CenterVertical);
        line.AddView(VulkanPanel.Text(_activity, name, 14, MobileHudTheme.Ink, serif: true));
        if (main)
        {
            line.AddView(VulkanPanelArt.Chip(_activity, "Dano", VulkanHudArt.EdgeLight), new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(6) });
        }

        line.AddView(new View(_activity), new LinearLayout.LayoutParams(0, 1, 1));
        _values[index] = VulkanPanel.Text(_activity, "", 15, Color.White, serif: true);
        line.AddView(_values[index]);
        text.AddView(line);
        _bars[index] = new VulkanGaugeView(_activity, eased: true);
        text.AddView(_bars[index], new LinearLayout.LayoutParams(-1, Dp(6)) { TopMargin = Dp(3) });
        var hint = VulkanPanel.Body(_activity, effect, 10, VulkanPanel.Muted);
        hint.SetSingleLine(true);
        hint.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        text.AddView(hint, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(2) });
        row.AddView(text, new LinearLayout.LayoutParams(0, -2, 1));

        var minus = new VulkanPointButton(_activity) { Minus = true, ContentDescription = $"Tirar um ponto de {name}" };
        minus.Click += (_, _) => Plan(index, -1);
        _minus[index] = minus;
        row.AddView(minus, new LinearLayout.LayoutParams(Dp(PointButtonSize), Dp(PointButtonSize)) { LeftMargin = Dp(6) });
        var plus = new VulkanPointButton(_activity) { ContentDescription = $"Somar um ponto em {name}" };
        plus.Click += (_, _) => Plan(index, 1);
        _plus[index] = plus;
        row.AddView(plus, new LinearLayout.LayoutParams(Dp(PointButtonSize), Dp(PointButtonSize)) { LeftMargin = Dp(6) });
        return row;
    }

    // The combat values as tiles, two to a row, and the line for tips and answers under them.
    private View BuildCombat()
    {
        var column = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        column.AddView(VulkanPanelArt.Section(_activity, "Combate"));
        LinearLayout row = null;
        for (var index = 0; index < CombatValues.Length; index++)
        {
            if (index % 2 == 0)
            {
                row = new LinearLayout(_activity);
                column.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(5) });
            }

            row.AddView(Tile(index), new LinearLayout.LayoutParams(0, -2, 1) { LeftMargin = index % 2 == 0 ? 0 : Dp(5) });
        }

        _note = VulkanPanel.Body(_activity, "", 12, VulkanPanel.Muted);
        _note.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        _note.Background = VulkanPanelArt.Inset(_activity);
        column.AddView(_note, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        var actions = new LinearLayout(_activity);
        _undo = MobileHudTheme.Button(_activity, "Desfazer");
        _undo.Click += (_, _) =>
        {
            Array.Clear(_planned);
            Render();
        };
        _confirm = MobileHudTheme.Button(_activity, "Confirmar", prominent: true);
        _confirm.Click += async (_, _) => await ConfirmAsync();
        actions.AddView(_undo, new LinearLayout.LayoutParams(0, Dp(42), 1) { RightMargin = Dp(6) });
        actions.AddView(_confirm, new LinearLayout.LayoutParams(0, Dp(42), 1.4f));
        column.AddView(actions, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        return column;

        View Tile(int index)
        {
            var tile = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Background = VulkanPanelArt.Inset(_activity) };
            tile.SetPadding(Dp(8), Dp(4), Dp(8), Dp(5));
            var caption = VulkanPanel.Body(_activity, CombatValues[index].Label, 10, VulkanPanel.Muted);
            caption.SetSingleLine(true);
            caption.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            tile.AddView(caption);
            var value = VulkanPanel.Text(_activity, "", 16, Color.White, serif: true);
            tile.AddView(value, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(2) });
            _combat[index] = value;
            return tile;
        }
    }

    private void Render()
    {
        if (_panel is null || _owner is not { } owner)
        {
            return;
        }

        _medallion.Level = owner.Level;
        _title.Text = ClassProfiles.Of(_session.Character.Class).TitleAt(owner.Tier);
        _rank.Text = $"Nível {owner.Level}";
        var span = owner.NextLevel > owner.LevelStart ? owner.NextLevel - owner.LevelStart : 1UL;
        var progress = owner.Experience > owner.LevelStart ? Math.Min(1d, (owner.Experience - owner.LevelStart) / (double)span) : 0d;
        _experience.Set(VulkanHudArt.Experience, (float)progress, owner.Level >= ExperienceTable.MaximumLevel ? "Nível máximo" : $"Experiência {progress:P1}");
        _life.Set(VulkanHudArt.Life, Share(owner.Life, owner.MaxLife), $"Vida {owner.Life} / {owner.MaxLife}");
        _mana.Set(VulkanHudArt.Mana, Share(owner.Mana, owner.MaxMana), $"Mana {owner.Mana} / {owner.MaxMana}");
        _stamina.Set(VulkanHudArt.Stamina, Share(owner.Stamina, owner.MaxStamina), $"Estamina {owner.Stamina} / {owner.MaxStamina}");
        _weight.Set(owner.Overweight ? VulkanPanelArt.Overweight : VulkanHudArt.Experience, Share(owner.CarriedWeight, owner.MaxWeight),
            $"Carga {owner.CarriedWeight} / {owner.MaxWeight}");

        var strongest = 1;
        for (var index = 0; index < Attributes.Length; index++)
        {
            _amounts[index] = Attributes[index].Value(owner);
            strongest = Math.Max(strongest, _amounts[index]);
        }

        var planned = Planned;
        var free = Math.Max(0, owner.StatPoints - planned);
        var ready = !_pending && owner.ProgressionRevision >= _expectedRevision;
        for (var index = 0; index < _amounts.Length; index++)
        {
            var shown = _amounts[index] + _planned[index];
            strongest = Math.Max(strongest, shown);
            _values[index].Text = _planned[index] > 0 ? $"{_amounts[index]} +{_planned[index]}" : _amounts[index].ToString();
            _values[index].SetTextColor(_planned[index] > 0 ? VulkanPanelArt.Done : Color.White);
            _bars[index].Set(Attributes[index].Gauge, shown / (float)strongest, "");
            if (_shown[index] >= 0 && shown > _shown[index])
            {
                Pop(_values[index]);
            }

            _shown[index] = shown;
            _plus[index].Enabled = ready && free > 0;
            _minus[index].Enabled = ready && _planned[index] > 0;
            _minus[index].Visibility = _planned[index] > 0 ? ViewStates.Visible : ViewStates.Invisible;
        }

        _points.Text = free == 1 ? "1 ponto livre" : $"{free} pontos livres";
        _points.Alpha = free > 0 ? 1f : .45f;
        _confirm.Enabled = ready && planned > 0;
        _confirm.Alpha = _confirm.Enabled ? 1f : .45f;
        _confirm.Text = planned > 0 ? $"Confirmar ({planned})" : "Confirmar";
        _undo.Enabled = ready && planned > 0;
        _undo.Alpha = _undo.Enabled ? 1f : .45f;
        VulkanAttention.Gold(_confirm, _confirm.Enabled, Dp(10));
        for (var index = 0; index < CombatValues.Length; index++)
        {
            _combat[index].Text = CombatValues[index].Value(owner);
        }

        if (_answer is not null)
        {
            _note.Text = _answer;
            _note.SetTextColor(_answerOk ? VulkanPanelArt.Done : VulkanPanelArt.Warning);
        }
        else
        {
            _note.Text = planned > 0
                ? "Os pontos só valem depois de Confirmar; até lá, - devolve e Desfazer limpa tudo."
                : owner.StatPoints > 0
                    ? $"Toque em + para reservar pontos e depois em Confirmar. Para seu dano, {MainName}; para aguentar mais, Vitalidade."
                    : "Subir de nível dá pontos para distribuir aqui.";
            _note.SetTextColor(VulkanPanel.Muted);
        }

        static float Share(long value, long max) => max > 0 ? value / (float)max : 0f;
    }

    // The points planned and not confirmed yet.
    public int Planned => _planned.Sum();

    // The Confirmar button, or null while the window is closed.
    public View ConfirmView => _panel is null ? null : _confirm;

    // A point planned on (step 1) or taken back from (step -1) an attribute, within the free points.
    private void Plan(int index, int step)
    {
        if (_owner is not { } owner || _planned[index] + step < 0 || step > 0 && Planned >= owner.StatPoints)
        {
            return;
        }

        _planned[index] += step;
        _answer = null;
        Render();
    }

    // The planned points, attribute by attribute, each as one command the server validates (its amount the points
    // planned there); a refusal stops the rest, which stay planned. The window redraws from the owner states that follow.
    private async Task ConfirmAsync()
    {
        if (_pending || Planned == 0 || _owner is not { } owner || owner.ProgressionRevision < _expectedRevision || _session.Play is not { } play)
        {
            return;
        }

        _pending = true;
        _answer = null;
        Render();
        var revision = owner.ProgressionRevision;
        var given = 0;
        _answerOk = true;
        for (var index = 0; index < _planned.Length && _answerOk; index++)
        {
            if (_planned[index] == 0)
            {
                continue;
            }

            var command = new ProgressionCommand(Guid.NewGuid(), revision, ProgressionAction.AllocateAttribute, Attributes[index].Attribute,
                _planned[index]);
            var answer = await VulkanProgression.ApplyAsync(play, command, "O servidor recusou os pontos.");
            _answerOk = answer.Ok;
            if (answer.Ok)
            {
                revision = answer.Revision;
                _expectedRevision = answer.Revision;
                given += _planned[index];
                _planned[index] = 0;
            }
            else
            {
                _answer = answer.Refusal;
            }
        }

        Spent += given;
        if (_answerOk)
        {
            _answer = given == 1 ? "1 ponto confirmado." : $"{given} pontos confirmados.";
        }

        _pending = false;
        Render();
    }

    // A value that just grew swells and settles.
    private static void Pop(View value)
    {
        value.ScaleX = 1.35f;
        value.ScaleY = 1.35f;
        value.Animate()?.ScaleX(1f).ScaleY(1f).SetDuration(320).Start();
    }

    private int Dp(int value) => VulkanPanelArt.Dp(_activity, value);

    // The level on the HUD's medallion (VulkanHudArt.LevelBadge), its stone breathing.
    private sealed class LevelMedallion(Context context) : View(context)
    {
        private const int FrameMs = 40;
        private const float Loop = 600f;
        private int _level = 1;
        private string _text = "1";

        public int Level
        {
            set
            {
                if (value != _level)
                {
                    _level = value;
                    _text = value.ToString();
                    Invalidate();
                }
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            var phase = Android.OS.SystemClock.ElapsedRealtimeNanos() % (long)(Loop * 1e9f) / 1e9f;
            var radius = Math.Min(Width, Height) / 2f / 1.75f;
            VulkanHudArt.LevelBadge(canvas, Width / 2f, Height / 2f, radius, _text, _level, ExperienceTable.MaximumLevel, phase);
            PostInvalidateDelayed(FrameMs);
        }
    }

    // An attribute's emblem: a dark disc in a gold ring with its sign in the attribute's colours (a sword for strength,
    // a flame for spirit, a star for talent, a bolt for agility, a heart for vitality), on a 24-unit grid.
    private sealed class AttributeEmblem : View
    {
        private readonly Path _sign = new();
        private readonly Paint _fill = new() { AntiAlias = true };
        private readonly Paint _edge = new() { AntiAlias = true };
        private readonly Matrix _scale = new();
        private readonly Path _scaled = new();
        private readonly VulkanHudArt.Gauge _gauge;

        public AttributeEmblem(Context context, CharacterAttribute attribute, VulkanHudArt.Gauge gauge) : base(context)
        {
            _gauge = gauge;
            _edge.SetStyle(Paint.Style.Stroke);
            _edge.Color = Color.Argb(220, 20, 12, 6);
            switch (attribute)
            {
                case CharacterAttribute.Strength:
                    _sign.MoveTo(12, 2.5f);
                    _sign.LineTo(14.2f, 5.5f);
                    _sign.LineTo(14.2f, 14.5f);
                    _sign.LineTo(17.5f, 14.5f);
                    _sign.LineTo(17.5f, 16.8f);
                    _sign.LineTo(13.3f, 16.8f);
                    _sign.LineTo(13.3f, 21.5f);
                    _sign.LineTo(10.7f, 21.5f);
                    _sign.LineTo(10.7f, 16.8f);
                    _sign.LineTo(6.5f, 16.8f);
                    _sign.LineTo(6.5f, 14.5f);
                    _sign.LineTo(9.8f, 14.5f);
                    _sign.LineTo(9.8f, 5.5f);
                    break;
                case CharacterAttribute.Spirit:
                    _sign.MoveTo(12, 2.5f);
                    _sign.CubicTo(17, 8, 19, 12, 17, 16.5f);
                    _sign.CubicTo(15.5f, 20, 8.5f, 20.5f, 7, 16.5f);
                    _sign.CubicTo(6, 13.5f, 8, 11, 9.5f, 9.5f);
                    _sign.CubicTo(9.5f, 12, 10.5f, 13.5f, 12, 14);
                    _sign.CubicTo(13, 10.5f, 11, 7, 12, 2.5f);
                    break;
                case CharacterAttribute.Talent:
                    for (var point = 0; point < 10; point++)
                    {
                        var (sin, cos) = MathF.SinCos(-MathF.PI / 2f + point * MathF.PI / 5f);
                        var reach = point % 2 == 0 ? 9.5f : 4f;
                        if (point == 0)
                        {
                            _sign.MoveTo(12 + cos * reach, 12.5f + sin * reach);
                        }
                        else
                        {
                            _sign.LineTo(12 + cos * reach, 12.5f + sin * reach);
                        }
                    }

                    break;
                case CharacterAttribute.Agility:
                    _sign.MoveTo(14, 2.5f);
                    _sign.LineTo(5.5f, 13.5f);
                    _sign.LineTo(11.5f, 13.5f);
                    _sign.LineTo(9.5f, 21.5f);
                    _sign.LineTo(18.5f, 10);
                    _sign.LineTo(12.5f, 10);
                    break;
                default:
                    _sign.MoveTo(12, 20.5f);
                    _sign.CubicTo(4, 15, 2.5f, 10, 5.5f, 6.5f);
                    _sign.CubicTo(8, 4, 11, 5.5f, 12, 8);
                    _sign.CubicTo(13, 5.5f, 16, 4, 18.5f, 6.5f);
                    _sign.CubicTo(21.5f, 10, 20, 15, 12, 20.5f);
                    break;
            }

            _sign.Close();
        }

        protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
        {
            base.OnSizeChanged(width, height, oldWidth, oldHeight);
            var size = Math.Min(width, height);
            var inset = size * .2f;
            _scale.SetScale((size - inset * 2f) / 24f, (size - inset * 2f) / 24f);
            _scale.PostTranslate((width - size) / 2f + inset, (height - size) / 2f + inset);
            _sign.Transform(_scale, _scaled);
            _fill.SetShader(new LinearGradient(0, inset, 0, size - inset,
                [Color.Rgb(Math.Min(255, _gauge.Top.R + 60), Math.Min(255, _gauge.Top.G + 60), Math.Min(255, _gauge.Top.B + 60)).ToArgb(), _gauge.Bottom.ToArgb()],
                null, Shader.TileMode.Clamp!));
            _edge.StrokeWidth = Math.Max(1f, size * .03f);
        }

        protected override void OnDraw(Canvas canvas)
        {
            var radius = Math.Min(Width, Height) / 2f;
            VulkanHudArt.Orb(canvas, Width / 2f, Height / 2f, radius, enabled: true, pressed: false);
            canvas.DrawPath(_scaled, _fill);
            canvas.DrawPath(_scaled, _edge);
        }
    }
}
