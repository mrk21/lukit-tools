# 常駐版と開発ビルドの分離

Lukit を自分の環境に常駐させながら開発するときに起きる **exe ファイルロック問題**と、その回避方法をまとめる。日常の操作手順は [README の「開発方法」](../README.md#開発方法) を参照。

## なぜ開発中に exe がロックされるのか

Lukit は GUI（トレイ常駐）と CLI（`--display-info` 等）を **1 つの実行ファイル**に同居させている（`Program.cs` の `Main` が引数で GUI / CLI を振り分ける）。そのため、動作確認のつもりで `bin\Debug` の `Lukit.exe` をそのまま常駐起動すると、その exe が実行中のプロセスに掴まれたままになる。

この状態で `dotnet run` や `dotnet build` を実行すると、MSBuild は同じ `bin\Debug\...\Lukit.exe` を上書きしようとして**ファイル使用中エラー**で失敗する。開発のたびに常駐版を落とす運用は面倒で、常駐ツールとして片手落ちになる。

## なぜ別パスに置くと解決するのか

Windows のファイルロックは**アプリ単位ではなく exe ファイル（パス）単位**でかかる。「Lukit というアプリが動いているか」ではなく「その exe ファイルが開かれているか」で決まる。

そこで、常駐用の実行ファイルを開発ビルドとは別の場所に置く：

- **常駐版**：self-contained single-file publish した `Lukit.exe` を `%LOCALAPPDATA%\Programs\Lukit\Lukit.exe` に配置し、ここから常駐起動する。
- **開発ビルド**：`dotnet run` / `dotnet watch` は従来どおり `bin\Debug\...\Lukit.exe` を生成・実行する。

常駐プロセスが握っているのは `%LOCALAPPDATA%` 側、MSBuild が書き換えるのは `bin\Debug` 側。**握るファイルと書くファイルが別**なので衝突しない。結果、常駐版を落とさずに開発ループ（再ビルド・再実行）をそのまま回せる（`--no-build` も要らない）。self-contained にするのは、`%LOCALAPPDATA%` に置いた実行ファイルが開発ビルドの成果物や SDK に依存せず単独で動くようにするため。

## 単一インスタンスと CLI の同居

Lukit は **セッションローカルの単一インスタンス Mutex** を持つ（`Program.cs` の `RunGui`）。トレイ常駐 GUI はユーザーセッションにつき 1 つだけで、既に常駐している状態でもう一度 GUI を起動しようとすると「すでに起動しています」という情報ダイアログで弾かれる。

一方、CLI ユーティリティ（`--display-info` など）は Mutex ガードに到達する前に処理を返すため、**常駐版が動いていても並行して実行できる**。診断コマンドを試すだけなら常駐版を落とす必要はない。

このため、常駐版を終了する必要があるのは **GUI 自体を触るテスト**のときだけ。引数なしの `dotnet run` は GUI を起動するので、常駐版が上がっていると情報ダイアログに阻まれる。GUI を開発ビルドで確認したいときは、先に常駐版を終了してから `dotnet run` する。

## 常駐版の更新とスタートアップ登録

常駐版の配置・更新とスタートアップ自動起動の登録は `scripts/install.ps1` にまとめてある（self-contained publish → `%LOCALAPPDATA%\Programs\Lukit` へ上書き、`-Startup` で HKCU Run キー登録、常駐プロセスは終了してから上書きする）。使い方は [README の「セットアップ」](../README.md#セットアップ) を参照。他人へ配る setup.exe / portable zip の発行手順は [README の「リリース」](../README.md#リリース) にある。
