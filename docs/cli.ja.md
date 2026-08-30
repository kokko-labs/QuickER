# CLI リファレンス（quicker）

*[English](cli.md) | 日本語*

QuickER の CLI は、コード生成用のサブコマンド（`generate` / `scaffold`）、生成 C# コードから図を復元する `reverse`、AI エージェント向けの MCP サーバを起動する `mcp`（[MCP サーバ](mcp.ja.md)を参照）を提供します。

| コマンド | 入力 | 出力 |
|---|---|---|
| `quicker generate` | ER 図 JSON（GUI の保存形式） | C# コード |
| `quicker scaffold` | データベース接続文字列（スキーマを直接取込） | C# コード |
| `quicker reverse` | 生成 C# ソース（`IncludeDataAnnotations` 有効で生成した `.g.cs`） | スキーマのみの ER 図 JSON |

CLI の表示言語は OS の言語設定に従います（日本語 / 英語）。

`dotnet tool install --global QuickER.Cli` でインストールできます。ソースから実行する場合:

```powershell
dotnet run --project src/QuickER.Cli -- generate --schema diagram.json --out ./Generated
```

## quicker generate

ER 図 JSON から C# コード（Entity / EditModel / Mapper / Repository など）を生成します。

```powershell
quicker generate --schema diagram.json --out ./Generated --provider sqlserver --config quicker.json
```

| オプション | 必須 | 説明 |
|---|:-:|---|
| `--schema <file>` | ✅ | 入力する ER 図 JSON ファイル（アプリの保存形式） |
| `--out <dir>` | ✅ | 生成コードの出力先フォルダ |
| `--provider <name>` | | 対象データベースの種類。`sqlserver`（既定）/ `postgresql` / `mysql` / `oracle` / `sqlite` |
| `--config <file>` | | 生成オプション設定ファイル（quicker.json）。下記参照 |

これらに加えて、**設定ファイル（quicker.json）の全キーは同名の kebab-case フラグとして指定でき**、設定ファイルより優先されます（優先順位: **CLI フラグ ＞ 設定ファイル ＞ 既定値**）。フラグ名はキーの機械的な kebab-case 変換で、例えば `rootNamespace` → `--root-namespace`、`generateRepositories` → `--generate-repositories`、`splitFilesByCategory` → `--split-files-by-category`、`outputPath` → `--output-path` です。ただし機械的変換の例外があります。API リファレンス関連の 3 キーと生成コードのサブフォルダは短い綴りで、`IncludeJapaneseApiDocs` → `--api-docs-ja`・`ApiDocsSubdirectory` → `--api-docs-subdir`・`ApiDocsFileName` → `--api-docs-file`・`CodeSubdirectory` → `--code-subdir` です。また正準キー `OutputFileName` には専用フラグがなく `--output-path`（そのファイル名部分のみが使われる）を使います。bool キーは**三値**で、`--flag`（値なし）＝ `true`、`--flag false` ＝ `false`、未指定＝設定ファイルの値になります。各キーの意味は下記「設定ファイル」の表を参照してください（`--repository-dialects` はカンマ区切りの方言リストで、未指定時は `--provider` の方言から単一導出します）。

## quicker scaffold

データベースへ直接接続し、スキーマを取り込んでコードを生成します。オプションは `generate` と共通で、`--schema` の代わりに `--connection` を指定します。

```powershell
quicker scaffold --connection "Server=.;Database=Shop;Integrated Security=true;TrustServerCertificate=true" --out ./Generated --provider sqlserver
```

| オプション | 必須 | 説明 |
|---|:-:|---|
| `--connection <string>` | ✅ | 接続文字列（形式は `--provider` の DBMS に従う） |

そのほかのオプション（`--out` / `--config` / `--provider`、および設定キーと同名の kebab-case フラグ群）は `generate` と同じです。

## quicker reverse

QuickER が生成した C# コードから ER 図を復元し、スキーマのみの図 JSON として書き出します。入力は `IncludeDataAnnotations` を有効にして生成した本体 `.g.cs` である必要があります（DB 定義メタ属性 `[DbTableMeta]` / `[DbColumnMeta]` がスキーマ情報を担うため）。手書きの POCO は対象外です。

