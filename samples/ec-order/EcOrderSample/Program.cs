using EcOrderSample.Generated;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// A sample that runs the code QuickER generated from the ER diagram (EcOrder.json) — Generated/EcOrder.g.cs —
// against a real SQLite file DB.
// The generated code references only NuGet packages (Microsoft.Data.Sqlite, etc.) and does not depend on
// the QuickER main projects.

// Place the DB file next to the executable (under bin) so the working directory (e.g. the repository root)
// stays clean.
var dbFilePath = Path.Combine(AppContext.BaseDirectory, "ec-order.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbFilePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
}.ConnectionString;

// Delete any existing DB file and recreate it from the DDL so the sample runs idempotently.
if (File.Exists(dbFilePath))
{
    // The connection pool may hold the file open and block deletion, so release the pools first.
    SqliteConnection.ClearAllPools();
    File.Delete(dbFilePath);
}

await CreateSchemaAsync(connectionString);
Console.WriteLine("[Setup] Created the SQLite file DB (ec-order.db) from the EcOrder.sql DDL.");
Console.WriteLine();

// Resolve all repositories (the QuickER SQLite implementations) via the generated DI registration extension.
using var provider = new ServiceCollection()
    .AddGeneratedSqliteRepositories(connectionString)
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRepository>();
var products = provider.GetRequiredService<IProductRepository>();
var orders = provider.GetRequiredService<IOrderRepository>();

// ---- 1. Register customer and product master data (InsertAsync) ----
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 1,
        Name = "Taro Yamada",
        Email = "taro@example.com",
    }
);
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 2,
        Name = "Hanako Suzuki",
        Email = null,
    }
);

await products.InsertAsync(
    new ProductEntity
    {
        ProductId = 100,
        Name = "Coffee beans 200g",
        UnitPrice = 980m,
    }
);
await products.InsertAsync(
    new ProductEntity
    {
        ProductId = 101,
        Name = "Mug",
        UnitPrice = 1500m,
    }
);

Console.WriteLine("[1] Registered 2 customers and 2 products.");
Check((await customers.GetAllAsync()).Count, 2, "customer count");
Check((await products.GetAllAsync()).Count, 2, "product count");
Console.WriteLine();

