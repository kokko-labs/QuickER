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

### 無制限バイナリ列の除外（ExcludeUnboundedBinaryColumns）

巨大な BLOB を一覧取得・更新のたびに往復させない（メモリを保護する）ためのオプションです（既定 OFF。CLI `--exclude-unbounded-binary` / GUI「無制限バイナリ列を取得しない」チェックボックス / quicker.json の `ExcludeUnboundedBinaryColumns`）。ON にすると、**サイズ上限のないバイナリ列**の Entity プロパティへマーカー属性 `[UnboundedBinaryColumn]` が付与され、Repository (QuickER) の SELECT / UPDATE から当該列が除外されます。生成時には除外した列の一覧が Info 診断（CLI 出力・GUI の生成結果ダイアログ）で通知されます。

判定は列の宣言型で行います（`rowversion` や `binary(n)` / `varbinary(n)` など長さ宣言のある型は対象外）:

| 方言 | 除外対象 | 対象外（有界） |
|---|---|---|
| SQL Server | `varbinary(max)` / `image` | `binary(n)` / `varbinary(n)` / `rowversion` |
| SQLite | 長さ宣言なし `BLOB` | `BLOB(n)` |
| PostgreSQL | `bytea` | — |
| MySQL | `BLOB` / `MEDIUMBLOB` / `LONGBLOB` | `TINYBLOB` / `BINARY(n)` / `VARBINARY(n)` |
| Oracle | `BLOB` / `LONG RAW` | `RAW(n)` |

挙動の要点:

- **SELECT から除外**: `GetByIdAsync` / `GetAllAsync` / `Query()` の結果で除外列は常に `null`（DB から読み出さない）
- **UPDATE から除外**: 更新 SQL の SET 句に除外列は含まれない。除外列に値を設定したまま `UpdateAsync` / `SaveAsync` を実行すると**実行時例外**になる（黙ってデータを取りこぼさない）
- **INSERT / BulkInsert は全列のまま**: 初回書き込みは通常どおり値を渡せる
- **名前付きクエリの射影**が除外列を参照する場合は取得される（射影は明示的な列選択のため）
- **生 SQL** で明示的に SELECT すれば取得できる（下記の運用例）
- **EF Core モード（`DbSet` 経由のクエリ / `SaveChanges`）には適用されない**（EF の列選択は EF の責務）
- インメモリ Repository（`GenerateInMemoryRepositories`）は実 DB とパリティ（同じ除外挙動）

除外列の読み書きは生 SQL で行います:

```csharp
// 除外列（画像など）を明示的に読む
var payload = await documents.QueryProjectionBySqlAsync<byte[]>(
    "SELECT payload FROM documents WHERE document_id = @id", new { id = 1 });

// 除外列を更新する（UPDATE の SET 句には自動で含まれないため生 SQL で書く）
await documents.ExecuteSqlAsync(
    "UPDATE documents SET payload = @payload WHERE document_id = @id",
    new { payload = bytes, id = 1 });
```

#### 読み取りオプトイン `WithUnboundedBinary()`

除外を有効にした図でも、**この呼び出しに限り**除外列を含めてエンティティを取得したい場合は、`Query()` チェーンに `WithUnboundedBinary()` を挟みます（除外列が無ければ何もしない no-op のため、API は常に存在します）。生 SQL の射影を書かずに、通常のエンティティ（`RowState = Unchanged`・除外列も実データでマップ済み）を取得できます。

```csharp
// GetById 相当を、除外列（payload / thumb）込みで取得する
var doc = await documents
    .Query()
    .Where(d => d.DocumentId == 1)
    .WithUnboundedBinary()
    .FirstOrDefaultAsync();
```

制約と挙動:

- **`Include` とは併用できません**（終端メソッド実行時に `InvalidOperationException`）。無制限バイナリ列が必要な場合は `Include` なしの別クエリで取得してください。これは SQL Server の `Include` 経路が FOR JSON＝Base64 経由で巨大 BLOB のメモリ膨張（ピーク 5〜6 倍）を招くためで、「巨大 BLOB を扱う」目的に常に正しいメモリ特性を保証します（SQL Server では FOR JSON を使わず**プレーン SELECT** で取得します）。
- 効果があるのは `ToListAsync` / `FirstOrDefaultAsync` のみです（件数・存在確認・射影 `ToProjectionListAsync` には影響しません）。
- 取得したエンティティは正当なエンティティですが、除外列が UPDATE 対象外である点は変わりません。そのまま `UpdateAsync` すると既存ガードで例外になります（除外列の更新は上記の生 SQL `ExecuteSqlAsync` で行ってください）。
- EF Core モードでは EF が元々全列を読むため no-op です（`Include` 併用エラーだけはパリティで同様に送出します）。

#### Stream アクセサ `Read/Write{Column}Async`

