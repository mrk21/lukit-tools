using System;
using System.Collections.Generic;

namespace Lukit.Display;

/// <summary>
/// One monitor's tone-mapped BGRA image together with its virtual-desktop rectangle.
/// </summary>
/// <param name="Bounds">Monitor rectangle in virtual-desktop pixels (drives placement and canvas size).</param>
/// <param name="Bgra">Tone-mapped BGRA32 pixels, length <paramref name="Width"/>*<paramref name="Height"/>*4.</param>
/// <param name="Width">Pixel width of <paramref name="Bgra"/>.</param>
/// <param name="Height">Pixel height of <paramref name="Bgra"/>.</param>
internal readonly record struct MonitorTile(Monitors.RECT Bounds, byte[] Bgra, int Width, int Height);

/// <summary>A composited BGRA image plus the row stride needed to interpret it.</summary>
internal readonly record struct ComposedImage(byte[] Bgra, int Width, int Height, int Stride);

/// <summary>
/// Composites several monitors' tone-mapped images into a single image at their
/// virtual-desktop positions. Pure integer geometry + array copies (no GPU/desktop
/// dependency), so it is unit-tested directly. Each monitor keeps its own tone
/// mapping; gaps between non-adjacent monitors are left opaque black.
/// </summary>
internal static class DesktopComposite
{
    public static ComposedImage Compose(IReadOnlyList<MonitorTile> tiles)
    {
        if (tiles.Count == 0)
            throw new ArgumentException("At least one monitor tile is required.", nameof(tiles));

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var t in tiles)
        {
            minX = Math.Min(minX, t.Bounds.Left);
            minY = Math.Min(minY, t.Bounds.Top);
            maxX = Math.Max(maxX, t.Bounds.Right);
            maxY = Math.Max(maxY, t.Bounds.Bottom);
        }

        int width = maxX - minX;
        int height = maxY - minY;
        int stride = width * 4;
        var canvas = new byte[(long)height * stride];
        for (long i = 3; i < canvas.LongLength; i += 4)
            canvas[i] = 255; // opaque black in any gaps between monitors

        foreach (var t in tiles)
            Blit(t.Bgra, t.Width, t.Height, canvas, stride, t.Bounds.Left - minX, t.Bounds.Top - minY, width, height);

        return new ComposedImage(canvas, width, height, stride);
    }

    // Copies a source BGRA tile into the canvas at (offX, offY), clipping any part that
    // would fall outside the canvas (e.g. a captured image larger than the monitor rect).
    private static void Blit(byte[] src, int sw, int sh, byte[] dst, int dstStride, int offX, int offY, int dstW, int dstH)
    {
        int srcStride = sw * 4;
        for (int y = 0; y < sh; y++)
        {
            int dy = offY + y;
            if (dy < 0 || dy >= dstH || offX < 0) continue;
            int copyW = Math.Min(sw, dstW - offX);
            if (copyW <= 0) continue;
            Array.Copy(src, (long)y * srcStride, dst, (long)dy * dstStride + (long)offX * 4, (long)copyW * 4);
        }
    }
}
