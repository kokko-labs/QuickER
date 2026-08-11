# 生成コードの使い方

*[English](code-generation.md) | 日本語*

QuickER が生成する C# コードの構成と、データアクセス層（QuickER 版 Repository / EF Core 版 Repository）の使い方をまとめます。生成方法は [CLI リファレンス](cli.ja.md)、動く実例は [samples/ec-order](../samples/ec-order) を参照してください。

## 生成されるもの

| カテゴリ | 内容 |
|---|---|
| Entity | テーブルに対応する POCO。UI フレームワーク非依存（CommunityToolkit 等に依存しない）。`RowState`（Unchanged / Added / Updated / Removed）と `MarkAdded()` などの状態遷移メソッド、ナビゲーションプロパティ（親参照・子コレクション）を持つ |
| EditModel | 画面編集用のモデルと Entity との相互変換。各列は確定値と画面入力文字列（`BindingXxx`）の 2 表現を持つ |
| Mapper | Entity ⇄ EditModel の変換器。**ロードは無損失**＝確定値は Entity から直接コピーし（入力文字列のパースで再構築しない）、`BindingXxx` は確定値から導出される表示用の投影になる。入力文字列の精度になるのはユーザーが実際に編集した欄だけなので、読み込んだだけで表示書式が表現できないもの（`DateTime` の秒未満・`DateTimeKind` など）が落ちることはない |
| 値オブジェクト（オプション） | 列名ごとの値オブジェクト型（`CustomerIdValue` など）。`GenerateValueObjects` 有効時のみ（[値オブジェクト](#値オブジェクトgeneratevalueobjects) 参照） |
| Repository 共通契約 | `IRepository<TEntity, TKey>` と各エンティティのインターフェイス（`ICustomerRepository` など）。QuickER 版 Repository と EF Core 版 Repository が同じ契約を実装する |
| QuickER 版 Repository 実装 | 方言別（SQL Server / SQLite）の軽量実装＋ DI 登録拡張 |
| EF Core 版 Repository | `QuickErDbContext`（Fluent 構成込み）＋ EF Core 版 Repository ＋ DI 登録拡張 |
| ランタイム | 上記が使う固定コード（既定でインライン出力。パッケージ参照モードあり） |

Entity には既定で DataAnnotations と **DB 定義メタ属性**（`[DbTableMeta]` / `[DbColumnMeta]`）が付き、方言中立の型トークン（`string(50)` / `decimal(10,2)` など）と説明が刻まれます。生成コードは DB 定義の自己記述ドキュメントとしても機能します。この付与は `IncludeDataAnnotations`（既定 ON）で制御しますが、QuickER 版 Repository・EF Core 版 Repository・インメモリ Repository のいずれかの契約を生成する構成では OFF にできません（診断エラー）。ランタイムが `[Table]` / `[Key]` / `[Column]` をリフレクションで参照するためです。

> **前提**: Repository の生成は単一主キー・アプリ側採番が対象です（複合キー・DB 自動採番のテーブルは Entity / EditModel のみ利用できます）。

> **対象フレームワーク**: 生成コードは .NET 10 で開発・検証しています。現状は .NET 8 でもビルドできますが、保証はしません。ランタイム NuGet パッケージは `net10.0` 単独のため、パッケージ参照モードは .NET 10 が必要です。

## 値オブジェクト（GenerateValueObjects）

列を素の型（`int` / `string` など）でなく、列ごとの**値オブジェクト型**（`CustomerIdValue` / `NameValue` など）として生成するオプションです（既定 OFF。CLI の `--generate-value-objects` / quicker.json の `GenerateValueObjects` / GUI「値オブジェクト」行の「全カラムを値オブジェクト化」チェックボックス）。DB アクセスの選択（なし / QuickER 版 Repository / EF Core 版 Repository）に依らず選択でき、マルチターゲット・インメモリ・リモートとも併用できます。

ON にすると、全テーブルの列を**列名で**グローバルにグルーピングし、列名ごとに 1 つの値オブジェクト型を生成します。主キーと同名の外部キー列は**同一の型を共有**するため、ID の取り違えがコンパイルエラーになります。Entity のプロパティと Repository のキー型も値オブジェクトになります:

```csharp
// ICustomerRepository : IRepository<CustomerEntity, CustomerIdValue>
var customer = await customers.GetByIdAsync(CustomerIdValue.Create(1));

// orders.GetByIdAsync(customer.CustomerId) は OrderIdValue でないためコンパイルエラー
```

同名列の定義（型・長さ・精度）が食い違う場合は Warning 診断を出し、主キーの定義を優先（主キーが無ければ最も広い定義）して 1 つの型に揃えます。

### 生成される型と検証

各値オブジェクトは `sealed partial class` で、コンストラクタは非公開・生成は静的ファクトリ経由のみです。図の列定義から検証コードが自動生成されます（文字列は最大長、`decimal` は精度・スケール＝丸めずに弾く）:

```csharp
var name = NameValue.Create("山田");   // 検証違反は ValueObjectValidationException

if (NameValue.TryCreate(input, out var vo, out var errors))   // 例外なしで検証
{
    entity.Name = vo!;
}
```

基底クラスは値の型に応じて選ばれ、値ベースの等価（`==` / `Equals`）に加えて、数値・日時系は比較演算子（`<` / `>=` など）、文字列は `Contains` / `StartsWith` / `EndsWith` を備えます。

### partial 拡張点

生成されるクラスはメッセージ・表示名の差し替えに 2 つの方法を持ち、静的クラス・生成モード（インライン／パッケージ参照）を問わず共通の規則です:

- **一括** — 固定 infra の static settable な `Func` をアプリ起動時に差し替える。全インスタンスに効く
- **個別** — 生成側の具象クラスに `Customize*`（ref 引数）の partial を実装する。その型・プロパティだけに効く

```csharp
// 一括（起動時）: メッセージの日本語化、表示名から Description を使わない切替
ValueObjectValidationMessages.ValueRequired = static () => "値を入力してください。";
EditModelMessages.Required = static name => $"{name}は必須です。";
GeneratedDisplayNames.Resolve = static (name, _) => name;   // Description を無視しメンバー名を使う
```

```csharp
public sealed partial class NameValue
{
    // 追加の検証（自動生成の検証の後に呼ばれる）
    static partial void OnValidate(string value, ICollection<string> errors)
    {
        if (value.Contains(' '))
        {
            errors.Add("空白は使えません。");
        }
    }

    // 検証メッセージ等で使う表示名の差し替え（既定は列の説明・無指定はプロパティ名）
    static partial void CustomizeDisplayName(ref string displayName) => displayName = "氏名";
}

public partial class CustomerEditModel
{
    // プロパティ単位の文言差し替え（propertyName で対象列を分岐できる）
    partial void CustomizeParseErrorMessage(string propertyName, string inputValue, string typeName, ref string message)
    {
        if (propertyName == nameof(Age))
        {
            message = $"'{inputValue}' は年齢として正しくありません。";
        }
    }
}
```

static クラス: `ValueObjectValidationMessages`（`MaxLengthExceeded` / `ScaleExceeded` / `PrecisionExceeded` / `ValueRequired`）、`EditModelMessages`（`Required` / `ParseFailed` / `JoinValueObjectErrors`）、`GeneratedDisplayNames`（`Resolve`＝Entity・EditModel プロパティ・値オブジェクトすべての表示名解決に使われる）。パッケージ参照モードでは、この 3 つは `QuickER.Runtime` パッケージに収載されます。

個別 partial: 値オブジェクト側は `CustomizeDisplayName` / `CustomizeMaxLengthErrorMessage` / `CustomizeScaleErrorMessage` / `CustomizePrecisionErrorMessage` / `CustomizeValueRequiredErrorMessage`（string / byte[] のみ）/ `OnValidate`、EditModel 側は `CustomizeRequiredErrorMessage` / `CustomizeParseErrorMessage` / `CustomizePropertyDisplayName`、Entity 側は `CustomizeDisplayName`（従来どおり partial でなく override 方式）。値オブジェクトの画面表示用文字列 `DisplayValue`（virtual）の override も引き続き使えます。

### 各機能との統合（透過対応）

値オブジェクトは生成コード全体で透過に扱えます。素の値へ手で開く必要はほとんどありません:

| 機能 | 挙動 |
|---|---|
| QuickER 版 Repository | SQL パラメータは内包値へ自動変換して束縛し、読み出しは `Create` で値オブジェクトへ復元する |
| `Query()`（式木） | 値オブジェクトどうしの比較・文字列の `Contains` などをそのまま SQL へ翻訳する |
| EF Core モード | Fluent 構成に値変換（`HasConversion`）と翻訳プラグイン（文字列メソッド・`.Value` 参照のサーバーサイド翻訳）を自動適用する |
| 名前付きクエリ | メソッドのパラメータは素の型のまま。生成される条件式が値オブジェクト比較へ自動変換する（IN はリストを持ち上げ） |
| EditModel | 確定値プロパティは `NameValue?` などの値オブジェクト。画面バインド用の `BindingXxx`（文字列）は `TryCreate` で検証し、エラーを `INotifyDataErrorInfo` へ載せる |
| JSON（`ToJson` / `Clone` / リモート転送） | **内包値として**直列化する（`{"customerId": 1}`。値オブジェクトのラッパー構造は JSON に現れない） |

> **注意**: DB や JSON からの読み出しも `Create` 経由で検証されます。検証を通らない値が既存データに残っていると読み出し時に `ValueObjectValidationException` になるため、`OnValidate` で足す追加検証は既存データと整合させてください。

### string 主キーの GUID 化（UseGuidKeyForStringPrimaryKey）

`GenerateValueObjects` と併せて `UseGuidKeyForStringPrimaryKey`（CLI の `--use-guid-key-for-string-primary-key` / GUI「string 主キーを GuidKey 化」）を ON にすると、string 主キーの値オブジェクトが GUID 採番基底（`ValueObjectGuidKeyBase`）になり、引数なしの `Create()` で新しいキーを採番できます:

```csharp
// document_id が string 主キーの場合
var id = DocumentIdValue.Create();   // Guid.NewGuid() を文字列で内包した新キー
```

「主キーはアプリ側採番」という Repository 生成の前提（上記）を、採番ロジックを書かずに満たせます。

## QuickER 版 Repository

依存最小（ADO のみ）の軽量 Repository です。対象方言は SQL Server（`FOR JSON` ベース）と SQLite（プレーン SELECT ＋ マルチクエリ）。

DI 登録拡張はエンジン別の名前（`AddGeneratedSqlServerRepositories` / `AddGeneratedSqliteRepositories`）で生成されます。

```csharp
// DI 登録（生成される拡張メソッド。方言に応じて SqlServer / Sqlite を選ぶ）
var provider = new ServiceCollection()
    .AddGeneratedSqliteRepositories(connectionString)
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

値の側が null になる `==` / `!=` の比較は、リテラルの null でも変数由来の null でも `IS NULL` / `IS NOT NULL` へ変換されます（C# / EF Core と同じ意味論で、全バックエンドで一致します）。素のパラメータとして束縛すると `col = @p` となり、SQL の 3 値論理では全行が偽になってしまうためです。補償は等値のみが対象で、関係演算子（`<` `<=` `>` `>=`）は従来どおり null をパラメータとして束縛します（null 対応の SQL 対応物が無いため）。

リストの `Contains` は要素 1 個につきバインド変数 1 個へ展開され、チャンク分割はしません。そのため巨大なリストは方言のバインド変数・IN リスト上限（Oracle の 1000、SQL Server の 2100 パラメータ、SQLite の歴史的な 999 など）を超えて実行時エラーになります。大量のキーを渡す場合は一時テーブルへ入れて結合するか、生 SQL を使ってください。

### グラフ保存（親子まとめて 1 回で保存）

```csharp
var order = new OrderEntity { OrderId = 1000, CustomerId = 1 };
order.MarkAdded();
var line = new OrderLineEntity { OrderLineId = 5000, OrderId = 1000, ProductId = 100, Quantity = 2 };
line.MarkAdded();
order.OrderLines.Add(line);

var affected = await orders.SaveAsync(order);   // RowState に従い INSERT / UPDATE / DELETE を 1 トランザクションで実行
```

### Save フック（ISaveHook）

グラフ保存（`SaveAsync`）の各操作の**前後に処理を差し込む**仕組みです。前処理での状態チェックによる単独スキップと、後処理での**同一トランザクション内のファイルデータ登録**（Save と blob 書き込みのアトミック性）が主なユースケースです。フックは常時生成され、1 つも登録しなければ完全に no-op（従来どおりの挙動）です。

`ISaveHook<TEntity>` を実装して DI に登録します。両メソッドとも既定実装を持つため、**必要な方だけ**書けます。

```csharp
public sealed class DocumentSaveHook : ISaveHook<DocumentEntity>
{
    // 操作の直前。false を返すとその 1 件だけをスキップする（既定はスキップしない）
    public Task<bool> BeforeSaveAsync(
        DocumentEntity entity, SaveOperation operation, CancellationToken ct = default)
    {
        // 例: 承認済みの文書だけ削除を許す（それ以外の削除はスキップ）
        if (operation == SaveOperation.Delete && !entity.IsApproved)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    // 操作の直後・コミット前。context は同一トランザクションに参加する
    public async Task AfterSaveAsync(
        DocumentEntity entity, SaveOperation operation, ISaveHookContext context,
        CancellationToken ct = default)
    {
        if (operation == SaveOperation.Insert)
        {
            // 除外列（blob）へストリーミング書き込み（Save と同一トランザクション＝アトミック）
            await context.WriteBinaryColumnFromFileAsync(
                nameof(DocumentEntity.Payload), entity.DocumentId, "/tmp/upload.bin", ct);
            // 生 SQL で監査行を残す（これも同一トランザクション）
            await context.ExecuteSqlAsync(
                "INSERT INTO audit (note) VALUES (@note)", new { note = $"created {entity.DocumentId}" }, ct);
        }
    }
}
```

```csharp
// DI 登録（Singleton / Scoped どちらでも可。フックが Scoped サービスを使うなら Scoped）
services.AddSingleton<ISaveHook<DocumentEntity>, DocumentSaveHook>();
```

同じエンティティ型に複数のフックを登録できます。**Before は登録順**に呼ばれ、**最初に `false` を返した時点で短絡**します（残りの Before は呼ばれず、その行はスキップ）。**After も登録順**に呼ばれます。Before / After が投げた例外はそのまま伝播し、Save 全体がロールバックします（実トランザクションを持つ実装先はトランザクションで巻き戻し、インメモリはそもそも書き込みを公開していません＝後述）。

**対象は `SaveAsync`（単一・複数の両形態）だけ**です。低レベル API である `InsertAsync` / `UpdateAsync` / `DeleteAsync` の直接呼び出しと `BulkInsertAsync` は、フックを**素通り**します（発火しません）。

#### Before とスキップの意味論

`false` は**そのエンティティの操作 1 件のみ**をスキップします（他の行は続行）。スキップされた行は After が呼ばれず、`RowState` も据え置かれます（`AcceptChanges` の対象外）。

スキップは単独であるため、整合性はフック実装者の責任です。とくに**削除は子から順に実行される**ため、**サブツリー削除で「root（親）だけ `false`」にすると、子は削除され root だけが残ります**。親を止めたいなら子のフックも `false` を返す必要があります。整合しないスキップ（例: 新規の親をスキップしつつ新規の子を保存）は、DB に FK 制約が張られていれば FK 制約違反 → 例外 → **全体ロールバック**で安全側に倒れます。

#### After とコンテキスト

After は**操作の直後・コミット前**に、進行中のトランザクションに参加する `ISaveHookContext` を受け取ります。フック内から Repository の通常 API を呼ぶと別接続でロック競合するため、context 経由の操作を使います。After が例外を投げると Save ごとロールバックするため、「行はあるがファイル未登録」という中途半端な状態は構造的に生じません（インメモリも、書き込みをステージングして全フェーズ成功後にだけ公開することで同じ保証を持ちます）。

context が提供する操作（生ハンドルは公開しません）:

- `WriteBinaryColumnAsync(propertyName, key, stream, length?)` ／ ファイル糖衣 `WriteBinaryColumnFromFileAsync(propertyName, key, path)` — 除外列（`ExcludeUnboundedBinaryColumns` 有効時）へのストリーミング書き込み（`nameof` で列を指定）
- `ExecuteSqlAsync(sql, parameters)` — 任意の DML（監査行・関連テーブルへの書き込みなど）

`operation` には**実際に行われた操作**が渡ります。`insertWhenUpdateMissing: true` で更新対象が見つからず INSERT に切り替わった場合、Before は `Update` で 1 回呼ばれ、After は実操作の `Insert` で呼ばれます。

#### 実装先ごとの差分

| 実装先 | フック発火 | context の対応 |
|---|---|---|
| QuickER 版 Repository（SQL Server / SQLite） | 完全対応（After は各操作の直後） | `WriteBinaryColumnAsync` / `ExecuteSqlAsync` とも対応 |
| EF Core 版 Repository（`GenerateEfCore`） | 対応（After は `SaveChanges` 後に一括） | `ExecuteSqlAsync` は対応・`WriteBinaryColumnAsync` は `NotSupportedException` |
| インメモリ（`GenerateInMemoryRepositories`） | 対応（擬似トランザクション） | `WriteBinaryColumnAsync` はストアへ・`ExecuteSqlAsync` は `NotSupportedException`。実トランザクションはありませんが copy-on-write で保存単位を all-or-nothing にします＝全書き込みをステージングし、最後のフェーズが成功したときだけ一括公開するため、**失敗した保存の書き込み（After が書いた blob を含む）は一度も見えず**、失敗の巻き添えで並行書き込みが消えることもありません |
| リモート（`--generate-remote-services`） | **サーバー側の DI に登録したフックが発火**します | サーバー側の実体実装に準じます。Before でサーバーがスキップした行は保存応答に載って戻るため、クライアント側の `RowState` も据え置かれます（その行は未保存のまま残り、次回の保存で再試行されます）＝直結と同じ挙動です |

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

### 重複の事前チェック（CheckUniquenessAsync）

テーブルの UNIQUE 制約は、生成される **Entity** クラスへ `[UniqueConstraint("PropA", "PropB", Name = "UQ_...")]` として `[DbTableMeta]` / `[DbColumnMeta]` と並んで刻まれます。これらと同じく「DB 定義の自己記述」のための定義メタで、実行時の振る舞いは持ちません（以下のチェックはいずれも生成コードそのものです）。属性型は、刻む制約が 1 つでもあるときだけ出力されます。C# リバースはこの属性を読み戻すため、UNIQUE 制約は往復します（[インポートとエクスポート](import-export.ja.md)を参照）。

生成される Repository 契約には、図の UNIQUE 制約から組み立てた一括チェックが常に含まれます（テーブルに制約が 1 件も無くても生成されます）:

```csharp
Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
    TEntity entity, CancellationToken cancellationToken = default);
```

テーブルの各 UNIQUE 制約について「**このエンティティと同じ主キーの行を除外して**、同じ値の組を持つ行が既に存在するか」を照合します。同一主キーの行を除くため、挿入前でも更新前でも同じ呼び方で正しく動きます。主キーが null を取り得る型（値オブジェクト・`string`）で未設定のとき（＝新規行の通常状態）は、除外条件そのものを付けないため、本当に全行が照合対象になります。構成列の値に `null` を含む組は、NULL の衝突意味論が方言で割れるためスキップします。

> 結果は**助言**です。最終的な保証は DB 自身の UNIQUE 制約で、チェックと保存の間に他プロセスが挿入すれば保存はやはり失敗します（TOCTOU）。チェックは親切なメッセージを出すために使い、保存時の例外処理は残してください。

実装は全実装先（各方言の QuickER 版 Repository・EF Core・インメモリ）で同一の式木クエリ 1 本なので、どのバックエンドでも同じ挙動になります。

```csharp
var violations = await orders.CheckUniquenessAsync(order);

foreach (var violation in violations)
{
    // ConstraintName = DDL 上の名前（図で未設定なら合成名 UQ_{テーブル}_{列連結}）
    // PropertyNames  = 制約を構成するエンティティプロパティ名（宣言順）
    Console.WriteLine($"{violation.ConstraintName}: {string.Join(", ", violation.PropertyNames)}");
}
```

#### ユーザー定義チェック

図では表せないルール（条件付きの一意性・テーブル横断の規則）は、各 Repository 実装に生成される省略可能な partial メソッドで足せます。未実装の間は呼び出しごと消えるためコストはゼロです。

```csharp
public sealed partial class OrderRepository
{
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= []).Add(static async (entity, cancellationToken) =>
            await SomeLookupAsync(entity, cancellationToken)
                ? new UniquenessViolation("UQ_custom_rule", [nameof(OrderEntity.Code)], "このコードは予約済みです。")
                : null);
}
```

生成分のチェックが先に走り、続いて収集されたデリゲートが登録順に走ります。null 以外の結果はすべて戻り値のリストへ合流します。リモートサービス構成では、チェック全体（フック込み）がサーバー側の Repository で走ります（HTTP クライアントは呼び出しを転送するだけです）。

#### EditModel: コレクション内の重複

UNIQUE 制約を持つテーブルの EditModel は、その制約を生成コードでも宣言します（`EditModelUniquenessConstraint`＝制約名・構成プロパティ名・値のコンパイル済みアクセサの `static readonly` テーブルを `UniquenessConstraints` プロパティで公開）。`EditModelCollection<T>.Validate()` はこのテーブルを読み、**要素どうし**で重複した値を検出して、重複したグループの全要素の構成列バインディングプロパティへエラーを登録します（必須検証と同じくリフレクションは使いません）。値の組に `null` を含む場合はスキップし、削除対象（`RowState.Removed`）は比較から外します（DB 照合と同じ規則）。`EditModelCollection<T>` ではないルートの一覧には、同じヘルパを直接呼べます:

```csharp
var valid = EditModelUniquenessValidator.Validate(models);
```

親の検証でもコレクション内の重複まで走ります。`parent.Validate(includeChildren: true)` は登録済みの子コレクションの検証を `EditModelCollection<T>.Validate()` へ委譲するため、要素個別の検証だけでなく兄弟どうしの重複検出も 1 回の呼び出しに含まれ、`parent.CollectErrors()` は重複エラーも `Orders[i]` のパス付きで返します。Mapper のロードで丸ごと差し替わった子コレクションも対象です（カスケード登録は登録時のインスタンスを捕捉せず、毎回アクセサ経由で現在のコレクションを解決します）。

重複エラーは入力エラー（必須・変換・値オブジェクト・`OnValidate`）とは別のストアで保持します。一方の登録・クリアがもう一方に触れないため、同じプロパティに変換エラーと重複エラーが同時に立ち、`GetErrors` は両方を返します。とくに、重複を解消して再検証しても同じ欄に残っている「変換できません」のエラーは消えません（変換エラーはバインディングのセッターからしか再生成されないため、ここで消すと不正な入力が画面に残ったまま `Validate` が成功を返してしまいます）。`HasErrors` は両ストアを合わせて判定します。

#### EditModel: DB との照合

EditModel と Repository 契約の両方を生成する構成では、各 EditModel に糖衣メソッドも生成されます:

```csharp
// 引数の型はリモート契約を生成する構成なら I{Entity}RemoteRepository、そうでなければ I{Entity}Repository
if (!await editModel.ValidateUniqueAsync(repository))
{
    // エラーはバインディングプロパティへ登録済み（INotifyDataErrorInfo により UI へ表示される）
}
```

EditModel の確定値から Entity を組み立てて `CheckUniquenessAsync` を呼び、各違反の `PropertyNames` をバインディングプロパティ名へ写します。構成列を持たない違反（および EditModel に無いプロパティ名の違反）は、空のプロパティ名で登録されるモデルレベルエラーになり、`GetErrors(null)` で取得できます。呼び出しの先頭で前回の重複エラーを消すため、再検証で古いエラーが残ることはありません。

メッセージは `EditModelMessages.DuplicateValue`（構成列の表示名列挙を受け取る `static Func`）が既定で、クラスごとの省略可能な `partial void CustomizeDuplicateErrorMessage(IReadOnlyList<string> propertyNames, ref string message)` で微調整できます。ユーザー定義チェックが `UniquenessViolation.Message` を返した場合は、そちらが優先されます。

#### 既存 API で書ける近隣の事前チェック

生成による支援があるのは重複チェックだけですが、隣接する検証は既存 API の 1 行で書けます。

```csharp
// 主キーが既に使われているか（挿入前）
var taken = await orders.GetByIdAsync(order.OrderId) is not null;

