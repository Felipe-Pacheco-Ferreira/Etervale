using System;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>Decodes DXT texture blocks into tightly packed RGBA pixels without creating a graphics device.</summary>
    public static class DxtDecoder
    {
        /// <summary>Decodes the first two-dimensional texture level in the supplied block data.</summary>
        /// <param name="blocks">DXT blocks, in row-major block order.</param>
        /// <param name="width">Positive pixel width.</param>
        /// <param name="height">Positive pixel height.</param>
        /// <param name="format">The Dxt1, Dxt3 or Dxt5 compression format.</param>
        /// <returns>Four bytes per pixel, in red, green, blue, alpha order.</returns>
        /// <exception cref="ArgumentNullException">The block data is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Dimensions or format are unsupported.</exception>
        /// <exception cref="ArgumentException">The block data is truncated or the image is too large.</exception>
        public static byte[] Decode(byte[] blocks, int width, int height, SurfaceFormat format)
        {
            if (blocks == null)
            {
                throw new ArgumentNullException(nameof(blocks));
            }
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            var blockBytes = format == SurfaceFormat.Dxt1 ? 8 : format == SurfaceFormat.Dxt3 || format == SurfaceFormat.Dxt5 ? 16 : 0;
            if (blockBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }
            var byteCount = ((width + 3L) / 4) * ((height + 3L) / 4) * blockBytes;
            if ((long)width * height > int.MaxValue / 4)
            {
                throw new ArgumentException("The decoded image is too large.", nameof(width));
            }
            if (byteCount > blocks.Length)
            {
                throw new ArgumentException("The compressed texture level is truncated.", nameof(blocks));
            }
            switch (format)
            {
                case SurfaceFormat.Dxt1: return DxtUtil.DecompressDxt1(blocks, width, height);
                case SurfaceFormat.Dxt3: return DxtUtil.DecompressDxt3(blocks, width, height);
                default: return DxtUtil.DecompressDxt5(blocks, width, height);
            }
        }
    }
}
