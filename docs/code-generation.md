# Using the Generated Code

*English | [日本語](code-generation.ja.md)*

This document describes the structure of the C# code QuickER generates and how to use its data-access layer (QuickER Repository / EF Core Repository). For how to run generation, see the [CLI reference](cli.md); for a working example, see [samples/ec-order](../samples/ec-order).

## What gets generated

| Category | Contents |
|---|---|
| Entity | A POCO that corresponds to a table. UI-framework independent (no dependency on CommunityToolkit or the like). Carries `RowState` (Unchanged / Added / Updated / Removed), state-transition methods such as `MarkAdded()`, and navigation properties (parent reference / child collection). |
| EditModel | A model for screen editing, plus conversion to and from the Entity. |
| Mapper | A converter for Entity ⇄ EditModel. |
| Value objects (optional) | A per-column value-object type (e.g. `CustomerIdValue`). Emitted only when `GenerateValueObjects` is enabled (see [Value objects](#value-objects-generatevalueobjects)). |
| Repository shared contracts | `IRepository<TEntity, TKey>` and a per-entity interface (e.g. `ICustomerRepository`). Both the QuickER Repository and the EF Core Repository implement the same contracts. |
| QuickER Repository implementation | Lightweight per-dialect implementations (SQL Server / SQLite) plus DI-registration extensions. |
| EF Core Repository | `QuickErDbContext` (including the Fluent configuration) plus the EF Core Repository plus DI-registration extensions. |
| Runtime | The fixed code the above relies on (inlined into the output by default; a package-reference mode is also available). |

By default, an Entity is decorated with DataAnnotations and **DB-definition metadata attributes** (`[DbTableMeta]` / `[DbColumnMeta]`) that record a dialect-neutral type token (`string(50)` / `decimal(10,2)`, etc.) and a description. The generated code therefore doubles as a self-describing document of the DB definition. This is controlled by `IncludeDataAnnotations` (default ON), but it cannot be turned off in a configuration that generates the QuickER Repository, EF Core Repository, or in-memory Repository contracts (a diagnostic error), because the runtime reads `[Table]` / `[Key]` / `[Column]` through reflection.

> **Prerequisite**: Repository generation targets a single primary key with application-assigned keys (tables with a composite key or DB auto-numbering can only use the Entity / EditModel).

> **Target framework**: The generated code is developed and verified on .NET 10. It also builds on .NET 8 as things stand, but that is not guaranteed. The runtime NuGet packages target `net10.0` only, so package-reference mode requires .NET 10.

## Value objects (GenerateValueObjects)

This option generates columns as a **per-column value-object type** (`CustomerIdValue` / `NameValue`, etc.) instead of a raw type (`int` / `string`, etc.) (default OFF; CLI `--generate-value-objects` / `GenerateValueObjects` in quicker.json / the "Turn all columns into value objects" checkbox in the "Value Objects" row of the GUI). It can be chosen regardless of the DB-access selection (None / QuickER Repository / EF Core Repository), and it combines with multi-target, in-memory, and remote.

When ON, the columns of every table are grouped globally **by column name**, and one value-object type is generated per column name. A foreign-key column that shares a name with a primary key **shares the same type**, so mixing up IDs becomes a compile error. Entity properties and repository key types also become value objects:

```csharp
// ICustomerRepository : IRepository<CustomerEntity, CustomerIdValue>
var customer = await customers.GetByIdAsync(CustomerIdValue.Create(1));

// orders.GetByIdAsync(customer.CustomerId) is a compile error because it is not an OrderIdValue
```

When same-named columns disagree on their definition (type, length, precision), a Warning diagnostic is emitted, and the definitions are unified into a single type by preferring the primary key's definition (or, if there is no primary key, the widest definition).

### Generated types and validation

Each value object is a `sealed partial class` whose constructor is private and which can only be created through a static factory. Validation code is generated automatically from the column definition in the diagram (maximum length for strings; precision and scale for `decimal`—out-of-range values are rejected rather than rounded):

```csharp
var name = NameValue.Create("Alice");   // A validation violation throws ValueObjectValidationException

if (NameValue.TryCreate(input, out var vo, out var errors))   // Validate without throwing
{
    entity.Name = vo!;
}
```

The base class is chosen according to the value type. In addition to value-based equality (`==` / `Equals`), numeric and date/time types get comparison operators (`<` / `>=`, etc.), and strings get `Contains` / `StartsWith` / `EndsWith`.

### partial extension points

Every generated class offers two ways to customize messages and display names — the rule is the same across every static class and every generation mode (inline / package-reference):

- **Bulk** — replace a static settable `Func` on the fixed infra at app startup; applies everywhere.
- **Per-type** — implement a `Customize*` (`ref`) partial on the generated concrete class; applies to that type/property only.

```csharp
// Bulk, at startup: localize messages, and stop using descriptions for display names
ValueObjectValidationMessages.ValueRequired = static () => "値を入力してください。";
EditModelMessages.Required = static name => $"{name}は必須です。";
GeneratedDisplayNames.Resolve = static (name, _) => name;   // ignore descriptions; use the member name
```

```csharp
public sealed partial class NameValue
{
    // Additional validation (called after the auto-generated validation)
    static partial void OnValidate(string value, ICollection<string> errors)
    {
        if (value.Contains(' '))
        {
            errors.Add("Whitespace is not allowed.");
        }
    }

    // Replace the display name used in validation messages, etc. (default is the column description; if unspecified, the property name)
    static partial void CustomizeDisplayName(ref string displayName) => displayName = "Full name";
}

public partial class CustomerEditModel
{
    // Per-property message tweak (propertyName lets you branch by column)
    partial void CustomizeParseErrorMessage(string propertyName, string inputValue, string typeName, ref string message)
    {
        if (propertyName == nameof(Age))
        {
            message = $"'{inputValue}' is not a valid age.";
        }
    }
}
```

Static classes: `ValueObjectValidationMessages` (`MaxLengthExceeded` / `ScaleExceeded` / `PrecisionExceeded` / `ValueRequired`), `EditModelMessages` (`Required` / `ParseFailed` / `JoinValueObjectErrors`), `GeneratedDisplayNames` (`Resolve` — used to resolve the display name of entities, edit-model properties, and value objects alike). In package-reference mode, all three ship inside the `QuickER.Runtime` package.

Per-type partials: value object — `CustomizeDisplayName` / `CustomizeMaxLengthErrorMessage` / `CustomizeScaleErrorMessage` / `CustomizePrecisionErrorMessage` / `CustomizeValueRequiredErrorMessage` (string / byte[] only) / `OnValidate`; edit model — `CustomizeRequiredErrorMessage` / `CustomizeParseErrorMessage` / `CustomizePropertyDisplayName`; entity — `CustomizeDisplayName` (an `override`, not a partial, as before). A value object's display string `DisplayValue` (virtual) can also be overridden.

### Integration with each feature (transparent support)

Value objects can be handled transparently throughout the generated code. You rarely need to unwrap them to the raw value by hand:

| Feature | Behavior |
|---|---|
| QuickER Repository | SQL parameters are automatically converted to the wrapped value before binding, and reads restore the value object via `Create`. |
| `Query()` (expression tree) | Value-object comparisons, string `Contains`, and so on are translated directly to SQL. |
| EF Core mode | The Fluent configuration automatically applies a value conversion (`HasConversion`) and a translation plugin (server-side translation of string methods and `.Value` references). |
| Named query | Method parameters stay the raw type. The generated condition expression is converted to value-object comparisons automatically (IN lifts the list). |
| EditModel | The committed-value property is a value object such as `NameValue?`. The screen-binding property `BindingXxx` (string) validates with `TryCreate` and surfaces errors through `INotifyDataErrorInfo`. |
| JSON (`ToJson` / `Clone` / remote transfer) | Serialized **as the wrapped value** (`{"customerId": 1}`. The value object's wrapper structure does not appear in the JSON). |

> **Note**: Reads from the DB or from JSON are also validated through `Create`. If existing data holds a value that does not pass validation, the read throws `ValueObjectValidationException`, so keep the extra validation you add in `OnValidate` consistent with existing data.

### GUID keys for string primary keys (UseGuidKeyForStringPrimaryKey)

Turning on `UseGuidKeyForStringPrimaryKey` (CLI `--use-guid-key-for-string-primary-key` / the GUI's "Use GuidKey for string primary keys") together with `GenerateValueObjects` makes the value object of a string primary key derive from a GUID-generating base class (`ValueObjectGuidKeyBase`), so a parameterless `Create()` mints a new key:

```csharp
// When document_id is a string primary key
var id = DocumentIdValue.Create();   // A new key wrapping Guid.NewGuid() as a string
```

This lets you satisfy the "primary keys are application-assigned" prerequisite of repository generation (above) without writing any key-generation logic.

## QuickER Repository

A lightweight repository with minimal dependencies (ADO only). The supported dialects are SQL Server (`FOR JSON` based) and SQLite (plain SELECT plus multi-query).

The DI-registration extensions are generated with per-engine names (`AddGeneratedSqlServerRepositories` / `AddGeneratedSqliteRepositories`).

```csharp
// DI registration (the generated extension method; choose SqlServer / Sqlite by dialect)
var provider = new ServiceCollection()
    .AddGeneratedSqliteRepositories(connectionString)
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRepository>();
```

### Basic operations

```csharp
await customers.InsertAsync(new CustomerEntity { CustomerId = 1, Name = "Alice" });
var one  = await customers.GetByIdAsync(1);
var all  = await customers.GetAllAsync();
one!.Name = "Alice (renamed)";
await customers.UpdateAsync(one);
await customers.DeleteAsync(1);
await customers.BulkInsertAsync(manyCustomers);   // bulk insert
```

### Queries (expression tree → SQL)

```csharp
var result = await customers.Query()
    .Where(c => c.Name.Contains("Ali") && c.Balance >= 1000m)   // LIKE escapes wildcards automatically
    .OrderBy(c => c.CustomerId)
    .Skip(20).Take(10)                                          // paging
    .Include(c => c.Orders)                                     // parent → child collection
        .ThenInclude(o => o.OrderLines)                         // load recursively
    .ToListAsync();
```

Supported: equality, comparison, `&&`/`||`, `Contains`/`StartsWith`/`EndsWith` (LIKE), `Contains` on a list (IN), date parts (`Year`, etc.), `string.IsNullOrEmpty`, and value-object comparison. **Projection (Select), GroupBy, Join, and arithmetic expressions are not supported** (they throw at runtime; work around them with raw SQL or EF Core).

### Graph save (save parent and children in one call)

```csharp
var order = new OrderEntity { OrderId = 1000, CustomerId = 1 };
order.MarkAdded();
var line = new OrderLineEntity { OrderLineId = 5000, OrderId = 1000, ProductId = 100, Quantity = 2 };
line.MarkAdded();
order.OrderLines.Add(line);

var affected = await orders.SaveAsync(order);   // Runs INSERT / UPDATE / DELETE per RowState in one transaction
```

### Save hooks (ISaveHook)

A mechanism that **inserts processing before and after each operation** of a graph save (`SaveAsync`). The main use cases are a single-row skip based on a state check in the before-step, and **registering file data within the same transaction** in the after-step (atomicity of the save and the blob write). Hooks are always generated; if none are registered they are a complete no-op (behavior is unchanged from before).

Implement `ISaveHook<TEntity>` and register it in DI. Both methods have a default implementation, so you can write **only the one you need**.

```csharp
public sealed class DocumentSaveHook : ISaveHook<DocumentEntity>
{
    // Just before the operation. Returning false skips that single row (default is not to skip)
    public Task<bool> BeforeSaveAsync(
        DocumentEntity entity, SaveOperation operation, CancellationToken ct = default)
    {
        // Example: allow deleting only approved documents (skip other deletes)
        if (operation == SaveOperation.Delete && !entity.IsApproved)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    // Just after the operation, before commit. context joins the same transaction
    public async Task AfterSaveAsync(
        DocumentEntity entity, SaveOperation operation, ISaveHookContext context,
        CancellationToken ct = default)
    {
        if (operation == SaveOperation.Insert)
        {
            // Stream-write to the excluded column (blob) within the same transaction as the save (atomic)
            await context.WriteBinaryColumnFromFileAsync(
                nameof(DocumentEntity.Payload), entity.DocumentId, "/tmp/upload.bin", ct);
            // Leave an audit row with raw SQL (also the same transaction)
            await context.ExecuteSqlAsync(
                "INSERT INTO audit (note) VALUES (@note)", new { note = $"created {entity.DocumentId}" }, ct);
        }
    }
}
```

```csharp
// DI registration (Singleton or Scoped; use Scoped if the hook uses scoped services)
services.AddSingleton<ISaveHook<DocumentEntity>, DocumentSaveHook>();
```

You can register multiple hooks for the same entity type. **Before runs in registration order** and **short-circuits the moment one returns `false`** (the remaining Before hooks are not called, and that row is skipped). **After also runs in registration order.** An exception thrown by Before / After propagates as-is and, on an implementation target that has a real transaction, rolls back the entire save (for in-memory, see the table below).

**Only `SaveAsync` (both the single and the multiple form) is targeted.** Direct calls to the low-level APIs `InsertAsync` / `UpdateAsync` / `DeleteAsync`, and `BulkInsertAsync`, **bypass** the hooks (they do not fire).

#### Before and skip semantics

`false` skips **only that entity's single operation** (other rows proceed). A skipped row does not get an After call, and its `RowState` is left unchanged (it is excluded from `AcceptChanges`).

Because a skip is isolated, consistency is the hook author's responsibility. In particular, **deletes run children-first**, so **if you return `false` for only the root (parent) in a subtree delete, the children are deleted and only the root remains**. To stop the parent, the children's hooks must also return `false`. An inconsistent skip (for example, skipping a new parent while saving a new child) falls to the safe side via an FK constraint violation → exception → **full rollback**, provided the FK constraint is actually defined in the DB.

#### After and the context

After receives, **just after the operation and before commit**, an `ISaveHookContext` that joins the in-flight transaction. Calling the repository's ordinary APIs from within the hook would contend for locks on a separate connection, so use the operations exposed through `context`. Because throwing from After rolls back the whole save, the half-finished state of "the row exists but the file is not registered" is structurally impossible in the QuickER Repository (SQL Server / SQLite) (for in-memory, see the table below).

Operations the context provides (it does not expose raw handles):

- `WriteBinaryColumnAsync(propertyName, key, stream, length?)` / the file convenience method `WriteBinaryColumnFromFileAsync(propertyName, key, path)` — stream-write to an excluded column (when `ExcludeUnboundedBinaryColumns` is enabled) (specify the column with `nameof`).
- `ExecuteSqlAsync(sql, parameters)` — arbitrary DML (an audit row, a write to a related table, etc.).

`operation` receives **the operation that actually happened**. If `insertWhenUpdateMissing: true` and no update target is found so it switches to INSERT, Before is called once with `Update` and After is called with the actual `Insert`.

#### Differences by implementation target

| Target | Hook firing | Context support |
|---|---|---|
| QuickER Repository (SQL Server / SQLite) | Full support (After fires right after each operation) | Both `WriteBinaryColumnAsync` and `ExecuteSqlAsync` are supported |
| EF Core Repository (`GenerateEfCore`) | Supported (After fires in a batch after `SaveChanges`) | `ExecuteSqlAsync` supported; `WriteBinaryColumnAsync` throws `NotSupportedException` |
| In-memory (`GenerateInMemoryRepositories`) | Supported (pseudo transaction) | `WriteBinaryColumnAsync` writes to the store; `ExecuteSqlAsync` throws `NotSupportedException`. Because there is no real transaction, **store changes remain even if After throws** (best effort) |
| Remote (`--generate-remote-services`) | **A hook registered in the server-side DI fires** | Follows the server-side real implementation. **Known limitation**: even for a row the server skipped in Before, the client-side `RowState` does not reflect the skip and is committed to `Unchanged` |

### Raw SQL escape hatch

When a query cannot be expressed with an expression tree, you can always drop down to raw SQL (parameters are an anonymous object).

```csharp
// Strict full-column mapping (restore into the Entity)
var rows = await customers.QueryBySqlAsync(
    "SELECT * FROM customers WHERE balance >= @min", new { min = 1000m });

// Projection / single value (also available on the entity-independent ISqlExecutor)
var names = await executor.QueryProjectionBySqlAsync<string>("SELECT name FROM customers", null);
var total = await orders.ExecuteScalarSqlAsync<decimal>(
    "SELECT SUM(quantity * unit_price) FROM order_lines WHERE order_id = @id", new { id = 1000 });

// Mutations (return the affected row count)
var affected = await customers.ExecuteSqlAsync("UPDATE customers SET balance = 0", null);
```

### Uniqueness pre-check (CheckUniquenessAsync)

A table's UNIQUE constraints are stamped on its generated **Entity** class as `[UniqueConstraint("PropA", "PropB", Name = "UQ_...")]`, next to `[DbTableMeta]` / `[DbColumnMeta]`. Like those, it is definition metadata that makes the entity a self-describing document of the DB definition; it drives no runtime behaviour (the checks below are plain generated code). The attribute type itself is emitted only when at least one table has a constraint to declare.

Every generated repository contract carries a bulk check built from the diagram's UNIQUE constraints (it is always generated, whether or not the table has any constraint):

```csharp
Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
    TEntity entity, CancellationToken cancellationToken = default);
```

For each UNIQUE constraint of the table it asks "does a row with the same value tuple already exist, **excluding rows that share this entity's primary key**?". Because the same-key row is excluded, the same call is correct both before an insert (there is no such row, so the exclusion is a no-op) and before an update. Constraint member values that contain a `null` are skipped, since NULL collision semantics differ per dialect.

> The result is **advisory**. The definitive guarantee is the database's own UNIQUE constraint: a concurrent insert between the check and the save can still make the save fail (TOCTOU). Use the check to give a friendly message, and keep handling the save exception.

The implementation is a single expression-tree query shared by every backend (QuickER Repository for each dialect, EF Core, and in-memory), so all of them behave the same.

```csharp
var violations = await orders.CheckUniquenessAsync(order);

foreach (var violation in violations)
{
    // ConstraintName = the DDL name (a synthesized UQ_{table}_{columns} name when the diagram sets none)
    // PropertyNames  = the entity property names that make up the constraint (declaration order)
    Console.WriteLine($"{violation.ConstraintName}: {string.Join(", ", violation.PropertyNames)}");
}
```

#### User-defined checks

Rules the diagram cannot express (a conditional uniqueness, a cross-table rule) are added through an optional partial method generated on every repository implementation. While it is unimplemented the call is erased at no cost.

```csharp
public sealed partial class OrderRepository
{
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= []).Add(static async (entity, cancellationToken) =>
            await SomeLookupAsync(entity, cancellationToken)
                ? new UniquenessViolation("UQ_custom_rule", [nameof(OrderEntity.Code)], "This code is reserved.")
                : null);
}
```

The generated checks run first, then the collected delegates in registration order; every non-null result joins the returned list. With remote services the whole check (hooks included) runs in the server-side repository — the HTTP client only forwards the call.

#### Edit models: duplicates inside a collection

An edit model whose table declares UNIQUE constraints also declares them in generated code, as a `static readonly` table of `EditModelUniquenessConstraint` (constraint name, member property names, and a compiled accessor for their values) published through the `UniquenessConstraints` property. `EditModelCollection<T>.Validate()` reads that table and flags values duplicated **among the elements themselves**, registering an error on the binding property of every member of each duplicated group — no reflection is involved, exactly like the generated required-field checks. Value tuples containing a `null` are skipped and deletion targets (`RowState.Removed`) are excluded, matching the database check. For a root-level list that is not an `EditModelCollection<T>`, call the same helper directly:

```csharp
var valid = EditModelUniquenessValidator.Validate(models);
```

#### Edit models: checking against the database

When edit models and a repository contract are both generated, each edit model also gets a convenience wrapper:

```csharp
// The repository parameter is I{Entity}RemoteRepository when remote contracts are generated, I{Entity}Repository otherwise
if (!await editModel.ValidateUniqueAsync(repository))
{
    // Errors are already registered on the binding properties (INotifyDataErrorInfo shows them in the UI)
}
```

It builds an entity from the edit model's confirmed values, calls `CheckUniquenessAsync`, and maps each violation's `PropertyNames` back to the binding properties. A violation with no property names (or with names that do not belong to the edit model) becomes a model-level error registered under the empty property name, which `GetErrors(null)` returns. The duplicate-value errors registered by the previous call are cleared first, so re-checking never leaves stale errors.

The message comes from `EditModelMessages.DuplicateValue` (a `static Func` taking the display names of the constraint's member properties), refined per class by the optional `partial void CustomizeDuplicateErrorMessage(IReadOnlyList<string> propertyNames, ref string message)`. A `UniquenessViolation.Message` supplied by a user-defined check wins over both.

#### Related pre-checks with the existing API

Uniqueness is the only pre-check with generated support; the neighbouring validations are one-liners over the existing API.

```csharp
// Primary key already taken (before an insert)
var taken = await orders.GetByIdAsync(order.OrderId) is not null;

// Foreign key target exists (before saving a child)
var parentExists = await customers.GetByIdAsync(order.CustomerId) is not null;

// Still referenced by children (before a delete)
var referenced = await orders.Query().Where(o => o.CustomerId == customerId).AnyAsync();
```

Like the uniqueness check these are advisory: the database's own constraints remain the final authority.

### Store-generated columns (rowversion / timestamp)

A column whose value the DB generates (SQL Server's `rowversion` / `timestamp`, etc.) gets the marker attribute `[StoreGeneratedColumn]` on its generated Entity property and is **automatically excluded from INSERT / BulkInsert / UPDATE** in the QuickER Repository (the attribute is applied to any column the type mapper recognizes as a row-version column — SQL Server's `rowversion` / `timestamp` — regardless of the generation options).

- **Never written**: the DB assigns these columns' values, so the repository writes no explicit value. Attempting an explicit insert makes SQL Server return `Cannot insert an explicit value into a timestamp column.`, but the exclusion avoids this runtime error.
- **Read on SELECT**: they are included in the results of `GetByIdAsync` / `GetAllAsync` / `Query()`, and you can read their values (they can be referenced as a concurrency token).
- **In EF Core mode** the Fluent configuration's `IsRowVersion()` already treats them as store-generated, so this mechanism is not applied.
- **Optimistic concurrency using rowversion (comparing the version in the UPDATE WHERE) is out of scope** (future work). Note that when a graph save (`SaveAsync`) cannot find the update target row (e.g. another user deleted it), `SaveConflictException` is thrown, but this is an existence check based on an affected-row count of 0 and is unrelated to rowversion comparison.

### Excluding unbounded binary columns (ExcludeUnboundedBinaryColumns)

This option avoids round-tripping a huge BLOB on every list fetch or update (it protects memory) (default OFF; CLI `--exclude-unbounded-binary-columns` / the "Do not fetch unbounded binary columns (varbinary(max) / BLOB)" checkbox in the GUI (shown only when the DB-access selection is QuickER Repository) / `ExcludeUnboundedBinaryColumns` in quicker.json). When ON, the marker attribute `[UnboundedBinaryColumn]` is applied to the Entity property of a **binary column with no size limit**, and that column is excluded from SELECT / UPDATE in the QuickER Repository. At generation time, the list of excluded columns is reported through an Info diagnostic (CLI output / the GUI's generation-result dialog).

The decision is made from the column's declared type (types with a declared length, such as `rowversion`, `binary(n)`, or `varbinary(n)`, are not targeted):

| Dialect | Excluded | Not excluded (bounded) |
|---|---|---|
| SQL Server | `varbinary(max)` / `image` | `binary(n)` / `varbinary(n)` / `rowversion` |
| SQLite | `BLOB` with no declared length | `BLOB(n)` |
| PostgreSQL | `bytea` | — |
| MySQL | `BLOB` / `MEDIUMBLOB` / `LONGBLOB` | `TINYBLOB` / `BINARY(n)` / `VARBINARY(n)` |
| Oracle | `BLOB` / `LONG RAW` | `RAW(n)` |

Key behaviors:

- **Excluded from SELECT**: in the results of `GetByIdAsync` / `GetAllAsync` / `Query()`, an excluded column is `null` (it is not read from the DB) unless you opt in with `WithUnboundedBinary()` (described below).
- **Excluded from UPDATE**: an excluded column is not in the SET clause of the update SQL. Running `UpdateAsync` / `SaveAsync` while an excluded column still holds a value throws a **runtime exception** (it does not silently drop data).
- **INSERT / BulkInsert keep all columns**: the first write can pass values as usual.
- **A named-query projection** that references an excluded column does fetch it (a projection is an explicit column selection).
- It can be fetched by explicitly SELECTing it in **raw SQL** (see the operational example below).
- **Not applied in EF Core mode** (queries via `DbSet` / `SaveChanges`) (column selection in EF Core is EF Core's responsibility).
- The in-memory repository (`GenerateInMemoryRepositories`) has parity with a real DB (the same exclusion behavior).

Read and write an excluded column with raw SQL:

```csharp
// Read the excluded column (an image, etc.) explicitly
var payload = await documents.QueryProjectionBySqlAsync<byte[]>(
    "SELECT payload FROM documents WHERE document_id = @id", new { id = 1 });

// Update the excluded column (it is not included in the UPDATE SET clause automatically, so write it with raw SQL)
await documents.ExecuteSqlAsync(
    "UPDATE documents SET payload = @payload WHERE document_id = @id",
    new { payload = bytes, id = 1 });
```

#### Read opt-in: `WithUnboundedBinary()`

Even for a diagram where exclusion is enabled, when you want to fetch the entity including the excluded column **for this call only**, splice `WithUnboundedBinary()` into the `Query()` chain (the API always exists because it is a no-op when there are no excluded columns). It lets you fetch an ordinary entity (`RowState = Unchanged`, with the excluded column mapped to its real data) without writing a raw-SQL projection.

```csharp
// Fetch the GetById equivalent, including the excluded columns (payload / thumb)
var doc = await documents
    .Query()
    .Where(d => d.DocumentId == 1)
    .WithUnboundedBinary()
    .FirstOrDefaultAsync();
```

Constraints and behavior:

- **Cannot be combined with `Include`** (`InvalidOperationException` when the terminal method runs). If you need the unbounded binary column, fetch it with a separate query that has no `Include`. This is because SQL Server's `Include` path goes through FOR JSON = Base64, which inflates memory for a huge BLOB (5–6× peak), so this restriction keeps the memory profile predictable for the "handle a huge BLOB" purpose (on SQL Server it fetches with a **plain SELECT** rather than FOR JSON).
- The effect applies only to `ToListAsync` / `FirstOrDefaultAsync` (it does not affect count, existence check, or the projection `ToProjectionListAsync`).
- The fetched entity is a legitimate entity, but the fact that the excluded column is out of scope for UPDATE does not change. Calling `UpdateAsync` on it as-is throws from the existing guard (update an excluded column with the raw SQL `ExecuteSqlAsync` above).
- In EF Core mode it is a no-op because EF Core reads all columns to begin with (only the `Include`-combination error is thrown identically for parity).

#### Stream accessors: `Read/Write{Column}Async`

When you enable the exclusion option (and generate the QuickER Repository), **streaming** read/write methods are additionally generated per excluded column (where they are placed depends on whether remote contracts exist; see below). They transfer between the DB and a stream (or file) **in O(chunks)—without loading the entire blob into memory**, avoiding a bulk `byte[]` read. Among the generated APIs, this is the option that keeps memory bounded for GB-scale binaries.

```csharp
// Example generated for documents.payload (an excluded column)
Task<bool> ReadPayloadAsync(int id, Stream destination, CancellationToken ct = default);
Task<bool> WritePayloadAsync(int id, Stream? source, long? length = null, CancellationToken ct = default);
// File convenience methods (extension methods; delegate to the Stream version)
Task<bool> ReadPayloadToFileAsync(int id, string path, CancellationToken ct = default);
Task<bool> WritePayloadFromFileAsync(int id, string path, CancellationToken ct = default);
```

Semantics:

- **Return value**: `Read` returns `true` once it has written to the destination (an empty blob is also `true`); no row or a NULL column returns `false` (nothing is written to the destination). `Write` returns `true` if it could update, `false` if there is no row. This matches the bool convention of the existing `UpdateAsync`.
- **`Write(id, null)`** sets the column to `NULL` (a way to reset an excluded column to "unset").
- **Length**: automatic when `source` is `CanSeek` (`Length - Position`); otherwise the `length` argument is required (an omission throws `ArgumentException`). This is because SQLite's `zeroblob` requires the length before writing, and the contract is unified to be dialect-neutral.
- **Optimistic concurrency (rowversion, etc.) is out of scope** (direct column manipulation on par with raw SQL).
- **There is no INSERT-only method.** Write a new row in two steps: "INSERT (blob is `null` or empty) → stream in the body with `Write{Column}Async`".
- **Cannot be used in EF Core mode** (`NotSupportedException`). Because EF Core is dialect-independent by design, it cannot have dialect-specific streaming. Use the QuickER Repository, or implement it in a `partial` class (in a configuration that combines `GenerateEfCore` with the QuickER Repository, only the EF Core implementation throws).
- **Placement**: if remote contracts (`--generate-remote-contracts` / `--generate-remote-services`) are disabled, they sit directly on the full-featured repository interface `I{Entity}Repository`. If enabled, they move to the remote surface `I{Entity}RemoteRepository` (the full-featured interface inherits the remote surface, so calling code is the same in either configuration—purely additive). The file convenience methods follow the same target interface. Enabling remote services (`--generate-remote-services`) lets them transfer over HTTP (see "Binary transfer endpoints" below).

Choosing between it and `WithUnboundedBinary()`:

| | `WithUnboundedBinary()` | Stream accessor |
|---|---|---|
| Unit | Entity shape (multiple columns, multiple rows, no Include) | Read/write of a single column |
| Memory | Moderate (bulk `byte[]`) | **Bounded** — constant regardless of blob size (O(chunks)) |
| Use case | You temporarily want an entity including the excluded columns | Transfer a huge blob between the DB and a file/stream |
| Write | Not possible (fetch only; update with raw SQL) | Can write per column with `Write{Column}Async` |

## EF Core mode (GenerateEfCore)

Generates a dialect-independent `QuickErDbContext` that puts the existing Entity onto EF Core as-is, plus an **EF Core implementation of the same repository interfaces**. Migrations are out of scope, and schema creation remains the responsibility of DDL generation (EF Core connects only to an existing schema).

```csharp
// Swappable with the QuickER Repository by changing one DI-registration line
services.AddGeneratedEfCoreRepositories(options => options.UseSqlServer(connectionString));
// For SQLite / PostgreSQL / MySQL / Oracle, specify the corresponding EF Core provider's Use*
```

- Save uses a disconnected-graph save via `TrackGraph` (converts `RowState` to EF Core's state).
- An optimistic-concurrency conflict converts EF Core's exception to `SaveConflictException` (unifying the contract).
- The raw-SQL APIs are at full parity.

**Combined generation with the QuickER Repository** (both ON) is for parity verification and can only be specified via the CLI / config file; the GUI is an exclusive choice. Also, the EF Core Repository and multi-target QuickER Repositories (below) cannot be combined (a diagnostic error).

## Multi-target repositories (sqlserver + sqlite)

Specifying `--repository-dialects sqlserver,sqlite` outputs the neutral contracts once and the per-dialect implementations into the `.SqlServer` / `.Sqlite` sub-namespaces, letting you write to multiple DBs from the same process with keyed DI.

```csharp
services.AddGeneratedSqlServerRepositories(serviceKey: "primary", sqlServerConn);
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);

// The resolving side picks the same contract type by key
var primary = provider.GetRequiredKeyedService<ICustomerRepository>("primary");
var local   = provider.GetRequiredKeyedService<ICustomerRepository>("local");
```

## Remote-capable interfaces (--generate-remote-contracts)

`I{Entity}Repository` is a full-featured interface that, in addition to CRUD, save, and named queries, has every method including `Query()` (expression-tree query), raw SQL, and bulk insert. Specifying `--generate-remote-contracts` (`GenerateRemoteContracts` in quicker.json, the "Generate Repository interfaces for remote operations" checkbox in the GUI's "Remote" row) **additionally generates** an interface for remote operations.

| Surface | Interface | Operations included |
|---|---|---|
| Remote surface (additionally generated) | `I{Entity}RemoteRepository` | CRUD (GetById / GetAll / Insert / Update / Delete), graph save (Save), named queries |
| Full-featured surface (as before) | `I{Entity}Repository` (inherits the remote surface) | The above plus `Query()` (expression tree), the three raw-SQL variants, bulk insert |

Every method of the remote surface has arguments and return values composed purely of data (entities, primary keys, counts), so it can in principle cross a network boundary. If you keep the application body dependent only on the remote surface, the compiler catches any use of an operation that cannot cross the boundary even when you later swap the repository's implementation for a web-service-backed remote one. Processing that needs an expression tree or raw SQL just uses `I{Entity}Repository` as before, so "this part needs a direct DB connection" is readable from the type.

```csharp
// The application body depends only on the remote surface (the part that can later be swapped for a remote implementation)
public sealed class OrderService(IOrderRemoteRepository orders)
{
    public Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, CancellationToken ct) =>
        orders.GetByCustomerAsync(customerId, ct);   // A named query is on the remote surface
}

// Processing that needs raw SQL or an expression-tree query requests the full-featured surface as before (the type makes the direct-DB requirement explicit)
public sealed class OrderMaintenance(IOrderRepository orders)
{
    public Task<int> ArchiveAsync(CancellationToken ct) =>
        orders.ExecuteSqlAsync("UPDATE orders SET archived = 1 WHERE ...", cancellationToken: ct);
}
```

This option is purely additive. Turning it ON leaves `I{Entity}Repository`, the implementation classes, and the DI implementation registrations unchanged; the remote surface is merely added to DI as a forward to the same instance, so you can enable it at any time without breaking existing code (`AddGenerated*Repositories` resolves either surface).

## Remote services (--generate-remote-services) — three-tier layout

Specifying `--generate-remote-services` (`GenerateRemoteServices` in quicker.json, the "Generate HTTP client / server implementations" checkbox in the GUI's "Remote" row) generates a client and server implementation that provides the remote surface over the network using **HTTP + JSON** (the remote surface `--generate-remote-contracts` is enabled automatically).

| Output | Location | Contents |
|---|---|---|
| HTTP client implementation | Bundled into the main output (the only dependency is the BCL `HttpClient`) | `Http{Entity}RemoteRepository` (implements `I{Entity}RemoteRepository`) plus `AddGeneratedHttpRemoteRepositories` |
| Server implementation | `{baseName}.RemoteServer.g.cs` (a separate file) | `MapGeneratedRemoteEndpoints` (Minimal API; `POST {prefix}/{entity}/{operation}`; prefix default `/quicker`) |

The recommended project layout is: a **shared class library** (the main output—entities, contracts, client implementation) referenced by both the **server** (ASP.NET Core) and the **client app** (WPF, etc.), placing only the server file in the server project.

```csharp
// ---- Server (ASP.NET Core, Microsoft.NET.Sdk.Web) ----
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGeneratedSqliteRepositories(connectionString);   // The real implementation can be the QuickER Repository or the EF Core Repository

var app = builder.Build();
app.MapGeneratedRemoteEndpoints();          // To add authorization, chain .RequireAuthorization()
app.Run();

// ---- Client app (switch direct ⇔ remote with one DI-registration line) ----
// Direct: services.AddGeneratedSqliteRepositories(connectionString);
// Remote: services.AddGeneratedHttpRemoteRepositories("https://server:5001/quicker");
// The application body injects and uses IOrderRemoteRepository either way (no code change)
```

Points to keep in mind:

- **Serialization** uses the same semantics as the entity's JSON round trip (`ToJson` / `Clone`) (VO as the wrapped value, RowState included, parent-reference navigation does not cycle), and the client and server share `RemoteJson.Options`.
- **Named queries can all be called through the remote surface regardless of implementation method** (simple DSL / raw SQL / manual implementation) (the real implementation lives in the server-side repository).
- **Exception types are restored**: the server's `SaveConflictException` is thrown on the client as `SaveConflictException` too via HTTP 409 (the same catch as in the direct case works), and other server exceptions become `RemoteRepositoryException` (preserving the status code and message).
- **After a successful graph save (Save), the local RowState is also committed** (the same behavior as the direct case).
- **A 500 response carries the server-side exception message verbatim.** This is intentional: the message is what lets the client restore the failure, so that `catch` looks the same as in the direct case. The full exception, including the stack trace, is written only to the server side, through `ILoggerFactory` (category `QuickER.RemoteServer`; a no-op when the host has no logging provider). When you expose the endpoints beyond a trusted boundary, combine them with authorization or with exception-translating middleware so that internal details do not leak into the response.
- Authentication and TLS are out of scope. Configure the client with an authentication-handler-equipped HttpClient via `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)`, and add ASP.NET Core authorization to the return value (`RouteGroupBuilder`) of `MapGeneratedRemoteEndpoints()`.
- **The HttpClient returned by the factory overload is owned by the caller.** `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` invokes the factory every time a repository is resolved (once per scope and per entity), and the returned HttpClient is disposed by neither the generated code nor the DI container. Return a shared instance, or one managed by `IHttpClientFactory`; creating a new HttpClient on every call exhausts sockets. (The base-address overload creates a single shared instance that the container owns, so the client is disposed together with the `ServiceProvider`; a repository resolved from an already disposed provider therefore throws `ObjectDisposedException` on use.)
- The server file requires the ASP.NET Core FrameworkReference (`Microsoft.AspNetCore.App`) (no extra setup is needed if the project's SDK is `Microsoft.NET.Sdk.Web`).
- **The generated server class is extensible.** `GeneratedRemoteEndpoints` is a `partial` class, so your own endpoint helpers can live alongside the generated ones, and you can implement the `static partial void OnServerError(HttpContext, Exception)` hook in another part of the class to add custom handling (notifications, metrics, extra logging) whenever an endpoint responds with HTTP 500 — it runs after the built-in logging, and when you do not implement it the compiler removes the call itself. An exception thrown inside the hook is isolated: it is written to the server log and swallowed, so it never gets in the way of the original error response. Additional endpoints under the same prefix can also be mapped directly onto the `RouteGroupBuilder` returned by `MapGeneratedRemoteEndpoints()`.

### Binary transfer endpoints (Stream accessors for unbounded binary columns)

When combined with unbounded-binary exclusion (`--exclude-unbounded-binary-columns`), the excluded column's Stream accessors (`Read/Write{Column}Async`) are **streamed over HTTP**. Because a JSON envelope (`POST` + Base64) cannot avoid the memory inflation of a huge blob, these intentionally use a **second, REST-style form** (verb separation, raw body, `application/octet-stream`). The following three endpoints are generated per excluded column (`{column}` is the C# property name):

| Verb / URL | Meaning | Response |
|---|---|---|
| `GET {prefix}/{entity}/{column}?id=` | Download (stream the body to the destination) | 200 + `application/octet-stream` (an empty blob is also 200) / no row or NULL is **404** (`false` on the client) |
| `PUT {prefix}/{entity}/{column}?id=` | Upload (raw body, `Content-Length` required) | Success **204** / no row **404** (`false`) / missing `Content-Length` (chunked) is **411** |
| `DELETE {prefix}/{entity}/{column}?id=` | Set the column to `NULL` (equivalent to `Write(id, null)`) | Success 204 / no row 404 |

- **The key is carried in the URL query `?id=`** (the body is used for the blob itself). A VO key is serialized by the same rule as the JSON envelope (the wrapped value).
- **A 0-byte PUT (empty body) and setting to `NULL` (DELETE) are structurally distinguished** (the former makes `Read` return `true` + empty; the latter `false`).
- **Only binary PUT has its request-size limit lifted by default** (the `IRequestSizeLimitMetadata` metadata is applied; JSON endpoints stay at the default 30 MB). This is to handle GB-scale data with no extra setup, but **because lifting it raises DoS concerns, combining it with authorization (`MapGeneratedRemoteEndpoints().RequireAuthorization()`) is strongly recommended**. To restore the limit or set a different value, override the whole group via the returned `RouteGroupBuilder`.
- The client (`Http{Entity}RemoteRepository`) receives `GET` with `ResponseHeadersRead` and copies to the destination in O(chunks), and sends `PUT` with `StreamContent` (with `Content-Length`). If you do not pass `length` for a non-seekable Stream, it throws `ArgumentException` **before sending** (the same length contract as existing).
- **Making `WithUnboundedBinary()` / `Query()` / raw SQL remote is out of scope** (as before).

A working example is in the repository at [samples/ec-order-remote](../samples/ec-order-remote/README.md) (a sample that runs exactly this recommended layout as three projects across two real processes; it also demonstrates remote transfer of named queries and type restoration of `SaveConflictException`).

## In-memory repositories for tests (GenerateInMemoryRepositories)

You can additionally generate an in-memory implementation for unit testing without a DB. It implements the same contract, and unsupported operations throw `NotSupportedException` with guidance to switch to the real-DB repository. Note that `GenerateInMemoryRepositories` cannot be combined with `UseRuntimePackages` (a diagnostic error), because the in-memory engine is emitted as fixed infra on the generation side and does not exist in the packages.

## Runtime package reference mode (--use-runtime-packages)

By default, the generated code is self-contained inline output that includes the runtime (the schema-independent fixed code). Specifying `--use-runtime-packages` omits the fixed code and relies instead on references to the following NuGet packages (the required PackageReference is described in the generation header and the CLI output; add it to the csproj by hand):

| Package | Contents | Dependencies |
|---|---|---|
| `QuickER.Runtime` | Shared foundation and dialect-neutral contracts | None |
| `QuickER.Runtime.SqlServer` | QuickER's SQL Server dialect engine | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | QuickER's SQLite dialect engine | Microsoft.Data.Sqlite |
| `QuickER.Runtime.EntityFrameworkCore` | EF Core shared parts | Microsoft.EntityFrameworkCore.Relational |

The package version and the tool version are published in lockstep (the same version), so use the same version for both. While the project is on 0.x, compatibility between minor versions is not promised (see the versioning policy in [CONTRIBUTING](../CONTRIBUTING.md)). Schema-dependent items such as the DI-registration extensions, `QuickErDbContext`, and per-entity implementations are always emitted on the generation side even in this mode.

## API reference (.g.md)

You can additionally output an API-reference Markdown that shares the base name of the generated code. Enable it with the "Output an API reference (.g.md)" checkbox in the GUI's generation dialog, or the CLI's `--generate-api-docs` flag (**default OFF**). It can always be chosen independently of the DB-access selection (None / QuickER Repository / EF Core Repository).

When enabled, one `.g.md` with the same base name as the `.g.cs` is output (e.g. `EcOrder.g.cs` → `EcOrder.g.md`). In per-category split mode it becomes the fixed name `ApiDocs.g.md` (the Japanese version is `ApiDocs.ja.g.md`), in the same style as the fixed names such as `Entities.g.cs`. The contents are as follows.

- A list of entities and, for each entity, a property table (including the DB type token, such as `string(50)` / `decimal(10,2)`).
- The repository contracts (`IRepository<TEntity, TKey>` and the per-entity interfaces) — included only in a configuration that generates the Repository contracts.
- Usage examples of DI registration, CRUD, and queries — likewise included only in a configuration that generates the Repository contracts (these sections are omitted when DB access is "None").
- A generated-file layout table.

**English is the canonical version.** If you also want a Japanese version, enable the GUI's sub-checkbox "Also output a Japanese version", or the CLI's `--api-docs-ja` flag (config key `IncludeJapaneseApiDocs`) (**default OFF**; requires `--generate-api-docs`). When enabled, a `.ja.g.md` is produced alongside the canonical English `.g.md` (e.g. `EcOrder.g.cs` → `EcOrder.ja.g.md`).

`.g.md` / `.ja.g.md` are auto-generated files. They are overwritten on regeneration, so do not edit them directly.

## Coexisting with an existing codebase

A running system already has entity and data-access assets, hand-written or scaffolded. After you get a diagram via DB import, there are stages of pairing those assets with the generated code — and **stopping at any stage is a valid setup**.

- **Coexistence without generation** — use the diagram only for review, definition-document output, and diff sync, and never touch the code. Your existing data layer stays as it is. The value of a single source of truth for the schema (definition documents you can regenerate from the diagram whenever it changes, diff detection against the DB) is already available even at this stage
- **Coexistence with basic generation only** — generate just Entity / EditModel / Mapper with DB access "None" and use them around your screens. Data access remains your existing asset; the generated code takes no part in reads or writes
- **Gradual adoption starting from new features** — use the generated QuickER Repository (or the EF Core Repository) only for newly built features, and migrate existing code when you touch it. The generated code is plain ADO / EF Core access to the same schema, so it can share the database with your existing data layer; designing the transaction boundaries and connection management across the two remains your responsibility. If your system is EF Core code-first, the generated `QuickErDbContext` connects to the existing schema only (it takes no part in migrations), so it can live alongside your existing DbContext — the common pattern of multiple contexts over one database

Two practical notes for coexistence:

- **Separate by namespace** — keep `RootNamespace` (and, if needed, the output project) apart from your existing code, and same-named classes can coexist (where you use both, distinguish them with a namespace qualification or a `using` alias)
- **The way to bring existing assets into a diagram is DB import** — the GUI's "Import Code" (C# reverse) only accepts a `.g.cs` that QuickER generated with `IncludeDataAnnotations` ON; hand-written POCOs are not eligible. Bring the structure of existing assets in from the live database, not from the code (see [Database round-tripping](database.md))

## License note

The code-generation engine (`QuickER.CodeGen.CSharp` / `CodeGen.UI` / `Cli`) is covered by [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) **plus additional grants**; thanks to those grants, **the current releases are free for everyone, including commercial use**. For the licensing and distribution policy (the permanent free grants — including the basic generation of Entity / EditModel / Mapper — and possible future paid licensing), see the [licensing guide](../LICENSING.md). **The generated code and the runtime packages (MIT) belong to you as part of your deliverable**: [LICENSE-NC.md](../LICENSE-NC.md) grants everyone a perpetual, irrevocable license to use, modify, distribute, and sell generated output for any purpose, with no attribution required.
