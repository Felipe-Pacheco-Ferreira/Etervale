using System.Diagnostics;
using System.Numerics;
using Android.Graphics;
using Android.Opengl;
using Java.Nio;
using Javax.Microedition.Khronos.Opengles;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Characters;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace PristonTale.Mobile;

internal sealed class RicartenRenderer(MobileScene scene, Vector3 spawn) : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const int FloatsPerVertex = 8;
    // Native durations of the Windows avatar clips used to bake the mobile poses.
    private const float WalkCycleDistance = 90f;
    private const float RunClipSeconds = .660f;
    private const float IdleClipSeconds = 1.65f;
    private RicartenCamera _camera = new();
    private readonly float[] _matrix = new float[16];
    private PlayerFrame _player = new(spawn, 0f, 1, false, false, true);
    private readonly object _playerFramesLock = new();
    private readonly List<TimedPlayerFrame> _playerFrames = new();
    private static readonly long PresentationDelay = Stopwatch.Frequency * 30 / 1000;
    private bool _snapPlayer;
    private SceneEntity[] _entities = [];
    private Vector3 _displayPosition = spawn;
    private long _lastFrame;
    private float _locomotionPhase;
    private float _idlePhase;
    private bool _moving;
    private bool _running;
    private float _displayFacing;
    private float _aspect = 1f;
    private CameraSnapshot? _projection;
    private float _npcTime;
    private const float NpcWalkUnitsPerSecond = 70f;
    private readonly Dictionary<ulong, NpcStride> _npcStrides = new();
    private SceneEntity[]? _lastRenderedEntities;
    private int _program;
    private int _matrixUniform;
    private int _tintUniform;
    private int _frameBlendUniform;
    private int _cubeBuffer;
    private int _whiteTexture;
    private GpuBatch[] _stage = [];
    private Dictionary<uint, NpcModel> _npcs = new();
    private GpuBatch[][] _idle = [];
    private GpuBatch[][] _walk = [];
    private GpuBatch[][] _run = [];

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

    public void SetEntities(SceneEntity[] entities) => Volatile.Write(ref _entities, entities);

    public void SetCamera(RicartenCamera camera) => Volatile.Write(ref _camera, camera);

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        GLES20.GlClearColor(.48f, .67f, .80f, 1f);
        GLES20.GlEnable(GLES20.GlDepthTest);
        _program = CreateProgram();
        _matrixUniform = GLES20.GlGetUniformLocation(_program, "uMatrix");
        _tintUniform = GLES20.GlGetUniformLocation(_program, "uTint");
        _frameBlendUniform = GLES20.GlGetUniformLocation(_program, "uFrameBlend");
        _cubeBuffer = Upload(BuildCube());
        using var white = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Argb8888!)!;
        white.EraseColor(Color.White);
        _whiteTexture = UploadTexture(white);
        var textures = scene.Textures.Select(DecodeTexture).ToArray();
        _stage = UploadFrame(scene.Stage, textures);
        _npcs = scene.Npcs.ToDictionary(entry => entry.Key,
            entry => new NpcModel(UploadNpcClip(entry.Value.Idle)!, UploadNpcClip(entry.Value.Walk), UploadNpcClip(entry.Value.Run)));

        NpcPoseClip? UploadNpcClip(MobileNpcClip? clip) => clip is null ? null
            : new NpcPoseClip(clip.Seconds, clip.Frames.Select(frame => UploadFrame(frame, textures)).ToArray());
        _idle = scene.Idle.Select(frame => UploadFrame(frame, textures)).ToArray();
        _walk = scene.Walk.Select(frame => UploadFrame(frame, textures)).ToArray();
        _run = scene.Run.Select(frame => UploadFrame(frame, textures)).ToArray();
        GLES20.GlUseProgram(_program);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_program, "uTexture"), 0);
        _lastFrame = 0;
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        GLES20.GlViewport(0, 0, width, height);
        _aspect = width / (float)Math.Max(height, 1);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = _lastFrame == 0 ? 0f : Math.Min(.05f, (float)Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        var player = SamplePlayer(now, out var snap);
        if (snap)
        {
            _displayPosition = player.Position;
            _locomotionPhase = 0f;
            _moving = false;
        }
        var previous = _displayPosition;
        _displayPosition = player.Position;
        _displayFacing = player.Facing;
        var offset = _displayPosition - previous;
        var distance = new Vector2(offset.X, offset.Z).Length();
        // Match the desktop playback rate at full speed and scale it by actual travel
        // so partial joystick input, acceleration and blocked steps keep the feet in sync.
        var running = player.Running;
        var cycleDistance = running
            ? Locomotion.RunUnitsPerSecond(player.MoveSpeed) * RunClipSeconds / Locomotion.StepRate(player.MoveSpeed, running: true)
            : WalkCycleDistance;
        var moving = player.Moving;
        if (moving != _moving || moving && running != _running)
        {
            _locomotionPhase = 0f;
            _idlePhase = 0f;
            _moving = moving;
            _running = running;
        }
        _locomotionPhase = moving ? (_locomotionPhase + distance / cycleDistance) % 1f : 0f;
        _idlePhase = (_idlePhase + dt / IdleClipSeconds) % 1f;

        var camera = Volatile.Read(ref _camera).ViewProjection(_displayPosition, _aspect);
        Volatile.Write(ref _projection, new CameraSnapshot(camera));
        _npcTime += dt;
        GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);
        GLES20.GlUseProgram(_program);
        DrawFrame(_stage, camera);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _whiteTexture);
        BindVertices(_cubeBuffer);
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
            if (entity.Kind == EntityKind.Npc && _npcs.TryGetValue(entity.TemplateId, out var model))
            {
                var (position, facing) = entity.At(now);
                var scale = entity.Scale > 0f ? entity.Scale : 1f;
                var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(facing + MathF.PI)
                    * Matrix4x4.CreateTranslation(position);
                var clip = entity.Motion switch
                {
                    MotionState.Run => model.Run ?? model.Walk ?? model.Idle,
                    MotionState.Walk => model.Walk ?? model.Run ?? model.Idle,
                    _ => model.Idle
                };
                var phase = (_npcTime / clip.Seconds + (entity.Id % 17) / 17f) % 1f;
                if (entity.Motion is MotionState.Walk or MotionState.Run && !ReferenceEquals(clip, model.Idle))
                {
                    var previousStride = _npcStrides.GetValueOrDefault(entity.Id);
                    var delta = new Vector2(position.X - previousStride.Position.X, position.Z - previousStride.Position.Z);
                    var stride = NpcWalkUnitsPerSecond * clip.Seconds;
                    phase = previousStride.Motion == entity.Motion ? (previousStride.Phase + delta.Length() / stride) % 1f : 0f;
                }
                _npcStrides[entity.Id] = new NpcStride(position, entity.Motion, phase);
                var npcPose = SamplePose(clip.Frames, phase);
                DrawFrame(npcPose.Current, world * camera, npcPose);
                continue;
            }
            GLES20.GlBindTexture(GLES20.GlTexture2d, _whiteTexture);
            BindVertices(_cubeBuffer);
            var color = entity.Kind == EntityKind.Npc ? new Vector3(.94f, .69f, .31f) : new Vector3(.30f, .63f, .91f);
            DrawBox(new Vector3(13f, 36f, 13f), new Vector3(0f, 18f, 0f), Matrix4x4.CreateTranslation(entity.Position), camera, color);
        }
        var pose = moving ? SamplePose(running ? _run : _walk, _locomotionPhase) : SamplePose(_idle, _idlePhase);
        var body = Matrix4x4.CreateRotationY(_displayFacing + MathF.PI) * Matrix4x4.CreateTranslation(_displayPosition);
        DrawFrame(pose.Current, body * camera, pose);
    }

    public bool TryProject(Vector3 point, out Vector3 ndc)
    {
        var snapshot = Volatile.Read(ref _projection);
        var clip = snapshot is null ? default : Vector4.Transform(new Vector4(point, 1f), snapshot.Matrix);
        ndc = clip.W > 0f ? new Vector3(clip.X, clip.Y, clip.Z) / clip.W : default;
        return clip.W > 0f && ndc.Z is >= 0f and <= 1f;
    }

    private readonly record struct NpcStride(Vector3 Position, MotionState Motion, float Phase);
    private sealed record NpcPoseClip(float Seconds, GpuBatch[][] Frames);
    private sealed record NpcModel(NpcPoseClip Idle, NpcPoseClip? Walk, NpcPoseClip? Run);

    private sealed record CameraSnapshot(Matrix4x4 Matrix);

    private static Pose SamplePose(GpuBatch[][] clip, float phase)
    {
        var frame = phase * clip.Length;
        var index = (int)frame;
        return new Pose(clip[index % clip.Length], clip[(index + 1) % clip.Length], frame - index);
    }

    private readonly record struct Pose(GpuBatch[] Current, GpuBatch[] Next, float Blend);

    private void DrawFrame(GpuBatch[] frame, Matrix4x4 transform, Pose? pose = null)
    {
        SetTransform(transform, Vector3.One);
        GLES20.GlUniform1f(_frameBlendUniform, pose?.Blend ?? 0f);
        for (var i = 0; i < frame.Length; i++)
        {
            var batch = frame[i];
            BindVertices(batch.Buffer);
            if (pose is { } animated)
            {
                BindPosePosition(3, animated.Next[i]);
            }
            GLES20.GlBindTexture(GLES20.GlTexture2d, batch.Texture);
            GLES20.GlDrawArrays(GLES20.GlTriangles, 0, batch.Vertices);
        }
        GLES20.GlDisableVertexAttribArray(3);
        GLES20.GlUniform1f(_frameBlendUniform, 0f);
    }

    private static void BindPosePosition(int attribute, GpuBatch batch)
    {
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, batch.Buffer);
        GLES20.GlEnableVertexAttribArray(attribute);
        GLES20.GlVertexAttribPointer(attribute, 3, GLES20.GlFloat, false, FloatsPerVertex * sizeof(float), 0);
    }
    private GpuBatch[] UploadFrame(MobileBatch[] frame, int[] textures) =>
        frame.Select(batch => new GpuBatch(Upload(batch.Vertices), batch.Texture < 0 ? _whiteTexture : textures[batch.Texture],
            batch.Vertices.Length / FloatsPerVertex)).ToArray();

    private int DecodeTexture(byte[] data)
    {
        using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(data, 0, data.Length, options)?.Dispose();
        var size = Math.Max(options.OutWidth, options.OutHeight);
        var sample = 1;
        while (size / sample > 256)
        {
            sample *= 2;
        }
        options.InJustDecodeBounds = false;
        options.InSampleSize = sample;
        using var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, options);
        return bitmap is null ? _whiteTexture : UploadTexture(bitmap);
    }

    private static int UploadTexture(Bitmap bitmap)
    {
        var id = new int[1];
        GLES20.GlGenTextures(1, id, 0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, id[0]);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bitmap, 0);
        return id[0];
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

    private static int Upload(float[] vertices)
    {
        var buffers = new int[1];
        GLES20.GlGenBuffers(1, buffers, 0);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, buffers[0]);
        var size = vertices.Length * sizeof(float);
        using var bytes = ByteBuffer.AllocateDirect(size)!;
        bytes.Order(ByteOrder.NativeOrder() ?? throw new InvalidOperationException("Native byte order is unavailable."));
        using var data = bytes.AsFloatBuffer()!;
        data.Put(vertices);
        data.Position(0);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, size, data, GLES20.GlStaticDraw);
        return buffers[0];
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
            void main() {
                vec4 texel = texture2D(uTexture, fract(vUv));
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
