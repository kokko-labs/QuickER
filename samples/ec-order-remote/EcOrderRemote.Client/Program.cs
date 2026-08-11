using EcOrderRemoteSample.Generated;
using Microsoft.Extensions.DependencyInjection;

// A sample client that calls the server (EcOrderRemote.Server) in another process over HTTP + JSON, using only
// the HTTP client implementations QuickER generated from the diagram with "remote service generation"
// (GenerateRemoteServices).
// The calling code is written against exactly the same interfaces (I{Entity}RemoteRepository) as the DB-direct
// case; the only difference is that DI registration becomes a single AddGeneratedHttpRemoteRepositories line.
// The scenarios are limited to the three points where going over HTTP actually matters (RowState settling
// after a graph save, transfer of a projection DTO, and type restoration of SaveConflictException). See
// samples/ec-order for the demonstrations of basic CRUD, Include, raw SQL, and so on.

// The server base URL can be overridden with the first argument (pass the same value as the server).
// The default is the same fixed port as the server.
var baseUrl = args.FirstOrDefault() ?? "http://127.0.0.1:5210";

// Register the generated HTTP client implementations with DI. baseAddress includes the server prefix, which is
// the one MapGeneratedRemoteEndpoints was mapped under — RemotePaths.DefaultPrefix here, the constant both
// sides read so the value is written down once.
// Swap just this line for AddGeneratedSqliteRepositories (DB-direct) and the same calling code runs locally.
using var provider = new ServiceCollection()
    .AddGeneratedHttpRemoteRepositories($"{baseUrl}{RemotePaths.DefaultPrefix}")
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRemoteRepository>();
var products = provider.GetRequiredService<IProductRemoteRepository>();
var orders = provider.GetRequiredService<IOrderRemoteRepository>();

// Wait for the server to come up (it starts in another process, so establishing a connection can take a few
// hundred ms). PingAsync polls the liveness endpoint (GET {prefix}/health) and reports "not reachable" as
// false instead of throwing, which is exactly what a wait loop needs.
await WaitForServerAsync(customers);

// ---- 1. Register the seed data the later scenarios refer to (1 customer, 2 products) ----
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 1,
        Name = "Taro Yamada",
        Email = "taro@example.com",
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

Console.WriteLine("[1] Registered the seed data (1 customer, 2 products) over HTTP.");
Console.WriteLine();

// ---- 2. Graph-save orders in one request (MarkAdded -> SaveAsync) and RowState settling after the save ----
// Order 1000 is a graph with 2 order lines; order 1001 has no lines. Both belong to customer 1.
var order1000 = new OrderEntity
{
    OrderId = 1000,
    CustomerId = 1,
    OrderedAt = new DateTime(2026, 7, 7, 13, 47, 9, DateTimeKind.Unspecified),
    Memo = "First order",
};

order1000.OrderLines.Add(
    new OrderLineEntity
    {
        OrderLineId = 5000,
        OrderId = 1000,
        ProductId = 100,
        Quantity = 2,
        UnitPrice = 980m,
    }
);
order1000.OrderLines.Add(
    new OrderLineEntity
    {
        OrderLineId = 5001,
        OrderId = 1000,
        ProductId = 101,
        Quantity = 1,
        UnitPrice = 1500m,
    }
);

// One call marks the whole freshly built aggregate: MarkAdded follows the cascade navigations a graph save
// follows, so the order lines are marked as inserts too (build the graph first, then mark it).
order1000.MarkAdded(includeChildren: true);

var order1001 = new OrderEntity
{
    OrderId = 1001,
    CustomerId = 1,
    OrderedAt = new DateTime(2026, 7, 8, 9, 12, 0, DateTimeKind.Unspecified),
    Memo = "Order without lines",
};
order1001.MarkAdded();

// Save multiple aggregate roots in a single request (order 1000 = 3 records + order 1001 = 1 record).
var savedCount = await orders.SaveAsync(new[] { order1000, order1001 });
Console.WriteLine(
    $"[2] Graph-saved 2 orders (one with 2 order lines) over HTTP in one request (records saved: {savedCount})."
);
Check(savedCount, 4, "graph-saved record count");

