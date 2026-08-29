# 生成コードの使い方

*[English](code-generation.md) | 日本語*

QuickER が生成する C# コードの構成と、データアクセス層（QuickER 版 Repository / EF Core 版 Repository）の使い方をまとめます。生成方法は [CLI リファレンス](cli.ja.md)、動く実例は [samples/ec-order](../samples/ec-order) を参照してください。

## 生成されるもの

| カテゴリ | 内容 |
|---|---|
| Entity | テーブルに対応する POCO。UI フレームワーク非依存（CommunityToolkit 等に依存しない）。`RowState`（Unchanged / Added / Updated / Removed）と `MarkAdded()` などの状態遷移メソッド、ナビゲーションプロパティ（親参照・子コレクション）を持つ |
| EditModel | 画面編集用のモデルと Entity との相互変換。各列は確定値と画面入力文字列（`BindingXxx`）の 2 表現を持つ |
| Mapper | Entity ⇄ EditModel の変換器。**ロードは無損失**＝確定値は Entity から直接コピーし（入力文字列のパースで再構築しない）、`BindingXxx` は確定値から導出される表示用の投影になる。入力文字列の精度になるのはユーザーが実際に編集した欄だけなので、読み込んだだけで表示書式が表現できないもの（`DateTime` の秒未満・`DateTimeKind` など）が落ちることはない。バイナリ列は防御的にコピーするため、ロードした EditModel の編集がロード元の Entity へ波及しない。`date` 列（`datetime` ではない）はカルチャの短い日付書式で表示し、末尾に "0:00:00" が付かない |
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

外部キー列は、**列名が参照先と違っていても参照先（親側）列の値オブジェクト型を共有**します（`orders.ship_customer_id` は `ShipCustomerIdValue` でなく `CustomerIdValue` になります）。「同じ識別子は同じ値型」を型で表すための規則で、EF Core が要求する「外部キーと参照先主キーの CLR 型一致」もこれで満たされます（自己参照テーブルの `parent_node_id → node_id` のような列名の違う外部キーは、列名ごとの別型のままだと EF Core のモデル検証が通りません）。外部キーの外部キーは参照をたどった先の型へ揃え、相互参照の循環に入った列は自分の列名由来の型のままです。型を共有した列は生成時に Info 診断で一覧されます（列名由来の型名が変わるため）。同一の列が「異なる型に解決される親」を複数参照している図は生成時エラー、親子で下地の C# 型が食い違う列ペアは共有せず列名由来の型のままです。

### 生成される型と検証

各値オブジェクトは `sealed partial class` で、コンストラクタは非公開・生成は静的ファクトリ経由のみです。図の列定義から検証コードが自動生成されます（文字列は最大長、`decimal` は精度・スケール＝丸めずに弾く）:

```csharp
var name = NameValue.Create("山田");   // 検証違反は ValueObjectValidationException

if (NameValue.TryCreate(input, out var vo, out var errors))   // 例外なしで検証
{
    entity.Name = vo!;
}

var errorList = new List<string>();
NameValue.Validate(input, errorList);      // VO を作らずに検証だけ
```

`Validate` は渡したコレクションへ違反を足すため、複数の値のエラーを 1 箇所へ集めたいときに向きます。戻り値は**その呼び出し**で違反が無かったかを表すので、コレクションに前の値のエラーが既に入っていても判定として使えます。

基底クラスは値の型に応じて選ばれ、値ベースの等価（`==` / `Equals`）に加えて、数値・日時系は比較演算子（`<` / `>=` など）、文字列は `Contains` / `StartsWith` / `EndsWith` を備えます。

### 手書きの値オブジェクト

`Create` / `TryCreate` / `Validate` の本体は `ValueObjectBase<TSelf, TValue>` に 1 回だけ置かれています（基底クラスから継承した静的メソッドは `static abstract` インターフェイスメンバを満たします）。そのため、図の列に対応しない概念（メールアドレス・期間・単位など）の値オブジェクトは **3 メンバ**＝private コンストラクタ＋`New`＋`ValidateCore` で書けます:

```csharp
public sealed class ContactMailValue
    : ValueObjectStringBase<ContactMailValue>,
        IValueObject<ContactMailValue, string>
{
    private ContactMailValue(string value) : base(value) { }

    static ContactMailValue IValueObject<ContactMailValue, string>.New(string value) => new(value);

    static void IValueObject<ContactMailValue, string>.ValidateCore(
        string value, ref List<string>? errors)
    {
        if (!value.Contains('@'))
        {
            (errors ??= new List<string>()).Add("メールアドレスには @ が必要です。");
        }
    }
}
```

- 形は生成物とまったく同じです。`ContactMailValue.Create(...)` / `TryCreate` / `Validate` が従来どおり使え、JSON 変換・SQL パラメータバインド・行の組み立ても生成された値オブジェクトと同じ扱いを受けます
- 検証規則を持たない値オブジェクトは `ValidateCore` ごと省略できます（インターフェイスの既定実装＝検証なし）＝2 メンバで成立します
- `ValidateCore` はエラーリストを**参照渡し・未確保**で受け取ります。最初の違反を足すときだけ確保する形（`(errors ??= new List<string>()).Add(...)`）にすると、正常な値の生成は何も確保しません
- 参照型の値（`string` / `byte[]`）で入力を信頼できない場合は、`ValidateCore` で `null` を弾いてください。値オブジェクトは null を包みません（NULL 許容列はプロパティ自体を null に保ちます）。生成された値オブジェクトは null 入力を検証エラーとして報告します
- `New` / `ValidateCore` は明示的実装なので型の公開面には出ません。型引数経由の `TVo.New` は検証を迂回するため、`New` を自分で呼ばないでください（検証は `Create` / `TryCreate` の仕事です）
- 基底は値の型で選びます: `ValueObjectStringBase`（文字列）・`ValueObjectOrderedBase<TSelf, TValue>`（数値・日時）・`ValueObjectBooleanBase`・`ValueObjectBinaryBase`・`ValueObjectGuidKeyBase`（GUID 文字列キー）・それ以外は `ValueObjectBase<TSelf, TValue>` を直接

### 生の値からの生成（CSV / Excel の取り込み）

`TryCreateFrom` / `CreateFrom` は、CSV のフィールドや表計算のセルのように**下地の型に揃っていない値**から値オブジェクトを作ります。

```csharp
// セルの値（string でも double でも DateTime でも）を、指定カルチャで読む
if (QuantityValue.TryCreateFrom(cell, culture, out var quantity, out var errors))
{
    entity.Quantity = quantity;      // 空セルなら quantity は null（エラーではない）
}

var amount = AmountValue.CreateFrom(cell, culture);   // 例外版・空セルは null
```

`IValueObject<TSelf>` は下地の型を型引数に取らないため、**取り込みコードは値オブジェクトごとの分岐を持たずに書けます**。

```csharp
private static T? ReadCell<T>(IXLTableRow row, int column, IFormatProvider? culture, List<string> errors)
    where T : class, IValueObject<T>
{
    if (T.TryCreateFrom(row.Cell(column).Value, culture, out var value, out var messages))
    {
        return value;
    }

    errors.Add(BuildCellErrorMessage(row, column, messages));

    return null;
}
```

- **空のセルは違反ではありません。** `null` / `DBNull` / 空文字は「未入力」として `true` ＋ `null` を返し、エラーを 1 件も積みません（NULL 許容列はプロパティ自体を null に保つ設計に合わせています）。必須かどうかは EditModel の必須チェックの担当です
- **カルチャは呼び出し側が決めます。** `null` はインバリアントです。人が書いた日付書式（`2026/08/28`）を読むなら、その書式のカルチャを渡してください
- **数値は桁区切りを許します。** 表計算の書式付き数値を文字列で読むと `1,234` で届くため、数値型は `NumberStyles` を明示して解析します。整数型は小数点を許さないので `1,234.5` は `int` として通りません
- 変換できたあとは通常の `TryCreate` と同じです。その型の検証（最大長・精度・`OnValidate`）がそのまま効きます
- 変換自体に失敗したときのメッセージは `ValueObjectValidationMessages.InputNotConvertible` で差し替えられます
- カルチャを省略するオーバーロード（`TryCreateFrom(raw, out var value, out var errors)` / `CreateFrom(raw)`）もあります

### 定義済みインスタンスだけを受け付ける（列挙型の値オブジェクト）

値が閉じた集合になる概念——区分・モード・ステータス——は、`static readonly` で定義したインスタンスだけを返す値オブジェクトにできます。`TryGetDefined` を実装すると、`Create` / `TryCreate` が新しいインスタンスを作らずに定義済みのものを返します。

```csharp
public sealed class DataAccessMode
    : ValueObjectOrderedBase<DataAccessMode, int>,
        IValueObject<DataAccessMode, int>
{
    public static readonly DataAccessMode Web = new(1, "Web");
    public static readonly DataAccessMode Database = new(2, "Database");
    public static readonly DataAccessMode Fake = new(3, "Fake");

    private DataAccessMode(int value, string modeName) : base(value) => ModeName = modeName;

    public string ModeName { get; }

    public static IEnumerable<DataAccessMode> GetList() => [Web, Database, Fake];

    static DataAccessMode IValueObject<DataAccessMode, int>.New(int value) =>
        GetList().First(x => x.Value == value);

    static bool IValueObject<DataAccessMode, int>.TryGetDefined(int value, out DataAccessMode? defined)
    {
        defined = GetList().FirstOrDefault(x => x.Value == value);

        return defined is not null;
    }

    // TryGetDefined だけでは未定義値を拒めません（New へ落ちるだけです）。対で検証を書いてください
    static void IValueObject<DataAccessMode, int>.ValidateCore(int value, ref List<string>? errors)
    {
        if (!GetList().Any(x => x.Value == value))
        {
            (errors ??= new List<string>()).Add($"データアクセスモード {value} は定義されていません。");
        }
    }

    public override string DisplayValue => ModeName;
}
```

> **注意**: `TryGetDefined` が決めるのは「検証を通った値に対して何を返すか」だけです。未定義の値を拒む検証（`ValidateCore` または `OnValidate`）を必ず対で書いてください。書き忘れると、集合の外の値が黙って `New` に落ちて普通のインスタンスとして作られ、「定義済み以外は受け付けない」という前提が破れます。

**生成された値オブジェクトも、partial を足すだけで同じ形にできます。** 生成側の `New` は差し替えられませんが、`TryGetDefined` はその一段外に割り込むため、利用者の partial だけで完結します（生成器のオプションは要りません）。

```csharp
// 生成された ModeValue（int 列）を列挙型に拡張する
public sealed partial class ModeValue
{
    public static readonly ModeValue List = new(1) { ModeName = "List" };
    public static readonly ModeValue Edit = new(2) { ModeName = "Edit" };

    public string ModeName { get; private init; } = string.Empty;

    public static IEnumerable<ModeValue> GetList() => [List, Edit];

    static bool IValueObject<ModeValue, int>.TryGetDefined(int value, out ModeValue? defined)
    {
        defined = GetList().FirstOrDefault(x => x.Value == value);

        return defined is not null;
    }

    static partial void OnValidate(int value, ICollection<string> errors)
    {
        if (!GetList().Any(x => x.Value == value))
        {
            errors.Add($"表示モード {value} は定義されていません。");
        }
    }
}
```

`TryGetDefined` は `New` ではなく `Create` / `TryCreate` の側に割り込むため、**DB から読んだ行も JSON から復元した値も定義済みインスタンスになります**（`ModeName` のような付随する状態が欠けたインスタンスが出回りません）。

値の代わりに名前で作れるようにしたいときは `TryCreateFrom` を型側で差し替えます。扱わない形は基底へ委譲すれば、通常の変換はそのまま残ります。

```csharp
static bool IValueObject<ModeValue>.TryCreateFrom(
    object? raw, IFormatProvider? provider, out ModeValue? result, out IReadOnlyList<string> errors)
{
    if (raw is string name && GetList().FirstOrDefault(x => x.ModeName == name) is { } hit)
    {
        result = hit;
        errors = Array.Empty<string>();

        return true;
    }

    return ValueObjectBase<ModeValue, int>.TryCreateFrom(raw, provider, out result, out errors);
}
```

これで、前節のジェネリックな取り込みコード（`ReadCell<ModeValue>`）が `"Edit"` も `2` も受け付けます。

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

この登録はエンティティ非依存の生 SQL 実行器 `ISqlExecutor` も登録し、登録する各リポジトリへ渡します。そのため生成拡張のあとに独自実装を登録すれば（生 SQL にログ・計測・再試行を挟むラッパーなど）、リポジトリの生 SQL メソッドもその実装を経由します。手で `new` するリポジトリは実行器を省略可能な第 3 引数で受け取り、省略時は従来どおり既定実装を組むため、既存の呼び出しは無変更です。

### 接続とスキーマの立ち上げ

