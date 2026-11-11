using System.Numerics;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Sessions;
using PristonTale.Simulation.Characters;
using Matrix = System.Numerics.Matrix4x4;

namespace PristonTale.Mobile.CharacterSelect;

internal readonly record struct CharSelectInfo(string Name, int Level, string BroodName, string ClassName, string ClanName);

// The desktop CharacterSelectScene on the baked scene: the account's characters and the class lineup on the stage, the
// chosen class's walk forward, picking and highlights. Members keep the desktop's names so the two can be read side by
// side. It differs where the baked data does (docs/android.md, "Differences from the desktop"): a character wears its
// class's default body, not its armour.
internal sealed class CharSelectScene
{
    private const float FloorProbeY = 200f;
    private static readonly Matrix MirrorZ = Matrix.CreateScale(1f, 1f, -1f);
    private static readonly Vector3 Ambient = new(CharacterSelectStage.Ambient.R, CharacterSelectStage.Ambient.G, CharacterSelectStage.Ambient.B);

    private enum JobMovePhase { None, TurnToHeading, Running, TurnToCamera }

    private sealed class Actor(PlayerClass playerClass, int face, Matrix world)
    {
        public PlayerClass Class { get; } = playerClass;
        public int Face { get; set; } = face;
        public Matrix World { get; set; } = world;
        public bool Running { get; private set; }
        public float Seconds { get; private set; }
        public float Glow { get; set; }

        public void SetMotion(bool running)
        {
            if (running != Running)
            {
                Running = running;
                Seconds = 0f;
            }
        }

        public void Advance(float dt) => Seconds += dt;
    }

    private sealed record Placed(CharacterSummary Summary, (float X, float Z) Slot, Actor Actor);

    private sealed record JobActor(PlayerClass Class, int SlotIndex, Actor Actor);

    private readonly CharSelectCamera _camera;
    private readonly CharacterClassRegistry _classRegistry = new();
    private readonly StageFloor _floor;
    private readonly List<Placed> _placed = [];
    private readonly List<JobActor> _jobActors = [];

    private JobMovePhase _jobPhase = JobMovePhase.None;
    private int _jobMoverIndex = -1;
    private bool _jobMoverReturning;
    private float _jobMoveX;
    private float _jobMoveZ;
    private float _jobMoveYaw;
    private float _jobTargetX;
    private float _jobTargetZ;
    private int _forwardSubject = -1;
    private int _rosterHover = -1;
    private int _rosterSelection = -1;
    private int _jobHover = -1;
    private int _jobSelection = -1;
    private Actor? _facePreview;

    public CharSelectScene(CharSelectSceneData data, CharSelectCamera camera)
    {
        _camera = camera;
        _floor = new StageFloor(data.Stage);
    }

    public int RosterCount => _placed.Count;
    public int MaxRoster => CharacterSlots.Maximum;
    public CharacterSummary RosterSummary(int i) => _placed[i].Summary;

    public void BuildRoster(IReadOnlyList<CharacterSummary> roster)
    {
        ClearRoster();
        var count = Math.Min(roster.Count, CharacterSlots.Maximum);
        for (var i = 0; i < count; i++)
        {
            var summary = roster[i];
            if (!Enum.IsDefined(summary.Class))
            {
                continue;
            }

            var slot = CharacterSelectStage.RosterPosition(count, i);
            _placed.Add(new Placed(summary, slot, new Actor(summary.Class, summary.FaceIndex, SlotWorld(slot))));
        }
    }

    public void ClearRoster()
    {
        _placed.Clear();
        _rosterHover = -1;
        _rosterSelection = -1;
    }

    public int JobCount => _jobActors.Count;
    public PlayerClass JobClass(int i) => _jobActors[i].Class;

    public void BuildJobLineup(CharacterBrood brood)
    {
        ClearJobLineup();
        var jobs = JobsForBrood(brood);
        for (var i = 0; i < jobs.Count; i++)
        {
            _jobActors.Add(new JobActor(jobs[i], i, new Actor(jobs[i], 0, SlotWorld(CharacterSelectStage.LineupPosition(i, jobs.Count)))));
        }
    }

    public void ClearJobLineup()
    {
        CloseFacePreview();
        _jobActors.Clear();
        _jobPhase = JobMovePhase.None;
        _jobMoverIndex = -1;
        _jobMoveYaw = 0f;
        _forwardSubject = -1;
        _jobHover = -1;
        _jobSelection = -1;
    }

    public void OpenFacePreview(int jobIndex, int faceIndex)
    {
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            return;
        }

