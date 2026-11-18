using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

[assembly: SupportedOSPlatform("browser")]

// The page (wwwroot/main.js) creates the WebGPU device, starts the runtime, calls Start once and Frame on every animation
// frame, then reads the checks back with Report and samples the canvas where SpriteSamples says.
return;

internal static partial class WebGpuRepro
{
    private static ReproGame? _game;
    private static bool _runReturned;

    // Game.Run returns only under the asynchronous run behaviour; the synchronous loop would never give the page back.
    [JSExport]
    public static void Start()
    {
        _game = new ReproGame();
        _game.Run();
        _runReturned = true;
    }

    [JSExport]
    public static void Frame()
    {
        _game?.Tick();
    }

    // One line per check, "ok" or "FAIL", in the order Program.cs lists them.
    [JSExport]
    public static string Report()
    {
        var game = _game ?? throw new InvalidOperationException("Start was not called.");
        return string.Join('\n',
            Check("the runtime reports the WebGPU backend", PlatformInfo.GraphicsBackend == GraphicsBackend.WebGPU),
            Check("the browser asks for the asynchronous run behaviour", _runReturned),
            Check("the back buffer has the canvas's drawing size", game.BackBufferMatchesCanvas),
            Check("Draw ran on animation frames", game.Frames >= 2),
            Check("index and vertex buffers grew after earlier draws in the frame", game.BufferGrowthDrawn));
    }

    // Where the page samples the canvas and the colour it must find, one "x,y,r,g,b,name" line per sample: the centre of
    // each quadrant of the sprite (a 2x2 texture drawn point-sampled), and a point of the background outside it.
    [JSExport]
    public static string SpriteSamples()
    {
        var game = _game ?? throw new InvalidOperationException("Start was not called.");
        var sprite = game.SpriteBounds;
        var quarterX = sprite.Width / 4;
        var quarterY = sprite.Height / 4;
        return string.Join('\n',
            Sample(sprite.Left + quarterX, sprite.Top + quarterY, ReproGame.TopLeft, "the sprite's top-left texel"),
            Sample(sprite.Right - quarterX, sprite.Top + quarterY, ReproGame.TopRight, "the sprite's top-right texel"),
            Sample(sprite.Left + quarterX, sprite.Bottom - quarterY, ReproGame.BottomLeft, "the sprite's bottom-left texel"),
            Sample(sprite.Right - quarterX, sprite.Bottom - quarterY, ReproGame.BottomRight, "the sprite's bottom-right texel"),
            Sample(sprite.Left / 2, sprite.Top / 2, ReproGame.Background, "the cleared background"),
            Sample(game.CheckCentre.X, game.CheckCentre.Y, ReproGame.CheckPassed, "the compute check's quad (dispatches, atomics, storage buffers)"));
    }

    private static string Check(string name, bool passed) => $"{(passed ? "ok  " : "FAIL")} {name}";

    private static string Sample(int x, int y, Color color, string name) =>
        string.Create(CultureInfo.InvariantCulture, $"{x},{y},{color.R},{color.G},{color.B},{name}");

    private sealed class ReproGame : Game
    {
        public static readonly Color Background = new(40, 90, 160);
        public static readonly Color TopLeft = new(220, 30, 30);
        public static readonly Color TopRight = new(240, 220, 40);
        public static readonly Color BottomLeft = new(30, 200, 70);
        public static readonly Color BottomRight = new(250, 250, 250);
        // ComputeCheck.fx's verdict colours: (0.1, 0.8, 0.3) when the count matched, (0.9, 0.1, 0.1) when not.
        public static readonly Color CheckPassed = new(26, 204, 77);

        // The count dispatch's groups of 64 threads (CS_Count).
        private const int CheckGroups = 4;
        private const int CheckGroupSize = 64;
        private const int GrowthSpriteCount = 1024;
        private static readonly VertexPositionColorTexture[] GrowthQuad =
        [
            new(new Vector3(-4f, -4f, 0f), Color.White, Vector2.Zero),
            new(new Vector3(-3f, -4f, 0f), Color.White, Vector2.Zero),
            new(new Vector3(-4f, -3f, 0f), Color.White, Vector2.Zero),
            new(new Vector3(-3f, -3f, 0f), Color.White, Vector2.Zero)
        ];
        private static readonly short[] GrowthIndices = CreateGrowthIndices();
        // The check's quad in clip space: from -0.9 to -0.6 on both axes, the lower left of the canvas.
        private const float CheckLow = -0.9f;
        private const float CheckHigh = -0.6f;

        private static readonly VertexPosition[] CheckQuad =
        [
            new(new Vector3(CheckLow, CheckHigh, 0.5f)),
            new(new Vector3(CheckHigh, CheckHigh, 0.5f)),
            new(new Vector3(CheckLow, CheckLow, 0.5f)),
            new(new Vector3(CheckHigh, CheckLow, 0.5f)),
        ];

        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch? _spriteBatch;
        private Texture2D? _quadrants;
        private Effect? _check;
        private StructuredBuffer? _totals;
        private StructuredBuffer? _colors;