// 外部キーの参照先が存在するか（子の保存前）
var parentExists = await customers.GetByIdAsync(order.CustomerId) is not null;

// 子から参照されているか（削除前）
var referenced = await orders.Query().Where(o => o.CustomerId == customerId).AnyAsync();
```

重複チェックと同じく、これらも助言です。最終的な権威は DB 自身の制約にあります。

### store-generated 列（rowversion / timestamp）

DB が値を生成する列（SQL Server の `rowversion` / `timestamp` など）は、生成 Entity のプロパティにマーカー属性 `[StoreGeneratedColumn]` が付与され、QuickER 版 Repository の **INSERT / BulkInsert / UPDATE の対象から自動的に除外**されます（付与は生成オプションに依らず、型マッパーが行バージョン列と認識する列＝SQL Server の `rowversion` / `timestamp` に対して行われます）。

- **書き込みでは触れない**: これらの列には DB が値を採番するため、Repository は明示的な値を書き込みません。明示挿入を試みると SQL Server は `Cannot insert an explicit value into a timestamp column.` を返しますが、除外によりこの実行時エラーを回避します。
- **SELECT では取得する**: `GetByIdAsync` / `GetAllAsync` / `Query()` の結果に含まれ、値を読めます（並行性トークンとして参照できます）。
- **EF Core モード**では Fluent 構成の `IsRowVersion()` が同じく store-generated として扱うため、この機構は適用されません。
- **テーブルの並行性トークンを兼ねます**。保存時にエンティティが読んだ版と現在の行が比較されます（次節）。

### rowversion による楽観排他

rowversion 列を持つテーブルは楽観排他で保存されます。オプトインは不要で、エンティティが読んだ版と現在の行を比較し、競り負けた保存は他人の変更を黙って上書きせずに拒否されます。rowversion 列のないテーブルの挙動は従来どおりです。

`UpdateAsync` / `SaveAsync` は省略可能な `ConcurrencyMode` を受け取ります:

| モード | 挙動 |
| --- | --- |
| `Optimistic`（既定） | 書き込みを版で守ります。他者が先に変更した行は `SaveConflictException` で拒否されます。 |
| `ForceOverwrite` | 版の条件を外します（明示的な last-write-wins）。 |

```csharp
// エンティティが読んだ版で守られる。競り負けると SaveConflictException
await repository.UpdateAsync(order, cancellationToken: ct);