```powershell
quicker reverse --source ./Generated/QuickEREntities.g.cs --out diagram.json --provider sqlserver
```

| オプション | 必須 | 説明 |
|---|:-:|---|
| `--source <file>` | ✅ | 入力する C# ソースファイル（`IncludeDataAnnotations` 有効で生成した本体 `.g.cs`） |
| `--out <file>` | ✅ | 出力する ER 図 JSON ファイル（スキーマのみ・`layout` キーなし） |
| `--provider <name>` | | 列型の展開に使う方言と、図に記録される `TargetDbms`。`sqlserver`（既定）/ `postgresql` / `mysql` / `oracle` / `sqlite` |

結果は新規の図として書き出し、既存図へのマージは行いません。致命的でない指摘は警告として標準エラーへ出力し、解析対象クラスが 0 件の場合は失敗します。

## quicker mcp

ER 図編集・コード生成ツールを AI エージェント（Claude Code・Codex など）へ公開する stdio MCP（Model Context Protocol）サーバを起動します。オプションはなく、ステートレスです。各ツールは対象の図ファイルを `file` 引数で受け取ります（唯一の情報系ツール `get_generation_config_schema` だけは `file` を取りません）。

```powershell
quicker mcp
```

エージェントはこれを子プロセスとして起動し、標準入出力（JSON-RPC）で通信します。ツールの全一覧・クライアント設定・注意事項は [MCP サーバ](mcp.ja.md)を参照してください。

## 設定ファイル（quicker.json）

`--config` で渡す JSON で、生成オプションをまとめて指定できます。これは GUI の設定保存ファイル（`codegen-settings.json`）と**同一スキーマ**です（GUI は camelCase で書き出しますが、CLI は大文字小文字を区別せず解釈するためそのまま渡せます）。**下表の各キーは同名の kebab-case フラグとして CLI からも指定でき、設定ファイルより優先されます**（優先順位: CLI フラグ ＞ 設定ファイル ＞ 既定値。bool は `--flag` / `--flag false` の三値）。

> **注意**: `GenerateRepositories` の既定が `true` → `false` になりました。以前はキーを省略すると QuickER 版 Repository が生成されましたが、**現在は DB アクセスコードを生成するには `GenerateRepositories: true`（または `GenerateEfCore: true`）の明示指定が必要**です（GUI の DB アクセス「なし」既定と揃えるため）。

キーはカテゴリ順（出力モード → 名前空間 → 生成対象 → 値オブジェクト → DB アクセス → リモート対応 → ランタイム・ドキュメント → 属性 → 出力先）に並べています。

```json
{
  "SplitFilesByCategory": false,
  "RootNamespace": "MyApp.Generated",
  "GenerateEditModels": true,
  "GenerateMappers": true,
  "GenerateValueObjects": false,
  "GenerateRepositories": true,
  "GenerateEfCore": false,
  "IncludeDataAnnotations": true,
  "OutputPath": "MyAppEntities.g.cs"
}
```

主なキー（かっこ内は既定値・カテゴリ順）:

