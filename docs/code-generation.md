# 生成コードの使い方

QuickER が生成する C# コードの構成と、データアクセス層（Repository (QuickER) / EF Core）の使い方をまとめます。生成方法は [CLI リファレンス](cli.md)、動く実例は [samples/ec-order](../samples/ec-order) を参照してください。

## 生成されるもの

| カテゴリ | 内容 |
|---|---|
| Entity | テーブルに対応する POCO。UI フレームワーク非依存（CommunityToolkit 等に依存しない）。`RowState`（Unchanged / Added / Updated / Removed）と `MarkAdded()` などの状態遷移メソッド、ナビゲーションプロパティ（親参照・子コレクション）を持つ |
| EditModel | 画面編集用のモデルと Entity との相互変換 |
| Mapper | Entity ⇄ EditModel の変換器 |
| Repository 共通契約 | `IRepository<TEntity, TKey>` と各エンティティのインターフェイス（`ICustomerRepository` など）。Repository (QuickER) と EF Core が同じ契約を実装する |
| Repository (QuickER) 実装 | 方言別（SQL Server / SQLite）の軽量実装＋ DI 登録拡張 |
| EF Core 実装 | `QuickErDbContext`（Fluent 構成込み）＋ EF 版 Repository ＋ DI 登録拡張 |
| ランタイム | 上記が使う固定コード（既定でインライン出力。パッケージ参照モードあり） |

Entity には既定で DataAnnotations と **DB 定義メタ属性**（`[DbTableMeta]` / `[DbColumnMeta]`）が付き、方言中立の型トークン（`string(50)` / `decimal(10,2)` など）と説明が刻まれます。生成コードは DB 定義の自己記述ドキュメントとしても機能します。

> **前提**: Repository の生成は単一主キー・アプリ側採番が対象です（複合キー・DB 自動採番のテーブルは Entity / EditModel のみ利用できます）。

## Repository (QuickER)

依存最小（ADO のみ）の軽量 Repository です。対象方言は SQL Server（`FOR JSON` ベース）と SQLite（プレーン SELECT ＋ マルチクエリ）。

```csharp
// DI 登録（生成される拡張メソッド）
var provider = new ServiceCollection()
    .AddGeneratedRepositories(connectionString)
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRepository>();
```

### 基本操作

```csharp
await customers.InsertAsync(new CustomerEntity { CustomerId = 1, Name = "山田" });
var one  = await customers.GetByIdAsync(1);
var all  = await customers.GetAllAsync();
one!.Name = "山田（改名）";
await customers.UpdateAsync(one);
await customers.DeleteAsync(1);
await customers.BulkInsertAsync(manyCustomers);   // 一括挿入
```

### クエリ（式木 → SQL 変換）

```csharp
var result = await customers.Query()
    .Where(c => c.Name.Contains("山田") && c.Balance >= 1000m)   // LIKE はワイルドカードを自動エスケープ
    .OrderBy(c => c.CustomerId)
    .Skip(20).Take(10)                                           // ページング
    .Include(c => c.Orders)                                      // 親→子コレクション
        .ThenInclude(o => o.OrderLines)                          // 再帰的にロード
    .ToListAsync();
```

対応: 等値・比較・`&&`/`||`・`Contains`/`StartsWith`/`EndsWith`（LIKE）・リストの `Contains`（IN）・日付部品（`Year` など）・`string.IsNullOrEmpty`・値オブジェクト比較。**射影（Select）・GroupBy・Join・算術式は未対応**です（実行時例外。生 SQL か EF Core で回避してください）。

### グラフ保存（親子まとめて 1 回で保存）

```csharp
var order = new OrderEntity { OrderId = 1000, CustomerId = 1 };
order.MarkAdded();
var line = new OrderLineEntity { OrderLineId = 5000, OrderId = 1000, ProductId = 100, Quantity = 2 };
line.MarkAdded();
order.OrderLines.Add(line);

var affected = await orders.SaveAsync(order);   // RowState に従い INSERT / UPDATE / DELETE を 1 トランザクションで実行
```

### 生 SQL の逃げ道

式木で表現できないクエリはいつでも生 SQL に落とせます（パラメータは匿名オブジェクト）。

```csharp
// 厳密全列マップ（Entity へ復元）
var rows = await customers.QueryBySqlAsync(
    "SELECT * FROM customers WHERE balance >= @min", new { min = 1000m });

// 射影・単一値（エンティティ非依存の ISqlExecutor でも可）
var names = await executor.QueryProjectionBySqlAsync<string>("SELECT name FROM customers", null);
var total = await orders.ExecuteScalarSqlAsync<decimal>(
    "SELECT SUM(quantity * unit_price) FROM order_lines WHERE order_id = @id", new { id = 1000 });

// 更新系（影響行数を返す）
var affected = await customers.ExecuteSqlAsync("UPDATE customers SET balance = 0", null);
```

### 楽観排他