// 明示的な last-write-wins
await repository.UpdateAsync(order, ConcurrencyMode.ForceOverwrite, ct);

// グラフ保存もグラフ内の更新・削除を同じ規則で守る
await repository.SaveAsync(order, cancellationToken: ct);
```

- **「行なし」と「版が古い」は別の結果です。** 単一の `UpdateAsync` は行が存在しなければ従来どおり `false` を返し、行はあるが版が進んでいれば `SaveConflictException` を送出します。`insertWhenUpdateMissing: true` も同じ線引きで、行なしは INSERT へ切り替わり、版が古い場合は競合として報告されます（INSERT へ倒すと競合が主キー重複に化けるためです）。
- **グラフ保存は削除も守り**、競合が 1 件でもあれば保存単位の全体がロールバックされます（インメモリ Repository は書き込みをステージングして一括公開する方式で同じ結果になります＝失敗した保存はそもそもストアへ届きません）。
- **インメモリは公開時にもう一度検証します。** Save フックはストアのロック外で走るため、保存が起点にした行を他者が先に書き換えている場合があり、公開時にそれを検出して `SaveConflictException` にします（他者の書き込みは無傷のまま残ります）。rowversion 列を**持たない**型はこの検証の対象外で、並行性トークンが無い以上ストアの契約は後勝ちのままです。`ForceOverwrite` も同じ理由で検証を外します。保存が挿入する行だけは必ず検証します（先に取られた主キーは並行性の判断ではなく主キー重複だからです）。
- **新しい版が反映されます。** 挿入・更新・グラフ保存が成功すると、エンティティは DB が採番した版を保持するため、再取得せずに同じインスタンスをそのまま保存できます。Save フックの `AfterSaveAsync` はコミット前に走るため、この時点ではまだ古い版が見えます。
- **どのバックエンドでも契約は同じです。** QuickER 版 Repository は `WHERE ... AND <rowversion> = @original` で文を守り `OUTPUT` 句で新しい版を読み戻します。EF Core は自前の並行性トークン（`IsRowVersion()`）を使い `DbUpdateConcurrencyException` を同じ例外へ変換します。インメモリ Repository は単調増加する 8 バイトの擬似版で DB を模します。HTTP リモートクライアントはモードをリクエストへ載せ、応答が返す版を書き戻します。

既知の制限:

- `rowversion` 型を持つのは SQL Server だけのため、QuickER 版 Repository では `sqlserver` 方言のみが対象です。SQLite（や他方言）向けの図にはそもそも該当列がないため影響しません。
- `BulkInsertAsync` は `SqlBulkCopy` を使い、生成値を返せないためエンティティの版は元のままです。後続の更新で版が要る場合は再取得してください。
- 版の読み戻しには `OUTPUT` 句を使いますが、SQL Server はトリガーのあるテーブルでこれを拒否します。QuickER の DDL 生成はトリガーを出力しないため、QuickER 外でトリガーを足したテーブルにのみ関係します。
- 既に消えている行の削除は、従来どおりバックエンド間で非対称です。QuickER 版 Repository は黙って許容し、EF Core のグラフ保存は `SaveConflictException` として報告します。
- rowversion 列には `[DbColumnMeta]` のトークンが付かないため、C# リバースでは復元されません（図の側で宣言してください）。
- 生 SQL（`ExecuteSqlAsync` 等）と無制限バイナリ列の Stream アクセサは直接操作のため、版では守られません。

### 無制限バイナリ列の除外（ExcludeUnboundedBinaryColumns）

巨大な BLOB を一覧取得・更新のたびに往復させない（メモリを保護する）ためのオプションです（既定 OFF。CLI `--exclude-unbounded-binary-columns` / GUI「無制限バイナリ列を取得しない (varbinary(max) / BLOB)」チェックボックス（DB アクセスで「QuickER 版 Repository」を選んだときのみ表示）/ quicker.json の `ExcludeUnboundedBinaryColumns`）。ON にすると、**サイズ上限のないバイナリ列**の Entity プロパティへマーカー属性 `[UnboundedBinaryColumn]` が付与され、QuickER 版 Repository の SELECT / UPDATE から当該列が除外されます。生成時には除外した列の一覧が Info 診断（CLI 出力・GUI の生成結果ダイアログ）で通知されます。

判定は列の宣言型で行います（`rowversion` や `binary(n)` / `varbinary(n)` など長さ宣言のある型は対象外）:

| 方言 | 除外対象 | 対象外（有界） |
|---|---|---|
| SQL Server | `varbinary(max)` / `image` | `binary(n)` / `varbinary(n)` / `rowversion` |
| SQLite | 長さ宣言なし `BLOB` | `BLOB(n)` |
| PostgreSQL | `bytea` | — |
| MySQL | `BLOB` / `MEDIUMBLOB` / `LONGBLOB` | `TINYBLOB` / `BINARY(n)` / `VARBINARY(n)` |
| Oracle | `BLOB` / `LONG RAW` | `RAW(n)` |

挙動の要点:

- **SELECT から除外**: `GetByIdAsync` / `GetAllAsync` / `Query()` の結果で除外列は `null`（DB から読み出さない）。ただし後述の `WithUnboundedBinary()` でオプトインした場合を除く
- **UPDATE から除外**: 更新 SQL の SET 句に除外列は含まれない。除外列に値を設定したまま `UpdateAsync` / `SaveAsync` を実行すると**実行時例外**になる（黙ってデータを取りこぼさない）
- **INSERT / BulkInsert は全列のまま**: 初回書き込みは通常どおり値を渡せる
- **名前付きクエリの射影**が除外列を参照する場合は取得される（射影は明示的な列選択のため）
- **生 SQL** で明示的に SELECT すれば取得できる（下記の運用例）
- **EF Core モード（`DbSet` 経由のクエリ / `SaveChanges`）には適用されない**（EF Core の列選択は EF Core の責務）
- インメモリ Repository（`GenerateInMemoryRepositories`）は実 DB とパリティ（同じ除外挙動）

除外列の読み書きは生 SQL でも行えます（他の手段は後述）:

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

- **`Include` とは併用できません**（終端メソッド実行時に `InvalidOperationException`）。無制限バイナリ列が必要な場合は `Include` なしの別クエリで取得してください。これは SQL Server の `Include` 経路が FOR JSON＝Base64 経由で巨大 BLOB のメモリ膨張（ピーク 5〜6 倍）を招くためで、「巨大 BLOB を扱う」目的でメモリ特性が予測可能に保たれるようにするためです（SQL Server では FOR JSON を使わず**プレーン SELECT** で取得します）。
- 効果があるのは `ToListAsync` / `FirstOrDefaultAsync` のみです（件数・存在確認・射影 `ToProjectionListAsync` には影響しません）。
- 取得したエンティティは正当なエンティティですが、除外列が UPDATE 対象外である点は変わりません。そのまま `UpdateAsync` すると既存ガードで例外になります（除外列の更新は上記の生 SQL `ExecuteSqlAsync` で行ってください）。
- EF Core モードでは EF Core が元々全列を読むため no-op です（`Include` 併用エラーだけはパリティで同様に送出します）。

#### Stream アクセサ `Read/Write{Column}Async`

除外オプションを有効（かつ QuickER 版 Repository を生成）にすると、除外列ごとに **ストリーミング**の読み書きメソッドが追加生成されます（配置先はリモート契約の有無で変わります。後述）。`byte[]` の一括読み込みを避け、**O(チャンク)＝blob 全量をメモリに載せずに** DB⇔ストリーム（またはファイル）を転送できます。生成される API の中で、GB 級のバイナリでもメモリ使用量が一定に保たれるのはこの手段です。

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
- **EF Core モードでは使用できません**（`NotSupportedException`）。EF Core は方言非依存設計のため方言固有のストリーミングを持てません。QuickER 版 Repository を使うか、`partial` クラスで実装してください（`GenerateEfCore` と QuickER 版 Repository を併用する構成では、EF Core 版実装のみ例外になります）。
- **配置先**: リモート契約（`--generate-remote-contracts` / `--generate-remote-services`）が無効なら全機能面 `I{Entity}Repository` に直接載ります。有効な場合はリモート面 `I{Entity}RemoteRepository` へ移設されます（全機能面はリモート面を継承するので、どちらの構成でも利用コードは同じ・純粋に追加的）。ファイル糖衣もその対象インターフェイスに合わせます。リモートサービス（`--generate-remote-services`）を有効にすると HTTP で転送できます（後述の「バイナリ転送エンドポイント」）。

`WithUnboundedBinary()` との使い分け:

| | `WithUnboundedBinary()` | Stream アクセサ |
|---|---|---|
| 単位 | エンティティ形（複数列・複数行・Include なし） | 列 1 本の読み書き |
| メモリ | 中規模（`byte[]` で一括） | **一定**（blob サイズに依らず O(チャンク)） |
| 用途 | 除外列込みのエンティティが一時的に欲しい | 巨大 blob を DB⇔ファイル/ストリームで転送 |
| 書き込み | 不可（取得のみ・更新は生 SQL） | `Write{Column}Async` で列単位に書ける |

## EF Core モード（GenerateEfCore）

既存 Entity をそのまま EF Core に載せる方言非依存の `QuickErDbContext` と、**同一 Repository インターフェイスの EF Core 版実装**を生成します。マイグレーションは範囲外で、スキーマ作成は DDL 生成の責務です（EF Core は既存スキーマへの接続専用）。

```csharp
// DI 登録 1 行の差し替えで QuickER 版 Repository と交換できる
services.AddGeneratedEfCoreRepositories(options => options.UseSqlServer(connectionString));
// SQLite / PostgreSQL / MySQL / Oracle は対応する EF Core プロバイダの Use* を指定する
```

- 保存は `TrackGraph` による切断グラフ保存（`RowState` を EF Core の状態へ変換）
- 楽観排他もパリティ（`ConcurrencyMode` でポリシーを選び、EF Core の `DbUpdateConcurrencyException` を `SaveConflictException` へ変換し、更新後の並行性トークンをエンティティへ残す。[rowversion による楽観排他](#rowversion-による楽観排他)）
- 生 SQL 系 API も完全パリティ

**QuickER 版 Repository との併用生成**（両方 ON）はパリティ検証用で、CLI / 設定ファイルでのみ指定できます。GUI は排他選択です。また EF Core 版 Repository とマルチターゲットの QuickER 版 Repository（下記）は併用できません（診断エラー）。

## マルチターゲット Repository（sqlserver + sqlite）

`--repository-dialects sqlserver,sqlite` を指定すると、中立契約を 1 回・方言別実装を `.SqlServer` / `.Sqlite` サブ名前空間へ出力し、keyed DI で同一プロセスから複数 DB へ書き分けられます。

```csharp
services.AddGeneratedSqlServerRepositories(serviceKey: "primary", sqlServerConn);
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);

