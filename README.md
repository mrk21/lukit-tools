# Lukit Tools

HDR 環境でも**色が破綻しない**スクリーンショットを撮る、Windows 用の常駐ツール。
Snipping Tool の代替として、HDR 有効時の「白飛び・色あせ（ミルキー）」問題を解決します。

- 画面全体 / ウィンドウ / 矩形選択 のキャプチャ
- HDR 有効時も正しい明るさ・コントラスト・彩度で保存
- PNG 保存 ＋ クリップボードコピー
- トレイ常駐・グローバルホットキー

HDR で色が破綻する仕組みと、それを解消するトーンマップ処理の詳細は [HDR とトーンマッピングの仕組み](docs/hdr-tone-mapping.md) を参照。

## 技術スタック

- C# / .NET: `10`（TFM `net10.0-windows10.0.22621.0` / 動作下限 `10.0.19041`）
- WPF + WinForms
  - UI・トレイ常駐（`UseWPF` / `UseWindowsForms`）
- Vortice.Windows: `3.x`
  - Direct3D 11 / DXGI。D3D11 デバイスを扱う（[amerkoleci/Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)）
- CsWinRT
  - Windows.Graphics.Capture の projection（Windows TFM 同梱、明示的な依存追加は不要）
- .NET SDK (`dotnet`) + PowerShell
  - ビルド・実行
- xUnit v3: `3.x`
  - 単体テスト（`test/Lukit.Tests`）
- Inno Setup 6 + GitHub Actions
  - 配布用インストーラのビルドと、`v*` タグでのリリース自動化（`installer/` / `.github/workflows/release.yml`）

## セットアップ

- Windows 10 2004 (build 19041) 以降 / Windows 11。ウィンドウ枠なしキャプチャは Windows 11 で有効。
- 配布物（setup.exe / portable zip）は self-contained なので **.NET ランタイム不要**。ソースからビルドする場合のみ **.NET SDK 10** が必要。

### インストール