接続を開くのは生成された `SqlConnectionFactory` で、**SQLite では外部キー強制を既定で有効**にします。SQLite は接続側が要求しない限り強制しないため、これがないと生成 DDL の外部キーが黙って無効になります（親のない子行が入り、親を消しても子が残る）。スキーマが制約を宣言している以上、強制されるのが既定として正しいという判断です。接続文字列の `Foreign Keys` 指定はそのまま尊重するので、`Foreign Keys=False` を明示すればプロバイダ本来の挙動に戻せます。

QuickER が生成した DDL からスキーマを作る用途には、`SqliteSchemaBootstrap.ApplyDdlAsync` / `SqlServerSchemaBootstrap.ApplyDdlAsync` があります。接続を開いてスクリプト全文を 1 回で実行します。

```csharp
var ddl = await File.ReadAllTextAsync("Shop.sql");
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl);

// 大きなスクリプトを遅いマシンへ流すときはコマンドタイムアウトを伸ばせる（既定 null はプロバイダ既定）
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl, TimeSpan.FromMinutes(5));
```

これは開発・テスト・サンプル向けのブートストラップであり、スキーマ管理ではありません。バージョンも既存の状態もロールバックも知らないため、使い捨てでない DB にはマイグレーションツールを使ってください（EF Core モードが既存スキーマへの接続専用で Migrations を範囲外としているのと同じ線引きです）。

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

`BulkInsertAsync` の契約はすべての実装先で共通です。`null` 要素は**スキップ**され（グラフ保存のリスト内 null と同じ流儀）、戻り値は実際に挿入した行数だけを数え、空コレクションは接続を開かずに 0 を返し、呼び出し時点でキャンセル済みのトークンは何も書き込む前に例外になります。

SQL Server では `SqlBulkCopy` 経由になりますが、`CheckConstraints` を**常時**付けています。外部キー・CHECK 制約が検査されるため、行単位の `InsertAsync` が弾く行は一括追加でも弾かれます（`SqlBulkCopy` は指定しない限りこれらを検査せず、放置すると「一括追加だけが不正な行を通す」非対称になります）。トリガーは意図的に発火させません（QuickER の DDL はトリガーを生成しないため）。

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

同じナビゲーションを重ねて指定した `Include` / `ThenInclude` は 1 本のノードへマージされます。EF Core と同じ分岐イディオム——`Include(c => c.Orders).ThenInclude(o => o.OrderLines)` に続けて `Include(c => c.Orders).ThenInclude(o => o.Customer)`——で、`Orders` の下に複数の枝を書けます。

対応: 等値・比較・`&&`/`||`・`Contains`/`StartsWith`/`EndsWith`（LIKE）・リストの `Contains`（IN）・日付部品（`Year` など）・`string.IsNullOrEmpty`・値オブジェクト比較。**射影（Select）・GroupBy・Join・算術式は未対応**です（実行時例外。生 SQL か EF Core で回避してください）。

**ナビゲーションプロパティは述語にも並び替えキーにも書けません**。`Where(o => o.Customer == null)` は `NotSupportedException` になります。ナビゲーションは自分の列を持たないため、外部キー列で絞ってください（`Where(o => o.CustomerId == null)`）。インメモリと EF Core はこの述語を翻訳できるので、これは QuickER 版 Repository 固有の制限です。

等値比較の null は、全バックエンドが C# / EF Core と同じ意味論になるよう両側で補償されます。

- **値の側**: 評価すると null になる `==` / `!=` は、リテラルの null でも変数由来の null でも `IS NULL` / `IS NOT NULL` へ変換されます（素のパラメータとして束縛すると `col = @p` となり、SQL の 3 値論理では全行が偽になってしまうため）。
- **列の側**: 非 null 値との `!=` は `(col <> @p OR col IS NULL)` へ変換され、列が `NULL` の行も一致に含まれます。C# も EF Core も「`NULL` は非 null 値と等しくない」と扱うのに対し、素の `col <> @p` はその行が UNKNOWN になって静かに脱落するためです。列の NULL 許容性は式木から確実には判定できないため無条件に補償します（非 NULL 列では追加した選言が成立しないだけで意味は変わりません）。
- **列同士**: どちら側にも `NULL` があり得るため、両方の演算子を展開します。`!=` は `(a <> b OR (a IS NULL AND b IS NOT NULL) OR (a IS NOT NULL AND b IS NULL))`、`==` は `(a = b OR (a IS NULL AND b IS NULL))` になります。いずれも「片側だけ `NULL` なら不一致・両側 `NULL` なら一致」という C#（および EF Core）の判断に揃えるためで、素の `<>` と `=` はこれを逆向きに取り違えます。列と値の比較では `==` 側に補償は要りません（`NULL` の列が非 null 値と一致しないことは SQL も C# も同じため）。
- **否定**: `!(a == b)` / `!(a != b)` は `NOT (...)` で包まず演算子を反転するため、反対の演算子を直接書いたときとまったく同じ補償が掛かります。否定の否定は打ち消し合い（`!(!(a != b))` は補償を保ったまま `a != b` として翻訳されます）、三重以上の否定も同じように畳み込まれます。等値以外の否定は従来どおり `NOT (...)` になります。
- **既知の割り切り（複合条件の否定）**: 反転が効くのは `!` が比較に直接乗っている場合だけです。`!(a == b && c)` は De Morgan 展開されず、個別に補償された各項を `NOT (...)` で包んだ形になります。`NOT (UNKNOWN)` は UNKNOWN のままなので、括弧の内側で `NULL` により UNKNOWN になった行は結果から落ち、C#（インメモリ）や EF Core とは割れます。`NULL` があり得る場合は否定を比較側へ書いてください（`a != b || !c`）。こちらは反転を通るため両者と一致します。

補償は等値のみが対象で、関係演算子（`<` `<=` `>` `>=`）は従来どおり null をパラメータとして束縛します（null 対応の SQL 対応物が無いため）。

簡易 DSL の文字列一致（`LIKE` / `CONTAINS` / `STARTSWITH` / `ENDSWITH`）も、NULL 許容列に対しては「列が `NULL` でないこと」を AND した形へエミットされます（`NOT LIKE` も同じ前提の内側に入るため、`NULL` の行はどちらの向きでも一致しません）。SQL の `LIKE` は `NULL` の行を UNKNOWN で落とすので SQL 側では意味が変わりませんが、インメモリ実装は式木をコンパイルして実際に評価するため、この前提が無いと `NULL` の行で `NullReferenceException` になります。

日付部品（`Year`・`Month` など）へ変換されるのは、読み出し元が `DateTime` / `DateOnly` / `DateTimeOffset`（いずれも nullable を含む）の列である場合だけです。同名のプロパティを別の型に持たせても——値オブジェクトへ partial で足した場合など——日付部品とは見なさず、黙って `YEAR([col])` になる代わりに `NotSupportedException` で失敗します。

リストの `Contains` は要素 1 個につきバインド変数 1 個へ展開され、チャンク分割はしません。そのため巨大なリストは方言のバインド変数・IN リスト上限（Oracle の 1000、SQL Server の 2100 パラメータ、SQLite の歴史的な 999 など）を超えて実行時エラーになります。大量のキーを渡す場合は一時テーブルへ入れて結合するか、生 SQL を使ってください。

### グラフ取得（IncludeGraph）

```csharp
var fetched = await orders.Query()
    .Where(o => o.CustomerId == 1)
    .IncludeGraph()                 // グラフ保存がたどるのと同じカスケードを Include する
    .ToListAsync();

var one = await orders.Query().IncludeGraph().GetByIdAsync(1000);   // キー指定でグラフごと 1 件
```

グラフ保存（`SaveAsync`）の取得側の対です。`IncludeGraph()` は、保存がたどるのと同じ子方向のカスケードナビゲーションを末端まで `Include` ツリーへ展開する糖衣で、手で `Include(...).ThenInclude(...)` を並べたのと同じ結果になります。エンティティごとの拡張メソッドとして常に生成され、`Where` / `OrderBy` / ページング / `FirstOrDefaultAsync` と自由に組み合わせられます。図に子テーブルを足して再生成すれば `IncludeGraph()` は自動で追従します——手書きの `Include` 鎖は追従せず、取得した「集約」が静かに不完全になります。これを防ぐのがこのメソッドの主目的です。

クエリ側の `GetByIdAsync` は、主キー述語を焼き込んだ終端糖衣です（`Where(x => x.OrderId == id).FirstOrDefaultAsync()` と等価・該当なしは null）。キーの型は契約の同名メソッドと同一で、`Include` / `IncludeGraph` を付けなければ `repo.GetByIdAsync(id)` と同じ結果を返します。手動の `Include(...)` 連鎖の途中からもそのまま呼べます。

取得したグラフは `RowState = Unchanged` で返るため、編集してからルートを `SaveAsync` へ渡す「取得 → 編集 → 保存」の往復がそのまま成立します。

- **パス上に既出のテーブルへ戻るナビゲーションはたどりません**。自己参照（`Category.Children` など）や相互参照は有限の `Include` ツリーに写せないため、その辺はスキップされ、生成時に Info 診断で名指しされます。スキップされたナビゲーションは空のまま返るので、再帰構造は必要な深さだけ手動の `Include` で取得してください。`IncludeGraph()` の後に追加の `Include` を重ねることもできます（`Query().IncludeGraph().Include(x => x.Customer).GetByIdAsync(id)`）。親参照やスキップされたナビを足すのが典型で、閉包が既に含む子方向ナビゲーションを重ねて指定しても同じノードへマージされるため安全です（`ThenInclude` でその下へ枝を足す用途にも使えます）。保存側はインスタンスグラフ（＝有限）をたどるため任意の深さを保存できます——この取得と保存の非対称は仕様です。
- カスケード子を 1 つも持たないエンティティにも生成され、その場合はクエリをそのまま返す no-op です。
- 深い階層・広い図では取得量が相応に大きくなります。SQL Server はグラフ全体を 1 本のネスト JSON クエリで取得するため（SQLite は階層ごとの分割クエリ）、一部の子だけでよい場面では手動の `Include` で絞ってください。
- `WithUnboundedBinary()` とは併用できません（`Include` と同じ排他）。リモート面（`I{Entity}RemoteRepository`）には `Query()` が無いため、`IncludeGraph` もリモートでは使えません。

### グラフ保存（親子まとめて 1 回で保存）

```csharp
var order = new OrderEntity { OrderId = 1000, CustomerId = 1 };
order.OrderLines.Add(new OrderLineEntity { OrderLineId = 5000, OrderId = 1000, ProductId = 100, Quantity = 2 });

order.MarkAdded(includeChildren: true);         // 保存がたどるのと同じカスケードで集約全体をマーク

var affected = await orders.SaveAsync(order);   // RowState に従い INSERT / UPDATE / DELETE を 1 トランザクションで実行
```

`MarkAdded(includeChildren: true)` は、グラフ保存がたどるカスケードナビゲーションを末端までたどってマークします。組み立てたばかりの集約を、ノードごとに 1 回ずつ呼ばずに 1 回でマークできます（先にグラフを組み立ててからマークしてください）。カスケード形を持つのは `MarkAdded` だけです——グラフ全体を更新対象にすると誰も触っていない行まで書き戻すことになり、グラフ全体を削除対象にするのはグラフ保存の `cascadeDelete` がルートだけで行っていることだからです。

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

// 対象のエンティティ型をインスタンス自身から導く登録もできる。実装している ISaveHook<TEntity> すべてに
// 登録するため、複数テーブルを 1 つのフックで賄う場合も型ごとの行を書かずに済む
services.AddSaveHook(new AuditSaveHook());
```

**DI コンテナを使わない**場合は、`SaveHookRegistry` を組み立ててリポジトリのコンストラクタへ渡します。フックは追加順に発火し、DI 版のレジストリと同じ挙動です:

```csharp
var hooks = new SaveHookRegistry()
    .Add<DocumentEntity>(new DocumentSaveHook())
    .Add<OrderEntity>(new OrderSaveHook());

var documents = new DocumentRepository(connectionFactory, hooks);
```

レジストリの組み立てはスレッドセーフではありません（リポジトリへ渡す前に全フックを追加してください）。渡した後の解決は読み取り専用です。

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

値がコレクション（`string` / `byte[]` を除く列挙可能なもの）のパラメータは `IN` 用に展開されます。SQL には `IN (@ids)` と括弧の中に書き、各要素が `@ids0, @ids1, ...` として束縛され、SQL 中の `@ids` もそれに合わせて書き換えられます。

```csharp
var rows = await customers.QueryBySqlAsync(
    "SELECT * FROM customers WHERE customer_id IN (@ids)", new { ids = new[] { 1, 2, 3 } });