// ---- 2. Graph-save an order with 2 order lines (MarkAdded -> SaveAsync) ----
var orderedAt = new DateTime(2026, 7, 7, 13, 47, 9, DateTimeKind.Unspecified);
var order = new OrderEntity
{
    OrderId = 1000,
    CustomerId = 1,
    OrderedAt = orderedAt,
    Memo = "First order",
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
Console.WriteLine($"[2] Graph-saved 1 order + 2 order lines (records saved: {savedCount}).");
Check(savedCount, 3, "graph-saved record count");
Console.WriteLine();

// ---- 3. Fetch the order with a Where expression tree + Include (parent -> child collection -> grandchild reference) ----
var loadedOrder = await orders
    .Query()
    .Where(o => o.OrderId == 1000)
    .Include(o => o.OrderLines)
        .ThenInclude(l => l.Product)
    .FirstOrDefaultAsync();

if (loadedOrder is null)
{
    throw new InvalidOperationException("Could not fetch order 1000.");
}

Console.WriteLine("[3] Fetched the order with a Where expression tree + Include:");
Console.WriteLine(
    $"    OrderId={loadedOrder.OrderId} CustomerId={loadedOrder.CustomerId} Memo={loadedOrder.Memo}"
);

foreach (var line in loadedOrder.OrderLines.OrderBy(l => l.OrderLineId))
{
    var productName = line.Product?.Name ?? "(not loaded)";
    Console.WriteLine(
        $"    LineId={line.OrderLineId} Product={productName} Qty={line.Quantity} UnitPrice={line.UnitPrice:0.##}"
    );
}

Check(loadedOrder.OrderLines.Count, 2, "fetched order line count");
Check(
    loadedOrder.OrderLines.All(l => l.Product is not null),
    true,
    "product references loaded via ThenInclude"
);
Console.WriteLine();

// ---- 4. Round-trip equality of ordered_at (DateTime) ----
// SQLite stores DateTime as ISO8601 TEXT. Verify that the loaded value equals the inserted value.
Console.WriteLine("[4] Verifying the ordered_at (DateTime) round trip:");
Console.WriteLine(
    $"    Inserted={orderedAt:yyyy-MM-dd HH:mm:ss} Loaded={loadedOrder.OrderedAt:yyyy-MM-dd HH:mm:ss}"
);
Check(loadedOrder.OrderedAt, orderedAt, "ordered_at round-trip equality");
Console.WriteLine();

// ---- 5. Aggregate the order total with raw SQL (ExecuteScalarSqlAsync<decimal>) ----
var totalAmount = await orders.ExecuteScalarSqlAsync<decimal>(
    "SELECT SUM(\"quantity\" * \"unit_price\") FROM \"order_lines\" WHERE \"order_id\" = @orderId",
    new { orderId = 1000 }
);
Console.WriteLine(
    $"[5] Aggregated the total amount of order 1000 with raw SQL: {totalAmount:0.##}"
);

// 980*2 + 1500*1 = 3460
Check(totalAmount, 3460m, "order total amount");
Console.WriteLine();

// ---- 6. Edit through the EditModel + Mapper (screen-input simulation) ----
// The EditModel is the generated binding model for screens: Binding{Property} holds the on-screen input
// string, the confirmed typed value and RowState track the change state, and conversion errors surface via
// INotifyDataErrorInfo. The Mapper converts between the entity and the edit model.
var productEntity = await products.GetByIdAsync(101);
var productMapper = new ProductMapper();
var editModel = productMapper.CreateEditModel(productEntity!);
Check(editModel.HasChanges, false, "edit model state right after load");

// Invalid input (as if typed into a TextBox bound to BindingUnitPrice): the conversion error is held
// per property, and the confirmed value stays untouched.
editModel.BindingUnitPrice = "abc";
Check(editModel.HasErrors, true, "conversion error detected for invalid input");

// Correct the input: the error clears, the confirmed value updates, and the model is promoted to Updated.
editModel.BindingUnitPrice = "1650";
Check(editModel.HasErrors, false, "error cleared after correcting the input");
Check(editModel.UnitPrice, 1650m, "confirmed value after input");
Check(editModel.HasChanges, true, "promoted to update target by the confirmed-value change");

// Apply the edit model back to the entity and save it with the repository.
productMapper.ApplyToEntity(editModel, productEntity!);
await products.UpdateAsync(productEntity!);

var reloadedProduct = await products.GetByIdAsync(101);
Console.WriteLine(
    $"[6] Edited the product through the EditModel + Mapper: UnitPrice={reloadedProduct!.UnitPrice:0.##}"
);
Check(reloadedProduct.UnitPrice, 1650m, "unit price after the edit-model round trip");
Console.WriteLine();

// ---- 7. Update (UpdateAsync) and cascade delete (ExecuteDeleteAsync(cascadeDelete: true)) ----
var toUpdate = await customers.GetByIdAsync(1);
toUpdate!.Name = "Taro Yamada (renamed)";
var updated = await customers.UpdateAsync(toUpdate);
Check(updated, true, "customer update");

var reloaded = await customers.GetByIdAsync(1);
Console.WriteLine($"[7-a] Updated the customer: {reloaded!.Name}");
Check(reloaded.Name, "Taro Yamada (renamed)", "customer name after update");

// Delete customer 1 together with its children (orders and order lines) — an explicit cascade delete on the
// application side, equivalent to FK ON DELETE CASCADE.
var deletedCount = await customers
    .Query()
    .Where(c => c.CustomerId == 1)
    .ExecuteDeleteAsync(cascadeDelete: true);
Console.WriteLine(
    $"[7-b] Deleted customer 1 together with its children (records deleted: {deletedCount})."
);

// 1 customer + 1 order + 2 order lines = 4
Check(deletedCount, 4, "cascade-deleted record count");

// Only customer 2 remains; verify no orders or order lines are left.
Check((await customers.GetAllAsync()).Count, 1, "customer count after delete");
Check((await orders.GetAllAsync()).Count, 0, "order count after delete");
Check(
    await orders.ExecuteScalarSqlAsync<int>("SELECT COUNT(*) FROM \"order_lines\"", null),
    0,
    "order line count after delete"
);
Console.WriteLine();

Console.WriteLine("All scenarios succeeded.");
return 0;

// Read the DDL (EcOrder.sql) and apply it to the SQLite file DB to create the schema.
static async Task CreateSchemaAsync(string connectionString)
{
    var ddlPath = Path.Combine(AppContext.BaseDirectory, "EcOrder.sql");
    var ddl = await File.ReadAllTextAsync(ddlPath);

    await using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();

    // Enable the foreign key constraints in the generated DDL (SQLite disables FK enforcement by default).
    await using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
    }

    // Microsoft.Data.Sqlite can execute multiple semicolon-separated statements in a single ExecuteNonQuery.
    await using var command = conn.CreateCommand();
    command.CommandText = ddl;
    await command.ExecuteNonQueryAsync();
}

// A small helper that throws when the actual value differs from the expected one, making the exit code
// non-zero so CI can detect it.
static void Check<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException(
            $"Verification failed ({label}): expected={expected} actual={actual}"
        );
    }
}
