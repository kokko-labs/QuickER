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

これらに加えて、**設定ファイル（quicker.json）の全キーは同名の kebab-case フラグとして指定でき**、設定ファイルより優先されます（優先順位: **CLI フラグ ＞ 設定ファイル ＞ 既定値**）。フラグ名はキーの機械的な kebab-case 変換で、例えば `rootNamespace` → `--root-namespace`、`generateRepositories` → `--generate-repositories`、`splitFilesByCategory` → `--split-files-by-category`、`outputPath` → `--output-path` です。bool キーは**三値**で、`--flag`（値なし）＝ `true`、`--flag false` ＝ `false`、未指定＝設定ファイルの値になります。各キーの意味は下記「設定ファイル」の表を参照してください（`--repository-dialects` はカンマ区切りの方言リストで、未指定時は `--provider` の方言から単一導出します）。

## quicker scaffold

データベースへ直接接続し、スキーマを取り込んでコードを生成します。オプションは `generate` と共通で、`--schema` の代わりに `--connection` を指定します。

```powershell
quicker scaffold --connection "Server=.;Database=Shop;Integrated Security=true;TrustServerCertificate=true" --out ./Generated --provider sqlserver
```

| オプション | 必須 | 説明 |
|---|:-:|---|
| `--connection <string>` | ✅ | 接続文字列（形式は `--provider` の DBMS に従う） |

そのほかのオプション（`--out` / `--config` / `--provider`、および設定キーと同名の kebab-case フラグ群）は `generate` と同じです。

## 設定ファイル（quicker.json）

`--config` で渡す JSON で、生成オプションをまとめて指定できます。これは GUI の設定保存ファイル（`codegen-settings.json`）と**同一スキーマ**です（GUI は camelCase で書き出しますが、CLI は大文字小文字を区別せず解釈するためそのまま渡せます）。**下表の各キーは同名の kebab-case フラグとして CLI からも指定でき、設定ファイルより優先されます**（優先順位: CLI フラグ ＞ 設定ファイル ＞ 既定値。bool は `--flag` / `--flag false` の三値）。

> **破壊的変更（v-next）**: `GenerateRepositories` の既定が `true` → `false` になりました。以前はキーを省略すると Repository が生成されましたが、**現在は DB アクセスコードを生成するには `GenerateRepositories: true`（または `GenerateEfCore: true`）の明示指定が必要**です（GUI の DB アクセス「なし」既定と揃えるため）。

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
| `RootNamespace`（`Generated`） | 生成コードのルート名前空間 |
| `GenerateEditModels` / `GenerateMappers`（ともに `true`） | 各カテゴリの生成有無。**Entity クラスは常時生成**され、専用キーはない |
| `GenerateValueObjects`（`false`） | 列ごとの値オブジェクト型（`CustomerIdValue` など）を生成する（[生成コードの使い方](code-generation.md#値オブジェクトgeneratevalueobjects) 参照） |
| `UseGuidKeyForStringPrimaryKey`（`false`） | string 主キーを GUID 値オブジェクトにする（`GenerateValueObjects` が有効な場合のみ） |
| `GenerateRepositories`（`false`） | QuickER 版 Repository（軽量ミニ ORM）を生成する。**既定では DB アクセスコードを生成しない**（GUI と同じ既定） |
| `RepositoryDialects`（未指定） | QuickER 版 Repository のマルチターゲット方言リスト（例 `["sqlserver", "sqlite"]`）。未指定時は CLI の `--provider` / `--repository-dialects` から設定される |
| `ExcludeUnboundedBinaryColumns`（`false`） | 無制限バイナリ列をQuickER 版 Repository の SELECT / UPDATE から除外する（CLI の `--exclude-unbounded-binary-columns` に対応。[生成コードの使い方](code-generation.md#無制限バイナリ列の除外excludeunboundedbinarycolumns) 参照） |
| `GenerateEfCore`（`false`） | EF Core 用の `QuickErDbContext` ＋ EF Core 版 Repository 実装を生成する。マルチターゲット（実効方言 2 つ以上）とは併用不可 |
| `GenerateInMemoryRepositories`（`false`） | テスト用のインメモリ Repository 実装を生成する（`UseRuntimePackages` とは併用不可） |
| `GenerateRemoteContracts`（`false`） | リモート操作用インターフェイス `I{Entity}RemoteRepository` を追加生成する（CLI の `--generate-remote-contracts` に対応。Repository / EF Core 契約が前提。[生成コードの使い方](code-generation.md) 参照） |
| `GenerateRemoteServices`（`false`） | リモート面の HTTP クライアント／サーバー実装を生成する（`GenerateRemoteContracts` を自動的に含意。CLI の `--generate-remote-services` に対応。[生成コードの使い方](code-generation.md) 参照） |
| `UseRuntimePackages`（`false`） | ランタイム固定コードを出力せず NuGet パッケージ参照で賄う（[生成コードの使い方](code-generation.md) 参照） |
| `GenerateApiDocs`（`false`） | API リファレンス Markdown（`{ベース名}.g.md`）を追加出力する（CLI の `--generate-api-docs` に対応。[生成コードの使い方](code-generation.md) 参照） |
| `IncludeDataAnnotations`（`true`） | `[Required]` / `[MaxLength]` 等の DataAnnotations と、DB 定義メタ属性（`[DbTableMeta]` / `[DbColumnMeta]`）を付与する |
| `IncludeJsonIgnoreOnParentNavigation`（`true`） | 親参照ナビゲーションへ `[JsonIgnore]` を付与する（JSON シリアライズ時の循環参照対策） |
| `OutputPath`（`QuickEREntities.g.cs` 相当） | 出力先パス。CLI（`--config` / `--output-path`）はそのファイル名部分のみを単一ファイル出力のファイル名として使う（出力先ディレクトリは常に `--out`）。GUI では出力先のフルパス（非分割時はファイル・分割時はフォルダ）が入ることがあるが、CLI は同じ規則で解釈する |

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
`EcOrder.g.md` も同梱出力されます（いずれもチェックイン済み・ドリフト検知の対象）。

## ライセンス注記

CLI（`QuickER.Cli`）とコード生成エンジンには [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) が適用されます。**現在は商用利用を含め全員無料**です。将来の提供方針（商用利用のみ有償化の可能性・個人/非商用は永続無料・基本生成は永続無料・有償化時は事前告知と移行期間）は [README の「ライセンス」節](../README.md#ライセンス)を参照してください。**生成されたコードはあなたの成果物**であり、ライセンスによる制限はありません。
