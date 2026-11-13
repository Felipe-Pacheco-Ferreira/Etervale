using System.Text.Json;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

// The HUD's own layout: where the player moved each part (an offset from its place, in fractions of the screen height)
//, whether the quest card is folded and which tutorials were seen, kept in hud-layout.json in the app's files folder. Open shows the editor: every
// movable part is outlined and follows a drag; Salvar keeps the new places, Padrão puts everything back, Cancelar
// restores the places from before. Called on the UI thread.
internal sealed class HudLayoutEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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

    private sealed record Part(string Key, string Name, View View, Action<float, float> Moved);

    // What hud-layout.json holds.
    public sealed class HudLayoutFile
    {
        public Dictionary<string, float[]> Offsets { get; set; } = [];
        public bool QuestFolded { get; set; }
        // Whether the last chat lines stay off the screen (they still show in the chat window).
        public bool ChatHidden { get; set; }
        // The skills each character put on the action wheel's buttons, by character name (0 where a button fills itself).
        public Dictionary<string, uint[]> SkillSlots { get; set; } = [];
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

    public IReadOnlyList<uint> SkillSlotsOf(string character) => _file.SkillSlots.TryGetValue(character, out var codes) ? codes : [];

    public void KeepSkillSlots(string character, uint[] codes)
    {
        _file.SkillSlots[character] = codes;
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
        var offset = _file.Offsets.TryGetValue(key, out var saved) && saved.Length == 2 ? saved : [0f, 0f];
        Apply(part, offset[0], offset[1]);
    }

    public bool IsOpen => _editor is not null;

    public void Open()
    {
        if (_editor is not null)
        {
            return;
        }

        var before = Snapshot();
        _editor = new FrameLayout(_activity) { Clickable = true };
        _editor.SetBackgroundColor(Color.Argb(70, 0, 0, 0));
        var outlines = new Outlines(_activity, this);
        _editor.AddView(outlines, new FrameLayout.LayoutParams(-1, -1));

        var bar = new LinearLayout(_activity) { Background = new PlateDrawable(Dp(12)) };
        bar.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        bar.SetGravity(GravityFlags.CenterVertical);
        var hint = new TextView(_activity) { Text = "Arraste os elementos da HUD", TextSize = 13 };
        hint.SetTextColor(MobileHudTheme.Gold);
        bar.AddView(hint, new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(12) });
        AddAction(bar, "Salvar", () =>
        {
            Save();
            Close();
        });
        AddAction(bar, "Padrão", () =>
        {
            foreach (var part in _parts)
            {
                Apply(part, 0f, 0f);
            }

            outlines.Invalidate();
        });
        AddAction(bar, "Cancelar", () =>
        {
            Restore(before);
            Close();
        });
        _editor.AddView(bar, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Center));
        _overlay.AddView(_editor, new FrameLayout.LayoutParams(-1, -1));
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
    }

    // Moves a part to an offset kept on screen, and records it.
    private void Apply(Part part, float x, float y)
    {
        var view = part.View;
        if (view.Width > 0 && _overlay.Width > 0)
        {
            // Where the view would be without its offset, in the overlay's coordinates.
            view.GetLocationOnScreen(_where);
            _overlay.GetLocationOnScreen(_origin);
            var left = _where[0] - _origin[0] - view.TranslationX;
            var top = _where[1] - _origin[1] - view.TranslationY;
            x = Math.Clamp(x * _height, -left, Math.Max(-left, _overlay.Width - view.Width - left)) / _height;
            y = Math.Clamp(y * _height, -top, Math.Max(-top, _overlay.Height - view.Height - top)) / _height;
        }

        view.TranslationX = x * _height;
        view.TranslationY = y * _height;
        _file.Offsets[part.Key] = [x, y];
        part.Moved?.Invoke(x, y);
    }

    private Dictionary<string, float[]> Snapshot() => _file.Offsets.ToDictionary(entry => entry.Key, entry => (float[])entry.Value.Clone());

    private void Restore(Dictionary<string, float[]> offsets)
    {
        foreach (var part in _parts)
        {
            var offset = offsets.TryGetValue(part.Key, out var saved) && saved.Length == 2 ? saved : [0f, 0f];
            Apply(part, offset[0], offset[1]);
        }
    }

    private static HudLayoutFile Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<HudLayoutFile>(File.ReadAllText(path)) ?? new HudLayoutFile() : new HudLayoutFile();
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
        _ = Task.Run(() =>
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
        });
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

                canvas.DrawRect(_box, ReferenceEquals(part, _held) ? _shade : _line);
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
                    Invalidate();
                    return true;
                case MotionEventActions.Move when _held is not null:
                    var x = _held.View.TranslationX + e.GetX() - _lastX;
                    var y = _held.View.TranslationY + e.GetY() - _lastY;
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    editor.Apply(_held, x / editor._height, y / editor._height);
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
            box.Set(left, top, left + view.Width, top + view.Height);
            return true;
        }
    }
}
