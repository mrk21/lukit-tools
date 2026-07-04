using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Lukit.UI;

/// <summary>Draws the tray icon at runtime so the app needs no embedded .ico asset.</summary>
internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Rounded dark tile with an HDR-ish bright-to-dark gradient (a nod to dynamic range).
            var rect = new Rectangle(1, 1, 30, 30);
            using var path = RoundedRect(rect, 7);
            using var bg = new LinearGradientBrush(rect, Color.FromArgb(28, 30, 38), Color.FromArgb(60, 66, 82), 45f);
            g.FillPath(bg, path);

            // A bright "sun" highlight (the HDR highlight that survives tone mapping).
            using var hi = new LinearGradientBrush(
                new Rectangle(6, 6, 12, 12), Color.White, Color.FromArgb(255, 210, 120), 90f);
            g.FillEllipse(hi, 6, 5, 11, 11);

            // Aperture-style stroke.
            using var pen = new Pen(Color.FromArgb(180, 235, 235, 245), 2f);
            g.DrawPath(pen, path);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
