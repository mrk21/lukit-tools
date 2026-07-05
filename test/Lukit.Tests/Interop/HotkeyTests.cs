using Lukit.Interop;
using Xunit;

namespace Lukit.Tests.Interop;

// Hotkey.TryParse は "Ctrl+Alt+2" 形式の文字列を Win32 の修飾キー＋仮想キーへ変換する
// 純粋ロジック（P/Invoke に触れない）。設定文字列から実際のホットキー登録までの入口なので、
// 正常系・別名・大小文字・空白許容・不正入力を網羅する。
public class HotkeyTests
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [Fact(DisplayName = "\"Ctrl+Alt+1\" は Ctrl|Alt|NoRepeat と VK '1'(0x31) に解釈される")]
    public void ParsesModifiersAndDigit()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+1", out Hotkey hk));
        Assert.Equal(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, hk.Modifiers);
        Assert.Equal(0x31u, hk.VirtualKey);
    }

    [Theory(DisplayName = "修飾キー名は大小文字・別名(control)を問わず解釈できる")]
    [InlineData("ctrl+alt+2")]
    [InlineData("CTRL+ALT+2")]
    [InlineData("Control+Alt+2")]
    public void ModifierNamesAreCaseAndAliasInsensitive(string spec)
    {
        Assert.True(Hotkey.TryParse(spec, out Hotkey hk));
        Assert.Equal(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, hk.Modifiers);
        Assert.Equal(0x32u, hk.VirtualKey); // '2'
    }

    [Fact(DisplayName = "Win 別名と複数修飾キーを合成できる")]
    public void CombinesAllModifiers()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+Alt+Windows+5", out Hotkey hk));
        Assert.Equal(MOD_CONTROL | MOD_SHIFT | MOD_ALT | MOD_WIN | MOD_NOREPEAT, hk.Modifiers);
        Assert.Equal(0x35u, hk.VirtualKey);
    }

    [Theory(DisplayName = "修飾キーが無くても、常に MOD_NOREPEAT が付与される")]
    [InlineData("F5")]
    [InlineData("A")]
    public void AlwaysSetsNoRepeat(string spec)
    {
        Assert.True(Hotkey.TryParse(spec, out Hotkey hk));
        Assert.True((hk.Modifiers & MOD_NOREPEAT) != 0);
    }

    [Theory(DisplayName = "英字キーは大文字の VK に解釈される（A=0x41, Z=0x5A）")]
    [InlineData("a", 0x41u)]
    [InlineData("Z", 0x5Au)]
    public void ParsesLetters(string spec, uint vk)
    {
        Assert.True(Hotkey.TryParse(spec, out Hotkey hk));
        Assert.Equal(vk, hk.VirtualKey);
    }

    [Theory(DisplayName = "ファンクションキー F1..F24 は 0x70.. に解釈される")]
    [InlineData("F1", 0x70u)]
    [InlineData("F12", 0x7Bu)]
    [InlineData("F24", 0x87u)]
    public void ParsesFunctionKeys(string spec, uint vk)
    {
        Assert.True(Hotkey.TryParse(spec, out Hotkey hk));
        Assert.Equal(vk, hk.VirtualKey);
    }

    [Theory(DisplayName = "特殊キーと別名を解釈できる")]
    [InlineData("PrintScreen", 0x2Cu)]
    [InlineData("PrtSc", 0x2Cu)]
    [InlineData("Insert", 0x2Du)]
    [InlineData("Ins", 0x2Du)]
    [InlineData("Home", 0x24u)]
    [InlineData("End", 0x23u)]
    [InlineData("PageUp", 0x21u)]
    [InlineData("PgUp", 0x21u)]
    [InlineData("PageDown", 0x22u)]
    [InlineData("PgDn", 0x22u)]
    [InlineData("Space", 0x20u)]
    public void ParsesNamedKeys(string spec, uint vk)
    {
        Assert.True(Hotkey.TryParse(spec, out Hotkey hk));
        Assert.Equal(vk, hk.VirtualKey);
    }

    [Fact(DisplayName = "余分な空白と連続する + を許容する")]
    public void ToleratesWhitespaceAndEmptyTokens()
    {
        Assert.True(Hotkey.TryParse("  Ctrl +  Alt ++ 1 ", out Hotkey hk));
        Assert.Equal(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, hk.Modifiers);
        Assert.Equal(0x31u, hk.VirtualKey);
    }

    [Theory(DisplayName = "無効な指定は false を返し、hotkey は default のまま")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]   // 修飾キーのみでキーが無い
    [InlineData("Ctrl+Shift")]
    [InlineData("Foo")]        // 未知のキー
    [InlineData("Ctrl+Bar")]   // 未知のキー
    [InlineData("F0")]         // ファンクションキー範囲外
    [InlineData("F25")]        // ファンクションキー範囲外
    public void RejectsInvalidSpecs(string? spec)
    {
        Assert.False(Hotkey.TryParse(spec, out Hotkey hk));
        Assert.Equal(default(Hotkey), hk);
    }
}
