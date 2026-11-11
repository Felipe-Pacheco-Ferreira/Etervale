using System.Diagnostics;
using System.Collections.Concurrent;
using Android.Content.Res;
using PristonTale.Contracts.Characters;
using System.Numerics;
using Android.Graphics;
using Android.Opengl;
using Javax.Microedition.Khronos.Opengles;
using PristonTale.Assets;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Characters;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace PristonTale.Mobile;

internal sealed record NpcPortrait(uint Template, int X, int Y, int Width, int Height);

// Draws a field's baked scene: the stage, the player as the class model and face it was given, NPCs with a baked model
// as that model and every other entity as a marker. An NPC's model is uploaded the first time the NPC is drawn, one a
// frame, so entering the field waits only for the stage and the player.
internal sealed class RicartenRenderer(MobileScene scene, Vector3 spawn, CharacterModel? player, int face, WorldArrival arrival) : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const int FloatsPerVertex = 8;
    private const float WalkCycleDistance = 90f;
    private const int MostTextureSide = 256;
    private RicartenCamera _camera = new();
    private readonly float[] _matrix = new float[16];
    private PlayerFrame _player = new(spawn, 0f, 1, false, false, true);
    private readonly object _playerFramesLock = new();
    private readonly List<TimedPlayerFrame> _playerFrames = new();
    private static readonly long PresentationDelay = Stopwatch.Frequency * 30 / 1000;
    private bool _snapPlayer;
    private SceneEntity[] _entities = [];
    private Vector3[] _route = [];
    private static readonly Vector3 RouteColor = new(1f, .82f, .13f);
    private Vector3 _displayPosition = spawn;
    private long _lastFrame;
    private float _locomotionPhase;
    private float _idlePhase;
    private bool _moving;
    private bool _running;
    private float _displayFacing;
    private float _aspect = 1f;
    private int _width;
    private int _height;
    private NpcPortrait? _portrait;
    private readonly Dictionary<uint, (Vector3 Center, float Height)> _portraitBounds = new();
    private CameraSnapshot? _projection;
    private float _npcTime;
    private const float NpcWalkUnitsPerSecond = 70f;
    private readonly Dictionary<ulong, NpcStride> _npcStrides = new();
    private SceneEntity[]? _lastRenderedEntities;
    private int _program;
    private int _matrixUniform;
    private int _tintUniform;
    private int _frameBlendUniform;
    private int _effectAlphaUniform;
    private GlActorPart? _arrivalPart;
    private Vector3 _arrivalPosition;
    private long _arrivalStart;
    private int _cubeBuffer;
    private GlTexture _whiteTexture;
    private GlTexture[] _textures = [];
    private GpuBatch[] _stage = [];
    private GlActorPart[] _scenery = [];
    private readonly Dictionary<uint, GlActorPart> _npcs = new();
    private readonly ConcurrentDictionary<PlayerClass, Task<CharacterModel>> _remoteModels = new();
    private readonly Dictionary<PlayerClass, (GlTexture[] Textures, GlActorPart Body, GlActorPart?[] Heads)> _remoteActors = new();
    private GlActorPart? _playerBody;
    private GlActorPart? _playerHead;
    private bool _uploadedThisFrame;
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Completes when the scene has been drawn once: its textures and buffers are uploaded and it is on screen.
    public Task FirstFrame => _firstFrame.Task;

    public void SetPlayer(PlayerFrame player)
    {
        lock (_playerFramesLock)
        {
            if (player.Snap)
            {
                _playerFrames.Clear();
                _snapPlayer = true;
            }
            _playerFrames.Add(new TimedPlayerFrame(Stopwatch.GetTimestamp(), player));
            while (_playerFrames.Count > 8)
            {
                _playerFrames.RemoveAt(0);
            }
        }
    }

    private PlayerFrame SamplePlayer(long now, out bool snap)
    {
        lock (_playerFramesLock)
        {
            snap = _snapPlayer;
            _snapPlayer = false;
            var target = now - PresentationDelay;
            while (_playerFrames.Count >= 2 && _playerFrames[1].Timestamp <= target)
            {
                _playerFrames.RemoveAt(0);
            }

            if (_playerFrames.Count > 0)
            {
                var first = _playerFrames[0];
                _player = first.Frame;
                if (_playerFrames.Count >= 2 && !snap)
                {
                    var second = _playerFrames[1];
                    var amount = Math.Clamp((float)(target - first.Timestamp) /
                        Math.Max(1L, second.Timestamp - first.Timestamp), 0f, 1f);
                    var turn = MathF.IEEERemainder(second.Frame.Facing - first.Frame.Facing, MathF.Tau);
                    return first.Frame with
                    {
                        Position = Vector3.Lerp(first.Frame.Position, second.Frame.Position, amount),
                        Facing = first.Frame.Facing + turn * amount,
                        Moving = amount > 0f ? second.Frame.Moving : first.Frame.Moving
                    };
                }
            }
            return _player;
        }
    }

    private readonly record struct TimedPlayerFrame(long Timestamp, PlayerFrame Frame);

    public void PrepareRemoteClass(AssetManager assets, PlayerClass playerClass)
    {
        if (_remoteModels.ContainsKey(playerClass))
        {
            return;
        }
        var load = CharacterModel.LoadAsync(assets, playerClass);
        _remoteModels.TryAdd(playerClass, load);
        _ = load.ContinueWith(task => global::Android.Util.Log.Warn("Etervale.World", task.Exception!.GetBaseException().ToString()),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void SetRoute(Vector3[] points) => Volatile.Write(ref _route, points);

    public void SetEntities(SceneEntity[] entities) => Volatile.Write(ref _entities, entities);

    public void SetCamera(RicartenCamera camera) => Volatile.Write(ref _camera, camera);

    public void ShowArrival(Vector3 position)
    {
        _arrivalPosition = position;
        _arrivalStart = Stopwatch.GetTimestamp();
    }

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        GLES20.GlClearColor(.48f, .67f, .80f, 1f);
        GLES20.GlEnable(GLES20.GlDepthTest);
        _program = CreateProgram();
        _matrixUniform = GLES20.GlGetUniformLocation(_program, "uMatrix");
        _tintUniform = GLES20.GlGetUniformLocation(_program, "uTint");
        _frameBlendUniform = GLES20.GlGetUniformLocation(_program, "uFrameBlend");
        _effectAlphaUniform = GLES20.GlGetUniformLocation(_program, "uEffectAlpha");
        _cubeBuffer = GlUpload.Buffer(BuildCube());
        using (var white = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Argb8888!)!)
        {
            white.EraseColor(Color.White);
            _whiteTexture = GlUpload.Texture(white, world: false);
        }

        // The context is new: nothing uploaded before belongs to it.
        _textures = new GlTexture[scene.Textures.Length];
        _npcs.Clear();
        _remoteActors.Clear();
        var arrivalTextures = new GlTexture[arrival.Textures.Length];
        for (var index = 0; index < arrivalTextures.Length; index++)
        {
            arrivalTextures[index] = GlUpload.Texture(arrival.Textures[index], MostTextureSide, world: false);
        }
        _arrivalPart = new GlActorPart(arrival.Part, arrivalTextures, _whiteTexture);
        _stage = new GpuBatch[scene.Stage.Length];
        for (var index = 0; index < _stage.Length; index++)
        {
            var batch = scene.Stage[index];
            _stage[index] = new GpuBatch(GlUpload.Buffer(batch.Vertices), Texture(batch.Texture).Id, batch.Vertices.Length / FloatsPerVertex);
        }

        _scenery = new GlActorPart[scene.Scenery.Length];
        for (var index = 0; index < _scenery.Length; index++)
        {
            var part = scene.Scenery[index];
            _scenery[index] = new GlActorPart(part, UsedTextures(part), _whiteTexture);
        }

        if (player is not null)
        {
            var textures = new GlTexture[player.Textures.Length];
            for (var index = 0; index < textures.Length; index++)
            {
                textures[index] = GlUpload.Texture(player.Textures[index], MostTextureSide, world: false);
            }

            _playerBody = new GlActorPart(player.Body, textures, _whiteTexture);
            _playerHead = new GlActorPart(player.Heads[Math.Clamp(face, 0, player.Heads.Length - 1)], textures, _whiteTexture);
        }

        GLES20.GlUseProgram(_program);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_program, "uTexture"), 0);
        _lastFrame = 0;
    }

    public void SetNpcPortrait(NpcPortrait? portrait) => Volatile.Write(ref _portrait, portrait);

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _width = width;
        _height = height;
        GLES20.GlViewport(0, 0, width, height);
        _aspect = width / (float)Math.Max(height, 1);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = _lastFrame == 0 ? 0f : Math.Min(.05f, (float)Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        var frame = SamplePlayer(now, out var snap);
        if (snap)
        {
            _displayPosition = frame.Position;
            _locomotionPhase = 0f;
            _moving = false;
        }
        var previous = _displayPosition;
        _displayPosition = frame.Position;
        _displayFacing = frame.Facing;
        var offset = _displayPosition - previous;
        var distance = new Vector2(offset.X, offset.Z).Length();
        // Match the desktop playback rate at full speed and scale it by actual travel
        // so partial joystick input, acceleration and blocked steps keep the feet in sync.
        var running = frame.Running;
        var cycleDistance = running
            ? Locomotion.RunUnitsPerSecond(frame.MoveSpeed) * ClipSeconds(MobileActorFormat.Run) / Locomotion.StepRate(frame.MoveSpeed, running: true)
            : WalkCycleDistance;
        var moving = frame.Moving;
        if (moving != _moving || moving && running != _running)
        {
            _locomotionPhase = 0f;
            _idlePhase = 0f;
            _moving = moving;
            _running = running;
        }
        _locomotionPhase = moving ? (_locomotionPhase + distance / cycleDistance) % 1f : 0f;
        _idlePhase = (_idlePhase + dt / ClipSeconds(MobileActorFormat.Stand)) % 1f;

        var camera = Volatile.Read(ref _camera).ViewProjection(_displayPosition, _aspect);
        Volatile.Write(ref _projection, new CameraSnapshot(camera));
        _npcTime += dt;
        GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);
        GLES20.GlUseProgram(_program);
        GLES20.GlUniform1f(_effectAlphaUniform, -1f);
        DrawFrame(_stage, camera);
        DrawRoute(camera);
        foreach (var scenery in _scenery)
        {
            const int clip = 0;
            DrawPart(scenery, clip, (_npcTime / scenery.Seconds(clip)) % 1f, camera);
        }
        _uploadedThisFrame = false;
        var entities = Volatile.Read(ref _entities);
        if (!ReferenceEquals(_lastRenderedEntities, entities))
        {
            var visibleIds = entities.Select(entity => entity.Id).ToHashSet();
            foreach (var id in _npcStrides.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
            {
                _npcStrides.Remove(id);
            }
            _lastRenderedEntities = entities;
        }
        foreach (var entity in entities)
        {
            if (entity.Kind == EntityKind.Player && DrawRemotePlayer(entity, now, camera))
            {
                continue;
            }
            if (entity.Kind == EntityKind.Npc && scene.Npcs.TryGetValue(entity.TemplateId, out var baked))
            {
                if (!_npcs.TryGetValue(entity.TemplateId, out var model))
                {
                    if (_uploadedThisFrame)
                    {
                        continue;
                    }

                    model = new GlActorPart(baked, UsedTextures(baked), _whiteTexture);
                    _npcs.Add(entity.TemplateId, model);
                    _uploadedThisFrame = true;
                }

                var (position, facing) = entity.At(now);
                var scale = entity.Scale > 0f ? entity.Scale : 1f;
                var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(facing + MathF.PI)
                    * Matrix4x4.CreateTranslation(position);
                var clip = entity.Motion switch
                {
                    MotionState.Run => First(model, MobileActorFormat.Run, MobileActorFormat.Walk),
                    MotionState.Walk => First(model, MobileActorFormat.Walk, MobileActorFormat.Run),
                    _ => MobileActorFormat.Stand
                };
                var seconds = model.Seconds(clip);
                var phase = (_npcTime / seconds + (entity.Id % 17) / 17f) % 1f;
                if (entity.Motion is MotionState.Walk or MotionState.Run && clip != MobileActorFormat.Stand)
                {
                    var previousStride = _npcStrides.GetValueOrDefault(entity.Id);
                    var delta = new Vector2(position.X - previousStride.Position.X, position.Z - previousStride.Position.Z);
                    var stride = NpcWalkUnitsPerSecond * seconds;
                    phase = previousStride.Motion == entity.Motion ? (previousStride.Phase + delta.Length() / stride) % 1f : 0f;
                }
                _npcStrides[entity.Id] = new NpcStride(position, entity.Motion, phase);
                DrawPart(model, clip, phase, world * camera);
                continue;
            }
            GLES20.GlBindTexture(GLES20.GlTexture2d, _whiteTexture.Id);
            BindVertices(_cubeBuffer);
            var color = entity.Kind == EntityKind.Npc ? new Vector3(.94f, .69f, .31f) : new Vector3(.30f, .63f, .91f);
            DrawBox(new Vector3(13f, 36f, 13f), new Vector3(0f, 18f, 0f), Matrix4x4.CreateTranslation(entity.Position), camera, color);
        }
        var body = Matrix4x4.CreateRotationY(_displayFacing + MathF.PI) * Matrix4x4.CreateTranslation(_displayPosition);
        if (_playerBody is not null && _playerHead is not null)
        {
            var clip = moving ? running ? MobileActorFormat.Run : MobileActorFormat.Walk : MobileActorFormat.Stand;
            var phase = moving ? _locomotionPhase : _idlePhase;
            DrawPart(_playerBody, clip, phase, body * camera);
            DrawPart(_playerHead, clip, phase, body * camera);
        }
        else
        {
            GLES20.GlBindTexture(GLES20.GlTexture2d, _whiteTexture.Id);
            BindVertices(_cubeBuffer);
            DrawBox(new Vector3(13f, 36f, 13f), new Vector3(0f, 18f, 0f), body, camera, Vector3.One);
        }

        DrawArrival(now, camera);
        DrawNpcPortrait();
        _firstFrame.TrySetResult();
    }

    private void DrawRoute(Matrix4x4 camera)
    {
        var route = Volatile.Read(ref _route);
        if (route.Length == 0)
        {
            return;
        }

        GLES20.GlBindTexture(GLES20.GlTexture2d, _whiteTexture.Id);
        BindVertices(_cubeBuffer);
        foreach (var point in route)
        {
            DrawBox(new Vector3(5f, .65f, 5f), new Vector3(0f, 1f, 0f), Matrix4x4.CreateTranslation(point), camera, RouteColor);
        }
    }

    private bool DrawRemotePlayer(SceneEntity entity, long now, Matrix4x4 camera)
    {
        if (entity.Appearance is not { } appearance || !_remoteModels.TryGetValue(appearance.Class, out var load)
            || !load.IsCompletedSuccessfully)
        {
            return false;
        }

        var model = load.Result;
        if (!_remoteActors.TryGetValue(appearance.Class, out var actor))
        {
            if (_uploadedThisFrame)
            {
                return false;
            }
            var textures = new GlTexture[model.Textures.Length];
            for (var index = 0; index < textures.Length; index++)
            {
                textures[index] = GlUpload.Texture(model.Textures[index], MostTextureSide, world: false);
            }

            actor = (textures, new GlActorPart(model.Body, textures, _whiteTexture), new GlActorPart?[model.Heads.Length]);
            _remoteActors.Add(appearance.Class, actor);
        }

        var face = Math.Clamp(appearance.FaceIndex, 0, actor.Heads.Length - 1);
        if (actor.Heads[face] is null)
        {
            if (_uploadedThisFrame)
            {
                return false;
            }
            actor.Heads[face] = new GlActorPart(model.Heads[face], actor.Textures, _whiteTexture);
            _uploadedThisFrame = true;
        }

        var (position, facing) = entity.At(now);
        var transform = Matrix4x4.CreateRotationY(facing + MathF.PI) * Matrix4x4.CreateTranslation(position) * camera;
        var clip = entity.Motion switch
        {
            MotionState.Run => First(actor.Body, MobileActorFormat.Run, MobileActorFormat.Walk),
            MotionState.Walk => First(actor.Body, MobileActorFormat.Walk, MobileActorFormat.Run),
            _ => MobileActorFormat.Stand
        };
        var phase = (_npcTime / actor.Body.Seconds(clip)) % 1f;
        DrawPart(actor.Body, clip, phase, transform);
        DrawPart(actor.Heads[face]!, clip, phase, transform);
        return true;
    }

    private void DrawNpcPortrait()
    {
        if (Volatile.Read(ref _portrait) is not { } portrait
            || !scene.Npcs.TryGetValue(portrait.Template, out var baked)
            || !_npcs.TryGetValue(portrait.Template, out var model))
        {
            return;
        }

        if (!_portraitBounds.TryGetValue(portrait.Template, out var bounds))
        {
            var minimum = new Vector3(float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity);
            foreach (var batch in baked.Clips[MobileActorFormat.Stand]!.Frames[0])
            {
                for (var index = 0; index < batch.Length; index += 3)
                {
                    var point = new Vector3(batch[index], batch[index + 1], batch[index + 2]);
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
            }

            var height = maximum.Y - minimum.Y;
            if (!(height > 0f) || !float.IsFinite(height))
            {
                return;
            }

            var center = (minimum + maximum) * .5f;
            center.Y = minimum.Y + height * .78f;
            bounds = (center, height);
            _portraitBounds.Add(portrait.Template, bounds);
        }

        var y = _height - portrait.Y - portrait.Height;
        GLES20.GlViewport(portrait.X, y, portrait.Width, portrait.Height);
        GLES20.GlEnable(GLES20.GlScissorTest);
        GLES20.GlScissor(portrait.X, y, portrait.Width, portrait.Height);
        GLES20.GlClearColor(.10f, .075f, .055f, 1f);
        GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);
        var view = Matrix4x4.CreateLookAt(bounds.Center - Vector3.UnitZ * bounds.Height * 2f, bounds.Center, Vector3.UnitY);
        var span = bounds.Height * .55f;
        var projection = Matrix4x4.CreateOrthographic(span * portrait.Width / portrait.Height, span, .1f, bounds.Height * 5f);
        const int clip = MobileActorFormat.Stand;
        DrawPart(model, clip, (_npcTime / model.Seconds(clip)) % 1f, view * projection);
        GLES20.GlDisable(GLES20.GlScissorTest);
        GLES20.GlViewport(0, 0, _width, _height);
        GLES20.GlClearColor(.48f, .67f, .80f, 1f);
    }

    private void DrawArrival(long now, Matrix4x4 camera)
    {
        if (_arrivalStart == 0 || _arrivalPart is not { } part)
        {
            return;
        }

        var seconds = part.Seconds(WorldArrival.Clip);
        var elapsed = (float)Stopwatch.GetElapsedTime(_arrivalStart, now).TotalSeconds;
        if (elapsed >= seconds)
        {
            _arrivalStart = 0;
            return;
        }

        var opacity = Math.Clamp(Math.Min(elapsed / arrival.FadeIn, (seconds - elapsed) / arrival.FadeOut), 0f, 1f);
        var position = _arrivalPosition + new Vector3(0f, arrival.OffsetY, 0f);
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOne);
        GLES20.GlDepthMask(false);
        GLES20.GlUniform1f(_effectAlphaUniform, opacity);
        DrawPart(part, WorldArrival.Clip, elapsed / seconds, Matrix4x4.CreateTranslation(position) * camera);
        GLES20.GlUniform1f(_effectAlphaUniform, -1f);
        GLES20.GlDepthMask(true);
        GLES20.GlDisable(GLES20.GlBlend);
    }

    public bool TryProject(Vector3 point, out Vector3 ndc)
    {
        var snapshot = Volatile.Read(ref _projection);
        var clip = snapshot is null ? default : Vector4.Transform(new Vector4(point, 1f), snapshot.Matrix);
        ndc = clip.W > 0f ? new Vector3(clip.X, clip.Y, clip.Z) / clip.W : default;
        return clip.W > 0f && ndc.Z is >= 0f and <= 1f;
    }

    private readonly record struct NpcStride(Vector3 Position, MotionState Motion, float Phase);
    private sealed record CameraSnapshot(Matrix4x4 Matrix);

    // The player's clip length in seconds; a second when there is no model, so phases still advance.
    private float ClipSeconds(int clip) => _playerBody is { } body && body.Has(clip) ? body.Seconds(clip) : 1f;

    private static int First(GlActorPart model, int wanted, int otherwise) =>
        model.Has(wanted) ? wanted : model.Has(otherwise) ? otherwise : MobileActorFormat.Stand;

    // An animated part in white, at phase of the clip.
    private void DrawPart(GlActorPart part, int clip, float phase, Matrix4x4 transform)
    {
        SetTransform(transform, Vector3.One);
        GLES20.GlDisableVertexAttribArray(1);
        GLES20.GlVertexAttrib3f(1, 1f, 1f, 1f);
        part.Draw(clip, phase, 0, 3, 2, _frameBlendUniform, -1);
        GLES20.GlDisableVertexAttribArray(3);
        GLES20.GlUniform1f(_frameBlendUniform, 0f);
    }

    private void DrawFrame(GpuBatch[] frame, Matrix4x4 transform)
    {
        SetTransform(transform, Vector3.One);
        GLES20.GlUniform1f(_frameBlendUniform, 0f);
        for (var i = 0; i < frame.Length; i++)
        {
            BindVertices(frame[i].Buffer);
            GLES20.GlBindTexture(GLES20.GlTexture2d, frame[i].Texture);
            GLES20.GlDrawArrays(GLES20.GlTriangles, 0, frame[i].Vertices);
        }
    }

    // A scene texture, decoded the first time something drawn uses it.
    private GlTexture Texture(int index)
    {
        if (index < 0 || index >= _textures.Length)
        {
            return _whiteTexture;
        }

        if (!_textures[index].IsValid)
        {
            var decoded = GlUpload.Texture(scene.Textures[index], MostTextureSide, world: false);
            _textures[index] = decoded.IsValid ? decoded : _whiteTexture;
        }

        return _textures[index];
    }

    // The scene's textures with the ones a part uses decoded.
    private GlTexture[] UsedTextures(ActorPart part)
    {
        foreach (var batch in part.Batches)
        {
            Texture(batch.Texture);
        }

        return _textures;
    }

    private void DrawBox(Vector3 size, Vector3 offset, Matrix4x4 body, Matrix4x4 camera, Vector3 tint) =>
        Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(offset) * body * camera, tint, 36);

    private void Draw(Matrix4x4 m, Vector3 tint, int count)
    {
        SetTransform(m, tint);
        GLES20.GlDrawArrays(GLES20.GlTriangles, 0, count);
    }

    private void SetTransform(Matrix4x4 m, Vector3 tint)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                _matrix[row * 4 + column] = m[row, column];
            }
        }
        GLES20.GlUniformMatrix4fv(_matrixUniform, 1, false, _matrix, 0);
        GLES20.GlUniform3f(_tintUniform, tint.X, tint.Y, tint.Z);
    }

    private static void BindVertices(int buffer)
    {
        const int stride = FloatsPerVertex * sizeof(float);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, buffer);
        GLES20.GlEnableVertexAttribArray(0);
        GLES20.GlEnableVertexAttribArray(1);
        GLES20.GlEnableVertexAttribArray(2);
        GLES20.GlVertexAttribPointer(0, 3, GLES20.GlFloat, false, stride, 0);
        GLES20.GlVertexAttribPointer(1, 3, GLES20.GlFloat, false, stride, 3 * sizeof(float));
        GLES20.GlVertexAttribPointer(2, 2, GLES20.GlFloat, false, stride, 6 * sizeof(float));
    }

    private static float[] BuildCube()
    {
        Vector3[] corners =
        [
            new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(.5f, .5f, -.5f), new(-.5f, .5f, -.5f),
            new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(.5f, .5f, .5f), new(-.5f, .5f, .5f)
        ];
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 3, 7, 0, 7, 4, 1, 5, 6, 1, 6, 2, 3, 2, 6, 3, 6, 7, 0, 4, 5, 0, 5, 1];
        float[] light = [.72f, .91f, .77f, .84f, 1f, .53f];
        var data = new float[indices.Length * FloatsPerVertex];
        var offset = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            Put(data, ref offset, corners[indices[i]], new Vector3(light[i / 6]));
        }
        return data;
    }

    private static void Put(float[] data, ref int offset, Vector3 position, Vector3 color)
    {
        data[offset++] = position.X;
        data[offset++] = position.Y;
        data[offset++] = position.Z;
        data[offset++] = color.X;
        data[offset++] = color.Y;
        data[offset++] = color.Z;
        data[offset++] = 0f;
        data[offset++] = 0f;
    }

    private static int CreateProgram()
    {
        var vertex = Compile(GLES20.GlVertexShader, """
            attribute vec3 aPosition;
            attribute vec3 aColor;
            attribute vec2 aUv;
            attribute vec3 aNextPosition;
            uniform mat4 uMatrix;
            uniform vec3 uTint;
            uniform float uFrameBlend;
            varying vec3 vColor;
            varying vec2 vUv;
            void main() {
                gl_Position = uMatrix * vec4(mix(aPosition, aNextPosition, uFrameBlend), 1.0);
                vColor = aColor * uTint;
                vUv = aUv;
            }
            """);
        var fragment = Compile(GLES20.GlFragmentShader, """
            precision mediump float;
            varying vec3 vColor;
            varying vec2 vUv;
            uniform sampler2D uTexture;
            uniform float uEffectAlpha;
            void main() {
                vec4 texel = texture2D(uTexture, fract(vUv));
                if (uEffectAlpha >= 0.0) {
                    gl_FragColor = vec4(texel.rgb * vColor, texel.a * uEffectAlpha);
                    return;
                }
                if (texel.a < 0.45) discard;
                gl_FragColor = vec4(texel.rgb * vColor, 1.0);
            }
            """);
        var program = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(program, vertex);
        GLES20.GlAttachShader(program, fragment);
        GLES20.GlBindAttribLocation(program, 0, "aPosition");
        GLES20.GlBindAttribLocation(program, 1, "aColor");
        GLES20.GlBindAttribLocation(program, 2, "aUv");
        GLES20.GlBindAttribLocation(program, 3, "aNextPosition");
        GLES20.GlLinkProgram(program);
        GLES20.GlDeleteShader(vertex);
        GLES20.GlDeleteShader(fragment);
        var status = new int[1];
        GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, status, 0);
        if (status[0] == 0)
        {
            throw new InvalidOperationException(GLES20.GlGetProgramInfoLog(program));
        }
        return program;
    }

    private static int Compile(int kind, string source)
    {
        var shader = GLES20.GlCreateShader(kind);
        GLES20.GlShaderSource(shader, source);
        GLES20.GlCompileShader(shader);
        var status = new int[1];
        GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, status, 0);
        if (status[0] == 0)
        {
            throw new InvalidOperationException(GLES20.GlGetShaderInfoLog(shader));
        }
        return shader;
    }

    private readonly record struct GpuBatch(int Buffer, int Texture, int Vertices);
}
