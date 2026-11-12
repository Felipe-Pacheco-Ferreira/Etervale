using Android.Graphics;
using System.Numerics;
using System.Diagnostics;
using PristonTale.Contracts.Motion;
using PristonTale.Client.Net.Presence;
using Android.Content;
using Android.Opengl;
using Android.Views;
using PristonTale.Contracts.World;
using PristonTale.Assets;

namespace PristonTale.Mobile;

internal sealed class RicartenSceneView : GLSurfaceView
{
    private RicartenCamera _camera = new();
    private int _cameraPointer = -1;
    private Vector2 _dragPosition;
    private Vector2 _touchStart;
    private bool _dragging;
    private readonly int _touchSlop;
    public event Action<ulong>? NpcSelected;
    private readonly RicartenRenderer _renderer;
    private readonly ScaleGestureDetector _zoom;
    private readonly CameraZoomListener _zoomListener;
    private bool _cameraPinch;
    private readonly Dictionary<ulong, SceneEntity> _entities = new();
    private bool _entitiesChanged;

    public RicartenSceneView(Context context, MobileScene scene, Vector3 spawn, CharacterModel? player, int face, WorldArrival arrival) : base(context)
    {
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 8;
        _sceneModels = scene.Npcs;
        _renderer = new RicartenRenderer(scene, spawn, player, face, arrival, Context!.Assets!);
        _zoomListener = new CameraZoomListener(this);
        _zoom = new ScaleGestureDetector(context, _zoomListener) { QuickScaleEnabled = false };
        SetEGLContextClientVersion(2);
        SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
        SetRenderer(_renderer);
        Clickable = true;
        ContentDescription = "Cena 3D do mapa. Arraste no lado direito para mover a câmera. Use dois dedos para ajustar o zoom.";
    }

    public void ShowArrival(Vector3 position) => QueueEvent(new Java.Lang.Runnable(() => _renderer.ShowArrival(position)));

    public void ShowNpcPortrait(ulong id, View placeholder)
    {
        if (!_entities.TryGetValue(id, out var entity) || placeholder.Width <= 0 || placeholder.Height <= 0)
        {
            return;
        }

        var mapOrigin = new int[2];
        var viewOrigin = new int[2];
        GetLocationInWindow(mapOrigin);
        placeholder.GetLocationInWindow(viewOrigin);
        _renderer.SetNpcPortrait(new NpcPortrait(entity.TemplateId, viewOrigin[0] - mapOrigin[0], viewOrigin[1] - mapOrigin[1],
            placeholder.Width, placeholder.Height));
    }

    public void ShowPlayerPortrait(View placeholder)
    {
        if (placeholder.Width <= 0 || placeholder.Height <= 0)
        {
            return;
        }
        var mapOrigin = new int[2];
        var viewOrigin = new int[2];
        GetLocationInWindow(mapOrigin);
        placeholder.GetLocationInWindow(viewOrigin);
        _renderer.SetPlayerPortrait(new NpcPortrait(0, viewOrigin[0] - mapOrigin[0], viewOrigin[1] - mapOrigin[1],
            placeholder.Width, placeholder.Height));
    }

    public void ClearNpcPortrait() => _renderer.SetNpcPortrait(null);

    public void SetRoute(Vector3[] points) => _renderer.SetRoute(points);

    public void SetLocalAppearance(PlayerAppearanceInfo appearance) => _renderer.SetLocalAppearance(appearance);

    public void SetWeather(WeatherState weather) => _renderer.SetWeather(weather);

    public void SetWorldClock(long? gameClockMs) => _renderer.SetWorldClock(gameClockMs);

    public Task FirstFrame => _renderer.FirstFrame;

    public Vector2 MovementDirection(Vector2 stick) => _camera.MovementDirection(stick);

    public void UpdatePlayer(Vector3 position, float facing, int moveSpeed, bool moving, bool running, bool snap) =>
        _renderer.SetPlayer(new PlayerFrame(position, facing, moveSpeed, moving, running, snap));

    private readonly IReadOnlyDictionary<uint, ActorPart> _sceneModels;
    private readonly Dictionary<uint, float> _npcLabelHeights = new();

    private float NpcLabelHeight(SceneEntity entity)
    {
        if (!_npcLabelHeights.TryGetValue(entity.TemplateId, out var height))
        {
            height = 0f;
            if (_sceneModels.TryGetValue(entity.TemplateId, out var model) && model.Clips[MobileActorFormat.Stand] is { } stand)
            {
                foreach (var batch in stand.Frames[0])
                {
                    for (var index = 1; index < batch.Length; index += 3)
                    {
                        height = Math.Max(height, batch[index]);
                    }
                }
            }
            height = height > 0f ? height : 36f;
            _npcLabelHeights.Add(entity.TemplateId, height);
        }
        return height * (entity.Scale > 0f ? entity.Scale : 1f) + 4f;
    }

