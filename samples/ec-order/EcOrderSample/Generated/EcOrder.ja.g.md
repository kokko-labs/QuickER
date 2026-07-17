# EcOrderSample.Generated API リファレンス

<!-- QuickER によって自動生成された API リファレンス。手編集しないでください（再生成で上書きされます） -->

この図から生成されたコード（名前空間 `EcOrderSample.Generated`）の API をまとめます。各エンティティのプロパティと、生成される場合はデータアクセス API（Repository 契約）の使い方を掲載します。

## エンティティ一覧

| クラス | テーブル | 説明 |
| --- | --- | --- |
| `CustomerEntity` | `customers` | 顧客。注文の発注元となる購入者マスタ |
| `ProductEntity` | `products` | 商品マスタ。販売対象となる商品の定義 |
| `OrderEntity` | `orders` | 注文ヘッダ。1 顧客の 1 回の注文を表す |
| `OrderLineEntity` | `order_lines` | 注文明細。注文と商品を多対多で結ぶ明細行 |

## 各エンティティ

### CustomerEntity

顧客。注文の発注元となる購入者マスタ

| プロパティ | C# 型 | DB 型 | PK | 必須 | 説明 |
| --- | --- | --- | --- | --- | --- |
| `CustomerId` | `int` | `int32` | ○ | ○ | 顧客ID（主キー。アプリ側で採番） |
| `Name` | `string` | `string(50)` |  | ○ | 顧客名 |
| `Email` | `string?` | `string(100)` |  |  | 連絡先メールアドレス（任意） |

ナビゲーション:

| プロパティ | 種別 | 相手エンティティ |
| --- | --- | --- |
| `Orders` | 子コレクション | `OrderEntity` |

リポジトリ契約: `ICustomerRepository`（主キー型 `int`）が生成されます。

### ProductEntity

商品マスタ。販売対象となる商品の定義

| プロパティ | C# 型 | DB 型 | PK | 必須 | 説明 |
| --- | --- | --- | --- | --- | --- |
| `ProductId` | `int` | `int32` | ○ | ○ | 商品ID（主キー。アプリ側で採番） |
| `Name` | `string` | `string(50)` |  | ○ | 商品名 |
| `UnitPrice` | `decimal` | `decimal(10,2)` |  | ○ | 商品マスタ上の販売単価 |

ナビゲーション:

| プロパティ | 種別 | 相手エンティティ |
| --- | --- | --- |
| `OrderLines` | 子コレクション | `OrderLineEntity` |

リポジトリ契約: `IProductRepository`（主キー型 `int`）が生成されます。

### OrderEntity

注文ヘッダ。1 顧客の 1 回の注文を表す

| プロパティ | C# 型 | DB 型 | PK | 必須 | 説明 |
| --- | --- | --- | --- | --- | --- |
| `OrderId` | `int` | `int32` | ○ | ○ | 注文ID（主キー。アプリ側で採番） |
| `CustomerId` | `int` | `int32` |  | ○ | 発注した顧客ID（customers への外部キー） |
| `OrderedAt` | `DateTime` | `datetime` |  | ○ | 注文日時 |
| `Memo` | `string?` | `string(100)` |  |  | 注文に添える備考（任意） |

ナビゲーション:

| プロパティ | 種別 | 相手エンティティ |
| --- | --- | --- |
| `Customer` | 親参照 | `CustomerEntity` |
| `OrderLines` | 子コレクション | `OrderLineEntity` |

リポジトリ契約: `IOrderRepository`（主キー型 `int`）が生成されます。

### OrderLineEntity

注文明細。注文と商品を多対多で結ぶ明細行

| プロパティ | C# 型 | DB 型 | PK | 必須 | 説明 |
| --- | --- | --- | --- | --- | --- |
| `OrderLineId` | `int` | `int32` | ○ | ○ | 注文明細ID（主キー。アプリ側で採番） |
| `OrderId` | `int` | `int32` |  | ○ | 所属する注文ID（orders への外部キー） |
| `ProductId` | `int` | `int32` |  | ○ | 対象の商品ID（products への外部キー） |
| `Quantity` | `int` | `int32` |  | ○ | 注文数量 |
| `UnitPrice` | `decimal` | `decimal(10,2)` |  | ○ | 注文時単価（商品マスタの改定に影響されないよう注文行に保持） |

ナビゲーション:

| プロパティ | 種別 | 相手エンティティ |
| --- | --- | --- |
| `Order` | 親参照 | `OrderEntity` |
| `Product` | 親参照 | `ProductEntity` |

リポジトリ契約: `IOrderLineRepository`（主キー型 `int`）が生成されます。

## データアクセス API

生成される各リポジトリは `IRepository<TEntity, TKey>` を実装します。主なメソッドは次のとおりです。

| メソッド | 説明 |
| --- | --- |
| `GetByIdAsync(id, ct)` | 主キーで 1 件取得します（該当なしは null）。 |
| `GetAllAsync(ct)` | 全件を取得します。 |
| `InsertAsync(entity, ct)` | 1 件を追加します。 |
| `UpdateAsync(entity, ct)` | 主キー一致の 1 件を更新します（成否を返します）。 |
| `DeleteAsync(id, ct)` | 主キー一致の 1 件を削除します（成否を返します）。 |
| `BulkInsertAsync(entities, ct)` | 複数件をまとめて追加します（追加件数を返します）。 |
| `Query()` | 絞り込み・並べ替え・件数制限などのクエリを組み立てます。 |

`Query()` の対応範囲・詳細は [docs/code-generation.md](https://github.com/kokko-labs/QuickER/blob/main/docs/code-generation.md) を参照してください。

## 使い方

### DI 登録

QuickER 版 Repository（Sqlite）を DI コンテナへ登録します。

```csharp
services.AddGeneratedSqliteRepositories(connectionString);
```

### CRUD とクエリ

`CustomerEntity`（主キー型 `int`）を例に、代表的な操作を示します。

```csharp
// 追加
var entity = new CustomerEntity();
await repository.InsertAsync(entity);

// 主キーで取得
var found = await repository.GetByIdAsync(1);

// 全件取得
var all = await repository.GetAllAsync();

// クエリ（絞り込み・並べ替え）
var results = await repository
    .Query()
    .OrderBy(x => x.CustomerId)
    .ToListAsync();

// 更新・削除
if (found is not null)
{
    await repository.UpdateAsync(found);
    await repository.DeleteAsync(1);
}
```

## 生成ファイル構成

| ファイル | 名前空間 | 内容 |
| --- | --- | --- |
| `EcOrder.g.cs` | `EcOrderSample.Generated` | Entity / EditModel / Mapper / Repository / Runtime |
