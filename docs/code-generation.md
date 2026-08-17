# Using the Generated Code

*English | [日本語](code-generation.ja.md)*

This document describes the structure of the C# code QuickER generates and how to use its data-access layer (QuickER Repository / EF Core Repository). For how to run generation, see the [CLI reference](cli.md); for a working example, see [samples/ec-order](../samples/ec-order).

## What gets generated

| Category | Contents |
|---|---|
| Entity | A POCO that corresponds to a table. UI-framework independent (no dependency on CommunityToolkit or the like). Carries `RowState` (Unchanged / Added / Updated / Removed), state-transition methods such as `MarkAdded()`, and navigation properties (parent reference / child collection). |
| EditModel | A model for screen editing, plus conversion to and from the Entity. Every column keeps two representations: the committed value and the on-screen input string (`BindingXxx`). |
| Mapper | A converter for Entity ⇄ EditModel. **Loading is lossless**: the committed values are copied straight from the entity instead of being rebuilt by parsing the input strings, and the `BindingXxx` strings are then derived from them purely for display. Only the fields the user actually edits take on the precision of their input string, so simply loading a row never drops what the display format cannot express (the sub-second part of a `DateTime`, its `DateTimeKind`, and so on). Binary columns are copied defensively, so editing the loaded model never writes into the entity it was loaded from. A `date` column (as opposed to `datetime`) is displayed with the culture's short date pattern, without a "0:00:00" tail. |
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

The registration also wires up `ISqlExecutor`, the entity-independent raw SQL executor, and hands it to every repository it registers. Registering your own implementation after the generated extension (to add logging, metrics, or retries around raw SQL) therefore makes the repositories' raw SQL methods go through it as well. A repository constructed by hand takes the executor as an optional third argument and builds the default one when it is omitted, so existing `new` calls are unaffected.

### Connections and schema bootstrapping

The generated `SqlConnectionFactory` is what opens every connection, and on **SQLite it enables foreign key enforcement by default**. SQLite leaves enforcement off unless a connection asks for it, so without this the foreign keys in the generated DDL would be silently inert: a child row could reference a parent that does not exist, and deleting a parent would leave its children behind. Since the schema declares the constraints, enforcing them is the correct default. An explicit `Foreign Keys` keyword in the connection string is honored exactly as written, so `Foreign Keys=False` restores the provider's own behavior.

For creating a schema from the DDL QuickER generates, `SqliteSchemaBootstrap.ApplyDdlAsync` / `SqlServerSchemaBootstrap.ApplyDdlAsync` open a connection and run the whole script in one call.

```csharp
var ddl = await File.ReadAllTextAsync("Shop.sql");
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl);

// A large script on a slow machine may need longer than the provider's default command timeout
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl, TimeSpan.FromMinutes(5));
```

This is a bootstrap convenience for development, tests, and samples — not schema management. It knows nothing about versions, about what already exists, or about rolling back, so anything that outlives a throwaway database wants a migration tool instead (see also: EF Core mode is for connecting to an existing schema, and Migrations are out of scope).

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

