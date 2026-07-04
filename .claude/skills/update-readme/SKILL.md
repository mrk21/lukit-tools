---
name: update-readme
description: README.md を実際のプロジェクト状態とテンプレートの構成に追従させる。ビルド/タスクスクリプト（package.json・*.csproj・Cargo.toml・Makefile など）の追加・変更・削除、依存（技術スタック）の追加、ソースのディレクトリ構成変更で README が実体に追いついていないとき、または README の見出し構成がテンプレートからずれているときに使う。言語・ビルドツールを問わず動く。「README を更新して」「コマンドを追加したので README に反映して」「ディレクトリ構成が変わった」「README をテンプレに合わせて」などの依頼で必ず起動する。
---

# update-readme

README.md を、実際のプロジェクト状態（マニフェストのビルド/タスク・依存、ソースのディレクトリ構成）と**テンプレートの構成**に追従させる。特定の言語・ビルドツールに依存しない。

基準は 2 つ:

- **内容（コマンド・依存・パス）** は実体（`package.json` / `*.csproj` / `Cargo.toml` / `Makefile` などのマニフェストとソース）が正
- **構成（セクションと順序）** は [assets/readme-template.md](assets/readme-template.md) が正。README に収まらない背景・原理などの「読み物」は `docs/` に抽出してリンクする

## やること

読み比べの使い捨て context をメインの会話に残さないよう、本体は**サブエージェントに委譲する**。

1. `Agent` ツールで `subagent_type: readme-maintainer` を **1 体**起動する（README 1 ファイルが中心・並列不要、worktree 分離も不要）。タスクは次のように渡す:

   > README.md を実際のプロジェクト状態とテンプレート（[assets/readme-template.md](assets/readme-template.md)）の構成に追従させて。**今回のきっかけ**は〈分かっていれば具体的な変更を書く。例: 「scripts に `test:run` を追加」「`src/Foo/` を新設」「csproj に依存を追加」〉。テンプレートの骨組みに沿わせつつ、背景・原理などの解説は `docs/` に抽出して README からリンクして。working tree を編集するだけで commit / push はしない。既に同期済みなら編集せず、その旨を返して。最後に「何をどのセクションでどう変えたか（無ければ "変更なし"）」を 1〜数行で返して。

   起動元が変更内容を把握しているなら、`git log` / `git diff` から再発見させるより**具体的なきっかけを渡したほうが速く・取りこぼしにくい**（実体を正とする方針は変わらない）。マニフェストの自動判別・手順・スタイル・取捨選択の基準・docs 抽出の指針はサブエージェント定義（[.claude/agents/readme-maintainer.md](../../agents/readme-maintainer.md)）側に持たせてあるので、ここで細かく指示する必要はない。

2. サブエージェントが戻ってきたら、返ってきた**変更サマリ**と `git diff -- README.md`（`docs/` を触っていればそれも）の要点をユーザーに提示し、レビューしてもらう（こちらでは commit しない）。
