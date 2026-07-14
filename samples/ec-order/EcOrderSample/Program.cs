using System.Text;
using EcOrderSample.Generated;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// QuickER が ER 図（EcOrder.json）から生成したコード（Generated/EcOrder.g.cs）を、
// 実際の SQLite ファイル DB に対して動かすサンプル。
// 生成物への参照は NuGet パッケージ（Microsoft.Data.Sqlite など）のみで、QuickER 本体には依存しない。

// 日本語出力の文字化けを避けるため標準出力を UTF-8 にする（リダイレクト時の失敗は無視）
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // 出力がリダイレクトされている場合などは設定できないが、致命的ではないため無視する
}

// DB ファイルは実行ファイルと同じ場所（bin 配下）に置く。作業ディレクトリ（リポジトリ直下等）を汚さないため。
var dbFilePath = Path.Combine(AppContext.BaseDirectory, "ec-order.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbFilePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
}.ConnectionString;

// 冪等に実行できるよう、既存の DB ファイルを削除してから DDL で作り直す
if (File.Exists(dbFilePath))
{
    // 接続プールがファイルを掴んでいると削除できないため、先にプールを解放する
    SqliteConnection.ClearAllPools();
    File.Delete(dbFilePath);
}

await CreateSchemaAsync(connectionString);
Console.WriteLine("[準備] EcOrder.sql の DDL で SQLite ファイル DB（ec-order.db）を作成しました。");
Console.WriteLine();

// 生成された DI 登録拡張で全リポジトリ（QuickER の SQLite 実装）を解決する
using var provider = new ServiceCollection()
    .AddGeneratedRepositories(connectionString)
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRepository>();
var products = provider.GetRequiredService<IProductRepository>();
var orders = provider.GetRequiredService<IOrderRepository>();

// ---- 1. 顧客・商品のマスタ登録（InsertAsync） ----
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 1,
        Name = "山田 太郎",
        Email = "taro@example.com",
    }
);
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 2,
        Name = "鈴木 花子",
        Email = null,
    }
);

await products.InsertAsync(
    new ProductEntity
    {
        ProductId = 100,
        Name = "コーヒー豆 200g",
        UnitPrice = 980m,
    }
);
await products.InsertAsync(
    new ProductEntity
    {
        ProductId = 101,
        Name = "マグカップ",
        UnitPrice = 1500m,
    }
);

Console.WriteLine("[1] 顧客 2 件・商品 2 件を登録しました。");
Check((await customers.GetAllAsync()).Count, 2, "顧客件数");
Check((await products.GetAllAsync()).Count, 2, "商品件数");
Console.WriteLine();

// ---- 2. 注文＋注文明細 2 行のグラフ保存（MarkAdded → SaveAsync） ----
var orderedAt = new DateTime(2026, 7, 7, 13, 47, 9, DateTimeKind.Unspecified);
var order = new OrderEntity
{
    OrderId = 1000,
    CustomerId = 1,
    OrderedAt = orderedAt,
    Memo = "初回注文",
};
order.MarkAdded();

var line1 = new OrderLineEntity
{
    OrderLineId = 5000,
    OrderId = 1000,
    ProductId = 100,
    Quantity = 2,
    UnitPrice = 980m,
};
line1.MarkAdded();

var line2 = new OrderLineEntity
{
    OrderLineId = 5001,
    OrderId = 1000,
    ProductId = 101,
    Quantity = 1,
    UnitPrice = 1500m,
};
line2.MarkAdded();

order.OrderLines.Add(line1);
order.OrderLines.Add(line2);

var savedCount = await orders.SaveAsync(order);
Console.WriteLine(
    $"[2] 注文 1 件＋注文明細 2 行をグラフ保存しました（保存レコード数: {savedCount}）。"
);
Check(savedCount, 3, "グラフ保存レコード数");
Console.WriteLine();

// ---- 3. Where 式木＋Include で注文を取得（親→子コレクション→孫参照） ----
var loadedOrder = await orders
    .Query()
    .Where(o => o.OrderId == 1000)
    .Include(o => o.OrderLines)
        .ThenInclude(l => l.Product)
    .FirstOrDefaultAsync();

if (loadedOrder is null)
{
    throw new InvalidOperationException("注文 1000 を取得できませんでした。");
}

Console.WriteLine("[3] Where 式木＋Include で注文を取得しました:");
Console.WriteLine(
    $"    注文ID={loadedOrder.OrderId} 顧客ID={loadedOrder.CustomerId} 備考={loadedOrder.Memo}"
);