SQL Server 方言では `rowversion` 列を持つテーブルに対して楽観排他が有効になり、競合時は `SaveConflictException` が送出されます。

## EF Core（GenerateEfCore）

既存 Entity をそのまま EF に載せる方言非依存の `QuickErDbContext` と、**同一 Repository インターフェイスの EF 実装**を生成します。マイグレーションは範囲外で、スキーマ作成は DDL 生成の責務です（EF は既存スキーマへの接続専用）。

```csharp
// DI 登録 1 行の差し替えで Repository (QuickER) と交換できる
services.AddGeneratedEfCoreRepositories(options => options.UseSqlServer(connectionString));
// SQLite / PostgreSQL / MySQL / Oracle は対応する EF Core プロバイダの Use* を指定する
```

- 保存は `TrackGraph` による切断グラフ保存（`RowState` を EF の状態へ変換）
- 楽観排他の競合は EF の例外を `SaveConflictException` へ変換（契約を統一）
- 生 SQL 系 API も完全パリティ

**Repository (QuickER) との併用生成**（両方 ON）はパリティ検証用で、CLI / 設定ファイルでのみ指定できます。GUI は排他選択です。また EF Core とマルチターゲット Repository（下記）は併用できません（診断エラー）。

## マルチターゲット Repository（sqlserver + sqlite）

`--repository-dialects sqlserver,sqlite` を指定すると、中立契約を 1 回・方言別実装を `.SqlServer` / `.Sqlite` サブ名前空間へ出力し、keyed DI で同一プロセスから複数 DB へ書き分けられます。

```csharp
services.AddGeneratedSqlServerRepositories(serviceKey: "primary", sqlServerConn);
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);

// 解決側は同一の契約型を keyed で選ぶ
var primary = provider.GetRequiredKeyedService<ICustomerRepository>("primary");
var local   = provider.GetRequiredKeyedService<ICustomerRepository>("local");
```

## テスト用インメモリ Repository（GenerateInMemoryRepositories）

DB なしでユニットテストするためのインメモリ実装を追加生成できます。同一契約を実装し、サポート外の操作は実 DB の Repository へ切り替える案内付きの `NotSupportedException` を送出します。

## ランタイムパッケージ参照モード（--runtime-packages）

既定では、生成コードはランタイム（スキーマ非依存の固定コード）込みのインライン出力で自己完結します。`--runtime-packages` を指定すると固定コードを出力せず、次の NuGet パッケージへの参照で賄います（生成ヘッダと CLI 出力に必要な PackageReference が案内されます。csproj には手動で追加してください）:

| パッケージ | 内容 | 依存 |
|---|---|---|
| `QuickER.Runtime` | 共通基盤・方言中立の契約 | なし |
| `QuickER.Runtime.SqlServer` | 自作 SQL Server 方言エンジン | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | 自作 SQLite 方言エンジン | Microsoft.Data.Sqlite |
| `QuickER.Runtime.EntityFrameworkCore` | EF 共通部品 | Microsoft.EntityFrameworkCore.Relational |

パッケージ版とツール版はロックステップ（同一バージョン）で公開され、同一メジャー内で互換です。DI 登録拡張・`QuickErDbContext`・エンティティ別実装などのスキーマ依存物は、本モードでも常に生成側に出力されます。

## API リファレンス（.g.md）

生成コードと同名ベースの API リファレンス Markdown を追加出力できます。GUI の生成ダイアログの「API リファレンス (.g.md) を出力する」チェック、または CLI の `--api-docs` フラグで有効化します（**既定 OFF**）。DB アクセスの選択（なし / Repository (QuickER) / EF Core）とは独立して、常に選択できます。

有効化すると、`.g.cs` と同じベース名の `.g.md` が 1 つ出力されます（例: `EcOrder.g.cs` → `EcOrder.g.md`）。内容は次のとおりです。

- エンティティ一覧と、各エンティティのプロパティ表（DB 型トークン込み。`string(50)` / `decimal(10,2)` など）
- Repository 契約（`IRepository<TEntity, TKey>` と各エンティティのインターフェイス）
- DI 登録・CRUD・クエリの使い方例
- 生成ファイル構成表

`.g.md` は自動生成ファイルです。再生成で上書きされるため、直接編集しないでください。

## ライセンス注記

コード生成エンジン（`QuickER.CodeGen.CSharp` / `CodeGen.UI` / `Cli`）には [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) が適用されます。**現在は商用利用を含め全員無料**です。将来の提供方針（DB アクセス生成＝Repository / EF Core / マルチターゲットについて商用利用のみ有償化の可能性・個人/非商用は永続無料・基本生成＝Entity / EditModel / Mapper は永続無料・有償化時は事前告知と移行期間）は [README の「ライセンス」節](../README.md#ライセンス)を参照してください。**生成されたコードとランタイムパッケージ（MIT）はあなたの成果物側**であり、ライセンスによる制限はありません。
