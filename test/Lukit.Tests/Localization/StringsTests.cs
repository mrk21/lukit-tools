using System.Globalization;
using Lukit.Localization;
using Xunit;

namespace Lukit.Tests.Localization;

// OS の UI 言語（CultureInfo）から対応言語を選ぶ純粋ロジックを検証する。
// 実際の文言表示や CurrentUICulture への依存は対象外（環境非依存の解決関数だけを見る）。
public class StringsTests
{
    [Theory(DisplayName = "日本語カルチャ（ja / ja-JP）は Japanese に解決する")]
    [InlineData("ja")]
    [InlineData("ja-JP")]
    public void JapaneseCultureResolvesToJapanese(string name)
        => Assert.Equal(AppLanguage.Japanese, Strings.FromCulture(new CultureInfo(name)));

    [Theory(DisplayName = "日本語以外のカルチャは English にフォールバックする")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("zh-CN")]
    public void NonJapaneseCultureFallsBackToEnglish(string name)
        => Assert.Equal(AppLanguage.English, Strings.FromCulture(new CultureInfo(name)));

    [Fact(DisplayName = "インバリアントカルチャは English にフォールバックする")]
    public void InvariantCultureFallsBackToEnglish()
        => Assert.Equal(AppLanguage.English, Strings.FromCulture(CultureInfo.InvariantCulture));

    [Theory(DisplayName = "Auto は OS カルチャに従う（ja→Japanese / それ以外→English）")]
    [InlineData("ja-JP", AppLanguage.Japanese)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("fr-FR", AppLanguage.English)]
    public void AutoFollowsOsCulture(string osCulture, AppLanguage expected)
        => Assert.Equal(expected, Strings.Resolve(LanguagePreference.Auto, new CultureInfo(osCulture)));

    [Theory(DisplayName = "明示指定は OS カルチャを上書きする")]
    [InlineData(LanguagePreference.English, "ja-JP", AppLanguage.English)]
    [InlineData(LanguagePreference.Japanese, "en-US", AppLanguage.Japanese)]
    public void ExplicitPreferenceOverridesOsCulture(LanguagePreference pref, string osCulture, AppLanguage expected)
        => Assert.Equal(expected, Strings.Resolve(pref, new CultureInfo(osCulture)));
}