        public ReproGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            IsFixedTimeStep = false;
            Content = new EmbeddedContent(Services);
        }

        // The centre of the check's quad in back buffer pixels (clip space y points up).
        public Point CheckCentre
        {
            get
            {
                var parameters = GraphicsDevice.PresentationParameters;
                var middle = (CheckLow + CheckHigh) / 2f;
                return new Point((int)((middle + 1f) / 2f * parameters.BackBufferWidth), (int)((1f - middle) / 2f * parameters.BackBufferHeight));
            }
        }

        public int Frames { get; private set; }
        public bool BufferGrowthDrawn { get; private set; }

        // A square in the middle of the back buffer, half its height.
        public Rectangle SpriteBounds
        {
            get
            {
                var parameters = GraphicsDevice.PresentationParameters;
                var size = parameters.BackBufferHeight / 2;
                return new Rectangle((parameters.BackBufferWidth - size) / 2, (parameters.BackBufferHeight - size) / 2, size, size);
            }
        }

        public bool BackBufferMatchesCanvas =>
            GraphicsDevice.PresentationParameters.BackBufferWidth == Window.ClientBounds.Width
            && GraphicsDevice.PresentationParameters.BackBufferHeight == Window.ClientBounds.Height
            && Window.ClientBounds.Width > 0;

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _quadrants = new Texture2D(GraphicsDevice, 2, 2);
            _quadrants.SetData([TopLeft, TopRight, BottomLeft, BottomRight]);
            _check = Content.Load<Effect>("ComputeCheck");
            _totals = new StructuredBuffer(GraphicsDevice, sizeof(uint), 1, BufferShaderAccess.ReadWrite);
            _colors = new StructuredBuffer(GraphicsDevice, 16, 1, BufferShaderAccess.ReadWrite);
            _check.Parameters["Expected"].SetValue(new Vector4(CheckGroups * CheckGroupSize, 0f, 0f, 0f));
            _check.Parameters["Totals"].SetValue(_totals);
            _check.Parameters["Colors"].SetValue(_colors);
            _check.Parameters["Verdict"].SetValue(_colors);
            base.LoadContent();
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Background);
            if (_spriteBatch != null && _quadrants != null)
            {
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                _spriteBatch.Draw(_quadrants, SpriteBounds, Color.White);
                _spriteBatch.End();
                if (!BufferGrowthDrawn)
                {
                    DrawBufferGrowth();
                    BufferGrowthDrawn = true;
                }
            }

            // The dispatches split the frame's render pass; the quad drawn after them loads what the sprite drew.
            if (_check != null)
            {
                Run(_check.Techniques["Count"], CheckGroups);
                Run(_check.Techniques["Resolve"], 1);
                _check.CurrentTechnique = _check.Techniques["Show"];
                _check.CurrentTechnique.Passes[0].Apply();
                GraphicsDevice.DepthStencilState = DepthStencilState.None;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, CheckQuad, 0, 2);
            }

            Frames++;
            base.Draw(gameTime);
        }

        private static short[] CreateGrowthIndices()
        {
            // Above the framework's initial 6000 indices, with just four vertices so only the index buffer grows.
            var indices = new short[6006];
            for (var offset = 0; offset < indices.Length; offset += 6)
            {
                indices[offset] = 0;
                indices[offset + 1] = 1;
                indices[offset + 2] = 2;
                indices[offset + 3] = 1;
                indices[offset + 4] = 3;
                indices[offset + 5] = 2;
            }
            return indices;
        }

        private void DrawBufferGrowth()
        {
            GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, GrowthQuad, 0, GrowthQuad.Length,
                GrowthIndices, 0, GrowthIndices.Length / 3);
            // This batch then replaces the vertex buffer still used by the index-growth draw.
            _spriteBatch!.Begin(samplerState: SamplerState.PointClamp);
            for (var sprite = 0; sprite < GrowthSpriteCount; sprite++)
            {
                _spriteBatch.Draw(_quadrants!, new Rectangle(-4, -4, 1, 1), Color.White);
            }
            _spriteBatch.End();
        }

        private void Run(EffectTechnique technique, int groups)
        {
            _check!.CurrentTechnique = technique;
            technique.Passes[0].Apply();
            GraphicsDevice.Dispatch(groups, 1, 1);
        }

        protected override void UnloadContent()
        {
            _colors?.Dispose();
            _totals?.Dispose();
            _quadrants?.Dispose();
            _spriteBatch?.Dispose();
            base.UnloadContent();
        }
    }

    // Content built into the assembly (WebGpuRepro.csproj's EmbedReproContent), read without files.
    private sealed class EmbeddedContent(IServiceProvider services) : ContentManager(services)
    {
        protected override Stream OpenStream(string assetName) =>
            typeof(EmbeddedContent).Assembly.GetManifestResourceStream(assetName + ".xnb")
            ?? throw new ContentLoadException($"No embedded content named {assetName}.");
    }
}