    public void SetEntity(ulong id, Vector3 position, EntityKind kind, uint templateId, float facing, float scale, MotionState motion, string name, int life, int maxLife, PlayerAppearanceInfo? appearance)
    {
        if (appearance is not null)
        {
            _renderer.PrepareRemoteClass(Context!.Assets!, appearance.Class);
            _renderer.PrepareAppearance(appearance);
        }

        _entities[id] = new SceneEntity(id, position, kind, templateId, facing, scale, motion, position, facing, 0L, 0f, 0f, name, life, maxLife, appearance);
        _entitiesChanged = true;
    }

    public void MoveEntity(ulong id, Vector3 position, float facing, MotionState motion, int life)
    {
        if (!_entities.TryGetValue(id, out var entity))
        {
            return;
        }
        if (entity.Life != life)
        {
            entity = entity with { Life = life };
            _entities[id] = entity;
        }
        if (entity.Position != position || entity.Facing != facing || entity.Motion != motion)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = entity.UpdatedAt == 0 ? .10f : (float)Stopwatch.GetElapsedTime(entity.UpdatedAt, now).TotalSeconds;
            var displayed = entity.At(now);
            var delta = position - entity.Position;
            var speed = new Vector2(delta.X, delta.Z).Length() / Math.Max(.05f, elapsed);
            _entities[id] = entity with
            {
                Position = position, Facing = facing, Motion = motion,
                PreviousPosition = displayed.Position, PreviousFacing = displayed.Facing,
                UpdatedAt = now, BlendSeconds = Math.Clamp(elapsed, .05f, .20f), PlanarSpeed = speed
            };
            _entitiesChanged = true;
        }
    }

    public void RemoveEntity(ulong id) => _entitiesChanged |= _entities.Remove(id);

    public void PublishEntities()
    {
        if (_entitiesChanged)
        {
            _renderer.SetEntities(_entities.Values.ToArray());
            _entitiesChanged = false;
        }
    }

    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion is null)
        {
            return false;
        }

        _zoom.OnTouchEvent(motion);
        var index = motion.ActionIndex;
        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                _cameraPointer = -1;
                _cameraPinch = false;
                _dragging = false;
                _touchStart = new Vector2(motion.GetX(index), motion.GetY(index));
                goto case MotionEventActions.PointerDown;
            case MotionEventActions.PointerDown:
                _cameraPinch |= motion.PointerCount > 1;
                if (_cameraPointer < 0 && motion.GetX(index) >= Width / 2f)
                {
                    _cameraPointer = motion.GetPointerId(index);
                    _dragPosition = new Vector2(motion.GetX(index), motion.GetY(index));
                }
                break;
            case MotionEventActions.Move:
                index = motion.FindPointerIndex(_cameraPointer);
                if (!_cameraPinch && index >= 0 && Height > 0)
                {
                    var position = new Vector2(motion.GetX(index), motion.GetY(index));
                    _dragging |= Vector2.DistanceSquared(position, _touchStart) > _touchSlop * _touchSlop;
                    if (!_dragging)
                    {
                        break;
                    }
                    _camera = _camera.Drag((position - _dragPosition) / Height);
                    _dragPosition = position;
                    _renderer.SetCamera(_camera);
                }
                break;
            case MotionEventActions.Up:
                var tap = new Vector2(motion.GetX(index), motion.GetY(index));
                if (!_cameraPinch && !_dragging && Vector2.DistanceSquared(tap, _touchStart) <= _touchSlop * _touchSlop)
                {
                    SelectNpc(tap);
                }
                _cameraPointer = -1;
                break;
            case MotionEventActions.PointerUp when motion.GetPointerId(index) == _cameraPointer:
            case MotionEventActions.Cancel:
                _cameraPointer = -1;
                break;
        }
        return true;
    }

    public bool TryNpcHead(ulong id, out Vector2 point)
    {
        point = default;
        if (!_entities.TryGetValue(id, out var entity) ||
            !Project(entity.At(Stopwatch.GetTimestamp()).Position + Vector3.UnitY * NpcLabelHeight(entity), out var head))
        {
            return false;
        }
        point = new Vector2(head.X, head.Y);
        return true;
    }

    public void DrawNameplates(Canvas canvas, Paint paint, float density)
    {
        var now = Stopwatch.GetTimestamp();
        var local = _renderer.LocalPosition;
        paint.TextSize = density * 10f;
        foreach (var entity in _entities.Values)
        {
            if (entity.Kind is not (EntityKind.Player or EntityKind.Npc) || string.IsNullOrEmpty(entity.Name))
            {
                continue;
            }
            var position = entity.At(now).Position;
            if (Vector3.DistanceSquared(position, local) > 650f * 650f)
            {
                continue;
            }
            var height = entity.Kind == EntityKind.Player ? 58f : NpcLabelHeight(entity);
            if (!Project(position + Vector3.UnitY * height, out var point)
                || point.X < 0f || point.X > Width || point.Y < 0f || point.Y > Height)
            {
                continue;
            }

            var halfWidth = paint.MeasureText(entity.Name) * .5f + 5f * density;
            var bottom = point.Y - 3f * density;
            var top = bottom - 15f * density;
            var radius = 4f * density;
            paint.SetStyle(Paint.Style.Fill);
            paint.Color = Color.Argb(220, 16, 15, 18);
            canvas.DrawRoundRect(point.X - halfWidth, top, point.X + halfWidth, bottom, radius, radius, paint);
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = density * .75f;
            paint.Color = MobileHudTheme.Gold;
            canvas.DrawRoundRect(point.X - halfWidth, top, point.X + halfWidth, bottom, radius, radius, paint);
            paint.SetStyle(Paint.Style.Fill);
            paint.Color = MobileHudTheme.Ink;
            paint.TextAlign = Paint.Align.Center;
            canvas.DrawText(entity.Name, point.X, bottom - 4f * density, paint);
            if (entity.Kind == EntityKind.Player && entity.MaxLife > 0)
            {
                var left = point.X - halfWidth;
                var right = point.X + halfWidth;
                var barTop = bottom + 2f * density;
                var barBottom = barTop + 4f * density;
                paint.Color = Color.Argb(235, 23, 17, 17);
                canvas.DrawRect(left, barTop, right, barBottom, paint);
                var fraction = Math.Clamp((float)entity.Life / entity.MaxLife, 0f, 1f);
                paint.Color = Color.Rgb(111, 200, 66);
                canvas.DrawRect(left, barTop, left + (right - left) * fraction, barBottom, paint);
            }
        }
    }

    private void SelectNpc(Vector2 tap)
    {
        var nearest = float.PositiveInfinity;
        ulong? selected = null;
        foreach (var entity in _entities.Values)
        {
            if (entity.Kind != EntityKind.Npc || !Project(entity.At(Stopwatch.GetTimestamp()).Position, out var feet) || !TryNpcHead(entity.Id, out var head))
            {
                continue;
            }
            var radius = Math.Max(2f * _touchSlop, Math.Abs(feet.Y - head.Y) * .35f);
            if (tap.Y >= head.Y - _touchSlop && tap.Y <= feet.Y + _touchSlop &&
                Math.Abs(tap.X - head.X) <= radius && feet.Z < nearest)
            {
                nearest = feet.Z;
                selected = entity.Id;
            }
        }
        if (selected is { } id)
        {
            NpcSelected?.Invoke(id);
        }
    }

    public bool TrySpeakerHead(ulong id, ulong selfId, out Vector2 head)
    {
        var visible = id == selfId
            ? Project(_renderer.LocalPosition + Vector3.UnitY * 60f, out var point)
            : ProjectRemoteHead(id, out point);
        head = new Vector2(point.X, point.Y);
        return visible && head.X >= 0 && head.X <= Width && head.Y >= 0 && head.Y <= Height;
    }

    private bool ProjectRemoteHead(ulong id, out Vector3 point)
    {
        point = default;
        return _entities.TryGetValue(id, out var entity)
            && Project(entity.At(Stopwatch.GetTimestamp()).Position + Vector3.UnitY * 60f, out point);
    }

    public Vector3 CameraEye => _renderer.CameraEye;
    public Vector3 LocalPosition => _renderer.LocalPosition;

    public bool Project(Vector3 world, out Vector3 screen)
    {
        var visible = _renderer.TryProject(world, out var ndc);
        screen = new Vector3((ndc.X + 1f) * Width * .5f, (1f - ndc.Y) * Height * .5f, ndc.Z);
        return visible;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _zoom.Dispose();
            _zoomListener.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class CameraZoomListener(RicartenSceneView view) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScale(ScaleGestureDetector detector)
        {
            var scale = detector.ScaleFactor;
            if (scale > 0f && float.IsFinite(scale))
            {
                view._camera = view._camera.Zoom(scale);
                view._renderer.SetCamera(view._camera);
            }
            return true;
        }
    }
}

internal sealed record PlayerFrame(Vector3 Position, float Facing, int MoveSpeed, bool Moving, bool Running, bool Snap);

internal readonly record struct SceneEntity(ulong Id, Vector3 Position, EntityKind Kind, uint TemplateId, float Facing, float Scale,
    MotionState Motion, Vector3 PreviousPosition, float PreviousFacing, long UpdatedAt, float BlendSeconds, float PlanarSpeed, string Name, int Life, int MaxLife, PlayerAppearanceInfo? Appearance)
{
    public (Vector3 Position, float Facing) At(long now)
    {
        var amount = BlendSeconds <= 0f ? 1f : Math.Clamp((float)Stopwatch.GetElapsedTime(UpdatedAt, now).TotalSeconds / BlendSeconds, 0f, 1f);
        var turn = MathF.IEEERemainder(Facing - PreviousFacing, MathF.Tau);
        return (Vector3.Lerp(PreviousPosition, Position, amount), PreviousFacing + turn * amount);
    }
}
