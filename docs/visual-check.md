# ビジュアルチェックの仕組み

GUI の「見た目」を PNG に落として、人／AI が目視で回帰確認できるようにする仕組み。
ブラウザで Playwright の `page.screenshot()` を AI ワークフローに組み込むのと同じことを、
.NET デスクトップ（WPF + WinForms）で実現する。

## 2 種類の対象

| | 対象 | 手段 | 性質 |
| --- | --- | --- | --- |
| **A** | 製品の出力（トーンマップ後のスクショ） | `--shot-fullscreen` / `--shot-monitor` / `--shot-all` / `--frame-stats` | ライブの画面と HDR 状態に依存する**動的**チェック |
| **B** | アプリ自身の UI（設定画面・矩形選択オーバーレイ） | `--shot-ui <surface> <out.png>` | 画面外レンダリングで**決定的**、環境非依存 |

A はこのアプリの本質（HDR→SDR トーンマップの正しさ）そのものなので、追加ツールなしで
既に AI ループに乗る。B が今回足した部分。

## B：UI の画面外レンダリング

[`src/Lukit/UI/UiShot.cs`](../src/Lukit/UI/UiShot.cs) が WPF の `RenderTargetBitmap` で
ウィンドウを**画面外**（`Left/Top = -32000`, `ShowActivated = false`）にレンダリングして
PNG 化する。ドライバ（FlaUI / WinAppDriver）も、トレイ操作も、ホットキーも使わない。

設計上の狙い：

- **トレイ常駐／単一インスタンス Mutex を回避**：`--shot-ui` は CLI 分岐なので、GUI 起動
  （[`Program.RunGui`](../src/Lukit/Program.cs)）の Mutex 取得まで到達しない。常駐版が動いて
  いても並行して撮れる。
- **画面を汚さない**：ユーザーのモニタにフラッシュせず、フォーカスも奪わない。
- **決定的**：DPI 96 固定・入力なしなので、同じ UI なら同じ PNG。
- **UI が育っても不変**：描画・保存は `UiShot` に集約。サーフェスを増やすときは
  `Build(surface)` に 1 分岐足すだけ。

### レンダリングの要点

- 画面外に `Show()` してから dispatcher を手動でポンプ（`DispatcherFrame`）し、
  レイアウトと `Loaded` ハンドラを走らせてから撮る。`app.Run()` はしない。
- コンテンツルート（`window.Content`）を、その `Margin` ぶん平行移動して描く。ウィンドウ
  背景を先に塗ってからコンテンツを重ねる。これをしないと標準の透明背景が抜け、
  `SizeToContent` の最終行（設定画面の Save/Cancel ボタン等）が欠ける。
- オーバーレイは実際のスクショの代わりに決定的なプレースホルダ画像を渡し、選択枠なしの
  ベースライン状態（フリーズ画像＋ディミング）を撮る。

### サーフェスを増やす

`UiShot.Build` に分岐を 1 つ足すだけ：

```csharp
private static Window Build(string surface) => surface.ToLowerInvariant() switch
{
    "settings" => new SettingsWindow(AppSettings.Load()),
    "overlay"  => BuildOverlay(),
    "mynewdialog" => new MyNewDialog(...),   // ← 足す
    _ => throw new ArgumentException(...),
};
```

## ループ用ハーネス

[`tools/visual-check.ps1`](../tools/visual-check.ps1) が A + B を 1 つのタイムスタンプ付き
フォルダ（`artifacts/visual/<timestamp>/`、git 無視）にまとめて撮り、`manifest.md` を出す。

```powershell
pwsh tools/visual-check.ps1              # A + B すべて
pwsh tools/visual-check.ps1 -Only ui     # 決定的な UI サーフェスのみ
pwsh tools/visual-check.ps1 -Only capture -Op aces
pwsh tools/visual-check.ps1 -NoBuild     # ビルドを飛ばして既存 Debug バイナリを使う
```

一度だけ `dotnet build` してから生成 exe を直接叩く（ショットごとに `dotnet run` すると
遅く、exe ロックも再発するため）。Lukit は WinExe なので stdout はコンソールに繋がらない。
各実行の標準出力はログファイルへリダイレクトし、終了コードで成否を判定している。

### AI ループへの組み込み

Claude Code は PNG を直接 Read できるので、次のループが回る：

1. `pwsh tools/visual-check.ps1` を実行
2. `artifacts/visual/<timestamp>/*.png` を Read して目視評価
3. コード／パラメータを修正して 1 に戻る

## 拡張余地：真の操作 e2e

`--shot-ui` は**静的な見た目**の確認。ボタン押下→画面遷移のような**操作を伴う e2e**が必要に
なったら、Windows のアクセシビリティ基盤 UI Automation を使う [FlaUI](https://github.com/FlaUI/FlaUI)
（`FlaUI.Core` + `FlaUI.UIA3`）を足す。実ウィンドウを起動して要素を検索・クリック・入力し、
`Capture.Element(window).ToFile(...)` で撮れる。その場合はトレイ経由でウィンドウを開くか、
テスト用に設定画面を直接開く CLI フックを足すと安定する（トレイの UIA 操作は shell 別プロセスで
不安定なため）。