        _facePreview = new Actor(_jobActors[jobIndex].Class, faceIndex, Matrix.Identity);
    }

    public void CloseFacePreview() => _facePreview = null;

    public void StartForwardRun(int jobIndex)
    {
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            return;
        }

        CompleteActiveSlide();
        _forwardSubject = jobIndex;
        var slot = JobSlot(jobIndex);
        StartSlide(jobIndex, slot.X, slot.Z, 0f, CharacterSelectStage.ForwardZ, returning: false);
    }

    public void ReturnToLineup(int jobIndex)
    {
        _forwardSubject = -1;
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            CompleteActiveSlide();
            return;
        }

        if (_jobMoverIndex >= 0 && _jobMoverIndex != jobIndex)
        {
            CompleteActiveSlide();
        }

        var slot = JobSlot(jobIndex);
        var active = _jobMoverIndex >= 0;
        StartSlide(jobIndex, active ? _jobMoveX : 0f, active ? _jobMoveZ : CharacterSelectStage.RestZ, slot.X, slot.Z, returning: true);
    }

    private void CompleteActiveSlide()
    {
        if (_jobMoverIndex >= 0 && _jobMoverIndex < _jobActors.Count)
        {
            _jobMoveX = _jobMoverReturning ? _jobTargetX : 0f;
            _jobMoveZ = _jobMoverReturning ? _jobTargetZ : CharacterSelectStage.RestZ;
            PlaceJobActor(_jobActors[_jobMoverIndex], (_jobMoveX, _jobMoveZ), running: false, 0f);
        }

        _jobMoverIndex = -1;
        _jobMoverReturning = false;
        _jobPhase = JobMovePhase.None;
        _jobMoveYaw = 0f;
    }

    private void StartSlide(int actorIndex, float startX, float startZ, float targetX, float targetZ, bool returning)
    {
        _jobMoverIndex = actorIndex;
        _jobMoveX = startX;
        _jobMoveZ = startZ;
        _jobTargetX = targetX;
        _jobTargetZ = targetZ;
        _jobMoverReturning = returning;
        _jobPhase = JobMovePhase.TurnToHeading;
        PlaceJobActor(_jobActors[actorIndex], (startX, startZ), running: false, _jobMoveYaw);
    }

    // Plays every character's motion and moves the class that walks forward or back to the lineup.
    public void UpdateRun(float dt)
    {
        for (var index = 0; index < _placed.Count; index++)
        {
            _placed[index].Actor.Advance(dt);
        }

        for (var index = 0; index < _jobActors.Count; index++)
        {
            _jobActors[index].Actor.Advance(dt);
        }

        _facePreview?.Advance(dt);
        if (_jobMoverIndex < 0 || _jobMoverIndex >= _jobActors.Count)
        {
            return;
        }

        var job = _jobActors[_jobMoverIndex];
        var turn = CharacterSelectStage.TurnRate * dt;
        switch (_jobPhase)
        {
            case JobMovePhase.TurnToHeading:
                {
                    var to = new Vector2(_jobTargetX - _jobMoveX, _jobTargetZ - _jobMoveZ);
                    var headingYaw = to.LengthSquared() > 1e-4f ? MathF.Atan2(to.X, -to.Y) : _jobMoveYaw;
                    _jobMoveYaw = ApproachAngle(_jobMoveYaw, headingYaw, turn);
                    PlaceJobActor(job, (_jobMoveX, _jobMoveZ), running: false, _jobMoveYaw);
                    if (MathF.Abs(WrapAngle(headingYaw - _jobMoveYaw)) <= CharacterSelectStage.YawSettle)
                    {
                        _jobPhase = JobMovePhase.Running;
                    }

                    break;
                }

            case JobMovePhase.Running:
                {
                    var step = CharacterSelectStage.RunUnitsPerSecond * dt;
                    var toTarget = new Vector2(_jobTargetX - _jobMoveX, _jobTargetZ - _jobMoveZ);
                    var distance = toTarget.Length();
                    if (distance <= step || (!_jobMoverReturning && _jobMoveZ <= CharacterSelectStage.StopZ))
                    {
                        _jobMoveX = _jobMoverReturning ? _jobTargetX : 0f;
                        _jobMoveZ = _jobMoverReturning ? _jobTargetZ : CharacterSelectStage.RestZ;
                        PlaceJobActor(job, (_jobMoveX, _jobMoveZ), running: false, _jobMoveYaw);
                        _jobPhase = JobMovePhase.TurnToCamera;
                        break;
                    }

                    var direction = toTarget / distance;
                    _jobMoveX += direction.X * step;
                    _jobMoveZ += direction.Y * step;
                    _jobMoveYaw = ApproachAngle(_jobMoveYaw, MathF.Atan2(direction.X, -direction.Y), turn);
                    PlaceJobActor(job, (_jobMoveX, _jobMoveZ), running: true, _jobMoveYaw);
                    break;
                }

            case JobMovePhase.TurnToCamera:
                {
                    _jobMoveYaw = ApproachAngle(_jobMoveYaw, 0f, turn);
                    PlaceJobActor(job, (_jobMoveX, _jobMoveZ), running: false, _jobMoveYaw);
                    if (MathF.Abs(_jobMoveYaw) <= CharacterSelectStage.YawSettle)
                    {
                        _jobMoveYaw = 0f;
                        PlaceJobActor(job, (_jobMoveX, _jobMoveZ), running: false, 0f);
                        _jobMoverIndex = -1;
                        _jobPhase = JobMovePhase.None;
                    }

                    break;
                }
        }
    }

    public void ApplyFace(int jobIndex, int faceIndex)
    {
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            return;
        }

        _jobActors[jobIndex].Actor.Face = faceIndex;
        if (_facePreview is not null)
        {
            _facePreview.Face = faceIndex;
        }
    }

    public int PickRoster(Vector2 cursor, int viewportWidth, int viewportHeight)
    {
        var best = -1;
        var bestDepth = float.MinValue;
        for (var i = 0; i < _placed.Count; i++)
        {
            Pick(cursor, viewportWidth, viewportHeight, _placed[i].Slot, i, ref best, ref bestDepth);
        }

        return best;
    }

    public int PickJob(Vector2 cursor, int viewportWidth, int viewportHeight)
    {
        var best = -1;
        var bestDepth = float.MinValue;
        for (var i = 0; i < _jobActors.Count; i++)
        {
            Pick(cursor, viewportWidth, viewportHeight, JobSlot(i), i, ref best, ref bestDepth);
        }

        return best;
    }

    // A character answers inside the box from its feet to PickHeight above them. Where two boxes overlap the farther
    // character wins, as on the desktop (its depth buffer is reversed and it keeps the smaller depth).
    private void Pick(Vector2 cursor, int viewportWidth, int viewportHeight, (float X, float Z) slot, int index, ref int best, ref float bestDepth)
    {
        var footWorld = SlotToWorld(slot, ResolveFloorY(slot));
        var foot = _camera.Project(footWorld, viewportWidth, viewportHeight);
        var head = _camera.Project(footWorld + new Vector3(0f, CharacterSelectStage.PickHeight, 0f), viewportWidth, viewportHeight);
        if (foot.Z is < 0f or > 1f)
        {
            return;
        }

        if (cursor.X >= foot.X - CharacterSelectStage.PickHalfWidthPixels && cursor.X <= foot.X + CharacterSelectStage.PickHalfWidthPixels
            && cursor.Y >= MathF.Min(foot.Y, head.Y) && cursor.Y <= MathF.Max(foot.Y, head.Y) && foot.Z > bestDepth)
        {
            best = index;
            bestDepth = foot.Z;
        }
    }

    public void SetRosterHighlight(int hoveredIndex, int selectedIndex)
    {
        if (hoveredIndex == _rosterHover && selectedIndex == _rosterSelection)
        {
            return;
        }

        _rosterHover = hoveredIndex;
        _rosterSelection = selectedIndex;
        for (var index = 0; index < _placed.Count; index++)
        {
            _placed[index].Actor.Glow = CharacterSelectStage.Glow(index, hoveredIndex, selectedIndex);
        }
    }

    public void SetJobHighlight(int hoveredIndex, int selectedIndex)
    {
        if (hoveredIndex == _jobHover && selectedIndex == _jobSelection)
        {
            return;
        }

        _jobHover = hoveredIndex;
        _jobSelection = selectedIndex;
        for (var index = 0; index < _jobActors.Count; index++)
        {
            _jobActors[index].Actor.Glow = CharacterSelectStage.Glow(index, hoveredIndex, selectedIndex);
        }
    }

    public Vector3 JobHeadWorld(int i)
    {
        var slot = JobSlot(i);
        return SlotToWorld(slot, ResolveFloorY(slot)) + new Vector3(0f, CharacterSelectStage.PickHeight, 0f);
    }

    public CharSelectInfo? Info(int i)
    {
        if (i < 0 || i >= _placed.Count)
        {
            return null;
        }

        var summary = _placed[i].Summary;
        var playerClass = _classRegistry.Get(summary.Class);
        var broodName = playerClass.Brood == CharacterBrood.Tempskron ? "Tempskron" : "Morion";
        return new CharSelectInfo(summary.Name, summary.Level, broodName, playerClass.Name, summary.ClanName);
    }

    public void Draw(CharSelectGraphics graphics)
    {
        var viewProjection = _camera.ViewProjection;
        graphics.DrawStage(MirrorZ * viewProjection);
        for (var index = 0; index < _placed.Count; index++)
        {
            DrawActor(graphics, _placed[index].Actor, viewProjection);
        }

        for (var index = 0; index < _jobActors.Count; index++)
        {
            DrawActor(graphics, _jobActors[index].Actor, viewProjection);
        }

        graphics.DrawStageBlended(MirrorZ * viewProjection);
    }

    // The face preview alone, framed as the desktop's portrait: from in front, PortraitVisibleHeight of the character's
    // height around the point PortraitFocusHeight up it.
    public void DrawFacePortrait(CharSelectGraphics graphics, float aspect)
    {
        if (_facePreview is not { } preview || !graphics.TryBounds(preview.Class, preview.Face, out var minimum, out var maximum))
        {
            return;
        }

        var size = maximum - minimum;
        // The baked files' z is the mirror of render space.
        var center = new Vector3((minimum.X + maximum.X) * 0.5f, minimum.Y + size.Y * CharacterSelectStage.PortraitFocusHeight, -(minimum.Z + maximum.Z) * 0.5f);
        var halfTangent = MathF.Tan(CharacterSelectStage.PortraitFieldOfView * 0.5f);
        var verticalDistance = size.Y * CharacterSelectStage.PortraitVisibleHeight * 0.5f / halfTangent;
        var horizontalDistance = size.X * 0.58f / (halfTangent * aspect);
        var distance = MathF.Max(MathF.Max(verticalDistance, horizontalDistance) + MathF.Max(size.Z * 0.25f, 1f), 10f);
        var view = Matrix.CreateLookAt(center + Vector3.UnitZ * distance, center, Vector3.UnitY);
        var projection = Matrix.CreatePerspectiveFieldOfView(CharacterSelectStage.PortraitFieldOfView, aspect, 0.1f,
            distance + MathF.Max(size.Length() * 4f, 100f));
        graphics.DrawActor(preview.Class, preview.Face, MobileActorFormat.SelectStand, preview.Seconds, MirrorZ * view * projection, Ambient, 0f);
    }

    public bool HasFacePreview => _facePreview is not null;

    private static void DrawActor(CharSelectGraphics graphics, Actor actor, in Matrix viewProjection) =>
        graphics.DrawActor(actor.Class, actor.Face, actor.Running ? MobileActorFormat.Run : MobileActorFormat.SelectStand, actor.Seconds,
            MirrorZ * actor.World * viewProjection, Ambient, actor.Glow);

    private (float X, float Z) JobSlot(int jobIndex) => CharacterSelectStage.LineupPosition(_jobActors[jobIndex].SlotIndex, _jobActors.Count);

    private static IReadOnlyList<PlayerClass> JobsForBrood(CharacterBrood brood) =>
        brood == CharacterBrood.Morion ? CharacterSelectArt.MorionLineup : CharacterSelectArt.TempskronLineup;

    private void PlaceJobActor(JobActor job, (float X, float Z) slot, bool running, float yaw)
    {
        job.Actor.World = SlotWorld(slot, yaw);
        job.Actor.SetMotion(running);
    }

    // Stage planar coordinates map to render space (x, -z).
    private Matrix SlotWorld((float X, float Z) slot, float yaw = 0f) =>
        Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(SlotToWorld(slot, ResolveFloorY(slot)));

    private static Vector3 SlotToWorld((float X, float Z) slot, float floorY) => new(slot.X, floorY, -slot.Z);

    private float ResolveFloorY((float X, float Z) slot) =>
        _floor.TryHeight(slot.X, slot.Z, FloorProbeY, out var y) && MathF.Abs(y) <= CharacterSelectStage.PlausibleFloor ? y : CharacterSelectStage.FloorY;

    private static float ApproachAngle(float current, float target, float maxDelta)
    {
        var delta = WrapAngle(target - current);
        return MathF.Abs(delta) <= maxDelta ? target : WrapAngle(current + MathF.Sign(delta) * maxDelta);
    }

    // Into (-pi, pi].
    private static float WrapAngle(float angle)
    {
        angle = MathF.IEEERemainder(angle, MathF.Tau);
        return angle <= -MathF.PI ? angle + MathF.Tau : angle;
    }
}