// Just like the DB-direct case (EntityGraphSaver.AcceptChanges), the local RowState settles to Unchanged once
// the save succeeds. That this settling also holds over HTTP is the point of this sample (the caller-side
// semantics do not change from the DB-direct case).
Check(order1000.HasChanges, false, "RowState of order 1000 settled after save");
Check(order1001.HasChanges, false, "RowState of order 1001 settled after save");
Console.WriteLine();

// ---- 3. Remote transfer of a named query (projection DTO) ----
// GetSummaries is a projection query that returns order summaries (order ID, ordered-at, memo) in descending
// order ID. Verify that the projection DTO (OrderSummaryRow) reaches the client as JSON.
var summaries = await orders.GetSummariesAsync(1);
Check(summaries.Count, 2, "order summary count");
Check(summaries[0].OrderId, 1001, "first summary order ID (descending)");
Check(summaries[1].OrderId, 1000, "second summary order ID (descending)");
Check(summaries[0].Memo, "Order without lines", "first summary memo");
Check(summaries[1].Memo, "First order", "second summary memo");

Console.WriteLine("[3] Fetched a named query (projection DTO) over HTTP:");

foreach (var row in summaries)
{
    Console.WriteLine(
        $"    OrderId={row.OrderId} OrderedAt={row.OrderedAt:yyyy-MM-dd HH:mm:ss} Memo={row.Memo}"
    );
}

Console.WriteLine();

// ---- 4. Type restoration of SaveConflictException via HTTP 409 ----
// An update-save of a non-existent order (insertWhenUpdateMissing=false) becomes an optimistic conflict on the
// server, which throws SaveConflictException. Via an HTTP 409 plus structured JSON it is restored on the client
// as the same SaveConflictException — demonstrating that you can write exactly the same catch as in the
// DB-direct case. The structured details (Reason / EntityTypeName / Key) survive the transport too, so a
// reload-and-retry loop does not have to parse the message.
var missing = new OrderEntity
{
    OrderId = 9999,
    CustomerId = 1,
    OrderedAt = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Unspecified),
    Memo = "Update of a non-existent order",
};
missing.MarkUpdated();

SaveConflictException? conflict = null;

try
{
    await orders.SaveAsync(missing);
}
catch (SaveConflictException ex)
{
    // The server-side exception type was restored across HTTP (the same catch as the DB-direct case).
    conflict = ex;
}

Check(conflict is not null, true, "type restoration of SaveConflictException via HTTP 409");
Console.WriteLine(
    "[4] Caught SaveConflictException over HTTP for an update-save of a non-existent order:"
);
Console.WriteLine(
    $"    Reason={conflict!.Reason} EntityType={conflict.EntityTypeName} Key={conflict.Key}"
);
Check(conflict.Reason, SaveConflictReason.NotFound, "conflict reason restored across HTTP");
Check(conflict.EntityTypeName, nameof(OrderEntity), "conflict entity type restored across HTTP");
Check(conflict.Key, "9999", "conflict key restored across HTTP");
Console.WriteLine();

Console.WriteLine("All scenarios succeeded.");
return 0;

// Poll the liveness endpoint until the server answers. A small helper for waiting on the server started in
// another process (including CI).
// PingAsync lives on the generated HTTP client (HttpRemoteRepository), not on the remote interface — the
// interface is the surface a DB-direct implementation shares, and liveness has no meaning there — so the
// resolved instance is cast to its concrete type.
static async Task WaitForServerAsync(ICustomerRemoteRepository customers)
{
    const int maxAttempts = 30;
    var client = (HttpCustomerRemoteRepository)customers;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        if (await client.PingAsync())
        {
            return;
        }

        // The server is not accepting connections yet; wait a bit and retry.
        await Task.Delay(500);
    }

    throw new InvalidOperationException(
        $"Could not connect to the server ({maxAttempts} attempts). Check that the server (EcOrderRemote.Server) is running."
    );
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
