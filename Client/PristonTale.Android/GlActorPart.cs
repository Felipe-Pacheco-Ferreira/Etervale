using System.Runtime.InteropServices;
using Android.Graphics;
using Android.Opengl;
using Java.Nio;

namespace PristonTale.Mobile;

internal readonly record struct GlTexture(int Id, int Width, int Height, bool Repeats)
{
    public bool IsValid => Id != 0;
}

// Buffer and texture uploads shared by the Android renderers. Everything here runs on a GL thread with its context
// current.
internal static class GlUpload
{
    public static int Buffer(float[] values)
    {
        var buffers = new int[1];
        GLES20.GlGenBuffers(1, buffers, 0);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, buffers[0]);
        var size = values.Length * sizeof(float);
        using var bytes = ByteBuffer.AllocateDirect(Math.Max(size, 1))!;
        // Copied straight into the direct buffer's memory, which is in the machine's byte order as the array is.
        Marshal.Copy(values, 0, bytes.GetDirectBufferAddress(), values.Length);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, size, bytes, GLES20.GlStaticDraw);
        return buffers[0];
    }

    // Decodes an encoded picture no larger than mostSide on its longer side.
    public static GlTexture Texture(byte[] data, int mostSide, bool world)
    {
        using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(data, 0, data.Length, options)?.Dispose();
        var sample = 1;
        while (Math.Max(options.OutWidth, options.OutHeight) / sample > mostSide)
        {
            sample *= 2;
        }

        options.InJustDecodeBounds = false;
        options.InSampleSize = sample;
        using var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, options);
        return bitmap is null ? default : Texture(bitmap, world);
    }

    // World textures of power-of-two size repeat and are mip-mapped; the others clamp, and a shader that needs them to
    // repeat wraps the coordinates itself.
    public static GlTexture Texture(Bitmap bitmap, bool world)
    {
        var id = new int[1];
        GLES20.GlGenTextures(1, id, 0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, id[0]);
        var repeats = world && IsPowerOfTwo(bitmap.Width) && IsPowerOfTwo(bitmap.Height);
        var wrap = repeats ? GLES20.GlRepeat : GLES20.GlClampToEdge;
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, repeats ? GLES20.GlLinearMipmapLinear : GLES20.GlLinear);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapS, wrap);
        GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapT, wrap);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bitmap, 0);
        if (repeats)
        {
            GLES20.GlGenerateMipmap(GLES20.GlTexture2d);
        }

        return new GlTexture(id[0], bitmap.Width, bitmap.Height, repeats);
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}

// An animated part on the GPU: one buffer of texture coordinates a batch, and one buffer of positions a batch for every
// frame of every clip. Drawing binds the two frames around the clip's time; the shader blends them.
internal sealed class GlActorPart
{
    private readonly int[] _uv;
    private readonly GlTexture[] _textures;
    private readonly int[] _vertices;
    private readonly Clip?[] _clips;

    private sealed record Clip(float Seconds, int[][] Frames);

    // textures are the part's file's, by the indices its batches hold; missing is drawn for a batch without one.
    public GlActorPart(ActorPart part, GlTexture[] textures, GlTexture missing)
    {
        _uv = new int[part.Batches.Length];
        _textures = new GlTexture[part.Batches.Length];
        _vertices = new int[part.Batches.Length];
        for (var batch = 0; batch < _uv.Length; batch++)
        {
            var index = part.Batches[batch].Texture;
            _uv[batch] = GlUpload.Buffer(part.Batches[batch].Uv);
            _textures[batch] = index >= 0 && index < textures.Length && textures[index].IsValid ? textures[index] : missing;
            _vertices[batch] = part.Batches[batch].Uv.Length / 2;
        }

        _clips = new Clip?[part.Clips.Length];
        for (var clip = 0; clip < _clips.Length; clip++)
        {
            if (part.Clips[clip] is not { } source)
            {
                continue;
            }

            var frames = new int[source.Frames.Length][];
            for (var frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = new int[source.Frames[frame].Length];
                for (var batch = 0; batch < frames[frame].Length; batch++)
                {
                    frames[frame][batch] = GlUpload.Buffer(source.Frames[frame][batch]);
                }
            }

            _clips[clip] = new Clip(source.Seconds, frames);
        }
    }

    public bool Has(int clip) => clip >= 0 && clip < _clips.Length && _clips[clip] is not null;

    // The clip's length in seconds, or 0 when the part lacks it.
    public float Seconds(int clip) => Has(clip) ? _clips[clip]!.Seconds : 0f;

    // Draws the clip at phase (0 to 1 of its length) through the given attributes; frameUniform takes the blend between
    // the two frames and wrapUniform, when not negative, 1 for a texture the shader must wrap itself and 0 otherwise.
    public void Draw(int clip, float phase, int positionAttribute, int nextAttribute, int uvAttribute, int frameUniform, int wrapUniform)
    {
        if (!Has(clip))
        {
            return;
        }

        var frames = _clips[clip]!.Frames;
        var at = (phase - MathF.Floor(phase)) * frames.Length;
        var index = (int)at % frames.Length;
        var current = frames[index];
        var next = frames[(index + 1) % frames.Length];
        GLES20.GlUniform1f(frameUniform, at - (int)at);
        for (var batch = 0; batch < _uv.Length; batch++)
        {
            Bind(positionAttribute, current[batch], 3);
            Bind(nextAttribute, next[batch], 3);
            Bind(uvAttribute, _uv[batch], 2);
            GLES20.GlBindTexture(GLES20.GlTexture2d, _textures[batch].Id);
            if (wrapUniform >= 0)
            {
                GLES20.GlUniform1f(wrapUniform, _textures[batch].Repeats ? 0f : 1f);
            }

            GLES20.GlDrawArrays(GLES20.GlTriangles, 0, _vertices[batch]);
        }
    }

    private static void Bind(int attribute, int buffer, int size)
    {
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, buffer);
        GLES20.GlEnableVertexAttribArray(attribute);
        GLES20.GlVertexAttribPointer(attribute, size, GLES20.GlFloat, false, 0, 0);
    }
}
