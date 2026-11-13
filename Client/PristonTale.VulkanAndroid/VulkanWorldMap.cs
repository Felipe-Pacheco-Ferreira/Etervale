using System.Numerics;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Quests;

// The full-screen map a tap on the minimap opens. The field's map fills most of the window and zooms (pinch, + and -)
// and pans (drag), and the round button centres it on the player again. Over it: the route of the automatic quest, every
// NPC in view (a name is drawn only where it does not cover another, so zooming in shows more), the player's arrow, and
// the quest's objectives: the tribe's guide as a gold star, the way out towards the hunting field, and in the hunting
// field a red round area over the places its monsters are. The side lists the objective and the NPCs, nearest first.
internal sealed class VulkanWorldMap
{
    // Names of the fields the journey goes through.
    private static readonly Dictionary<int, string> FieldTitles = new()
    {
        [PristonTale.Simulation.World.FieldCodes.Ricarten] = "Ricarten",
        [PristonTale.Simulation.World.FieldCodes.Pillai] = "Pillai",
        [PristonTale.Simulation.World.FieldCodes.ForeverFall1] = "Forever Fall 1",
        [PristonTale.Simulation.World.Fields.Forest1Field.Definition.FieldCode] = "Floresta 1"
    };

    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly VulkanWorldSession _session;
    private VulkanPanel _panel;
    private MapView _map;
    private LinearLayout _list;

    public VulkanWorldMap(Activity activity, FrameLayout overlay, VulkanWorldSession session)
    {
        _activity = activity;
        _overlay = overlay;
        _session = session;
    }

    public static string FieldTitle(int code) => FieldTitles.TryGetValue(code, out var title) ? title : $"Campo {code}";

    // The red hunting area the maps (this one and the minimap) mark in a field: the route's, while the quest still counts
    // kills and the field is the route's hunting field.
    public static HuntArea? HuntAreaIn(int field, TribeRoute route, QuestView quest) =>
        quest is { KillsNeeded: > 0 } && field == route.HuntField && route.HuntArea.Radius > 0 ? route.HuntArea : null;

    public void Open(MobileMinimap minimap, string title)
    {
        _panel?.Close();
        _panel = new VulkanPanel(_activity, _overlay, title, 4000, 4000);
        _panel.Closed += () => _panel = null;
        var body = new LinearLayout(_activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, -1));