`BulkInsertAsync` keeps the same contract on every backend: a `null` element is **skipped** (as it is in a graph save's list), the return value counts only the rows actually inserted, an empty collection returns 0 without opening a connection, and a cancellation already requested on entry throws before anything is written.

On SQL Server the bulk insert runs through `SqlBulkCopy` with `CheckConstraints` **always on**: foreign key and CHECK constraints are honored, so a row the row-at-a-time `InsertAsync` would reject fails the copy too. (`SqlBulkCopy` skips those checks unless asked, which would otherwise let a bulk insert write rows the rest of the API refuses.) Triggers are deliberately **not** fired — QuickER's DDL generates none.

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

**Navigation properties cannot appear in a predicate or an ordering key** either — `Where(o => o.Customer == null)` throws `NotSupportedException`. A navigation has no column of its own, so filter on the foreign-key column instead (`Where(o => o.CustomerId == null)`). The in-memory and EF Core backends do translate such a predicate, so this is a limitation of the QuickER Repository rather than a shared one.

Nulls in an equality comparison are compensated on both sides so that every backend agrees with C# and EF Core:

- **Value side.** An `==` / `!=` whose value turns out to be null becomes `IS NULL` / `IS NOT NULL`, whether the null is written as a literal or comes from a variable. (Binding it as an ordinary parameter would leave `col = @p`, which SQL's three-valued logic makes false for every row.)
- **Column side.** A `!=` against a non-null value becomes `(col <> @p OR col IS NULL)`, so rows whose column is `NULL` are included — C# and EF Core both treat `NULL` as different from a non-null value, while the bare `col <> @p` is UNKNOWN for those rows and silently drops them. It is applied without asking whether the column allows `NULL`, since that cannot be decided reliably from the expression tree; on a column that is never `NULL` the added disjunct simply never holds.
- **Two columns.** Both operators are spelled out, since a `NULL` is possible on either side: `!=` becomes `(a <> b OR (a IS NULL AND b IS NOT NULL) OR (a IS NOT NULL AND b IS NULL))` and `==` becomes `(a = b OR (a IS NULL AND b IS NULL))`. Both match C# (and EF Core) in counting one `NULL` side as different and two `NULL` sides as the same, which the bare `<>` and `=` get wrong in opposite directions. A column compared against a value needs nothing on the `==` side, where SQL and C# already agree that a `NULL` column does not match a non-null value.
- **Negation.** `!(a == b)` and `!(a != b)` flip the operator instead of being wrapped in `NOT (...)`, so they get exactly the compensation the opposite operator gets on its own. A negated negation cancels out (`!(!(a != b))` is translated as `a != b`, with its compensation intact), and any deeper stack of negations collapses the same way. Negating anything else still produces `NOT (...)`.
- **Known limitation — negating a composite condition.** The flip only applies when the `!` sits directly on a comparison. `!(a == b && c)` is *not* rewritten by De Morgan's laws: it becomes `NOT (...)` around individually compensated operands, and `NOT (UNKNOWN)` is still UNKNOWN, so a row that a `NULL` made UNKNOWN inside the parentheses is dropped where C# and the in-memory/EF Core backends would have kept it. Where a `NULL` is possible, write the negation on the comparison itself — `a != b || !c` — which takes the flip and agrees with them.

The compensation covers equality only: the relational operators (`<` `<=` `>` `>=`) still bind the null as a parameter, because they have no null-aware SQL counterpart.

String matching in the mini DSL (`LIKE` / `CONTAINS` / `STARTSWITH` / `ENDSWITH`) is emitted against a nullable column with an explicit "the column is not `NULL`" conjunct (`NOT LIKE` sits inside the same premise, so a `NULL` row matches in neither direction). SQL's `LIKE` already drops `NULL` rows as UNKNOWN, so nothing changes on the SQL side; the in-memory backend, however, compiles the expression tree and actually evaluates it, where the missing premise would raise a `NullReferenceException` on a `NULL` row.

Date parts (`Year`, `Month`, …) are translated only when the member is read from a `DateTime`, `DateOnly`, or `DateTimeOffset` column (nullable forms included). A property of the same name on any other type — one added to a value object through a partial declaration, for instance — is not a date part and fails with `NotSupportedException` instead of silently becoming `YEAR([col])`.

`Contains` on a list expands to one bind variable per element and is not chunked, so a very large list runs into the dialect's bind-variable / IN-list limit (Oracle's 1000, SQL Server's 2100 parameters, SQLite's historical 999, etc.) and fails at runtime. For a large set of keys, stage them in a temporary table and join, or use raw SQL.

### Graph save (save parent and children in one call)

```csharp
var order = new OrderEntity { OrderId = 1000, CustomerId = 1 };
order.OrderLines.Add(new OrderLineEntity { OrderLineId = 5000, OrderId = 1000, ProductId = 100, Quantity = 2 });

order.MarkAdded(includeChildren: true);         // Marks the whole aggregate: the same cascade the save follows

var affected = await orders.SaveAsync(order);   // Runs INSERT / UPDATE / DELETE per RowState in one transaction
```

`MarkAdded(includeChildren: true)` walks the cascade navigations a graph save follows — all the way down — so a freshly built aggregate is marked in one call instead of one call per node (build the graph first, then mark it). Only `MarkAdded` offers the cascading form: marking a whole graph for update would rewrite every row including the untouched ones, and marking a whole graph for removal is what the graph save's `cascadeDelete` already does from the root alone.

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

// Or let the registration derive the entity types from the instance. Registers the hook under every
// ISaveHook<TEntity> it implements, so one object covering several tables needs no per-type line
services.AddSaveHook(new AuditSaveHook());
```

**Without a DI container**, build a `SaveHookRegistry` and hand it to the repository constructor — hooks fire in the order they were added, exactly as with the DI-backed registry:

```csharp
var hooks = new SaveHookRegistry()
    .Add<DocumentEntity>(new DocumentSaveHook())
    .Add<OrderEntity>(new OrderSaveHook());

var documents = new DocumentRepository(connectionFactory, hooks);
```

Building the registry is not thread-safe (add every hook before handing it to a repository); resolution afterwards is read-only.

You can register multiple hooks for the same entity type. **Before runs in registration order** and **short-circuits the moment one returns `false`** (the remaining Before hooks are not called, and that row is skipped). **After also runs in registration order.** An exception thrown by Before / After propagates as-is and rolls back the entire save (a target with a real transaction rolls the transaction back; in-memory never published the writes in the first place - see below).

**Only `SaveAsync` (both the single and the multiple form) is targeted.** Direct calls to the low-level APIs `InsertAsync` / `UpdateAsync` / `DeleteAsync`, and `BulkInsertAsync`, **bypass** the hooks (they do not fire).

#### Before and skip semantics

`false` skips **only that entity's single operation** (other rows proceed). A skipped row does not get an After call, and its `RowState` is left unchanged (it is excluded from `AcceptChanges`).

Because a skip is isolated, consistency is the hook author's responsibility. In particular, **deletes run children-first**, so **if you return `false` for only the root (parent) in a subtree delete, the children are deleted and only the root remains**. To stop the parent, the children's hooks must also return `false`. An inconsistent skip (for example, skipping a new parent while saving a new child) falls to the safe side via an FK constraint violation → exception → **full rollback**, provided the FK constraint is actually defined in the DB.

#### After and the context

After receives, **just after the operation and before commit**, an `ISaveHookContext` that joins the in-flight transaction. Calling the repository's ordinary APIs from within the hook would contend for locks on a separate connection, so use the operations exposed through `context`. Because throwing from After rolls back the whole save, the half-finished state of "the row exists but the file is not registered" is structurally impossible (in-memory gives the same guarantee by staging its writes and publishing them only once every phase has succeeded).

Operations the context provides (it does not expose raw handles):

- `WriteBinaryColumnAsync(propertyName, key, stream, length?)` / the file convenience method `WriteBinaryColumnFromFileAsync(propertyName, key, path)` — stream-write to an excluded column (when `ExcludeUnboundedBinaryColumns` is enabled) (specify the column with `nameof`).
- `ExecuteSqlAsync(sql, parameters)` — arbitrary DML (an audit row, a write to a related table, etc.).

`operation` receives **the operation that actually happened**. If `insertWhenUpdateMissing: true` and no update target is found so it switches to INSERT, Before is called once with `Update` and After is called with the actual `Insert`.

#### Differences by implementation target

| Target | Hook firing | Context support |
|---|---|---|
| QuickER Repository (SQL Server / SQLite) | Full support (After fires right after each operation) | Both `WriteBinaryColumnAsync` and `ExecuteSqlAsync` are supported |
| EF Core Repository (`GenerateEfCore`) | Supported (After fires in a batch after `SaveChanges`) | `ExecuteSqlAsync` supported; `WriteBinaryColumnAsync` throws `NotSupportedException` |
| In-memory (`GenerateInMemoryRepositories`) | Supported (pseudo transaction) | `WriteBinaryColumnAsync` writes to the store; `ExecuteSqlAsync` throws `NotSupportedException`. There is no real transaction, but the save unit is all-or-nothing through copy-on-write: every write is staged and published as one unit only after the last phase succeeds, so **nothing a failed save wrote (including a blob written by After) is ever visible**, and a concurrent writer's changes cannot be trampled by the failure |
| Remote (`--generate-remote-services`) | **A hook registered in the server-side DI fires** | Follows the server-side real implementation. A row the server skipped in Before travels back in the save response, so the client-side `RowState` is left untouched as well (the row stays pending and is retried on the next save), exactly as on a direct connection |

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

A parameter whose value is a collection (anything enumerable except `string` and `byte[]`) is expanded for `IN`: write it inside parentheses as `IN (@ids)` and each element is bound as `@ids0, @ids1, ...`, with the `@ids` in the SQL rewritten to match.

```csharp
var rows = await customers.QueryBySqlAsync(
    "SELECT * FROM customers WHERE customer_id IN (@ids)", new { ids = new[] { 1, 2, 3 } });
```

Two things to know about the expansion:

- **An empty collection expands to `(NULL)`.** For `IN` that is right — nothing matches. For `NOT IN (@ids)` it is a trap: `x NOT IN (NULL)` is UNKNOWN for every row, so **no** row matches, which is the opposite of what an empty exclusion list should mean. Branch to a different statement when the collection can be empty.
- **The rewrite is textual.** It replaces `@name` anywhere in the command text, including inside a string literal or a comment, so do not write the parameter's own name in those places. Names that merely start the same (`@idsSuffix`, `@ids0`) and system variables that merely end the same (`@@ids`) are left alone.

### Uniqueness pre-check (CheckUniquenessAsync)

A table's UNIQUE constraints are stamped on its generated **Entity** class as `[UniqueConstraint("PropA", "PropB", Name = "UQ_...")]`, next to `[DbTableMeta]` / `[DbColumnMeta]`. Like those, it is definition metadata that makes the entity a self-describing document of the DB definition; it drives no runtime behaviour (the checks below are plain generated code). The attribute type itself is emitted only when at least one table has a constraint to declare. C# reverse reads the attribute back, so the constraints round-trip (see [Import and export](import-export.md)).

Every generated repository contract carries a bulk check built from the diagram's UNIQUE constraints (it is always generated, whether or not the table has any constraint):

```csharp
Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
    TEntity entity, CancellationToken cancellationToken = default);
