using Lukit.Imaging;
using Lukit.Settings;
using Xunit;

namespace Lukit.Tests.Settings;

// 設定の「トーンマップ設定への変換」と「保存先の解決」という環境非依存の純粋ロジックを検証する。
// ファイル I/O を伴う Load/Save（%APPDATA% の固定パスに触れる）は対象外。
public class AppSettingsTests
{
    [Fact(DisplayName = "AutoSdrWhite=true のとき EffectiveSdrWhite は検出値をそのまま返す")]
    public void EffectiveSdrWhiteUsesDetectedWhenAuto()
    {
        var s = new AppSettings { AutoSdrWhite = true, ManualSdrWhiteNits = 200f };
        Assert.Equal(320f, s.EffectiveSdrWhite(320f));
    }

    [Fact(DisplayName = "AutoSdrWhite=false のとき EffectiveSdrWhite は手動値を返す（検出値を無視）")]
    public void EffectiveSdrWhiteUsesManualWhenNotAuto()
    {
        var s = new AppSettings { AutoSdrWhite = false, ManualSdrWhiteNits = 240f };
        Assert.Equal(240f, s.EffectiveSdrWhite(320f));
    }

    [Fact(DisplayName = "ToToneMapSettings は Operator を引き継ぎ、手動 SDR 白を反映する")]
    public void ToToneMapSettingsCarriesOperatorAndManualWhite()
    {
        var s = new AppSettings
        {
            AutoSdrWhite = false,
            ManualSdrWhiteNits = 150f,
            Operator = ToneMapOperator.AcesFilmic,
        };

        ToneMapSettings ts = s.ToToneMapSettings(detectedSdrWhite: 999f);

        Assert.Equal(ToneMapOperator.AcesFilmic, ts.Operator);
        Assert.Equal(150f, ts.SdrWhiteNits); // Auto=false なので検出値 999 は無視される
    }

    [Fact(DisplayName = "ToToneMapSettings は Auto のとき検出値を SDR 白に使う")]
    public void ToToneMapSettingsUsesDetectedWhenAuto()
    {
        var s = new AppSettings { AutoSdrWhite = true, Operator = ToneMapOperator.Reinhard };

        ToneMapSettings ts = s.ToToneMapSettings(280f);

        Assert.Equal(280f, ts.SdrWhiteNits);
        Assert.Equal(ToneMapOperator.Reinhard, ts.Operator);
    }

    [Fact(DisplayName = "SaveFolder を指定するとその値が ResolvedSaveFolder になる")]
    public void ResolvedSaveFolderReturnsExplicitFolder()
    {
        var s = new AppSettings { SaveFolder = @"D:\Shots" };
        Assert.Equal(@"D:\Shots", s.ResolvedSaveFolder);
    }

    [Theory(DisplayName = "SaveFolder が空/空白なら既定の Pictures\\Lukit にフォールバックする")]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvedSaveFolderFallsBackToPicturesLukit(string folder)
    {
        var s = new AppSettings { SaveFolder = folder };
        Assert.EndsWith("Lukit", s.ResolvedSaveFolder);
    }
}