// 解決側は同一の契約型を keyed で選ぶ
var primary = provider.GetRequiredKeyedService<ICustomerRepository>("primary");
var local   = provider.GetRequiredKeyedService<ICustomerRepository>("local");
```

## リモート対応インターフェイス（--generate-remote-contracts）

`I{Entity}Repository` は CRUD・保存・名前付きクエリに加え、`Query()`（式木クエリ）・生 SQL・一括追加まで全メソッドを持つ全機能面です。`--generate-remote-contracts`（quicker.json の `GenerateRemoteContracts`、GUI「リモート対応」行の「リモート操作用の Repository インターフェースを生成する」チェックボックス）を指定すると、リモート操作用のインターフェイスを**追加生成**します。

| 面 | インターフェイス | 含まれる操作 |
|---|---|---|
| リモート面（追加生成） | `I{Entity}RemoteRepository` | CRUD（GetById / GetAll / Insert / Update / Delete）・グラフ保存（Save）・名前付きクエリ |
| 全機能面（従来どおり） | `I{Entity}Repository`（リモート面を継承） | 上記＋ `Query()`（式木）・生 SQL 3 種・一括追加 |

リモート面の全メソッドは引数・戻り値が純粋なデータ（エンティティ・主キー・件数）だけで構成され、原理的にネットワーク境界を越えられます。アプリ本体をリモート面だけに依存させておけば、将来 Repository の実体を Web サービス経由のリモート実装へ差し替えるときも、境界を越えられない操作を使っていればコンパイルエラーで気づけます。式木や生 SQL が必要な処理は従来どおり `I{Entity}Repository` を使えばよく、「ここは DB 直結が必要」なことが型で読み取れます。

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

## リモートサービス（--generate-remote-services）— 3 階層構成

`--generate-remote-services`（quicker.json の `GenerateRemoteServices`、GUI「リモート対応」行の「HTTP クライアント / サーバー実装を生成する」チェックボックス）を指定すると、リモート面を **HTTP + JSON** でネットワーク越しに提供するクライアント／サーバー実装を生成します（リモート面 `--generate-remote-contracts` は自動的に有効になります）。

| 生成物 | 置き場所 | 内容 |
|---|---|---|
| HTTP クライアント実装 | 本体生成物へ同梱（依存は BCL の `HttpClient` のみ） | `Http{Entity}RemoteRepository`（`I{Entity}RemoteRepository` 実装）＋ `AddGeneratedHttpRemoteRepositories` |
| サーバー実装 | `{ベース名}.RemoteServer.g.cs`（別ファイル） | `MapGeneratedRemoteEndpoints`（Minimal API。`POST {prefix}/{エンティティ}/{操作}`・prefix 既定 `/quicker`） |

推奨のプロジェクト構成は「**共有クラスライブラリ**（本体生成物＝エンティティ・契約・クライアント実装）を**サーバー**（ASP.NET Core）と**クライアントアプリ**（WPF 等）の両方が参照し、サーバーファイルだけをサーバープロジェクトへ置く」形です。

```csharp
// ---- サーバー（ASP.NET Core・Microsoft.NET.Sdk.Web）----
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGeneratedSqliteRepositories(connectionString);   // 実体は QuickER 版 Repository でも EF Core 版 Repository でもよい