```

展開について 2 点だけ注意があります。

- **空コレクションは `(NULL)` へ展開されます。** `IN` なら「何にも一致しない」で正しいのですが、`NOT IN (@ids)` は罠です。`x NOT IN (NULL)` は全行 UNKNOWN になるため**どの行も一致しません**＝「除外リストが空」の意味と正反対になります。空になり得るなら SQL 自体を分岐してください。
- **書き換えはテキスト置換です。** コマンドテキスト中の `@name` を、文字列リテラルやコメントの中も含めて置換します。それらの中にパラメータ名そのものを書かないでください。先頭が同じだけの名前（`@idsSuffix` / `@ids0`）や、末尾が同じだけのシステム変数（`@@ids`）は置換されません。

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

エラーは登録したチェックの持ち物で、各チェックは自分が付けたものだけを付け外しします。

- **バインディングのセッター**が変換エラー・値オブジェクトエラーを持ちます（`SetError`）。再生成できるのはセッターだけなので、他のチェックは消しません。
- **必須チェック**（生成される `ValidateSelf`）は、そのプロパティに他の入力エラーが無いときだけ未入力エラーを付けます（変換できない文字列が入っている欄を「必須です」で塗り潰しません）。値が入れば自分のエラーを消します（バインディング経由ではなく確定値へ直接代入した場合も同じ）。
- **2 つの重複チェック**は、1 つのプロパティ上にそれぞれ専用のスロット（`DuplicateErrorSource`＝コレクション要素どうしの検証は `Siblings`、DB の既存行との照合は `Database`）を持ちます。互いのスロットを上書きもクリアもしないため、兄弟間でも DB でも重複している値は 2 つの所見をそのまま報告し、各所見はそれを見つけたチェックが報告しなくなった時点で消えます。保存前にグラフ全体を `Validate` しても、直前の DB 照合の結果が消えることはありません（逆も同様）。
- **確定値を編集すると、その EditModel の `Database` 側の所見は取り下げられます。** 照合したのは編集前の値だからで、複合制約は構成列すべての組で判定しているため、1 列でも変われば同じモデルの DB 由来の所見はすべて対象です。`Siblings` 側は次の検証が判断するのでそのまま残ります。
- **`OnValidate`** が登録したエラーはフックの持ち物です。条件が解消したらフック側で消してください（`SetError` に null を渡す）。

`RevertInput()` は入力文字列を作り直して入力エラーだけを消します。Mapper のロードはさらに両方の重複エラーも消します（判定の対象だった値そのものが入れ替わるためです）。

#### EditModel: DB との照合

EditModel と Repository 契約の両方を生成する構成では、各 EditModel に糖衣メソッドも生成されます:

```csharp
// 引数の型はリモート契約を生成する構成なら I{Entity}RemoteRepository、そうでなければ I{Entity}Repository
if (!await editModel.ValidateUniqueAsync(repository))
{
    // エラーはバインディングプロパティへ登録済み（INotifyDataErrorInfo により UI へ表示される）
}
```

EditModel の確定値から Entity を組み立てて `CheckUniquenessAsync` を呼び、各違反の `PropertyNames` をバインディングプロパティ名へ写します。構成列を持たない違反（および EditModel に無いプロパティ名の違反）は、空のプロパティ名で登録されるモデルレベルエラーになり、`GetErrors(null)` で取得できます。呼び出しの先頭で前回の重複エラーを消すため、再検証で古いエラーが残ることはありません（消すのは自分が付けた分だけなので、要素どうしの検証が報告したエラーは残ります）。

エラーの登録は `await` の後＝呼び出し元のスレッドではなくスレッドプール上で行われ、`ErrorsChanged` も同じスレッドで発火します。WPF のバインディングエンジンは通知を UI スレッドへ自動でマーシャルするため通常は何もする必要がありませんが、UI の状態を直接更新する購読者は呼び出し側でマーシャルしてください。

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
- **書き込み除外は SQL Server だけの話です。** 値を採番するのは SQL Server だけなので、除外するのもそのエンジンだけです。マルチターゲット生成（`--repository-dialects sqlserver,sqlite`）では、SQLite エンジンは同じ列を通常のバイナリ列として INSERT / BulkInsert / UPDATE で書き込みます（ローカル側がサーバーの版を写して持つ場所になります）。[マルチターゲット Repository](#マルチターゲット-repositorysqlserver--sqlite) を参照してください。

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

`SaveConflictException` は再試行に必要な材料を構造化して持つため、メッセージを解析する必要はありません: `Reason`（`NotFound`＝行が消えた／`Modified`＝行はあるが版が進んだ）・`EntityTypeName`・`Key`。この情報はリモート転送（HTTP 409）でも復元されるため、直結でもリモートでも呼び出し側は同じプロパティを読めます。

競合への通常の対処は、再取得して適用し直すことです:

```csharp
try
{
    await repository.UpdateAsync(order, cancellationToken: ct);
}
catch (SaveConflictException ex) when (ex.Reason == SaveConflictReason.Modified)
{
    var current = await repository.GetByIdAsync(order.OrderId, ct);   // 勝った側の版を読み直す
    current!.Memo = order.Memo;                                       // その上へ自分の編集を当て直す
    await repository.UpdateAsync(current, cancellationToken: ct);     // 今度は新しい版で守られる
}
```

再取得して当て直すのは、2 つの編集をマージできる場合の素直な答えです。マージできず、この書き込みを無条件に通したい場合が `ForceOverwrite` です（最初から版の条件を外すので、読み直しもマージも行いません）。

- **「行なし」と「版が古い」は別の結果です。** 単一の `UpdateAsync` は行が存在しなければ従来どおり `false` を返し、行はあるが版が進んでいれば `SaveConflictException` を送出します。`insertWhenUpdateMissing: true` も同じ線引きで、行なしは INSERT へ切り替わり、版が古い場合は競合として報告されます（INSERT へ倒すと競合が主キー重複に化けるためです）。
- **グラフ保存は削除も守り**、競合が 1 件でもあれば保存単位の全体がロールバックされます（インメモリ Repository は書き込みをステージングして一括公開する方式で同じ結果になります＝失敗した保存はそもそもストアへ届きません）。
- **インメモリは公開時にもう一度検証します。** Save フックはストアのロック外で走るため、保存が起点にした行を他者が先に書き換えている場合があり、公開時にそれを検出して `SaveConflictException` にします（他者の書き込みは無傷のまま残ります）。rowversion 列を**持たない**型はこの検証の対象外で、並行性トークンが無い以上ストアの契約は後勝ちのままです。`ForceOverwrite` も同じ理由で検証を外します。保存が挿入する行だけは必ず検証します（先に取られた主キーは並行性の判断ではなく主キー重複だからです）。なお「行がまだそこにあるか」は版の比較より先に、しかもモードにも rowversion 列の有無にも依らず判定します。その間に削除されていた行は `SaveConflictReason.NotFound` になり（無くなった行に対して版を比べても何も言えないためです）、その行への staged 削除は競合になりません。
- **新しい版が反映されます。** 挿入・更新・グラフ保存が成功すると、エンティティは DB が採番した版を保持するため、再取得せずに同じインスタンスをそのまま保存できます。Save フックの `AfterSaveAsync` はコミット前に走るため、この時点ではまだ古い版が見えます。
- **どのバックエンドでも契約は同じです。** QuickER 版 Repository は `WHERE ... AND <rowversion> = @original` で文を守り `OUTPUT` 句で新しい版を読み戻します。EF Core は自前の並行性トークン（`IsRowVersion()`）を使い `DbUpdateConcurrencyException` を同じ例外へ変換します。インメモリ Repository は単調増加する 8 バイトの擬似版で DB を模します。HTTP リモートクライアントはモードをリクエストへ載せ、応答が返す版を書き戻します。

既知の制限:

- `rowversion` 型を持つのは SQL Server だけのため、QuickER 版 Repository では `sqlserver` 方言のみが対象です。SQLite（や他方言）**単独**向けの図にはそもそも該当列がないため影響しません。`sqlserver` を含む**マルチターゲット**生成では列そのものは他方言と共有されますが、版で守るのは SQL Server エンジンだけです（[マルチターゲット Repository](#マルチターゲット-repositorysqlserver--sqlite)）。
- `BulkInsertAsync` は `SqlBulkCopy` を使い、生成値を返せないためエンティティの版は元のままです。後続の更新で版が要る場合は再取得してください。
- 版の読み戻しには `OUTPUT` 句を使いますが、SQL Server はトリガーのあるテーブルでこれを拒否します。QuickER の DDL 生成はトリガーを出力しないため、QuickER 外でトリガーを足したテーブルにのみ関係します。
- 既に消えている行の削除は、従来どおりバックエンド間で非対称です。QuickER 版 Repository は黙って許容し、EF Core のグラフ保存は `SaveConflictException` として報告します。
- rowversion 列には `[DbColumnMeta]` のトークンが付かないため、C# リバースでは復元されません（図の側で宣言してください）。
- **キー指定の削除は版で守られません。** `DeleteAsync(id)` はエンティティではなくキーを受け取るため比較すべき版がなく、現在の版が何であれ行は削除されます。読んだ版で削除を守りたい場合は、エンティティを `MarkRemoved()` してグラフ保存してください（グラフ保存は削除もエンティティが読んだ版で守ります）。
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
- **双方向同期でも行の転送からは外れます**。列単位でコピーさせる方法は同期支援の[無制限バイナリ列](#無制限バイナリ列)を参照してください

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

> **注意**: DB 間のデータ移送では注意が必要です。`GetAllAsync` して `BulkInsertAsync` する形では、除外列は取得時のまま（`null`、非 nullable 列なら空配列）書き込まれます。INSERT は全列を対象にするため例外にもならず、移送先で BLOB だけが黙って失われます（UPDATE のガードは効きません。あれは「除外列に値が残っている」ときに発動するもので、移送が運ぶのはその逆の状態です）。移送では `Query().WithUnboundedBinary()` で読むか、行をコピーしてから `Read/Write{Column}Async` の Stream アクセサで BLOB を個別に移送してください（後者は BLOB 全体をメモリに載せない唯一の手段でもあります）。

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
- 効果があるのは、エンティティ形の取得（`ToListAsync` / `FirstOrDefaultAsync`）と、`ToProjectionListAsync` が**エンティティを全列取得してから射影するフォールバック経路**（セレクタから列を抽出できない場合や `Include` 併用時）です。件数・存在確認と、サーバー側で列を刈り込める射影には影響しません（後者は参照した列を除外列も含めて取得済みのためです）。
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

### マルチターゲットでの rowversion 列

`rowversion` 列は方言ごとに別の C# 型へ解決されます（SQL Server は `byte[]`、SQLite は日時または未知の型）が、共有 Entity は 1 つの型しか持てません。QuickER はこれを行バージョンの解決＝`byte[]`＋`[StoreGeneratedColumn]` へ統一し、型不一致エラーで止める代わりに、統一した列を Info 診断で通知します。統一後の両者は別のものを意味しますが、その違いこそが狙いです:

| 側 | 列の意味 | 書き込み | 版ガード |
|---|---|---|---|
| SQL Server（サーバー） | DB が採番する並行性トークン | INSERT / BulkInsert / UPDATE の対象外。採番された版はエンティティへ書き戻される | あり（古い版は `SaveConflictException`） |
| SQLite（ローカル） | 通常のバイナリ列 | 他の列と同じく INSERT / BulkInsert / UPDATE が書き込む | **なし**（エンティティが持つ値のまま書かれる） |

このため、この列はローカル側がサーバーの版を写して持つ場所として使えます。サーバーから行を（版込みで）読んでそのままローカルへ格納し、後でその版をサーバー側更新のガード値として送り返す、という流れです。ローカルで作った行はまだ版を持たないため、方言切替では同時に NOT NULL も解除されます（[方言切替](database.ja.md#方言切替)）。

既知の制限:

- **ローカル側は守られません。** SQLite では版ガードが働かないため、ローカルの書き手どうしは依然として上書きし合います。そこでの版はロックではなくデータです。
- **ミラーの鮮度は誰も保証しません。** 列には最後に書かれた値が入っているだけです。同期を飛ばしたまま古い値で押し戻せばサーバーが競合として拒否しますが、これは意図した結果です（再取得して適用し直してください）。
- **ローカル側の `ForceOverwrite` は no-op です**（外すべきガードがありません）。
- EF Core 版 Repository はマルチターゲットと併用できない（診断エラー）ため、ここで説明したミラーは QuickER 版 Repository の話です。

ミラーを実際に同期させる処理は、この 2 つの Repository を使って自分で書くこともできますし、[双方向同期の支援](#双方向同期の支援--generate-sync-support)に生成させることもできます。

## 双方向同期の支援（--generate-sync-support）

上のマルチターゲット構成は、ローカルにサーバーの版をミラーする場所を与えます。`--generate-sync-support`（quicker.json の `GenerateSyncSupport`・GUI では対象 DB を両方選んだときに現れる「双方向同期の支援コードを生成する」チェック）は、その 2 つを実際に同期させる仕掛けを、サーバーを正として生成します。

前提は「実効方言が `sqlserver`（サーバー）と `sqlite`（ローカル）のちょうど 2 つ」「QuickER 版 Repository の実装を生成する」「同期可能なテーブル（Repository 契約が生成される＝単一の主キー列を持つテーブル）が 1 つ以上ある」の 3 つです。**同期対象はその全テーブル**で、`rowversion` 列の有無は対象かどうかではなく**モード**を決めます——列を持つテーブルは増分ダウンロード＋版ガード付きの再生（この節の既定の話）、持たないテーブルは[後勝ちモード](#版なしテーブルと後勝ちモードsyncmodelastwritewins)専用です。拾われたテーブルは生成時の Info 診断に一覧され、版なしのものはそこで名指しされます。rowversion 列が両者で何を意味するかは[マルチターゲットでの rowversion 列](#マルチターゲットでの-rowversion-列)にあり、この節はその上に載る仕掛けの話です。

### どこに何を作るか

サーバー側には**追加スキーマを一切作りません**。ローカルにだけ共有テーブル `quicker_sync_journal` を初回利用時に `CREATE TABLE IF NOT EXISTS` で用意し、オフライン編集（テーブル・キー・操作・削除時はその行が持っていた版）を記録します。

再開点は**保存せず導出**します。ローカル行のミラー版の最大値がそれで、データと食い違い得る管理行が存在しません。この導出が正しいことは 2 つの性質に支えられており、どちらも外せません——サーバーの変更は版の昇順で取得し、バッチはローカル 1 トランザクションで適用する。どこで中断してもローカルには順序付きストリームの接頭辞だけが残り、その最大値が次回の再開点そのものになります。ローカルで作られてまだアップロードしていない行はミラー版を持たないため、最大値から自然に外れます。

1 回のパスの上限は `MIN_ACTIVE_ROWVERSION()` で、実行ごとに 1 回だけ取得します。後からコミットされた行が先にコミットされた行より小さい版を持ち得るため、「現在の最大値まで」で読むと未コミットの行を跨いで読み飛ばし、二度と戻ってこられなくなるからです。

### 組み立て方

```csharp
services.AddGeneratedSqlServerRepositories(serviceKey: "server", sqlServerConn);
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);
services.AddGeneratedSyncSupport(serverServiceKey: "server", localServiceKey: "local");