```

For each UNIQUE constraint of the table it asks "does a row with the same value tuple already exist, **excluding rows that share this entity's primary key**?". Because the same-key row is excluded, the same call is correct both before an insert and before an update. When the primary key can be null (a value object or `string` key) and has not been assigned yet — the normal state of a new row — the exclusion is left out entirely, so the check really does search every row. Constraint member values that contain a `null` are skipped, since NULL collision semantics differ per dialect.

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

Validating the parent covers the collection: `parent.Validate(includeChildren: true)` delegates each registered child collection to `EditModelCollection<T>.Validate()`, so the duplicate check among the siblings runs from the root call as well, and `parent.CollectErrors()` returns the duplicate-value errors along with the rest, each under its `Orders[i]` path. A child collection that a mapper load replaces wholesale is picked up too — the cascade registry resolves the collection through an accessor at every use rather than capturing the instance it was registered with.

Duplicate-value errors live in a store of their own, separate from input errors (required, conversion, value object, `OnValidate`). Registering or clearing one kind never touches the other, so a property can carry a conversion error and a duplicate-value error at the same time, and `GetErrors` returns both. In particular, resolving a duplication and re-validating does not silently drop the "cannot be converted" error left on the same field — that error only ever comes back through the binding setter, so clearing it would leave an invalid input on screen with `Validate` reporting success. `HasErrors` covers both stores.

Every error is owned by the check that registered it, and a check only ever adds or removes its own:

- **The binding setter** owns conversion and value-object errors (`SetError`). It is the only thing that can produce them again, so nothing else clears them.
- **The required-field check** (generated `ValidateSelf`) adds a missing-input error only when the property carries no other input error — a field that holds unconvertible text is not silently relabelled "is required" — and clears its own error as soon as the value is present, even when the value was assigned straight to the committed property rather than typed.
- **The two uniqueness checks** each have their own slot on a property, addressed by `DuplicateErrorSource` (`Siblings` for the check among the elements of a collection, `Database` for the check against the stored rows). Neither overwrites nor clears the other's slot, so a value that is a duplicate both among its siblings and in the database reports both findings, each disappearing when its own check stops reporting it. Validating the graph before a save therefore no longer discards what the database check has just reported, and vice versa.
- **Changing a committed value withdraws the `Database` findings** of that edit model, because the check compared the values it held before the edit — all of them, since a composite constraint's verdict rests on every column it covers. The `Siblings` findings are left for the next validation to decide.
- **`OnValidate`** owns whatever it registers: clear a custom error from the hook (`SetError` with a null message) once its condition no longer holds.

`RevertInput()` rebuilds the input strings and clears the input errors only. A mapper load goes further and clears the duplicate-value errors of both checks as well, because the values they judged are gone.

#### Edit models: checking against the database

When edit models and a repository contract are both generated, each edit model also gets a convenience wrapper:

```csharp
// The repository parameter is I{Entity}RemoteRepository when remote contracts are generated, I{Entity}Repository otherwise
if (!await editModel.ValidateUniqueAsync(repository))
{
    // Errors are already registered on the binding properties (INotifyDataErrorInfo shows them in the UI)
}
```

It builds an entity from the edit model's confirmed values, calls `CheckUniquenessAsync`, and maps each violation's `PropertyNames` back to the binding properties. A violation with no property names (or with names that do not belong to the edit model) becomes a model-level error registered under the empty property name, which `GetErrors(null)` returns. The duplicate-value errors registered by the previous call are cleared first, so re-checking never leaves stale errors — its own findings only, so what the check among the siblings reported stays.

The errors are registered after the `await`, which puts them on a thread pool thread rather than the caller's, and `ErrorsChanged` fires on that same thread. A WPF binding marshals the notification back to the UI thread by itself, so the ordinary case needs nothing from you; a subscriber that updates UI state directly has to marshal it at the call site.

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
- **They double as the table's concurrency token**: saves compare the version the entity was read with against the current row (see the next section).
- **The write exclusion is SQL Server's alone.** Only SQL Server assigns the value, so only its engine excludes the column. In a multi-target build (`--repository-dialects sqlserver,sqlite`) the SQLite engine writes the same column with INSERT / BulkInsert / UPDATE like any other binary column — it is the place a local copy mirrors the version the server assigned. See [Multi-target repositories](#multi-target-repositories-sqlserver--sqlite).

### Optimistic concurrency (rowversion)

A table that carries a rowversion column is saved with optimistic concurrency, and there is nothing to turn on: the version the entity was read with is compared against the current row, and a save that lost the race is rejected instead of silently overwriting someone else's change. A table without such a column keeps exactly the behaviour it had.

`UpdateAsync` and `SaveAsync` take an optional `ConcurrencyMode`:

| Mode | Behaviour |
| --- | --- |
| `Optimistic` (default) | The write is guarded by the version. A row someone else changed first is rejected with `SaveConflictException`. |
| `ForceOverwrite` | The version guard is dropped (an explicit last-write-wins). |

```csharp
// Guarded by the version the entity was read with; losing the race throws SaveConflictException
await repository.UpdateAsync(order, cancellationToken: ct);

// Explicit last-write-wins
await repository.UpdateAsync(order, ConcurrencyMode.ForceOverwrite, ct);

