# tdd — このプロジェクトでの TDD 規約

SKILL.md のサイクルを、Lukit の実体（.NET 10 + xUnit v3 + `dotnet format`）に合わせて具体化したもの。

## コマンド

| 目的                     | コマンド                                                          | 備考                                                        |
| ------------------------ | ---------------------------------------------------------------- | ----------------------------------------------------------- |
| テスト（watch）          | `dotnet watch test --project test/Lukit.Tests/Lukit.Tests.csproj` | 変更を監視して再実行。開発中ずっと回しておく用              |
| テスト（単発）           | `dotnet test`                                                    | ソリューション全体。各フェーズの検証はこれを使う            |
| ビルド / 型チェック      | `dotnet build src/Lukit/Lukit.csproj`                            | コンパイルエラー・型エラーの確認                            |
| 整形（format）           | `dotnet format`                                                  | `.editorconfig` に沿って**自動修正される**                  |
| 整形チェックのみ         | `dotnet format --verify-no-changes`                              | 差分があれば非0終了。完了ゲート用                           |

`dotnet test` はリポジトリ直下の `Lukit.slnx` を拾ってテストプロジェクトを走らせる。特定のテストだけ回したいときは `dotnet test --filter "FullyQualifiedName~ToneMapper"` のようにフィルタする。

## テストの書き方

テストは**本体とは別のプロジェクト** `test/Lukit.Tests/`（xUnit v3）に置く。.NET では TypeScript のように「ソースの隣に `*.test.cs`」を置いても拾われないので、この分離が前提。これに従う:

- テストは `src/Lukit/` の**フォルダ構成をミラーして**置く（例: `src/Lukit/Imaging/ToneMapper.cs` → `test/Lukit.Tests/Imaging/ToneMapperTests.cs`）。ファイル名は `<対象型>Tests.cs`、クラス名は `<対象型>Tests`。
- `using Xunit;` を使い、`[Fact]`（引数なし）/ `[Theory]` + `[InlineData(...)]`（パラメータ化）で書く。対象型はそのまま `using Lukit.Imaging;` などで参照する。
- **テスト名は日本語**で、振る舞いを説明する文にする。C# のメソッド名に長い文は書きづらいので `[Fact(DisplayName = "…")]` / `[Theory(DisplayName = "…")]` に日本語を入れる（テスト結果に出るのはこの表示名）。
- 既存の手本: [ToneMapperTests.cs](../../../test/Lukit.Tests/Imaging/ToneMapperTests.cs)。下のような粒度・文体に揃える。

```csharp
using Lukit.Capture;
using Lukit.Imaging;
using Xunit;

namespace Lukit.Tests.Imaging;

public class ToneMapperTests
{
    [Fact(DisplayName = "SDR 白（80nit 基準の 1.0）は Clip で 255 になる")]
    public void SdrWhiteClipsToFullByte()
    {
        var frame = new HdrFrame(1, 1, new[] { 1f, 1f, 1f });
        var settings = new ToneMapSettings { SdrWhiteNits = 80f, Operator = ToneMapOperator.Clip };

        byte[] bgra = ToneMapper.ToBgra32(frame, settings, out int stride);

        Assert.Equal(4, stride);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, bgra);
    }
}
```

数値計算の比較で浮動小数点誤差が出るときは、xUnit の許容誤差つき比較を使う: `Assert.Equal(expected, actual, tolerance: 1e-4)`（`double`）や `Assert.Equal(expected, actual, precision: 4)`（小数第4位で丸めて比較）。`byte[]` は `Assert.Equal(expected, actual)` でそのまま要素比較できる。

### `private` なヘルパを直接テストしたいとき

`ToneMapper` の `Luminance` / `Aces` / `LinearToSrgb8` などは `private static`。まずは**公開 API（`ToBgra32`）経由**でテストするのが素直で、実際の振る舞いを検証できる（入力フレーム＋設定 → 出力バイト列）。

ヘルパ単体を直接検証する価値が高い（分岐や式に中身があり、公開 API 経由だと条件を作りにくい）ときだけ、そのヘルパを `internal static` に上げ、本体側に次の一行を足してテストプロジェクトから見えるようにする:

```csharp
// src/Lukit/ 内のどこか（例: 対象ファイル or AssemblyInfo）
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Lukit.Tests")]
```

`public` に格上げして API を広げるより、`internal` + `InternalsVisibleTo` の方が公開面を汚さない。テストのためだけに何でも公開しない。

## テスト可否を分ける軸

テストしやすさは「**純粋関数かどうか**」ではなく「**実行環境（GPU / D3D11・WGC / WPF・WinForms / ウィンドウ・モニタ）に依存するか**」で決まる。

- **通常の .NET プロセスでそのままテストできる** = 数値・文字列・配列・プレーンなオブジェクトと標準ライブラリだけで動くコード。`ToneMapSettings` を受けて `HdrFrame`（`float[]`）から `byte[]` を作る `ToneMapper.ToBgra32` のように、内部で `Parallel.For` を使っていても環境に依存しなければテストできる。
- **そのままでは動かない** = `ID3D11Device`・`IDXGIOutputDuplication`・`GraphicsCaptureItem`・`GraphicsCaptureSession`・`HWND`/`HMONITOR`・WPF/WinForms のウィンドウやメッセージフックに触れるコード。実 GPU／実デスクトップ／メッセージループが要り、CI やヘッドレスでは再現できない。たとえロジックが純粋でも、これらの型を引数に取ればモックが要る。
- 副作用があってもテストはできる（例: PNG をファイルに書き出す関数は純粋ではないが、一時ファイルに書いて読み戻せば普通にテストできる）。純粋関数（同一入力→同一出力・副作用なし）であればテストが一段簡単になる、という補助的な関係にすぎない。

