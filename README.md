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

## セットアップ

- Windows 10 2004 (build 19041) 以降 / Windows 11。ウィンドウ枠なしキャプチャは Windows 11 で有効。
- self-contained ビルドなら **.NET ランタイム不要**。ソースからビルドする場合は **.NET SDK 10** が必要。

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
- トレイメニューの **Settings…** で、SDR 白色輝度（自動/手動）、トーンマップ演算子、保存先、出力方法、ホットキーを変更できます（ホットキー変更は再起動で反映）。

保存画像が暗い/明るい、ホットキーが効かない等は [トラブルシューティング](docs/hdr-tone-mapping.md#トラブルシューティング) を参照。

### CLI ユーティリティ

```powershell
Lukit.exe --display-info                 # 各モニタの HDR 状態と SDR 白色輝度を表示
Lukit.exe --frame-stats                  # プライマリを撮って scRGB 統計を表示（診断用）
Lukit.exe --shot-fullscreen out.png [--sdr-white <nits>] [--op clip|reinhard|aces]
Lukit.exe --shot-monitor <index> out.png # 特定ディスプレイ（順番は --display-info 参照）
Lukit.exe --shot-all out.png             # 全ディスプレイを合成
Lukit.exe --shot-window out.png [--hwnd <handle>]
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

# フレームワーク依存（.NET 10 ランタイムが必要）
dotnet build src/Lukit/Lukit.csproj -c Release

# 単体で動く self-contained 実行ファイル（ランタイム不要）
dotnet publish src/Lukit/Lukit.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true

# スタートアップ登録（ログオン時に常駐版を自動起動・管理者権限不要）
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
  -Name Lukit -Value "`"$env:LOCALAPPDATA\Programs\Lukit\Lukit.exe`""

# スタートアップ登録の解除
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Lukit
```

### 常駐版と開発ビルドを分離する

Lukit は GUI（トレイ常駐）と CLI を 1 バイナリに同居させているため、`bin\Debug` の `Lukit.exe` をそのまま常駐起動すると exe がロックされ、`dotnet run`（再ビルド）が失敗します。常駐用の実行ファイルを開発ビルドと別の場所に置き、握るファイルと書くファイルを分けることで衝突を避けます（仕組みの詳細は [常駐版と開発ビルドの分離](docs/dev-resident-build.md)）。

- **常駐版を用意する**：上の **開発コマンド** の `dotnet publish` で作った self-contained 実行ファイルを `%LOCALAPPDATA%\Programs\Lukit\Lukit.exe` に配置し、そこから常駐起動する。
- **開発ループ**：`bin\Debug` のまま素の `dotnet run` / `dotnet watch` で回す（`--no-build` は不要）。ロックは exe ファイル単位なので、常駐版が動いていても再ビルドは通る。
- **常駐版を更新する**：再 publish → `%LOCALAPPDATA%\Programs\Lukit` へ上書きコピー → プロセスを入れ替える。
- **GUI を触るテスト時のみ**：常駐版を終了してから `dotnet run` する。単一インスタンス Mutex 実装済みのため、終了しないと二重起動は情報ダイアログで弾かれる（CLI ユーティリティは常駐版と並行して実行可）。
- **スタートアップ自動起動（任意）**：ログオン時に常駐版を起動したい場合は HKCU Run キーに登録する。登録/解除コマンドは上の **開発コマンド** を参照（管理者権限不要）。

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
  UI/
    TrayApp.cs               トレイ常駐・メニュー・配線
    SelectionOverlay.cs      矩形選択オーバーレイ
    SettingsWindow.cs        設定画面
    TrayIconFactory.cs       トレイアイコンを実行時に生成
```

## 詳細・ドキュメント

- [HDR とトーンマッピングの仕組み](docs/hdr-tone-mapping.md) — なぜ HDR で白飛びするのか、FP16 取り込み＋トーンマップによる解決方法、トーンマップ演算子、トラブルシューティング
- [常駐版と開発ビルドの分離](docs/dev-resident-build.md) — GUI+CLI 同居バイナリの exe ロック問題と、常駐版を別パスに置いて開発ループと共存させる仕組み
- [設計メモ](docs/design.md) — 初期の要件・設計草案
