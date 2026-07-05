using System;
using Lukit.Display;
using Xunit;

namespace Lukit.Tests.Display;

// 全モニタ合成（バウンディングボックス計算・原点補正・配置・隙間の黒埋め・矩形へのクランプ）を
// 環境非依存の整数ジオメトリとして検証する。実 GPU/デスクトップは不要。
public class DesktopCompositeTests
{
    private static Monitors.RECT Rect(int left, int top, int right, int bottom)
        => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    // width×height の単色 BGRA タイル（B チャンネルに識別値、A=255）。
    private static MonitorTile Tile(Monitors.RECT bounds, int width, int height, byte b)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            bgra[i * 4] = b;       // B
            bgra[i * 4 + 3] = 255; // A
        }
        return new MonitorTile(bounds, bgra, width, height);
    }

    private static byte B(ComposedImage img, int x, int y) => img.Bgra[y * img.Stride + x * 4];
    private static byte A(ComposedImage img, int x, int y) => img.Bgra[y * img.Stride + x * 4 + 3];

    [Fact(DisplayName = "隣接する2モニタは横に連結され、各モニタの画素が対応位置に配置される")]
    public void PlacesAdjacentMonitorsSideBySide()
    {
        var img = DesktopComposite.Compose(new[]
        {
            Tile(Rect(0, 0, 2, 1), 2, 1, b: 10),
            Tile(Rect(2, 0, 4, 1), 2, 1, b: 20),
        });

        Assert.Equal(4, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(16, img.Stride);
        Assert.Equal(10, B(img, 0, 0));
        Assert.Equal(10, B(img, 1, 0));
        Assert.Equal(20, B(img, 2, 0));
        Assert.Equal(20, B(img, 3, 0));
    }

    [Fact(DisplayName = "プライマリの左（負の仮想座標）にあるモニタも原点補正され正しく配置される")]
    public void HandlesMonitorsAtNegativeVirtualCoordinates()
    {
        var img = DesktopComposite.Compose(new[]
        {
            Tile(Rect(0, 0, 2, 1), 2, 1, b: 20),  // プライマリ
            Tile(Rect(-2, 0, 0, 1), 2, 1, b: 10), // 左のモニタ（負の X）
        });

        Assert.Equal(4, img.Width);
        Assert.Equal(10, B(img, 0, 0)); // 左モニタが原点へ寄る
        Assert.Equal(10, B(img, 1, 0));
        Assert.Equal(20, B(img, 2, 0));
        Assert.Equal(20, B(img, 3, 0));
    }

    [Fact(DisplayName = "縦にずれたモニタは行方向へ配置され、stride で行が正しく分かれる")]
    public void PlacesMonitorsVertically()
    {
        var img = DesktopComposite.Compose(new[]
        {
            Tile(Rect(0, 0, 1, 1), 1, 1, b: 10), // 上
            Tile(Rect(0, 1, 1, 2), 1, 1, b: 20), // 下
        });

        Assert.Equal(1, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(10, B(img, 0, 0));
        Assert.Equal(20, B(img, 0, 1));
    }

    [Fact(DisplayName = "モニタ間の隙間は不透明の黒（BGR=0, A=255）で埋められる")]
    public void FillsGapsWithOpaqueBlack()
    {
        var img = DesktopComposite.Compose(new[]
        {
            Tile(Rect(0, 0, 2, 1), 2, 1, b: 10),
            Tile(Rect(3, 0, 5, 1), 2, 1, b: 20), // 1px の隙間を空けて配置
        });

        Assert.Equal(5, img.Width);
        Assert.Equal(0, B(img, 2, 0));   // 隙間: 黒
        Assert.Equal(255, A(img, 2, 0)); // 隙間: 不透明
    }

    [Fact(DisplayName = "モニタ1枚なら、その画像とサイズがそのまま返る")]
    public void SingleMonitorReturnsItsOwnImage()
    {
        var img = DesktopComposite.Compose(new[]
        {
            Tile(Rect(0, 0, 3, 2), 3, 2, b: 42),
        });

        Assert.Equal(3, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(12, img.Stride);
        Assert.Equal(3 * 2 * 4, img.Bgra.Length);
        Assert.Equal(42, B(img, 2, 1));
    }

    [Fact(DisplayName = "モニタ矩形より大きい画像はキャンバス（矩形）にクランプされ、溢れない")]
    public void ClampsOversizedTileToItsRectangle()
    {
        // 矩形は 2×2 だが実画像は 3×3。左上の 2×2 だけがコピーされる。
        var bgra = new byte[3 * 3 * 4];
        for (int i = 0; i < 3 * 3; i++)
        {
            bgra[i * 4] = (byte)((i + 1) * 10); // B = 10,20,...,90（3px 幅で行優先）
            bgra[i * 4 + 3] = 255;
        }
        var tile = new MonitorTile(Rect(0, 0, 2, 2), bgra, 3, 3);

        var img = DesktopComposite.Compose(new[] { tile });

        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(2 * 2 * 4, img.Bgra.Length);
        Assert.Equal(10, B(img, 0, 0)); // src(row0,col0)
        Assert.Equal(20, B(img, 1, 0)); // src(row0,col1)
        Assert.Equal(40, B(img, 0, 1)); // src(row1,col0) = src index 3
        Assert.Equal(50, B(img, 1, 1)); // src(row1,col1)
    }

    [Fact(DisplayName = "タイルが空なら ArgumentException")]
    public void ThrowsWhenNoTiles()
    {
        Assert.Throws<ArgumentException>(() => DesktopComposite.Compose(Array.Empty<MonitorTile>()));
    }
}
