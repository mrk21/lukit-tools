using System;
using Lukit.Capture;
using Xunit;

namespace Lukit.Tests.Capture;

public class HdrFrameTests
{
    [Fact(DisplayName = "小さすぎるバッファでの生成は ArgumentException")]
    public void ThrowsWhenBufferTooSmall()
    {
        Assert.Throws<ArgumentException>(() => new HdrFrame(2, 2, new float[(2 * 2 * 3) - 1]));
    }

    [Fact(DisplayName = "Crop は指定矩形の画素だけを取り出す")]
    public void CropExtractsRequestedRegion()
    {
        // 2x2。各ピクセルの R に通し番号（0,1,2,3）を入れておく。
        var rgb = new float[2 * 2 * 3];
        for (int i = 0; i < 4; i++)
        {
            rgb[i * 3] = i;
        }
        var frame = new HdrFrame(2, 2, rgb);

        var cropped = frame.Crop(1, 1, 1, 1); // 右下 1px（通し番号3）

        Assert.Equal(1, cropped.Width);
        Assert.Equal(1, cropped.Height);
        Assert.Equal(3f, cropped.Rgb[0]);
    }

    [Fact(DisplayName = "画面外・過大な矩形は境界にクランプされる")]
    public void CropClampsToBounds()
    {
        var frame = new HdrFrame(4, 4, new float[4 * 4 * 3]);

        var cropped = frame.Crop(-10, -10, 100, 100);

        Assert.Equal(4, cropped.Width);
        Assert.Equal(4, cropped.Height);
    }
}
