using System;
using System.Threading.Tasks;
using Lukit.Capture;

namespace Lukit.Imaging;

public enum ToneMapOperator
{
    /// <summary>Hard clip to [0,1] after normalization. Fastest; blows out true HDR highlights.</summary>
    Clip,

    /// <summary>Luminance-preserving extended Reinhard. Natural roll-off of highlights, SDR untouched.</summary>
    Reinhard,

    /// <summary>Narkowicz ACES filmic approximation. Punchier, more contrast.</summary>
    AcesFilmic,
}

public sealed class ToneMapSettings
{
    /// <summary>
    /// The luminance (in nits) that should map to SDR white (255). This is the single
    /// most important knob for "washed out" HDR screenshots: it should match the SDR
    /// content white level Windows is using for the display. Auto-detected from the
    /// monitor when possible; overridable by the user.
    /// </summary>
    public float SdrWhiteNits { get; set; } = 200f;

    public ToneMapOperator Operator { get; set; } = ToneMapOperator.Reinhard;
}

/// <summary>
/// Converts a linear scRGB <see cref="HdrFrame"/> to an 8-bit sRGB BGRA image.
///
/// scRGB shares the Rec.709 primaries with sRGB, so no gamut/primary conversion is
/// needed — only (1) normalization so SDR white lands at 1.0, (2) an optional
/// highlight roll-off for values still above 1.0, and (3) the linear→sRGB transfer.
/// Getting step (1) right is what fixes the classic "everything above ~1/3 clips to
/// white" washout seen when HDR content is captured without normalization.
/// </summary>
public static class ToneMapper
{
    public static byte[] ToBgra32(HdrFrame frame, ToneMapSettings settings, out int stride)
    {
        int width = frame.Width;
        int height = frame.Height;
        int rowStride = width * 4;
        stride = rowStride;
        var output = new byte[(long)height * rowStride];

        float refScale = MathF.Max(settings.SdrWhiteNits, 1f) / 80f;
        ToneMapOperator op = settings.Operator;
        float[] rgb = frame.Rgb;

        // Extended Reinhard needs the scene's peak luminance so it can map it to 1.0.
        float lWhiteSq = 1f;
        if (op == ToneMapOperator.Reinhard)
        {
            float peak = ComputePeakLuminance(frame, refScale);
            lWhiteSq = MathF.Max(peak * peak, 1e-4f);
        }

        Parallel.For(0, height, y =>
        {
            int si = y * width * 3;
            int di = y * rowStride;
            for (int x = 0; x < width; x++)
            {
                float r = MathF.Max(rgb[si] / refScale, 0f);
                float g = MathF.Max(rgb[si + 1] / refScale, 0f);
                float b = MathF.Max(rgb[si + 2] / refScale, 0f);
                si += 3;

                switch (op)
                {
                    case ToneMapOperator.Clip:
                        r = Clamp01(r); g = Clamp01(g); b = Clamp01(b);
                        break;

                    case ToneMapOperator.Reinhard:
                        float l = Luminance(r, g, b);
                        if (l > 1e-6f)
                        {
                            float ld = l * (1f + l / lWhiteSq) / (1f + l);
                            float scale = ld / l;
                            r *= scale; g *= scale; b *= scale;
                        }
                        r = Clamp01(r); g = Clamp01(g); b = Clamp01(b);
                        break;

                    case ToneMapOperator.AcesFilmic:
                        r = Aces(r); g = Aces(g); b = Aces(b);
                        break;
                }

                output[di] = LinearToSrgb8(b);
                output[di + 1] = LinearToSrgb8(g);
                output[di + 2] = LinearToSrgb8(r);
                output[di + 3] = 255;
                di += 4;
            }
        });

        return output;
    }

    private static float ComputePeakLuminance(HdrFrame frame, float refScale)
    {
        float[] rgb = frame.Rgb;
        int width = frame.Width;
        object gate = new();
        float peak = 1f;

        Parallel.For(0, frame.Height, () => 1f,
            (y, _, local) =>
            {
                int si = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    float r = MathF.Max(rgb[si] / refScale, 0f);
                    float g = MathF.Max(rgb[si + 1] / refScale, 0f);
                    float b = MathF.Max(rgb[si + 2] / refScale, 0f);
                    si += 3;
                    float l = Luminance(r, g, b);
                    if (l > local) local = l;
                }
                return local;
            },
            local => { lock (gate) { if (local > peak) peak = local; } });

        return peak;
    }

    private static float Luminance(float r, float g, float b)
        => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    private static float Aces(float x)
    {
        const float a = 2.51f, b = 0.03f, c = 2.43f, d = 0.59f, e = 0.14f;
        x = (x * (a * x + b)) / (x * (c * x + d) + e);
        return Clamp01(x);
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private static byte LinearToSrgb8(float c)
    {
        c = Clamp01(c);
        float s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        int v = (int)(s * 255f + 0.5f);
        return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
    }
}
