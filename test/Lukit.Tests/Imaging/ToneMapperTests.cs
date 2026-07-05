using Lukit.Capture;
using Lukit.Imaging;
using Xunit;

namespace Lukit.Tests.Imaging;

public class ToneMapperTests
{
    // 単色フレームを作るヘルパ。scRGB 値をそのまま全ピクセルに敷き詰める。
    private static HdrFrame SolidFrame(int width, int height, float r, float g, float b)
    {
        var rgb = new float[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            rgb[i * 3] = r;
            rgb[i * 3 + 1] = g;
            rgb[i * 3 + 2] = b;
        }
        return new HdrFrame(width, height, rgb);
    }

    private static ToneMapSettings Clip(float sdrWhiteNits = 80f)
        => new() { SdrWhiteNits = sdrWhiteNits, Operator = ToneMapOperator.Clip };

    [Fact(DisplayName = "出力は BGRA32・長さ width*height*4・stride は width*4")]
    public void ProducesBgra32BufferWithExpectedStride()
    {
        var frame = SolidFrame(2, 2, 0.5f, 0.5f, 0.5f);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out int stride);

        Assert.Equal(2 * 4, stride);
        Assert.Equal(2 * 2 * 4, bgra.Length);
    }

    [Fact(DisplayName = "SDR 白（80nit 基準の 1.0）は Clip で 255,255,255,255 になる")]
    public void SdrWhiteClipsToOpaqueWhite()
    {
        var frame = SolidFrame(1, 1, 1f, 1f, 1f);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out _);

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, bgra);
    }

    [Fact(DisplayName = "黒（0）は 0,0,0,255 になり、アルファは常に不透明")]
    public void BlackMapsToOpaqueZero()
    {
        var frame = SolidFrame(1, 1, 0f, 0f, 0f);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out _);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, bgra);
    }

    [Fact(DisplayName = "1.0 を超える白飛びは Clip で 255 に丸められる")]
    public void OverRangeHighlightsClampToWhite()
    {
        var frame = SolidFrame(1, 1, 5f, 5f, 5f);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out _);

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, bgra);
    }

    [Fact(DisplayName = "負の scRGB（sRGB 色域外）は 0 にクランプされる")]
    public void NegativeChannelsClampToZero()
    {
        var frame = SolidFrame(1, 1, -0.5f, -0.5f, -0.5f);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out _);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, bgra);
    }

    [Fact(DisplayName = "チャンネル順は BGRA（純赤入力で index0=B=0, index2=R=255）")]
    public void ChannelOrderIsBgra()
    {
        var frame = SolidFrame(1, 1, 1f, 0f, 0f); // 純赤

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out _);

        Assert.Equal(0, bgra[0]);   // B
        Assert.Equal(0, bgra[1]);   // G
        Assert.Equal(255, bgra[2]); // R
        Assert.Equal(255, bgra[3]); // A
    }

    [Fact(DisplayName = "sRGB 伝達関数で中間調（linear 0.5）は約188へ持ち上がる（線形の127ではない）")]
    public void MidToneUsesSrgbTransfer()
    {
        var frame = SolidFrame(1, 1, 0.5f, 0.5f, 0.5f);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(), out _);

        Assert.InRange(bgra[0], 185, 191);
    }

    [Theory(DisplayName = "SdrWhiteNits を上げると、より高い scRGB 値が SDR 白（255）に対応する")]
    [InlineData(80f, 1f)]
    [InlineData(160f, 2f)]
    [InlineData(240f, 3f)]
    public void SdrWhiteNitsNormalizesReferenceWhite(float sdrWhiteNits, float scRgbForWhite)
    {
        var frame = SolidFrame(1, 1, scRgbForWhite, scRgbForWhite, scRgbForWhite);

        byte[] bgra = ToneMapper.ToBgra32(frame, Clip(sdrWhiteNits), out _);

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, bgra);
    }
}
