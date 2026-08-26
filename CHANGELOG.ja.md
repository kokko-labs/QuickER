# 変更履歴

*[English](CHANGELOG.md) | 日本語*

QuickER の利用者に影響する変更を記録します。形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/)、バージョンは [Semantic Versioning](https://semver.org/lang/ja/) に従います（0.x の間の版上げルールとリリース手順は [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照）。

## [Unreleased]

### Added

- **手書きの値オブジェクトが 3 メンバで書けるように** — `Create` / `TryCreate` / `Validate` の本体を `ValueObjectBase<TSelf, TValue>` へ 1 回だけ置く形にし、図の列に対応しない値オブジェクトを「private コンストラクタ＋`New`／`ValidateCore` の明示的実装」で書けるようにした（docs/code-generation.ja.md の「手書きの値オブジェクト」節を参照）。生成される値オブジェクトも同じ形に縮む（挙動は同一）

### Changed

- **破壊的変更**: `IValueObject<TSelf, TValue>` に必須メンバ `New`（と既定実装付き `ValidateCore`）が増え、値オブジェクトの基底クラス群は `TSelf` に `IValueObject<TSelf, TValue>` の実装を要求するようになった。インターフェイスを手で実装している型は 1 行（`static T IValueObject<T, V>.New(V v) => new(v);`）、基底から派生だけしてインターフェイスを宣言していなかった型は宣言への追加で移行できる。再生成したコードは影響なし

### Changed

- インメモリ Repository のサンプルデータが利用者定義の検証規則（手書きの `OnValidate`・手書きの値オブジェクト）に拒否されたとき——これは生成サンプル値には知りようのない規則——`AddGeneratedInMemoryRepositories()` から出る例外が、エンティティ名・プロパティ名・入れようとした値・元の検証メッセージを名指しし、対処（`seedSampleData: false` にして自前でデータを入れる）を案内するようになった。元の `ValueObjectValidationException` は inner として保全される。そのような規則を書いていない場合の挙動は変わらない

### Fixed

- インメモリ Repository のサンプルデータシーダーが、decimal 列ごとの宣言 precision / scale に収まる値を組み立てるようになった。従来は全 decimal 列に同じリテラルを使っていたため、値オブジェクト有効時に scale 1・scale 0・整数部の余裕が 3 桁未満のいずれかの decimal 列があると、既定設定の `AddGeneratedInMemoryRepositories()` が登録時に `ValueObjectValidationException` で失敗していた
- 値オブジェクトの `Create` ファクトリのリフレクション解決（SQL パラメータの再ラップ・行 materializer の高速経路）が、基底クラスから継承したファクトリを見つけるようになった（`BindingFlags.FlattenHierarchy`）。従来はそのような値オブジェクトが静かに遅い読み取りへフォールバックし、生 SQL のスカラー・射影変換は失敗していた
- リモートクライアントが、結果が null になり得ない操作への「2xx＋JSON リテラル `null`」応答を `RemoteRepositoryException` として分類するようになった（従来は離れた場所の不明瞭な `NullReferenceException` になっていた）。`GetById`・単一戻り形・null 許容スカラーのクエリは従来どおり null を「該当行なし」として返す。あわせて生成 Mapper の `ApplyToEntity` の全パラメータが文書化され、`GenerateDocumentationFile` 有効のビルドで CS1573 が出なくなった

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
