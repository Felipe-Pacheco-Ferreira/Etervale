using System.Numerics;
using System.Diagnostics;
using PristonTale.Contracts.Motion;
using Android.Content;
using Android.Opengl;
using Android.Views;
using PristonTale.Contracts.World;

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

    public RicartenSceneView(Context context, MobileScene scene, Vector3 spawn) : base(context)
    {
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 8;
        _renderer = new RicartenRenderer(scene, spawn);
        _zoomListener = new CameraZoomListener(this);
        _zoom = new ScaleGestureDetector(context, _zoomListener) { QuickScaleEnabled = false };
        SetEGLContextClientVersion(2);
        SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
        SetRenderer(_renderer);
        Clickable = true;
        ContentDescription = "Cena 3D de Ricarten. Arraste no lado direito para mover a câmera. Use dois dedos para ajustar o zoom.";
    }

    public Vector2 MovementDirection(Vector2 stick) => _camera.MovementDirection(stick);

    public void UpdatePlayer(Vector3 position, float facing, int moveSpeed, bool moving, bool running, bool snap) =>
        _renderer.SetPlayer(new PlayerFrame(position, facing, moveSpeed, moving, running, snap));

    public void SetEntity(ulong id, Vector3 position, EntityKind kind, uint templateId, float facing, float scale, MotionState motion)
    {
        _entities[id] = new SceneEntity(id, position, kind, templateId, facing, scale, motion, position, facing, 0L, 0f);
        _entitiesChanged = true;
    }

    public void MoveEntity(ulong id, Vector3 position, float facing, MotionState motion)
    {
        if (_entities.TryGetValue(id, out var entity) && (entity.Position != position || entity.Facing != facing || entity.Motion != motion))
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = entity.UpdatedAt == 0 ? .10f : (float)Stopwatch.GetElapsedTime(entity.UpdatedAt, now).TotalSeconds;
            var displayed = entity.At(now);
            _entities[id] = entity with
            {
                Position = position, Facing = facing, Motion = motion,
                PreviousPosition = displayed.Position, PreviousFacing = displayed.Facing,
                UpdatedAt = now, BlendSeconds = Math.Clamp(elapsed, .05f, .20f)
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
            !Project(entity.At(Stopwatch.GetTimestamp()).Position + Vector3.UnitY * (48f * Math.Max(.5f, entity.Scale)), out var head))
        {
            return false;
        }
        point = new Vector2(head.X, head.Y);
        return true;
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

    private bool Project(Vector3 world, out Vector3 screen)
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
    MotionState Motion, Vector3 PreviousPosition, float PreviousFacing, long UpdatedAt, float BlendSeconds)
{
    public (Vector3 Position, float Facing) At(long now)
    {
        var amount = BlendSeconds <= 0f ? 1f : Math.Clamp((float)Stopwatch.GetElapsedTime(UpdatedAt, now).TotalSeconds / BlendSeconds, 0f, 1f);
        var turn = MathF.IEEERemainder(Facing - PreviousFacing, MathF.Tau);
        return (Vector3.Lerp(PreviousPosition, Position, amount), PreviousFacing + turn * amount);
    }
}
