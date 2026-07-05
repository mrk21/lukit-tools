# CLAUDE.md

## 概要

プロジェクトの概要・技術スタック・セットアップ・使い方・開発コマンド・ディレクトリ構成は [README.md](README.md) にある。まずそちらを参照する。

なお、応答は日本語で行う。

## README.md の更新

- `.csproj` の依存パッケージ・TFM・ビルド設定、開発/実行コマンド、ディレクトリ構成、技術スタックを変えたら、`/update-readme` で README.md の該当セクションを同期する。

## TDD

- `Imaging/`（`ToneMapper` など）や、環境非依存の計算ロジック（座標・矩形ジオメトリ、輝度・色の計算、設定値の検証/シリアライズ）を足す/直すときは原則テストファースト。先に失敗するテストを書いてから実装する。手順は `/tdd`。
- D3D11/WGC/WPF/WinForms 依存（`Capture/`・`Interop/`・`UI/`）の中の計算ロジックは、環境非依存の関数に抽出して TDD する。「UI だからテストしない」と諦めない一方、テストのために設計を歪めるほど無理はしない（程度問題。詳細は `/tdd`）。
- テストは別プロジェクト `test/Lukit.Tests/`（xUnit v3）に、`src/Lukit/` の構成をミラーして置く。検証は `dotnet test`。

## ピクセル出力の正しさ

このツールの価値は「HDR でも色が破綻しないスクリーンショット」なので、出力ピクセルの正しさが要。純粋ロジックに抽出できる部分（トーンマップ演算・正規化・輝度計算・sRGB 変換）は `/tdd` の単体テストで守る（`algorithm` 相当＝`Imaging/`）。

実際にキャプチャ→トーンマップした「絵」そのものは、実 GPU／HDR ディスプレイが要り単体テストに載らない。ここは CLI 診断で目視確認する（役割分担: 計算ロジックは `/tdd`、実描画は CLI 診断）:

- `Lukit.exe --display-info` … 各モニタの HDR 状態・SDR 白色輝度
- `Lukit.exe --frame-stats` … プライマリを撮って scRGB 統計を表示
- `Lukit.exe --shot-fullscreen out.png [--op clip|reinhard|aces] [--sdr-white <nits>]` … 実際の保存画像を出して見る

CI で自動失敗する視覚回帰ゲートはまだ無い。必要になったら、固定入力の `HdrFrame`（合成 scRGB データ）→ `ToneMapper.ToBgra32` → PNG のゴールデン比較として `test/Lukit.Tests` 側に足すのが素直（実 GPU 不要で決定的にできる）。

## 見た目の確認（ビジュアルチェック）

GUI の見た目を PNG に落として人／AI が目視で回帰確認するループがある。手順・設計は [docs/visual-check.md](docs/visual-check.md)。対象は 2 種類:

- **A. 製品の出力**（トーンマップ後のスクショ）: 上の `--shot-*` / `--frame-stats`。ライブ画面・HDR 状態に依存する動的チェック。
- **B. アプリ自身の UI**（設定画面・矩形選択オーバーレイ）: `Lukit.exe --shot-ui <settings|overlay> out.png` で**画面外レンダリング**して PNG 化。トレイ／単一インスタンス Mutex／ホットキーに触れず決定的。UI を足したら [UiShot](src/Lukit/UI/UiShot.cs) の `Build` にサーフェスを 1 分岐足すだけ。

一括で撮るなら `pwsh tools/visual-check.ps1`（A+B を `artifacts/visual/<timestamp>/` に出して `manifest.md`）。出た PNG を Read して講評→修正で回す。UI 変更の確認を頼まれたらこのループを使う。これは目視ループであって、上記の CI 自動ゲートとは別（そちらは未整備）。真の操作 e2e（ボタン押下→遷移）が要るなら FlaUI 等の UIA ドライバを足す。

## 複雑度と設計見直し

分岐・ネストが深くなって**読みにくくなった**関数は「設計を見直す合図」。行数ではなく、人/AI が制御フローを追えるかを見る。踏んだら盛る前に一度止まって判断する:

- 既定は**再設計**（責務分割・データ構造・アルゴリズムの見直し）で複雑さを下げる。
- **数値・見た目のためだけの機械的な関数分割はしない**（ロジックが散らばるだけで設計改善にならない）。
- 本質的に必要な複雑さなら、そのままにして理由をコメントで残す。

複雑度が溜まりやすいのは新規の純粋ロジックより**既存コード（特に `Capture/`・`Interop/`・`UI/` のグルー）への機能追加**。そこを編集してネストが深くなったときこそこの方針が効く。

なお .NET には既定で認知的複雑度を機械計測するゲートが無い（移植元 terrain-playground の Biome 相当が無い）。機械化したくなったら `SonarAnalyzer.CSharp`（`S3776`）や `Roslynator` をアナライザとして追加し、`.editorconfig` で閾値・重大度を設定して `dotnet build`/`dotnet format` に載せる。背景と手順は `/tdd` の reference.md「複雑度ゲート」。
