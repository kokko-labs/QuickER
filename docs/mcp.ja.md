# MCP サーバ（quicker mcp）

*日本語 | [English](mcp.md)*

`quicker mcp` は、stdio トランスポート（標準入出力・JSON-RPC）の [Model Context Protocol](https://modelcontextprotocol.io) サーバを起動します。ER 図を編集し、コードを生成するツールを公開するため、外部の AI エージェント（Claude Code・Codex など）が自身のワークフローの一部として QuickER の図を構築・発展させられます。エージェントは `quicker mcp` を子プロセスとして起動し、標準入出力で通信します。

このサーバは**ステートレス**です。オプションを取らず、図をメモリに保持しません。各ツールは対象の図ファイルを `file` 引数で受け取り、呼び出しごとにそのファイルへの「読込 → 変更 → 保存」を完結させます。複数のエージェント（あるいは複数の図を扱う 1 エージェント）は、それぞれ異なる `file` パスを渡すだけです。

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

サーバは 12 個のツールを公開します。ER 図編集の 10 個と、コード生成の 2 個です。**すべてのツールに `file` 引数が必要**です（図 JSON のパス。GUI の保存形式＝`DiagramDocument`）。下表にはそれ以外の引数を挙げます。必須の引数には ✅ を付けています。

### ER 図編集

| ツール | 引数 | 説明 |
|---|---|---|
| `create_diagram` | `target_dbms` ✅（`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`） | 指定した対象 DBMS の新規の空図ファイルを作成する。ファイルが既に存在する場合は失敗する（このツールは新規作成専用） |
| `get_diagram_summary` | — | 図のテーブル・カラム・リレーションをテキストで一覧する |
| `add_entity` | `table_name` ✅, `description` | 新しいテーブルを追加する（カラムは作成しない） |
| `remove_entity` | `table_name` ✅ | テーブルを、接続するリレーションごと削除する |
| `add_column` | `table_name` ✅, `column_name` ✅, `data_type` ✅, `is_primary_key`, `is_nullable`, `description` | テーブルへカラムを追加する。各テーブルの主キー列はちょうど 1 つ（複合主キーは非対応） |
| `remove_column` | `table_name` ✅, `column_name` ✅ | テーブルからカラムを削除する |
| `set_entity_property` | `table_name` ✅, `new_table_name`, `memo`, `description` | テーブルの名前・メモ・説明を変更する |
| `set_column_property` | `table_name` ✅, `column_name` ✅, `description`, `data_type`, `is_nullable` | カラムの説明・データ型・NULL 許容を変更する（いずれか 1 つ以上を指定） |
| `add_relationship` | `source_table` ✅, `target_table` ✅, `relationship_type` ✅（`OneToOne` / `OneToMany` / `ManyToMany`）, `source_column`, `target_column` | 2 テーブル間に外部キーを追加する。リレーションは 1 列を 1 列で参照する（複合外部キーは非対応） |
| `remove_relationship` | `source_table` ✅, `target_table` ✅ | 2 テーブル間のリレーションを削除する |

### コード生成

| ツール | 引数 | 説明 |
|---|---|---|
| `generate_csharp` | `out_dir` ✅, `config`, `provider` | `quicker generate` と同一の経路で C# コード（Entity / EditModel / Mapper / Repository など）を出力先ディレクトリへ生成する。`config` は生成設定 JSON（`quicker generate --config` と同じ意味。[CLI リファレンス](cli.ja.md#設定ファイルquickerjson)を参照） |
| `generate_ddl` | `out_file` ✅, `provider` | DDL（CREATE TABLE / 外部キー）の SQL スクリプトを生成し、`.sql` ファイルへ書き出す |

どちらの生成ツールでも `provider` は省略可能です。省略時は図の対象 DBMS（図に無ければ `sqlserver`）を使用します。指定できる値は `create_diagram` の `target_dbms` と同じ 5 方言です。

## 典型フロー

図はファイル単位の呼び出しを 1 つずつ重ねて構築します。例えば SQLite 向けに顧客／注文スキーマを設計し、その DDL と C# コードを生成する場合は次のようになります。

1. `create_diagram` — `file` = `shop.json`、`target_dbms` = `sqlite`
2. `add_entity` — `table_name` = `customers`。続いて `add_column` で `customer_id`（`data_type` = `integer`、`is_primary_key` = true）と残りのカラムを追加
3. `add_entity` — `table_name` = `orders`。続いて `add_column` で `order_id`（主キー）、`customer_id` などを追加
4. `add_relationship` — `source_table` = `customers`、`target_table` = `orders`、`relationship_type` = `OneToMany`、`source_column` = `customer_id`、`target_column` = `customer_id`
5. `generate_ddl` — `out_file` = `shop.sql`、あるいは `generate_csharp` — `out_dir` = `./Generated`

途中で現在のテーブル・リレーションを読み返したいときは、`get_diagram_summary` を呼びます。

## 注意

- **GUI で開いている図を MCP 経由で同時編集しないでください。** GUI とサーバはどちらもファイル全体を保存するため、同時に編集すると互いに上書きします。変更をレビュー可能に保つため、図ファイルは git で管理することを推奨します。
- **DiagramDocument の検証。** 編集系ツールは、存在しないファイル・`DiagramDocument` でない JSON（`Version` と `Schema` を持つオブジェクトが期待される）・このツールが対応するより新しいフォーマット版で保存された文書を拒否します（未知のデータを失わないため）。`get_diagram_summary` は、新しいフォーマットの文書でも警告付きで読み込みます。
- **レイアウトはサーバが書きません。** 新規作成したファイルはスキーマのみ（座標なし）で、GUI で開くと全テーブルが自動整列されます。既存ファイルへ追加したカラム・テーブルは、次に GUI で開いたときに空き領域へ配置されます。

## 関連

- [CLI リファレンス（generate / scaffold / reverse・quicker.json）](cli.ja.md) — コード生成ツールが再利用する `quicker generate` の経路
- [AI チャットの設定](ai-chat.ja.md) — アプリ内蔵の AI チャット。現在開いている図を GUI 内で編集する（stdio でエージェントが駆動するこの外部 MCP サーバとは対照的）
