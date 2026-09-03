# 変更履歴

*[English](CHANGELOG.md) | 日本語*

QuickER の利用者に影響する変更を記録します。形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/)、バージョンは [Semantic Versioning](https://semver.org/lang/ja/) に従います（0.x の間の版上げルールとリリース手順は [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照）。

## [Unreleased]

### Added

- **生成された値オブジェクトが走らせる検証ルールを公開し、再利用できるようにしました** — `ValueObjectRules.ValidateRequired` / `ValueObjectStringRules.ValidateMaxLength` / `ValueObjectDecimalRules.Validate` を値オブジェクトごとの展開から固定 infra（パッケージ参照モードでは `QuickER.Runtime`）へ移しました。生成される `ValidateCore` は規則 1 つにつき 1 行の委譲になり、手書きの値オブジェクトからも同じ規則を呼べます
- **`OnValidate` から呼べる汎用の検証ルールを 4 種追加しました** — `ValueObjectNumberRules.ValidateMaxDigits`（整数の桁数）・`ValueObjectNumberRules.ValidateRange`（閉区間。`IComparable<T>` なら何でも）・`ValueObjectStringRules.ValidateAsciiAlphanumeric`（ASCII 英数字＋許可した記号）・`ValueObjectStringRules.ValidateEmailAddress`（実用最小。RFC 5322 の完全検証は意図的にしない）。文言は `ValueObjectValidationMessages` の新エントリ（`DigitsExceeded` / `OutOfRange` / `InvalidCharacters` / `InvalidEmailAddress`）です
- **同期対象テーブルを「クラス」でなく「データ」で記述するようにしました** — `SyncTableDescriptor<TEntity, TKey>` がテーブル固有の情報（名前・方言別 SQL・キー／ミラー版／無制限バイナリ列の読み書き）をすべて持ち、生成される `GeneratedSyncTables` がテーブルごとの静的記述子 1 つと `All`・共有の `GraphRecorder` を公開します。`SyncGraphRecorder` は `QuickER.Runtime.Sync` の固定クラスになり、走査は保存側（グラフ保存）と同じ `EntityBase.EnumerateCascadeChildren` 1 つを辿ります（エンティティ型ごとの再帰メソッド生成をやめたので「同じ走査の 2 実装」が乖離しようがありません）。そのために `EntityBase.EnumerateCascadeChildren` を public にしました。無制限バイナリ列のコピー面も `SyncRepositoryBinaryColumns<TEntity, TKey>` 1 実装（ローカル・直結サーバー共通）になります

### Changed

- **破壊的変更: 値オブジェクトごとの文言・表示名フックを廃止しました** — 生成される値オブジェクトは `CustomizeDisplayName` / `CustomizeMaxLengthErrorMessage` / `CustomizeScaleErrorMessage` / `CustomizePrecisionErrorMessage` / `CustomizeValueRequiredErrorMessage` を宣言しなくなったため、既存の実装はコンパイルできなくなります。表示名は `GeneratedDisplayNames.Resolve`、文言は対応する `ValueObjectValidationMessages` のエントリで差し替え、型を絞りたいときは名前で分岐してください（`memberName == nameof(CustomerEntity.Name)` / `displayName == NameValue.DisplayName`）。文言が「型ごとに 1 か所」でなく「メッセージごとに 1 か所」へまとまり、再生成後に生成側クラスへ実装し直す必要もなくなります。`OnValidate` / `GetDefinedInstance` / `ConvertCustomInput` は変わりません
- **破壊的変更: `ValueObjectValidationMessages` の各エントリが第 1 引数に表示名を取るようになりました** — `MaxLengthExceeded` は `(displayName, maxLength, actualLength)`、`ScaleExceeded` / `PrecisionExceeded` は `(displayName, …)`、`ValueRequired` は `(displayName)`、`InputNotConvertible` は引数順を `(displayName, raw)` へ入れ替えました。1 つの差し替えで型ごとに文言を変えられるのはこの引数のおかげです。既定の文面は変えていないため、差し替えていないアプリの見た目は変わりません
- **破壊的変更: `CheckUniquenessAsync` の宣言を `IRemoteRepository<TEntity, TKey>` へ移しました** — 重複事前チェックは従来エンティティごとの `I{Entity}RemoteRepository`（リモート契約なしなら `I{Entity}Repository`）が宣言し、生成される全 Repository クラスが同じ実装を持っていました。宣言はランタイム共通面へ、実装は各バックエンドの Repository 基底（方言・インメモリ・EF Core・HTTP クライアント）へ 1 回ずつ移しています。呼び出し側への影響はありません（`I{Entity}Repository` は継承で同じメンバーを提供し、シグネチャ・戻り値・`CollectCustomUniquenessChecks` フックは不変＝既存の手書きフックは無修正で動きます）。壊れるのは `IRemoteRepository<,>` / `IRepository<,>` を**手書きで実装**しているコード（テストダブルやアダプタ）で、このメンバーの実装が必要になります（生成基底を継承していれば自動的に満たされます）。生成 Repository が持つのは制約テーブル（`{Entity}UniquenessConstraints.Set`）とフックへの 2 行の橋渡しだけになり、エンティティ 1 つ × バックエンド 1 つあたり約 50 行減ります
- **DI 登録がエンティティ 1 つにつき 1 行になりました** — `AddGenerated{SqlServer,Sqlite}Repositories` と `AddGeneratedEfCoreRepositories` は `services.AddScoped<IContract, Implementation>()` で登録し、実体の生成は同じ呼び出しが登録した依存からコンテナに任せます（`AddGeneratedInMemoryRepositories` が元から使っていた形）。keyed オーバーロードと `AddGeneratedHttpRemoteRepositories` は、全エンティティで共通の配線を 1 つのローカル関数へ畳みました。登録のライフタイム・`ISqlExecutor` の keyed / 非 keyed 解決・`TryAddScoped<ISaveHookRegistry>` の既定・リモート面が同一インスタンスへ転送されること、はいずれも変わりません
- **破壊的変更: テーブルごとの同期クラスを廃止しました** — `{Entity}SyncTable` / `{Entity}DirectSyncSource` / `Http{Entity}SyncSource` は生成されなくなりました。これらが継承していた固定クラスが記述子を受け取る形になり、直接インスタンス化します（`new SyncTable<OrderEntity, int>(local, localSql, source, GeneratedSyncTables.Order)` / `new VersionlessSyncTable<…>` / `new DirectSyncSource<OrderEntity, int>(serverSql, serverRepository, GeneratedSyncTables.Order)` / `new VersionlessDirectSyncSource<…>` / `new HttpSyncServerSource<OrderEntity, int>(httpClient, GeneratedSyncTables.Order)`）。これに伴い `SyncTable<,>` / `VersionlessSyncTable<,>` / `DirectSyncSource<,>` / `VersionlessDirectSyncSource<,>` / `HttpSyncServerSource<,>` は abstract から sealed へ変わり、`JournalingRepositoryBase<,>` と 2 つの派生は記述子と共有 `SyncGraphRecorder` をコンストラクタ引数に取ります。DI 登録（`AddGeneratedSyncSupport` / `AddGeneratedSyncEngine` / `AddGeneratedDirectSyncSources` / `AddGeneratedHttpSyncSources`）・エンジン・`ISyncTable` / `ISyncServerSource<,>` / `ISyncBinaryColumns<TKey>`・生成される `Journaling{Entity}Repository`（引き続き生成・`(inner, journal)` のまま）を使うコードに影響はありません。壊れるのは廃止クラスを名指ししているコードと、sealed 化したクラスを継承しているコードです。テーブルごとの生成コードは約 70〜80% 減ります（3 テーブルの同期フィクスチャで 779 行 → 210 行）
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
