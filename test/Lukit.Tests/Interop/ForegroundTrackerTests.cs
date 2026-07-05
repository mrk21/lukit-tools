using Lukit.Interop;
using Xunit;

namespace Lukit.Tests.Interop;

// ForegroundTracker.ShouldTrack は「フォアグラウンドになったウィンドウを、トレイメニュー起点の
// ウィンドウキャプチャ対象として覚えておくか」を、ウィンドウクラス名と自プロセス判定だけで決める
// 純粋ロジック（WinEvent フック本体には触れない）。トレイをクリックすると前面がタスクバーや自前の
// メニューへ移るため、それらを除外して「直前の実アプリウィンドウ」だけを残すのが役割。
public class ForegroundTrackerTests
{
    [Theory(DisplayName = "通常のアプリウィンドウ（自プロセス外）は追跡対象になる")]
    [InlineData("Chrome_WidgetWin_1")]      // Chromium 系ブラウザ/アプリ
    [InlineData("CabinetWClass")]           // エクスプローラー
    [InlineData("Notepad")]
    [InlineData("ConsoleWindowClass")]      // コンソール
    [InlineData("ApplicationFrameWindow")]  // 一部の UWP アプリのホスト
    public void TracksNormalAppWindows(string className)
        => Assert.True(ForegroundTracker.ShouldTrack(className, isOwnProcess: false));

    [Theory(DisplayName = "シェルのサーフェス（タスクバー・デスクトップ等）は追跡対象にしない")]
    [InlineData("Shell_TrayWnd")]            // プライマリのタスクバー（トレイクリックで前面になる元凶）
    [InlineData("Shell_SecondaryTrayWnd")]   // セカンダリモニタのタスクバー
    [InlineData("Progman")]                  // デスクトップ（Program Manager）
    [InlineData("WorkerW")]                  // デスクトップの壁紙ホスト
    [InlineData("NotifyIconOverflowWindow")] // 「隠れているインジケーター」のポップアップ
    public void SkipsShellSurfaces(string className)
        => Assert.False(ForegroundTracker.ShouldTrack(className, isOwnProcess: false));

    [Fact(DisplayName = "自プロセスのウィンドウ（トレイメニュー・設定・オーバーレイ）は追跡対象にしない")]
    public void SkipsOwnProcessWindows()
        => Assert.False(ForegroundTracker.ShouldTrack("Chrome_WidgetWin_1", isOwnProcess: true));

    [Theory(DisplayName = "クラス名が null・空・空白のときは追跡対象にしない")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SkipsBlankClassName(string? className)
        => Assert.False(ForegroundTracker.ShouldTrack(className, isOwnProcess: false));
}