// A graph save guards the updates and deletes inside the graph the same way
await repository.SaveAsync(order, cancellationToken: ct);
```

`SaveConflictException` carries the material a retry needs, so the message never has to be parsed: `Reason` (`NotFound` — the row is gone — or `Modified` — the row is there but its version moved on), `EntityTypeName`, and `Key`. The same details survive the remote transport (HTTP 409), so a caller reads the same properties against a direct and a remote repository.

The usual answer to a conflict is to reload and reapply:

```csharp
try
{
    await repository.UpdateAsync(order, cancellationToken: ct);
}
catch (SaveConflictException ex) when (ex.Reason == SaveConflictReason.Modified)
{
    var current = await repository.GetByIdAsync(order.OrderId, ct);   // Read the version that won
    current!.Memo = order.Memo;                                       // Reapply this user's edit on top of it
    await repository.UpdateAsync(current, cancellationToken: ct);     // Now guarded by the fresh version
}
```

Reload-and-reapply is the honest answer when the two edits can be merged. `ForceOverwrite` is for when they cannot and this write is meant to win regardless — it skips the guard on the first attempt, so nothing is read back and nothing is merged.

- **A missing row and a stale version are different outcomes.** A single `UpdateAsync` returns `false` when the row no longer exists (the pre-existing contract) and throws `SaveConflictException` when the row is still there but its version moved on. `insertWhenUpdateMissing: true` draws the same line: a missing row switches to an INSERT, while a stale version is reported as a conflict (switching that to an INSERT would turn the conflict into a primary-key violation).
- **A graph save guards deletes as well**, and a single conflict rolls the whole save unit back (the in-memory repository reaches the same result differently: it stages its writes and publishes them in one go, so a failed save simply never reaches the store).
- **The in-memory repository verifies again when it publishes.** Save hooks run outside the store lock, so a row the save started from may have been written by someone else in the meantime; publishing rejects that with a `SaveConflictException` and leaves the other writer's row untouched. A type **without** a rowversion column is not verified at that point - without a concurrency token the store's contract stays last-write-wins - and `ForceOverwrite` waives the verification for the same reason. A row the save inserts is always verified, because a primary key taken meanwhile is a duplicate key rather than a concurrency decision. Whether the row is still *there* is settled before its version is, and independently of both the mode and whether the type carries a version column at all: a row deleted meanwhile is reported as `SaveConflictReason.NotFound` (a version comparison against a row that no longer exists cannot say anything about it), and a staged delete for such a row is not a conflict.
- **The new version is written back.** After a successful insert, update, or graph save, the entity holds the version the database assigned, so the same instance can be saved again without being re-read. A save hook's `AfterSaveAsync` runs before the commit and therefore still sees the old version.
- **Every backend follows the same contract.** The QuickER Repository guards the statement with `WHERE ... AND <rowversion> = @original` and reads the new version back through an `OUTPUT` clause; EF Core uses its own concurrency token (`IsRowVersion()`) and converts `DbUpdateConcurrencyException` into the same exception; the in-memory repository emulates the database with a monotonically increasing 8-byte token; the HTTP remote client carries the mode in the request and writes back the versions the response returns.

Known limitations:

- Only SQL Server has a `rowversion` type, so the QuickER Repository applies this to the `sqlserver` dialect only. A diagram targeting SQLite (or another dialect) alone has no such column and is unaffected; a **multi-target** build that includes `sqlserver` does share the column with the other dialects, but only the SQL Server engine guards with it (see [Multi-target repositories](#multi-target-repositories-sqlserver--sqlite)).
- `BulkInsertAsync` uses `SqlBulkCopy`, which cannot return generated values, so the entities keep whatever version they had. Re-read them when a later update needs the version.
- The version is read back through an `OUTPUT` clause, which SQL Server rejects on a table that has a trigger. QuickER's DDL generation never emits triggers, so this only affects tables whose triggers were added outside QuickER.
- Deleting a row that is already gone stays asymmetric between the backends, as it always has: the QuickER Repository tolerates it silently, while a graph save on EF Core reports it as `SaveConflictException`.
- A rowversion column carries no `[DbColumnMeta]` token, so C# reverse engineering does not restore it (declare it in the diagram).
- **Deleting by key is not guarded.** `DeleteAsync(id)` takes a key rather than an entity, so there is no version to compare it against and the row goes whatever its current version is. Where a delete has to lose the race it lost, mark the entity `MarkRemoved()` and save the graph — a graph save guards its deletes with the version the entity was read with.
- Raw SQL (`ExecuteSqlAsync` and friends) and the stream accessors for unbounded binary columns are direct operations and are not guarded.

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
- **Bidirectional sync leaves them out too**, and copies them separately when asked to: see [Unbounded binary columns](#unbounded-binary-columns) under the sync support.

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

> **Note**: Copying data between databases needs care. `GetAllAsync` followed by `BulkInsertAsync` writes the excluded columns exactly as the fetch left them — `null`, or an empty array for a non-nullable column — and since INSERT keeps every column, nothing throws and the blobs are quietly gone at the destination. (The UPDATE guard does not cover this: it fires on an excluded column that still holds a value, which is the opposite of what a copy carries.) Read with `Query().WithUnboundedBinary()` instead, or copy the rows first and then move each blob separately with the `Read/Write{Column}Async` stream accessors — the latter is also the only way that does not hold a whole blob in memory.

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
- The effect applies to the entity-shaped fetches `ToListAsync` / `FirstOrDefaultAsync`, and to `ToProjectionListAsync` **when the projection falls back to materializing the entity in full** (a selector whose columns cannot be extracted, or one combined with `Include`). It does not affect count, existence check, or a projection whose columns are pruned server-side — that projection already fetches exactly the columns it references, excluded ones included.
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
- Optimistic concurrency is at parity: `ConcurrencyMode` selects the policy, EF Core's `DbUpdateConcurrencyException` is converted to `SaveConflictException`, and the refreshed concurrency token is left on the entity (see [Optimistic concurrency (rowversion)](#optimistic-concurrency-rowversion)).
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

### Row version columns in a multi-target build

A `rowversion` column resolves to different C# types per dialect (`byte[]` on SQL Server, a date/time or an unknown type on SQLite), and the shared Entity can only have one. QuickER unifies it to the row-version resolution — `byte[]` with `[StoreGeneratedColumn]` — and reports the columns it unified through an Info diagnostic instead of failing with a type-mismatch error. The two sides then mean different things, and that difference is the point:

| Side | What the column is | Writes | Version guard |
|---|---|---|---|
| SQL Server (server) | A concurrency token the database assigns | Excluded from INSERT / BulkInsert / UPDATE; the assigned version is read back onto the entity | Yes — a stale version raises `SaveConflictException` |
| SQLite (local copy) | An ordinary binary column | Written by INSERT / BulkInsert / UPDATE like any other column | **No** — the write goes through whatever the entity holds |

That makes the column the natural place for a local copy to mirror the server's version: read a row from the server (version included), store it locally as-is, and later send the mirrored version back as the guard value of the server-side update. A row created locally simply has no version yet, which is why the dialect switch also lifts NOT NULL on that column (see [Dialect switching](database.md#dialect-switching)).

Known limitations:

- **The local side is not protected.** SQLite runs no version guard, so two local writers still overwrite each other. The version is data there, not a lock.
- **Nothing keeps the mirror fresh.** The column holds whatever was written last; if a sync is skipped, a later push using that value is rejected by the server as a conflict, which is the intended outcome (reload and reapply).
- **`ForceOverwrite` is a no-op on the local side**, since there is no guard to waive.
- The EF Core Repository cannot be combined with a multi-target build (a diagnostic error), so the mirroring described here applies to the QuickER Repository.

Keeping the mirror in step is a job you can write yourself against these two repositories, or hand to [Bidirectional sync support](#bidirectional-sync-support---generate-sync-support), which generates it.

## Bidirectional sync support (--generate-sync-support)

The multi-target build above gives a local copy a place to mirror the server's version. `--generate-sync-support` (`GenerateSyncSupport` in quicker.json, the "Generate bidirectional sync support" checkbox in the GUI, shown once both target databases are selected) generates the machinery that actually keeps the two in step, with the server as the source of truth.

It requires exactly the two dialects `sqlserver` (the server) and `sqlite` (the local database), the QuickER Repository implementations, and at least one table with a `rowversion` column. **Only tables with that column take part** - the differential scan and the version guard are both built on it - and the tables it picked up are listed in an Info diagnostic at generation time. What that column means on each of the two sides is described under [Row version columns in a multi-target build](#row-version-columns-in-a-multi-target-build); this section is about the machinery built on top of it.

### What it puts where

The server gets **no extra schema at all**. The local database gets one shared table, `quicker_sync_journal`, created on first use with `CREATE TABLE IF NOT EXISTS`; it records offline edits (table, key, operation, and for a delete the version the row carried).

The resume point is **derived, not stored**: it is the highest mirrored version among the local rows, so there is no bookkeeping row that can drift out of step with the data. Two properties make that derivation correct, and both are load-bearing: server changes are fetched in ascending version order, and each batch is applied in a single local transaction. A run interrupted anywhere leaves the local database holding a prefix of the ordered stream, and the maximum of that prefix is exactly where the next run resumes. Rows created locally and not yet uploaded have no mirrored version, so they drop out of the maximum on their own.

The upper bound of a pass is `MIN_ACTIVE_ROWVERSION()`, taken once per run: a row committed later can carry a lower version than one committed earlier, so reading up to "the current maximum" would step over rows that are still uncommitted and never come back for them.

### Wiring it up

```csharp
services.AddGeneratedSqlServerRepositories(serviceKey: "server", sqlServerConn);
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);
services.AddGeneratedSyncSupport(serverServiceKey: "server", localServiceKey: "local");

// The local repositories resolved by key are now wrapped so every write is recorded
var local = provider.GetRequiredKeyedService<ICustomerRepository>("local");

var result = await provider.GetRequiredService<SyncEngine>().SyncAsync(cancellationToken: ct);

if (result.HasConflicts)
{
    foreach (var conflict in result.Conflicts)
    {
        // conflict carries the table, the key, the operation, the reason, and both sides' rows
    }
}
```

The registration comes in two halves, and which server half you combine with the local one is the only thing that decides how the server is reached.

| Call | What it registers |
|---|---|
| `AddGeneratedSyncEngine(localServiceKey)` | The local half: the journal, the per-table descriptors, the engine, and the journaling decorators that wrap the local repositories |
| `AddGeneratedDirectSyncSources(serverServiceKey)` | The server half over a database connection this process holds |
| `AddGeneratedHttpSyncSources(baseAddress)` / `(httpClientFactory)` | The server half over HTTP (see below) |
| `AddGeneratedSyncSupport(serverServiceKey, localServiceKey)` | The two halves above for the all-direct setup - the common case when both databases are reachable from the same process |

Every key argument accepts `null`, which means the ordinary non-keyed registration rather than a key whose value is null: a keyed registration cannot be made with a null key, so the two never collide. Registering the local half without a server half is not a silent failure - resolving the engine then fails on the missing source.

A run uploads first and downloads second, and visits tables in foreign-key order - parents first when rows are written, children first when rows are deleted. `SyncOptions` is what you turn:

| Option | Default | Meaning |
|---|---|---|
| `DownloadBatchSize` | 500 | How many rows one download batch fetches and applies in a single local transaction |
| `PropagateDeletes` | `true` | Whether to delete local rows whose key no longer exists on the server. The check compares the full key set, which costs one key-only pass over each server table; turn it off and run it on a slower schedule when the tables are large |
| `ConflictPolicy` | `Collect` | How a local change that collides with the server is treated (see [Conflicts](#conflicts)) |
| `IncludeUnboundedBinary` | `false` | Whether to carry the unbounded binary columns the row transfer leaves out (see [Unbounded binary columns](#unbounded-binary-columns)). Nothing changes for a build without such columns |

`SyncResult` reports what the run did:

| Member | Meaning |
|---|---|
| `Uploaded` | Local changes that reached the server |
| `Downloaded` | Server rows applied locally |
| `DeletedLocally` | Local rows removed because the server no longer has that key |
| `Discarded` | Changes settled without being sent - a stale intent whose row is no longer there, or everything the journal held under `ServerWins` |
| `Conflicts` / `HasConflicts` | The local changes that could not be replayed; they stay in the journal |

Replaying one change has **three** outcomes, not two, and `Uploaded` / `Discarded` / `Conflicts` are exactly those three: sent, nothing to send, refused. Folding the middle one into either of the others would report a change as delivered when nothing crossed the wire. `Uploaded` and `Discarded` count rows rather than journal entries - a row edited several times offline collapses into its latest intent - except under `ServerWins`, where nothing is sent at all and `Discarded` is the number of entries dropped.

### Reaching the server over HTTP

Combining `--generate-sync-support` with [`--generate-remote-services`](#remote-services---generate-remote-services--three-tier-layout) adds a client that reaches the server over HTTP instead of a database connection. Swapping one for the other is a change of one registration line and of nothing else, because the engine resolves the same `ISyncServerSource<TEntity, TKey>` either way.

```csharp
// Client: local repositories as before, but the server half now speaks HTTP
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);
services.AddGeneratedHttpSyncSources("https://example.com/quicker");   // or an HttpClient factory
services.AddGeneratedSyncEngine(localServiceKey: "local");

