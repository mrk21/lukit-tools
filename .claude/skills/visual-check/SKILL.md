---
name: visual-check
description: Lukit の GUI の「見た目」を PNG に落として、人／AI が目視で回帰確認するループを回す。対象は 2 種類 — A. 製品の出力（トーンマップ後のスクショ: `--shot-fullscreen` / `--shot-monitor` / `--shot-all` / `--frame-stats`）、B. アプリ自身の UI（設定画面・矩形選択オーバーレイを画面外レンダリングする `--shot-ui settings|overlay`）。`UI/`（`SettingsWindow`・`SelectionOverlay`・`TrayApp` 等）の見た目を足す/変える、`Imaging/` のトーンマップ結果の見た目を変える、といった変更のあとに「見た目が壊れてないか」を確かめたいときに使う。「見た目を確認して」「UI のスクショ撮って」「設定画面どう見える？」「オーバーレイの見た目チェック」「トーンマップの出力を見て」「ビジュアルチェック」「`--shot-ui` で撮って」「変更後の画面を見せて」等の依頼では必ず起動する。UI やトーンマップの見た目を変えた直後は、明示されなくても積極的に使って自分で撮って確認する。純粋ロジックのユニットテストは `/tdd`、README 同期は `/update-readme` の担当なのでそちらへ。ボタン押下→画面遷移のような操作を伴う e2e はこのスキルの範囲外（末尾の FlaUI 参照）。
---

# visual-check

GUI の見た目を PNG に落として、人／AI が目視で回帰確認するループを回す。ブラウザで Playwright の `page.screenshot()` を AI ワークフローに組み込むのと同じことを、.NET デスクトップ（WPF + WinForms）でやる。仕組み・設計の詳細は [docs/visual-check.md](../../../docs/visual-check.md)。

## 対象（A と B）

| | 対象 | 手段 | 性質 |
| --- | --- | --- | --- |
| **A** | 製品の出力（トーンマップ後のスクショ） | `--shot-fullscreen` / `--shot-monitor` / `--shot-all` / `--frame-stats` | ライブ画面・HDR 状態に依存する**動的**チェック |
| **B** | アプリ自身の UI（設定画面・矩形選択オーバーレイ） | `--shot-ui settings\|overlay` | 画面外レンダリングで**決定的**、環境非依存 |

B は [`UI/UiShot.cs`](../../../src/Lukit/UI/UiShot.cs) が `RenderTargetBitmap` でウィンドウを画面外（`Left/Top = -32000`, `ShowActivated = false`）に描いて PNG 化する。ドライバも、トレイ操作も、ホットキーも使わない。

## 手順

1. **撮る。** 原則ハーネスで一括:

   ```powershell
   pwsh tools/visual-check.ps1              # A + B を artifacts/visual/<timestamp>/ に出力し manifest.md を書く
   pwsh tools/visual-check.ps1 -Only ui     # 決定的な UI サーフェスのみ（UI をいじったとき用）
   pwsh tools/visual-check.ps1 -Only capture -Op aces   # ライブ出力のみ、演算子指定
   pwsh tools/visual-check.ps1 -NoBuild     # ビルドを飛ばして既存 Debug バイナリを使う
   ```

   単発で足りるなら個別に叩いてもよい（例: `src/Lukit/bin/Debug/<TFM>/Lukit.exe --shot-ui settings out.png`）。

2. **見る。** 出力フォルダ（`artifacts/visual/<timestamp>/`、git 無視）の各 PNG を **Read で開いて実際に目視する**。`manifest.md` に一覧と成否が載る。これがループの肝 — 「撮れた」で終わらせず、必ず絵を見て評価する。

3. **直して戻る。** 気になる点があればコードやパラメータを直し、1 に戻って撮り直す。UI 変更なら `-Only ui` で高速に回せる。

## 注意点（この環境固有）

- **常駐版と並行できる。** `--shot-*` / `--shot-ui` は CLI 分岐なので、GUI 起動（`Program.RunGui`）の単一インスタンス Mutex 取得まで到達しない。常駐版が動いていても撮れるし、`dotnet run` の再ビルドとも衝突しない（開発ループとの共存は CLAUDE.md／README 参照）。
- **`Lukit.exe` は WinExe なので stdout がコンソールに繋がらない。** `& $exe ...` で直接叩くと成功していても「OK …」は表示されない（ファイルは書けている）。ハーネスは各実行の標準出力をログへリダイレクトし、終了コードで成否を判定している。手で確認するなら PNG の有無を見る。
- **決定的なのは B だけ。** A はそのとき画面に映っているものと HDR 状態を反映する。回帰の基準にするなら B、いまの出力を診るなら A。
- **ビルドは一度でよい。** ハーネスが冒頭で `dotnet build` してから生成 exe を直接叩く（ショットごとに `dotnet run` すると遅く、exe ロックも再発する）。連続で回すなら `-NoBuild`。

## サーフェスを増やす

UI が育ったら、描画・保存は `UiShot` に集約されているので **`UiShot.Build` に 1 分岐足すだけ**で新しい画面を撮れる:

```csharp
private static Window Build(string surface) => surface.ToLowerInvariant() switch
{
    "settings" => new SettingsWindow(AppSettings.Load()),
    "overlay"  => BuildOverlay(),
    "mynewdialog" => new MyNewDialog(...),   // ← 足す
    _ => throw new ArgumentException(...),
};
```

ハーネスにも一括対象として名前を加えたいなら [`tools/visual-check.ps1`](../../../tools/visual-check.ps1) の UI サーフェス列（`'settings', 'overlay'`）に足す。

## このスキルの範囲外

`--shot-ui` は**静的な見た目**の確認。ボタン押下→画面遷移のような**操作を伴う e2e** が要るなら、UI Automation を使う [FlaUI](https://github.com/FlaUI/FlaUI)（`FlaUI.Core` + `FlaUI.UIA3`）を足す。実ウィンドウを起動して要素を検索・クリック・入力し `Capture.Element(window).ToFile(...)` で撮る。その際はトレイ経由で開くか、テスト用に画面を直接開く CLI フックを足すと安定する（トレイの UIA 操作は shell 別プロセスで不安定）。判断材料は [docs/visual-check.md](../../../docs/visual-check.md) の「拡張余地」。
