using Lukit.Capture;
using Lukit.Imaging;
using Xunit;

namespace Lukit.Tests.Imaging;

// Clip 演算子と出力フォーマットの基本は ToneMapperTests が担当。
// ここは Reinhard / AcesFilmic 固有の「ハイライトのロールオフ」を検証する。
// 期待値は演算子の式から手計算で導出しており（コメント参照）、実装出力に合わせたものではない。
public class ToneMapperOperatorTests
{
    private static HdrFrame SolidFrame(float r, float g, float b)
        => new(1, 1, new[] { r, g, b });

    // 横 N ピクセルのフレーム。各ピクセルにグレー値（R=G=B）を敷く。
    private static HdrFrame GrayRow(params float[] grays)
    {
        var rgb = new float[grays.Length * 3];
        for (int i = 0; i < grays.Length; i++)
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = grays[i];
        return new HdrFrame(grays.Length, 1, rgb);
    }

    private static ToneMapSettings Op(ToneMapOperator op, float sdrWhiteNits = 80f)
        => new() { SdrWhiteNits = sdrWhiteNits, Operator = op };

    // グレーピクセルなので B=G=R。index 番目のピクセルの B チャンネルを返す。
    private static byte Gray(byte[] bgra, int pixelIndex) => bgra[pixelIndex * 4];

    // --- Reinhard（拡張 Reinhard：シーンのピーク輝度を 1.0 へ写す） ---

    [Fact(DisplayName = "Reinhard: フレーム内の最大輝度ピクセルは白(255)へマップされる")]
    public void ReinhardMapsScenePeakToWhite()
    {
        // 2px: 暗(2.0) と ピーク(8.0)。80nit 基準なので両方 SDR 白(1.0)超。
        byte[] bgra = ToneMapper.ToBgra32(GrayRow(2f, 8f), Op(ToneMapOperator.Reinhard), out _);

        Assert.Equal(255, Gray(bgra, 1)); // ピーク → 白
    }

    [Fact(DisplayName = "Reinhard: ピーク未満のハイライトは白へ潰れず、Clip より暗くロールオフされる")]
    public void ReinhardRollsOffHighlightsBelowPeak()
    {
        // ピーク=8 のとき L=2 の画素: ld = 2(1+2/64)/(1+2) = 0.6875 → sRGB ≈ 216。
        // Clip なら 2.0 は 1.0 に飽和して 255。
        var frame = GrayRow(2f, 8f);

        byte[] reinhard = ToneMapper.ToBgra32(frame, Op(ToneMapOperator.Reinhard), out _);
        byte[] clip = ToneMapper.ToBgra32(frame, Op(ToneMapOperator.Clip), out _);

        Assert.Equal(255, Gray(clip, 0));            // Clip は 2.0 を白へ飽和
        Assert.True(Gray(reinhard, 0) < 255);        // Reinhard はロールオフ（潰さない）
        Assert.InRange(Gray(reinhard, 0), 205, 225); // 導出値 ~216
    }

    [Fact(DisplayName = "Reinhard: フレームのピークが SDR 白以下なら SDR 域は素通し（Clip と一致）")]
    public void ReinhardLeavesInRangeContentUntouched()
    {
        // ピーク=1.0(SDR 白)なら lWhite=1 で ld=L となり恒等写像。0.5 は Clip と同じ ~188。
        var frame = GrayRow(0.5f, 1.0f);

        byte[] reinhard = ToneMapper.ToBgra32(frame, Op(ToneMapOperator.Reinhard), out _);
        byte[] clip = ToneMapper.ToBgra32(frame, Op(ToneMapOperator.Clip), out _);

        Assert.Equal(255, Gray(reinhard, 1));
        Assert.InRange(Gray(reinhard, 0), 185, 191);
        Assert.Equal(Gray(clip, 0), Gray(reinhard, 0)); // SDR 域では両者一致
    }

    [Fact(DisplayName = "Reinhard: 黒(0)は 0 のまま")]
    public void ReinhardKeepsBlack()
    {
        byte[] bgra = ToneMapper.ToBgra32(GrayRow(0f, 4f), Op(ToneMapOperator.Reinhard), out _);
        Assert.Equal(0, Gray(bgra, 0));
    }

    // --- ACES Filmic（Narkowicz 近似：チャンネル毎、ピーク非依存） ---

    [Fact(DisplayName = "ACES: SDR 白(1.0)はフィルミックにロールオフされ 255 未満(~232)になる")]
    public void AcesRollsOffSdrWhiteBelow255()
    {
        // ACES(1.0) = (2.51+0.03)/((2.43+0.59)+0.14) = 2.54/3.16 ≈ 0.804 → sRGB ≈ 232。
        byte[] bgra = ToneMapper.ToBgra32(SolidFrame(1f, 1f, 1f), Op(ToneMapOperator.AcesFilmic), out _);
        Assert.InRange(bgra[0], 228, 236);
    }

    [Fact(DisplayName = "ACES: 十分明るいハイライトは白(255)へ漸近する")]
    public void AcesSaturatesBrightHighlightsToWhite()
    {
        byte[] bgra = ToneMapper.ToBgra32(SolidFrame(100f, 100f, 100f), Op(ToneMapOperator.AcesFilmic), out _);
        Assert.Equal(255, bgra[0]);
    }

    [Fact(DisplayName = "ACES: 黒(0)は 0、チャンネル順は BGRA（純赤 → R のみ点灯）")]
    public void AcesBlackAndChannelOrder()
    {
        byte[] bgra = ToneMapper.ToBgra32(SolidFrame(1f, 0f, 0f), Op(ToneMapOperator.AcesFilmic), out _);
        Assert.Equal(0, bgra[0]);           // B
        Assert.Equal(0, bgra[1]);           // G
        Assert.InRange(bgra[2], 228, 236);  // R = ACES(1.0)
        Assert.Equal(255, bgra[3]);         // A
    }

    [Fact(DisplayName = "ACES: 出力は入力に対して単調増加する（0.25 < 0.5 < 1.0）")]
    public void AcesIsMonotonic()
    {
        byte dark = ToneMapper.ToBgra32(SolidFrame(0.25f, 0.25f, 0.25f), Op(ToneMapOperator.AcesFilmic), out _)[0];
        byte mid = ToneMapper.ToBgra32(SolidFrame(0.5f, 0.5f, 0.5f), Op(ToneMapOperator.AcesFilmic), out _)[0];
        byte bright = ToneMapper.ToBgra32(SolidFrame(1f, 1f, 1f), Op(ToneMapOperator.AcesFilmic), out _)[0];

        Assert.True(dark < mid);
        Assert.True(mid < bright);
    }
}
