using System.Diagnostics;
using System.Numerics;
using Android.Content;
using Android.Graphics;
using MediaPlayer = Android.Media.MediaPlayer;
using Android.Views;
using PristonTale.Contracts.World;
using PristonTale.Geometry;

namespace PristonTale.Mobile;

// Drops are attached to world positions and stop at the topmost collision surface, including roofs.
internal sealed class MobileRainView(Context context, RicartenSceneView scene, CollisionMesh terrain, MediaPlayer? rainSound, MediaPlayer? thunderSound) : View(context)
{
    private const float Cell = 12f;
    private const float FieldSize = 540f;
    private const float DropHeight = 310f;
    private readonly Paint _paint = new() { AntiAlias = true };
    private readonly Drop[] _drops = CreateDrops();
    private long _previousFrame;
    private float _time;
    private float _intensity;
    private float _target;
    private Vector2 _wind;
    private bool _receivedWeather;
    private bool _storm;
    private float _strikeIn;
    private float _thunderIn = -1f;
    private float _flash;
    private float _audioIn;

    public void SetWeather(WeatherState weather)
    {
        var storm = weather.Kind == WeatherKind.Storm;
        if (storm && !_storm)
        {
            _strikeIn = 2f + Random.Shared.NextSingle() * 6f;
        }
        _storm = storm;
        _target = weather.Raining ? weather.Intensity / 255f : 0f;
        var heading = weather.WindHeading * MathF.Tau / 256f;
        _wind = new Vector2(MathF.Sin(heading), MathF.Cos(heading)) * (weather.Wind / 255f);
        if (!_receivedWeather)
        {
            _receivedWeather = true;
            _intensity = _target;
        }
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var now = Stopwatch.GetTimestamp();
        var dt = _previousFrame == 0 ? 0f : Math.Min(.05f, (float)Stopwatch.GetElapsedTime(_previousFrame, now).TotalSeconds);
        _previousFrame = now;
        _time += dt;
        _intensity += (_target - _intensity) * (1f - MathF.Exp(-dt * .75f));
        if (_target == 0f && _intensity < .004f)
        {
            _previousFrame = 0;
            if (rainSound?.IsPlaying == true)
            {
                rainSound.Pause();
            }
            return;
        }

        var eye = scene.CameraEye;
        var center = scene.LocalPosition;
        UpdateStorm(canvas, dt);
        _audioIn -= dt;
        if (rainSound is not null && _audioIn <= 0f)
        {
            _audioIn = .25f;
            var sheltered = terrain.TryGetFloorHeight(center.X, center.Z, out var roof) && roof > center.Y + 40f;
            var volume = .8f * _intensity * (sheltered ? .45f : 1f);
            rainSound.Looping = true;
            rainSound.SetVolume(volume, volume);
            if (!rainSound.IsPlaying)
            {
                rainSound.Start();
            }
        }
        var count = (int)MathF.Ceiling(_drops.Length * MathF.Sqrt(_intensity));
        var opacity = MathF.Min(1f, _intensity * 4f);
        _paint.StrokeWidth = Math.Max(1f, Height / 780f);
        for (var index = 0; index < count; index++)
        {
            ref var drop = ref _drops[index];
            var x = Wrap(drop.X * FieldSize, center.X);
            var z = Wrap(drop.Z * FieldSize, center.Z);
            var cellX = (int)MathF.Floor(x / Cell);
            var cellZ = (int)MathF.Floor(z / Cell);
            if (drop.CellX != cellX || drop.CellZ != cellZ)
            {
                drop.CellX = cellX;
                drop.CellZ = cellZ;
                drop.HasSurface = terrain.TryGetFloorHeight(x, z, out drop.Surface);
            }
            if (!drop.HasSurface)
            {
                continue;
            }

            var phase = Fraction(drop.Phase + _time * drop.Speed);
            if (phase > .92f)
            {
                DrawSplash(canvas, eye, new Vector3(x, drop.Surface + .5f, z), (phase - .92f) / .08f, opacity);
                continue;
            }

            var height = DropHeight * (1f - phase / .92f);
            var tip = new Vector3(x - _wind.X * height * .22f, drop.Surface + height, z - _wind.Y * height * .22f);
            var tail = tip + new Vector3(-_wind.X * drop.Length * .22f, drop.Length, -_wind.Y * drop.Length * .22f);
            if (!scene.Project(tip, out var a) || !scene.Project(tail, out var b)
                || a.X < -30f || a.X > Width + 30f || a.Y < -30f || a.Y > Height + 30f
                || terrain.TryTraceSegment(eye, tip, out _, out _))
            {
                continue;
            }

            _paint.Color = Color.Argb((int)(drop.Alpha * opacity), 188, 207, 224);
            canvas.DrawLine(a.X, a.Y, b.X, b.Y, _paint);
        }
        PostInvalidateOnAnimation();
    }

    private void UpdateStorm(Canvas canvas, float seconds)
    {
        if (_storm && (_strikeIn -= seconds) <= 0f)
        {
            _strikeIn = 4f + Random.Shared.NextSingle() * 13f;
            _thunderIn = .4f + Random.Shared.NextSingle() * 3f;
            _flash = .12f + Random.Shared.NextSingle() * .10f;
        }
        if (_thunderIn >= 0f && (_thunderIn -= seconds) <= 0f)
        {
            _thunderIn = -1f;
            if (thunderSound is not null)
            {
                thunderSound.SeekTo(0);
                thunderSound.Start();
            }
        }
        if (_flash > .002f)
        {
            _flash *= MathF.Exp(-seconds * 11f);
            canvas.DrawColor(Color.Argb((int)(_flash * 255f), 200, 216, 255));
        }
    }

    private void DrawSplash(Canvas canvas, Vector3 eye, Vector3 surface, float phase, float opacity)
    {
        if (!scene.Project(surface, out var center) || center.X < 0f || center.X > Width || center.Y < 0f || center.Y > Height
            || terrain.TryTraceSegment(eye, surface, out _, out _)
            || !scene.Project(surface + Vector3.UnitY * (1f + 4f * MathF.Sin(phase * MathF.PI)), out var spray))
        {
            return;
        }

        _paint.Color = Color.Argb((int)(112f * (1f - phase) * opacity), 198, 215, 231);
        var spread = 1f + phase * 4f;
        canvas.DrawLine(center.X, center.Y, spray.X - spread, spray.Y, _paint);
        canvas.DrawLine(center.X, center.Y, spray.X + spread, spray.Y, _paint);
    }

    private static float Fraction(float value) => value - MathF.Floor(value);

    private static float Wrap(float offset, float center)
    {
        var origin = center - FieldSize * .5f;
        return origin + Fraction((offset - origin) / FieldSize) * FieldSize;
    }

    private static Drop[] CreateDrops()
    {
        var random = new Random(701);
        var drops = new Drop[190];
        for (var index = 0; index < drops.Length; index++)
        {
            drops[index] = new Drop
            {
                X = random.NextSingle(), Z = random.NextSingle(), Phase = random.NextSingle(),
                Speed = 1.2f + random.NextSingle() * 1.4f, Length = 12f + random.NextSingle() * 26f,
                Alpha = random.Next(42, 120), CellX = int.MinValue, CellZ = int.MinValue
            };
        }
        return drops;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rainSound?.Release();
            thunderSound?.Release();
            _paint.Dispose();
        }
        base.Dispose(disposing);
    }

    private struct Drop
    {
        public float X, Z, Phase, Speed, Length, Surface;
        public int Alpha, CellX, CellZ;
        public bool HasSurface;
    }
}