        var mapFrame = new FrameLayout(_activity);
        _map = new MapView(_activity, minimap, _session);
        mapFrame.AddView(_map, new FrameLayout.LayoutParams(-1, -1));
        var controls = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        AddControl(controls, "+", () => _map.ZoomBy(1.5f));
        AddControl(controls, "−", () => _map.ZoomBy(1 / 1.5f));
        AddControl(controls, "◎", _map.CentreOnPlayer);
        mapFrame.AddView(controls, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Top | GravityFlags.Right) { TopMargin = Dp(6), RightMargin = Dp(6) });
        body.AddView(mapFrame, new LinearLayout.LayoutParams(0, -1, 1));

        var side = new ScrollView(_activity);
        _list = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        side.AddView(_list);
        body.AddView(side, new LinearLayout.LayoutParams(Dp(230), -1) { LeftMargin = Dp(14) });
        Refresh();
    }

    private void AddControl(LinearLayout controls, string label, Action action)
    {
        var button = MobileHudTheme.Button(_activity, label);
        button.TextSize = 18;
        button.Click += (_, _) => action();
        controls.AddView(button, new LinearLayout.LayoutParams(Dp(44), Dp(44)) { BottomMargin = Dp(6) });
    }

    // Called a few times a second on the UI thread while the map is open.
    public void Refresh()
    {
        if (_panel is null || _session.MapPose is not { } pose)
        {
            return;
        }

        _map.Invalidate();
        _list.RemoveAllViews();
        var route = AdventurerJourneyRoute.For(_session.Character.Class);
        if (_session.Quest is { } quest && quest.Progress.Count > 0)
        {
            AddLine(VulkanPanel.Heading(_activity, "Objetivo"));
            AddLine(VulkanPanel.Body(_activity, quest.Progress[^1].Title, 14));
            if (quest.KillsNeeded > 0)
            {
                AddLine(VulkanPanel.Body(_activity, $"{quest.TargetName}  {Math.Min(quest.Kills, quest.KillsNeeded)}/{quest.KillsNeeded} em {FieldTitle(route.HuntField)}",
                    13, Color.Rgb(255, 150, 130)));
            }

            AddLine(VulkanPanel.Divider(_activity), Dp(1), Dp(10));
        }

        AddLine(VulkanPanel.Heading(_activity, "NPCs por perto"));
        var here = new Vector2(pose.Position.X, pose.Position.Z);
        foreach (var mark in _session.NpcMarks.OrderBy(mark => Vector2.DistanceSquared(here, new Vector2(mark.X, mark.Z))))
        {
            var guide = mark.Name == route.GuideName;
            AddLine(VulkanPanel.Body(_activity, $"{(guide ? "★" : "•")}  {mark.Name}", 13, guide ? VulkanHudArt.EdgeLight : MobileHudTheme.Ink));
        }

        if (_session.NpcMarks.Count == 0)
        {
            AddLine(VulkanPanel.Body(_activity, "Nenhum NPC à vista.", 13, VulkanPanel.Muted));
        }

        AddLine(VulkanPanel.Divider(_activity), Dp(1), Dp(10));
        AddLine(VulkanPanel.Body(_activity, "▲ Você    ★ Guia    ● NPC\n● Área de caça    • Rota", 12, VulkanPanel.Muted));
    }

    private void AddLine(View view, int height = -2, int margin = 0) =>
        _list.AddView(view, new LinearLayout.LayoutParams(-1, height) { TopMargin = Math.Max(Dp(4), margin), BottomMargin = margin });

    private int Dp(int value) => (int)MathF.Round(value * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));

    // The field's map with its marks, zoomed and panned by touch; north up.
    private sealed class MapView : View
    {
        private const float RouteDotSpacing = 96f;
        private const float MostZoom = 8f;

        private readonly MobileMinimap _minimap;
        private readonly VulkanWorldSession _session;
        private readonly ScaleGestureDetector _pinch;
        private readonly Paint _paint = new() { AntiAlias = true, FilterBitmap = true };
        private readonly Paint _measure = new() { AntiAlias = true, FakeBoldText = true };
        private readonly RectF _box = new();
        private readonly global::Android.Graphics.Path _shape = new();
        private readonly List<RectF> _labels = [];
        private float _zoom = 1f;
        private Vector2 _centre;
        private bool _centred;
        private float _lastX;
        private float _lastY;

        public MapView(Activity activity, MobileMinimap minimap, VulkanWorldSession session) : base(activity)
        {
            _minimap = minimap;
            _session = session;
            _pinch = new ScaleGestureDetector(activity, new Pinch(this));
            var world = minimap.World;
            _centre = new Vector2(world.CenterX(), world.CenterY());
        }

        public void ZoomBy(float factor)
        {
            _zoom = Math.Clamp(_zoom * factor, 1f, MostZoom);
            Invalidate();
        }

        public void CentreOnPlayer()
        {
            if (_session.MapPose is { } pose)
            {
                _centre = new Vector2(pose.Position.X, pose.Position.Z);
                _zoom = Math.Max(_zoom, 2.5f);
                Invalidate();
            }
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            _pinch.OnTouchEvent(e);
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    break;
                case MotionEventActions.Move when !_pinch.IsInProgress && e.PointerCount == 1:
                    var scale = Scale();
                    _centre -= new Vector2((e.GetX() - _lastX) / scale, -(e.GetY() - _lastY) / scale);
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    Invalidate();
                    break;
                case MotionEventActions.PointerUp:
                    var other = e.ActionIndex == 0 ? 1 : 0;
                    _lastX = e.GetX(other);
                    _lastY = e.GetY(other);
                    break;
            }

            return true;
        }

        private sealed class Pinch(MapView map) : ScaleGestureDetector.SimpleOnScaleGestureListener
        {
            public override bool OnScale(ScaleGestureDetector detector)
            {
                map.ZoomBy(detector.ScaleFactor);
                return true;
            }
        }

        // Pixels per field unit: the whole field fits at zoom 1.
        private float Scale()
        {
            var world = _minimap.World;
            return Math.Min(Width / world.Width(), Height / world.Height()) * _zoom;
        }

        protected override void OnDraw(Canvas canvas)
        {
            if (Width == 0)
            {
                return;
            }

            var world = _minimap.World;
            var pose = _session.MapPose;
            if (!_centred && pose is not null)
            {
                _centred = true;
                _centre = new Vector2(pose.Position.X, pose.Position.Z);
                _zoom = 2f;
            }

            var scale = Scale();
            float X(float x) => Width / 2f + (x - _centre.X) * scale;
            float Y(float z) => Height / 2f - (z - _centre.Y) * scale;

            canvas.DrawColor(Color.Argb(255, 10, 12, 16));
            canvas.Save();
            canvas.ClipRect(0, 0, Width, Height);
            _box.Set(X(world.Left), Y(world.Bottom), X(world.Right), Y(world.Top));
            canvas.DrawBitmap(_minimap.Image, null, _box, _paint);

            if (pose is null)
            {
                canvas.Restore();
                return;
            }

            var route = AdventurerJourneyRoute.For(_session.Character.Class);
            var quest = _session.Quest;
            var size = Math.Max(22f, Height * .032f);
            _labels.Clear();
            DrawObjectives(canvas, pose.FieldId, route, quest, size, X, Y);
            DrawRoute(canvas, pose, X, Y);
            foreach (var mark in _session.NpcMarks)
            {
                var guide = mark.Name == route.GuideName;
                if (guide && route.Guide.FieldCode == pose.FieldId)
                {
                    continue;
                }

                _paint.Color = Color.Rgb(120, 200, 255);
                canvas.DrawCircle(X(mark.X), Y(mark.Z), 7f, _paint);
                _paint.SetStyle(Paint.Style.Stroke);
                _paint.StrokeWidth = 2f;
                _paint.Color = Color.Argb(220, 0, 0, 0);
                canvas.DrawCircle(X(mark.X), Y(mark.Z), 7f, _paint);
                _paint.SetStyle(Paint.Style.Fill);
                Label(canvas, mark.Name, X(mark.X), Y(mark.Z) - size, size * .85f, Color.White);
            }

            DrawPlayer(canvas, pose, X, Y, size);
            canvas.Restore();
            _paint.SetStyle(Paint.Style.Stroke);
            _paint.StrokeWidth = 3f;
            _paint.Color = VulkanHudArt.Edge;
            canvas.DrawRect(1.5f, 1.5f, Width - 1.5f, Height - 1.5f, _paint);
            _paint.SetStyle(Paint.Style.Fill);
        }

        private void DrawObjectives(Canvas canvas, int field, TribeRoute route, QuestView quest, float size, Func<float, float> x, Func<float, float> y)
        {
            if (HuntAreaIn(field, route, quest) is { } area)
            {
                var radius = area.Radius * Scale();
                _paint.SetShader(new RadialGradient(x(area.X), y(area.Z), radius,
                    [Color.Argb(110, 255, 60, 40).ToArgb(), Color.Argb(60, 220, 30, 20).ToArgb()], null, Shader.TileMode.Clamp!));
                canvas.DrawCircle(x(area.X), y(area.Z), radius, _paint);
                _paint.SetShader(null);
                _paint.SetStyle(Paint.Style.Stroke);
                _paint.StrokeWidth = 4f;
                _paint.Color = Color.Rgb(255, 90, 70);
                _paint.SetPathEffect(new DashPathEffect([18f, 10f], 0f));
                canvas.DrawCircle(x(area.X), y(area.Z), radius, _paint);
                _paint.SetPathEffect(null);
                _paint.SetStyle(Paint.Style.Fill);
                Label(canvas, $"Caça: {quest.TargetName} {Math.Min(quest.Kills, quest.KillsNeeded)}/{quest.KillsNeeded}", x(area.X), y(area.Z), size,
                    Color.Rgb(255, 190, 170), always: true);
            }
            else if (quest is { KillsNeeded: > 0 } && route.ExitTowards(field, route.HuntField) is { } exit)
            {
                Star(canvas, x(exit.Point.X), y(exit.Point.Z), size * .7f, Color.Rgb(255, 120, 80));
                Label(canvas, $"Saída para {FieldTitle(exit.To)}", x(exit.Point.X), y(exit.Point.Z) - size * 1.1f, size, Color.Rgb(255, 190, 170), always: true);
            }

            if (route.Guide.FieldCode == field)
            {
                Star(canvas, x(route.Guide.X), y(route.Guide.Z), size * .8f, Color.Rgb(255, 214, 64));
                Label(canvas, $"Guia: {route.GuideName}", x(route.Guide.X), y(route.Guide.Z) - size * 1.2f, size, VulkanHudArt.EdgeLight, always: true);
            }
        }

        private void DrawRoute(Canvas canvas, VulkanWorldSession.WorldMapPose pose, Func<float, float> x, Func<float, float> y)
        {
            _paint.Color = Color.Rgb(255, 214, 64);
            var from = new Vector2(pose.Position.X, pose.Position.Z);
            foreach (var to in _minimap.Route)
            {
                var dots = Math.Max(1, (int)(Vector2.Distance(from, to) / RouteDotSpacing));
                for (var index = 1; index <= dots; index++)
                {
                    var point = Vector2.Lerp(from, to, index / (float)dots);
                    canvas.DrawCircle(x(point.X), y(point.Y), 4f, _paint);
                }

                from = to;
            }
        }

        private void DrawPlayer(Canvas canvas, VulkanWorldSession.WorldMapPose pose, Func<float, float> x, Func<float, float> y, float size)
        {
            var cx = x(pose.Position.X);
            var cy = y(pose.Position.Z);
            canvas.Save();
            canvas.Rotate(pose.Facing * 180f / MathF.PI, cx, cy);
            _shape.Reset();
            _shape.MoveTo(cx, cy - size * .7f);
            _shape.LineTo(cx + size * .5f, cy + size * .6f);
            _shape.LineTo(cx, cy + size * .3f);
            _shape.LineTo(cx - size * .5f, cy + size * .6f);
            _shape.Close();
            _paint.Color = Color.Rgb(255, 120, 60);
            canvas.DrawPath(_shape, _paint);
            _paint.SetStyle(Paint.Style.Stroke);
            _paint.StrokeWidth = 3f;
            _paint.Color = Color.White;
            canvas.DrawPath(_shape, _paint);
            _paint.SetStyle(Paint.Style.Fill);
            canvas.Restore();
        }

        // A five-pointed star with a dark outline.
        private void Star(Canvas canvas, float cx, float cy, float radius, Color color)
        {
            _shape.Reset();
            for (var point = 0; point < 10; point++)
            {
                var reach = point % 2 == 0 ? radius : radius * .45f;
                var (sin, cos) = MathF.SinCos(point * MathF.PI / 5f - MathF.PI / 2f);
                if (point == 0)
                {
                    _shape.MoveTo(cx + cos * reach, cy + sin * reach);
                }
                else
                {
                    _shape.LineTo(cx + cos * reach, cy + sin * reach);
                }
            }

            _shape.Close();
            _paint.Color = color;
            canvas.DrawPath(_shape, _paint);
            _paint.SetStyle(Paint.Style.Stroke);
            _paint.StrokeWidth = 3f;
            _paint.Color = Color.Argb(230, 30, 18, 6);
            canvas.DrawPath(_shape, _paint);
            _paint.SetStyle(Paint.Style.Fill);
        }

        // A name centred at (x, y), skipped when it would cover one already drawn (unless always).
        private void Label(Canvas canvas, string text, float x, float y, float size, Color color, bool always = false)
        {
            _measure.TextSize = size;
            var half = _measure.MeasureText(text) / 2f + 4f;
            var box = new RectF(x - half, y - size * .6f, x + half, y + size * .6f);
            if (!always)
            {
                foreach (var placed in _labels)
                {
                    if (RectF.Intersects(placed, box))
                    {
                        return;
                    }
                }
            }

            _labels.Add(box);
            VulkanHudArt.Label(canvas, text, x, y, size, color, Paint.Align.Center!);
        }
    }
}
