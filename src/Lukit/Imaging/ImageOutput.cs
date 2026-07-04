using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lukit.Imaging;

/// <summary>
/// Turns a tone-mapped BGRA buffer into a frozen <see cref="BitmapSource"/> and
/// writes it to a PNG file and/or the clipboard. Clipboard access requires an STA
/// thread.
/// </summary>
public static class ImageOutput
{
    public static BitmapSource CreateBitmap(byte[] bgra, int width, int height, int stride)
    {
        BitmapSource bmp = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, palette: null, bgra, stride);
        bmp.Freeze();
        return bmp;
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }

    public static void CopyToClipboard(BitmapSource bmp)
    {
        Clipboard.SetImage(bmp);
    }
}