// キーで解決するローカルのリポジトリは、全書き込みを記録するデコレータへ差し替わっている
var local = provider.GetRequiredKeyedService<ICustomerRepository>("local");

var result = await provider.GetRequiredService<SyncEngine>().SyncAsync(cancellationToken: ct);

if (result.HasConflicts)
{
    foreach (var conflict in result.Conflicts)
    {
        // conflict はテーブル・キー・操作・理由・両者の行を持つ
    }
}
```

DI 登録は 2 つの半分に分かれており、サーバー側の半分をどちらにするかだけで転送経路が決まります。

| 呼び出し | 登録されるもの |
|---|---|
| `AddGeneratedSyncEngine(localServiceKey)` | ローカル側の半分＝ジャーナル・テーブル記述子・エンジン・ローカル Repository を包むジャーナル記録デコレータ |
| `AddGeneratedDirectSyncSources(serverServiceKey)` | サーバー側の半分（このプロセスが持つ DB 接続で読む） |
| `AddGeneratedHttpSyncSources(baseAddress)` / `(httpClientFactory)` | サーバー側の半分（HTTP 越しに読む。下記） |
| `AddGeneratedSyncSupport(serverServiceKey, localServiceKey)` | 上の 2 つを合わせた全直結構成（両方の DB が同一プロセスから届く、いちばん多い形） |

キー引数はいずれも `null` を受け取れ、これは「値が null のキー」ではなく**非 keyed の通常登録**を指します（keyed 登録は null キーを取れないため、両者が衝突することはありません）。サーバー側の半分を呼び忘れても黙って壊れることはなく、エンジンの解決がソース不在で失敗します。

1 回の実行はアップロードが先、ダウンロードが後です。テーブルは外部キー順（書き込みは親から・削除は子から）で巡ります。つまみは `SyncOptions` です:

| オプション | 既定 | 意味 |
|---|---|---|
| `Mode` | `Versioned` | ランの意味論。既定＝版ありテーブルだけを増分＋版ガードで同期し、版なしテーブルには触れない（従来どおり）。`LastWriteWins` は[後勝ちモード](#版なしテーブルと後勝ちモードsyncmodelastwritewins) |
| `ExcludedEntityTypes` | 空 | 今回のランから外すエンティティ型。記録は続き、次の対象ランで回収される（恒久的に外すのは構築時の `excludeFromSync`）。エンジンが同期しない型の指定は `ArgumentException` |
| `DownloadBatchSize` | 500 | 1 バッチで取得し、ローカル 1 トランザクションで適用する行数 |
| `PropagateDeletes` | `true` | サーバーから消えたキーのローカル行を削除するか。判定はキー全比較で、サーバーの各テーブルをキーだけ 1 回走査するコストがかかるため、テーブルが大きいときは off にして低頻度で回す。未送信のジャーナルエントリを持つキーは対象外（下記） |
| `ConflictPolicy` | `Collect` | サーバーと衝突したローカル変更の扱い（[競合](#競合)） |
| `IncludeUnboundedBinary` | `false` | 行の転送から外れる無制限バイナリ列も運ぶか（[無制限バイナリ列](#無制限バイナリ列)）。除外列を持たない生成物では意味を持ちません |

実行結果は `SyncResult` が報告します:

| メンバー | 意味 |
|---|---|
| `Uploaded` | サーバーへ届いたローカル変更 |
| `Downloaded` | ローカルへ適用したサーバー行 |
| `DeletedLocally` | サーバーにキーが無くなったため削除したローカル行 |
| `Discarded` | 送らずに決着した変更＝行が既に無い陳腐化した意図、または `ServerWins` でジャーナルごと捨てた分 |
| `Conflicts` / `HasConflicts` | 再生できなかったローカル変更（ジャーナルに残る） |

**削除伝搬は、ジャーナルがまだ語っている行に手を出しません。** 何かを消す前にジャーナルを読み、未送信のエントリを持つキーは残します——同じ実行が競合として報告したばかりのキーも含みます。これが、`Collect` が「サーバーにその行が無い」種類の競合を、行を消すことでサーバー側の勝ちとして黙って決着させてしまわない理由です。エントリが解決されれば（送信成功・陳腐化した意図としての破棄・`ServerWins` による破棄）、以降の実行では従来どおり削除が伝搬されます。守れる範囲はジャーナルが見えている範囲と正確に一致します＝別経路で同期対象テーブルへ書かれた行にはエントリが無く、伝搬が有効なら削除されます（[既知の割り切り](#既知の割り切り)）。

1 件の再生の結果は 2 通りではなく**3 通り**で、`Uploaded` / `Discarded` / `Conflicts` がちょうどその 3 つです（送った／送るものが無かった／拒まれた）。真ん中をどちらかへ畳むと、何も送っていないのに「変更を届けた」と報告することになります。`Uploaded` と `Discarded` が数えるのはジャーナルのエントリではなく行です（オフラインで何度も編集した行は最新の意図 1 件へ畳まれます）。ただし何も送らない `ServerWins` だけは、捨てたエントリ数がそのまま `Discarded` になります。

### HTTP 越しにサーバーへ届く

`--generate-sync-support` と [`--generate-remote-services`](#リモートサービス--generate-remote-services-3-階層構成) を併用すると、DB 接続の代わりに HTTP でサーバーへ届くクライアントが加わります。どちらに差し替えても変わるのは登録 1 行だけです（エンジンはどちらでも同じ `ISyncServerSource<TEntity, TKey>` を解決するため）。

```csharp
// クライアント側: ローカルは従来どおり・サーバー側の半分だけが HTTP になる
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);
services.AddGeneratedHttpSyncSources("https://example.com/quicker");   // HttpClient ファクトリ版もある
services.AddGeneratedSyncEngine(localServiceKey: "local");