除外オプションを有効（かつ Repository (QuickER) を生成）にすると、除外列ごとに **ストリーミング**の読み書きメソッドが `I{Entity}Repository`（全機能面）へ追加生成されます。`byte[]` の一括読み込みを避け、**O(チャンク)＝blob 全量をメモリに載せずに** DB⇔ストリーム（またはファイル）を転送できます。GB 級のバイナリを扱う唯一正しいメモリ特性を持つ手段です。

```csharp
// documents.payload（除外列）に対して生成される例
Task<bool> ReadPayloadAsync(int id, Stream destination, CancellationToken ct = default);
Task<bool> WritePayloadAsync(int id, Stream? source, long? length = null, CancellationToken ct = default);
// ファイル糖衣（拡張メソッド・Stream 版へ委譲）
Task<bool> ReadPayloadToFileAsync(int id, string path, CancellationToken ct = default);
Task<bool> WritePayloadFromFileAsync(int id, string path, CancellationToken ct = default);
```

意味論:

- **戻り値**: `Read` は宛先へ書いたら `true`（空 blob も `true`）、行なし・列 NULL は `false`（宛先へ何も書きません）。`Write` は更新できたら `true`、行なしは `false`。既存の `UpdateAsync` の bool 規約に揃えています。
- **`Write(id, null)`** は列を `NULL` に設定します（除外列を「未設定」へ戻す手段）。
- **長さ**: `source` が `CanSeek` なら自動（`Length - Position`）、そうでなければ `length` 引数が必須です（欠落は `ArgumentException`）。SQLite の `zeroblob` が書き込み前に長さを要求するためで、契約は方言中立に統一しています。
- **楽観排他（rowversion 等）はスコープ外**です（生 SQL と同格の直接列操作）。
- **INSERT 専用メソッドはありません**。新規行は「INSERT（blob は `null` または空）→ `Write{Column}Async` で本体を流し込む」の 2 段で書きます。
- **EF Core モードでは使用できません**（`NotSupportedException`）。EF は方言非依存設計のため方言固有のストリーミングを持てません。Repository (QuickER) を使うか、`partial` クラスで実装してください（`GenerateEfCore` と Repository (QuickER) を併用する構成では、EF 版実装のみ例外になります）。
- **リモート面（`I{Entity}RemoteRepository`）には載りません**（HTTP クライアント・サーバーも無変更）。ネットワーク境界を越えるストリーミングは将来対応です。

`WithUnboundedBinary()` との使い分け:

| | `WithUnboundedBinary()` | Stream アクセサ |
|---|---|---|
| 単位 | エンティティ形（複数列・複数行・Include なし） | 列 1 本の読み書き |
| メモリ | 中規模（`byte[]` で一括） | 無制限（O(チャンク)） |
| 用途 | 除外列込みのエンティティが一時的に欲しい | 巨大 blob を DB⇔ファイル/ストリームで転送 |
| 書き込み | 不可（取得のみ・更新は生 SQL） | `Write{Column}Async` で列単位に書ける |

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

## リモート対応インターフェイス（--remote-contracts）

`I{Entity}Repository` は CRUD・保存・名前付きクエリに加え、`Query()`（式木クエリ）・生 SQL・一括追加まで全メソッドを持つ全機能面です。`--remote-contracts`（quicker.json の `GenerateRemoteContracts`、GUI の「リモート対応」チェックボックス）を指定すると、リモート操作用のインターフェイスを**追加生成**します。

| 面 | インターフェイス | 含まれる操作 |
|---|---|---|
| リモート面（追加生成） | `I{Entity}RemoteRepository` | CRUD（GetById / GetAll / Insert / Update / Delete）・グラフ保存（Save）・名前付きクエリ |
| 全機能面（従来どおり） | `I{Entity}Repository`（リモート面を継承） | 上記＋ `Query()`（式木）・生 SQL 3 種・一括追加 |

リモート面の全メソッドは引数・戻り値が純粋なデータ（エンティティ・主キー・件数）だけで構成され、原理的にネットワーク境界を越えられます。アプリ本体をリモート面だけに依存させておけば、将来 Repository の実体を Web サービス経由のリモート実装へ差し替えるときもコンパイル時に安全が保証されます。式木や生 SQL が必要な処理は従来どおり `I{Entity}Repository` を使えばよく、「ここは DB 直結が必要」なことが型で読み取れます。

```csharp
// アプリ本体はリモート面だけに依存する（将来リモート実装へ差し替え可能な部分）
public sealed class OrderService(IOrderRemoteRepository orders)
{
    public Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, CancellationToken ct) =>
        orders.GetByCustomerAsync(customerId, ct);   // 名前付きクエリはリモート面に載る
}

// 生 SQL・式木クエリが要る処理は従来どおり全機能面を要求する（DB 直結前提であることが型で明示される）
public sealed class OrderMaintenance(IOrderRepository orders)
{
    public Task<int> ArchiveAsync(CancellationToken ct) =>
        orders.ExecuteSqlAsync("UPDATE orders SET archived = 1 WHERE ...", cancellationToken: ct);
}
```

