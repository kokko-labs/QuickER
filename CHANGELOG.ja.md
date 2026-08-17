# 変更履歴

*[English](CHANGELOG.md) | 日本語*

QuickER の利用者に影響する変更を記録します。形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/)、バージョンは [Semantic Versioning](https://semver.org/lang/ja/) に従います（0.x の間の版上げルールとリリース手順は [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照）。

## [0.1.0] - 2026-08-02

初回公開リリース。

### Added

- **ビジュアル ER 設計** — crow's foot 記法、1 対 1 / 1 対多 / 多対多、複合主キー、FK 参照アクション、包括的な Undo/Redo、大規模図向けのキャンバス UX（ズーム / パン / 検索 / ミニマップ）
- **マルチ DB 対応** — SQL Server / PostgreSQL / MySQL / Oracle / SQLite の 5 方言で、スキーマ取込・差分同期・DDL 生成・方言切替時の型自動変換
- **C# コード生成** — Entity / EditModel / Mapper と、DB アクセス 3 択（なし / QuickER 版 Repository / EF Core 版 Repository。同じインターフェイスを実装し、DI 登録 1 行で差し替え可能）。値オブジェクト、名前付きクエリ、リモート契約と HTTP + JSON サービス、ランタイム NuGet パッケージ参照モードはオプション
- **双方向同期** — ローカルの SQLite を SQL Server と同期させるエンジンを任意で生成（転送は直結／HTTP のどちらでも可）。初回構築・復旧向けの高速な洗い替えも生成
- **AI チャットとモック生成** — 対話による図の生成・編集（OpenAI API / Anthropic API / OpenAI 互換のローカル LLM / Codex / Claude Code / Copilot）、ER モデルからの Web 画面モック生成、任意で実行可能な Blazor / WPF モックプロジェクトの生成
- **MCP サーバ** — `quicker mcp` が図の編集とコード生成を外部 AI エージェントへ stdio で公開
- **入出力** — DBML / Mermaid / Excel 定義書 / HTML 定義書 / スキーマ JSON / PNG / SVG / ベクタ印刷
- **CLI** — `quicker generate` / `quicker scaffold` / `quicker reverse` / `quicker mcp`
- **動くサンプル** — `samples/ec-order`（SQLite・外部データベース不要）と `samples/ec-order-remote`（HTTP + JSON の 3 階層構成）

配布形態は、GUI（Setup.exe と Portable zip。自己完結の full チャンネルとフレームワーク依存の lite チャンネル）と NuGet パッケージ（dotnet tool の `QuickER.Cli` と、ランタイムパッケージ `QuickER.Runtime` / `.SqlServer` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.AspNetCore` / `.Sync`）です。

本リポジトリは混合ライセンスです。コアは MIT、AI 機能・コード生成・CLI・MCP ツール実行ホストの 8 プロジェクトは PolyForm Noncommercial 1.0.0 ＋追加許諾で、現在は商用利用を含め全員無料、基本コード生成の商用利用は恒久的に許諾されています。条文は [LICENSE-NC.md](LICENSE-NC.md)、平易な解説は [LICENSING.ja.md](LICENSING.ja.md) にあります。