// サーバー側: 通常の Repository ＋ エンドポイントが答えるためのソース ＋ エンドポイントのマップ
services.AddGeneratedSqlServerRepositories(sqlServerConn);
services.AddGeneratedDirectSyncSources(serverServiceKey: null);
app.MapGeneratedRemoteEndpoints(RemoteAccess.RequireAuthorization);
```

既存のエンドポイントグループへ 3 本（`POST {prefix}/{エンティティ}/…` の `SyncCeiling` / `SyncChanges` / `SyncKeys`）が加わります。これは差分ソースの薄い remoting で、各ハンドラは DI から `ISyncServerSource<,>` を解決して呼ぶだけ＝意味論の実装は 1 つを両経路が共有します。この登録は `MapGeneratedRemoteEndpoints` がマップ時に同期対象テーブルごとに検査するため、`AddGeneratedDirectSyncSources` を呼び忘れたサーバーは、正常に起動して CRUD を全部答えたうえで最初の同期でだけ落ちるのではなく、起動時に足りないソースを名指しして失敗します。グループのメンバーなので、マップ時に選んだ `RemoteAccess.RequireAuthorization`（やグループへ後付けしたポリシー）はそのまま効きます。アップロードは新しい経路を作らず、既存の CRUD／保存エンドポイントを通ります（`ConcurrencyMode` は既に転送され、版の競合は 409 として返ります）。

サーバーは**クライアント別の状態を持ちません**。再開点はリクエストの anchor、上限は ceiling として毎回送られます。その裏返しとして上限は呼び出し側の値であり、導出アンカーの保証は「その回の `SyncCeiling` が返した値をそのまま送り返す」限りで成立します（自前で大きな ceiling を送ると、その下で実行中のトランザクションの行を恒久的に読み飛ばします）。バッチサイズが 0 以下なら 400 で拒否します。上限は設けていません——取り放題を止めるのはグループの認可の仕事だからです。

### ローカル編集の捕まえ方

生成される `Journaling{Entity}Repository` がローカルのリポジトリを包み、**全書き込み入口**（`InsertAsync` / `UpdateAsync` / `DeleteAsync` / `BulkInsertAsync` / `SaveAsync` 2 種）で記録します。保存フックでは足りません——グラフ保存でしか発火せず、直接の挿入や削除が素通りしてしまいます。

`SaveAsync` のグラフ保存は**カスケード全体**を記録します。生成される `SyncGraphRecorder` が、保存側（グラフセーバー）と同じカスケードナビゲーションを同じ規則で辿り、保存が書く・消す子孫の行をルートと同じように記録します——Unchanged のルート配下で子だけを編集した保存も、カスケード削除で一緒に消える子も漏れません。記録は保存の前にグラフ全体ぶん行われるため、途中で保存が失敗しても余分なエントリは（単独の書き込みと同じく）アップロード時に無害化されます。

記録は業務書き込みの**前**に行います。生成 Repository は接続を自分で管理するため、デコレータの INSERT を包んだ書き込みのトランザクションへ乗せられません。どちらかを先にせざるを得ず、意図を先に記録する方が安全です。業務書き込みが失敗した場合、ジャーナルには書かれなかった行のエントリが残りますが、アップロードはローカルの現在行を読み直して送るため「送るものが無い」として破棄されます。逆順にすると変更がそのまま失われます。

**生 SQL は記録されません。** `ExecuteSqlAsync` は素通しで、文の形からはどの行が変わったかが読めないためジャーナルに書くキーがありません。`Query().ExecuteDeleteAsync`（条件一括削除）も同じです——どの行が消えるかは述語が決め、デコレータからは見えません。これらの経路で変えた行は、別の何かが記録しない限りサーバーへ届きません。そして、その経路で**作った**行はもう一段損をします＝守ってくれるジャーナルエントリが無く、サーバーにもそのキーが無いため、次回の実行で削除伝搬に消されます。ローカル DB が自分だけで持ち続けたい行は、構築時に同期から除外したテーブル（`AddGeneratedSyncEngine` の `excludeFromSync`）へ置いてください。

**保存フックには影響しません。** デコレータは包んだ Repository へそのまま委譲するため、`ISaveHook<T>` は従来どおり発火します——同期中にエンジン自身が適用する行に対しても発火します（同期の実行が抑制するのはジャーナル記録だけです）。洗い替え（下記）は `BulkInsertAsync` で書くため、通常の契約どおりフックは発火しません。1 つ知っておく価値があるのは、`BeforeSaveAsync` が `false` を返して止めた書き込みでもジャーナルにはエントリが残ることです（記録が先に走るため）。挿入なら読む行が無いので破棄され、更新ならその行が現在の内容（＝編集前のまま）で送られます。サーバーはそれを受け入れ、新しい版を採番します。

### 無制限バイナリ列

`--generate-sync-support` と [`--exclude-unbounded-binary-columns`](#無制限バイナリ列の除外excludeunboundedbinarycolumns) は併用できます。同期対象テーブルにある除外列は生成時に Info 診断で名指しされます。名指しする理由は、**同期が読み書きする「行」にそれらの列が入っていない**ためです（差分取得の SELECT は残りの列を明示列挙し、UPDATE は除外列に触れません）。

既定（`IncludeUnboundedBinary = false`）ではその帰結が 3 つあり、意外なのは 3 つ目です。

- サーバーから**降りてきた行**には blob が入っていません。
- サーバーへ**上げる行**にも blob は載りません。
- 受け取り側に**既にある** blob は残ります（更新は除外列に触れないため）。**ただし、その側にとって新しい行には残す中身が無く、列は空のまま届きます。** 「blob は温存される」は既にある行についてだけ真で、降りてきたばかりの行では偽です。空のローカル DB への初回同期では、したがって blob はすべて空になります。

`SyncOptions.IncludeUnboundedBinary` を立てると、行の転送のあとに列を 1 本ずつ、両方向でコピーします。

```csharp
var result = await engine.SyncAsync(new SyncOptions { IncludeUnboundedBinary = true }, ct);
```

コピーは一時ファイルを経由するため、どちらの側も blob をメモリへ載せません（読みは渡されたストリームへ押し出し、書きは渡されたストリームから引き出す形なので、両者を繋ぐには間に何かが要ります。ファイルなら、書き込みが前もって必要とする長さもそのまま得られます）。このファイルは OS の一時フォルダ（`Path.GetTempPath()`）に作られ、列を書き終えた時点で削除されます。機密の blob を扱う場合は知っておく必要があります＝[ストリーミングアクセサ](#無制限バイナリ列の除外excludeunboundedbinarycolumns)のファイル糖衣は置き場所を呼び出し側が選ぶのに対し、ここは自動で、blob が暗号化されないままディスクへ落ちる唯一の箇所です。HTTP 経路では[ストリーミングアクセサ](#無制限バイナリ列の除外excludeunboundedbinarycolumns)が使う既存のエンドポイント `GET`/`PUT`/`DELETE {prefix}/{エンティティ}/{列名}?id=` をそのまま使うため、新しいルートは増えません。

列を別々にコピーすることから、2 点が従います。

- **コピー元が NULL なら、コピー先も NULL にします。** この機能の目的は両側を揃えることなので、サーバー側に blob が無い行はローカルの blob を残さず消します。
- **アップロードの後にサーバーの版を読み直します。** blob の書き込みも行への書き込みなので、サーバーの版は挿入・更新が返した値よりさらに進みます。古い版をミラーへ書くとローカルのアンカーが行の現在版より下に留まり、次のダウンロードがその行を「サーバー側の変更」として返してきます。

**blob だけの編集も追跡されます。** `Write{列}Async`（およびファイル糖衣）は他の書き込みと同じくジャーナル記録デコレータを通り、意図を先に記録します。したがって「blob しか変えていないオフライン編集」もサーバーへ届きます。記録は `IncludeUnboundedBinary` に依存しません（何を送るかは送信時の判断で、生成時には決まらないため）。既定のまま同期すれば、行だけが送られてエントリは片付きます。

代償は「変更行 1 件・列 1 本につき 1 往復」です。既定を OFF にしているのはこのためで、大きな blob を持つ表の行が頻繁に変わる構成では毎回その分を払うことになります。

### 競合

黙って解決することはありません。既定では、サーバーと衝突したローカル変更はジャーナルに残り、テーブル・キー・操作・理由・両者の行を添えて `SyncResult.Conflicts` に返ります。理由が `MissingOnServer`（サーバー側でその行が消えていた）の場合も含め、ローカルの行はその場に残ります＝削除伝搬はジャーナルにエントリのあるキーを対象外にするためです。

| `SyncConflictPolicy` | 挙動 |
|---|---|
| `Collect`（既定） | エントリをジャーナルに残して報告する（判断してから再実行） |
| `ServerWins` | ランのスコープ内のジャーナルを捨て、ダウンロードがサーバー行でローカルを上書きする（スコープ外＝版なしテーブルや除外テーブルのエントリは残る） |
| `LocalWins` | `ConcurrencyMode.ForceOverwrite` で再送し、サーバー行を上書きする |

### 版なしテーブルと後勝ちモード（SyncMode.LastWriteWins）

`rowversion` 列を持たないテーブルには差分の手掛かりも版ガードの原本もありません。それでも同期したい——ほとんど変わらないマスタ系のテーブルをサーバー列 1 本足さずに配りたい——ときの答えが後勝ちモードです。

```csharp
var result = await engine.SyncAsync(new SyncOptions { Mode = SyncMode.LastWriteWins }, ct);
```

**既定（`Versioned`）のランは版なしテーブルに一切触れません**——ダウンロードもアップロードも削除伝搬もしません（記録だけは続き、エントリは後勝ちランが回収します）。つまり版なしテーブルを図に足しても、既定の同期の実行時挙動は従来と同一です。

`LastWriteWins` を指定したランは**全テーブル**を対象にし、意味論をラン全体で 1 つに揃えます:

- **アップロードは全テーブル一律**の「`ForceOverwrite` で更新→行が無ければ挿入・削除は無条件」です。版の読み取りも事前の実在確認もなく、**何も競合として報告されません**（`Conflicts` は常に空・`ConflictPolicy` は無視されます）。版ありテーブルもこのランでは版ガードなしで上書きします（ミラー版の書き戻しは従来どおり行われ、次のランが自分の変更を取り戻すことはありません）。
- **勝つのは「後から編集した側」ではなく「後からアップロードした側」**です。すれ違いで失われた更新は検出も報告もされません——それがこのモードの名づけた取引です。
- **削除とのすれ違いは行を復活させます。** サーバー側で消えた行をオフラインで編集していた場合、アップロード（更新→行なし→挿入）がその行を蘇らせます。逆にローカルの削除はサーバーの更新を消します。後勝ちとして一貫した挙動です。
- **版なしテーブルのダウンロードは毎回、キー昇順の全量スキャン**です（`SELECT TOP … WHERE キー > @afterKey ORDER BY キー` のページング＋既存の削除伝搬）。コストは実行ごとに O(テーブル) ＝このモードが小さくて変化の少ないテーブル向けである理由で、**大きい・忙しいテーブルはサーバーへ rowversion 列を 1 本足して増分側に乗せる**のが正しい住み分けです。版ありテーブルは後勝ちランでも従来どおり増分で降ります（結果は同じで転送が小さいだけです）。

**恒久的に同期から外すテーブル（ローカル専用）は、実行時ではなく構築時に宣言します:**

```csharp
services.AddGeneratedSyncSupport("server", "local", excludeFromSync: [typeof(LocalCacheEntity)]);
```

構築時に除外したテーブルはジャーナル記録デコレータで包まれず（記録ゼロ＝書き込みの追加コストもゼロ）、記述子も登録されません（どのランのダウンロード・削除伝搬・洗い替えにも入りません）。同期対象でない型の指定と全テーブルの除外は、最初のランではなく登録時に `ArgumentException` で拒否されます。`SyncOptions.ExcludedEntityTypes` はこれと高度が違い、**今回のランから外すだけ**です（記録は続き、次の対象ランが回収します）。

除外へ切り替える前に積まれていたエントリは、どのランにも回収されないまま未送信として数えられ続け、洗い替えを恒久的に阻みます。ジャーナルの掃除はそのためにあります——`SyncJournal.RemoveTableAsync(テーブル名)` が 1 テーブル分、`RemoveAllAsync()` が全部を破棄します（どちらも「そのローカル編集はもうサーバーへ届かない」という明示の宣言で、同期の実行中には呼ばないでください）。

最後に位相の注意を 1 つ。版なしテーブルが版ありテーブルを FK で参照する構成は 1 回の後勝ちランの中では FK 順で自然に整合しますが、**構築時に除外したテーブルと同期テーブルの間の FK** は誰も守りません（除外した親の行をアプリが自前で維持する構成は成立し得るため、ブロックせずこの注意に留めています）。

### ローカル DB の作り直し（RefreshAsync）

`SyncEngine.RefreshAsync` は同期対象テーブルを全消しし、サーバーの行で入れ直します。用途は**ローカル DB の初回構築・失われた（壊れた）DB の復旧・長期間未同期で 1 行ずつ追いつく価値が無くなったときの作り直し**で、増分同期のためのものではありません（そちらは `SyncAsync` です）。

```csharp
var refreshed = await engine.RefreshAsync(new SyncRefreshOptions { BatchSize = 2000 }, ct);

// refreshed.Tables はテーブル別の Deleted / Inserted（行を書いた順）、
// refreshed.Deleted / .Inserted はその合計、.Elapsed は実行時間
```

未送信のローカル変更は失わずに拒否します。ジャーナルが空でなければ**何も消す前に** `SyncPendingChangesException` を送出し、テーブル別の内訳（`PendingChanges`・`PendingCount`）を添えます。呼び出し側は先に `SyncAsync` で送ってから洗い替えられます。`SyncRefreshOptions.Force` は「捨ててよい」という明示の指定で、捨てた件数は `SyncRefreshResult.DiscardedChanges` に出ます。

ローカルの blob も同じ扱いで拒否します。同期対象テーブルが[無制限バイナリ列](#無制限バイナリ列)を持つ場合（行の転送に載らない＝作り直しでは戻ってこない）、**何も消す前に** `SyncUnboundedBinaryLossException` を送出し、テーブルごとに列を名指しします。答えは 2 つのフラグのどちらかで、いずれかの指定が要ります。

| `SyncRefreshOptions` | 既定 | 意味 |
|---|---|---|
| `Mode` | `LastWriteWins` | 洗い替えが覆うテーブル。**既定は全テーブル**（洗い替えは「ローカルをサーバーの姿にする」操作で、版なしテーブルを置き去りにすると版あり親の全消しがその FK に阻まれるため）。`Versioned` は版ありだけへ絞る明示指定で、版なし行が作り直す親を参照していない構成でのみ健全です。未送信の拒否・`Force` の破棄もこのスコープ内のエントリだけを数えます |
| `ExcludedEntityTypes` | 空 | この洗い替えから外すエンティティ型（`SyncOptions` と同じラン単位の除外） |
| `IncludeUnboundedBinary` | `false` | 行を書いたあとに除外列も降ろし直す（作り直したローカルが完全な複製になる） |
| `DiscardLocalUnboundedBinaries` | `false` | 損失を受け入れる（blob が作り直せるローカルキャッシュのときの答え）。損失を許可するだけで、何かを降ろし直すわけではありません |

版なしテーブルの流し込みは版の昇順ではなく**キーの昇順**で進みます（差分ダウンロードと同じページング）。再開点の性質もキー基準で同じ形が成り立つため、途中で落ちた洗い替えは再実行で直ります。

除外列を持たない生成物ではこの例外は起きないため、挙動は従来どおりです。

`BatchSize` の既定は **2000** で、通常同期のダウンロードより数倍大きくしてあります。1 バッチ＝1 ローカルトランザクションであり、洗い替えの速さはほぼここから来ること、そして途中で落ちても再実行で直るので細かい再開粒度の価値が低いことが理由です。上げる代償はメモリ（1 バッチを丸ごと保持する）と、HTTP 経由なら 1 応答の本文サイズです。大きなバイナリ列を持つテーブルは、上げるのではなく下げたい側です。

速いのは、やらないことがあるからです——置き換える行との比較なし・バッチごとのアンカー導出なし・消えた行を探すキー集合の取得なし・ジャーナルの再生なし。親子 2 テーブル・2 万行・出荷時の既定どうしの実測で、通常同期の **3〜4.5 倍速**でした（両方の DB がローカルのときが上側、実 SQL Server をサーバーにしたときで 3 倍前後）。この比には構造的な上限があります。サーバーから行を読み出す時間は、どちらの経路も等しく払うためです。

**通常同期の安い代替ではありません。** 転送するのは同期対象テーブルの**全行**なので、低速回線かつ大きなテーブルでは転送が支配的になり、2 回目以降は「変わった分だけ運ぶ」通常同期の方が安くなります。ローカル専用のテーブル（構築時に `excludeFromSync` で除外したもの）は対象外で、まったく手を触れません。

**実行全体は 1 トランザクションではありません。** 生成 Repository は接続を自分で管理するため、ここで作ったトランザクションに乗せられないからです。代わりに成り立っているのは「コミットするどの時点も、後の実行が再開できる状態である」ことです。削除は子から・書き戻しは親から進むので外部キーが宙に浮くことは無く、各テーブルの行は版の昇順で届くので、途中で止まったテーブルは「その版以下の全行」を持っています——これは導出アンカーが指す状態そのものです。単一トランザクションとの唯一の差は、**作り直しの途中のローカル DB が見える**ことです。最初の削除から最後のテーブルの最終行までの間、読み手にはどちらの DB より少ない行が見えます。失われるものはありませんが、画面が動いている裏で流す操作ではありません。

### 既知の割り切り

- **Repository を通らない書き込みは追跡されません。** 上記の生 SQL（`ExecuteSqlAsync`）はもちろん、別経路でローカル DB へ届く書き込みも同様です。ジャーナルが見えるのはデコレータが包む書き込み入口だけで、見えないものは守れません＝その経路で作った行は「上がらない」だけでなく、`PropagateDeletes` が有効な限り**次回の実行で削除されます**（サーバーにそのキーが無いためです）。ローカル専用の行は、構築時に `excludeFromSync` で除外したテーブルへ置いてください。
- **後勝ちモードは失われた更新を検出しません。** すれ違いは後からアップロードした側が黙って勝ち、削除とのすれ違いは行が復活します。競合を検出したいテーブルには rowversion 列を持たせて増分側に乗せてください（[版なしテーブルと後勝ちモード](#版なしテーブルと後勝ちモードsyncmodelastwritewins)）。
- **無制限バイナリ列は指定しない限り運ばれません**（`SyncOptions.IncludeUnboundedBinary`）。運ぶ場合の代償は「変更行 1 件・列 1 本につき 1 往復」です。[無制限バイナリ列](#無制限バイナリ列)を参照してください。
- **EF Core 版 Repository とは併用できません。** 同期支援はマルチターゲット前提で、その組合せが元から排他だからです。
- **HTTP 経路には `--generate-remote-services` が要ります。** 無くても直結ソースは生成されエンジンは動きますが、サーバーへ届くためのクライアントもエンドポイントもありません。
- **ローカル側に版ガードはありません**（[マルチターゲットでの rowversion 列](#マルチターゲットでの-rowversion-列)）。ローカルの書き手どうしは依然として上書きし合い、エンジンが検出する競合はサーバー側の版についての話です。
- 対応するランタイムパッケージは `QuickER.Runtime.Sync` です。

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
// 認可の要否は既定値を持たない必須引数 RemoteAccess で明示する（詳細は「注意」節）。
// 500 応答はサーバー側の詳細を既定で隠す。IsDevelopment() を渡せば開発時だけ実メッセージ・本番は汎用文言になる
app.MapGeneratedRemoteEndpoints(
    RemoteAccess.RequireAuthorization,
    exposeErrorDetails: app.Environment.IsDevelopment()
);
app.Run();

// ---- クライアントアプリ（DI 登録 1 行で直結⇔リモートを切り替え）----
// 直結:    services.AddGeneratedSqliteRepositories(connectionString);
// リモート: services.AddGeneratedHttpRemoteRepositories("https://server:5001/quicker");
// アプリ本体はどちらでも IOrderRemoteRepository を注入して使う（コード変更なし）
```