var app = builder.Build();
app.MapGeneratedRemoteEndpoints();          // 認可を付けるなら .RequireAuthorization() を続ける
app.Run();

// ---- クライアントアプリ（DI 登録 1 行で直結⇔リモートを切り替え）----
// 直結:    services.AddGeneratedSqliteRepositories(connectionString);
// リモート: services.AddGeneratedHttpRemoteRepositories("https://server:5001/quicker");
// アプリ本体はどちらでも IOrderRemoteRepository を注入して使う（コード変更なし）
```

押さえておくポイント:

- **直列化**はエンティティの JSON 往復（`ToJson` / `Clone`）と同じ意味論（VO は内包値・RowState 込み・親参照ナビは循環しない）で、クライアント・サーバーが共有の `RemoteJson.Options` を使います
- **名前付きクエリは実装方式（簡易 DSL／生 SQL／手動実装）に依らず全部**リモート面経由で呼び出せます（実装の実体はサーバー側のリポジトリ）
- **例外は型が復元されます**: サーバーの `SaveConflictException` は HTTP 409 を介してクライアントでも `SaveConflictException` として送出され（直結時と同じ catch が機能）、その他のサーバー例外は `RemoteRepositoryException`（ステータスコード・メッセージ保持）になります
- **リクエストを解釈できない場合は 500 ではなく 400 になります**。リクエスト自体の読み取り中に失敗するもの（不正な JSON・空ボディ・JSON でない Content-Type・型不一致・値オブジェクトの検証違反・必須フィールドの欠落〔`Insert` / `Update` / `Save` / `SaveMany` への `{}`、参照型キーを省いた `GetById` / `Delete`〕・未定義の `ConcurrencyMode` 値、バイナリエンドポイントの `?id=` 欠落・復元不能）はクライアントが送った内容の問題なので、HTTP 400＋`RemoteError`（`Type` は `"BadRequest"`）を返します（クライアントは `StatusCode` が 400 の `RemoteRepositoryException` を送出）。400 のメッセージにはクライアント自身のペイロードに関する情報しか載らず、サーバー側のログ出力も `OnServerError` フックも実行されません（どちらも 500 専用）。サーバー基盤が拒否したリクエスト（`BadHttpRequestException`。例: リクエストボディのサイズ上限超過）は、その例外が持つステータスコード（413 など）をそのまま返します
- **グラフ保存（Save）成功後はローカルの RowState も確定**します（直結時と同じ挙動）
- **楽観排他も転送されます**。`ConcurrencyMode` 引数は Update / Save のリクエストに含まれ、Insert / Update / Save の応答は保存で採番された版を「エンティティ型名＋主キー」の対応表として運び、クライアントが手元のグラフへ書き戻します。これによりリモートでも直結と同じ版を保持でき、再取得なしで同じエンティティを続けて保存できます
- **500 応答にはサーバー側例外のメッセージがそのまま載ります**。これは意図的な設計で、クライアントが失敗内容を復元し「直結時と同じ catch」を成立させるための情報です。スタックトレースを含む例外全体はサーバー側だけに記録されます（`ILoggerFactory` 経由・カテゴリ `QuickER.RemoteServer`。ロギング未構成のホストでは何もしません）。信頼境界の外へ公開する場合は、認可か例外変換ミドルウェアを併用して内部情報が応答へ漏れないようにしてください
- 認証・TLS はスコープ外です。クライアントは `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` で認証ハンドラ付きの HttpClient を構成し、サーバーは `MapGeneratedRemoteEndpoints()` の戻り値（`RouteGroupBuilder`）へ ASP.NET Core の認可を付与してください
- **ファクトリ版が返す HttpClient の所有権は呼び出し側にあります**。`AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` はリポジトリ解決のたび（スコープ×エンティティ数だけ）ファクトリを呼び出し、返された HttpClient は生成コードも DI コンテナも破棄しません。共有インスタンスか `IHttpClientFactory` 管理のインスタンスを返してください（毎回 new するとソケットが枯渇します）。ベースアドレス版は共有インスタンスを 1 つだけ作り、それを DI コンテナが所有します（`ServiceProvider` の破棄と同時に HttpClient も破棄されるため、破棄済み provider から取得したリポジトリを使うと `ObjectDisposedException` になります）
- サーバーファイルは ASP.NET Core の FrameworkReference（`Microsoft.AspNetCore.App`）が必要です（SDK が `Microsoft.NET.Sdk.Web` のプロジェクトなら追加設定不要）
- **生成サーバークラスは拡張できます。** `GeneratedRemoteEndpoints` は `partial` クラスなので、生成物と並べて独自のエンドポイントヘルパを同じクラスへ置けます。また `static partial void OnServerError(HttpContext, Exception)` フックを別パートで実装すると、エンドポイントが HTTP 500 を返すたびに独自処理（通知・メトリクス・追加ログ）を差し込めます（組み込みログの後に実行され、実装しなければコンパイル時に呼び出しが消えます。フック内で例外が起きても隔離され、元のエラー応答を妨げません＝フックの例外はサーバーログへ記録して握り潰します）。同じプレフィックス配下への追加エンドポイントは、`MapGeneratedRemoteEndpoints()` が返す `RouteGroupBuilder` へ直接 Map しても構いません

### バイナリ転送エンドポイント（無制限バイナリ列の Stream アクセサ）

無制限バイナリ除外（`--exclude-unbounded-binary-columns`）と併用すると、除外列の Stream アクセサ（`Read/Write{Column}Async`）が **HTTP でストリーミング転送**されます。JSON エンベロープ（`POST` + Base64）では巨大 blob のメモリ膨張を避けられないため、これらは意図的に **REST 風の第 2 形式**（動詞分離・生ボディ・`application/octet-stream`）を使います。除外列ごとに次の 3 エンドポイントが生成されます（`{列名}` は C# プロパティ名）:

| 動詞・URL | 意味 | 応答 |
|---|---|---|
| `GET {prefix}/{エンティティ}/{列名}?id=` | ダウンロード（本文を宛先へストリーム） | 200＋`application/octet-stream`（空 blob も 200）／行なし・NULL は **404**（クライアントで `false`）／`id` 欠落・不正は **400** |
| `PUT {prefix}/{エンティティ}/{列名}?id=` | アップロード（生ボディ・`Content-Length` 必須） | 成功 **204**／行なし **404**（`false`）／`Content-Length` 欠落（chunked）は **411**／`id` 欠落・不正は **400** |
| `DELETE {prefix}/{エンティティ}/{列名}?id=` | 列を `NULL` へ（`Write(id, null)` 相当） | 成功 204／行なし 404／`id` 欠落・不正は **400** |

- **キーは URL クエリ `?id=`** で運びます（本文は blob 本体に使うため）。VO キーは JSON エンベロープと同一規則（内包値）で直列化されます。
- **0 バイトの PUT（空ボディ）と `NULL` 化（DELETE）は構造的に区別**されます（前者は `Read` が `true`＋空・後者は `false`）。
- **バイナリ PUT だけリクエストサイズ制限が既定で解除**されます（`IRequestSizeLimitMetadata` メタデータ付与。JSON エンドポイントは既定 30MB のまま）。GB 級を追加設定なしで扱うためですが、**解除は DoS 面の懸念があるため認可（`MapGeneratedRemoteEndpoints().RequireAuthorization()`）との併用を強く推奨**します。上限へ戻す・別値にする場合は戻り値の `RouteGroupBuilder` でグループ全体を上書きしてください。
- クライアント（`Http{Entity}RemoteRepository`）は `GET` を `ResponseHeadersRead` で受けて宛先へ O(チャンク) でコピーし、`PUT` は `StreamContent`（`Content-Length` 付き）で送ります。非シーク Stream で `length` を渡さない場合は**送信前**に `ArgumentException` になります（既存の長さ契約と同一）。
- **`WithUnboundedBinary()` / `Query()` / 生 SQL のリモート化はスコープ外**です（従来どおり）。

動く実例はリポジトリの [samples/ec-order-remote](../samples/ec-order-remote/README.ja.md) にあります（この推奨構成そのままの 3 プロジェクト＋実 2 プロセスで動かすサンプル。名前付きクエリのリモート転送・`SaveConflictException` の型復元も実演）。

## テスト用インメモリ Repository（GenerateInMemoryRepositories）

DB なしでユニットテストするためのインメモリ実装を追加生成できます。同一契約を実装し、サポート外の操作は実 DB の Repository へ切り替える案内付きの `NotSupportedException` を送出します。なお `GenerateInMemoryRepositories` と `UseRuntimePackages` は併用できません（診断エラー）。インメモリの実行器は生成側の固定 infra として出力され、パッケージには存在しないためです。

### 実 DB との既知の乖離

インメモリストアはクエリを SQL でなく LINQ-to-Objects で評価するため、いくつかの意味論は DB のものではなくインメモリ固有です。「インメモリでは通るのに実 DB では落ちる」を避けるために把握しておいてください。

- **文字列の比較と並び順は序数（Ordinal）です。** 絞り込み（`Where`）も `OrderBy` も序数比較なので、`"B"` は `"a"` より前に並びます。SQL Server の既定照合は大文字小文字を区別せず、並び順も照合順序に従うため、大文字小文字やアクセントの扱いに依存するテストは実 DB の裏付けにはなりません。
- **UNIQUE 制約は書き込み時に強制されません。** 強制されるのは主キーだけです（重複主キーは実 DB の INSERT と同じく拒否されます）。UNIQUE 制約の重複値は黙って格納されるため、チェックが要るテストでは `CheckUniquenessAsync` を使ってください。

## ランタイムパッケージ参照モード（--use-runtime-packages）

既定では、生成コードはランタイム（スキーマ非依存の固定コード）込みのインライン出力で自己完結します。`--use-runtime-packages` を指定すると固定コードを出力せず、次の NuGet パッケージへの参照で賄います（生成ヘッダと CLI 出力に必要な PackageReference が案内されます。csproj には手動で追加してください）:

| パッケージ | 内容 | 依存 |
|---|---|---|
| `QuickER.Runtime` | 共通基盤・方言中立の契約 | なし |
| `QuickER.Runtime.SqlServer` | QuickER の SQL Server 方言エンジン | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | QuickER の SQLite 方言エンジン | Microsoft.Data.Sqlite |
| `QuickER.Runtime.EntityFrameworkCore` | EF Core 共通部品 | Microsoft.EntityFrameworkCore.Relational |

パッケージ版とツール版はロックステップ（同一バージョン）で公開されるため、両者には同じバージョンを使ってください。0.x の間は minor 間の互換性を約束していません（[CONTRIBUTING](../CONTRIBUTING.ja.md) のバージョニング方針を参照）。DI 登録拡張・`QuickErDbContext`・エンティティ別実装などのスキーマ依存物は、本モードでも常に生成側に出力されます。

## API リファレンス（.g.md）

生成コードと同名ベースの API リファレンス Markdown を追加出力できます。GUI の生成ダイアログの「API リファレンス (.g.md) を出力する」チェック、または CLI の `--generate-api-docs` フラグで有効化します（**既定 OFF**）。DB アクセスの選択（なし / QuickER 版 Repository / EF Core 版 Repository）とは独立して、常に選択できます。

有効化すると、`.g.cs` と同じベース名の `.g.md` が 1 つ出力されます（例: `EcOrder.g.cs` → `EcOrder.g.md`）。カテゴリ別分割モードでは `Entities.g.cs` 等の固定名と同じ流儀の固定名 `ApiDocs.g.md`（日本語版は `ApiDocs.ja.g.md`）になります。内容は次のとおりです。

- エンティティ一覧と、各エンティティのプロパティ表（DB 型トークン込み。`string(50)` / `decimal(10,2)` など）
- Repository 契約（`IRepository<TEntity, TKey>` と各エンティティのインターフェイス）— Repository 契約を生成する構成でのみ含まれます
- DI 登録・CRUD・クエリの使い方例 — 同じく Repository 契約を生成する構成でのみ含まれます（DB アクセス「なし」ではこれらの節は省略されます）
- 生成ファイル構成表

**英語が正本です。** 日本語版も併産したい場合は、GUI の下位チェック「日本語版を出力する」、または CLI の `--api-docs-ja` フラグ（設定キー `IncludeJapaneseApiDocs`）を有効化します（**既定 OFF**・`--generate-api-docs` が前提）。有効化すると、英語正本の `.g.md` に加えて `.ja.g.md` が併産されます（例: `EcOrder.g.cs` → `EcOrder.ja.g.md`）。

`.g.md` / `.ja.g.md` は自動生成ファイルです。再生成で上書きされるため、直接編集しないでください。

## 既存コードベースとの共存

稼働中のシステムには、手書きやスキャフォールドで作ったエンティティ・データアクセス資産がすでにあるはずです。DB 取込で図を手に入れたあと、それらと生成コードをどう付き合わせるかには段階があり、**どの段階で止めても成立します**。

- **生成を使わない共存** — 図をレビュー・定義書出力・差分同期のためだけに使い、コードには一切触れない使い方です。既存のデータ層はそのまま残ります。スキーマの単一情報源としての価値（再エクスポートで定義書を図へ追従させられること・DB との差分検出）は、この段階だけでも得られます
- **基本生成だけの共存** — DB アクセス「なし」で Entity / EditModel / Mapper だけを生成し、画面まわりに使う段階です。データアクセスは既存資産のままで、生成コードは読み書きに関与しません
- **新規機能からの段階導入** — 新しく作る機能だけ QuickER 版 Repository（または EF Core 版 Repository）を使い、既存コードは触るときに移行する段階です。生成コードは同じスキーマへの素の ADO / EF Core アクセスなので、既存のデータ層と同じデータベースを共有できます（両者にまたがるトランザクション境界と接続管理の設計は利用側の責任です）。既存システムが EF Core code-first の場合も、生成される `QuickErDbContext` は既存スキーマへの接続専用（マイグレーション非関与）のため、既存 DbContext と同居できます（1 つの DB に複数コンテキストを持つ一般的なパターンです）

共存時の実務的な注意は 2 点です。

- **名前空間で分離する** — `RootNamespace`（と必要なら出力先プロジェクト）を既存コードと分けておけば、同名クラスがあっても共存できます（両方を使う箇所では名前空間修飾か using エイリアスで区別します）
- **既存資産を図へ起こす入口は DB 取込** — GUI の「コード取込」（C# リバース）が対象にするのは QuickER が `IncludeDataAnnotations` ON で生成した `.g.cs` のみで、手書き POCO は対象外です。既存資産の構造は、コードではなく稼働 DB から取り込んでください（[データベース連携](database.ja.md)を参照）

## ライセンス注記

コード生成エンジン（`QuickER.CodeGen.CSharp` / `CodeGen.UI` / `Cli`）には [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) **＋追加許諾**が適用されます。この追加許諾により、**現行リリースは商用利用を含め全員無料**です。提供方針（基本生成＝Entity / EditModel / Mapper を含む恒久無料の許諾と将来の有償化の可能性）は[ライセンスガイド](../LICENSING.ja.md)を参照してください。**生成されたコードとランタイムパッケージ（MIT）はあなたの成果物側**です。[LICENSE-NC.md](../LICENSE-NC.md) は生成物の利用・改変・配布・販売について、目的を問わず恒久的で取消不能な許諾を全員に与えており、クレジット表記も不要です。