foreach (var line in loadedOrder.OrderLines.OrderBy(l => l.OrderLineId))
{
    var productName = line.Product?.Name ?? "(未ロード)";
    Console.WriteLine(
        $"    明細ID={line.OrderLineId} 商品={productName} 数量={line.Quantity} 単価={line.UnitPrice:0.##}"
    );
}

Check(loadedOrder.OrderLines.Count, 2, "取得した注文明細の行数");
Check(
    loadedOrder.OrderLines.All(l => l.Product is not null),
    true,
    "ThenInclude による商品参照のロード"
);
Console.WriteLine();

// ---- 4. ordered_at（DateTime）の往復値一致 ----
// SQLite は DateTime を ISO8601 TEXT で格納する。挿入した値と読み出した値が一致することを確認する。
Console.WriteLine("[4] ordered_at（DateTime）の往復を確認します:");
Console.WriteLine(
    $"    挿入値={orderedAt:yyyy-MM-dd HH:mm:ss} 読出値={loadedOrder.OrderedAt:yyyy-MM-dd HH:mm:ss}"
);
Check(loadedOrder.OrderedAt, orderedAt, "ordered_at の往復値一致");
Console.WriteLine();

// ---- 5. 生 SQL で注文合計金額を集計（ExecuteScalarSqlAsync<decimal>） ----
var totalAmount = await orders.ExecuteScalarSqlAsync<decimal>(
    "SELECT SUM(\"quantity\" * \"unit_price\") FROM \"order_lines\" WHERE \"order_id\" = @orderId",
    new { orderId = 1000 }
);
Console.WriteLine($"[5] 生 SQL で注文 1000 の合計金額を集計しました: {totalAmount:0.##}");

// 980*2 + 1500*1 = 3460
Check(totalAmount, 3460m, "注文合計金額");
Console.WriteLine();

// ---- 6. 更新（UpdateAsync）と削除カスケード（ExecuteDeleteAsync(cascadeDelete: true)） ----
var toUpdate = await customers.GetByIdAsync(1);
toUpdate!.Name = "山田 太郎（改名）";
var updated = await customers.UpdateAsync(toUpdate);
Check(updated, true, "顧客の更新");

var reloaded = await customers.GetByIdAsync(1);
Console.WriteLine($"[6-a] 顧客を更新しました: {reloaded!.Name}");
Check(reloaded.Name, "山田 太郎（改名）", "更新後の顧客名");

// 顧客 1 を子（注文・注文明細）ごと削除する（FK ON DELETE CASCADE 相当をアプリ側で明示連鎖削除）
var deletedCount = await customers
    .Query()
    .Where(c => c.CustomerId == 1)
    .ExecuteDeleteAsync(cascadeDelete: true);
Console.WriteLine($"[6-b] 顧客 1 を子ごと削除しました（削除レコード数: {deletedCount}）。");

// 顧客 1 ＋ 注文 1 ＋ 注文明細 2 = 4
Check(deletedCount, 4, "削除カスケードのレコード数");

// 残っているのは顧客 2 のみ。注文・注文明細は残っていないことを確認する
Check((await customers.GetAllAsync()).Count, 1, "削除後の顧客件数");
Check((await orders.GetAllAsync()).Count, 0, "削除後の注文件数");
Check(
    await orders.ExecuteScalarSqlAsync<int>("SELECT COUNT(*) FROM \"order_lines\"", null),
    0,
    "削除後の注文明細件数"
);
Console.WriteLine();

Console.WriteLine("すべてのシナリオが成功しました。");
return 0;

// DDL（EcOrder.sql）を読み込み、SQLite ファイル DB へ適用してスキーマを作成する。
static async Task CreateSchemaAsync(string connectionString)
{
    var ddlPath = Path.Combine(AppContext.BaseDirectory, "EcOrder.sql");
    var ddl = await File.ReadAllTextAsync(ddlPath);

    await using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();

    // 生成 DDL の外部キー制約を有効化する（SQLite は既定で FK 制約を無効にしている）
    await using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
    }

    // Microsoft.Data.Sqlite は 1 回の ExecuteNonQuery で複数文（セミコロン区切り）をまとめて実行できる
    await using var command = conn.CreateCommand();
    command.CommandText = ddl;
    await command.ExecuteNonQueryAsync();
}

// 期待値と実測値が一致しなければ例外を投げて終了コードを非 0 にする（CI で検知できるようにする）小さなヘルパー。
static void Check<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException(
            $"検証失敗（{label}）: 期待値={expected} 実測値={actual}"
        );
    }
}