**配布物から入れる（推奨）** — [GitHub Releases](https://github.com/mrk21/lukit-tools/releases) から次のいずれかを取得します。

- `Lukit-Setup-<version>-x64.exe` — Inno Setup 製インストーラ。ユーザー単位・**管理者権限不要**で `%LOCALAPPDATA%\Programs\Lukit` に導入し、スタートアップ自動起動やデスクトップショートカットを選べます。
- `Lukit-portable-<version>-x64.zip` — 展開して `Lukit.exe` を起動するだけの持ち運び版。

**自分の環境でビルドして入れる** — リポジトリを clone し、簡易インストーラを実行します（.NET SDK 10 が必要）。

```powershell
# self-contained 単一 exe をビルドして %LOCALAPPDATA%\Programs\Lukit へ配置し、起動する
pwsh scripts/install.ps1

# あわせてログオン時の自動起動を登録する（HKCU Run・管理者権限不要）
pwsh scripts/install.ps1 -Startup
```

- `-NoLaunch` 配置後に起動しない ／ `-SkipBuild` 直近の publish 成果物（`artifacts\publish\Lukit.exe`）を使って配置だけ行う。
- アンインストールは `pwsh scripts/uninstall.ps1`（設定 `%APPDATA%\Lukit` は既定で保持、`-PurgeSettings` で削除）。
- self-contained 単一 exe は .NET ランタイムを同梱するため約 190MB になります（ランタイム不要と引き換えのサイズ）。

### ソースからビルドして動かす（開発）

```powershell
# .NET SDK 10 のインストール
winget install Microsoft.DotNet.SDK.10

# ビルドと起動
dotnet run --project src/Lukit/Lukit.csproj
```

## 使用方法

`Lukit.exe` を起動するとトレイに常駐します。トレイアイコンの右クリックメニュー、または既定のホットキー：

| 操作       | 既定のホットキー |
| ---------- | ---------------- |
| 画面全体   | `Ctrl+Alt+1`     |
| 矩形選択   | `Ctrl+Alt+2`     |
| ウィンドウ | `Ctrl+Alt+3`     |

- 撮影結果は既定で `ピクチャ\Lukit`（例：`D:\Users\<name>\ピクチャ\Lukit`、既定フォルダの場所に従う）に PNG 保存＋クリップボードにコピー。
- 全画面・矩形は **カーソルのあるディスプレイ** を対象にします。特定のディスプレイや全ディスプレイをまとめて撮るには、トレイメニューの **「Capture specific display」** から選択（各ディスプレイ個別 / All displays combined）。各モニタは自分の SDR 白色輝度で個別にトーンマップされるので、HDR/SDR 混在環境でも正しく合成されます。
- トレイメニューの **Settings…** で、表示言語（自動＝OS 設定／英語／日本語）、SDR 白色輝度（自動/手動）、トーンマップ演算子、保存先、出力方法、ホットキーを変更できます。変更は設定画面を閉じた時点で即反映（表示言語・ホットキーも再起動不要）。UI の既定言語は OS の表示言語（日本語環境なら日本語、それ以外は英語）です。

保存画像が暗い/明るい、ホットキーが効かない等は [トラブルシューティング](docs/hdr-tone-mapping.md#トラブルシューティング) を参照。

### CLI ユーティリティ

```powershell
Lukit.exe --display-info                 # 各モニタの HDR 状態と SDR 白色輝度を表示
Lukit.exe --frame-stats                  # プライマリを撮って scRGB 統計を表示（診断用）
Lukit.exe --shot-fullscreen out.png [--sdr-white <nits>] [--op clip|reinhard|aces]
Lukit.exe --shot-monitor <index> out.png # 特定ディスプレイ（順番は --display-info 参照）
Lukit.exe --shot-all out.png             # 全ディスプレイを合成
Lukit.exe --shot-window out.png [--hwnd <handle>]
Lukit.exe --shot-ui settings out.png     # 設定ウィンドウを画面外レンダリングして PNG 化
Lukit.exe --shot-ui overlay out.png      # 矩形選択オーバーレイを PNG 化
```

## 開発方法

### 開発コマンド

```powershell
# 開発中の起動（デバッグビルドでそのまま実行）
dotnet run --project src/Lukit/Lukit.csproj

# 再ビルドして実行（--no-build 不要／--display-info は CLI 実行の一例）
dotnet run --project src/Lukit/Lukit.csproj -- --display-info

# 変更を監視して自動リビルド＆再実行
dotnet watch --project src/Lukit/Lukit.csproj -- --display-info

# コンパイル確認（デバッグビルド）
dotnet build src/Lukit/Lukit.csproj

# テスト実行（リポジトリ直下。Lukit.slnx を自動で拾う）
dotnet test

# テストを絞り込んで実行
dotnet test --filter "FullyQualifiedName~ToneMapper"

# テストを監視して自動再実行
dotnet watch test --project test/Lukit.Tests/Lukit.Tests.csproj

# カバレッジ付きで実行（任意）
dotnet test --collect:"XPlat Code Coverage"

# ビジュアルチェック（A+B を撮って artifacts/visual/<timestamp>/ に manifest 出力）
pwsh scripts/visual-check.ps1
pwsh scripts/visual-check.ps1 -Only ui          # 決定的な UI サーフェスのみ（-Only all|ui|capture）
pwsh scripts/visual-check.ps1 -Op aces -NoBuild # 演算子を変えてビルド省略（-Op clip|reinhard|aces）

# self-contained 単一 exe をビルドして常駐用に配置（更新も同じ。詳細は「セットアップ」参照）
pwsh scripts/install.ps1

# 常駐版を削除（設定は保持、-PurgeSettings で設定も削除）
pwsh scripts/uninstall.ps1

# 配布物（setup.exe / portable zip）をローカルでビルド（要 Inno Setup 6。-InstallInno で自動導入）
pwsh scripts/build-installer.ps1 -Version 1.2.3 -Portable
```

### 見た目の確認（ビジュアルチェック）

GUI の「見た目」を PNG に落として、人／AI が目視で回帰確認できるようにしています。対象は 2 種類：

- **A. 製品の出力**：トーンマップ後のスクショそのもの（上の `--shot-*` / `--frame-stats`）。ライブの画面と HDR 状態に依存する動的チェック。
- **B. アプリ自身の UI**：設定画面・矩形選択オーバーレイを **画面外レンダリング**して PNG 化（`--shot-ui`）。トレイ／単一インスタンス Mutex／ホットキーに触れず、決定的。UI が増えても [UiShot](src/Lukit/UI/UiShot.cs) にサーフェスを 1 つ足すだけで回り続ける。

一括で撮ってマニフェストを出すハーネスが `scripts/visual-check.ps1`（起動コマンドと代表的なバリアントは上の **開発コマンド** を参照）。

出力 PNG はそのまま AI に渡して講評→修正のループに乗せられる（Claude Code は PNG を直接読める）。真の操作 e2e（ボタン押下→遷移）が必要になったら FlaUI 等の UIA ドライバを足す。詳細は [ビジュアルチェックの仕組み](docs/visual-check.md) を参照。

### 常駐版と開発ビルドを分離する

Lukit は GUI（トレイ常駐）と CLI を 1 バイナリに同居させているため、`bin\Debug` の `Lukit.exe` をそのまま常駐起動すると exe がロックされ、`dotnet run`（再ビルド）が失敗します。常駐用の実行ファイルを開発ビルドと別の場所に置き、握るファイルと書くファイルを分けることで衝突を避けます（仕組みの詳細は [常駐版と開発ビルドの分離](docs/dev-resident-build.md)）。

- **常駐版を用意する**：`scripts/install.ps1` で self-contained 実行ファイルをビルドし、`%LOCALAPPDATA%\Programs\Lukit\Lukit.exe` に配置してそこから常駐起動する（[セットアップ](#セットアップ) 参照）。
- **開発ループ**：`bin\Debug` のまま素の `dotnet run` / `dotnet watch` で回す（`--no-build` は不要）。ロックは exe ファイル単位なので、常駐版が動いていても再ビルドは通る。
- **常駐版を更新する**：`scripts/install.ps1` を再実行する。常駐プロセスを終了してから `%LOCALAPPDATA%\Programs\Lukit` を上書きしてくれる。
- **GUI を触るテスト時のみ**：常駐版を終了してから `dotnet run` する。単一インスタンス Mutex 実装済みのため、終了しないと二重起動は情報ダイアログで弾かれる（CLI ユーティリティは常駐版と並行して実行可）。
- **スタートアップ自動起動（任意）**：`scripts/install.ps1 -Startup` で HKCU Run キーに登録、`scripts/uninstall.ps1` で解除（管理者権限不要）。

### リリース

配布物は GitHub Actions（[.github/workflows/release.yml](.github/workflows/release.yml)）で発行する。`v*` タグを push すると、self-contained 単一 exe を publish し、portable zip（`Lukit-portable-<version>-x64.zip`）と Inno Setup 製 setup.exe（`Lukit-Setup-<version>-x64.exe`）を作って GitHub Release に添付する。

```powershell
# 例：v1.2.3 を発行する
git tag v1.2.3
git push origin v1.2.3
```

- GitHub の Actions から release ワークフローを手動起動（workflow_dispatch）し、バージョン（例 `1.2.3`）を渡してもよい（`v<version>` タグを作って発行する）。
- 同じ成果物はローカルでも作れる（ワークフローもこのスクリプトを呼ぶ）。`scripts/build-installer.ps1`（要 Inno Setup 6。未導入なら `-InstallInno` で winget 導入）で publish → setup.exe を `dist\` に出力する。コマンドは上の **開発コマンド** を参照。

### ディレクトリ構成

```
src/Lukit/
  Program.cs                 エントリ（GUI / CLI 分岐）
  Capture/
    CaptureEngine.cs         D3D11 デバイス + WGC で FP16 フレームを取得
    HdrFrame.cs              linear scRGB フレーム（float[]）
    CaptureController.cs     取得→トーンマップ→保存/クリップボードのオーケストレーション
  Imaging/
    ToneMapper.cs            scRGB → sRGB トーンマップ
    ImageOutput.cs           PNG エンコード / クリップボード
  Display/
    DisplayInfo.cs           DisplayConfig で HDR 状態・SDR 白色輝度を取得
    Monitors.cs              モニタ/ウィンドウの解決
  Interop/
    CaptureInterop.cs        HMONITOR/HWND → GraphicsCaptureItem（CsWinRT）
    Direct3DInterop.cs       Vortice ↔ WinRT D3D 相互運用
    HotkeyManager.cs         グローバルホットキー
  Settings/
    AppSettings.cs           設定（保存先・演算子・ホットキー等）を JSON で永続化
  Localization/
    Strings.cs               GUI 文言の日英カタログ＋OS UI 言語からの言語判定
  UI/
    TrayApp.cs               トレイ常駐・メニュー・配線
    SelectionOverlay.cs      矩形選択オーバーレイ
    SettingsWindow.cs        設定画面
    TrayIconFactory.cs       トレイアイコンを実行時に生成
test/Lukit.Tests/            src/Lukit/ の構成をミラーした xUnit v3 テスト
  Capture/
    HdrFrameTests.cs         HdrFrame の単体テスト
  Imaging/
    ToneMapperTests.cs       ToneMapper の単体テスト
scripts/
  install.ps1                自分用の簡易インストーラ（self-contained exe → %LOCALAPPDATA%）
  uninstall.ps1              install.ps1 で入れた常駐版の削除
  build-installer.ps1        配布物（setup.exe / portable zip）をローカルでビルド
  visual-check.ps1           ビジュアルチェックのハーネス
installer/
  Lukit.iss                  配布用 Inno Setup インストーラ定義（ユーザー単位・管理者権限不要）
.github/workflows/
  ci.yml                     main への push / PR で dotnet test を実行
  release.yml                v* タグで publish → portable zip / setup.exe → GitHub Release
```

## 詳細・ドキュメント

- [HDR とトーンマッピングの仕組み](docs/hdr-tone-mapping.md) — なぜ HDR で白飛びするのか、FP16 取り込み＋トーンマップによる解決方法、トーンマップ演算子、トラブルシューティング
- [常駐版と開発ビルドの分離](docs/dev-resident-build.md) — GUI+CLI 同居バイナリの exe ロック問題と、常駐版を別パスに置いて開発ループと共存させる仕組み
- [ビジュアルチェックの仕組み](docs/visual-check.md) — UI を画面外レンダリングで PNG 化する仕組み、AI ループへの組み込み、サーフェスの増やし方、FlaUI への拡張余地
- [設計メモ](docs/design.md) — 初期の要件・設計草案