// Server: the ordinary repositories, the sources the endpoints answer from, and the endpoint group
services.AddGeneratedSqlServerRepositories(sqlServerConn);
services.AddGeneratedDirectSyncSources(serverServiceKey: null);
app.MapGeneratedRemoteEndpoints().RequireAuthorization();
```

Three endpoints join the existing group - `SyncCeiling`, `SyncChanges`, and `SyncKeys` under `POST {prefix}/{entity}/…` - and they are a thin remoting of the differential source: each handler resolves `ISyncServerSource<,>` from DI and calls it, so the meaning lives in one implementation that both transports share. Being group members, they are covered by whatever `RequireAuthorization()` the group carries. Uploads add nothing new: they go through the ordinary CRUD and save endpoints, which already carry `ConcurrencyMode` and already turn a version conflict into a 409.

The server keeps **no per-client state**: the resume point travels as the request's anchor and the upper bound as its ceiling. The flip side is that the bound is the caller's value, so the guarantee behind the derived anchor holds only as long as you send back the ceiling `SyncCeiling` returned for that same pass - a hand-made larger ceiling permanently skips the rows of transactions running below it. A batch size of zero or less is refused with 400; there is no upper limit, because what stops a client from asking for everything is the group's authorization.

### How local edits are captured

The generated `Journaling{Entity}Repository` wraps the local repository and records **every write entry point**: `InsertAsync`, `UpdateAsync`, `DeleteAsync`, `BulkInsertAsync`, and both `SaveAsync` overloads. A save hook would not do: it only fires for a graph save, and a direct insert or delete would pass it by.

The record is written **before** the business write. The generated repositories manage their own connection, so a decorator cannot enlist its INSERT in the transaction of the write it wraps; something has to go first, and recording the intent first is the safe order. If the business write then fails, the journal holds an entry for a row that was never written - and the upload discards it, because it re-reads the current local row and finds nothing. The opposite order would lose changes outright.

**Raw SQL is not recorded.** `ExecuteSqlAsync` forwards untouched: the statement's shape is opaque to the decorator, so there is no key to journal. Rows changed that way reach the server only if something else records them.

**Save hooks are unaffected.** The decorator delegates to the repository it wraps, so `ISaveHook<T>` fires exactly as it did before - including for the rows the engine itself applies during a download, since a sync run suppresses journaling and nothing else. A refresh (below) writes through `BulkInsertAsync`, which is outside the save pipeline and fires no hooks, in keeping with the ordinary contract. One consequence is worth knowing: a write a `BeforeSaveAsync` returned `false` for still leaves a journal entry, because the entry is written first. For an insert that entry is discarded (there is no row to read), and for an update the row is uploaded as it stands - unchanged content, which the server accepts and stamps with a new version.

### Unbounded binary columns

Combining `--generate-sync-support` with [`--exclude-unbounded-binary-columns`](#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns) is allowed, and the excluded columns of a synchronised table are named in an Info diagnostic at generation time. They need saying, because **the row a run reads and writes does not contain them**: the differential SELECT lists the remaining columns explicitly, and an UPDATE never touches an excluded column.

By default (`IncludeUnboundedBinary = false`) that has three consequences, and the third is the one that surprises:

- A row **downloaded** from the server arrives without its blob.
- A row **uploaded** to the server is sent without its blob.
- A blob **already stored** on the receiving side survives - an update does not touch the column - **but a row that is new to that side has nothing to keep and arrives with the column empty.** "The blob is preserved" is true of rows that are already there and false of rows that have just arrived; a first sync into an empty local database therefore leaves every blob empty.

Setting `SyncOptions.IncludeUnboundedBinary` copies each such column separately after its row has been transferred, in both directions:

```csharp
var result = await engine.SyncAsync(new SyncOptions { IncludeUnboundedBinary = true }, ct);
```

The copy streams through a temporary file, so neither side holds the blob in memory: the read pushes bytes into a stream and the write pulls them out of one, and the file is what joins the two while also supplying the length the write needs up front. Over HTTP it reuses the existing `GET`/`PUT`/`DELETE {prefix}/{entity}/{column}?id=` endpoints - the ones the [streaming accessors](#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns) already use - so nothing new is mapped.

Two details follow from copying columns separately:

- **A NULL source clears the destination.** The point of carrying these columns is that both sides end up alike, so a row whose server copy has no blob loses the local one rather than keeping a stale copy.
- **After an upload the server's version is read again.** Writing a blob is a write to the row, so the server moves the version on past the one the insert or update handed back. Mirroring the stale value would leave the local anchor below the row's current version, and the very next download would hand the row back as if the server had changed it.

**A blob written on its own is tracked.** `Write{Column}Async` (and the file convenience method) goes through the journaling decorator like every other write and records its intent first, so an offline edit that changes nothing but a blob still reaches the server. The recording does not depend on `IncludeUnboundedBinary` - what to send is decided when sending, not when generating - so a run left at the default uploads the row (without the blob) and settles the entry.

The cost is one round trip per column per changed row, which is why it is off by default: a table of large blobs whose rows change often pays for it on every run.

### Conflicts

Nothing is resolved silently. Under the default policy a local change that collides with the server stays in the journal and comes back in `SyncResult.Conflicts` with the table, the key, the operation, the reason, and both sides' rows attached.

| `SyncConflictPolicy` | What happens |
|---|---|
| `Collect` (default) | The entry stays in the journal and is reported; re-run after deciding |
| `ServerWins` | The journal is dropped and the download overwrites the local row with the server's |
| `LocalWins` | The change is resent with `ConcurrencyMode.ForceOverwrite`, overwriting the server row |

### Rebuilding the local database (RefreshAsync)

`SyncEngine.RefreshAsync` empties every synchronised table and reloads it from the server. It is for **building the local database the first time, recovering one that was lost or corrupted, and starting over when a database has fallen so far behind that catching up row by row is not worth it** - not for the incremental case, which is what `SyncAsync` is for.

```csharp
var refreshed = await engine.RefreshAsync(new SyncRefreshOptions { BatchSize = 2000 }, ct);