用語注意: このドキュメントで「純粋関数」と書くときは上の厳密な意味（同一入力→同一出力・副作用なし）。「テストできる／切り出す」の判断は純粋性ではなく**環境非依存かどうか**で行う。

## レイヤと抽出方針

このリポジトリはおおまかに「実行環境に触るか」で層が分かれており、テスト可否（＝環境依存の有無）もこの境界とほぼ一致する:

- **`Imaging/`**（`ToneMapper`・`ImageOutput` の一部）: scRGB→sRGB のトーンマップ、正規化、輝度計算、sRGB 変換など。数値だけで決定的に動き環境に依存しないので直接テストする。境界（黒・SDR 白・1.0 超の白飛び・負値）・範囲・既知の入力に対する既知の出力・BGRA のチャンネル順を確認する。
- **`Capture/` の一部・`HdrFrame`**: `HdrFrame` 自体は `float[]` のプレーンなデータ構造で、`Crop` は矩形ジオメトリの純計算なので直接テストできる（クランプ・境界）。一方 `CaptureEngine`（D3D11＋WGC）は環境依存でテスト対象外。
- **`Interop/`・`UI/`**: Vortice↔WinRT 相互運用、`HMONITOR`/`HWND` 解決、グローバルホットキー、トレイ常駐、矩形選択オーバーレイ、設定ウィンドウ。実 GPU・デスクトップ・メッセージループに触るのでユニットテストしない。

**抽出は程度問題**: `Display/`・`Interop/`・`UI/` の中に計算（モニタ矩形の合成・座標変換、ズーム/回転の更新、感度スケーリング、設定値のクランプ/検証、状態遷移など）が埋まっていることがある。これをテストのために何でも環境非依存へ追い込むと、不自然な型変換や薄い間接層が増えて**設計のほうが歪む**ことがある。テストで得られる価値が抽出のコストを上回るときにだけ切り出す。判断材料:

- 計算に中身があり（分岐・式・境界があり）単体で検証する価値が高い → 切り出す価値が高い。
- もともと数値や素の値だけで動く計算が、たまたま環境依存コードに同居しているだけ → 切り出しは自然。素直に出す。
- 環境依存の型（`HMONITOR`・`Rect` 相当）を受け取っているが、関数が実際に使うのは一部の数値だけ → その数値だけを引数にすれば、無理のない範囲で環境非依存にできる。
- 切り出すと不自然な変換層が要る／ほぼロジックの無い薄いグルー → 無理に出さない。グルーのまま対象外にする。

切り出すと決めたら:

1. 環境（GPU/D3D11/WGC/WPF/WinForms）に依存しない関数として出す。行き先は、**汎用的でどこからでも再利用できるものはその機能の中心となる namespace**（例: 画像処理なら `Imaging`、モニタ/座標なら `Display`）に `internal static` ヘルパとして、その画面・入力に固有のものは対象ファイルの隣（同 namespace）に置く。
2. その関数を TDD（RED→GREEN→REFACTOR）する。可能なら状態や時刻に依存させず純粋関数にすると、テストがさらに楽になる。
3. 元の環境依存側は、その関数を呼ぶだけの薄いグルーにする。グルーはテスト対象外でよい。

要は「UI だからテストしない」と最初から諦めない一方で、「**テストのために設計を歪めない**」。削り出すのが自然なものを、自然な形で削り出す。

## 複雑度ゲート

「複雑度→設計見直し」の運用方針は CLAUDE.md「複雑度と設計見直し」にある。ここはその技術的背景。

terrain-playground（移植元）は Biome の `noExcessiveCognitiveComplexity`（閾値15）で認知的複雑度を機械化していたが、**.NET には既定でこれに相当するゲートは無い**。測りたいのは行数ではなく**読みにくさ**（分岐・ネストの深さ＝人/AI が追える設計か）で、冗長でも直線的なコードは問題になりにくく、深くネストした制御フローで効いてくる、という考え方だけを引き継ぐ。

- 既定の運用は**自分でのレビュー観点**: `Capture/`・`Interop/`・`UI/` のような既存グルーへ機能追加してネストが深くなったら、盛る前に一度止まって設計を見直す。数値を通すためだけの機械的な関数分割はしない（ロジックが散らばるだけ）。
- 機械計測したくなったら、アナライザ（`SonarAnalyzer.CSharp` の `S3776` 認知的複雑度、または `Roslynator`）をパッケージ追加し、`.editorconfig` で重大度と閾値を設定して `dotnet build` / `dotnet format` に載せる。導入したらこの節と CLAUDE.md を実態に合わせて更新する（そのときは `/update-readme` も）。

## カバレッジ

既定ではカバレッジ閾値を強制しない（個人プロジェクトで摩擦になるため）。必要なときだけ任意で測る:

```powershell
dotnet test --collect:"XPlat Code Coverage"   # coverlet.collector（テストテンプレート同梱）で cobertura を出力
```
