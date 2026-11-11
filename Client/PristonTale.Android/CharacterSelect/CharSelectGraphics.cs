using System.Numerics;
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using Matrix = System.Numerics.Matrix4x4;

namespace PristonTale.Mobile.CharacterSelect;

// Everything the character selection draws with OpenGL ES 2: the baked stage, vertex-animated actors, camera-facing
// flames and the 2D art and text. It lives on the GL thread and is rebuilt when the surface is created again, so every
// GL object it made belongs to the current context.
internal sealed class CharSelectGraphics
{
    private const int StageStride = 8 * sizeof(float);
    private const float CutOut = 0.45f;
    private const float Faint = 1f / 255f;
    private const int MostTextureSide = 512;
    private const int MostTexts = 64;
    private const string ArtDirectory = "CharSelect";
    private const string FontAsset = ArtDirectory + "/DejaVuSans.ttf";

    private readonly Context _context;
    private readonly float[] _matrix = new float[16];
    private readonly int _program;
    private readonly int _matrixUniform;
    private readonly int _frameUniform;
    private readonly int _uvUniform;
    private readonly int _tintUniform;
    private readonly int _addUniform;
    private readonly int _cutoffUniform;
    private readonly int _fractUniform;
    private readonly int _quad;
    private readonly GlTexture _white;
    private readonly GlTexture[] _textures;
    private readonly GpuStageBatch[] _stage;
    private readonly Dictionary<PlayerClass, GpuClass> _classes = new();
    private readonly Dictionary<string, GlTexture> _art = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Text, int Size), GlTexture> _texts = new();
    private readonly Typeface _typeface;
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private int _surfaceWidth = 1;
    private int _surfaceHeight = 1;
    private Matrix _screen = Matrix.Identity;
    private float _pixelsPerUnit = 1f;
    private bool _world;

    public CharSelectGraphics(Context context, CharSelectSceneData data)
    {
        _context = context;
        _program = CreateProgram();
        _matrixUniform = GLES20.GlGetUniformLocation(_program, "uMatrix");
        _frameUniform = GLES20.GlGetUniformLocation(_program, "uFrame");
        _uvUniform = GLES20.GlGetUniformLocation(_program, "uUv");
        _tintUniform = GLES20.GlGetUniformLocation(_program, "uTint");
        _addUniform = GLES20.GlGetUniformLocation(_program, "uAdd");
        _cutoffUniform = GLES20.GlGetUniformLocation(_program, "uCutoff");
        _fractUniform = GLES20.GlGetUniformLocation(_program, "uFract");
        GLES20.GlUseProgram(_program);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_program, "uTexture"), 0);
        _quad = GlUpload.Buffer([0f, 0f, 0f, 1f, 1f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 1f, 0f, 1f, 1f, 1f, 0f, 1f, 1f, 1f, 0f, 1f, 1f, 1f, 1f, 1f]);
        using (var white = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Argb8888!)!)
        {
            white.EraseColor(Color.White);
            _white = GlUpload.Texture(white, world: false);
        }

        _textures = new GlTexture[data.Textures.Length];
        for (var index = 0; index < _textures.Length; index++)
        {
            _textures[index] = GlUpload.Texture(data.Textures[index], MostTextureSide, world: true);
        }

        _stage = new GpuStageBatch[data.Stage.Length];
        for (var index = 0; index < _stage.Length; index++)
        {
            var batch = data.Stage[index];
            _stage[index] = new GpuStageBatch(GlUpload.Buffer(batch.Vertices), Texture(batch.Texture), batch.Vertices.Length * sizeof(float) / StageStride, batch.Blend);
        }

        _typeface = Typeface.CreateFromAsset(context.Assets, FontAsset) ?? Typeface.Default!;
        _paint.SetTypeface(_typeface);
        _paint.Color = Color.White;
    }

    // The surface in device pixels and the virtual viewport the 2D art is laid out in.
    public void Resize(int surfaceWidth, int surfaceHeight, int viewportWidth, int viewportHeight)
    {
        _surfaceWidth = surfaceWidth;
        _surfaceHeight = surfaceHeight;
        _pixelsPerUnit = surfaceHeight / (float)viewportHeight;
        _screen = new Matrix(2f / viewportWidth, 0f, 0f, 0f, 0f, -2f / viewportHeight, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 1f, 0f, 1f);
    }

    public void BeginFrame(float red, float green, float blue)
    {
        GLES20.GlViewport(0, 0, _surfaceWidth, _surfaceHeight);
        GLES20.GlDisable(GLES20.GlScissorTest);
        GLES20.GlDepthMask(true);
        GLES20.GlClearColor(red, green, blue, 1f);
        GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);
        GLES20.GlUseProgram(_program);
        BeginWorld();
    }

    public void BeginWorld()
    {
        GLES20.GlEnable(GLES20.GlDepthTest);
        GLES20.GlDepthMask(true);
        GLES20.GlDisable(GLES20.GlBlend);
        GLES20.GlUniform4f(_uvUniform, 1f, 1f, 0f, 0f);
        _world = true;
    }

    // The stage's solid and cut-out surfaces.
    public void DrawStage(in Matrix worldViewProjection)
    {
        SetMatrix(worldViewProjection);
        GLES20.GlUniform1f(_frameUniform, 0f);
        GLES20.GlUniform4f(_tintUniform, 1f, 1f, 1f, 1f);
        GLES20.GlUniform3f(_addUniform, 0f, 0f, 0f);
        GLES20.GlUniform1f(_cutoffUniform, CutOut);
        for (var index = 0; index < _stage.Length; index++)
        {
            if (_stage[index].Blend != 2)
            {
                DrawStageBatch(_stage[index]);
            }
        }
    }

    // The stage's see-through surfaces, over everything solid and without writing depth.
    public void DrawStageBlended(in Matrix worldViewProjection)
    {
        SetMatrix(worldViewProjection);
        GLES20.GlUniform1f(_frameUniform, 0f);
        GLES20.GlUniform4f(_tintUniform, 1f, 1f, 1f, 1f);
        GLES20.GlUniform3f(_addUniform, 0f, 0f, 0f);
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlDepthMask(false);
        GLES20.GlUniform1f(_cutoffUniform, Faint);
        for (var index = 0; index < _stage.Length; index++)
        {
            if (_stage[index].Blend == 2)
            {
                DrawStageBatch(_stage[index]);
            }
        }

        GLES20.GlDepthMask(true);
        GLES20.GlDisable(GLES20.GlBlend);
    }

    private void DrawStageBatch(in GpuStageBatch batch)
    {
        BindInterleaved(batch.Buffer);
        BindTexture(batch.Texture);
        GLES20.GlDrawArrays(GLES20.GlTriangles, 0, batch.Vertices);
    }

    public bool IsPrepared(PlayerClass playerClass) => _classes.ContainsKey(playerClass);

    // Uploads a class's body and faces with the textures of its character file.
    public void Prepare(PlayerClass playerClass, CharacterModel model)
    {
        if (_classes.ContainsKey(playerClass))
        {
            return;
        }

        var textures = new GlTexture[model.Textures.Length];
        for (var index = 0; index < textures.Length; index++)
        {
            textures[index] = GlUpload.Texture(model.Textures[index], MostTextureSide, world: true);
        }

        var heads = new GlActorPart[model.Heads.Length];
        var bounds = new (Vector3 Minimum, Vector3 Maximum)[heads.Length];
        for (var face = 0; face < heads.Length; face++)
        {
            heads[face] = new GlActorPart(model.Heads[face], textures, _white);
            var minimum = new Vector3(float.MaxValue);
            var maximum = new Vector3(float.MinValue);
            Extend(model.Body.Clips[MobileActorFormat.SelectStand]!.Frames[0], ref minimum, ref maximum);
            Extend(model.Heads[face].Clips[MobileActorFormat.SelectStand]!.Frames[0], ref minimum, ref maximum);
            bounds[face] = (minimum, maximum);
        }

        _classes.Add(playerClass, new GpuClass(new GlActorPart(model.Body, textures, _white), heads, bounds));
    }

    // A character lit by the ambient colour, its body and the chosen face at the same time of a clip of
    // MobileActorFormat, with glow added.
    public void DrawActor(PlayerClass playerClass, int face, int clip, float seconds, in Matrix worldViewProjection, Vector3 ambient, float glow)
    {
        if (!_classes.TryGetValue(playerClass, out var model) || !model.Body.Has(clip))
        {
            return;
        }

        SetMatrix(worldViewProjection);
        GLES20.GlUniform4f(_tintUniform, 1f, 1f, 1f, 1f);
        GLES20.GlUniform3f(_addUniform, glow, glow, glow);
        GLES20.GlUniform1f(_cutoffUniform, CutOut);
        GLES20.GlDisableVertexAttribArray(2);
        GLES20.GlVertexAttrib3f(2, ambient.X, ambient.Y, ambient.Z);
        var phase = seconds / model.Body.Seconds(clip);
        model.Body.Draw(clip, phase, 0, 1, 3, _frameUniform, _fractUniform);
        model.Heads[Math.Clamp(face, 0, model.Heads.Length - 1)].Draw(clip, phase, 0, 1, 3, _frameUniform, _fractUniform);
        GLES20.GlUniform1f(_frameUniform, 0f);
        GLES20.GlUniform3f(_addUniform, 0f, 0f, 0f);
    }

    // The box around a class's body and face at the start of the selection's stand, in the baked files' space.
    public bool TryBounds(PlayerClass playerClass, int face, out Vector3 minimum, out Vector3 maximum)
    {
        if (!_classes.TryGetValue(playerClass, out var model))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        (minimum, maximum) = model.Bounds[Math.Clamp(face, 0, model.Bounds.Length - 1)];
        return minimum.X <= maximum.X;
    }

    private static void Extend(float[][] frame, ref Vector3 minimum, ref Vector3 maximum)
    {
        foreach (var positions in frame)
        {
            for (var offset = 0; offset + 2 < positions.Length; offset += 3)
            {
                var point = new Vector3(positions[offset], positions[offset + 1], positions[offset + 2]);
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
        }
    }

    public void BeginAdditive()
    {
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOne);
        GLES20.GlDepthMask(false);
        GLES20.GlUniform1f(_frameUniform, 0f);
        GLES20.GlUniform3f(_addUniform, 0f, 0f, 0f);
        GLES20.GlUniform1f(_cutoffUniform, 0f);
        // The image's first row is its top; a billboard's local y points up.
        GLES20.GlUniform4f(_uvUniform, 1f, -1f, 0f, 1f);
        _world = false;
        BindInterleaved(_quad);
    }

    // A square facing the camera, size across, centred on a point.
    public void DrawBillboard(GlTexture texture, Vector3 center, float size, Vector3 cameraRight, Vector3 cameraUp, in Matrix viewProjection, Vector4 color)
    {
        var right = cameraRight * size;
        var up = cameraUp * size;
        var corner = center - (right + up) * 0.5f;
        var world = new Matrix(right.X, right.Y, right.Z, 0f, up.X, up.Y, up.Z, 0f, 0f, 0f, 1f, 0f, corner.X, corner.Y, corner.Z, 1f);
        SetMatrix(world * viewProjection);
        GLES20.GlUniform4f(_tintUniform, color.X, color.Y, color.Z, color.W);
        BindTexture(texture);
        GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
    }

    public void Begin2D()
    {
        GLES20.GlViewport(0, 0, _surfaceWidth, _surfaceHeight);
        GLES20.GlDisable(GLES20.GlScissorTest);
        GLES20.GlDisable(GLES20.GlDepthTest);
        GLES20.GlDepthMask(false);
        _world = false;
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUniform1f(_frameUniform, 0f);
        GLES20.GlUniform3f(_addUniform, 0f, 0f, 0f);
        GLES20.GlUniform1f(_cutoffUniform, 0f);
        BindInterleaved(_quad);
    }

    // Limits drawing to a rectangle of the virtual viewport with a fresh depth buffer, for a 3D view inside the 2D art.
    public void BeginInset(ScreenRect rect)
    {
        var x = (int)(rect.X * _pixelsPerUnit);
        var y = _surfaceHeight - (int)((rect.Y + rect.Height) * _pixelsPerUnit);
        var width = (int)(rect.Width * _pixelsPerUnit);
        var height = (int)(rect.Height * _pixelsPerUnit);
        GLES20.GlViewport(x, y, width, height);
        GLES20.GlEnable(GLES20.GlScissorTest);
        GLES20.GlScissor(x, y, width, height);
        GLES20.GlDepthMask(true);
        GLES20.GlClear(GLES20.GlDepthBufferBit);
        BeginWorld();
    }

    public void Sprite(GlTexture texture, float x, float y, float width, float height, float opacity = 1f) =>
        Sprite(texture, x, y, width, height, 0f, 0f, texture.Width, texture.Height, new Vector4(1f, 1f, 1f, opacity));

    // Art at its own size.
    public void Sprite(GlTexture texture, float x, float y, float opacity = 1f) => Sprite(texture, x, y, texture.Width, texture.Height, opacity);

    public void Sprite(GlTexture texture, ScreenRect rect, float opacity = 1f) => Sprite(texture, rect.X, rect.Y, rect.Width, rect.Height, opacity);

    public void Sprite(GlTexture texture, float x, float y, float width, float height, float sourceX, float sourceY, float sourceWidth, float sourceHeight,
        Vector4 color)
    {
        if (!texture.IsValid)
        {
            return;
        }

        var world = new Matrix(width, 0f, 0f, 0f, 0f, height, 0f, 0f, 0f, 0f, 1f, 0f, x, y, 0f, 1f);
        SetMatrix(world * _screen);
        GLES20.GlUniform4f(_uvUniform, sourceWidth / texture.Width, sourceHeight / texture.Height, sourceX / texture.Width, sourceY / texture.Height);
        GLES20.GlUniform4f(_tintUniform, color.X, color.Y, color.Z, color.W);
        BindTexture(texture);
        GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
    }

    public void Fill(float x, float y, float width, float height, Vector4 color) => Sprite(_white, x, y, width, height, 0f, 0f, 1f, 1f, color);

    // Text with its line's top left at (x, y); emSize is the font's em in virtual viewport units.
    public void Text(string text, float x, float y, float emSize, Vector4 color)
    {
        if (text.Length == 0)
        {
            return;
        }

        var texture = TextTexture(text, Math.Max(1, (int)MathF.Round(emSize * _pixelsPerUnit)));
        Sprite(texture, x, y, texture.Width / _pixelsPerUnit, texture.Height / _pixelsPerUnit, 0f, 0f, texture.Width, texture.Height, color);
    }

    private GlTexture TextTexture(string text, int pixelSize)
    {
        if (_texts.TryGetValue((text, pixelSize), out var cached))
        {
            return cached;
        }

        if (_texts.Count >= MostTexts)
        {
            foreach (var old in _texts.Values)
            {
                GLES20.GlDeleteTextures(1, [old.Id], 0);
            }

            _texts.Clear();
        }

        _paint.TextSize = pixelSize;
        var metrics = _paint.GetFontMetrics()!;
        var width = Math.Max(1, (int)MathF.Ceiling(_paint.MeasureText(text)));
        var height = Math.Max(1, (int)MathF.Ceiling(metrics.Descent - metrics.Ascent));
        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        using (var canvas = new Canvas(bitmap))
        {
            canvas.DrawText(text, 0f, -metrics.Ascent, _paint);
        }

        var texture = GlUpload.Texture(bitmap, world: false);
        _texts.Add((text, pixelSize), texture);
        return texture;
    }

    // The screen's art by its source file name (CharacterSelectArt), packaged as PNG.
    public GlTexture Art(string sourceName)
    {
        if (_art.TryGetValue(sourceName, out var cached))
        {
            return cached;
        }

        GlTexture texture = default;
        try
        {
            using var stream = _context.Assets!.Open($"{ArtDirectory}/{System.IO.Path.ChangeExtension(sourceName, ".png")}");
            using var bitmap = BitmapFactory.DecodeStream(stream);
            if (bitmap is not null)
            {
                texture = GlUpload.Texture(bitmap, world: false);
            }
        }
        catch (Java.IO.IOException)
        {
        }

        _art.Add(sourceName, texture);
        return texture;
    }

    private GlTexture Texture(int index) => index >= 0 && index < _textures.Length && _textures[index].IsValid ? _textures[index] : _white;

    private void BindTexture(GlTexture texture)
    {
        GLES20.GlBindTexture(GLES20.GlTexture2d, texture.Id);
        // A world texture that cannot repeat is wrapped in the shader; 2D art never wraps.
        GLES20.GlUniform1f(_fractUniform, _world && !texture.Repeats ? 1f : 0f);
    }

    private void BindInterleaved(int buffer)
    {
        BindAttribute(0, buffer, 3, StageStride, 0);
        BindAttribute(1, buffer, 3, StageStride, 0);
        BindAttribute(2, buffer, 3, StageStride, 3 * sizeof(float));
        BindAttribute(3, buffer, 2, StageStride, 6 * sizeof(float));
    }

    private static void BindAttribute(int attribute, int buffer, int size, int stride, int offset)
    {
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, buffer);
        GLES20.GlEnableVertexAttribArray(attribute);
        GLES20.GlVertexAttribPointer(attribute, size, GLES20.GlFloat, false, stride, offset);
    }

    private void SetMatrix(in Matrix matrix)
    {
        _matrix[0] = matrix.M11;
        _matrix[1] = matrix.M12;
        _matrix[2] = matrix.M13;
        _matrix[3] = matrix.M14;
        _matrix[4] = matrix.M21;
        _matrix[5] = matrix.M22;
        _matrix[6] = matrix.M23;
        _matrix[7] = matrix.M24;
        _matrix[8] = matrix.M31;
        _matrix[9] = matrix.M32;
        _matrix[10] = matrix.M33;
        _matrix[11] = matrix.M34;
        _matrix[12] = matrix.M41;
        _matrix[13] = matrix.M42;
        _matrix[14] = matrix.M43;
        _matrix[15] = matrix.M44;
        GLES20.GlUniformMatrix4fv(_matrixUniform, 1, false, _matrix, 0);
    }

    private static int CreateProgram()
    {
        var vertex = Compile(GLES20.GlVertexShader, """
            attribute vec3 aPosition;
            attribute vec3 aNext;
            attribute vec3 aColor;
            attribute vec2 aUv;
            uniform mat4 uMatrix;
            uniform float uFrame;
            uniform vec4 uUv;
            varying vec3 vColor;
            varying vec2 vUv;
            void main() {
                gl_Position = uMatrix * vec4(mix(aPosition, aNext, uFrame), 1.0);
                vColor = aColor;
                vUv = aUv * uUv.xy + uUv.zw;
            }
            """);
        var fragment = Compile(GLES20.GlFragmentShader, """
            precision mediump float;
            varying vec3 vColor;
            varying vec2 vUv;
            uniform sampler2D uTexture;
            uniform vec4 uTint;
            uniform vec3 uAdd;
            uniform float uCutoff;
            uniform float uFract;
            void main() {
                vec4 texel = texture2D(uTexture, mix(vUv, fract(vUv), uFract));
                if (texel.a < uCutoff) discard;
                gl_FragColor = vec4(texel.rgb * vColor * uTint.rgb + uAdd, texel.a * uTint.a);
            }
            """);
        var program = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(program, vertex);
        GLES20.GlAttachShader(program, fragment);
        GLES20.GlBindAttribLocation(program, 0, "aPosition");
        GLES20.GlBindAttribLocation(program, 1, "aNext");
        GLES20.GlBindAttribLocation(program, 2, "aColor");
        GLES20.GlBindAttribLocation(program, 3, "aUv");
        GLES20.GlLinkProgram(program);
        GLES20.GlDeleteShader(vertex);
        GLES20.GlDeleteShader(fragment);
        var status = new int[1];
        GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, status, 0);
        return status[0] != 0 ? program : throw new InvalidOperationException(GLES20.GlGetProgramInfoLog(program));
    }

    private static int Compile(int kind, string source)
    {
        var shader = GLES20.GlCreateShader(kind);
        GLES20.GlShaderSource(shader, source);
        GLES20.GlCompileShader(shader);
        var status = new int[1];
        GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, status, 0);
        return status[0] != 0 ? shader : throw new InvalidOperationException(GLES20.GlGetShaderInfoLog(shader));
    }

    private readonly record struct GpuStageBatch(int Buffer, GlTexture Texture, int Vertices, int Blend);

    private sealed record GpuClass(GlActorPart Body, GlActorPart[] Heads, (Vector3 Minimum, Vector3 Maximum)[] Bounds);
}
