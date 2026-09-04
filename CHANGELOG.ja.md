# 変更履歴

*[English](CHANGELOG.md) | 日本語*

QuickER の利用者に影響する変更を記録します。形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/)、バージョンは [Semantic Versioning](https://semver.org/lang/ja/) に従います（0.x の間の版上げルールとリリース手順は [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照）。

## [Unreleased]

### Added

- **生成された値オブジェクトが走らせる検証ルールを公開し、再利用できるようにしました** — `ValueObjectRules.ValidateRequired` / `ValueObjectStringRules.ValidateMaxLength` / `ValueObjectDecimalRules.Validate` を値オブジェクトごとの展開から固定 infra（パッケージ参照モードでは `QuickER.Runtime`）へ移しました。生成される `ValidateCore` は規則 1 つにつき 1 行の委譲になり、手書きの値オブジェクトからも同じ規則を呼べます
- **`OnValidate` から呼べる汎用の検証ルールを 4 種追加しました** — `ValueObjectNumberRules.ValidateMaxDigits`（整数の桁数）・`ValueObjectNumberRules.ValidateRange`（閉区間。`IComparable<T>` なら何でも）・`ValueObjectStringRules.ValidateAsciiAlphanumeric`（ASCII 英数字＋許可した記号）・`ValueObjectStringRules.ValidateEmailAddress`（実用最小。RFC 5322 の完全検証は意図的にしない）。文言は `ValueObjectValidationMessages` の新エントリ（`DigitsExceeded` / `OutOfRange` / `InvalidCharacters` / `InvalidEmailAddress`）です

### Changed

- **破壊的変更: 値オブジェクトごとの文言・表示名フックを廃止しました** — 生成される値オブジェクトは `CustomizeDisplayName` / `CustomizeMaxLengthErrorMessage` / `CustomizeScaleErrorMessage` / `CustomizePrecisionErrorMessage` / `CustomizeValueRequiredErrorMessage` を宣言しなくなったため、既存の実装はコンパイルできなくなります。表示名は `GeneratedDisplayNames.Resolve`、文言は対応する `ValueObjectValidationMessages` のエントリで差し替え、型を絞りたいときは名前で分岐してください（`memberName == nameof(CustomerEntity.Name)` / `displayName == NameValue.DisplayName`）。文言が「型ごとに 1 か所」でなく「メッセージごとに 1 か所」へまとまり、再生成後に生成側クラスへ実装し直す必要もなくなります。`OnValidate` / `GetDefinedInstance` / `ConvertCustomInput` は変わりません
- **破壊的変更: `ValueObjectValidationMessages` の各エントリが第 1 引数に表示名を取るようになりました** — `MaxLengthExceeded` は `(displayName, maxLength, actualLength)`、`ScaleExceeded` / `PrecisionExceeded` は `(displayName, …)`、`ValueRequired` は `(displayName)`、`InputNotConvertible` は引数順を `(displayName, raw)` へ入れ替えました。1 つの差し替えで型ごとに文言を変えられるのはこの引数のおかげです。既定の文面は変えていないため、差し替えていないアプリの見た目は変わりません
- **破壊的変更: 生成 Mapper の `includeRemoved` が必須引数になりました** — `CreateEntity(editModel, includeRemoved)` / `CreateEntities(collection, includeRemoved)` / `ApplyToEntity(editModel, entity, includeRemoved)` の既定値 `false` を撤廃したため、呼び出しごとに「保存用のグラフを作るのか（`true`）表示用なのか（`false`）」を明示します。省略できた頃は削除追跡中の行がグラフから漏れ、保存してもユーザーが消したはずの行が黙って残っていました。移行はコードを再生成し、コンパイルエラーになった呼び出しへ保存経路なら `includeRemoved: true`、表示経路なら `includeRemoved: false` を付けてください

## [0.1.0] - 2026-08-30

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

[Unreleased]: https://github.com/kokko-labs/QuickER/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.1.0
