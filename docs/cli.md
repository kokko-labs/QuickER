# CLI リファレンス（quicker）

QuickER の CLI は 2 つのサブコマンドを提供します。

| コマンド | 入力 | 出力 |
|---|---|---|
| `quicker generate` | ER 図 JSON（GUI の保存形式） | C# コード |
| `quicker scaffold` | データベース接続文字列（スキーマを直接取込） | C# コード |

CLI の表示言語は OS の言語設定に従います（日本語 / 英語）。

NuGet 公開後は `dotnet tool install --global QuickER.Cli` でインストールできます。未公開の間はソースから実行してください:

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
| `--namespace <name>` | | 生成コードのルート名前空間（設定ファイルを上書き） |
| `--split` | | カテゴリごとに別ファイル・別名前空間で出力する（設定ファイルを上書き） |
| `--repository-dialects <list>` | | 自作 Repository を同時生成する方言（カンマ区切り。例 `sqlserver,sqlite`）。未指定時は `--provider` から単一導出する |
| `--runtime-packages` | | ランタイム（固定コード）を出力せず、NuGet パッケージ `QuickER.Runtime.*` への参照で賄う |
| `--remote-contracts` | | リモート操作用インターフェイス `I{Entity}RemoteRepository`（CRUD・保存・名前付きクエリのみ）を追加生成する。`I{Entity}Repository` は全メソッドを持ったままこれを継承する（既定 OFF・設定ファイルを上書き） |
| `--remote-services` | | リモート面の HTTP クライアント実装（`Http{Entity}RemoteRepository`）を本体へ同梱し、ASP.NET Core サーバー実装（`MapGeneratedRemoteEndpoints`）を `{ベース名}.RemoteServer.g.cs` へ追加出力する（`--remote-contracts` を自動的に含意。既定 OFF・設定ファイルを上書き。[生成コードの使い方](code-generation.md) 参照） |
| `--api-docs` | | API リファレンス Markdown（`{ベース名}.g.md`）を 1 つ追加出力する（既定 OFF・設定ファイルを上書き） |
| `--exclude-unbounded-binary` | | 無制限バイナリ列（`varbinary(max)`・長さ宣言なし BLOB）を自作 Repository の SELECT / UPDATE から除外する（INSERT は全列のまま。既定 OFF・設定ファイルを上書き。[生成コードの使い方](code-generation.md#無制限バイナリ列の除外excludeunboundedbinarycolumns) 参照） |

## quicker scaffold

データベースへ直接接続し、スキーマを取り込んでコードを生成します。オプションは `generate` と共通で、`--schema` の代わりに `--connection` を指定します。

```powershell
quicker scaffold --connection "Server=.;Database=Shop;Integrated Security=true;TrustServerCertificate=true" --out ./Generated --provider sqlserver
```

| オプション | 必須 | 説明 |
|---|:-:|---|
| `--connection <string>` | ✅ | 接続文字列（形式は `--provider` の DBMS に従う） |

そのほかのオプション（`--out` / `--config` / `--provider` / `--namespace` / `--split` / `--repository-dialects` / `--runtime-packages` / `--remote-contracts` / `--remote-services` / `--api-docs` / `--exclude-unbounded-binary`）は `generate` と同じです。

## 設定ファイル（quicker.json）

`--config` で渡す JSON で、生成オプションをまとめて指定できます。キー名は大文字小文字を区別しません。CLI フラグ（`--namespace` / `--split` / `--repository-dialects` / `--runtime-packages` / `--remote-contracts` / `--remote-services` / `--api-docs` / `--exclude-unbounded-binary`）は設定ファイルより優先されます。

```json
{
  "NamespaceName": "MyApp.Generated",
  "OutputFileName": "MyAppEntities.g.cs",
  "GenerateEntityClasses": true,
  "GenerateEditModels": true,
  "GenerateMappers": true,
  "GenerateRepositories": true,
  "GenerateEfCore": false,
  "GenerateValueObjects": false,
  "IncludeDataAnnotations": true,
  "SplitFilesByCategory": false
}
```

主なキー（かっこ内は既定値）:

| キー | 説明 |
|---|---|
| `NamespaceName`（`Generated`） | 生成コードのルート名前空間 |
| `OutputFileName`（`QuickEREntities.g.cs`） | 単一ファイル出力時のファイル名 |
| `GenerateEntityClasses` / `GenerateEditModels` / `GenerateMappers`（すべて `true`） | 各カテゴリの生成有無 |
| `GenerateRepositories`（`true`） | 自作 Repository（軽量ミニ ORM）を生成する |
| `RepositoryDialects`（未指定） | 自作 Repository のマルチターゲット方言リスト（例 `["sqlserver", "sqlite"]`）。通常は CLI の `--provider` / `--repository-dialects` から設定されるため、設定ファイルで指定する必要はない |
| `GenerateEfCore`（`false`） | EF Core 用の `QuickErDbContext` ＋ EF 版 Repository 実装を生成する。マルチターゲット（実効方言 2 つ以上）とは併用不可 |
| `GenerateInMemoryRepositories`（`false`） | テスト用のインメモリ Repository 実装を生成する（`--runtime-packages` とは併用不可） |
| `GenerateValueObjects`（`false`） | 列ごとの値オブジェクト型（`CustomerIdValue` など）を生成する |
| `UseGuidKeyForStringPrimaryKey`（`false`） | string 主キーを GUID 値オブジェクトにする（`GenerateValueObjects` が有効な場合のみ） |
| `IncludeDataAnnotations`（`true`） | `[Required]` / `[MaxLength]` 等の DataAnnotations と、DB 定義メタ属性（`[DbTableMeta]` / `[DbColumnMeta]`）を付与する |
| `SplitFilesByCategory`（`false`） | カテゴリごとに別ファイル・別名前空間で出力する。`EntityNamespace` / `RepositoryNamespace` などで名前空間を個別指定できる |
| `UseRuntimePackages`（`false`） | ランタイム固定コードを出力せず NuGet パッケージ参照で賄う（[生成コードの使い方](code-generation.md) 参照） |
| `GenerateRemoteContracts`（`false`） | リモート操作用インターフェイス `I{Entity}RemoteRepository` を追加生成する（CLI の `--remote-contracts` に対応。[生成コードの使い方](code-generation.md) 参照） |
| `GenerateRemoteServices`（`false`） | リモート面の HTTP クライアント／サーバー実装を生成する（`GenerateRemoteContracts` を自動的に含意。CLI の `--remote-services` に対応。[生成コードの使い方](code-generation.md) 参照） |
| `GenerateApiDocs`（`false`） | API リファレンス Markdown（`{ベース名}.g.md`）を追加出力する（CLI の `--api-docs` に対応。[生成コードの使い方](code-generation.md) 参照） |
| `ExcludeUnboundedBinaryColumns`（`false`） | 無制限バイナリ列を自作 Repository の SELECT / UPDATE から除外する（CLI の `--exclude-unbounded-binary` に対応。[生成コードの使い方](code-generation.md#無制限バイナリ列の除外excludeunboundedbinarycolumns) 参照） |

## 実行例 — リポジトリ同梱サンプルの再生成

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json `
  --api-docs
```

`--api-docs` により、生成コード `EcOrder.g.cs` と同じベース名の API リファレンス Markdown
`EcOrder.g.md` も同梱出力されます（いずれもチェックイン済み・ドリフト検知の対象）。

## ライセンス注記

CLI（`QuickER.Cli`）とコード生成エンジンには [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) が適用されます。**現在は商用利用を含め全員無料**です。将来の提供方針（商用利用のみ有償化の可能性・個人/非商用は永続無料・基本生成は永続無料・有償化時は事前告知と移行期間）は [README の「ライセンス」節](../README.md#ライセンス)を参照してください。**生成されたコードはあなたの成果物**であり、ライセンスによる制限はありません。
