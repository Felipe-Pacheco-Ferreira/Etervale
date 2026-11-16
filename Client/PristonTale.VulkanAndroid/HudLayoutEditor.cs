using System.Text.Json;
using PristonTale.Gameplay.Combat;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

internal sealed class HudLayoutEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static Task _pendingWrite = Task.CompletedTask;
    private static (string Path, string Text) _pendingLayout;
    private const int DefaultSize = 100;
    private const int MinimumSize = 65;
    private const int MaximumSize = 160;
    private const int MaximumTransparency = 80;
    // Raised when the tutorials change enough to be shown again to everyone.
    private const int TutorialRound = 2;

    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly float _height;
    private readonly string _path;
    private readonly List<Part> _parts = [];
    private HudLayoutFile _file;
    private readonly int[] _where = new int[2];
    private readonly int[] _origin = new int[2];
    private FrameLayout _editor;
    private Dictionary<string, Placement> _draft;

    private sealed record Part(string Key, string Name, View View, Action<float, float> Moved)
    {
        public bool Adjustable => View is VulkanActionOrb;
    }

    public readonly record struct ButtonAppearance(int Size, int Transparency);

    private readonly record struct Placement(float X, float Y, ButtonAppearance Appearance)
    {
        public static Placement Default => new(0f, 0f, new ButtonAppearance(DefaultSize, 0));
    }

    // What hud-layout.json holds.
    public sealed class HudLayoutFile
    {
        public Dictionary<string, float[]> Offsets { get; set; } = [];
        public Dictionary<string, ButtonAppearance> Buttons { get; set; } = [];
        public bool QuestFolded { get; set; }
        // Whether the last chat lines stay off the screen (they still show in the chat window).
        public bool ChatHidden { get; set; }
        // Whether the frames per second and server latency readout stays off the screen.
        public bool StatsHidden { get; set; }
        // The skills each character put on the action wheel's buttons, by character name (0 where a button fills itself).
        public Dictionary<string, uint[]> SkillSlots { get; set; } = [];
        public Dictionary<string, int> FreeHuntRadius { get; set; } = [];
        public Dictionary<string, FreeHuntSettings> FreeHuntProfiles { get; set; } = [];
        public Dictionary<string, FreeHuntSettings> SharedFreeHuntProfiles { get; set; } = [];
        public Dictionary<string, string[]> FreeHuntLogs { get; set; } = [];
        public Dictionary<string, VulkanWorldSession.HuntTotals> FreeHuntTotals { get; set; } = [];
        public List<string> Tutorials { get; set; } = [];
        // Which round of tutorials the list belongs to; an older round is cleared so the tutorials show again.
        public int TutorialRound { get; set; }
    }

    public HudLayoutEditor(Activity activity, FrameLayout overlay, float screenHeight)
    {
        _activity = activity;
        _overlay = overlay;
        _height = screenHeight;
        _path = System.IO.Path.Combine(activity.FilesDir!.AbsolutePath, "hud-layout.json");
        _file = Load(_path);
        if (_file.TutorialRound < TutorialRound)
        {
            _file.Tutorials.Clear();
            _file.TutorialRound = TutorialRound;
            Save();
        }
    }

    public bool QuestFolded
    {
        get => _file.QuestFolded;
        set
        {
            _file.QuestFolded = value;
            Save();
        }
    }

    public bool ChatHidden
    {
        get => _file.ChatHidden;
        set
        {
            _file.ChatHidden = value;
            Save();
        }
    }

    public bool StatsHidden
    {
        get => _file.StatsHidden;
        set
        {
            _file.StatsHidden = value;
            Save();
        }
    }

    public IReadOnlyList<uint> SkillSlotsOf(string character) => _file.SkillSlots.TryGetValue(character, out var codes) ? codes : [];

    public void KeepSkillSlots(string character, uint[] codes)
    {
        _file.SkillSlots[character] = codes;
        Save();
    }

    public FreeHuntSettings FreeHuntSettingsOf(string character, string account)
    {
        var personal = _file.FreeHuntProfiles.GetValueOrDefault(character) ?? (_file.FreeHuntRadius.TryGetValue(character, out var metres)
            ? new FreeHuntSettings { Radius = FreeHuntRange.Clamp(metres) * FreeHuntSettings.UnitsPerStep }
            : new FreeHuntSettings());
        return (_file.SharedFreeHuntProfiles.TryGetValue(account, out var shared)
            ? shared with { Skills = personal.Skills, SkillManaFloor = personal.SkillManaFloor } : personal with { ShareAccount = false }).UpgradeLootCategories();
    }

    public void KeepFreeHuntSettings(string character, string account, FreeHuntSettings settings)
    {
        _file.FreeHuntProfiles[character] = settings;
        if (settings.ShareAccount)
        {
            _file.SharedFreeHuntProfiles[account] = settings with { Skills = [], SkillManaFloor = [] };
        }
        else
        {
            _file.SharedFreeHuntProfiles.Remove(account);
        }

        Save();
    }

    public IReadOnlyList<string> FreeHuntLogOf(string character) => _file.FreeHuntLogs.GetValueOrDefault(character) ?? [];

    public void KeepFreeHuntLog(string character, string[] lines)
    {
        _file.FreeHuntLogs[character] = lines;
        Save();
    }

    public VulkanWorldSession.HuntTotals FreeHuntTotalsOf(string character) =>
        _file.FreeHuntTotals.GetValueOrDefault(character, VulkanWorldSession.HuntTotals.Empty);

    public void KeepFreeHuntTotals(string character, VulkanWorldSession.HuntTotals totals)
    {
        _file.FreeHuntTotals[character] = totals with { Members = [], RecentDamagePerSecond = 0, RecentExperiencePerHour = 0, RecentKills = 0 };
        Save();
    }

    public bool TutorialDone(string id) => _file.Tutorials.Contains(id);

    public void MarkTutorial(string id)
    {
        if (!_file.Tutorials.Contains(id))
        {
            _file.Tutorials.Add(id);
            Save();
        }
    }

    // Shows every tutorial again (Menu, Rever tutorial).
    public void ReplayTutorials()
    {
        _file.Tutorials.Clear();
        Save();
    }

    // Lets the player move view; moved hears the offset (fractions of the screen height) whenever it changes.
    public void Register(string key, string name, View view, Action<float, float> moved = null)
    {
        var part = new Part(key, name, view, moved);
        _parts.Add(part);
        Apply(part, SavedPlacement(part));
        if (part.Adjustable)
        {
            view.LayoutChange += (_, _) => Apply(part, CurrentPlacement(part));
        }
    }

    public bool IsOpen => _editor is not null;

    public void Open()
    {
        if (_editor is not null)
        {
            return;
        }

        _draft = _parts.ToDictionary(part => part.Key, SavedPlacement);
        _editor = new FrameLayout(_activity) { Clickable = true };
        _editor.SetBackgroundColor(Color.Argb(70, 0, 0, 0));
        var outlines = new Outlines(_activity, this);
        _editor.AddView(outlines, new FrameLayout.LayoutParams(-1, -1));

        var bar = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Background = new PlateDrawable(Dp(12)), Clickable = true };
        bar.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        var title = new TextView(_activity) { Text = "Toque para selecionar. Arraste para mover.", TextSize = 13, Gravity = GravityFlags.Center };
        title.SetTextColor(MobileHudTheme.Gold);
        bar.AddView(title, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(4) });
        DragPanelByTitle(bar, title, _editor);
        var sliders = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        bar.AddView(sliders);
        var size = AddSlider(sliders, "Tamanho", MinimumSize, MaximumSize, value => UpdateSelected(value, null));
        var transparency = AddSlider(sliders, "Transparência", 0, MaximumTransparency, value => UpdateSelected(null, value));

        void UpdateSelected(int? sizePercent, int? transparencyPercent)
        {
            if (outlines.SelectedPart is not { Adjustable: true } part)
            {
                return;
            }

            var placement = CurrentPlacement(part);
            var appearance = placement.Appearance;
            UpdateDraft(part, placement with
            {
                Appearance = new ButtonAppearance(sizePercent ?? appearance.Size, transparencyPercent ?? appearance.Transparency)
            });
            outlines.Invalidate();
        }

        void ShowSelection(Part part)
        {
            title.Text = part?.Name ?? "Toque para selecionar. Arraste para mover.";
            sliders.Visibility = part?.Adjustable == true ? ViewStates.Visible : ViewStates.Gone;
            if (part?.Adjustable == true)
            {
                var appearance = CurrentPlacement(part).Appearance;
                size.Progress = appearance.Size - MinimumSize;
                transparency.Progress = appearance.Transparency;
            }
        }

        outlines.SelectionChanged += ShowSelection;
        var actions = new LinearLayout(_activity);
        actions.SetGravity(GravityFlags.Center);
        bar.AddView(actions);
        AddAction(actions, "Salvar", () =>
        {
            foreach (var part in _parts)
            {
                var placement = CurrentPlacement(part);
                _file.Offsets[part.Key] = [placement.X, placement.Y];
                if (part.Adjustable)
                {
                    _file.Buttons[part.Key] = placement.Appearance;
                }
            }

            Save();
            Close();
        });
        AddAction(actions, "Padrão", () =>
        {
            foreach (var part in _parts)
            {
                UpdateDraft(part, Placement.Default);
            }

            ShowSelection(outlines.SelectedPart);
            outlines.Invalidate();
        });
        AddAction(actions, "Cancelar", () =>
        {
            foreach (var part in _parts)
            {
                Apply(part, SavedPlacement(part));
            }

            Close();
        });
        _editor.AddView(bar, new FrameLayout.LayoutParams(Dp(318), -2, GravityFlags.Center));
        _overlay.AddView(_editor, new FrameLayout.LayoutParams(-1, -1));
    }

    private SeekBar AddSlider(LinearLayout controls, string name, int minimum, int maximum, Action<int> changed)
    {
        var row = new LinearLayout(_activity);
        row.SetGravity(GravityFlags.CenterVertical);
        var label = new TextView(_activity) { TextSize = 12 };
        label.SetTextColor(MobileHudTheme.Ink);
        row.AddView(label, new LinearLayout.LayoutParams(Dp(122), -2));
        var tint = Android.Content.Res.ColorStateList.ValueOf(MobileHudTheme.Gold);
        var slider = new SeekBar(_activity) { Max = maximum - minimum, ContentDescription = name, ProgressTintList = tint, ThumbTintList = tint };
        row.AddView(slider, new LinearLayout.LayoutParams(0, Dp(44), 1f));
        controls.AddView(row);
        label.Text = $"{name} {minimum}%";
        slider.ProgressChanged += (_, args) =>
        {
            var value = args.Progress + minimum;
            label.Text = $"{name} {value}%";
            if (args.FromUser)
            {
                changed(value);
            }
        };
        return slider;
    }

    private static void DragPanelByTitle(View panel, View title, View bounds)
    {
        float x = 0f, y = 0f;
        void Move(float dx, float dy)
        {
            panel.TranslationX = Math.Clamp(panel.TranslationX + dx, -panel.Left, Math.Max(-panel.Left, bounds.Width - panel.Right));
            panel.TranslationY = Math.Clamp(panel.TranslationY + dy, -panel.Top, Math.Max(-panel.Top, bounds.Height - panel.Bottom));
        }

        panel.LayoutChange += (_, _) => Move(0f, 0f);
        title.Touch += (_, args) =>
        {
            var touch = args.Event;
            if (touch.ActionMasked == MotionEventActions.Move)
            {
                Move(touch.RawX - x, touch.RawY - y);
            }

            x = touch.RawX;
            y = touch.RawY;
            args.Handled = true;
        };
    }

    private void AddAction(LinearLayout bar, string name, Action action)
    {
        var button = MobileHudTheme.Button(_activity, name);
        button.Click += (_, _) => action();
        bar.AddView(button, new LinearLayout.LayoutParams(-2, Dp(40)) { LeftMargin = Dp(4) });
    }

    private void Close()
    {
        _overlay.RemoveView(_editor);
        _editor = null;
        _draft = null;
    }

    private Placement SavedPlacement(Part part)
    {
        var placement = Placement.Default;
        if (_file.Offsets.TryGetValue(part.Key, out var offset) && offset is { Length: 2 } && float.IsFinite(offset[0]) && float.IsFinite(offset[1]))
        {
            placement = placement with { X = offset[0], Y = offset[1] };
        }

        if (part.Adjustable && _file.Buttons.TryGetValue(part.Key, out var saved))
        {
            var size = saved.Size > 0 ? Math.Clamp(saved.Size, MinimumSize, MaximumSize) : DefaultSize;
            placement = placement with { Appearance = new ButtonAppearance(size, Math.Clamp(saved.Transparency, 0, MaximumTransparency)) };
        }

        return placement;
    }

    private Placement CurrentPlacement(Part part) => _draft?.TryGetValue(part.Key, out var draft) == true ? draft : SavedPlacement(part);

    private void UpdateDraft(Part part, Placement placement)
    {
        _draft[part.Key] = placement;
        Apply(part, placement);
    }

    private void Drag(Part part, float x, float y)
    {
        var placement = CurrentPlacement(part) with { X = x / _height, Y = y / _height };
        Apply(part, placement);
        _draft[part.Key] = placement with { X = part.View.TranslationX / _height, Y = part.View.TranslationY / _height };
    }

    private void Apply(Part part, Placement placement)
    {
        var view = part.View;
        var x = placement.X * _height;
        var y = placement.Y * _height;
        if (part.Adjustable)
        {
            view.ScaleX = view.ScaleY = placement.Appearance.Size / 100f;
            view.Alpha = 1f - placement.Appearance.Transparency / 100f;
        }

        if (view.Width > 0 && _overlay.Width > 0 && view.Parent is View parent)
        {
            parent.GetLocationOnScreen(_where);
            _overlay.GetLocationOnScreen(_origin);
            var left = _where[0] - _origin[0] + view.Left + view.PivotX * (1f - view.ScaleX);
            var top = _where[1] - _origin[1] + view.Top + view.PivotY * (1f - view.ScaleY);
            x = Math.Clamp(x, -left, Math.Max(-left, _overlay.Width - view.Width * view.ScaleX - left));
            y = Math.Clamp(y, -top, Math.Max(-top, _overlay.Height - view.Height * view.ScaleY - top));
        }

        view.TranslationX = x;
        view.TranslationY = y;
        part.Moved?.Invoke(x / _height, y / _height);
    }

    private static HudLayoutFile Load(string path)
    {
        try
        {
            var text = !_pendingWrite.IsCompleted && _pendingLayout.Path == path ? _pendingLayout.Text
                : File.Exists(path) ? File.ReadAllText(path) : null;
            var layout = text is null ? null : JsonSerializer.Deserialize<HudLayoutFile>(text);
            layout ??= new HudLayoutFile();
            layout.Offsets ??= [];
            layout.Buttons ??= [];
            layout.SkillSlots ??= [];
            layout.FreeHuntRadius ??= [];
            layout.FreeHuntProfiles ??= [];
            layout.SharedFreeHuntProfiles ??= [];
            layout.FreeHuntLogs ??= [];
            layout.FreeHuntTotals ??= [];
            layout.Tutorials ??= [];
            return layout;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Android.Util.Log.Warn("Etervale.Hud", $"HUD layout ignored: {ex.Message}");
            return new HudLayoutFile();
        }
    }

    private void Save()
    {
        var text = JsonSerializer.Serialize(_file, JsonOptions);
        var path = _path;
        _pendingLayout = (path, text);
        _pendingWrite = _pendingWrite.ContinueWith(_ =>
        {
            try
            {
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Android.Util.Log.Warn("Etervale.Hud", $"HUD layout not saved: {ex.Message}");
            }
        }, TaskScheduler.Default);
    }

    private int Dp(int value) => (int)MathF.Round(value * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));

    // Draws each part's outline and name, and drags the part under the finger.
    private sealed class Outlines(Activity activity, HudLayoutEditor editor) : View(activity)
    {
        private readonly Paint _line = new() { AntiAlias = true, StrokeWidth = 3f, Color = Color.Rgb(255, 214, 120) };
        private readonly Paint _shade = new() { Color = Color.Argb(60, 255, 214, 120) };
        private readonly RectF _box = new();
        private Part _held;
        private readonly int[] _here = new int[2];
        private readonly int[] _there = new int[2];
        private float _lastX;
        private float _lastY;
        public Part SelectedPart { get; private set; }
        public event Action<Part> SelectionChanged;

        protected override void OnDraw(Canvas canvas)
        {
            _line.SetStyle(Paint.Style.Stroke);
            _line.SetPathEffect(new DashPathEffect([14f, 10f], 0f));
            foreach (var part in editor._parts)
            {
                if (!Bounds(part, _box))
                {
                    continue;
                }

                if (ReferenceEquals(part, SelectedPart))
                {
                    canvas.DrawRect(_box, _shade);
                }

                canvas.DrawRect(_box, _line);
                VulkanHudArt.Label(canvas, part.Name, _box.CenterX(), _box.CenterY(), 34f, Color.White, Paint.Align.Center!);
            }
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _held = null;
                    var smallest = float.MaxValue;
                    foreach (var part in editor._parts)
                    {
                        if (!Bounds(part, _box))
                        {
                            continue;
                        }

                        var area = _box.Width() * _box.Height();
                        if (_box.Contains(e.GetX(), e.GetY()) && area < smallest)
                        {
                            _held = part;
                            smallest = area;
                        }
                    }

                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    SelectedPart = _held;
                    SelectionChanged?.Invoke(SelectedPart);
                    Invalidate();
                    return true;
                case MotionEventActions.Move when _held is not null:
                    var x = _held.View.TranslationX + e.GetX() - _lastX;
                    var y = _held.View.TranslationY + e.GetY() - _lastY;
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    editor.Drag(_held, x, y);
                    Invalidate();
                    return true;
                case MotionEventActions.Up or MotionEventActions.Cancel:
                    _held = null;
                    Invalidate();
                    return true;
                default:
                    return true;
            }
        }

        // Where a part is now in this view's coordinates, its offset included; false for a part not on screen.
        private bool Bounds(Part part, RectF box)
        {
            var view = part.View;
            if (!view.IsShown || view.Width == 0)
            {
                return false;
            }

            view.GetLocationOnScreen(_there);
            GetLocationOnScreen(_here);
            var left = _there[0] - _here[0];
            var top = _there[1] - _here[1];
            box.Set(left, top, left + view.Width * view.ScaleX, top + view.Height * view.ScaleY);
            return true;
        }
    }
}