押さえておくポイント:

- **直列化**はエンティティの JSON 往復（`ToJson` / `Clone`）と同じ意味論（VO は内包値・RowState 込み・親参照ナビは循環しない）で、クライアント・サーバーが共有の `RemoteJson.Options` を使います
- **liveness エンドポイントがあります**。`MapGeneratedRemoteEndpoints` は `GET {prefix}/health` も同時にマップし、サーバーが待ち受け始めた時点で本文なしの 200 を返します。DB には意図的に触らないので、「プロセスが上がっていてエンドポイントがマップされている」ことだけを表します。クライアント側は `Http{Entity}RemoteRepository.PingAsync` がこれを呼び、到達できない事象（接続拒否・DNS/TLS 失敗・HttpClient 自身のタイムアウト・成功以外のステータス）はすべて例外でなく `false` として返すため、起動待ちループの条件にそのまま使えます（渡したトークンのキャンセルは従来どおり例外になるので、呼び出し側のタイムアウトとサーバー停止は区別できます）。エンドポイントはグループの一員なので、グループに付けた認可はここにも効きます。プレフィックスと health ルートは公開定数 `RemotePaths.DefaultPrefix`（`"/quicker"`）／`RemotePaths.HealthRoute` として両側が参照し、値の正本を 1 箇所に保ちます
- **名前付きクエリは実装方式（簡易 DSL／生 SQL／手動実装）に依らず全部**リモート面経由で呼び出せます（実装の実体はサーバー側のリポジトリ）
- **例外は型が復元されます**: サーバーの `SaveConflictException` は HTTP 409 を介してクライアントでも `SaveConflictException` として送出され（直結時と同じ catch が機能）、その他のサーバー例外は `RemoteRepositoryException`（ステータスコードは保持。メッセージの扱いは後述）になります。**成功ステータスなのに本文が期待した JSON でない応答**も `RemoteRepositoryException` になります（応答しているのは生成エンドポイントではない別物＝プロキシやポータルの 200 ページであることがほとんどで、実体は転送の失敗だからです。素の `JsonException` が出ると他のリモート失敗と同じ catch で拾えません）。**本文が JSON リテラル `null` の成功応答**も、結果が null になり得ない操作（`GetAll`・保存系・一覧/件数クエリ）では同じ分類になります（`null` は正当な JSON なので、検査しないと素通りして呼び出しから離れた場所の不明瞭な `NullReferenceException` になります）。null が正当な結果である操作（`GetById`・単一戻り形・null 許容スカラーのクエリ）の 200＋`null` は従来どおり「該当行なし」です
- **リクエストを解釈できない場合は 500 ではなく 400 になります**。リクエスト自体の読み取り中に失敗するもの（不正な JSON・空ボディ・JSON でない Content-Type・型不一致・値オブジェクトの検証違反・必須フィールドの欠落〔`Insert` / `Update` / `Save` / `SaveMany` への `{}`、参照型キーを省いた `GetById` / `Delete`〕・未定義の `ConcurrencyMode` 値、バイナリエンドポイントの `?id=` 欠落・復元不能）はクライアントが送った内容の問題なので、HTTP 400＋`RemoteError`（`Type` は `"BadRequest"`）を返します（クライアントは `StatusCode` が 400 の `RemoteRepositoryException` を送出）。400 のメッセージにはクライアント自身のペイロードに関する情報しか載らず、サーバー側のログ出力も `OnServerError` フックも実行されません（どちらも 500 専用）。サーバー基盤が拒否したリクエスト（`BadHttpRequestException`。例: リクエストボディのサイズ上限超過）は、その例外が持つステータスコード（413 など）をそのまま返します
- **グラフ保存（Save）成功後はローカルの RowState も確定**します（直結時と同じ挙動）
- **楽観排他も転送されます**。`ConcurrencyMode` 引数は Update / Save のリクエストに含まれ、Insert / Update / Save の応答は保存で採番された版を「エンティティ型名＋主キー」の対応表として運び、クライアントが手元のグラフへ書き戻します。これによりリモートでも直結と同じ版を保持でき、再取得なしで同じエンティティを続けて保存できます
- **500 応答はサーバー側の詳細を既定で公開しません**。本文には固定文言（`An unexpected error occurred on the server.`）と `CorrelationId` が載り、クライアントでは `RemoteRepositoryException.CorrelationId` として取り出せます。スタックトレースを含む例外全体は従来どおり常にサーバー側へ記録され（`ILoggerFactory` 経由・カテゴリ `QuickER.RemoteServer`。ロギング未構成のホストでは何もしません）、**そのログ行にも同じ相関 ID が載る**ので、利用者から報告された ID を突き合わせれば、内部メッセージ（テーブル名・列名・接続文字列・ファイルパス）を信頼境界の外へ出さないまま完全な記録に辿り着けます。従来どおりメッセージを透過させたい場合は `MapGeneratedRemoteEndpoints(exposeErrorDetails: true)` を渡してください（このとき `CorrelationId` は null＝ボディは以前のバージョンと同一です）。定型は `exposeErrorDetails: app.Environment.IsDevelopment()` で、これを生成時オプションでなく実行時引数にしているのは、同じ生成物のまま開発と本番を使い分けられるようにするためです。スイッチが変えるのは**クライアントから見える 500 の内容だけ**で、サーバー側のログ出力と `OnServerError` フックはどちらのモードでも例外そのものを受け取ります。また 400（クライアント自身のペイロードについての説明）と 409 の競合内訳（`Reason` / `EntityType` / `Key`＝再取得リトライを組むための材料）は自前の文言なのでスイッチの影響を受けず、常に従来どおり返ります。バイナリ転送エンドポイントの 500 も同じスイッチに従います
- **認証・TLS はスコープ外です。認可を要求するかどうかは、既定値を持たない必須引数 `RemoteAccess` として呼び出し側が明示します。** ワイヤ形式は主キーを含む全列を受け付け、呼び出し側が名指しできる任意の行を読み書き削除できるため、この面を認可で守るかどうかを、誰も書いていない既定値が（開放側にも安全側にも）黙って決めることはしません。`RemoteAccess.RequireAuthorization` は ASP.NET Core の既定認可ポリシーをグループ全体（health 含む）へ適用します——生成コードにできるのは認可の**要求**までで**用意**はできないため、ホスト側で認証・認可を構成していなければ全リクエストが失敗します。`RemoteAccess.AllowAnonymous` は「このマップ自体は何も要求しない」という宣言で、メタデータを一切付けません（`[AllowAnonymous]` を付けてホストの FallbackPolicy＝全エンドポイント既定認証必須の網から生成面だけを抜くことはしない）。ローカル開発や、認可を別レイヤで掛ける構成で使ってください。未定義値はマップ時に `ArgumentOutOfRangeException` で fail-fast します。クライアント側は `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` で認証ハンドラ付きの HttpClient を構成してください。**認可がエンドポイントを選ばずグループ全体へ掛かるのは意図的です。** `Save` は `Delete` と同じ強さを持つからです＝`RowState.Removed` を含むグラフを送れば該当行は削除されるため、`Delete` だけを守って `Save` を開けておく方針は何も守っていません。追加のポリシーは戻り値の `RouteGroupBuilder` へ重ねられます
- **どちらの登録オーバーロードにも keyed 版があり、複数のバックエンドを同時に抱えられます**。`AddGeneratedHttpRemoteRepositories(serviceKey, baseAddress)` と `AddGeneratedHttpRemoteRepositories(serviceKey, httpClientFactory)` は `I{Entity}RemoteRepository` をサービスキー付きで登録し、方言別拡張が元から持つ keyed 版（`AddGeneratedSqliteRepositories(serviceKey, connectionString)`）と対になります。ハイブリッド構成はこの形で組みます＝サーバーを HTTP で 1 つのキーへ、ローカル DB をもう 1 つのキーへ登録し、利用側が欲しい方を名指しで受け取ります。

  ```csharp
  services.AddGeneratedHttpRemoteRepositories("server", "https://server:5001/quicker");
  services.AddGeneratedSqliteRepositories("local", localConnectionString);

  // コンストラクタ引数:
  //   [FromKeyedServices("server")] IOrderRemoteRepository remote
  //   [FromKeyedServices("local")]  IOrderRepository       local
  ```

  ベースアドレス版が作る共有 HttpClient も同じキーで登録されるため、非 keyed 登録とも別キーとも衝突せず、所有者が DI コンテナである点も非 keyed 版と同じです。keyed 登録と非 keyed 登録は別の名簿で（keyed 登録は `GetRequiredKeyedService` にしか応えません）、同じキーへ 2 回登録すると後の登録が有効になります
