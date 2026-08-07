# MCP サーバ（quicker mcp）

*[English](mcp.md) | 日本語*

`quicker mcp` は、stdio トランスポート（標準入出力・JSON-RPC）の [Model Context Protocol](https://modelcontextprotocol.io) サーバを起動します。ER 図を編集し、コードを生成するツールを公開するため、外部の AI エージェント（Claude Code・Codex など）が自身のワークフローの一部として QuickER の図を構築・発展させられます。エージェントは `quicker mcp` を子プロセスとして起動し、標準入出力で通信します。

このサーバは**ステートレス**です。オプションを取らず、図をメモリに保持しません。ほぼすべてのツールが対象の図ファイルを `file` 引数で受け取りますが、そのファイルをどう扱うかはツールの種類によります。変更系ツールは 1 回の呼び出しで「読込 → 変更 → 保存」を完結させ、読み取り系ツール（`get_diagram_summary` / `list_queries`）は読み込むだけで保存せず、生成系ツール（`generate_csharp` / `generate_ddl`）は図を変更せず別の出力先へ書き出します（`create_diagram` は既存の図を読まずに新規ファイルを書き出します）。`get_generation_config_schema` だけは `file` も取りません。複数のエージェント（あるいは複数の図を扱う 1 エージェント）は、それぞれ異なる `file` パスを渡すだけです。

## セットアップ

### Claude Code

プロジェクトの `.mcp.json` にサーバを追加します。

```json
{
  "mcpServers": {
    "quicker": {
      "command": "quicker",
      "args": ["mcp"]
    }
  }
}
```

コマンドラインから登録することもできます。

```powershell
claude mcp add quicker -- quicker mcp
```

### その他の stdio クライアント

stdio トランスポートに対応した MCP クライアントであれば利用できます。コマンド `quicker` を引数 `mcp` 1 つで起動するよう設定してください（例えば Codex も、独自の MCP サーバ設定で同じコマンド／引数の組を受け取ります）。これには `quicker` コマンドが `PATH` に通っている必要があります（[CLI のインストール](cli.ja.md)を参照）。CLI が NuGet に公開されるまでは、一度ビルドし（`dotnet build QuickER.slnx`）、ビルド済みアセンブリを指すよう設定してください（`command: "dotnet"`、`args: ["<リポジトリ>/src/QuickER.Cli/bin/Debug/net10.0/QuickER.Cli.dll", "mcp"]`）。`dotnet run` は使わないでください（ビルド出力が stdout＝JSON-RPC プロトコルのチャネルへ混入します）。

## ツール

サーバは 18 個のツールを公開します。ER 図編集の 12 個・名前付きクエリの 3 個・コード生成の 3 個です。**`file` 引数はすべてのツールに必要**です（図 JSON のパス。GUI の保存形式＝`DiagramDocument`）。ただし唯一の情報系ツール `get_generation_config_schema` は例外で、引数を一切取りません。下表にはそれ以外の引数を挙げます。必須の引数には ✅ を付けています。

### ER 図編集

| ツール | 引数 | 説明 |
|---|---|---|
| `create_diagram` | `target_dbms` ✅（`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`） | 指定した対象 DBMS の新規の空図ファイルを作成する。ファイルが既に存在する場合は失敗する（このツールは新規作成専用）。親ディレクトリが存在しない場合も失敗する（ディレクトリは作成しない） |
| `get_diagram_summary` | — | 図のテーブル・カラム・一意制約・リレーションをテキストで一覧する。リレーションの各行には列ペアと制約名が付く（`Customer → Order (OneToMany, FK: (CustomerId → CustomerId)) [FK_Order_Customer]`）ため、複合外部キーは構成ペアが宣言順にすべて並ぶ |
| `add_entity` | `table_name` ✅, `description` | 新しいテーブルを追加する（カラムは作成しない） |
| `remove_entity` | `table_name` ✅ | テーブルを、接続するリレーションごと削除する |
| `add_column` | `table_name` ✅, `column_name` ✅, `data_type` ✅, `is_primary_key`, `is_nullable`, `description` | テーブルへカラムを追加する。主キー列は各テーブルにちょうど 1 つにする。ツールは 2 本目を拒否しないが、コード生成器が複合主キーに対応していない |
| `remove_column` | `table_name` ✅, `column_name` ✅ | テーブルからカラムを削除する。削除カラムを構成列に含む一意制約は制約ごと削除される（残りの列だけの制約へ黙って変質させない） |
| `set_entity_property` | `table_name` ✅, `new_table_name`, `memo`, `description` | テーブルの名前・メモ・説明を変更する（いずれか 1 つ以上を指定） |
| `set_column_property` | `table_name` ✅, `column_name` ✅, `description`, `data_type`, `is_nullable` | カラムの説明・データ型・NULL 許容を変更する（いずれか 1 つ以上を指定） |
| `add_relationship` | `source_table` ✅, `target_table` ✅, `relationship_type` ✅（`OneToOne` / `OneToMany` / `ManyToMany`）, `source_columns`, `target_columns` | 2 テーブル間に外部キーを追加する。`source_columns` と `target_columns` は列名の並行配列で、`source_columns` の *i* 番目（親の参照先列）を `target_columns` の *i* 番目（子の外部キー列）が参照する。2 つは同じ長さで、並びがそのまま外部キーの列順になる。各 1 要素なら通常の単一列外部キー、2 要素以上なら複合外部キーになる。片方だけの指定・長さ不一致・存在しない列名・同じ列の重複はエラー。**両方を省略した場合**は、親の主キー列すべてに対して列名から子列を推論してペア化する（GUI のリレーション作成フローと同じ既定解決）。推論できなかった親列はペアに含まれないので、必要なら GUI で補える。多対多は列ペアを持たない（中間テーブルを介する設計を表すため） |
| `remove_relationship` | `source_table` ✅, `target_table` ✅, `constraint_name` | 2 テーブル間のリレーションを削除する。1 回の呼び出しで削除されるのは 1 件のみ。同じ向きのテーブル対に複数のリレーションがある場合は `constraint_name` で対象を指定する。無指定のときは先頭を黙って削除せず、候補の制約名を挙げてエラーになる（制約名は `get_diagram_summary` でも確認できる） |
| `set_unique_constraint` | `table_name` ✅, `columns` ✅（列名の配列・宣言順）, `name` | テーブルへ UNIQUE 制約を定義する（upsert）。照合キーは (`table_name`, 列集合) で、同じ列集合の制約があれば再定義し（id は温存・名前と列順は今回の指定に従う）、無ければ追加する。列の順序・大文字小文字は照合にも制約の意味にも影響しない。`name` を省略すると DDL 生成時に `UQ_{テーブル}_{列…}` が合成される。主キーは自身の列の一意性を既に保証するため、主キー列と同じ構成の制約は通常不要 |
| `remove_unique_constraint` | `table_name` ✅, `columns` ✅ | UNIQUE 制約を削除する。対象は列集合で特定する（順序・大文字小文字は不問）。構成列が完全に一致する制約が無ければ失敗する |

### 名前付きクエリ

名前付きクエリは図に保存され、C# コード生成で Repository メソッドになります（[生成コードの使い方](code-generation.ja.md)を参照）。エンティティ・列は名前で指定し、ツール実行時に解決します。

| ツール | 引数 | 説明 |
|---|---|---|
| `set_query` | `table_name` ✅, `query_name` ✅, `returns` ✅（`list` / `single` / `count` / `scalar` / `projection`）, `description`, `scalar_type`, `implementation`（`dsl` / `sql` / `manual`・既定 `dsl`）, `condition`, `sql`, `parameters`, `order_by`, `paging`, `result_type_name`, `fields` | テーブルにクエリを定義／置換（upsert）する。(`table_name`, `query_name`) で照合し、既にクエリがあれば丸ごと置換（Id は温存）、なければ追加する。保存前に検証し、エラー時はファイルを変更しない |
| `list_queries` | — | 図のクエリをテーブル別に一覧する（戻り形・実装方式・条件／SQL の要約・パラメータ付き） |
| `remove_query` | `table_name` ✅, `query_name` ✅ | クエリを 1 件削除する。不在の場合は失敗する |

`set_query` のネスト引数:

- `scalar_type` — `returns` = `scalar` のとき必須。方言中立の型トークン（例: `decimal(12,2)`）。
- `condition` — 簡易 DSL の検索条件（比較・`AND`/`OR`/`NOT`・括弧・`IS [NOT] NULL`・`[NOT] LIKE`・`[NOT] IN`・`CONTAINS`/`STARTSWITH`/`ENDSWITH`）。`implementation` = `dsl` のとき使用（省略は無条件）。列名はテーブルの列を、`@名前` は宣言済みパラメータを指す。
- `sql` — 方言名（`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`）→ 生 SQL 文字列の辞書。`implementation` = `sql` のとき使用。
- `parameters` — `{ name` ✅ `, type, source_column, is_list }` の配列。`type`（方言中立トークン）と `source_column`（このテーブルの列。その生成型を使う）のどちらか一方を指定する。
- `order_by` — `{ column` ✅ `, descending }` の配列（`returns` が `list` / `single` / `projection` のときのみ有効。`single` では並び替えて先頭 1 件を取得する）。
- `paging` — 真偽値。真のとき `take` / `skip` 引数が追加される（`list` と `projection` に適用）。
- `result_type_name` / `fields` — `returns` = `projection` のとき必須。`fields` は `{ name` ✅ `, type, source_column, is_nullable }` の配列（`type` / `source_column` はどちらか一方）。

検証は「実行時に必ず失敗するもの」には厳格・「衛生上の警告」には寛容です。簡易 DSL の構文エラー・未知の列や未宣言の `@パラメータ`・生 SQL の未宣言パラメータ・構造の不整合（`scalar_type` / `fields` の欠落、パラメータの `type` / `source_column` の両方指定または両方欠落、`order_by` の誤用、未知の SQL 方言、`sql` の値が文字列でない）は保存を拒否します。未使用パラメータや複文の SQL は警告として報告し、保存は続行します。型トークンの内容はここでは検証せず、生成時に検証します。

### コード生成

| ツール | 引数 | 説明 |
|---|---|---|
| `generate_csharp` | `out_dir` ✅, `config`, `provider` | `quicker generate` と同一の経路で C# コード（Entity / EditModel / Mapper / Repository など）を出力先ディレクトリへ生成する。`config` は生成設定 JSON の中身ではなく、既存ファイルの**パス**（`quicker generate --config` と同じ意味。存在しないパスは失敗する。[CLI リファレンス](cli.ja.md#設定ファイルquickerjson)を参照）。`config` の全キーは `get_generation_config_schema` で取得できる |
| `generate_ddl` | `out_file` ✅, `provider` | DDL（CREATE TABLE / 外部キー）の SQL スクリプトを生成し、`.sql` ファイルへ書き出す |
| `get_generation_config_schema` | *(なし)* | 設定 JSON（`quicker.json`。`generate_csharp` の `config` はこのファイルへのパスを渡す）で有効な全キーを機械可読 JSON で返す。各キーの名前・型・既定値・分類・取り得る値・説明に加え、キー間のルールと例を含む。docs を参照せずに config を書けるようにするためのツール。`file` 引数を取らない唯一のツール |

ファイルを対象にする 2 つの生成ツール（`generate_csharp` / `generate_ddl`）では `provider` は省略可能です。省略時は図の対象 DBMS（図に無ければ `sqlserver`）を使用します。指定できる値は `create_diagram` の `target_dbms` と同じ 5 方言です。

## 典型フロー

図はファイル単位の呼び出しを 1 つずつ重ねて構築します。例えば SQLite 向けに顧客／注文スキーマを設計し、その DDL と C# コードを生成する場合は次のようになります。

1. `create_diagram` — `file` = `shop.json`、`target_dbms` = `sqlite`
2. `add_entity` — `table_name` = `Customer`。続いて `add_column` で `CustomerId`（`data_type` = `integer`、`is_primary_key` = true）と残りのカラムを追加
3. `add_entity` — `table_name` = `Order`。続いて `add_column` で `OrderId`（主キー）、`CustomerId` などを追加
4. `add_relationship` — `source_table` = `Customer`、`target_table` = `Order`、`relationship_type` = `OneToMany`、`source_columns` = `["CustomerId"]`、`target_columns` = `["CustomerId"]`
5. `generate_ddl` — `out_file` = `shop.sql`、あるいは `generate_csharp` — `out_dir` = `./Generated`

途中で現在のテーブル・リレーションを読み返したいときは、`get_diagram_summary` を呼びます。`generate_csharp` の `config` を書く前には、`get_generation_config_schema` を呼んで利用可能なキーと既定値を確認できます。

## 注意

- **GUI は外部変更に追従します。** GUI で開いている図をサーバが書き換えると、GUI がそれを検知して追従します。GUI 側に未保存の変更がなく、書かれた内容が読み込めるものであれば、自動でファイルを再読込し（ズーム・スクロール位置は維持されます）、控えめなステータス通知を出します。取り込めない内容——不正な JSON・`DiagramDocument` でないもの・新しいフォーマット版——は読み込まず、現状を維持したうえで通知します。確認ダイアログが出るのは「GUI 側に未保存の変更がある状態で外部がファイルを書いた」場合のみで、そのときは再読込するか（未保存の変更は破棄されます）このまま編集を続けるかを確認ダイアログで選べます。変更をレビュー可能に保つため、図ファイルは git で管理することを引き続き推奨します。
- **DiagramDocument の検証。** 編集系ツールは、存在しないファイル・`DiagramDocument` でない JSON（`Version` と `Schema` を持つオブジェクトが期待される）・このツールが対応するより新しいフォーマット版で保存された文書を拒否します（未知のデータを失わないため）。`get_diagram_summary` は、新しいフォーマットの文書でも警告付きで読み込みます。
- **サーバ指針（instructions）による設計既定。** サーバは初期化時に、既定の設計指針を MCP の instructions として返します：ユーザーの指示がない限りテーブル名はパスカルケース単数形（既存の図があればその様式に合わせる）・主キー列は各テーブルにちょうど 1 つ・外部キーの定義手順・一意制約は主キー以外の列（の組み合わせ）に対して定義するもの。instructions 対応クライアント（Claude Code など）はこれを自動でエージェントへ提示します。あくまで誘導であり強制ではないため、別の規則に従う図もツール上はそのまま扱えます。
- **レイアウトはサーバが書きません。** 新規作成したファイルはスキーマのみ（座標なし）で、GUI で開くと全テーブルが自動整列されます。既存ファイルへ追加したテーブルは、次に GUI で開いたときに空き領域へ配置されます。既存テーブルへ追加したカラムはそのテーブルのカード内に表示されるだけで、テーブル自体の位置は変わりません。

## 関連

- [CLI リファレンス（generate / scaffold / reverse・quicker.json）](cli.ja.md) — コード生成ツールが再利用する `quicker generate` の経路
- [AI チャットの設定](ai-chat.ja.md) — アプリ内蔵の AI チャット。現在開いている図を GUI 内で編集する（stdio でエージェントが駆動するこの外部 MCP サーバとは対照的）

## ライセンス注記

外部 MCP サーバは CLI（`QuickER.Cli`）の一部として提供され、ファイルベースのツール実行ホスト（`QuickER.Mcp.Tools`）にも同じく [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) **＋追加許諾**が適用されます。この追加許諾により、**現行リリースは商用利用を含め全員無料**です。ツール定義カタログ・stdio ホスト基盤（`QuickER.Mcp`）は MIT です。NC 対象は全 8 プロジェクトで、対応の全体と提供方針は[ライセンスガイド](../LICENSING.ja.md)を参照してください。**これらのツールが生成したコードはあなたの成果物**であり、目的を問わず恒久的・取消不能に許諾され、クレジット表記も不要です。