このオプションは純粋に追加的です。ON にしても `I{Entity}Repository`・実装クラス・DI の実装登録は従来のまま変わらず、リモート面が同一インスタンスへの転送として DI に追加登録されるだけなので、既存コードを壊さずいつでも有効化できます（`AddGenerated*Repositories` でどちらの面も解決できます）。

## リモートサービス（--remote-services）— 3 階層構成

`--remote-services`（quicker.json の `GenerateRemoteServices`、GUI の「リモート対応」2 つ目のチェックボックス）を指定すると、リモート面を **HTTP + JSON** でネットワーク越しに提供するクライアント／サーバー実装を生成します（リモート面 `--remote-contracts` は自動的に有効になります）。

| 生成物 | 置き場所 | 内容 |
|---|---|---|
| HTTP クライアント実装 | 本体生成物へ同梱（依存は BCL の `HttpClient` のみ） | `Http{Entity}RemoteRepository`（`I{Entity}RemoteRepository` 実装）＋ `AddGeneratedHttpRemoteRepositories` |
| サーバー実装 | `{ベース名}.RemoteServer.g.cs`（別ファイル） | `MapGeneratedRemoteEndpoints`（Minimal API。`POST {prefix}/{エンティティ}/{操作}`・prefix 既定 `/quicker`） |

推奨のプロジェクト構成は「**共有クラスライブラリ**（本体生成物＝エンティティ・契約・クライアント実装）を**サーバー**（ASP.NET Core）と**クライアントアプリ**（WPF 等）の両方が参照し、サーバーファイルだけをサーバープロジェクトへ置く」形です。

```csharp
// ---- サーバー（ASP.NET Core・Microsoft.NET.Sdk.Web）----
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGeneratedRepositories(connectionString);   // 実体は Repository (QuickER) でも EF Core でもよい

var app = builder.Build();
app.MapGeneratedRemoteEndpoints();          // 認可を付けるなら .RequireAuthorization() を続ける
app.Run();

// ---- クライアントアプリ（DI 登録 1 行で直結⇔リモートを切り替え）----
// 直結:    services.AddGeneratedRepositories(connectionString);
// リモート: services.AddGeneratedHttpRemoteRepositories("https://server:5001/quicker");
// アプリ本体はどちらでも IOrderRemoteRepository を注入して使う（コード変更なし）
```

押さえておくポイント:

- **直列化**はエンティティの JSON 往復（`ToJson` / `Clone`）と同じ意味論（VO は内包値・RowState 込み・親参照ナビは循環しない）で、クライアント・サーバーが共有の `RemoteJson.Options` を使います
- **名前付きクエリは実装方式（簡易 DSL／生 SQL／手動実装）に依らず全部**リモート面経由で呼び出せます（実装の実体はサーバー側のリポジトリ）
- **例外は型が復元されます**: サーバーの `SaveConflictException` は HTTP 409 を介してクライアントでも `SaveConflictException` として送出され（直結時と同じ catch が機能）、その他のサーバー例外は `RemoteRepositoryException`（ステータスコード・メッセージ保持）になります
- **グラフ保存（Save）成功後はローカルの RowState も確定**します（直結時と同じ挙動）
- 認証・TLS はスコープ外です。クライアントは `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` で認証ハンドラ付きの HttpClient を構成し、サーバーは `MapGeneratedRemoteEndpoints()` の戻り値（`RouteGroupBuilder`）へ ASP.NET Core の認可を付与してください
- サーバーファイルは ASP.NET Core の FrameworkReference（`Microsoft.AspNetCore.App`）が必要です（SDK が `Microsoft.NET.Sdk.Web` のプロジェクトなら追加設定不要）

動く実例はリポジトリの [samples/ec-order-remote](../samples/ec-order-remote/README.ja.md) にあります（この推奨構成そのままの 3 プロジェクト＋実 2 プロセスで動かすサンプル。名前付きクエリのリモート転送・`SaveConflictException` の型復元も実演）。

## テスト用インメモリ Repository（GenerateInMemoryRepositories）

DB なしでユニットテストするためのインメモリ実装を追加生成できます。同一契約を実装し、サポート外の操作は実 DB の Repository へ切り替える案内付きの `NotSupportedException` を送出します。

## ランタイムパッケージ参照モード（--runtime-packages）

既定では、生成コードはランタイム（スキーマ非依存の固定コード）込みのインライン出力で自己完結します。`--runtime-packages` を指定すると固定コードを出力せず、次の NuGet パッケージへの参照で賄います（生成ヘッダと CLI 出力に必要な PackageReference が案内されます。csproj には手動で追加してください）:

| パッケージ | 内容 | 依存 |
|---|---|---|
| `QuickER.Runtime` | 共通基盤・方言中立の契約 | なし |
| `QuickER.Runtime.SqlServer` | QuickER の SQL Server 方言エンジン | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | QuickER の SQLite 方言エンジン | Microsoft.Data.Sqlite |
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