- **ファクトリ版が返す HttpClient の所有権は呼び出し側にあります**。`AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` はリポジトリ解決のたび（スコープ×エンティティ数だけ）ファクトリを呼び出し、返された HttpClient は生成コードも DI コンテナも破棄しません。共有インスタンスか `IHttpClientFactory` 管理のインスタンスを返してください（毎回 new するとソケットが枯渇します）。ベースアドレス版は共有インスタンスを 1 つだけ作り、それを DI コンテナが所有します（`ServiceProvider` の破棄と同時に HttpClient も破棄されるため、破棄済み provider から取得したリポジトリを使うと `ObjectDisposedException` になります）
- **ベースアドレス版が作る HttpClient は、タイムアウトを持たず 5 分でコネクションを作り直します**。`PooledConnectionLifetime`（`SocketsHttpHandler`）を設定しているのは、長命のシングルトンが最初に解決したアドレスを固定し続けず DNS 変更に追従するためです。`Timeout` は `Timeout.InfiniteTimeSpan` にしています＝`HttpClient.Timeout` は本文を含むリクエスト全体に掛かるため、既定の 100 秒では大きな blob 転送が途中で切られてしまうからです。したがって**個々の呼び出しの制限時間は、もともと渡している `CancellationToken` で与えてください**（それがタイムアウトです）。クライアント全体に有限の期限を持たせたい場合はファクトリ版を使い、自分で構成した HttpClient を渡してください（ファクトリ版の HttpClient は利用者の所有物で、生成コードは設定に手を入れません）
- **0.x の間はワイヤ形式の互換を約束しないため、クライアントとサーバーは同時に再生成し、同時に配置してください**。サーバーだけを先に更新しても不一致はバージョンエラーとして報告されません＝新しいエンドポイントが受け付けなくなったリクエストは、ただの転送失敗（404 や 400）として返ります。バイナリエンドポイントが自分の出す 404 へマーカーを載せている（後述）のも、クライアントが 404 を一律「データなし」と読まないためです
- **更新系の操作（`Insert` / `Update` / `Save` / `SaveMany` / `Delete`）に HTTP レベルの自動リトライ（Polly 等）を掛けないでください**。冪等キーを持たないため、実際には成功したのに応答が返る途中で失われたリクエストを再送すると二重に適用されます（挿入の重複、あるいは版が二度進んで次回保存が偽の競合になる、など）。リトライしてよいのは読み取り専用の操作（`GetById` / `GetAll` / 名前付きクエリ）と health エンドポイントなので、ポリシーはクライアント全体でなくそれらへ限定してください
- サーバーファイルは ASP.NET Core の FrameworkReference（`Microsoft.AspNetCore.App`）が必要です（SDK が `Microsoft.NET.Sdk.Web` のプロジェクトなら追加設定不要）。その固定エンジンは共有コードのため、`--use-runtime-packages` では `QuickER.Runtime.AspNetCore` が提供し、参照するのはサーバーファイルを載せるプロジェクトだけです（[ランタイムパッケージ参照モード](#ランタイムパッケージ参照モード--use-runtime-packages)を参照）
- **生成サーバークラスは拡張できます。** `GeneratedRemoteEndpoints` は `partial` クラスなので、生成物と並べて独自のエンドポイントヘルパを同じクラスへ置けます。また `static partial void OnServerError(HttpContext, Exception)` フックを別パートで実装すると、エンドポイントが HTTP 500 を返すたびに独自処理（通知・メトリクス・追加ログ）を差し込めます（組み込みログの後に実行され、実装しなければコンパイル時に呼び出しが消えます。フック内で例外が起きても隔離され、元のエラー応答を妨げません＝フックの例外はサーバーログへ記録して握り潰します）。同じプレフィックス配下への追加エンドポイントは、`MapGeneratedRemoteEndpoints()` が返す `RouteGroupBuilder` へ直接 Map しても構いません

### バイナリ転送エンドポイント（無制限バイナリ列の Stream アクセサ）

無制限バイナリ除外（`--exclude-unbounded-binary-columns`）と併用すると、除外列の Stream アクセサ（`Read/Write{Column}Async`）が **HTTP でストリーミング転送**されます。JSON エンベロープ（`POST` + Base64）では巨大 blob のメモリ膨張を避けられないため、これらは意図的に **REST 風の第 2 形式**（動詞分離・生ボディ・`application/octet-stream`）を使います。除外列ごとに次の 3 エンドポイントが生成されます（`{列名}` は C# プロパティ名）:

| 動詞・URL | 意味 | 応答 |
|---|---|---|
| `GET {prefix}/{エンティティ}/{列名}?id=` | ダウンロード（本文を宛先へストリーム） | 200＋`application/octet-stream`（空 blob も 200）／行なし・NULL は **404**（クライアントで `false`）／`id` 欠落・不正は **400** |
| `PUT {prefix}/{エンティティ}/{列名}?id=` | アップロード（生ボディ・`Content-Length` 必須） | 成功 **204**／行なし **404**（`false`）／`Content-Length` 欠落（chunked）は **411**／`id` 欠落・不正は **400** |
| `DELETE {prefix}/{エンティティ}/{列名}?id=` | 列を `NULL` へ（`Write(id, null)` 相当） | 成功 204／行なし 404／`id` 欠落・不正は **400** |

- **これらのエンドポイント自身が返す 404 には、`Type` が `"NotFound"` の `RemoteError` 本文が載ります**。クライアントで `false` になるのはこの marker 付きの 404 だけです。本文なしの素の 404（ベースアドレスやプレフィックスの誤り、消えたルート、プロキシ自身の応答）は「データなし」と区別できないため、クライアントは `RemoteRepositoryException` として送出します＝設定ミスが空の結果に化けません。411 にも同様に `RemoteError` 本文（`Type` は他の分類済み拒否と同じ `"BadRequest"`）が載ります。
- **キーは URL クエリ `?id=`** で運びます（本文は blob 本体に使うため）。VO キーは JSON エンベロープと同一規則（内包値）で直列化されます。
- **0 バイトの PUT（空ボディ）と `NULL` 化（DELETE）は構造的に区別**されます（前者は `Read` が `true`＋空・後者は `false`）。
- **バイナリ PUT のリクエストサイズ制限解除はオプトイン**です（`MapGeneratedRemoteEndpoints(access, prefix, exposeErrorDetails, allowUnboundedUploads)`）。既定は `false`＝ホスト側の上限（Kestrel 既定で 30MB）がこのエンドポイントにも効き、超過は **413** で拒否されます。GB 級を扱うときだけ `allowUnboundedUploads: true` を渡してください。**任意サイズの本文を受けるエンドポイントは DoS 面になるため、`RemoteAccess.RequireAuthorization` との併用を強く推奨**します。影響を受けるのはバイナリ PUT のみ（JSON エンドポイントは常にホストの上限のまま）で、別の値にする場合は戻り値の `RouteGroupBuilder` でグループ全体を上書きしてください。
- クライアント（`Http{Entity}RemoteRepository`）は `GET` を `ResponseHeadersRead` で受けて宛先へ O(チャンク) でコピーし、`PUT` は `StreamContent`（`Content-Length` 付き）で送ります。非シーク Stream で `length` を渡さない場合は**送信前**に `ArgumentException` になります（既存の長さ契約と同一）。**渡した Stream の所有権は呼び出し側のまま**で、クライアントは閉じも破棄もしません（直結実装と同じ＝実装を差し替えても Stream の扱いが変わりません。HTTP レイヤは送信後にリクエストコンテンツを破棄するため、Stream は非クローズのラッパー経由で `StreamContent` へ渡しています）。
- **`WithUnboundedBinary()` / `Query()` / 生 SQL のリモート化はスコープ外**です（従来どおり）。

動く実例はリポジトリの [samples/ec-order-remote](../samples/ec-order-remote/README.ja.md) にあります（この推奨構成そのままの 3 プロジェクト＋実 2 プロセスで動かすサンプル。名前付きクエリのリモート転送・`SaveConflictException` の型復元も実演）。

## テスト用インメモリ Repository（GenerateInMemoryRepositories）

DB なしでユニットテストするためのインメモリ実装を追加生成できます。同一契約を実装し、サポート外の操作は実 DB の Repository へ切り替える案内付きの `NotSupportedException` を送出します。

### 実 DB との既知の乖離

インメモリストアはクエリを SQL でなく LINQ-to-Objects で評価するため、いくつかの意味論は DB のものではなくインメモリ固有です。「インメモリでは通るのに実 DB では落ちる」を避けるために把握しておいてください。

- **文字列の比較と並び順は序数（Ordinal）です。** 絞り込み（`Where`）も `OrderBy` も序数比較なので、`"B"` は `"a"` より前に並びます。SQL Server の既定照合は大文字小文字を区別せず、並び順も照合順序に従うため、大文字小文字やアクセントの扱いに依存するテストは実 DB の裏付けにはなりません。
- **UNIQUE 制約は書き込み時に強制されません。** 強制されるのは主キーだけです（重複主キーは実 DB の INSERT と同じく拒否されます）。UNIQUE 制約の重複値は黙って格納されるため、チェックが要るテストでは `CheckUniquenessAsync` を使ってください。
- **Before フックで RowState を書き換えると、インメモリでだけ操作が変わります。** このバックエンドは操作を実行する時点で RowState を読み直すため、`BeforeSaveAsync` が `Modified` を `Added` へ変えると実際の操作も変わります。QuickER 版 Repository と EF Core はその時点で発行する文を決め終えているため、書き換えは無視されます。フック契約はどちらの挙動も保証していないので、**フックから RowState を書き換えないでください**（その行だけ飛ばしたい場合は `false` を返します）。
- **After フックの実行後に `SaveConflictException` が出ることがあります。** 書き込みはステージングされて一括公開され、公開時に保存の起点となった行を再検証します。フックはストアのロック外で走るため、この再検証は `AfterSaveAsync` より後です。実 DB はその時点よりずっと前にロックを取っているため、「After フックが走った＝保存は確定」と仮定するテストは実 DB では成立してもここでは成立しません。
- **rowversion 列を持たない型の並行保存は後勝ち（last-write-wins）です。** 公開時の再検証は並行性トークンを持つ型だけが対象のため、版のない同一行を 2 つの保存が奪い合うと後から公開した側の値が残ります。ただし後勝ちは「行の復活」までは含みません。他者が先にその行を削除していた場合、更新しようとしていた保存は古いスナップショットを書き戻さず `SaveConflictException`（`SaveConflictReason.NotFound`）で失敗します（実 DB の UPDATE は対象行が無ければ 0 行更新であり、黙って捨てると「保存できた」と報告しながら行が無い状態になるためです）。staged 側が削除の場合は競合になりません（既に無い行の削除は実 DB でも no-op のためです）。この 2 つの規則は版を持つ型にも同じように適用されます（存否は版の比較より先に決まります）。
- **`insertWhenUpdateMissing` は公開時の窓までは面倒を見ません。** UPDATE と INSERT へのフォールバックの選択は、ストアのロックを保持している保存フェーズで決まります。その後に他者がその行を削除すると、保存は staged な更新を抱えたままになり、公開時の検証は INSERT へ切り替えるのではなく `SaveConflictException`（`SaveConflictReason.NotFound`）を報告します。実 DB には対応する窓が存在しない（文が書き込むその瞬間に行の不在を見る）ため、「並行削除があっても `insertWhenUpdateMissing` で通る」ことを前提としたテストはこのバックエンド固有の挙動を見ていることになります。

## ランタイムパッケージ参照モード（--use-runtime-packages）

既定では、生成コードはランタイム（スキーマ非依存の固定コード）込みのインライン出力で自己完結します。`--use-runtime-packages` を指定すると固定コードを出力せず、次の NuGet パッケージへの参照で賄います（生成ヘッダと CLI 出力に必要な PackageReference が案内されます。csproj には手動で追加してください）:

| パッケージ | 内容 | サードパーティ依存 |
|---|---|---|
| `QuickER.Runtime` | 共通基盤・方言中立の契約 | なし |
| `QuickER.Runtime.SqlServer` | QuickER の SQL Server 方言エンジン | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | QuickER の SQLite 方言エンジン | Microsoft.Data.Sqlite・SQLitePCLRaw.bundle_e_sqlite3 |
| `QuickER.Runtime.EntityFrameworkCore` | EF Core 共通部品 | Microsoft.EntityFrameworkCore.Relational |
| `QuickER.Runtime.InMemory` | インメモリエンジン（テスト用） | なし |
| `QuickER.Runtime.AspNetCore` | 生成されるリモートエンドポイントのサーバー側固定エンジン | ASP.NET Core（NuGet 依存ではなく `FrameworkReference`） |
| `QuickER.Runtime.Sync` | 双方向同期エンジン（ジャーナル・テーブル記述子・競合の型） | なし |

`QuickER.Runtime` 以外の 6 本は、上表に加えて `QuickER.Runtime` への依存を宣言します（nuget.org の Dependencies 欄にはそれも並びます）。

パッケージ版とツール版はロックステップ（同一バージョン）で公開されるため、両者には同じバージョンを使ってください。0.x の間は minor 間の互換性を約束していません（[CONTRIBUTING](../CONTRIBUTING.ja.md) のバージョニング方針を参照）。DI 登録拡張・`QuickErDbContext`・エンティティ別実装などのスキーマ依存物は、本モードでも常に生成側に出力されます。

### 生成ファイルとパッケージの対応（分割生成時）

ファイル分割生成では、固定ランタイムとスキーマ依存コードが別ファイルに分かれ、**固定ランタイム側のファイルはパッケージと 1:1 対応**します。命名は「ファイル名・名前空間サフィックス＝パッケージ名のサフィックス」という単一規則です（`Runtime.SqlServer.g.cs` → 名前空間 `{Runtime}.SqlServer` → パッケージ `QuickER.Runtime.SqlServer`）。以下の `{Runtime}` はランタイム名前空間（既定 `{RootNamespace}.Runtime`）を指します。

| 生成ファイル（名前空間） | 対応パッケージ | 内容 |
|---|---|---|
| `Runtime.g.cs`（`{Runtime}`） | `QuickER.Runtime` | 共有基盤（基底クラス・属性・VO 基底・JSON コンバータ）＋方言中立の契約（`IRepository`・クエリ基盤・リモートクライアント固定部） |
| `Runtime.SqlServer.g.cs` / `Runtime.Sqlite.g.cs`（`{Runtime}.{方言}`） | `QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite` | 方言エンジン（方言 Repository 基底・式木翻訳・実行器・接続ファクトリ） |
| `Runtime.EntityFrameworkCore.g.cs`（`{Runtime}.EntityFrameworkCore`） | `QuickER.Runtime.EntityFrameworkCore` | EF Core 共通部品（`TContext : DbContext` ジェネリックの Repository 基底・VO 翻訳プラグイン） |
| `Runtime.InMemory.g.cs`（`{Runtime}.InMemory`） | `QuickER.Runtime.InMemory` | インメモリ基盤（ストア・Repository 基底・保存ステージング） |
| `Runtime.AspNetCore.g.cs`（`{Runtime}.AspNetCore`） | `QuickER.Runtime.AspNetCore` | サーバー側固定エンジン（`RemoteServerEngine`＝リクエスト読み取り・エラー分類・詳細公開ポリシー・バイナリ転送の補助） |
| `Runtime.Sync.g.cs`（`{Runtime}.Sync`） | `QuickER.Runtime.Sync` | 同期エンジン（`SyncEngine`・`SyncJournal`・`SyncTable<,>`・オプション／結果／競合の型。リモートサービス併用時は同期エンベロープと HTTP ソース基底） |
| `Repositories.g.cs`・`Repositories.SqlServer.g.cs` / `Repositories.Sqlite.g.cs` / `Repositories.EntityFrameworkCore.g.cs` / `Repositories.InMemory.g.cs` / `Repositories.Sync.g.cs` / `Repositories.Http.g.cs`・`RemoteServer.g.cs` | —（対応パッケージなし＝常に生成） | スキーマ依存物のみ（per-entity の契約と実装・DI 登録・`QuickErDbContext` と Fluent 構成・射影 DTO・per-entity のエンドポイント（`GeneratedRemoteEndpoints`）・per-table の同期記述子とジャーナル記録デコレータ）。リモートサービス併用時、HTTP クライアント（`Http{Entity}RemoteRepository` と DI 登録）は専用の `Repositories.Http.g.cs` へ分かれ、契約ファイルはインターフェイスだけに保たれます（名前空間は契約と同一のため型名は変わりません） |

`Runtime.g.cs` は常に出力され、それ以降のファイルは有効にした機能の分だけ出力されます（方言ファイルは QuickER 版 Repository を生成するとき・EF Core ファイルは `GenerateEfCore`・インメモリファイルは `GenerateInMemoryRepositories`・ASP.NET Core ファイルは `GenerateRemoteServices`・同期ファイルは `GenerateSyncSupport` のときだけ）＝参照すべきパッケージの集合とそのまま一致します。

この構成のため、`--use-runtime-packages` の意味は 1 つに収まります。**`Runtime*.g.cs` を 1 本も出力せず、生成コードの `using` が `{Runtime}…` ではなく固定のパッケージ名前空間（`QuickER.Runtime`・`QuickER.Runtime.SqlServer` …）を指すようになる**、それだけです。`Repositories*` 側はモードの ON / OFF で内容が変わりません。

なお、ファイル名・名前空間の `EntityFrameworkCore` はパッケージ名へ揃えるためのもので、**C# の型名は従来どおり**です（`EfCore{Entity}Repository`・`QuickErDbContext`・`AddGeneratedEfCoreRepositories`）。

`Entities.g.cs`・`ValueObjects.g.cs`・`EditModels.g.cs`・`Mappers.g.cs` は全体がスキーマ依存で、本モードでも内容は変わりません。`RemoteServer.g.cs` もスキーマ依存ですが、分割時はその裏側の固定エンジンが `Runtime.AspNetCore.g.cs`（パッケージ参照モードでは `QuickER.Runtime.AspNetCore`）へ分かれ、ファイル本体には per-entity のエンドポイントと `OnServerError` フックだけが残ります。非分割（インライン）生成ではエンジンが `RemoteServer.g.cs` 自身に同居します。いずれの場合も ASP.NET Core の `FrameworkReference` を要するため別ファイルのままで、上記のそれ以外は非分割時に 1 ファイルへ連結されます。

## 層別フォルダ出力（--layered-output）

`--layered-output`（設定キー `LayeredOutput`・**既定 OFF**）は、分割生成された各ファイルを出力ディレクトリ配下の層別サブフォルダへ振り分け、各層を独立プロジェクトにできるようにします——DDD 風のドメイン／プレゼンテーション／インフラストラクチャ分割＋リモートサービス生成時のサーバープロジェクトです。`SplitFilesByCategory` を自動的に含意します（単一ファイルはフォルダへ割れないため）。

バケット→層の対応は固定です：

| 層（既定フォルダ） | ファイル |
|---|---|
| ドメイン（`Domain/`） | `Entities.g.cs`・`ValueObjects.g.cs`・`Repositories.g.cs`（契約）・`Runtime.g.cs`（インラインランタイム） |
| プレゼンテーション（`Presentation/`） | `EditModels.g.cs`・`Mappers.g.cs` |
| インフラストラクチャ（`Infrastructure/`） | `Repositories.SqlServer.g.cs` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.Sync` / `.Http` と対応する固定 infra の `Runtime.{...}.g.cs` |
| サーバー（`Server/`） | `RemoteServer.g.cs`・`Runtime.AspNetCore.g.cs`（ASP.NET Core の `FrameworkReference` を要するため通常のクラスライブラリには置けません） |
| 出力ディレクトリ直下 | API リファレンス（`*.g.md`）＝どの csproj にも属さないため。`--api-docs-subdir` で `docs` などのサブフォルダへ移せます（層とは独立） |

各層のフォルダは `--domain-layer-dir` / `--presentation-layer-dir` / `--infrastructure-layer-dir` / `--server-layer-dir`（設定キー `DomainLayerDirectory`・`PresentationLayerDirectory`・`InfrastructureLayerDirectory`・`ServerLayerDirectory`）で上書きできます。値は出力ディレクトリからの相対パスで、複数階層（`MyApp.Domain/Generated`）も指定できるため、出力ディレクトリをソリューションのソースフォルダへ向ければ層プロジェクトの中へ直接生成できます。絶対パス・ドライブ指定・`..` は生成時エラーとして拒否され、空の値は既定フォルダ名へフォールバックします。

**名前空間の既定は層フォルダに追従**し、フォルダと名前空間が揃います。各層の名前空間ルートはフォルダパスの区切りを `.` に変換したもの（フォルダ `MyApp.Domain/Generated` → ルート `MyApp.Domain.Generated`＝「プロジェクトフォルダ名＝RootNamespace」という csproj の慣行と一致）で、各種別がその下へ `{ルート}.{接尾辞}` でぶら下がります：

| 層（フォルダ `MyApp.Domain` 等） | 名前空間 |
|---|---|
| ドメイン | `MyApp.Domain.Entities` / `.ValueObjects` / `.Repositories`（契約） / `.Runtime` |
| プレゼンテーション | `MyApp.Presentation.EditModels` / `.Mappers` |
| インフラストラクチャ | `MyApp.Infrastructure.SqlServer` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.Sync` / `.Http`——各系統の固定 infra ファイル（`Runtime.SqlServer.g.cs` 等）と per-entity ファイル（`Repositories.SqlServer.g.cs` 等）は同一の名前空間を共有します |
| サーバー | `MyApp.Server.RemoteServer` / `.AspNetCore` |

明示の名前空間オプション（`EntityNamespace`・`RepositoryNamespace` 等）は従来どおり導出より優先されます。名前空間として成立しないフォルダ名（ハイフン等）は生成時エラーになります（その層の名前空間をすべて明示指定している場合を除く）。通常分割で方言実装が契約名前空間の下（`{契約}.SqlServer`）にぶら下がっていたねじれ（別プロジェクト在住なのにドメインの名前空間）も、層別出力ではインフラ層ルートの下へ移って解消されます。`RootNamespace` は導出既定には現れなくなります（層フォルダが代わりを務めます）。

### 生成コードのサブフォルダ（--code-subdir）

`--code-subdir`（設定キー `CodeSubdirectory`・**既定は指定なし**）は、生成コード（`.g.cs`）をもう 1 段下のサブフォルダへ出します。層別出力では層フォルダの下、そうでなければ出力ディレクトリの下です。**全出力モード（非分割・分割・層別）で有効**で、生成コードと手書きコードを同じプロジェクトの中で分けるために使います。

**名前空間には一切影響しません。** 層フォルダと違い、この値は名前空間の導出に入りません。だから手書きの partial クラスを「生成物と同じ名前空間・親フォルダ」に置けます：

```
MyApp.Domain/                        ← --domain-layer-dir
  Generated/                         ← --code-subdir
    Entities.g.cs                    namespace MyApp.Domain.Entities
    Repositories.g.cs                namespace MyApp.Domain.Repositories
  OrderEntity.Rules.cs               namespace MyApp.Domain.Entities（手書きの partial）
  Services/                          ← 手書き
MyApp.Infrastructure/
  Generated/
    Repositories.SqlServer.g.cs      namespace MyApp.Infrastructure.SqlServer
    Runtime.SqlServer.g.cs
EcOrder.g.md                         ← サブフォルダに追随しない（下記）
```

SDK 形式のプロジェクトは `**/*.cs` を暗黙に取り込むため、サブフォルダを掘っても csproj に手を入れる必要はありません。フォルダ単位で「まとめて消して再生成する」「アナライザの対象から外す」といった扱いができるようになります。

値は複数階層（`Generated/QuickER`）も指定できます。絶対パス・ドライブ指定・`..` は生成時エラーですが、**名前空間に現れないため C# 識別子である必要はありません**（`generated-code` のような名前も使えます）。API リファレンス（`.g.md`）はこのサブフォルダに追随しません——ドキュメントの置き場を決めるのは `--api-docs-subdir` だけです。

GUI では生成ダイアログの「出力先」欄で、出力先パスのすぐ下に「サブフォルダ」として並びます（出力モード・層別出力のチェックとは独立に、常に指定できます）。

押さえておくべき点：

- **変わるのは名前空間・ファイル配置・固定ランタイムの可視性だけです。** `namespace` 宣言と `using` 行を除けば、スキーマ依存の生成コードは通常の分割出力と一致し、API リファレンス（`.g.md`）には実際の（導出後の）名前空間が載ります。固定ランタイム（`Runtime*.g.cs`）は **public** で出力されます：各層は別アセンブリであり、同じ理由で同じ型を public として配布している NuGet パッケージと同一の規則です。そのため生成プロジェクトは素のプロジェクト参照だけでビルドでき、**`InternalsVisibleTo` の手書きは不要**です。
- リポジトリ契約は DDD のポートとしてドメイン層に置かれます：プレゼンテーションプロジェクト（EditModel の DB 照合は `I{Entity}Repository` 経由）はドメインプロジェクトへの参照だけで成立し、インフラストラクチャは「ドメインの契約を実装する側」になります。プロジェクト参照は `プレゼンテーション → ドメイン ← インフラストラクチャ ← サーバー` です（サーバープロジェクトは DI 組み立てのためインフラストラクチャも参照します）。
- インラインランタイム（`Runtime.g.cs`）はドメイン層に入ります。これはパッケージ参照モードと対称です：`--use-runtime-packages` ならドメインプロジェクトが代わりに `QuickER.Runtime` を参照し、いずれの場合も他の層にはドメイン参照経由で推移的に届きます。
- モードの切替（や層フォルダ名・サブフォルダ名の変更）をしても、以前の場所に書かれたファイルは削除されません——手動で削除してください。

## API リファレンス（.g.md）

生成コードと同名ベースの API リファレンス Markdown を追加出力できます。GUI の生成ダイアログの「API リファレンス (.g.md) を出力する」チェック、または CLI の `--generate-api-docs` フラグで有効化します（**既定 OFF**）。DB アクセスの選択（なし / QuickER 版 Repository / EF Core 版 Repository）とは独立して、常に選択できます。

有効化すると、`.g.cs` と同じベース名の `.g.md` が 1 つ出力されます（例: `EcOrder.g.cs` → `EcOrder.g.md`）。カテゴリ別分割モードでは `Entities.g.cs` 等の固定名と同じ流儀の固定名 `ApiDocs.g.md`（日本語版は `ApiDocs.ja.g.md`）になります。内容は次のとおりです。

- エンティティ一覧と、各エンティティのプロパティ表（DB 型トークン込み。`string(50)` / `decimal(10,2)` など）
- Repository 契約（`IRepository<TEntity, TKey>` と各エンティティのインターフェイス）— Repository 契約を生成する構成でのみ含まれます
- DI 登録・CRUD・クエリの使い方例 — 同じく Repository 契約を生成する構成でのみ含まれます（DB アクセス「なし」ではこれらの節は省略されます）
- 生成ファイル構成表

**英語が正本です。** 日本語版も併産したい場合は、GUI の下位チェック「日本語版を出力する」、または CLI の `--api-docs-ja` フラグ（設定キー `IncludeJapaneseApiDocs`）を有効化します（**既定 OFF**・`--generate-api-docs` が前提）。有効化すると、英語正本の `.g.md` に加えて `.ja.g.md` が併産されます（例: `EcOrder.g.cs` → `EcOrder.ja.g.md`）。

Markdown は既定で出力ディレクトリ直下に出ます。`--api-docs-subdir`（設定キー `ApiDocsSubdirectory`）で出力ディレクトリからの相対パスのサブフォルダへ移せます（例: `docs`・複数階層可・絶対パスと `..` は拒否）。全出力モードで有効で、層別出力ではドキュメントを層プロジェクトの外へ寄せる用途に使えます。

ファイル名は `--api-docs-file`（設定キー `ApiDocsFileName`）で変えられます（例: `--api-docs-file Api.md` → `Api.g.md`／日本語版は `Api.ja.g.md`）。拡張子は `.g.md` へ正規化されるため、`Api` / `Api.md` / `Api.g.md` のどれを渡しても結果は同じです（生成物の上書きは `.g.md` / `.g.cs` だけに限っているため、拡張子は指定に委ねません）。指定は出力モードに依らず優先され、未指定なら従来どおりの導出名（非分割＝出力ファイル名のベース名／分割＝`ApiDocs.g.md`）になります。指定できるのはファイル名だけで、パス区切りを含む指定は生成時エラーです（置き場を決めるのは `--api-docs-subdir` の役割）。GUI では「出力先サブフォルダ」の下の「出力ファイル名」欄で指定し、**空欄のときは実際に使われる名前がグレーで表示されます**（出力ファイル名・出力モードの変更に追従します）。

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