| キー | 説明 |
|---|---|
| `SplitFilesByCategory`（`false`） | カテゴリごとに別ファイル・別名前空間で出力する。`EntityNamespace` / `RepositoryNamespace` などで名前空間を個別指定できる |
| `LayeredOutput`（`false`） | 分割ファイルを出力ディレクトリ配下の層別サブフォルダ（ドメイン／プレゼンテーション／インフラ／サーバー）へ振り分け、各層を独立プロジェクトにできるようにする。`SplitFilesByCategory` を自動含意（[生成コードの使い方](code-generation.ja.md#層別フォルダ出力--layered-output) 参照） |
| `DomainLayerDirectory` / `PresentationLayerDirectory` / `InfrastructureLayerDirectory` / `ServerLayerDirectory`（`Domain` / `Presentation` / `Infrastructure` / `Server`） | `LayeredOutput` の層別フォルダ（出力ディレクトリからの相対パス）。複数階層（`MyApp.Domain/Generated`）も可。`LayeredOutput` では空の名前空間キーの既定もこのフォルダから導出され、フォルダと名前空間が揃う。絶対パス・ドライブ指定・`..` は生成時エラーで拒否され、空の値は既定へフォールバックする。`ServerLayerDirectory` は `GenerateRemoteServices` のときのみ使われる |
| `CodeSubdirectory`（未指定＝サブフォルダなし） | 生成コード（`*.g.cs`）の出力先サブフォルダ。`LayeredOutput` では層フォルダの下、そうでなければ出力ディレクトリの下へ 1 段（例: `Generated`・複数階層可）。全出力モードで有効で、**名前空間には影響しない**ため C# 識別子である必要もない。絶対パスと `..` は拒否。API リファレンス Markdown は追随しない（CLI の `--code-subdir` に対応） |
| `RootNamespace`（`Generated`） | 生成コードのルート名前空間 |
| `GenerateEditModels` / `GenerateMappers`（ともに `true`） | 各カテゴリの生成有無。**Entity クラスは常時生成**され、専用キーはない |
| `GenerateValueObjects`（`false`） | 列ごとの値オブジェクト型（`CustomerIdValue` など）を生成する（[生成コードの使い方](code-generation.ja.md#値オブジェクトgeneratevalueobjects) 参照） |
| `UseGuidKeyForStringPrimaryKey`（`false`） | string 主キーを GUID 値オブジェクトにする（`GenerateValueObjects` が有効な場合のみ） |
| `GenerateRepositories`（`false`） | QuickER 版 Repository（軽量ミニ ORM）を生成する。**既定では DB アクセスコードを生成しない**（GUI と同じ既定） |
| `RepositoryDialects`（未指定） | QuickER 版 Repository のマルチターゲット方言リスト（例 `["sqlserver", "sqlite"]`）。対応方言は `sqlserver` と `sqlite` のみで、それ以外を `GenerateRepositories` と併用すると生成前にエラーになる。未指定時の実効値は `--repository-dialects` ＞ 設定ファイルの本キー ＞ `--provider` からの単一導出、の順で決まる |
| `ExcludeUnboundedBinaryColumns`（`false`） | 無制限バイナリ列を QuickER 版 Repository の SELECT / UPDATE から除外する（CLI の `--exclude-unbounded-binary-columns` に対応。[生成コードの使い方](code-generation.ja.md#無制限バイナリ列の除外excludeunboundedbinarycolumns) 参照） |
| `GenerateEfCore`（`false`） | EF Core 用の `QuickErDbContext` ＋ EF Core 版 Repository 実装を生成する。マルチターゲット（実効方言 2 つ以上）とは併用不可 |
| `GenerateInMemoryRepositories`（`false`） | テスト用のインメモリ Repository 実装を生成する |
| `GenerateRemoteContracts`（`false`） | リモート操作用インターフェイス `I{Entity}RemoteRepository` を追加生成する（CLI の `--generate-remote-contracts` に対応。QuickER 版 Repository・EF Core 版 Repository・インメモリ Repository のいずれか＝`GenerateRepositories` / `GenerateEfCore` / `GenerateInMemoryRepositories` のいずれかが前提。[生成コードの使い方](code-generation.ja.md) 参照） |
| `GenerateRemoteServices`（`false`） | リモート面の HTTP クライアント／サーバー実装を生成する（`GenerateRemoteContracts` を自動的に含意。CLI の `--generate-remote-services` に対応。[生成コードの使い方](code-generation.ja.md) 参照） |
| `GenerateSyncSupport`（`false`） | サーバー（SQL Server）＋ローカル（SQLite）構成の双方向同期支援を生成する。`GenerateRepositories` が有効で実効方言が `sqlserver` と `sqlite` のちょうど 2 つ、かつ `rowversion` 列を持つテーブルが 1 つ以上あることが前提（`ExcludeUnboundedBinaryColumns` とは併用可能で、除外列は `SyncOptions.IncludeUnboundedBinary` を指定したときだけ運ばれる）。CLI の `--generate-sync-support` に対応。[生成コードの使い方](code-generation.ja.md#双方向同期の支援--generate-sync-support) 参照 |
| `UseRuntimePackages`（`false`） | ランタイム固定コードを出力せず NuGet パッケージ参照で賄う（[生成コードの使い方](code-generation.ja.md) 参照） |
| `GenerateApiDocs`（`false`） | API リファレンス Markdown（`{ベース名}.g.md`・英語正本）を追加出力する（CLI の `--generate-api-docs` に対応。[生成コードの使い方](code-generation.ja.md) 参照） |
| `IncludeJapaneseApiDocs`（`false`） | 日本語版 API リファレンス Markdown（`{ベース名}.ja.g.md`）も併産する（`GenerateApiDocs` が前提。CLI の `--api-docs-ja` に対応） |
| `ApiDocsSubdirectory`（未指定＝出力ディレクトリ直下） | API リファレンス Markdown の出力先サブフォルダ（出力ディレクトリからの相対パス。例: `docs`・複数階層可・絶対パスと `..` は拒否）。`GenerateApiDocs` が前提で `LayeredOutput` とは独立（CLI の `--api-docs-subdir` に対応） |
| `ApiDocsFileName`（未指定＝導出名） | API リファレンス Markdown の出力ファイル名（拡張子は `.g.md` へ正規化・日本語版は同じベース名の `.ja.g.md`）。未指定なら従来どおりの導出名（非分割＝出力ファイル名のベース名／分割＝`ApiDocs.g.md`）。指定できるのはファイル名だけでパス区切りは拒否（置き場は `ApiDocsSubdirectory`）。`GenerateApiDocs` が前提（CLI の `--api-docs-file` に対応） |
| `IncludeDataAnnotations`（`true`） | `[Required]` / `[MaxLength]` 等の DataAnnotations と、DB 定義メタ属性（`[DbTableMeta]` / `[DbColumnMeta]`）を付与する |
| `IncludeJsonIgnoreOnParentNavigation`（`true`） | 親参照ナビゲーションへ `[JsonIgnore]` を付与する（JSON シリアライズ時の循環参照対策） |
| `OutputFileName`（`QuickEREntities.g.cs`）— 別名 `OutputPath` も受け付ける | 単一ファイル出力のファイル名（`.g.cs` が無ければ補われる。`SplitFilesByCategory` が真のときは無視）。正準キーは `OutputFileName` で、`get_generation_config_schema` が返すのもこの名前。`OutputPath` はその別名で、ファイル名部分のみが使われる（出力先ディレクトリは常に `--out`）。GUI では `OutputPath` に出力先のフルパス（非分割時はファイル・分割時はフォルダ）が入ることがあるが、CLI は同じ規則で解釈する。**ここだけは「CLI フラグ ＞ 設定ファイル」の例外**で、`--output-path` が効くのは設定ファイルに `OutputFileName` が無い場合のみ。設定ファイルに `OutputFileName` があるときはそちらが優先される |

## 実行例 — リポジトリ同梱サンプルの再生成

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json `
  --generate-api-docs
```

`--generate-api-docs` により、生成コード `EcOrder.g.cs` と同じベース名の API リファレンス Markdown
`EcOrder.g.md`（英語正本）も同梱出力されます（チェックイン済み・ドリフト検知の対象）。日本語版
`{ベース名}.ja.g.md` も欲しい場合は `--api-docs-ja` を追加します（`--generate-api-docs` が前提）。

## ライセンス注記

CLI（`QuickER.Cli`）・コード生成エンジン・MCP ツール実行ホスト（`QuickER.Mcp.Tools`）には [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) **＋追加許諾**が適用されます。この追加許諾により、**現行リリースは商用利用を含め全員無料**です。ツール定義カタログ・stdio ホスト基盤（`QuickER.Mcp`）は MIT です。NC 対象は全 8 プロジェクトで、対応の全体と提供方針は[ライセンスガイド](../LICENSING.ja.md)を参照してください。**生成されたコードはあなたの成果物**です。[LICENSE-NC.md](../LICENSE-NC.md) は生成物の利用・改変・配布・販売について、目的を問わず恒久的で取消不能な許諾を全員に与えており、クレジット表記も不要です。
