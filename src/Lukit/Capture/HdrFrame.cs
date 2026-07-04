using System;

namespace Lukit.Capture;

/// <summary>
/// A captured frame in linear scRGB (Rec.709 primaries) where a channel value of
/// 1.0 corresponds to 80 nits — the reference white of the scRGB color space, and
/// the format the Windows Desktop is composited in while HDR is enabled.
///
/// Values may be greater than 1.0 (brighter than 80 nits) and, for colors outside
/// the sRGB gamut, slightly negative. Keeping the raw linear data (rather than an
/// already tone-mapped 8-bit image) lets us re-tone-map with different settings
/// without re-capturing.
/// </summary>
public sealed class HdrFrame
{
    /// <summary>Width in physical pixels.</summary>
    public int Width { get; }

    /// <summary>Height in physical pixels.</summary>
    public int Height { get; }

    /// <summary>Interleaved R,G,B triples, length Width*Height*3, linear scRGB.</summary>
    public float[] Rgb { get; }

    public HdrFrame(int width, int height, float[] rgb)
    {
        if (rgb.Length < (long)width * height * 3)
            throw new ArgumentException("Pixel buffer too small for the given dimensions.");
        Width = width;
        Height = height;
        Rgb = rgb;
    }

    /// <summary>
    /// Returns a new frame cropped to the given rectangle (in this frame's pixel
    /// coordinates). The rectangle is clamped to the frame bounds.
    /// </summary>
    public HdrFrame Crop(int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, Width);
        y = Math.Clamp(y, 0, Height);
        w = Math.Clamp(w, 1, Width - x);
        h = Math.Clamp(h, 1, Height - y);

        var dst = new float[w * h * 3];
        for (int row = 0; row < h; row++)
        {
            int srcStart = ((y + row) * Width + x) * 3;
            int dstStart = row * w * 3;
            Array.Copy(Rgb, srcStart, dst, dstStart, w * 3);
        }
        return new HdrFrame(w, h, dst);
    }
}