// refreshed.Tables holds per-table Deleted / Inserted counts in the order rows were written;
// refreshed.Deleted / .Inserted are the totals, and .Elapsed is the wall time of the run
```

Unsent local changes are refused rather than lost: when the journal is not empty the run throws `SyncPendingChangesException` **before deleting anything**, with a per-table breakdown (`PendingChanges`, `PendingCount`) so the caller can upload them with `SyncAsync` first and refresh afterwards. `SyncRefreshOptions.Force` is the explicit request to drop them instead, and `SyncRefreshResult.DiscardedChanges` reports how many went.

Local blobs are refused on the same terms. When a synchronised table has [unbounded binary columns](#unbounded-binary-columns) - which the row transfer leaves out, so the reload does not bring them back - the run throws `SyncUnboundedBinaryLossException` **before deleting anything**, naming the columns per table. Two flags answer it, and one of them has to be set:

| `SyncRefreshOptions` | Default | Meaning |
|---|---|---|
| `IncludeUnboundedBinary` | `false` | Copy each excluded column back down after its row has been written, making the rebuilt database a complete copy |
| `DiscardLocalUnboundedBinaries` | `false` | Accept the loss - the right answer when the blobs are a local cache that can be rebuilt. It permits the loss; it does not reload anything |

A generated setup without such columns never sees this exception, so nothing changes for it.

`BatchSize` defaults to **2000**, several times the download batch of an ordinary run: each batch is one local transaction, a refresh gains most of its speed there, and an interrupted run is repaired by running it again, so a fine resume granularity is worth less here. The cost of raising it is memory, and over HTTP the size of one response body; a table with large binary columns is the case for lowering it.

What makes a refresh fast is what it leaves out - nothing is compared with the row it replaces, no anchor is derived per batch, no key set is fetched, and no journal is replayed. Measured on a two-table, 20,000-row diagram at the shipped defaults, it runs about **3 to 4.5 times faster** than an ordinary run - the upper end with both databases local, around 3x with a real SQL Server as the server. The ceiling on that ratio is structural: reading the rows out of the server is work both paths do.

It is not a cheaper `SyncAsync`. It transfers **every row of every synchronised table**, so over a slow link and a large table the transfer dominates and an ordinary run - which carries only what changed - is the cheaper one from the second run onwards. Tables the local database keeps for itself (anything without a version column) are not part of it and are left exactly as they were.

The run is **not one transaction**. The generated repositories manage their own connections, so nothing here can enlist in a transaction of its own making; what holds instead is that every point at which it commits is a state a later run can start from. Deletes go children first and reloads parents first, so no foreign key ever points at a row that is not there, and each table's rows arrive in ascending version order, so a table interrupted part way holds exactly the rows below the version it stopped at - which is the resume point the local maximum yields anyway. The state this leaves that a single transaction would not is a **partly rebuilt local database**: between the first delete and the last row of the last table, a reader sees fewer rows than either side holds. Nothing is lost by it, but a refresh is not something to run underneath a live screen.

### Known limitations

- **Writes that do not go through the repository are not tracked.** That includes raw SQL (`ExecuteSqlAsync`), as described above, and anything that reaches the local database by another route. The journal only sees the write entry points the decorator wraps.
- **Unbounded binary columns are not carried unless you ask for them** (`SyncOptions.IncludeUnboundedBinary`), and carrying them costs one round trip per column per changed row. See [Unbounded binary columns](#unbounded-binary-columns).
- **Cannot be combined with the EF Core Repository**, since the sync support requires a multi-target build and that combination is already exclusive.
- **The HTTP transport requires `--generate-remote-services`.** Without it the direct sources are generated and the engine still works, but there is no client or endpoint to reach the server with.
- **The local side has no version guard**, as under [Row version columns in a multi-target build](#row-version-columns-in-a-multi-target-build): two local writers still overwrite each other, and the engine's conflict detection is about the server's version, not theirs.
- The runtime package for this is `QuickER.Runtime.Sync`.

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
// 500 responses hide the server-side error detail unless you say otherwise; passing IsDevelopment()
// gives you the real message while developing and the generic one in production
app.MapGeneratedRemoteEndpoints(exposeErrorDetails: app.Environment.IsDevelopment());
// To add authorization, chain .RequireAuthorization()
app.Run();

// ---- Client app (switch direct ⇔ remote with one DI-registration line) ----
// Direct: services.AddGeneratedSqliteRepositories(connectionString);
// Remote: services.AddGeneratedHttpRemoteRepositories("https://server:5001/quicker");
// The application body injects and uses IOrderRemoteRepository either way (no code change)
```

Points to keep in mind:

- **Serialization** uses the same semantics as the entity's JSON round trip (`ToJson` / `Clone`) (VO as the wrapped value, RowState included, parent-reference navigation does not cycle), and the client and server share `RemoteJson.Options`.
- **There is a liveness endpoint**: `MapGeneratedRemoteEndpoints` also maps `GET {prefix}/health`, which answers 200 with an empty body as soon as the server is listening. It deliberately does not touch the database, so it says only that the process is up and the endpoints are mapped. On the client, `Http{Entity}RemoteRepository.PingAsync` calls it and returns `false` — rather than throwing — for every flavor of "not reachable" (connection refused, DNS or TLS failure, the HttpClient's own timeout, any non-success status), which makes it usable as the condition of a wait-for-startup loop; cancelling the token you pass still throws, so your own timeout stays distinguishable from a server that is down. The endpoint is a member of the group, so authorization applied to the group covers it too. The prefix and the health route are exposed as the constants `RemotePaths.DefaultPrefix` (`"/quicker"`) and `RemotePaths.HealthRoute`, which both sides read so the value is written down once.
- **Named queries can all be called through the remote surface regardless of implementation method** (simple DSL / raw SQL / manual implementation) (the real implementation lives in the server-side repository).
- **Exception types are restored**: the server's `SaveConflictException` is thrown on the client as `SaveConflictException` too via HTTP 409 (the same catch as in the direct case works), and other server exceptions become `RemoteRepositoryException` (preserving the status code; see below for what happens to the message). A **success** response whose body is not the expected JSON becomes a `RemoteRepositoryException` as well: something other than the generated endpoint answered — a proxy's or a portal's 200 page, most often — and that is a transport failure, so it belongs in the same catch as every other remote failure rather than surfacing as a raw `JsonException`. (A 200 carrying `null` from `GetById` stays what it always was: no such row.)
- **A request the server cannot interpret is answered with 400, not 500.** Anything that fails while the request itself is being read — a malformed or empty JSON body, a non-JSON content type, a type mismatch, a value that fails value-object validation, a body that omits a required field (`{}` sent to `Insert` / `Update` / `Save` / `SaveMany`, or a reference-type key omitted from `GetById` / `Delete`), an undefined `ConcurrencyMode` value, or a missing/unrestorable `?id=` key on the binary endpoints — is a fault in what the client sent, so it returns HTTP 400 with a `RemoteError` of type `"BadRequest"` (the client throws `RemoteRepositoryException` with `StatusCode` 400). The message of a 400 only describes the client's own payload, and neither the server-side logging nor the `OnServerError` hook runs (both are reserved for 500). A request rejected by the server infrastructure (`BadHttpRequestException`, for example when the request body size limit is exceeded) keeps the status code it carries, such as 413.
- **After a successful graph save (Save), the local RowState is also committed** (the same behavior as the direct case).
- **Optimistic concurrency travels over the wire.** The `ConcurrencyMode` argument is part of the Update / Save request, and the Insert / Update / Save responses carry the row versions the save assigned, keyed by entity type and primary key, which the client writes back onto the local graph. A remote client therefore ends up holding the same versions a direct connection would, and can keep saving the same entities without re-reading them.
- **A 500 response hides the server-side error detail by default.** The body carries a fixed message (`An unexpected error occurred on the server.`) plus a `CorrelationId`, which the client surfaces as `RemoteRepositoryException.CorrelationId`. The full exception, including the stack trace, always goes to the server side through `ILoggerFactory` (category `QuickER.RemoteServer`; a no-op when the host has no logging provider) **with the same correlation id in the log line**, so a caller who reports the id lets you find the complete record without the internal message — table and column names, connection strings, file paths — ever crossing the trust boundary. Pass `MapGeneratedRemoteEndpoints(exposeErrorDetails: true)` to send the message verbatim instead (`CorrelationId` is then null and the body is exactly what earlier versions sent); the idiomatic form is `exposeErrorDetails: app.Environment.IsDevelopment()`, which is why it is a runtime argument rather than a generation-time option — one set of generated code covers both environments. The switch changes **only** what the client sees on a 500: server-side logging and the `OnServerError` hook always receive the exception itself, and the classified responses are unaffected — a 400 describes the caller's own payload, and the conflict detail on a 409 (`Reason` / `EntityType` / `Key`) is the material a reload-and-retry loop is built on, so both keep their own messages in either mode. The binary transfer endpoints follow the same switch.
- **Authentication and TLS are out of scope, and the endpoints are wide open until you supply them.** Configure the client with an authentication-handler-equipped HttpClient via `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)`, and add ASP.NET Core authorization to the return value (`RouteGroupBuilder`) of `MapGeneratedRemoteEndpoints()` — `MapGeneratedRemoteEndpoints(...).RequireAuthorization()` covers the whole generated group in one line. **Apply the policy to the group rather than picking endpoints**, because `Save` is as powerful as `Delete`: a graph whose nodes carry `RowState.Removed` deletes those rows, so a policy that guards `Delete` and leaves `Save` open guards nothing. The same holds for the entity payloads generally — the wire format accepts every column the entity has, including the primary key, so authorization is the only thing standing between a caller and any row it can name.
- **Both registration overloads have a keyed form, for holding more than one back end at once.** `AddGeneratedHttpRemoteRepositories(serviceKey, baseAddress)` and `AddGeneratedHttpRemoteRepositories(serviceKey, httpClientFactory)` register `I{Entity}RemoteRepository` under a service key, pairing with the keyed form the dialect extensions already have (`AddGeneratedSqliteRepositories(serviceKey, connectionString)`). That is how a hybrid app is wired: the server over HTTP under one key, a local database under another, with each consumer asking for the side it wants.

  ```csharp
  services.AddGeneratedHttpRemoteRepositories("server", "https://server:5001/quicker");
  services.AddGeneratedSqliteRepositories("local", localConnectionString);

  // Constructor parameters:
  //   [FromKeyedServices("server")] IOrderRemoteRepository remote
  //   [FromKeyedServices("local")]  IOrderRepository       local
  ```

  The shared HttpClient the base-address form creates is registered under the same key, so it collides with neither the non-keyed registration nor another key's, and it stays owned by the container just as in the non-keyed form. Keyed and non-keyed registrations are separate name lists — a keyed one answers only `GetRequiredKeyedService` — and registering the same key twice leaves the last registration in effect.
- **The HttpClient returned by the factory overload is owned by the caller.** `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` invokes the factory every time a repository is resolved (once per scope and per entity), and the returned HttpClient is disposed by neither the generated code nor the DI container. Return a shared instance, or one managed by `IHttpClientFactory`; creating a new HttpClient on every call exhausts sockets. (The base-address overload creates a single shared instance that the container owns, so the client is disposed together with the `ServiceProvider`; a repository resolved from an already disposed provider therefore throws `ObjectDisposedException` on use.)
- **The client the base-address overload builds has no timeout of its own and recycles pooled connections every five minutes.** `PooledConnectionLifetime` (a `SocketsHttpHandler`) is what makes a long-lived singleton follow DNS changes rather than pin the address it first resolved. `Timeout` is `Timeout.InfiniteTimeSpan` because `HttpClient.Timeout` covers a whole request including the body — the 100-second default would cut off a large blob transfer part-way through — so **bound each call with the `CancellationToken` you already pass to it**; that token is the timeout. If you would rather have a finite client-wide deadline, use the factory overload and hand in an HttpClient configured your way (that one is yours, and the generated code does not touch its settings).
- **The wire format is not promised to be stable while QuickER is at 0.x, so regenerate the client and the server together** (and deploy them together). A server updated on its own does not report the mismatch as a version error: a request its newer endpoints no longer recognize comes back as an ordinary transport failure — a 404 or a 400 — which is also why the binary endpoints mark the 404 they produce themselves (see below) instead of letting the client read every 404 as "no data".
- **Do not put an HTTP-level retry policy (Polly and the like) on the mutating operations** — `Insert`, `Update`, `Save`, `SaveMany`, `Delete`. They carry no idempotency key, so a request that in fact succeeded and lost its response on the way back would be applied a second time (a duplicate insert, or a second version bump that turns the next save into a spurious conflict). Retrying the read-only operations (`GetById`, `GetAll`, the named queries) and the health endpoint is safe, so scope the policy to those rather than to the whole client.
- The server file requires the ASP.NET Core FrameworkReference (`Microsoft.AspNetCore.App`) (no extra setup is needed if the project's SDK is `Microsoft.NET.Sdk.Web`). Its fixed engine is shared code, so under `--use-runtime-packages` it comes from `QuickER.Runtime.AspNetCore` and only the project hosting the server file references that package (see [Runtime package reference mode](#runtime-package-reference-mode---use-runtime-packages)).
- **The generated server class is extensible.** `GeneratedRemoteEndpoints` is a `partial` class, so your own endpoint helpers can live alongside the generated ones, and you can implement the `static partial void OnServerError(HttpContext, Exception)` hook in another part of the class to add custom handling (notifications, metrics, extra logging) whenever an endpoint responds with HTTP 500 — it runs after the built-in logging, and when you do not implement it the compiler removes the call itself. An exception thrown inside the hook is isolated: it is written to the server log and swallowed, so it never gets in the way of the original error response. Additional endpoints under the same prefix can also be mapped directly onto the `RouteGroupBuilder` returned by `MapGeneratedRemoteEndpoints()`.

### Binary transfer endpoints (Stream accessors for unbounded binary columns)

When combined with unbounded-binary exclusion (`--exclude-unbounded-binary-columns`), the excluded column's Stream accessors (`Read/Write{Column}Async`) are **streamed over HTTP**. Because a JSON envelope (`POST` + Base64) cannot avoid the memory inflation of a huge blob, these intentionally use a **second, REST-style form** (verb separation, raw body, `application/octet-stream`). The following three endpoints are generated per excluded column (`{column}` is the C# property name):

| Verb / URL | Meaning | Response |
|---|---|---|
| `GET {prefix}/{entity}/{column}?id=` | Download (stream the body to the destination) | 200 + `application/octet-stream` (an empty blob is also 200) / no row or NULL is **404** (`false` on the client) / a missing or malformed `id` is **400** |
| `PUT {prefix}/{entity}/{column}?id=` | Upload (raw body, `Content-Length` required) | Success **204** / no row **404** (`false`) / missing `Content-Length` (chunked) is **411** / a missing or malformed `id` is **400** |
| `DELETE {prefix}/{entity}/{column}?id=` | Set the column to `NULL` (equivalent to `Write(id, null)`) | Success 204 / no row 404 / a missing or malformed `id` is **400** |

- **The 404 these endpoints produce themselves carries a `RemoteError` body of type `"NotFound"`**, and only a 404 with that marker becomes `false` on the client. A bare 404 — a base address or prefix that does not match the server's, a route that no longer exists, a proxy answering on its own — would otherwise be indistinguishable from "no data", so the client raises `RemoteRepositoryException` for it instead of hiding the misconfiguration behind an empty result. The 411 carries a `RemoteError` body as well (type `"BadRequest"`, like the other classified rejections).
- **The key is carried in the URL query `?id=`** (the body is used for the blob itself). A VO key is serialized by the same rule as the JSON envelope (the wrapped value).
- **A 0-byte PUT (empty body) and setting to `NULL` (DELETE) are structurally distinguished** (the former makes `Read` return `true` + empty; the latter `false`).
- **Lifting the request-size limit on binary PUT is opt-in**: `MapGeneratedRemoteEndpoints(prefix, exposeErrorDetails, allowUnboundedUploads)`. It defaults to `false`, so the host's own limit (30 MB under Kestrel's defaults) applies to these endpoints too and a larger upload is rejected with **413**. Pass `allowUnboundedUploads: true` to stream GB-scale blobs, and **pair it with authorization (`MapGeneratedRemoteEndpoints(...).RequireAuthorization()`), because an endpoint that accepts a body of any size is a denial-of-service surface**. Only the binary PUT endpoints are affected (JSON endpoints always keep the host limit); to set a limit of a different size, override the whole group via the returned `RouteGroupBuilder`.
- The client (`Http{Entity}RemoteRepository`) receives `GET` with `ResponseHeadersRead` and copies to the destination in O(chunks), and sends `PUT` with `StreamContent` (with `Content-Length`). If you do not pass `length` for a non-seekable Stream, it throws `ArgumentException` **before sending** (the same length contract as existing). **The Stream you pass in stays yours**: the client neither closes nor disposes it, exactly as a direct connection does not, so switching between the two implementations does not change what happens to the stream (the HTTP layer disposes the request content after sending, so the stream is handed to `StreamContent` through a non-closing wrapper).
- **Making `WithUnboundedBinary()` / `Query()` / raw SQL remote is out of scope** (as before).

A working example is in the repository at [samples/ec-order-remote](../samples/ec-order-remote/README.md) (a sample that runs exactly this recommended layout as three projects across two real processes; it also demonstrates remote transfer of named queries and type restoration of `SaveConflictException`).

## In-memory repositories for tests (GenerateInMemoryRepositories)

You can additionally generate an in-memory implementation for unit testing without a DB. It implements the same contract, and unsupported operations throw `NotSupportedException` with guidance to switch to the real-DB repository.

### Known divergences from a real database

The in-memory store evaluates queries with LINQ-to-Objects rather than SQL, so a few semantics are its own rather than the database's. They are worth knowing when a test passes in memory and fails against the real thing:

- **String comparison and ordering are ordinal.** Filtering (`Where`) and `OrderBy` both compare strings ordinally, so `"B"` sorts before `"a"`. SQL Server's default collation is case-insensitive and orders by the collation's rules instead, so a test that depends on case or accent handling is not evidence about the database.
- **UNIQUE constraints are not enforced on write.** Only the primary key is (a duplicate key is rejected exactly as a real INSERT would be). A duplicate value in a UNIQUE constraint is stored without complaint; use `CheckUniquenessAsync` if a test needs the check.
- **Changing an entity's RowState from a Before hook takes effect here and nowhere else.** This backend reads the state again when it carries the operation out, so a `BeforeSaveAsync` that turns `Modified` into `Added` changes what happens; the QuickER Repository and EF Core have chosen the statement by then and the change is ignored. The hook contract promises neither behaviour — do not rewrite RowState from a hook. Return `false` to skip the row instead.
- **A `SaveConflictException` can surface after the After hooks have already run.** Writes are staged and published as one unit, and the publish re-verifies the rows the save started from — which is after `AfterSaveAsync`, since the hooks run outside the store lock. A real database has taken its locks long before that point, so a test that asserts "the After hook ran, therefore the save is done" holds against a database and not here.
- **Types without a rowversion column are last-write-wins under concurrent saves.** The publish verification only covers types that carry a concurrency token, so two saves racing on the same versionless row leave the later one's values. Last-write-wins stops short of resurrection, though: if another writer deleted the row in the meantime, the save that was updating it fails with `SaveConflictException` (`SaveConflictReason.NotFound`) rather than putting the stale snapshot back — a real database's UPDATE would simply affect no rows, and dropping the write silently would report a saved row that is not there. A staged delete for such a row is not a conflict, since deleting a row that is already gone is the same no-op it is against a database. Both of those rules apply to versioned types as well — existence is settled before the version is compared.
- **`insertWhenUpdateMissing` does not cover the publish window.** The choice between an UPDATE and the fallback INSERT is made during the save phase, while the store lock is held; if another writer deletes the row after that, the save is left holding a staged update and the publish verification reports `SaveConflictException` (`SaveConflictReason.NotFound`) instead of turning the write into an insert. A real database has no such window — its statement sees the row's absence at the moment it writes — so a test that relies on `insertWhenUpdateMissing` surviving a concurrent delete is testing this backend only.

## Runtime package reference mode (--use-runtime-packages)

By default, the generated code is self-contained inline output that includes the runtime (the schema-independent fixed code). Specifying `--use-runtime-packages` omits the fixed code and relies instead on references to the following NuGet packages (the required PackageReference is described in the generation header and the CLI output; add it to the csproj by hand):

| Package | Contents | Dependencies |
|---|---|---|
| `QuickER.Runtime` | Shared foundation and dialect-neutral contracts | None |
| `QuickER.Runtime.SqlServer` | QuickER's SQL Server dialect engine | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | QuickER's SQLite dialect engine | Microsoft.Data.Sqlite |
| `QuickER.Runtime.EntityFrameworkCore` | EF Core shared parts | Microsoft.EntityFrameworkCore.Relational |
| `QuickER.Runtime.InMemory` | The in-memory engine (for tests) | None |
| `QuickER.Runtime.AspNetCore` | The fixed server-side engine behind the generated remote endpoints | ASP.NET Core (a `FrameworkReference`, not a NuGet dependency) |
| `QuickER.Runtime.Sync` | The bidirectional sync engine (journal, table descriptors, conflict types) | None |

The package version and the tool version are published in lockstep (the same version), so use the same version for both. While the project is on 0.x, compatibility between minor versions is not promised (see the versioning policy in [CONTRIBUTING](../CONTRIBUTING.md)). Schema-dependent items such as the DI-registration extensions, `QuickErDbContext`, and per-entity implementations are always emitted on the generation side even in this mode.

### How generated files map to the packages (split output)

With file splitting, the fixed runtime and the schema-dependent code go into separate files, and **the fixed-runtime files correspond one-to-one with the packages**. The naming follows a single rule: the file name and the namespace suffix are the suffix of the package name (`Runtime.SqlServer.g.cs` → namespace `{Runtime}.SqlServer` → package `QuickER.Runtime.SqlServer`). `{Runtime}` below is the runtime namespace (`{RootNamespace}.Runtime` by default).

| Generated file (namespace) | Corresponding package | Contents |
|---|---|---|
| `Runtime.g.cs` (`{Runtime}`) | `QuickER.Runtime` | The shared foundation (base classes, attributes, VO bases, JSON converters) plus the dialect-neutral contracts (`IRepository`, the query pipeline, the remote client fixed part) |
| `Runtime.SqlServer.g.cs` / `Runtime.Sqlite.g.cs` (`{Runtime}.{dialect}`) | `QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite` | The dialect engine (repository base, expression-tree translation, executor, connection factory) |
| `Runtime.EntityFrameworkCore.g.cs` (`{Runtime}.EntityFrameworkCore`) | `QuickER.Runtime.EntityFrameworkCore` | EF Core shared parts (the `TContext : DbContext` generic repository base, VO translation plugins) |
| `Runtime.InMemory.g.cs` (`{Runtime}.InMemory`) | `QuickER.Runtime.InMemory` | The in-memory foundation (store, repository base, save staging) |
| `Runtime.AspNetCore.g.cs` (`{Runtime}.AspNetCore`) | `QuickER.Runtime.AspNetCore` | The fixed server-side engine (`RemoteServerEngine` — request reading, error classification, the error-detail exposure policy, the binary streaming helpers) |
| `Runtime.Sync.g.cs` (`{Runtime}.Sync`) | `QuickER.Runtime.Sync` | The sync engine (`SyncEngine`, `SyncJournal`, `SyncTable<,>`, the options, results, and conflict types; with remote services, the sync envelopes and the HTTP source base) |
| `Repositories.g.cs`, `Repositories.SqlServer.g.cs` / `Repositories.Sqlite.g.cs` / `Repositories.EntityFrameworkCore.g.cs` / `Repositories.InMemory.g.cs` / `Repositories.Sync.g.cs`, `RemoteServer.g.cs` | — (no package; always generated) | Schema-dependent code only: per-entity contracts and implementations, DI registration, `QuickErDbContext` and its Fluent configuration, the HTTP client, projection DTOs, the per-entity endpoints (`GeneratedRemoteEndpoints`), the per-table sync descriptors and journaling decorators |

`Runtime.g.cs` is always there, while the files below it are emitted only for a feature you actually enabled (the dialect files only when the QuickER Repository is generated, the EF Core file only with `GenerateEfCore`, the in-memory file only with `GenerateInMemoryRepositories`, the ASP.NET Core file only with `GenerateRemoteServices`, the sync file only with `GenerateSyncSupport`) — exactly the same set of packages you would have to reference.

Because of this layout, `--use-runtime-packages` means exactly one thing: **no `Runtime*.g.cs` is emitted at all, and the `using` directives in the generated code point at the fixed package namespaces (`QuickER.Runtime`, `QuickER.Runtime.SqlServer`, …) instead of `{Runtime}…`.** The `Repositories*` files are the same either way — turning the mode on or off does not change their contents.

Note that the file and namespace suffix `EntityFrameworkCore` is about matching the package name; the **C# type names are unchanged** (`EfCore{Entity}Repository`, `QuickErDbContext`, `AddGeneratedEfCoreRepositories`).

`Entities.g.cs`, `ValueObjects.g.cs`, `EditModels.g.cs`, and `Mappers.g.cs` are schema-dependent in their entirety and are unchanged by this mode. `RemoteServer.g.cs` is schema-dependent too — with split output the fixed engine behind it lives in `Runtime.AspNetCore.g.cs` (or, in package mode, in `QuickER.Runtime.AspNetCore`) and the file itself holds only the per-entity endpoints and the `OnServerError` hook; with non-split inline output the engine is embedded in `RemoteServer.g.cs` itself. Either way it stays a separate file, because it needs the ASP.NET Core `FrameworkReference`; all the other files above are concatenated into one with non-split generation.

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
