# Using the generated code

*English | [日本語](code-generation.ja.md)*

This document describes the structure of the C# code QuickER generates and how to use it.
It assumes you write .NET and know your way around C# and either ADO.NET or EF Core.
For how to run generation, see the [CLI reference](cli.md); for a working example, see [samples/ec-order](../samples/ec-order).

## Contents

The first eight sections are meant to be read in order.
They cover what you need to use the generated code.

- [What gets generated](#what-gets-generated)
- [The generated file header](#the-generated-file-header)
- [Edit model save workflow](#edit-model-save-workflow)
- [QuickER Repository](#quicker-repository)
- [EF Core mode](#ef-core-mode-generateefcorerepositories)
- [In-memory repositories for tests](#in-memory-repositories-for-tests-generateinmemoryrepositories)
- [rowversion columns and optimistic concurrency](#rowversion-columns-and-optimistic-concurrency)
- [Extending the generated base classes](#extending-the-generated-base-classes)

The rest is reference material, one section per generation option.
None of them is on by default, so read only the sections for the options you turn on.

- Adding capabilities: [value objects](#value-objects-generatevalueobjects) / [excluding unbounded binary columns](#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns) / [multi-target repositories](#multi-target-repositories-sqlserver--sqlite) / [bidirectional sync support](#bidirectional-sync-support---generate-sync-support) / [remote-capable interfaces](#remote-capable-interfaces---generate-remote-contracts) / [remote services](#remote-services---generate-remote-services)
- Changing the shape of the output: [runtime package reference mode](#runtime-package-reference-mode---use-runtime-packages) / [layered folder output](#layered-folder-output---layered-output) / [API reference](#api-reference-gmd)
- Also: [coexisting with an existing codebase](#coexisting-with-an-existing-codebase) / [license note](#license-note)

## What gets generated

| Category | Contents |
|---|---|
| Entity | A POCO that corresponds to a table. UI-framework independent (no dependency on CommunityToolkit or the like). Carries `RowState` (Unchanged / Added / Updated / Removed), state-transition methods such as `MarkAdded()`, and navigation properties (parent reference / child collection). |
| EditModel | A model for screen editing, plus conversion to and from the Entity. Every column keeps two representations: the committed value and the on-screen input string (`BindingXxx`). |
| Mapper | A converter for Entity ⇄ EditModel. Loading is lossless (see below). |
| Value objects (optional) | A per-column value-object type such as `CustomerIdValue`. Emitted only when `GenerateValueObjects` is enabled (see [Value objects](#value-objects-generatevalueobjects)). |
| Repository shared contracts | `IRepository<TEntity, TKey>` and a per-entity interface such as `ICustomerRepository`. Both the QuickER Repository and the EF Core Repository implement the same contracts. |
| QuickER Repository implementation | Lightweight per-dialect implementations (SQL Server / SQLite) plus DI-registration extensions. |
| EF Core Repository | `QuickErDbContext` (including the Fluent configuration) plus the EF Core Repository plus DI-registration extensions. |
| Runtime | The fixed code the above relies on (inlined into the output by default; a package-reference mode is also available). |

> **Prerequisite**: Repository generation targets a single primary key with application-assigned keys (tables with a composite key or DB auto-numbering can only use the Entity / EditModel).

> **Target framework**: The generated code is developed and verified on .NET 10. It also builds on .NET 8 as things stand, but that is not guaranteed. The runtime NuGet packages target `net10.0` only, so package-reference mode requires .NET 10.

### Mapper loading is lossless

The mapper copies committed values straight from the entity, and the `BindingXxx` strings are derived from them purely for display.
It never rebuilds a committed value by parsing an input string.
Only the fields the user actually edits take on the precision of their input string, so simply loading a row never drops what the display format cannot express, such as the sub-second part of a `DateTime` or its `DateTimeKind`.

Binary columns are copied defensively, so editing the loaded model never writes into the entity it was loaded from.
A `date` column (as opposed to `datetime`) is displayed with the culture's short date pattern, without a "0:00:00" tail.

### DB-definition metadata attributes

By default, an Entity is decorated with DataAnnotations and DB-definition metadata attributes (`[DbTableMeta]` / `[DbColumnMeta]`) that record a dialect-neutral type token (`string(50)` / `decimal(10,2)`, and so on) and a description.
The generated code therefore doubles as a self-describing document of the DB definition.

For some columns the token does not reproduce the DB type spelling: `numeric` collapses onto `decimal`, and `datetime` onto `datetime2`.
For those columns, `[DbColumnMeta]` also records the original text as `NativeType`, and an information diagnostic names them at generation time.
That record is what lets C# reverse engineering restore the column type exactly as the diagram spelled it.

Whether the attributes are applied is controlled by `IncludeDataAnnotations` (default ON).
It cannot be turned off in a configuration that generates the QuickER Repository, EF Core Repository, or in-memory Repository contracts, which is a diagnostic error.
The runtime reads `[Table]` and `[Key]` through reflection.
`[Column]` sits outside that option and is always applied: it is the persistence mapping itself, and the generated runtime treats a property without it as not being a column.

## The generated file header

Every generated `.g.cs` file starts with three lines:

```csharp
// <auto-generated />
// Generated by QuickER 0.2.0
// Content hash: sha256:3f7a…
```

The first line tells the compiler, analyzers and formatters (CSharpier among them) to leave the file alone.
The second records the QuickER version that produced the file, and the API reference carries the same line as an HTML comment.
The third lets QuickER tell whether the file has been edited by hand since it was generated.

### Regenerating over edited files

Before writing, QuickER checks every `.g.cs` it is about to replace.
If any of them has been edited by hand, nothing is written until you decide:

| Entry point | When edited files exist | To overwrite them |
|---|---|---|
| GUI | A confirmation lists the edited files; Cancel writes nothing | Choose OK |
| CLI (`generate` / `scaffold`) | The edited files are listed on stderr, nothing is written, and the exit code is `2` | Add `--force` |
| MCP (`generate_csharp`) | The call fails and lists the edited files | Pass `force: true` |

To keep a change, put it in a separate file rather than in the `.g.cs` itself: a `partial` class next to the generated one.

### What counts as an edit

The hash is computed over the file with whitespace, line endings, a byte-order mark, and a comma directly before `}`, `)` or `]` removed.
The version and hash lines are left out of it as well.
Reformatting (CSharpier, the IDE's Format Document, git's line-ending conversion) and upgrading QuickER are therefore not edits.
It follows that a change touching only whitespace, including whitespace inside a string literal, is not detected either.

A file without a hash line is not checked and is overwritten as before, such as one generated by an earlier QuickER or one whose header was removed.
The API reference (`.g.md`) is not checked.
The hash is there to catch accidents, not tampering: anyone can recompute it.

## Edit model save workflow

An edit model is what the screen binds to; the entity is what gets saved.
The mapper moves values between them, and the round trip is always the same four steps:

```csharp
var mapper = new CustomerMapper();

// 1. Fetch, and turn the entity into an edit model (loading is lossless)
var entity = await customers.GetByIdAsync(1);
var editModel = mapper.CreateEditModel(entity!);

// 2. The screen writes the BindingXxx strings; each one commits to the typed value
editModel.BindingName = "Alice";
editModel.Orders[0].BindingAmount = "1200";

// 3. Validate the whole graph (required inputs, conversion failures, duplicates among siblings)
if (!editModel.Validate())
{
    foreach (var error in editModel.CollectErrors())
    {
        Console.WriteLine($"{error.Path}.{error.Property}: {error.Message}");
    }

    return;
}

// 4. Write the committed values back and save (pass includeRemoved: true when saving)
mapper.ApplyToEntity(editModel, entity!, includeRemoved: true);
await customers.SaveAsync(entity!);

// The save succeeded, so reset the graph to the unchanged state
editModel.AcceptChanges();
```

For a brand-new row, build the entity instead of applying to one.
Use `mapper.CreateEntity(editModel, includeRemoved: true)`, or `CreateEntities(collection, includeRemoved: true)` for a whole collection.

**Loading into an edit model rebuilds its child collections.**
`mapper.ApplyToEditModel(entity, editModel)` replaces the previous collection instances with new ones.
That call is the load `CreateEditModel` performs, and it is also how a screen refreshes a model it already holds.
View state tied to the old instance goes with them, the selected item for one.
Event subscriptions on the old collection (CollectionChanged and the like) go with them too, so re-subscribe on the new instance.
The removals the collection was tracking for the next save go with them as well.
Reload only when discarding pending child edits is what you mean.

**Pass `includeRemoved: true` whenever the result is going to be saved.**
`includeRemoved` has no default: it is a required argument, so every call site states whether it is building a graph to save (`true`) or to display (`false`).
`false` is for display purposes, such as a report or a preview.
It leaves out the rows that are being tracked for deletion, so the resulting entity graph carries no deletions and the save silently keeps the rows the user removed.

### Removing rows: `Remove()` versus `MarkRemoved()`

There are two ways to delete a child row, and they differ only in where the row lives afterwards:

| Call | Where the row goes | Typical use |
|---|---|---|
| `collection.Remove(item)` | Out of the collection, into its deletion tracking (`RemovedItems`); the row is marked `Removed` | The row disappears from the screen |
| `item.MarkRemoved()` | Stays in the collection, marked `Removed` | The row stays on screen, struck through or greyed out, until the save |
| `collection.Clear()` | Everything leaves the collection and nothing is tracked for deletion; rows an earlier `Remove()` had set aside are restored to the state they held before removal and released | Rebuilding the list on screen (after a reload) |

Either way the row is deleted on save, as long as you passed `includeRemoved: true`, and either way only its key takes part in the delete.
Adding the very same instance back cancels the deletion tracking and restores the state the row had before it was removed.

`Clear()` is the exception: it is a wipe of the display, not a delete, so it also **drops the deletions that were pending**.
Rows set aside by an earlier `Remove()` no longer reach the save.
Releasing the tracking list without restoring them would be worse: they would stay marked `Removed` with nothing left to undo it, and adding such an instance back would put a silent deletion target into the collection.
To delete every row, remove or mark each one rather than clearing.

A row that is about to be deleted contributes nothing but its key.
**Rows marked for deletion are therefore left out of `Validate()`, `CollectErrors()`, and the uniqueness checks**, both among the siblings and against the database, subtree and all.
An unfinished, unconvertible, or duplicate value on a row the user has deleted cannot block the save.
The mapper follows the same rule and skips a removed row's absent non-key values instead of requiring them.
The errors themselves stay registered on the row, so a per-row display (`HasErrors` / `GetErrors`, that is `INotifyDataErrorInfo`) keeps showing them, and putting the row back brings them straight back into the validation.

The cleanup after a confirmed save (`AcceptChanges()`) is automatic.

- Rows set aside by `Remove()` leave the tracking list and become Added again, subtree and all.
  Their stored rows are gone together with their cascade descendants, so adding the same instance back means inserting the whole graph as new rows.
- Rows left in the collection by `MarkRemoved()` leave the collection.
  A deleted row must not come back as an ordinary one.
  The departed row stays `Removed` and is not a deletion target for the next save either.

A single cascade child is the one exception, because it has no collection to leave.
A `MarkRemoved()` single child stays on the navigation property, still `Removed`.
Assign null to it once the save is confirmed.
Left in place, the next save attempts the same delete again.
A no-row delete is silently accepted by the QuickER repositories but throws in an EF Core graph save, a known asymmetry, so nulling it out is the safe move.

## QuickER Repository

A lightweight repository with minimal dependencies (ADO only).
The supported dialects are SQL Server (`FOR JSON` based) and SQLite (plain SELECT plus multi-query), and the DI-registration extensions are generated with per-engine names (`AddGeneratedSqlServerRepositories` / `AddGeneratedSqliteRepositories`).

```csharp
// DI registration (the generated extension method; choose SqlServer / Sqlite by dialect)
var provider = new ServiceCollection()
    .AddGeneratedSqliteRepositories(connectionString)
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRepository>();
```

The registration also wires up `ISqlExecutor`, the entity-independent raw SQL executor, and hands it to every repository it registers.
Registering your own implementation after the generated extension therefore makes the repositories' raw SQL methods go through it as well, which is how you add logging, metrics, or retries around raw SQL.
A repository constructed by hand takes the executor as an optional third argument and builds the default one when it is omitted, so existing `new` calls are unaffected.

### Connections and schema bootstrapping

The generated `SqlConnectionFactory` is what opens every connection, and on **SQLite it enables foreign key enforcement by default**.
SQLite checks foreign keys only when a connection asks it to, so without this the foreign keys in the generated DDL would be silently inert: a child row could reference a parent that does not exist, and deleting a parent would leave its children behind.
Since the schema declares the constraints, enforcing them is the correct default.
An explicit `Foreign Keys` keyword in the connection string is honored exactly as written, so `Foreign Keys=False` restores the provider's own behavior.

For creating a schema from the DDL QuickER generates, `SqliteSchemaBootstrap.ApplyDdlAsync` / `SqlServerSchemaBootstrap.ApplyDdlAsync` open a connection and run the whole script in one call.

```csharp
var ddl = await File.ReadAllTextAsync("Shop.sql");
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl);

// A large script on a slow machine may need longer than the provider's default command timeout
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl, TimeSpan.FromMinutes(5));
```

This is a bootstrap convenience for development, tests, and samples, not schema management.
It knows nothing about versions, about what already exists, or about rolling back, so anything that outlives a throwaway database wants a migration tool instead.
EF Core mode draws the same line: it connects to an existing schema, and Migrations are out of scope.

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

`BulkInsertAsync` keeps the same contract on every backend.
A `null` element is skipped, as it is in a graph save's list, and the return value counts only the rows actually inserted.
An empty collection returns 0 without opening a connection, and a cancellation already requested on entry throws before anything is written.

On SQL Server the bulk insert runs through `SqlBulkCopy` with `CheckConstraints` always on.
Foreign key and CHECK constraints are honored, so a row the row-at-a-time `InsertAsync` would reject fails the copy too.
`SqlBulkCopy` skips those checks unless asked, which would otherwise let a bulk insert write rows the rest of the API refuses.
Triggers are deliberately not fired, since QuickER's DDL generates none.

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

`Include` / `ThenInclude` calls naming the same navigation are merged into one node.
The same branching idiom as EF Core therefore writes multiple branches under `Orders`: `Include(c => c.Orders).ThenInclude(o => o.OrderLines)` followed by `Include(c => c.Orders).ThenInclude(o => o.Customer)`.

Supported: equality, comparison, `&&`/`||`, `Contains`/`StartsWith`/`EndsWith` (LIKE), `Contains` on a list (IN), date parts (`Year`, etc.), `string.IsNullOrEmpty` and `string.IsNullOrWhiteSpace`, and value-object comparison.
Projection (Select), GroupBy, Join, and arithmetic expressions are not supported and throw at runtime; work around them with raw SQL or EF Core.

**Navigation properties cannot appear in a predicate or an ordering key.**
`Where(o => o.Customer == null)` throws `NotSupportedException`.
A navigation has no column of its own, so filter on the foreign-key column instead (`Where(o => o.CustomerId == null)`).
The in-memory and EF Core backends do translate such a predicate, so this is a limitation of the QuickER Repository rather than a shared one.

#### Null compensation

Nulls in an equality comparison and in an IN search are compensated so that every backend agrees with C# and EF Core.

| What you write | The SQL it becomes |
|---|---|
| `col == null` (also when a variable or expression evaluates to null) | `col IS NULL` |
| `col != null` (same) | `col IS NOT NULL` |
| `col != value`, value non-null | `(col <> @p OR col IS NULL)` |
| `a == b`, column to column | `(a = b OR (a IS NULL AND b IS NULL))` |
| `a != b`, column to column | `(a <> b OR (a IS NULL AND b IS NOT NULL) OR (a IS NOT NULL AND b IS NULL))` |
| `list.Contains(col)`, list holds a null | `(col IN (@p0) OR col IS NULL)` |
| `!list.Contains(col)`, list holds a null | `(col NOT IN (@p0) AND col IS NOT NULL)` |
| `!list.Contains(col)`, no nulls in the list | `(col NOT IN (@p0, @p1) OR col IS NULL)` |

The reason is that a bare comparison such as `col <> @p` or `a = b` drops `NULL` rows as UNKNOWN, while C# and EF Core both read `NULL` as different from a non-null value and two `NULL` sides as the same.
Binding a null as an ordinary parameter would leave `col = @p`, which SQL's three-valued logic makes false for every row.
Whether a column allows `NULL` cannot be decided reliably from the expression tree, so the column side is compensated unconditionally; on a column that is never `NULL` the added disjunct simply never holds.

**Some combinations are deliberately left alone.**
A column compared against a value needs nothing on the `==` side, where SQL and C# already agree that a `NULL` column does not match a non-null value.
The relational operators (`<` `<=` `>` `>=`) still bind the null as a parameter, because they have no null-aware SQL counterpart.

An IN search has a few cases the table does not cover.

- A null mixed into the collection is not bound as an element; it folds into an `IS NULL` test on the column. SQL's `IN` never matches a value against `NULL`, so binding it would never reach a row whose column is `NULL`, while C# and EF Core both read a list containing null as matching such a row.
- A list without nulls keeps the plain `col IN (...)`. Only its negation is compensated, for exactly the reason the column side of `!=` is: C# and EF Core both count null as *not* contained in a list of non-null values and so read the negation as true, while the bare `NOT IN` is UNKNOWN for those rows and drops them.
- A list of nothing but nulls has no value left to compare against once the nulls are folded out, so it becomes `IS NULL` / `IS NOT NULL` on its own.
- An empty list matches no row for `IN` and every row for `NOT IN` (`IN ()` is invalid SQL, so it becomes a constant condition). Both agree with C#'s `Contains`.

Negation flips the operator instead of wrapping the comparison in `NOT (...)`.
`!(a == b)` and `!(a != b)` therefore get exactly the compensation the opposite operator gets on its own.
A negated `Contains` is folded into the IN clause the same way, emitting the `NOT IN` forms above.
A negated negation cancels out, so `!(!(a != b))` is translated as `a != b` with its compensation intact, and any deeper stack of negations collapses the same way.
Negating anything other than an equality or an IN search still produces `NOT (...)`.

> **Known limitation**: the flip only applies when the `!` sits directly on a comparison. `!(a == b && c)` is not rewritten by De Morgan's laws: it becomes `NOT (...)` around individually compensated operands, and `NOT (UNKNOWN)` is still UNKNOWN, so a row that a `NULL` made UNKNOWN inside the parentheses is dropped where C# and the in-memory and EF Core backends would have kept it. Where a `NULL` is possible, write the negation on the comparison itself (`a != b || !c`), which takes the flip and agrees with them.

String matching in the mini DSL (`LIKE` / `CONTAINS` / `STARTSWITH` / `ENDSWITH`) is emitted against a nullable column with an explicit "the column is not `NULL`" conjunct.
`NOT LIKE` sits inside the same premise, so a `NULL` row matches in neither direction.
SQL's `LIKE` already drops `NULL` rows as UNKNOWN, so nothing changes on the SQL side.
The in-memory backend, however, compiles the expression tree and actually evaluates it, where the missing premise would raise a `NullReferenceException` on a `NULL` row.

In a DSL `LIKE` literal, only a leading or trailing `%` is read as a pattern (a `%` or `_` in the middle is a validation error).
Character classes such as SQL Server's `[...]` get no special treatment: they match as strings that contain the brackets themselves.
Write a raw SQL query when you need pattern syntax.

#### Letter case, whitespace, and date parts

How a string match treats letter case is left to the store, because the `LIKE` is emitted unqualified: no `LOWER`, no `COLLATE`.
And the stores disagree.

| Backend | How `Contains` / `StartsWith` / `EndsWith` decide |
|---|---|
| SQL Server | Follows the column's collation, which is case-insensitive under the usual `..._CI_AS` default |
| SQLite | The built-in `LIKE` folds case for ASCII letters only, so `A` matches `a` but `Á` does not match `á` |
| EF Core | Hands the call to its provider and gets that same store-dependent answer |
| In-memory | Compiles the expression and evaluates it in C#, so the comparison is ordinal and case matters, on a plain string column and on a value object alike |

`Equals(..., StringComparison.*IgnoreCase)` is translated as `LOWER(col) = LOWER(@p)` and folds by the engine's own rules, ASCII-only on SQLite included.

This is worth knowing wherever one predicate has to hold across backends.
A hybrid build runs the identical predicate on two engines, so a search that is case-insensitive on the server can be case-sensitive on the local side.
Multi-target generation is one such build; a remote repository against the server with a local one against the copy is another.

In a `Query()` predicate, `string.IsNullOrWhiteSpace` becomes `(col IS NULL OR LTRIM(RTRIM(col)) = '')`.
The one-argument `LTRIM` / `RTRIM` of both SQL engines strip the space character alone, so a column holding a tab, a newline, or a non-breaking space is not matched.
The in-memory backend evaluates the real `string.IsNullOrWhiteSpace`, which counts every Unicode whitespace character, and does match it.

Date parts (`Year`, `Month`, …) are translated only when the member is read from a `DateTime`, `DateOnly`, or `DateTimeOffset` column, nullable forms included.
A property of the same name on any other type is not a date part, such as one added to a value object through a partial declaration.
It fails with `NotSupportedException` instead of silently becoming `YEAR([col])`.

`Contains` on a list expands to one bind variable per element and is not chunked.
A very large list therefore runs into the dialect's bind-variable or IN-list limit and fails at runtime (Oracle's 1000, SQL Server's 2100 parameters, SQLite's historical 999).
For a large set of keys, stage them in a temporary table and join, or use raw SQL.

### Graph fetch (IncludeGraph)

```csharp
var fetched = await orders.Query()
    .Where(o => o.CustomerId == 1)
    .IncludeGraph()                 // Includes the same cascade the graph save follows
    .ToListAsync();

var one = await orders.Query().IncludeGraph().GetByIdAsync(1000);   // one row, whole graph, by key
```

This is the fetch-side counterpart of the graph save (`SaveAsync`).
`IncludeGraph()` is sugar that expands the same child-direction cascade navigations the save follows, all the way down, into an `Include` tree.
The result is the same as spelling out `Include(...).ThenInclude(...)` by hand.
It is always generated as a per-entity extension method and composes freely with `Where` / `OrderBy` / paging / `FirstOrDefaultAsync`.

Add a child table to the diagram and regenerate, and `IncludeGraph()` follows automatically.
A hand-written `Include` chain does not, and the "aggregate" it fetches silently becomes incomplete.
Preventing that is the main point of this method.

The query-side `GetByIdAsync` is a terminal sugar with the primary-key predicate baked in, equivalent to `Where(x => x.OrderId == id).FirstOrDefaultAsync()` and null when there is no match.
Its key takes the same type as the contract method of the same name, and without `Include` / `IncludeGraph` it returns the same result as `repo.GetByIdAsync(id)`.
It can also be called from the middle of a manual `Include(...)` chain.

The fetched graph comes back with `RowState = Unchanged`, so the fetch, edit, save round trip works as is.

- **A navigation pointing back to a table already on the path from the root is not followed.**
  Self references (`Category.Children` and the like) and mutual references cannot be mapped onto a finite `Include` tree, so those edges are skipped and called out in an Info diagnostic at generation time.
  A skipped navigation comes back empty; fetch recursive structures with manual `Include` to whatever depth you need.
  Additional `Include` calls can be stacked after `IncludeGraph()` (`Query().IncludeGraph().Include(x => x.Customer).GetByIdAsync(id)`).
  Adding parent references or skipped navigations is the typical use, and re-including a child-direction navigation the closure already covers is safe: it merges into the node the closure already has, which is useful for hanging a `ThenInclude` branch below it.
  The save side walks the instance graph, which is finite, and therefore saves to any depth. This asymmetry between fetch and save is by design.
- The method is generated for entities without any cascade child too, where it is a no-op that returns the query unchanged.
- On a deep or wide diagram the fetch is correspondingly large.
  SQL Server fetches the whole graph as one nested JSON query (SQLite splits it into one query per level), so when only some children are needed, narrow the fetch with manual `Include`.
- It cannot be combined with `WithUnboundedBinary()` (the same exclusion as `Include`).
  The remote face (`I{Entity}RemoteRepository`) has no `Query()`, so `IncludeGraph` is not available remotely either.

> **Note**: the cascade is an application-side notion, independent of the database's referential actions. Every child-direction navigation belongs to the cascade closure, whether the foreign key says `ON DELETE CASCADE`, `NO ACTION`, or nothing at all. The aggregate this fetches and the aggregate `SaveAsync` cascades over are the same one, and the database's action is not consulted for either.
>
> `MarkRemoved()` on a root fetched with `IncludeGraph()` followed by `SaveAsync` therefore deletes every descendant with explicit DELETE statements, even where the foreign keys are `NO ACTION` and the delete would otherwise have been refused. On a diagram where a master table is the parent of transaction tables, deleting one master row that way reaches all the data below it. Fetch only what should be saved together (narrow with manual `Include`), or delete through `DeleteAsync(id)`, which is a single-row delete and lets the database's own referential action have the last word.

### Graph save (save parent and children in one call)

```csharp
var order = new OrderEntity { OrderId = 1000, CustomerId = 1 };
order.OrderLines.Add(new OrderLineEntity { OrderLineId = 5000, OrderId = 1000, ProductId = 100, Quantity = 2 });

order.MarkAdded(includeChildren: true);         // Marks the whole aggregate: the same cascade the save follows

var affected = await orders.SaveAsync(order);   // Runs INSERT / UPDATE / DELETE per RowState in one transaction
```

`MarkAdded(includeChildren: true)` walks the cascade navigations a graph save follows, all the way down.
A freshly built aggregate is therefore marked in one call instead of one call per node; build the graph first, then mark it.

Only `MarkAdded` offers the cascading form.
Marking a whole graph for update would rewrite every row including the untouched ones, and marking a whole graph for removal is what the graph save's `cascadeDelete` already does from the root alone.

### Save hooks (ISaveHook)

A mechanism that inserts processing before and after each operation of a graph save (`SaveAsync`).
The main use cases are a single-row skip based on a state check in the before-step, and registering file data within the same transaction in the after-step, which makes the save and the blob write atomic.
Hooks are always generated; if none are registered they are a complete no-op.

Implement `ISaveHook<TEntity>` and register it in DI.
Both methods have a default implementation, so you can write only the one you need.

```csharp
public sealed class DocumentSaveHook : ISaveHook<DocumentEntity>
{
    // Just before the operation. Returning false skips this one row (the default does not skip)
    public Task<bool> BeforeSaveAsync(
        DocumentEntity entity, SaveOperation operation, CancellationToken ct = default)
    {
        // Example: only approved documents may be deleted (other deletes are skipped)
        if (operation == SaveOperation.Delete && !entity.IsApproved)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    // Just after the operation, before commit. The context joins the same transaction
    public async Task AfterSaveAsync(
        DocumentEntity entity, SaveOperation operation, ISaveHookContext context,
        CancellationToken ct = default)
    {
        if (operation == SaveOperation.Insert)
        {
            // Stream into the excluded (blob) column, in the same transaction as the save
            await context.WriteBinaryColumnFromFileAsync(
                nameof(DocumentEntity.Payload), entity.DocumentId, "/tmp/upload.bin", ct);
            // Leave an audit row through raw SQL, also in the same transaction
            await context.ExecuteSqlAsync(
                "INSERT INTO audit (note) VALUES (@note)", new { note = $"created {entity.DocumentId}" }, ct);
        }
    }
}
```

```csharp
// DI registration (Singleton or Scoped; use Scoped when the hook consumes scoped services)
services.AddSingleton<ISaveHook<DocumentEntity>, DocumentSaveHook>();

// The entity types can also be derived from the instance itself: this registers the hook for
// every ISaveHook<TEntity> it implements, so one hook can cover several tables without a line each
services.AddSaveHook(new AuditSaveHook());
```

Without a DI container, build a `SaveHookRegistry` and hand it to the repository constructor.
Hooks fire in the order they were added, exactly as with the DI-backed registry.

```csharp
var hooks = new SaveHookRegistry()
    .Add<DocumentEntity>(new DocumentSaveHook())
    .Add<OrderEntity>(new OrderSaveHook());

var documents = new DocumentRepository(connectionFactory, hooks);
```

Building the registry is not thread-safe, so add every hook before handing it to a repository.
Resolution afterwards is read-only.

You can register multiple hooks for the same entity type.
Before runs in registration order and short-circuits the moment one returns `false`: the remaining Before hooks are not called, and that row is skipped.
After also runs in registration order.
An exception thrown by Before or After propagates as-is and rolls back the entire save.
A target with a real transaction rolls the transaction back, and in-memory never published the writes in the first place (see below).

**Only `SaveAsync` is targeted**, in both the single and the multiple form.
Direct calls to the low-level APIs `InsertAsync` / `UpdateAsync` / `DeleteAsync`, and `BulkInsertAsync`, bypass the hooks and do not fire them.

#### Before and skip semantics

`false` skips only that entity's single operation; other rows proceed.
A skipped row does not get an After call, and its `RowState` is left unchanged, which excludes it from `AcceptChanges`.

Because a skip is isolated, consistency is the hook author's responsibility.
In particular, deletes run children-first, so **returning `false` for only the root of a subtree delete leaves the children deleted and the root behind**.
To stop the parent, the children's hooks must also return `false`.
An inconsistent skip, such as skipping a new parent while saving a new child, falls to the safe side through an FK constraint violation, an exception, and a full rollback, provided the FK constraint is actually defined in the database.

#### After and the context

After receives an `ISaveHookContext` that joins the in-flight transaction, just after the operation and before commit.
Calling the repository's ordinary APIs from within the hook would contend for locks on a separate connection, so use the operations exposed through `context`.
Because throwing from After rolls back the whole save, the half-finished state of "the row exists but the file is not registered" is structurally impossible.
In-memory gives the same guarantee by staging its writes and publishing them only once every phase has succeeded.

The context provides two operations and does not expose raw handles.

- `WriteBinaryColumnAsync(propertyName, key, stream, length?)`, and the file convenience method `WriteBinaryColumnFromFileAsync(propertyName, key, path)`.
  Both stream-write to an excluded column (when `ExcludeUnboundedBinaryColumns` is enabled); name the column with `nameof`.
- `ExecuteSqlAsync(sql, parameters)` for arbitrary DML, such as an audit row or a write to a related table.

`operation` receives the operation that actually happened.
If `insertWhenUpdateMissing: true` and no update target is found so it switches to INSERT, Before is called once with `Update` and After is called with the actual `Insert`.

#### Differences by implementation target

| Target | Hook firing | Context support |
|---|---|---|
| QuickER Repository (SQL Server / SQLite) | Full support (After fires right after each operation) | Both `WriteBinaryColumnAsync` and `ExecuteSqlAsync` are supported |
| EF Core Repository (`GenerateEfCoreRepositories`) | Supported (After fires in a batch after `SaveChanges`) | `ExecuteSqlAsync` supported; `WriteBinaryColumnAsync` throws `NotSupportedException` |
| In-memory (`GenerateInMemoryRepositories`) | Supported (pseudo transaction) | `WriteBinaryColumnAsync` writes to the store; `ExecuteSqlAsync` throws `NotSupportedException`. There is no real transaction, but the save unit is all-or-nothing through copy-on-write: every write is staged and published as one unit only after the last phase succeeds, so nothing a failed save wrote (including a blob written by After) is ever visible, and a concurrent writer's changes cannot be trampled by the failure |
| Remote (`--generate-remote-services`) | A hook registered in the server-side DI fires | Follows the server-side real implementation. A row the server skipped in Before travels back in the save response, so the client-side `RowState` is left untouched as well (the row stays pending and is retried on the next save), exactly as on a direct connection |

### Raw SQL escape hatch

When a query cannot be expressed with an expression tree, you can always drop down to raw SQL.
Parameters are an anonymous object.

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

A parameter whose value is a collection (anything enumerable except `string` and `byte[]`) is expanded for `IN`.
Write it inside parentheses as `IN (@ids)`.
Each element is bound as `@ids0, @ids1, ...`, and the `@ids` in the SQL is rewritten to match.

```csharp
var rows = await customers.QueryBySqlAsync(
    "SELECT * FROM customers WHERE customer_id IN (@ids)", new { ids = new[] { 1, 2, 3 } });
```

Two things to know about the expansion:

- **An empty collection expands to `(NULL)`.**
  For `IN` that is right, because nothing matches.
  For `NOT IN (@ids)` it is a trap: `x NOT IN (NULL)` is UNKNOWN for every row, so no row matches, which is the opposite of what an empty exclusion list should mean.
  Branch to a different statement when the collection can be empty.
- **The rewrite is textual.**
  It replaces `@name` anywhere in the command text, including inside a string literal or a comment, so do not write the parameter's own name in those places.
  Names that merely start the same (`@idsSuffix`, `@ids0`) and system variables that merely end the same (`@@ids`) are left alone.

### Uniqueness pre-check (CheckUniquenessAsync)

A table's UNIQUE constraints are stamped on its generated Entity class as `[UniqueConstraint("PropA", "PropB", Name = "UQ_...")]`, next to `[DbTableMeta]` / `[DbColumnMeta]`.
Like those, it is definition metadata that makes the entity a self-describing document of the DB definition, and the checks below read none of it, being plain generated code.
The one part that does read it is the in-memory store, which has no database behind it and enforces the constraint from this declaration.
The attribute type is emitted when at least one table has a constraint to declare, and whenever the in-memory repository is generated.
C# reverse reads the attribute back, so the constraints round-trip (see [Import and export](import-export.md)).

Every repository contract carries a bulk check driven by the diagram's UNIQUE constraints.
It is declared once on the common surface `IRemoteRepository<TEntity, TKey>`, so every `I{Entity}Repository` (and `I{Entity}RemoteRepository`) provides it through inheritance, whether or not the table has any constraint.

```csharp
Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
    TEntity entity, CancellationToken cancellationToken = default);
```

For each UNIQUE constraint of the table it asks "does a row with the same value tuple already exist, excluding rows that share this entity's primary key?".
Because the same-key row is excluded, the same call is correct both before an insert and before an update.
When the primary key can be null (a value object or `string` key) and has not been assigned yet, which is the normal state of a new row, the exclusion is left out entirely and the check really does search every row.
Constraint member values that contain a `null` are skipped, since NULL collision semantics differ per dialect.

> **Note**: the result is advisory. The definitive guarantee is the database's own UNIQUE constraint: a concurrent insert between the check and the save can still make the save fail (TOCTOU). Use the check to give a friendly message, and keep handling the save exception.

The implementation lives on each backend's repository base class and runs the same expression-tree query (QuickER Repository for each dialect, EF Core, and in-memory), so all of them behave the same.
What a generated repository adds is its constraint table and the bridge to the hook below.

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

Rules the diagram cannot express, such as a conditional uniqueness or a cross-table rule, are added through an optional partial method generated on every repository implementation.
While it is unimplemented the call is erased at no cost.

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

The generated checks run first, then the collected delegates in registration order, and every non-null result joins the returned list.
With remote services the whole check, hooks included, runs in the server-side repository; the HTTP client only forwards the call.

#### Edit models: duplicates inside a collection

An edit model whose table declares UNIQUE constraints also declares them in generated code, as a `static readonly` table of `EditModelUniquenessConstraint` (constraint name, member property names, and a compiled accessor for their values) published through the `UniquenessConstraints` property.
`EditModelCollection<T>.Validate()` reads that table and flags values duplicated among the elements themselves, registering an error on the binding property of every member of each duplicated group.
No reflection is involved, exactly like the generated required-field checks.
Value tuples containing a `null` are skipped and deletion targets (`RowState.Removed`) are excluded, matching the database check.

For a root-level list that is not an `EditModelCollection<T>`, call the same helper directly.

```csharp
var valid = EditModelUniquenessValidator.Validate(models);
```

Validating the parent covers the collection.
`parent.Validate(includeChildren: true)` delegates each registered child collection to `EditModelCollection<T>.Validate()`, so the duplicate check among the siblings runs from the root call as well, and `parent.CollectErrors()` returns the duplicate-value errors along with the rest, each under its `Orders[i]` path.
A child collection that a mapper load replaces wholesale is picked up too, because the cascade registry resolves the collection through an accessor at every use rather than capturing the instance it was registered with.

Duplicate-value errors live in a store of their own, separate from input errors (required, conversion, value object, `OnValidate`).
Registering or clearing one kind never touches the other, so a property can carry a conversion error and a duplicate-value error at the same time, and `GetErrors` returns both.
In particular, resolving a duplication and re-validating does not silently drop the "cannot be converted" error left on the same field.
That error only ever comes back through the binding setter, so clearing it would leave an invalid input on screen with `Validate` reporting success.
`HasErrors` covers both stores.

Every error is owned by the check that registered it, and a check only ever adds or removes its own.

- **The binding setter** owns conversion and value-object errors (`SetError`).
  It is the only thing that can produce them again, so nothing else clears them.
  A blank input is never a conversion error: it sets the confirmed value to null and withdraws the setter's own error, and whether null is acceptable is the required check's call.
  This rule holds regardless of value objects and of the column's type (numeric, date/time, bool, binary, string), so a nullable column can be set back to NULL from the screen.
- **The required-field check** (generated `ValidateSelf`) adds a missing-input error only when the property carries no other input error, so a field that holds unconvertible text is not silently relabelled "is required".
  It clears its own error as soon as the value is present, even when the value was assigned straight to the committed property rather than typed.
- **The two uniqueness checks** each have their own slot on a property, addressed by `DuplicateErrorSource` (`Siblings` for the check among the elements of a collection, `Database` for the check against the stored rows).
  Neither overwrites nor clears the other's slot, so a value that is a duplicate both among its siblings and in the database reports both findings, each disappearing when its own check stops reporting it.
  Validating the graph before a save therefore does not discard what the database check has just reported, and vice versa.
- **Changing a committed value withdraws the `Database` findings** of that edit model, because the check compared the values it held before the edit.
  All of them go, since a composite constraint's verdict rests on every column it covers.
  The `Siblings` findings are left for the next validation to decide.
- **`OnValidate`** owns whatever it registers.
  Clear a custom error from the hook (`SetError` with a null message) once its condition no longer holds.

`RevertInput()` rebuilds the input strings and clears the input errors only.
A mapper load goes further and clears the duplicate-value errors of both checks as well, because the values they judged are gone.

#### Edit models: checking against the database

When edit models and a repository contract are both generated, each edit model also gets a convenience wrapper.

```csharp
// The repository parameter is I{Entity}RemoteRepository when remote contracts are generated, I{Entity}Repository otherwise
if (!await editModel.ValidateUniqueAsync(repository))
{
    // Errors are already registered on the binding properties (INotifyDataErrorInfo shows them in the UI)
}
```

It builds an entity from the edit model's confirmed values, calls `CheckUniquenessAsync`, and maps each violation's `PropertyNames` back to the binding properties.
A violation with no property names, or with names that do not belong to the edit model, becomes a model-level error registered under the empty property name, which `GetErrors(null)` returns.
The duplicate-value errors registered by the previous call are cleared first, so re-checking never leaves stale errors; it clears its own findings only, so what the check among the siblings reported stays.
A model marked for removal returns `true` without querying at all, in line with every other read of the validation state.

The errors are registered after the `await`, which puts them on a thread pool thread rather than the caller's, and `ErrorsChanged` fires on that same thread.
A WPF binding marshals the notification back to the UI thread by itself, so the ordinary case needs nothing from you.
A subscriber that updates UI state directly has to marshal it at the call site.

The message comes from `EditModelMessages.DuplicateValue`, a `static Func` taking the constraint's member property names and their display names, both in declaration order.
Branch on the property names to single out one constraint.
A `UniquenessViolation.Message` supplied by a user-defined check wins over it.

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

## EF Core mode (GenerateEfCoreRepositories)

Generates a dialect-independent `QuickErDbContext` that puts the existing Entity onto EF Core as-is, plus an EF Core implementation of the same repository interfaces.
Migrations are out of scope, and schema creation remains the responsibility of DDL generation (EF Core connects only to an existing schema).

```csharp
// Swappable with the QuickER Repository by changing one DI-registration line
services.AddGeneratedEfCoreRepositories(options => options.UseSqlServer(connectionString));
// For SQLite / PostgreSQL / MySQL / Oracle, specify the corresponding EF Core provider's Use*
```

- Save uses a disconnected-graph save via `TrackGraph`, converting `RowState` to EF Core's state.
- Optimistic concurrency is at parity: `ConcurrencyMode` selects the policy, EF Core's `DbUpdateConcurrencyException` is converted to `SaveConflictException`, and the refreshed concurrency token is left on the entity (see [Optimistic concurrency (rowversion)](#optimistic-concurrency-rowversion)).
- The raw-SQL APIs are at full parity.
- A relationship whose referenced (principal) column is not the primary key, such as a UNIQUE column or one column of a composite primary key, is configured with an explicit `HasPrincipalKey`.
  The foreign key then joins to that column just as the QuickER Repository does, where EF Core's default would silently join it to the primary key.
- A table without a primary key is **excluded from the DbContext** (no DbSet, no Fluent configuration, and navigations to it are ignored via an explicit `modelBuilder.Ignore<T>()`), and a Warning diagnostic names the table.
  EF Core requires a key on every mapped entity type, and one keyless type in the model makes the whole DbContext throw on first use.
  This is the same line the QuickER Repository draws, whose generation skips such tables too.

Combined generation with the QuickER Repository (both ON) is for parity verification and can only be specified through the CLI or the config file; the GUI is an exclusive choice.
The EF Core Repository and multi-target QuickER Repositories (below) cannot be combined either, which is a diagnostic error.

## In-memory repositories for tests (GenerateInMemoryRepositories)

You can additionally generate an in-memory implementation for unit testing without a DB.
It implements the same contract, and unsupported operations throw `NotSupportedException` with guidance to switch to the real-DB repository.

### Known divergences from a real database

The in-memory store evaluates queries with LINQ-to-Objects rather than SQL, so a few semantics are its own rather than the database's.
They are worth knowing when a test passes in memory and fails against the real thing.

- **String comparison and ordering are ordinal.**
  Filtering (`Where`) and `OrderBy` both compare strings ordinally, so `"B"` sorts before `"a"`.
  SQL Server's default collation is case-insensitive and orders by the collation's rules instead, so a test that depends on case or accent handling is not evidence about the database.
- **UNIQUE constraints are enforced, but the exception type is not the database's.**
  A direct insert, update or bulk insert, and a graph save, are rejected when they would leave two rows holding the same values on a constraint the diagram declares.
  The rules are `CheckUniquenessAsync`'s: a value tuple containing a `null` is not compared, and the row being written is excluded by its primary key.
  Skipping null tuples follows the ANSI semantics most dialects use, where SQLite, PostgreSQL and others accept any number of NULL rows.
  A SQL Server UNIQUE constraint instead treats NULLs as equal and allows only one NULL row, and the in-memory store does not reproduce that SQL Server behavior.
  A bulk insert is validated in full before any of it is applied, and a graph save is judged by the state it would leave behind, so it can free a value on one row and take it on another within the same save, and a violation drops the save whole.
  The violation is an `InvalidOperationException`, as a duplicate primary key is, whereas a real database raises its provider's own exception (`SqlException` with error 2627, for one), so a test that branches on the exception type is not backed by the database.
  The user-defined checks added through `CollectCustomUniquenessChecks` are not enforced, being rules of the application rather than constraints the database holds, and neither is seeding through `InMemoryDataStore.Put` / `InMemorySampleData.Seed`.
- **Changing an entity's RowState from a Before hook takes effect here and nowhere else.**
  This backend reads the state again when it carries the operation out, so a `BeforeSaveAsync` that turns `Modified` into `Added` changes what happens.
  The QuickER Repository and EF Core have chosen the statement by then, and the change is ignored.
  The hook contract promises neither behaviour, so do not rewrite RowState from a hook; return `false` to skip the row instead.
- **A `SaveConflictException` can surface after the After hooks have already run.**
  Writes are staged and published as one unit, and the publish re-verifies the rows the save started from.
  That happens after `AfterSaveAsync`, since the hooks run outside the store lock.
  A real database has taken its locks long before that point, so a test that asserts "the After hook ran, therefore the save is done" holds against a database and not here.
- **Types without a rowversion column are last-write-wins under concurrent saves.**
  The publish verification only covers types that carry a concurrency token, so two saves racing on the same versionless row leave the later one's values.
  Last-write-wins stops short of resurrection, though.
  If another writer deleted the row in the meantime, the save that was updating it fails with `SaveConflictException` (`SaveConflictReason.NotFound`) rather than putting the stale snapshot back.
  A real database's UPDATE would simply affect no rows, and dropping the write silently would report a saved row that is not there.
  A staged delete for such a row is not a conflict, since deleting a row that is already gone is the same no-op it is against a database.
  Both of those rules apply to versioned types as well, because existence is settled before the version is compared.
- **`insertWhenUpdateMissing` does not cover the publish window.**
  The choice between an UPDATE and the fallback INSERT is made during the save phase, while the store lock is held.
  If another writer deletes the row after that, the save is left holding a staged update, and the publish verification reports `SaveConflictException` (`SaveConflictReason.NotFound`) instead of turning the write into an insert.
  A real database has no such window, since its statement sees the row's absence at the moment it writes, so a test that relies on `insertWhenUpdateMissing` surviving a concurrent delete is testing this backend only.

## rowversion columns and optimistic concurrency

A table with a rowversion column (SQL Server's `rowversion` / `timestamp`) is written differently in two respects.
The database assigns the value, so the repository never writes the column, and the assigned value serves as the concurrency token the save compares against.
Neither has anything to turn on, and a table without such a column keeps exactly the behaviour it had.

### Store-generated columns (rowversion / timestamp)

A column whose value the DB generates gets the marker attribute `[StoreGeneratedColumn]` on its generated Entity property, and is automatically excluded from INSERT / BulkInsert / UPDATE in the QuickER Repository.
The attribute is applied to any column the type mapper recognizes as a row-version column (SQL Server's `rowversion` / `timestamp`), regardless of the generation options.

- **Never written.**
  The DB assigns these columns' values, so the repository writes no explicit value.
  Attempting an explicit insert makes SQL Server return `Cannot insert an explicit value into a timestamp column.`, and the exclusion avoids that runtime error.
- **Read on SELECT.**
  They are included in the results of `GetByIdAsync` / `GetAllAsync` / `Query()`, and you can read their values as a concurrency token.
- **Not applied in EF Core mode.**
  The Fluent configuration's `IsRowVersion()` already treats them as store-generated.
- **They double as the table's concurrency token.**
  Saves compare the version the entity was read with against the current row (see the next section).
- **The write exclusion is SQL Server's alone.**
  Only SQL Server assigns the value, so only its engine excludes the column.
  In a multi-target build (`--repository-dialects sqlserver,sqlite`) the SQLite engine writes the same column with INSERT / BulkInsert / UPDATE like any other binary column, which is how a local copy mirrors the version the server assigned.
  See [Multi-target repositories](#multi-target-repositories-sqlserver--sqlite).

### Optimistic concurrency (rowversion)

A table that carries a rowversion column is saved with optimistic concurrency, and there is nothing to turn on.
The version the entity was read with is compared against the current row, and a save that lost the race is rejected instead of silently overwriting someone else's change.

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

`SaveConflictException` carries the material a retry needs, so the message never has to be parsed.
It exposes `Reason` (`NotFound` when the row is gone, `Modified` when the row is there but its version moved on), `EntityTypeName`, and `Key`.
The same details survive the remote transport (HTTP 409), so a caller reads the same properties against a direct and a remote repository.

The usual answer to a conflict is to reload and reapply.

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

Reload-and-reapply is the honest answer when the two edits can be merged.
`ForceOverwrite` is for when they cannot and this write is meant to win regardless: it skips the guard on the first attempt, so nothing is read back and nothing is merged.

- **A missing row and a stale version are different outcomes.**
  A single `UpdateAsync` returns `false` when the row no longer exists, which is the pre-existing contract, and throws `SaveConflictException` when the row is still there but its version moved on.
  `insertWhenUpdateMissing: true` draws the same line: a missing row switches to an INSERT, while a stale version is reported as a conflict, because switching that to an INSERT would turn the conflict into a primary-key violation.
- **A graph save guards deletes as well**, and a single conflict rolls the whole save unit back.
  The in-memory repository reaches the same result differently: it stages its writes and publishes them in one go, so a failed save simply never reaches the store.
- **The in-memory repository verifies again when it publishes.**
  Save hooks run outside the store lock, so a row the save started from may have been written by someone else in the meantime; publishing rejects that with a `SaveConflictException` and leaves the other writer's row untouched.
  A type without a rowversion column is not verified at that point, since without a concurrency token the store's contract stays last-write-wins, and `ForceOverwrite` waives the verification for the same reason.
  A row the save inserts is always verified, because a primary key taken meanwhile is a duplicate key rather than a concurrency decision.
  Whether the row is still *there* is settled before its version is, and independently of both the mode and whether the type carries a version column at all.
  A row deleted meanwhile is reported as `SaveConflictReason.NotFound`, since a version comparison against a row that no longer exists cannot say anything about it, and a staged delete for such a row is not a conflict.
- **The new version is written back.**
  After a successful insert, update, or graph save, the entity holds the version the database assigned, so the same instance can be saved again without being re-read.
  A save hook's `AfterSaveAsync` runs before the commit and therefore still sees the old version.
- **Every backend follows the same contract.**
  The QuickER Repository guards the statement with `WHERE ... AND <rowversion> = @original` and reads the new version back through an `OUTPUT` clause.
  EF Core uses its own concurrency token (`IsRowVersion()`) and converts `DbUpdateConcurrencyException` into the same exception.
  The in-memory repository emulates the database with a monotonically increasing 8-byte token.
  The HTTP remote client carries the mode in the request and writes back the versions the response returns.

#### Known limitations

- Only SQL Server has a `rowversion` type, so the QuickER Repository applies this to the `sqlserver` dialect only.
  A diagram targeting SQLite (or another dialect) alone has no such column and is unaffected.
  A multi-target build that includes `sqlserver` does share the column with the other dialects, but only the SQL Server engine guards with it (see [Multi-target repositories](#multi-target-repositories-sqlserver--sqlite)).
- `BulkInsertAsync` uses `SqlBulkCopy`, which cannot return generated values, so the entities keep whatever version they had.
  Re-read them when a later update needs the version.
- The version is read back through an `OUTPUT` clause, which SQL Server rejects on a table that has a trigger.
  QuickER's DDL generation never emits triggers, so this only affects tables whose triggers were added outside QuickER.
- Deleting a row that is already gone stays asymmetric between the backends, as it always has.
  The QuickER Repository tolerates it silently, while a graph save on EF Core reports it as `SaveConflictException`.
- A rowversion column carries no `[DbColumnMeta]` token, so C# reverse engineering does not restore it; declare it in the diagram.
- **Deleting by key is not guarded.**
  `DeleteAsync(id)` takes a key rather than an entity, so there is no version to compare it against and the row goes whatever its current version is.
  Where a delete has to lose the race it lost, mark the entity `MarkRemoved()` and save the graph, since a graph save guards its deletes with the version the entity was read with.
- Raw SQL (`ExecuteSqlAsync` and friends) and the stream accessors for unbounded binary columns are direct operations and are not guarded.

## Extending the generated base classes

Every base the generated code inherits or implements comes in two layers, across entities, edit models, value objects, repository contracts, backend implementations, and mappers.

- **`*Core`**: the fixed runtime, which holds the implementation.
  It is emitted inline by default and ships inside the `QuickER.Runtime*` packages under `--use-runtime-packages`.
- **The plainly named type**: a `partial` that QuickER emits as source, alongside the per-type code, in every output mode (`EntityBase`, `EditModelBase<TSelf>`, `ValueObjectBase<TSelf, TValue>`, `ValueObjectStringBase<TSelf>`, `IValueObject`, `IRepository<TEntity, TKey>`, `SqlServerRepository<TEntity, TKey>`, `MapperBase<TEntity, TEditModel>`, and so on).
  This is the surface you extend.

```text
EntityBaseCore                        runtime
└─ EntityBase                         generated   ← extend here
   └─ CustomerEntity                  generated

EditModelBaseCore                     runtime
└─ EditModelBaseCore<TSelf>           runtime
   └─ EditModelBase<TSelf>            generated   ← extend here
      └─ CustomerEditModel            generated

ValueObjectBaseCore<TSelf, TValue>    runtime
└─ ValueObjectBase<TSelf, TValue>     generated   ← extend here (reaches every value object)
   └─ ValueObjectStringBase<TSelf>    generated   ← extend here (one class per value shape)
      └─ NameValue                    generated

IValueObjectCore                      runtime
└─ IValueObject                       generated   ← marker every value object implements

IRemoteRepositoryCore<TEntity, TKey>  runtime
└─ IRemoteRepository<TEntity, TKey>   generated   ← extend here (default implementations only)
   └─ ICustomerRemoteRepository       generated
IRepositoryCore<TEntity, TKey>        runtime
└─ IRepository<TEntity, TKey>         generated   ← extend here (default implementations only)
   └─ ICustomerRepository             generated

SqlServerRepositoryCore<TEntity, TKey>  runtime
└─ SqlServerRepository<TEntity, TKey>   generated   ← extend here (one branch per engine)
   └─ CustomerRepository                generated

MapperBaseCore<TEntity, TEditModel>   runtime
└─ MapperBase<TEntity, TEditModel>    generated   ← extend here
   └─ CustomerMapper                  generated
```

Because the extension surface is always source, the same `partial` compiles whether the runtime is inlined or referenced as a package.
**Turning `--use-runtime-packages` on or off never makes you rewrite an extension.**
Writing a `partial` against a `*Core` type is the one thing that does not carry over, since those are compiled assembly types in package-reference mode.

### Adding members to every entity or edit model

Put the file in the same namespace as the generated types (`{RootNamespace}.Entities` in split output; the entity extension surface lands in the domain layer and the edit model one in the presentation layer under `--layered-output`).

```csharp
public interface IAuditable
{
    string AuditLabel { get; }
}

// Reaches every generated entity in the diagram.
public partial class EntityBase : IAuditable
{
    public string DescribeRow() => $"{GetType().Name}/{RowState}";

    string IAuditable.AuditLabel => $"{DisplayName} ({RowState})";
}
```

`EditModelBase<TSelf>` works the same way and additionally sees the concrete type through `TSelf`.
Repeat the type parameter list on your part (`public abstract partial class EditModelBase<TSelf>`); constraints may be omitted, because the generated part already declares them.

The edit model surface is the generic layer alone.
The non-generic plumbing underneath it is typed as the runtime base, so a reference it hands back does not see a member you added, the untyped `ParentModel` for one, and reaching it needs a cast to the concrete edit model.
Concrete edit models generate a typed `ParentModel` that hides the untyped one wherever the parent type is unambiguous, so this comes up mainly in code written against the base.

A read-write instance property added to the `EntityBase` partial is **not a column** for the QuickER repositories, the in-memory store, and the value comparisons.
A property is a column only when it carries `[Column]`, which the generator puts on every generated column property.
An added property therefore stays out of the SQL statements, out of `HasSameValues`, and out of the column copy the in-memory store makes.
A `Query()` predicate that references one fails on the SQL dialect backends with `NotSupportedException` rather than emitting a column that does not exist; the in-memory backend evaluates the predicate as ordinary C#.

Two paths look at the public properties themselves and need their own opt-out.
The JSON path (`ToJson`, `Clone`, and the remote transfer) is kept out with `[JsonIgnore]`.
EF Core maps every public read-write property including inherited ones, so under `GenerateEfCoreRepositories` an added property needs `[NotMapped]`.

To make an added property a column, give it the standard `[Column("<an existing column>")]`.
That is the opt-in, and the name has to be a column the table really has but **no generated property already maps**.
Naming a column a generated property covers puts the same column into the statements twice and fails at the database.

### Adding a member or an interface to every value object

`IValueObject` is the marker every value object implements, so it is the single place that reaches all of them regardless of value shape.

```csharp
// Injecting an interface with a default implementation means no value object writes anything itself.
public partial interface IValueObject : IAuditable
{
    string IAuditable.AuditLabel => $"{DisplayValue}";
}

// Extension methods on the marker reach all of them for the same reason.
public static class ValueObjectExtensions
{
    public static bool IsBlank(this IValueObject value) => value.UnderlyingValue is null or "";
}
```

`ValueObjectBase<TSelf, TValue>` is the class-side counterpart.
Every value object derives from it whatever its value shape, so a member added there reaches all of them, and unlike the marker it can see `Value` and `TSelf`.
Repeat the type parameter list on your part (`public partial class ValueObjectBase<TSelf, TValue>`); constraints may be omitted, because the generated part already declares them.

To reach one shape only, every string value object say, put the member on that shape's class instead (`public abstract partial class ValueObjectStringBase<TSelf> { … }`).
Those classes are generated as well, and each of them is a `partial` you may extend.

The two surfaces play different roles.
The class root `ValueObjectBase<TSelf, TValue>` is where members normally go: it is typed, and a variable of a concrete type such as `NameValue` sees it directly.
The `IValueObject` partial suits two things.
One is a default implementation that individual value objects may override with an explicit implementation, for code that handles value objects through the `IValueObject` type; the `IAuditable` example above is exactly that shape.
The other is making every value object implement an interface you use in a constraint or a DI registration.
A default interface implementation is only callable through a variable of the interface type, and a `NameValue` variable does not see it, so put anything you want to call from the concrete type on the class root.

**The generic contracts `IValueObject<TSelf>` and `IValueObject<TSelf, TValue>` are not extension surfaces**, and neither is `IStringMatchValueObject<TSelf>`.
The first two are the static-factory contract; the third is how each engine's query translator recognises the substring match of a string value object.
All three live in the runtime, so a `partial` written against them is a `partial` against a package type in package-reference mode.
Extend the non-generic `IValueObject` marker or the generated classes instead.

### Adding a member to every repository (the contract side)

`IRepository<TEntity, TKey>` is the full-featured surface every generated `I{Entity}Repository` inherits, and `IRemoteRepository<TEntity, TKey>` is the remote surface, limited to operations that cross a network boundary.
Both are extension surfaces, so a member with a default implementation added there is callable through any repository reference you resolve from DI.
That holds whatever the entity, and whatever the backend behind it: the QuickER engines, EF Core, in-memory, or the HTTP client.

```csharp
// Reaches every repository in the diagram.
public partial interface IRepository<TEntity, TKey>
{
    async Task<TEntity> GetRequiredAsync(TKey id, CancellationToken cancellationToken = default) =>
        await GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} '{id}' was not found.");
}
```

```csharp
ICustomerRepository customers = provider.GetRequiredService<ICustomerRepository>();
var customer = await customers.GetRequiredAsync(1);
```

**Only members that carry a default implementation may be added.**
A member without a body breaks every generated repository implementing that interface, because QuickER does not generate an implementation for it.

Default implementations dispatch virtually, so they keep working when a repository is wrapped in a decorator.
The `GetByIdAsync` call inside `GetRequiredAsync` above is an interface call on `this` and lands on whatever the decorator supplies.

`IRepository<TEntity, TKey>` also inherits the remote surface `IRemoteRepository<TEntity, TKey>`.
A member added to the remote surface is visible through the full-featured one as well, and additionally through code that depends on `I{Entity}RemoteRepository` alone, the HTTP client implementations included.
Put members that need expression-tree queries or raw SQL on the full-featured surface, and members that only need CRUD and save on the remote one.

### Adding a member to a backend repository base (the implementation side)

The per-backend repository bases are extension surfaces too: `SqlServerRepository<TEntity, TKey>`, `SqliteRepository<TEntity, TKey>`, `EfCoreRepository<TEntity, TKey, TContext>`, `InMemoryRepository<TEntity, TKey>`, and `HttpRemoteRepository<TEntity, TKey>`.
They are five independent branches with no shared parent, since their implementations differ all the way down.
A member added to one therefore reaches only that engine's repositories, and a solution using several engines adds it once per engine it uses.

What fits here is a `protected` helper shared by the handwritten `partial` of each `{Entity}Repository`, such as the manual implementations of named queries.
It removes the duplication without widening the surface callers see.

```csharp
public abstract partial class SqlServerRepository<TEntity, TKey>
{
    /// <summary>Boilerplate the manual query implementations share (called from each {Entity}Repository partial).</summary>
    protected Task<int?> CountBySqlAsync(string sql, object? parameters = null) =>
        ExecuteScalarSqlAsync<int?>(sql, parameters);
}
```

### Adding a member to every mapper

`MapperBase<TEntity, TEditModel>` is the base of every generated `{Entity}Mapper`, and an extension surface as well.
Repeat the type parameter list on your part (`public partial class MapperBase<TEntity, TEditModel>`); constraints may be omitted, because the generated part already declares them.

### Which surfaces have an interface, and which do not

Among the extension surfaces, only the repository contracts (`IRepository` / `IRemoteRepository`) and the value object marker (`IValueObject`) are interfaces.
Repositories are reached through an interface to begin with, and value objects need one non-generic type to gather an open generic root (`ValueObjectBase<TSelf, TValue>`) under.
Entities, edit models, and mappers are single implementations held as concrete classes, so QuickER declares no interface for them; extend the class `partial` instead.

## Value objects (GenerateValueObjects)

This option generates columns as a per-column value-object type (`CustomerIdValue` / `NameValue`, and so on) instead of a raw type such as `int` or `string` (default OFF; CLI `--generate-value-objects` / `GenerateValueObjects` in quicker.json / the "Turn all columns into value objects" checkbox in the "Value Objects" row of the GUI).
It can be chosen regardless of the DB-access selection (None / QuickER Repository / EF Core Repository), and it combines with multi-target, in-memory, and remote.

When ON, the columns of every table are grouped globally by column name, and one value-object type is generated per column name.
A foreign-key column that shares a name with a primary key shares the same type, so mixing up IDs becomes a compile error.
Entity properties and repository key types also become value objects.

```csharp
// ICustomerRepository : IRepository<CustomerEntity, CustomerIdValue>
var customer = await customers.GetByIdAsync(CustomerIdValue.Create(1));

// orders.GetByIdAsync(customer.CustomerId) is a compile error because it is not an OrderIdValue
```

When same-named columns disagree on length or precision, a Warning diagnostic is emitted, and the definitions are unified into a single type by preferring the primary key's definition, or the widest definition when there is no primary key.
**When they disagree on the underlying C# type itself it is a generation-time error**, as a `varbinary` column and a `varchar` column do.
Different value types cannot fold into one value object, so align the column types in the ER diagram or rename the columns so they stay apart.

A foreign-key column shares the referenced (principal) column's value-object type even when the column names differ, so `orders.ship_customer_id` becomes `CustomerIdValue` rather than `ShipCustomerIdValue`.
The rule expresses "the same identifier is the same value type" in the type system, and it also satisfies EF Core's requirement that a foreign key and the principal key have matching CLR types.
A differently named foreign key, inevitable in a self-referencing table's `parent_node_id → node_id`, would fail EF Core's model validation under per-column-name typing.
A foreign key of a foreign key resolves to the type at the end of the reference chain, and a column caught in a mutual-reference cycle keeps its own name-derived type.
Columns whose type was shared are listed in an Info diagnostic at generation time, because their name-derived type name changes.
A diagram where one column references multiple principals that resolve to different types is a generation-time error, and a pair whose underlying C# types disagree is not shared: the column keeps its name-derived type.

### Generated types and validation

Each value object is a `sealed partial class` whose constructor is private and which can only be created through a static factory.
Validation code is generated automatically from the column definition in the diagram: maximum length for strings, and precision and scale for `decimal`, where out-of-range values are rejected rather than rounded.

```csharp
var name = NameValue.Create("Alice");   // A validation violation throws ValueObjectValidationException

if (NameValue.TryCreate(input, out var vo, out var errors))   // Validate without throwing
{
    entity.Name = vo!;
}

var errorList = new List<string>();
NameValue.Validate(input, errorList);      // Validate without creating the value object
```

`Validate` adds any violations to the collection you pass, so it suits collecting the errors of several values in one place.
It returns true when *that call* found no violation, which stays meaningful even when the collection already holds errors from earlier values.

The base class is chosen according to the value type.
In addition to value-based equality (`==` / `Equals`), numeric and date/time types get comparison operators (`<` / `>=`, and so on), and strings get `Contains` / `StartsWith` / `EndsWith`.

### Hand-written value objects

The bodies of `Create` / `TryCreate` / `Validate` live once on `ValueObjectBaseCore<TSelf, TValue>`, because an inherited static method satisfies a `static abstract` interface member.
A value object with no diagram column behind it, such as a mail address, a period, or a unit, is therefore written with three members: a private constructor, `New`, and `ValidateCore`.

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
            (errors ??= new List<string>()).Add("Mail address must contain '@'.");
        }
    }
}
```

- The shape is exactly what the generator emits, so `ContactMailValue.Create(...)` / `TryCreate` / `Validate` work as usual, and JSON conversion, SQL parameter binding, and row materialization treat the type like any generated value object.
- A value object without rules can omit `ValidateCore` entirely and needs only two members (the interface's default implementation validates nothing).
- `ValidateCore` receives the error list by reference and unallocated.
  Allocate it only when adding the first violation (`(errors ??= new List<string>()).Add(...)`), so creating a valid value allocates nothing.
- For a reference-typed value (`string` / `byte[]`), reject `null` in `ValidateCore` when the input is not trusted.
  A value object never wraps null, since a nullable column keeps the property itself null, and the generated ones report a null input as a validation error.
  `ValueObjectRules.ValidateRequired(value, DisplayName, ref errors)`, the very call the generated ones make, does it for you and returns false when the rules that follow must be skipped.
- `New` and `ValidateCore` are explicit implementations, so they stay off the type's public surface.
  Calling `TVo.New` through a generic type parameter would skip validation, so never call `New` yourself: validation belongs to `Create` / `TryCreate`.
- Pick the base by value type: `ValueObjectStringBase` (string), `ValueObjectOrderedBase<TSelf, TValue>` (numbers and date/time), `ValueObjectBooleanBase`, `ValueObjectBinaryBase`, `ValueObjectGuidKeyBase` (GUID-as-string keys), or `ValueObjectBase<TSelf, TValue>` directly for anything else.

### Creating from a raw value (CSV and spreadsheet imports)

`TryCreateFrom` and `CreateFrom` build a value object from a value that is not already the underlying type, such as a CSV field, a spreadsheet cell, or a form field.

```csharp
// Read the cell (a string, a double, a DateTime - whatever it holds) in a given culture
if (QuantityValue.TryCreateFrom(cell, culture, out var quantity, out var errors))
{
    entity.Quantity = quantity;      // a blank cell leaves quantity null, and that is not an error
}

var amount = AmountValue.CreateFrom(cell, culture);   // throwing version; a blank cell returns null
```

`IValueObject<TSelf>` does not name the underlying type, so import code needs no branch per value object.

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

- **A blank cell is not a violation.**
  `null`, `DBNull`, and an empty string mean "not filled in": the call returns `true` with a null result and adds no errors, matching the rule that a nullable column keeps the property itself null.
  Whether the field is required is the edit model's required check to decide.
  A type can claim absent inputs as a value of its own through the `ConvertAbsentInput` hook described below, in which case the result is that instance instead of null.
  A whitespace-only string is not absent and goes through the ordinary conversion.
- **The culture is the caller's.**
  `null` means the invariant culture.
  To read a date a person wrote (`28/08/2026`), pass the culture it was written in.
- **Numeric text may carry group separators.**
  A spreadsheet hands back `1,234` for a column formatted that way, so numeric targets are parsed with an explicit `NumberStyles`.
  An integral target still rejects a decimal point, so `1,234.5` does not pass as an `int`.
- **Binary text is read as Base64**, the same notation the edit models' binding input accepts, so the screen and an import never disagree on the spelling.
  A `byte[]` value passes through as is.
- Past the conversion this is the ordinary `TryCreate`, so the type's own validation applies unchanged: maximum length, precision, and `OnValidate`.
- The message for a value that could not be converted is `ValueObjectValidationMessages.InputNotConvertible`.
- Overloads without the culture (`TryCreateFrom(raw, out var value, out var errors)` and `CreateFrom(raw)`) use the invariant culture, which is what machine-produced data wants, such as a serialized payload or a fixed export format.
  Text a person wrote or a spreadsheet formatted belongs to a culture, so pass one: `CreateFrom(raw, provider)` / `TryCreateFrom(raw, provider, ...)`.

### Accepting only declared instances (enumeration-like value objects)

A concept whose values are a closed set, such as a mode, a category, or a status, can be a value object that only ever hands back the instances it declares as `static readonly`.
The declarative form is an attribute on the fields.

```csharp
// Extend the generated StatusValue (an int column) into an enumeration - no other code needed
public sealed partial class StatusValue
{
    [DeclaredInstance, Display(Name = "Preparing")]
    public static readonly StatusValue Preparing = new(1);

    [DeclaredInstance, Display(Name = "In progress")]
    public static readonly StatusValue InProgress = new(2);

    [DeclaredInstance, Display(Name = "Completed")]
    public static readonly StatusValue Completed = new(9);

    public override string DisplayValue => DeclaredDisplayName ?? base.DisplayValue;
}
```

The marked fields form the type's whole set of values.

- **Every creation path returns the declared instance itself**, whether through `Create` / `TryCreate`, a database read, or a JSON restore.
- **Any other value is rejected as a validation error.**
  The wording is replaceable through `ValueObjectValidationMessages.ValueNotDeclared`, where the display name and the rejected value arrive as arguments like every other message.
- `StatusValue.GetDeclaredInstances()` lists the instances in declaration order, a ready-made source for a selection list.
- A `[Display(Name = ...)]` on the field is available as the protected `DeclaredDisplayName`, which makes the `DisplayValue` override above a one-liner.
- A convenience constant the set should not contain is simply left unmarked.

The fields have to take a particular shape.
Each one must be `public static readonly` of the declaring type, and it must be initialized with the private constructor (`new(...)`, which the partial class can reach) rather than `Create`.
`Create` validates, and validation consults the declared set that is still being built at that point.
A field of any other shape, and a duplicate value, are reported on first use.

One shape cannot be detected, though.
**A field left without an initializer, and therefore permanently null, cannot be told apart from a field whose initialization is still running**, so the set silently stays inactive and every creation rescans.
The compiler flags such a field (CS8618 / CS0649); do not leave those warnings unresolved.

The attribute works on a hand-written value object the same way, and it is not supported on a binary (`byte[]`) value object.

A declared instance can also claim its input notation.
`InputText` reads that exact string input as the instance, and `ClaimsAbsent` claims the absent input (null, `DBNull`, an empty string).
A notation like "a mark or a blank cell" therefore closes in two declarations, reading (input) and display (`[Display]`) together.

```csharp
// A flag column written as "○" or a blank cell: ○ → Marked, blank → Unmarked, display via [Display]
public sealed partial class MarkValue
{
    [DeclaredInstance(InputText = "○"), Display(Name = "○")]
    public static readonly MarkValue Marked = new(true);

    [DeclaredInstance(ClaimsAbsent = true), Display(Name = "")]
    public static readonly MarkValue Unmarked = new(false);

    public override string DisplayValue => DeclaredDisplayName ?? base.DisplayValue;
}
```

- Matching is exact and ordinal (no trimming, no case folding, no culture) and applies to string inputs only, ahead of the ordinary conversion.
  A non-string input (`true`, `9`) and a convertible string (`"True"` / `"9"`) take the ordinary conversion as before, and still resolve to the same declared instance.
- The notations apply to `TryCreateFrom` / `CreateFrom` only; an edit model's blank input and a database NULL are unchanged.
- A `[Display]` name is display-only and is never read as input, since an accepted notation is declared explicitly with `InputText`.
- At most one field may declare `ClaimsAbsent`.
  A duplicate `InputText`, and an empty `InputText`, are reported on first use.
  An empty one can never match, because an empty input is absent first, and `ClaimsAbsent` is what was meant.
- The hooks below (`ConvertCustomInput` / `ConvertAbsentInput`) win over the declared notations.
  Normalization, aliases, and anything an exact match cannot express stay their territory.

Those hooks remain for the shapes the attribute cannot express, such as a lookup with logic of its own (normalization, aliases) or a per-type error wording, and they compose with it.
`GetDefinedInstance` is consulted ahead of the declared set, and an `OnValidate` runs in addition to the membership check.
A generated type's `New` cannot be replaced and its `TryGetDefined` is already emitted as a bridge, so the extension goes into the partial methods the bridges consult.
That keeps it self-contained in the user's partial, with no generator option involved.

```csharp
// Extend the generated ModeValue (an int column) into an enumeration through the hooks alone
// (the table, the lookup and the rejection are all yours)
public sealed partial class ModeValue
{
    public static readonly ModeValue List = new(1) { ModeName = "List" };
    public static readonly ModeValue Edit = new(2) { ModeName = "Edit" };

    // Built once: the hooks below run on every creation (per column per row when rows are read), so keep them allocation-free
    private static readonly Dictionary<int, ModeValue> Defined =
        new[] { List, Edit }.ToDictionary(x => x.Value);

    public string ModeName { get; private init; } = string.Empty;

    public static IEnumerable<ModeValue> GetList() => Defined.Values;

    static partial void GetDefinedInstance(int value, ref ModeValue? defined) =>
        defined = Defined.GetValueOrDefault(value);

    // GetDefinedInstance alone does not reject an undefined value - it only falls through to New. Write the pair
    static partial void OnValidate(int value, ref List<string>? errors)
    {
        if (!Defined.ContainsKey(value))
        {
            (errors ??= new List<string>()).Add($"Screen mode {value} is not defined.");
        }
    }
}
```

> **Note**: built from hooks, the rejection does not come for free. `GetDefinedInstance` only decides what to return for a value that already passed validation, so without the `OnValidate` above a value outside the set quietly falls through to `New` and "only declared instances" no longer holds. And never call `Create` / `TryCreate` / `TryCreateFrom` from inside a hook: every creation path runs through it, so the call recurses with no way to catch the resulting stack overflow.

Like the attribute, the hooks sit on the `Create` / `TryCreate` side.
A row read from the database and a value restored from JSON therefore return the declared instance too, and an instance missing its extra state (`ModeName` here) never gets into circulation.

A hand-written value object has no partial hooks; it implements the corresponding interface hooks directly.
`GetDefinedInstance` maps to `TryGetDefined`, `OnValidate` to `ValidateCore` (that one is the validation body itself), and, below, `ConvertCustomInput` to `TryConvertCustomInput` and `ConvertAbsentInput` to `TryConvertAbsentInput`.
Only the hook names differ; the content and the warnings are the same.

To create from a name instead of the value, implement the `ConvertCustomInput` partial hook.
`TryCreateFrom` / `CreateFrom` consult it ahead of the ordinary conversion, on every call shape: the generic import path and a call spelled with the concrete type name alike.
Set the result to claim the value; anything left null falls through to the ordinary conversion, which keeps `2` working.
For the fixed one-string-per-instance case, the declarative `InputText` above does the same without a hook, and the hook is for matching that needs logic.

```csharp
static partial void ConvertCustomInput(object raw, IFormatProvider? provider, ref ModeValue? result)
{
    if (raw is string name)
    {
        result = GetList().FirstOrDefault(x => x.ModeName == name);
    }
}
```

The generic import code from the previous section (`ReadCell<ModeValue>`) now accepts both `"Edit"` and `2`.
A hand-written value object implements the interface hook `TryConvertCustomInput` directly, per the mapping above.
In either form, do not call `TryCreateFrom` / `CreateFrom` from inside, because they consult the hook and the call would recurse.
Do not throw either: `TryCreateFrom` reports failures through its return value, so an exception would ride straight through that contract.

Implement the hook only, never `TryCreateFrom` itself, on generated and hand-written types alike.
Re-implementing `TryCreateFrom` compiles, but a call spelled with the concrete type name binds to the shared base implementation and silently skips it on that call shape, which is exactly why the extension point is the hook.

An absent input (`null`, `DBNull`, an empty string) never reaches `ConvertCustomInput`, because `TryCreateFrom` answers "success with a null result" before consulting it.
A type whose notation writes one of its values as a blank, such as a flag written as a mark or nothing, claims the blank half of that notation through the `ConvertAbsentInput` partial hook instead.
A hand-written type implements the interface hook `TryConvertAbsentInput` directly, the same split as above, and for a fixed blank-means-this-instance rule the declarative `ClaimsAbsent` above does the same, with the hook winning when both are present.
The culture is passed for symmetry, but an absent input carries no text, so implementations usually ignore it.

```csharp
// The notation writes the flag as "○" or a blank cell: ConvertCustomInput claims the mark,
// and ConvertAbsentInput claims the blank - imported as False instead of null
static partial void ConvertAbsentInput(IFormatProvider? provider, ref MarkValue? result) =>
    result = False;
```

The hook applies to `TryCreateFrom` / `CreateFrom` only.
An edit model's blank input still leaves the confirmed value null and is reported by its required check, and a database NULL still reads back as a null property.
The hook decides what a blank import cell means, not what null means everywhere.
The warnings above apply unchanged: never call `TryCreateFrom` / `CreateFrom` from inside, and do not let an exception escape.

### partial extension points

Every generated class offers two ways to customize messages and display names, and the rule is the same across every static class and every generation mode (inline or package-reference).

- **Bulk**: replace a static settable `Func` on the fixed infra at app startup.
  It applies everywhere.
- **Per-type**: branch inside the replacement you installed.
  Every value object message takes the value object's display name as its first argument, every edit model message takes the confirmed-value property name, and `GeneratedDisplayNames.Resolve` takes the member name, so one replacement covers both "all types" and "this type only".

```csharp
// Bulk, at startup: localize messages, and stop using descriptions for display names
ValueObjectValidationMessages.ValueRequired = static _ => "値を入力してください。";
EditModelMessages.Required = static (_, displayName) => $"{displayName}は必須です。";
GeneratedDisplayNames.Resolve = static (name, _) => name;   // ignore descriptions; use the member name
```

```csharp
// Per-type display name: branch on the member name the resolver is given.
// nameof keeps the branch compiling against the generated code, so a renamed column is a build error.
GeneratedDisplayNames.Resolve = static (memberName, description) =>
    memberName == nameof(CustomerEntity.Name) ? "Full name" : description ?? memberName;

// Per-type message: branch on that type's own DisplayName, which follows the resolver above.
ValueObjectValidationMessages.MaxLengthExceeded = static (displayName, maxLength, actualLength) =>
    displayName == NameValue.DisplayName
        ? $"A name is at most {maxLength} characters ({actualLength} given)."
        : $"Enter at most {maxLength} characters. (currently {actualLength} characters)";
```

```csharp
public sealed partial class NameValue
{
    // Additional validation (called after the auto-generated validation). The list arrives unallocated;
    // allocate it only when adding the first violation, so a value that passes allocates nothing.
    static partial void OnValidate(string value, ref List<string>? errors)
    {
        if (value.Contains(' '))
        {
            (errors ??= new List<string>()).Add("Whitespace is not allowed.");
        }
    }
}
```

```csharp
// Per-property message tweak: the resolver receives the confirmed-value property name first,
// so a branch can be written with nameof and stops compiling if that property is renamed away.
EditModelMessages.ParseFailed = static (propertyName, displayName, inputValue, typeName) =>
    propertyName == nameof(CustomerEditModel.Age)
        ? $"'{inputValue}' is not a valid age."
        : $"'{inputValue}' cannot be converted to {typeName}.";
```

The replacements live on three static classes.
In package-reference mode, all three ship inside the `QuickER.Runtime` package.

| Static class | Members | First argument |
|---|---|---|
| `ValueObjectValidationMessages` | `MaxLengthExceeded` / `ScaleExceeded` / `PrecisionExceeded` / `ValueRequired` / `DigitsExceeded` / `OutOfRange` / `InvalidCharacters` / `InvalidEmailAddress` / `InputNotConvertible` / `ValueNotDeclared` | The display name |
| `EditModelMessages` | `Required` / `ParseFailed` / `DuplicateValue` / `JoinValueObjectErrors` | The confirmed-value property name, or the list of them, on the first three |
| `GeneratedDisplayNames` | `Resolve` | The member name (it resolves the display name of entities, edit-model properties, and value objects alike) |

The per-type partials are `OnValidate` on a value object, plus `GetDefinedInstance` / `ConvertCustomInput` covered above.
An edit model has the semantic hooks only (`OnValidate`, `OnBeginEdit` / `OnEndEdit` / `OnCancelEdit`, `On{Property}Changing` / `Changed`), since wording and display names are resolved centrally.
An entity has none, for the same reason.

An entity's `DisplayName` is resolved through `GeneratedDisplayNames.Resolve`, which receives the runtime class name and the table description, so branch on the class name there to substitute one (`static (memberName, description) => memberName == nameof(CustomerEntity) ? "Customer" : description ?? memberName;`).
The default resolver prefers the table description, so writing the display name into the diagram is often all it takes.

A value object's display string `DisplayValue` (virtual) can also be overridden.
Value objects implement `IFormattable` as well: `price.ToString("N2")`, and the culture-taking overload, formats the underlying value.
A format specifier in string interpolation, `string.Format`, or a WPF binding's StringFormat reaches the underlying value the same way.
Without a format specifier the result is always `ToString()`, so an override of `ToString()` (a handwritten one, or the Base64 form of binary value objects) also governs unformatted interpolation.
The same applies when the underlying value is not formattable (a string, a byte array, a bool): the format is ignored.
`DisplayValue` is the type's own display form, and `ToString(format)` renders whatever format the caller asks for.

### Validation rules you can call yourself

The rules the generated `ValidateCore` runs are public static methods on the fixed infra, and so are four more that no column declaration can express.
Call them from `OnValidate`.
Each one takes the error list by reference and possibly unallocated, so a value that passes still allocates nothing.

```csharp
public sealed partial class ContactMailValue
{
    static partial void OnValidate(string value, ref List<string>? errors) =>
        ValueObjectStringRules.ValidateEmailAddress(value, DisplayName, ref errors);
}

public sealed partial class ProductCodeValue
{
    static partial void OnValidate(string value, ref List<string>? errors)
    {
        // ASCII letters and digits, plus the two symbols this code allows
        ValueObjectStringRules.ValidateAsciiAlphanumeric(value, "-_", DisplayName, ref errors);
    }
}

public sealed partial class QuantityValue
{
    static partial void OnValidate(int value, ref List<string>? errors)
    {
        ValueObjectNumberRules.ValidateRange(value, 1, 999, DisplayName, ref errors);   // closed interval
        ValueObjectNumberRules.ValidateMaxDigits(value, 3, DisplayName, ref errors);    // sign not counted; 0 is one digit
    }
}
```

| Rule | What it rejects |
|---|---|
| `ValueObjectRules.ValidateRequired(value, displayName, ref errors)` | `null`. Returns false so the caller can stop, because every rule after it dereferences the value |
| `ValueObjectStringRules.ValidateMaxLength(value, maxLength, displayName, ref errors)` | Longer than the limit in UTF-16 code units (`string.Length`). A database may count differently, as Oracle's BYTE semantics do |
| `ValueObjectStringRules.ValidateAsciiAlphanumeric(value, allowedSymbols, displayName, ref errors)` | Anything but ASCII letters and digits, plus the characters in `allowedSymbols` (pass `""` for none). A full-width letter or digit is rejected |
| `ValueObjectStringRules.ValidateEmailAddress(value, displayName, ref errors)` | Text that is not shaped like an address: exactly one `@`, a non-empty part on each side, no whitespace. Deliberately not RFC 5322. `MailAddress` is not used either, because it also parses the `Name <a@b>` form |
| `ValueObjectNumberRules.ValidateMaxDigits(value, maxDigits, displayName, ref errors)` | An integer written with more digits than the limit (the sign does not count; zero is one digit) |
| `ValueObjectNumberRules.ValidateRange(value, minimum, maximum, displayName, ref errors)` | A value outside the closed interval; any `IComparable<T>`, so dates work too |
| `ValueObjectDecimalRules.Validate(value, precision, scale, displayName, ref errors)` | More decimal places than `scale`, or more integer digits than `precision - scale`. Never rounds; trailing zeros count toward the scale |

### Integration with each feature (transparent support)

Value objects can be handled transparently throughout the generated code.
You rarely need to unwrap them to the raw value by hand.

| Feature | Behavior |
|---|---|
| QuickER Repository | SQL parameters are automatically converted to the wrapped value before binding, and reads restore the value object via `Create`. |
| `Query()` (expression tree) | Value-object comparisons, string `Contains`, and so on are translated directly to SQL. |
| EF Core mode | The Fluent configuration automatically applies a value conversion (`HasConversion`) and a translation plugin (server-side translation of string methods and `.Value` references). |
| Named query | Method parameters stay the raw type. The generated condition expression is converted to value-object comparisons automatically (IN lifts the list). |
| EditModel | The committed-value property is a value object such as `NameValue?`. The screen-binding property `BindingXxx` (string) validates with `TryCreate` and surfaces errors through `INotifyDataErrorInfo`. |
| JSON (`ToJson` / `Clone` / remote transfer) | Serialized **as the wrapped value** (`{"customerId": 1}`. The value object's wrapper structure does not appear in the JSON). |

> **Note**: reads from the DB or from JSON are also validated through `Create`. If existing data holds a value that does not pass validation, the read throws `ValueObjectValidationException`, so keep the extra validation you add in `OnValidate` consistent with existing data.

### GUID keys for string primary keys (UseGuidKeyForStringPrimaryKey)

Turning on `UseGuidKeyForStringPrimaryKey` (CLI `--use-guid-key-for-string-primary-key` / the GUI's "Use GuidKey for string primary keys") together with `GenerateValueObjects` makes the value object of a string primary key derive from a GUID-generating base class (`ValueObjectGuidKeyBase`).
A parameterless `Create()` then mints a new key.

```csharp
// When document_id is a string primary key
var id = DocumentIdValue.Create();   // A new key wrapping Guid.NewGuid() as a string
```

This lets you satisfy the "primary keys are application-assigned" prerequisite of repository generation without writing any key-generation logic.

Length validation still applies, exactly as it does for any other string value object.
The value object carries the column's declared width, so `Create` and `TryCreate` reject a value longer than it.
The generator leaves `[MaxLength]` off the entity property precisely because the value object owns that check.

The parameterless `Create()` always mints 36 characters (`Guid.NewGuid().ToString()`).
The option applies to every string primary key in the diagram, so it also turns a deliberately short key into a GUID key, a five-character code column for instance.
Generation warns by name for any such column narrower than 36 characters and then continues.
Auto-numbering on that column always fails length validation at run time, while assigning a short key explicitly keeps working.

Keys compare ordinally, so the comparison is case-sensitive.
QuickER never generates a `DEFAULT` clause, but if you add `DEFAULT NEWID()` to the column outside QuickER, SQL Server stores uppercase GUIDs while the application mints lowercase ones.
Mixing the two breaks key matching.

## Excluding unbounded binary columns (ExcludeUnboundedBinaryColumns)

This option avoids round-tripping a huge BLOB on every list fetch or update, which protects memory (default OFF; CLI `--exclude-unbounded-binary-columns` / the "Do not fetch unbounded binary columns (varbinary(max) / BLOB)" checkbox in the GUI, shown only when the DB-access selection is QuickER Repository / `ExcludeUnboundedBinaryColumns` in quicker.json).
When ON, the marker attribute `[UnboundedBinaryColumn]` is applied to the Entity property of a binary column with no size limit, and that column is excluded from SELECT / UPDATE in the QuickER Repository.
At generation time, the list of excluded columns is reported through an Info diagnostic (CLI output, or the GUI's generation-result dialog).

The decision is made from the column's declared type.
Types with a declared length, such as `rowversion`, `binary(n)`, or `varbinary(n)`, are not targeted.

| Dialect | Excluded | Not excluded (bounded) |
|---|---|---|
| SQL Server | `varbinary(max)` / `image` | `binary(n)` / `varbinary(n)` / `rowversion` |
| SQLite | `BLOB` with no declared length | `BLOB(n)` |
| PostgreSQL | `bytea` | — |
| MySQL | `BLOB` / `MEDIUMBLOB` / `LONGBLOB` | `TINYBLOB` / `BINARY(n)` / `VARBINARY(n)` |
| Oracle | `BLOB` / `LONG RAW` | `RAW(n)` |

The key behaviors are these.

- **Excluded from SELECT.**
  In the results of `GetByIdAsync` / `GetAllAsync` / `Query()`, an excluded column is `null`, because it is not read from the DB.
  That holds unless you opt in with `WithUnboundedBinary()`, described below.
- **Excluded from UPDATE.**
  An excluded column is not in the SET clause of the update SQL.
  Running `UpdateAsync` / `SaveAsync` while an excluded column still holds a value throws a runtime exception rather than silently dropping data.
- **INSERT / BulkInsert keep all columns**, so the first write can pass values as usual.
- **Edit models and mappers treat an absent value as "keep what is there".**
  An excluded column is left out of the required-input check, and the mapper writes it to the entity only when the edit model actually holds a value.
  An ordinary fetch leaves the column unfetched, so the usual round trip (fetch, edit the other columns, `ApplyToEntity`, save) passes validation, leaves the column out of the UPDATE, and keeps the stored blob.
  Fetching with `WithUnboundedBinary()` and then saving through the mapper still throws: the entity holds a real value, which is exactly what the UPDATE guard is there to catch.
- **A NOT NULL excluded column is required input while the row is new.**
  "Keep what is there" has nothing to keep on a row that does not exist yet, and INSERT keeps every column, so a missing value goes in as `NULL` and the database rejects it.
  While the edit model is `RowState.Added`, such a column is therefore checked for missing input like any required field, and the error names the column.
  Once the row exists the check withdraws itself, because the column is out of scope for UPDATE again.
  For a new row you can either put the real blob into the edit model, since INSERT keeps every column, or take the two-step route below (insert first, stream the body in afterwards) by putting an empty value in: `editModel.Thumb = ThumbValue.Create([])` with value objects, or `editModel.Thumb = []` without them.
  An empty value is not a missing one, so it passes the check, and it is what the entity of a non-value-object column starts out with anyway.
  The confirmed-value setter is `internal`, so that line belongs in the assembly the generated code lives in; from outside it, build the entity with `mapper.CreateEntity()`, put the empty value on it, and insert that.
- **A named-query projection** that references an excluded column does fetch it, because a projection is an explicit column selection.
- It can be fetched by explicitly SELECTing it in **raw SQL** (see the operational example below).
- **Not applied in EF Core mode** (queries via `DbSet` / `SaveChanges`), because column selection in EF Core is EF Core's responsibility.
- The in-memory repository (`GenerateInMemoryRepositories`) has parity with a real DB, that is, the same exclusion behavior.
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

> **Note**: copying data between databases needs care. `GetAllAsync` followed by `BulkInsertAsync` writes the excluded columns exactly as the fetch left them, which is `null`, or an empty array for a non-nullable column. Since INSERT keeps every column, nothing throws and the blobs are quietly gone at the destination. The UPDATE guard does not cover this: it fires on an excluded column that still holds a value, which is the opposite of what a copy carries. Read with `Query().WithUnboundedBinary()` instead, or copy the rows first and then move each blob separately with the `Read/Write{Column}Async` stream accessors. The latter is also the only way that does not hold a whole blob in memory.

### Read opt-in: `WithUnboundedBinary()`

Even for a diagram where exclusion is enabled, when you want to fetch the entity including the excluded column for this call only, splice `WithUnboundedBinary()` into the `Query()` chain.
The API always exists, because it is a no-op when there are no excluded columns.
It lets you fetch an ordinary entity (`RowState = Unchanged`, with the excluded column mapped to its real data) without writing a raw-SQL projection.

```csharp
// Fetch the GetById equivalent, including the excluded columns (payload / thumb)
var doc = await documents
    .Query()
    .Where(d => d.DocumentId == 1)
    .WithUnboundedBinary()
    .FirstOrDefaultAsync();
```

Constraints and behavior:

- **Cannot be combined with `Include`**, which throws `InvalidOperationException` when the terminal method runs.
  If you need the unbounded binary column, fetch it with a separate query that has no `Include`.
  SQL Server's `Include` path goes through FOR JSON and Base64, which inflates memory for a huge BLOB by 5 to 6 times at the peak, so this restriction keeps the memory profile predictable for the "handle a huge BLOB" purpose.
  On SQL Server the opt-in fetches with a plain SELECT rather than FOR JSON.
- The effect applies to the entity-shaped fetches `ToListAsync` / `FirstOrDefaultAsync`, and to `ToProjectionListAsync` when the projection falls back to materializing the entity in full: a selector whose columns cannot be extracted, or one combined with `Include`.
  It does not affect count, existence check, or a projection whose columns are pruned server-side, because that projection already fetches exactly the columns it references, excluded ones included.
- The fetched entity is a legitimate entity, but the fact that the excluded column is out of scope for UPDATE does not change.
  Calling `UpdateAsync` on it as-is throws from the existing guard; update an excluded column with the raw SQL `ExecuteSqlAsync` above.
- In EF Core mode it is a no-op because EF Core reads all columns to begin with.
  Only the `Include`-combination error is thrown identically, for parity.

### Stream accessors: `Read/Write{Column}Async`

When you enable the exclusion option and generate the QuickER Repository, streaming read/write methods are additionally generated per excluded column.
Where they are placed depends on whether remote contracts exist; see below.
They transfer between the DB and a stream (or file) in O(chunks), without loading the entire blob into memory, instead of a bulk `byte[]` read.
Among the generated APIs, this is the option that keeps memory bounded for GB-scale binaries.

```csharp
// Example generated for documents.payload (an excluded column)
Task<bool> ReadPayloadAsync(int id, Stream destination, CancellationToken ct = default);
Task<bool> WritePayloadAsync(int id, Stream? source, long? length = null, CancellationToken ct = default);
// File convenience methods (extension methods; delegate to the Stream version)
Task<bool> ReadPayloadToFileAsync(int id, string path, CancellationToken ct = default);
Task<bool> WritePayloadFromFileAsync(int id, string path, CancellationToken ct = default);
```

Semantics:

- **Return value.**
  `Read` returns `true` once it has written to the destination, and an empty blob is also `true`; no row or a NULL column returns `false` and writes nothing to the destination.
  `Write` returns `true` if it could update, `false` if there is no row.
  This matches the bool convention of the existing `UpdateAsync`.
- **`Write(id, null)`** sets the column to `NULL`, which is how an excluded column is reset to "unset".
- **Length.**
  It is automatic when `source` is `CanSeek` (`Length - Position`); otherwise the `length` argument is required, and an omission throws `ArgumentException`.
  SQLite's `zeroblob` requires the length before writing, and the contract is unified to be dialect-neutral.
- **Optimistic concurrency (rowversion and the like) is out of scope**, since this is direct column manipulation on par with raw SQL.
- **There is no INSERT-only method.**
  Write a new row in two steps: INSERT with the blob left empty, then stream in the body with `Write{Column}Async`.
  On a nullable column, `null` is what "left empty" means.
  On a NOT NULL column it has to be an actual empty value (`ThumbValue.Create([])` with value objects, `[]` without them), because the database rejects a `null` insert, and that is also what the edit model's required check asks for while the row is new.
- **Cannot be used in EF Core mode** (`NotSupportedException`).
  Because EF Core is dialect-independent by design, it cannot have dialect-specific streaming.
  Use the QuickER Repository, or implement it in a `partial` class; in a configuration that combines `GenerateEfCoreRepositories` with the QuickER Repository, only the EF Core implementation throws.
- **Placement.**
  If remote contracts (`--generate-remote-contracts` / `--generate-remote-services`) are disabled, they sit directly on the full-featured repository interface `I{Entity}Repository`.
  If enabled, they move to the remote surface `I{Entity}RemoteRepository`; the full-featured interface inherits the remote surface, so calling code is the same in either configuration and the change is purely additive.
  The file convenience methods follow the same target interface.
  Enabling remote services (`--generate-remote-services`) lets them transfer over HTTP (see "Binary transfer endpoints" below).

Choosing between it and `WithUnboundedBinary()`:

| | `WithUnboundedBinary()` | Stream accessor |
|---|---|---|
| Unit | Entity shape (multiple columns, multiple rows, no Include) | Read/write of a single column |
| Memory | Moderate (bulk `byte[]`) | **Bounded**: constant regardless of blob size (O(chunks)) |
| Use case | You temporarily want an entity including the excluded columns | Transfer a huge blob between the DB and a file or stream |
| Write | Not possible (fetch only; update with raw SQL) | Can write per column with `Write{Column}Async` |

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

A `rowversion` column resolves to different C# types per dialect (`byte[]` on SQL Server, a date/time or an unknown type on SQLite), and the shared Entity can only have one.
QuickER unifies it to the row-version resolution, `byte[]` with `[StoreGeneratedColumn]`, and reports the columns it unified through an Info diagnostic instead of failing with a type-mismatch error.
The two sides then mean different things, and that difference is the point.

| Side | What the column is | Writes | Version guard |
|---|---|---|---|
| SQL Server (server) | A concurrency token the database assigns | Excluded from INSERT / BulkInsert / UPDATE; the assigned version is read back onto the entity | Yes: a stale version raises `SaveConflictException` |
| SQLite (local copy) | An ordinary binary column | Written by INSERT / BulkInsert / UPDATE like any other column | **No**: the write goes through whatever the entity holds |

That makes the column the natural place for a local copy to mirror the server's version.
Read a row from the server, version included, store it locally as-is, and later send the mirrored version back as the guard value of the server-side update.
A row created locally simply has no version yet, which is why the dialect switch also lifts NOT NULL on that column (see [Dialect switching](database.md#dialect-switching)).

#### Known limitations

- **The local side is not protected.**
  SQLite runs no version guard, so two local writers still overwrite each other.
  The version is data there, not a lock.
- **Nothing keeps the mirror fresh.**
  The column holds whatever was written last.
  If a sync is skipped, a later push using that value is rejected by the server as a conflict, which is the intended outcome: reload and reapply.
- **`ForceOverwrite` is a no-op on the local side**, since there is no guard to waive.
- The EF Core Repository cannot be combined with a multi-target build (a diagnostic error), so the mirroring described here applies to the QuickER Repository.

Keeping the mirror in step is a job you can write yourself against these two repositories, or hand to [Bidirectional sync support](#bidirectional-sync-support---generate-sync-support), which generates it.

## Bidirectional sync support (--generate-sync-support)

The multi-target build above gives a local copy a place to mirror the server's version.
`--generate-sync-support` generates the machinery that actually keeps the two in step, with the server as the source of truth (`GenerateSyncSupport` in quicker.json, or the "Generate bidirectional sync support" checkbox in the GUI, shown once both target databases are selected).

It has three prerequisites.

- Exactly the two dialects `sqlserver` (the server) and `sqlite` (the local database)
- The QuickER Repository implementations
- At least one table that can sync at all, meaning one a repository contract is generated for, with a single primary-key column

**Every such table takes part, and what the `rowversion` column decides is not membership but the mode.**
A table with the column syncs incrementally under version guards, which is the default story this section tells.
A table without one takes part only in [last-write-wins runs](#versionless-tables-and-last-write-wins-syncmodelastwritewins).
The tables picked up are listed in an Info diagnostic at generation time, with the versionless ones named as such.
What the column means on each of the two sides is described under [Row version columns in a multi-target build](#row-version-columns-in-a-multi-target-build); this section is about the machinery built on top of it.

### What it puts where

The server gets no extra schema at all.
The local database gets two shared tables, created on first use with `CREATE TABLE IF NOT EXISTS`.
`quicker_sync_journal` records offline edits (table, key, operation, and for a delete the version the row carried), and `quicker_sync_ack` records, per row, the version the server stamped on an upload it took.

The resume point is derived, not stored.
It is the highest mirrored version among the local rows, so there is no bookkeeping row that can drift out of step with the data.
Three properties make that derivation correct, and all three are load-bearing.

- Server changes are fetched in ascending version order
- Each batch is applied in a single local transaction
- **The download is the only thing that writes a mirrored version**

A run interrupted anywhere leaves the local database holding a prefix of the ordered stream, and the maximum of that prefix is exactly where the next run resumes.
Rows created locally and not yet uploaded have no mirrored version, so they drop out of the maximum on their own.

The third property is what keeps the first two worth anything.
A version written from anywhere but the ordered stream can stand above rows of that stream the local database has not seen yet, and each of those rows then sits below the resume point for good.
An upload therefore does not mirror the version the server stamps on the row it sends.

It writes that version to `quicker_sync_ack` instead.
**The receipt is about one row; it is not a resume point**, and nothing derives one from it, which is why it can be written where a mirrored version cannot.
It answers the question the mirror cannot answer between an upload and the echo that follows it: what version the server actually holds for a row this device has already sent.

Two things read it.

- **The replay of a further local edit** guards against the later of the mirrored version and the acknowledged one.
  Reading the mirror alone makes a row whose echo has not come down look older than the server's copy of *itself*, and the version guard then fires against a version this very device wrote, a conflict with nobody.
  It is also what tells an offline insert apart from a row already accepted but not yet echoed: both have an empty mirror, and only the second has a receipt.
  The same applies to a delete, whose journal entry carries the mirror as of the moment the row was deleted.
- **The download** recognizes a row carrying exactly the acknowledged version as this device's own echo, and takes only the version from it, since the content is the row's own.
  A row someone else has changed since carries a different version and is applied like any other change.

A receipt is written *before* the journal entry it settles is removed.
It goes once the mirror has caught up with it: after the local commit that wrote the version, when delete propagation removes the row, and wholesale when `RefreshAsync` rebuilds the table.
`SyncJournal.RemoveTableAsync` and `RemoveAllAsync` deal with journal entries only, since dropping unsent local changes says nothing about what the server has already taken.

An echo that has not come down is no longer a state that has to be cleared up before the next edit.
Calling `UploadAsync` and `DownloadAsync` separately is therefore as sound as calling `SyncAsync`, because the receipts outlive the call that wrote them.

The upper bound of a pass is `MIN_ACTIVE_ROWVERSION()`, taken once per run.
A row committed later can carry a lower version than one committed earlier, so reading up to "the current maximum" would step over rows that are still uncommitted and never come back for them.

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
| `AddGeneratedSyncSupport(serverServiceKey, localServiceKey)` | The two halves above for the all-direct setup, the common case when both databases are reachable from the same process |

Every key argument accepts `null`, which means the ordinary non-keyed registration rather than a key whose value is null.
A keyed registration cannot be made with a null key, so the two never collide.
Registering the local half without a server half is not a silent failure: resolving the engine then fails on the missing source.

A run uploads first and downloads second, and visits tables in foreign-key order, parents first when rows are written and children first when rows are deleted.
`SyncOptions` is what you turn.

| Option | Default | Meaning |
|---|---|---|
| `Mode` | `Versioned` | The run's semantics. The default syncs the versioned tables incrementally under version guards and leaves the versionless ones alone (the pre-existing behaviour); `LastWriteWins` is [last-write-wins](#versionless-tables-and-last-write-wins-syncmodelastwritewins) |
| `ExcludedEntityTypes` | empty | Entity types this run leaves out. Recording continues, and the next run that covers them replays what accumulated (excluding a table permanently is the construction-time `excludeFromSync` instead). A type the engine does not synchronize is rejected with `ArgumentException` |
| `DownloadBatchSize` | 500 | How many rows one download batch fetches and applies in a single local transaction |
| `PropagateDeletes` | `true` | Whether to delete local rows whose key no longer exists on the server. The check compares the full key set, which costs one key-only pass over each server table; turn it off and run it on a slower schedule when the tables are large. A key the journal still holds an unsent entry for is spared, as described below |
| `ConflictPolicy` | `Collect` | How a local change that collides with the server is treated (see [Conflicts](#conflicts)) |
| `IncludeUnboundedBinary` | `false` | Whether to carry the unbounded binary columns the row transfer leaves out (see [Unbounded binary columns](#unbounded-binary-columns)). Nothing changes for a build without such columns |

`SyncResult` reports what the run did.

| Member | Meaning |
|---|---|
| `Uploaded` | Local changes that reached the server |
| `Downloaded` | Server rows applied locally |
| `DeletedLocally` | Local rows removed because the server no longer has that key |
| `Discarded` | Changes settled without being sent: a stale intent whose row is no longer there, or everything the journal held under `ServerWins` |
| `Conflicts` / `HasConflicts` | The local changes that could not be replayed; they stay in the journal |
| `Truncations` / `HasTruncations` | The tables whose download stopped before the end of the server's changes (see [Conflicts](#conflicts)) |

**A journal entry is settled the moment the server takes it**, one entry at a time rather than all of them at the end of the run: its receipt is written, then the entry is removed.
An upload interrupted part way therefore leaves entries for exactly the changes that did not reach the server.
Settling them together at the end would leave entries for changes that did, and the next run would resend them, either reporting a conflict against the row this run wrote itself or putting a stale local row over a newer server one.

**Delete propagation spares the rows the journal still speaks for.**
Before it removes anything it reads the journal, and a key with an unsent entry is left alone, including one this same run has just reported as a conflict.
That is what keeps `Collect` from resolving a "the server no longer has this row" conflict in the server's favour by deleting the row.
Once the entry is settled, whether uploaded, discarded as a stale intent, or dropped by `ServerWins`, a later run propagates the deletion as usual.
The protection reaches exactly as far as the journal does: a row written into a synchronized table by some other route has no entry, and while propagation is on it is deleted (see [Known limitations](#known-limitations)).

Replaying one change has three outcomes, not two, and `Uploaded` / `Discarded` / `Conflicts` are exactly those three: sent, nothing to send, refused.
Folding the middle one into either of the others would report a change as delivered when nothing crossed the wire.
`Uploaded` and `Discarded` count rows rather than journal entries, since a row edited several times offline collapses into its latest intent.
The exception is `ServerWins`, where nothing is sent at all and `Discarded` is the number of entries dropped.

### Reaching the server over HTTP

Combining `--generate-sync-support` with [`--generate-remote-services`](#remote-services---generate-remote-services) adds a client that reaches the server over HTTP instead of a database connection.
Swapping one for the other is a change of one registration line and of nothing else, because the engine resolves the same `ISyncServerSource<TEntity, TKey>` either way.

```csharp
// Client: local repositories as before, but the server half now speaks HTTP
services.AddGeneratedSqliteRepositories(serviceKey: "local", sqliteConn);
services.AddGeneratedHttpSyncSources("https://example.com/quicker");   // or an HttpClient factory
services.AddGeneratedSyncEngine(localServiceKey: "local");

// Server: the ordinary repositories, the sources the endpoints answer from, and the endpoint group
services.AddGeneratedSqlServerRepositories(sqlServerConn);
services.AddGeneratedDirectSyncSources(serverServiceKey: null);
app.MapGeneratedRemoteEndpoints(RemoteAccess.RequireAuthorization);
```

Three endpoints join the existing group: `SyncCeiling`, `SyncChanges`, and `SyncKeys` under `POST {prefix}/{entity}/…`.
They are a thin remoting of the differential source, where each handler resolves `ISyncServerSource<,>` from DI and calls it, so the meaning lives in one implementation that both transports share.
`MapGeneratedRemoteEndpoints` checks that registration for every synchronized table while it maps them.
A server that forgot `AddGeneratedDirectSyncSources` therefore fails at startup naming the sources it lacks, rather than starting, answering every CRUD call, and failing only when a client first syncs.
Being group members, they are covered by the `RemoteAccess.RequireAuthorization` chosen at mapping time, and by any policy added to the group afterwards.
Uploads add nothing new: they go through the ordinary CRUD and save endpoints, which already carry `ConcurrencyMode` and already turn a version conflict into a 409.

The server keeps no per-client state.
The resume point travels as the request's anchor and the upper bound as its ceiling.
The flip side is that the bound is the caller's value, so the guarantee behind the derived anchor holds only as long as you send back the ceiling `SyncCeiling` returned for that same pass.
A hand-made larger ceiling permanently skips the rows of transactions running below it.
A batch size of zero or less is refused with 400, and there is no upper limit, because what stops a client from asking for everything is the group's authorization.

### How local edits are captured

The generated `Journaling{Entity}Repository` wraps the local repository and records every write entry point: `InsertAsync`, `UpdateAsync`, `DeleteAsync`, `BulkInsertAsync`, and both `SaveAsync` overloads.
A save hook would not do, because it only fires for a graph save and a direct insert or delete would pass it by.

A graph save records the whole cascade.
`SyncGraphRecorder` walks the same cascade navigations the graph saver walks, literally the same enumeration (`EntityBaseCore.EnumerateCascadeChildren`), under the same rules.
The descendant rows the save writes or deletes are therefore journaled exactly like the root: a save that edits only a child under an unchanged root is not missed, and neither are the children a cascade delete takes along.
A table on the path that is not synchronized is walked through without an entry of its own.
The whole graph is recorded before the save runs, so entries left behind by a failed save are settled harmlessly at upload, just as they are for a single write.

The record is written **before** the business write.
The generated repositories manage their own connection, so a decorator cannot enlist its INSERT in the transaction of the write it wraps.
Something has to go first, and recording the intent first is the safe order.
If the business write then fails, the journal holds an entry for a row that was never written, and the upload discards it, because it re-reads the current local row and finds nothing.
The opposite order would lose changes outright.

The same re-read protects a delete whose business write failed.
An entry whose local row is still there says the delete never landed, whether a foreign key refused it or a lock timed out, and the entry is discarded unsent rather than carried out on the server.
Without that check a failed local delete would complete itself on the server, and the delete propagation of the same run would take the surviving local row with it.

**Raw SQL is not recorded.**
`ExecuteSqlAsync` forwards untouched: the statement's shape is opaque to the decorator, so there is no key to journal.
The same holds for the bulk delete behind `Query().ExecuteDeleteAsync`, whose rows are chosen by a predicate the decorator never sees.
Rows changed either way reach the server only if something else records them.
A row *created* that way fares worse than that: with no journal entry to spare it and no key on the server, delete propagation removes it on the next run.
A row the local database is meant to keep to itself belongs in a table excluded from sync where the engine is put together, through the `excludeFromSync` argument of `AddGeneratedSyncEngine`.

**Save hooks are unaffected.**
The decorator delegates to the repository it wraps, so `ISaveHook<T>` fires exactly as it did before, including for the rows the engine itself applies during a download, since a sync run suppresses journaling and nothing else.
A refresh (below) writes through `BulkInsertAsync`, which is outside the save pipeline and fires no hooks, in keeping with the ordinary contract.
One consequence is worth knowing: a write a `BeforeSaveAsync` returned `false` for still leaves a journal entry, because the entry is written first.
For an insert that entry is discarded, since there is no row to read, and for an update the row is uploaded as it stands, with unchanged content, which the server accepts and stamps with a new version.

### Unbounded binary columns

Combining `--generate-sync-support` with [`--exclude-unbounded-binary-columns`](#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns) is allowed, and the excluded columns of a synchronized table are named in an Info diagnostic at generation time.
They need saying, because the row a run reads and writes does not contain them: the differential SELECT lists the remaining columns explicitly, and an UPDATE never touches an excluded column.

By default (`IncludeUnboundedBinary = false`) that has three consequences, and the third is the one that surprises.

- A row **downloaded** from the server arrives without its blob.
- A row **uploaded** to the server is sent without its blob.
- A blob **already stored** on the receiving side survives, because an update does not touch the column.
  **But a row that is new to that side has nothing to keep and arrives with the column empty.**
  "The blob is preserved" is true of rows that are already there and false of rows that have just arrived, so a first sync into an empty local database leaves every blob empty.

Setting `SyncOptions.IncludeUnboundedBinary` copies each such column separately after its row has been transferred, in both directions.

```csharp
var result = await engine.SyncAsync(new SyncOptions { IncludeUnboundedBinary = true }, ct);
```

The copy streams through a temporary file, so neither side holds the blob in memory.
The read pushes bytes into a stream and the write pulls them out of one, and the file is what joins the two while also supplying the length the write needs up front.
That file is created in the operating system's temporary folder (`Path.GetTempPath()`) and deleted once the column has been written.
This is worth knowing when the blobs are sensitive: it is the one place where they land on disk unencrypted without the caller choosing where, unlike the file convenience methods of the [streaming accessors](#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns).
Over HTTP it reuses the existing `GET`/`PUT`/`DELETE {prefix}/{entity}/{column}?id=` endpoints, the ones the [streaming accessors](#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns) already use, so nothing new is mapped.

Two details follow from copying columns separately.

- **A NULL source clears the destination.**
  The point of carrying these columns is that both sides end up alike, so a row whose server copy has no blob loses the local one rather than keeping a stale copy.
- **After an upload the server's version is read again.**
  Writing a blob is a write to the row, so the server moves the version on past the one the insert or update handed back.
  Reporting the stale value to the run would leave the download unable to recognize the row as its own echo, and it would be applied, blob copied back down, as if the server had changed it.

**A blob written on its own is tracked.**
`Write{Column}Async` and the file convenience method go through the journaling decorator like every other write and record their intent first, so an offline edit that changes nothing but a blob still reaches the server.
The recording does not depend on `IncludeUnboundedBinary`, because what to send is decided when sending, not when generating.
A run left at the default uploads the row without the blob and settles the entry.

The cost is one round trip per column per changed row, which is why it is off by default.
A table of large blobs whose rows change often pays for it on every run.

### Conflicts

Nothing is resolved silently.
Under the default policy a local change that collides with the server stays in the journal and comes back in `SyncResult.Conflicts` with the table, the key, the operation, the reason, and both sides' rows attached.
The local row stays where it is, including when the reason is `MissingOnServer`, because both halves of the download leave every key the journal still holds an entry for alone: the row is not overwritten by the server's copy, and delete propagation does not remove it.

**Leaving such a row alone stops that table's download there, and the tables after it with it.**
The download applies a table's rows in ascending version order, so skipping one row and carrying on would move the resume point past it, and the change that was being protected would never be seen again.
It stops at that row instead.
Every table after it in foreign-key order is left for the next run too, because the rows it would bring down can point at parents that were behind the stopping point.
What was left unread is reported.

```csharp
foreach (var stop in result.Truncations)
{
    // stop.TableName, and exactly one of:
    //   stop.PendingKeyText        - the unsent local change this table stopped at
    //   stop.TruncatedByTableName  - the table whose stop this one waited for
}
```

The rows behind the stopping point are still on the server and come down as soon as the entry that stopped them is settled, whether decided and re-run, or dropped with `SyncJournal.RemoveTableAsync`.
What a run stops for is an unsent journal entry.
That is usually a conflict it has just collected, but it is equally an entry an interrupted upload left behind.
A run that settles every entry reports no truncations.

| `SyncConflictPolicy` | What happens |
|---|---|
| `Collect` (default) | The entry stays in the journal and is reported; re-run after deciding |
| `ServerWins` | The run's entries are dropped and each of their rows is read back from the server over the local one (entries of tables outside the run's scope, versionless or excluded ones, stay put) |
| `LocalWins` | The change is resent with `ConcurrencyMode.ForceOverwrite`, overwriting the server row, and inserted again when the server has no such row, so a row deleted on the server is resurrected by a local edit (the same resolution applies to a key the server already holds under a local insert) |

**`ServerWins` reads the rows back rather than leaving them to the download.**
The download carries a row only when its version has moved since it was last mirrored, so a local edit to a row nobody else has touched would otherwise outlive the run that was told to discard it, and with its journal entry gone, no later run would correct it.
Every discarded entry's key is therefore read from the server and written over the local row, which also brings back a row that was deleted locally.
A key the server no longer holds has nothing to apply and is left to the delete propagation, so with `PropagateDeletes` turned off such a row stays where it is.

**A restored row keeps its mirrored version.**
The copy read from the server carries the server's current version, and writing that into the mirror would raise the resume point to it, past every change this device has not fetched yet, none of which a later run would ask for again.
The content is applied and the version is left alone, so the next download reaches the row in its proper version order and mirrors it then; applying the same content twice costs nothing.
A row that was put back has no mirrored version at all until that happens, which is the state a locally created row starts in.

**An applied row also gets a receipt.**
With the mirror left where it was, nothing about the row says that its local content is the server's content at the version just read.
For a row restored under a version the ordered stream has already passed, no download will ever say so either.
The key and the server's version therefore go into `quicker_sync_ack`, the same receipts an upload writes, and a later edit to that row reads its original from there.
Without them such an edit is replayed with no version to guard it, as a row that has never been on the server, which the server answers by already having it.

### Versionless tables and last-write-wins (SyncMode.LastWriteWins)

A table without a `rowversion` column has no change stream to scan and no original version to guard with.
Last-write-wins mode is the answer for when such a table should sync anyway, such as a small, rarely changing master table you want to distribute without adding a column to the server.

```csharp
var result = await engine.SyncAsync(new SyncOptions { Mode = SyncMode.LastWriteWins }, ct);
```

**A default (`Versioned`) run never touches a versionless table**: no download, no upload, no delete propagation.
Recording continues, and a last-write-wins run collects what accumulated.
Adding a versionless table to a diagram therefore changes nothing about what the default runs do.

A `LastWriteWins` run covers every table and gives the whole run one semantics.

- **Uploads are uniform across all tables**: update with `ForceOverwrite`, insert when the update finds no row, delete unconditionally.
  No version is read, no existence is probed, and nothing is ever reported as a conflict (`Conflicts` stays empty and `ConflictPolicy` is ignored).
  Versioned tables overwrite without their guards too.
  The version the server stamps is still reported to the run, so the same run's download recognizes the row as its own echo and takes nothing but the version from it.
- **The winner is whoever uploads last, not whoever edited last.**
  An update lost to the crossing is neither detected nor reported, and that is the trade this mode names.
- **A delete crossed with an edit resurrects the row.**
  A row deleted on the server while it was being edited offline comes back when the upload's update finds nothing and inserts.
  A local delete likewise removes whatever the server did to the row meanwhile.
  Consistent, as last-write-wins goes.
- **A versionless table downloads as a full key-ordered scan on every run** (`SELECT TOP … WHERE key > @afterKey ORDER BY key` paging, plus the ordinary delete propagation).
  The cost is O(table) per run, which is why this mode is meant for small, rarely changing tables, and why a large or busy table is better served by adding a rowversion column and riding the incremental side.
  Versioned tables still download incrementally in this mode; the result is the same, the transfer just smaller.

A table that should never sync, holding local-only data, is declared where the engine is put together rather than per run.

```csharp
services.AddGeneratedSyncSupport("server", "local", excludeFromSync: [typeof(LocalCacheEntity)]);
```

A construction-excluded table gets no journaling decorator, so nothing is recorded and writes cost nothing extra, and no descriptor, so no run downloads it, propagates deletes into it, or wipes it in a refresh.
A type that is not a synchronized entity type, or an exclusion that leaves no table at all, is rejected at registration with `ArgumentException` rather than discovered at the first run.
`SyncOptions.ExcludedEntityTypes` sits at a different altitude: it takes a table out of one run, while recording continues and the next covering run replays the backlog.

Entries recorded before a table was excluded are never replayed, keep counting as unsent changes, and block a refresh for ever.
Journal maintenance exists for exactly that.
`SyncJournal.RemoveTableAsync(tableName)` drops one table's entries and `RemoveAllAsync()` drops them all.
Both are the explicit statement that those local edits will never reach the server, and neither should run while a sync is in flight.

One topological note.
A versionless table referencing a versioned one is kept consistent inside a single last-write-wins run by the foreign-key ordering.
**But a foreign key between a construction-excluded table and a synchronized one is nobody's job to protect.**
An application may legitimately maintain the excluded side by hand, so this is a documented caution rather than a block.

### Rebuilding the local database (RefreshAsync)

`SyncEngine.RefreshAsync` empties every synchronized table and reloads it from the server.
It is for building the local database the first time, recovering one that was lost or corrupted, and starting over when a database has fallen so far behind that catching up row by row is not worth it.
It is not for the incremental case, which is what `SyncAsync` is for.

```csharp
var refreshed = await engine.RefreshAsync(new SyncRefreshOptions { BatchSize = 2000 }, ct);

// refreshed.Tables holds per-table Deleted / Inserted counts in the order rows were written;
// refreshed.Deleted / .Inserted are the totals, and .Elapsed is the wall time of the run
```

Unsent local changes are refused rather than lost.
When the journal is not empty the run throws `SyncPendingChangesException` **before deleting anything**, with a per-table breakdown (`PendingChanges`, `PendingCount`), so the caller can upload them with `SyncAsync` first and refresh afterwards.
`SyncRefreshOptions.Force` is the explicit request to drop them instead, and `SyncRefreshResult.DiscardedChanges` reports how many went.

Local blobs are refused on the same terms.
When a synchronized table has [unbounded binary columns](#unbounded-binary-columns), which the row transfer leaves out so the reload does not bring them back, the run throws `SyncUnboundedBinaryLossException` **before deleting anything**, naming the columns per table.
Two flags answer it, and one of them has to be set.

| `SyncRefreshOptions` | Default | Meaning |
|---|---|---|
| `Mode` | `LastWriteWins` | Which tables the refresh covers. **The default takes everything**: a refresh rebuilds the local database as a copy of the server's, and leaving the versionless tables out would keep stale rows pointing at parents the wipe removes, a foreign-key failure whenever a versionless table references a versioned one. `Versioned` narrows it to the versioned tables, which is sound only while no versionless row references a table being rebuilt. The unsent-changes refusal and the forced discard follow the scope |
| `ExcludedEntityTypes` | empty | Entity types this refresh leaves out (the same per-run scope as on `SyncOptions`) |
| `IncludeUnboundedBinary` | `false` | Copy each excluded column back down after its row has been written, making the rebuilt database a complete copy |
| `DiscardLocalUnboundedBinaries` | `false` | Accept the loss, the right answer when the blobs are a local cache that can be rebuilt. It permits the loss; it does not reload anything |

A versionless table reloads in ascending key order rather than version order, the same paging its download uses.
The resume property holds in the same shape, so a refresh cut short is still repaired by running it again.

A generated setup without such columns never sees this exception, so nothing changes for it.

`BatchSize` defaults to 2000, several times the download batch of an ordinary run.
Each batch is one local transaction, a refresh gains most of its speed there, and an interrupted run is repaired by running it again, so a fine resume granularity is worth less here.
The cost of raising it is memory, and over HTTP the size of one response body; a table with large binary columns is the case for lowering it.

What makes a refresh fast is what it leaves out: nothing is compared with the row it replaces, no anchor is derived per batch, no key set is fetched, and no journal is replayed.
Measured on a two-table, 20,000-row diagram at the shipped defaults, it runs about 3 to 4.5 times faster than an ordinary run, the upper end with both databases local and around 3x with a real SQL Server as the server.
The ceiling on that ratio is structural, because reading the rows out of the server is work both paths do.

**It is not a cheaper `SyncAsync`.**
It transfers every row of every synchronized table, so over a slow link and a large table the transfer dominates, and an ordinary run, which carries only what changed, is the cheaper one from the second run onwards.
Tables the local database keeps for itself, anything excluded with `excludeFromSync` where the engine was put together, are not part of it and are left exactly as they were.

**The run is not one transaction.**
The generated repositories manage their own connections, so nothing here can enlist in a transaction of its own making.
What holds instead is that every point at which it commits is a state a later run can start from.
Deletes go children first and reloads parents first, so no foreign key ever points at a row that is not there, and each table's rows arrive in ascending version order, so a table interrupted part way holds exactly the rows below the version it stopped at, which is the resume point the local maximum yields anyway.
The state this leaves that a single transaction would not is a partly rebuilt local database: between the first delete and the last row of the last table, a reader sees fewer rows than either side holds.
Nothing is lost by it, but a refresh is not something to run underneath a live screen.

### Known limitations

- **Writes that do not go through the repository are not tracked.**
  That includes raw SQL (`ExecuteSqlAsync`), as described above, and anything that reaches the local database by another route.
  The journal only sees the write entry points the decorator wraps, and what it does not see it cannot protect.
  A row created that way is not merely left unsent: it is **deleted on the next run** while `PropagateDeletes` is on, because the server has no such key.
  Keep local-only rows in a table excluded with `excludeFromSync` where the engine is put together.
- **Last-write-wins does not detect lost updates.**
  A crossing is silently won by whoever uploads last, and a delete crossed with an edit resurrects the row.
  Give a table a rowversion column when its conflicts should be detected (see [Versionless tables and last-write-wins](#versionless-tables-and-last-write-wins-syncmodelastwritewins)).
- **Unbounded binary columns are not carried unless you ask for them** (`SyncOptions.IncludeUnboundedBinary`), and carrying them costs one round trip per column per changed row.
  See [Unbounded binary columns](#unbounded-binary-columns).
- **Cannot be combined with the EF Core Repository**, since the sync support requires a multi-target build and that combination is already exclusive.
- **The HTTP transport requires `--generate-remote-services`.**
  Without it the direct sources are generated and the engine still works, but there is no client or endpoint to reach the server with.
- **The local side has no version guard**, as under [Row version columns in a multi-target build](#row-version-columns-in-a-multi-target-build).
  Two local writers still overwrite each other, and the engine's conflict detection is about the server's version, not theirs.
- **There is one window a crash can still fall into**: between the server accepting an upload and the receipt being written for it.
  The journal entry survives, and the next run replays it as a version-guarded update against a mirrored version the server has already moved past.
  It is reported as a conflict, with the row this device itself wrote on the other side of it.
  Resolving it with `LocalWins` sends the same local content again.
- **A row deleted on the server and recreated under the same key can conflict when `PropagateDeletes` is off.**
  The receipt of such a row is dropped when delete propagation removes the local row; with propagation off nothing removes it.
  A local row recreated under that key is then replayed as an update against a version of a row that is gone, and reported as `MissingOnServer`.
  The same applies when the local row is removed by a route the generated repositories do not see.
- **Suppression is ambient to the async flow, so work started outside it is still journaled.**
  `SyncSession.Suppress`, what keeps the engine's own writes out of the journal, is an `AsyncLocal` counter.
  Work a save hook starts and awaits inside that flow inherits it, `Task.Run` included.
  What does not inherit it is work handed to a flow that began somewhere else, such as an existing worker loop, a channel consumer, or a timer callback.
  Writes made there are recorded as ordinary local edits, and the rows the engine has just applied are then uploaded straight back on the next run.
  The mirror image is a fire-and-forget task started inside the scope: it keeps the suppression after the scope ends, so its later writes are never recorded at all.
  Do the work inside the hook's own flow.
- **Keys are matched as text, ordinally.**
  The journal and the receipt table hold the key as a string (a `byte[]` key as uppercase hex, a `Guid` by `ToString()`), and every comparison the engine makes (pending keys, receipts, delete propagation) is ordinal and case-sensitive.
  The server, by contrast, looks its rows up with ordinary SQL equality on the key column, under that column's collation.
  On a case-insensitive column, then, `ABC` and `abc` are one row to the server and two separate journal and receipt entries locally.
  Keep the case of a text key stable, the same way [GUID keys](#guid-keys-for-string-primary-keys-useguidkeyforstringprimarykey) have to be.
- The runtime package for this is `QuickER.Runtime.Sync`.

## Remote-capable interfaces (--generate-remote-contracts)

`I{Entity}Repository` is a full-featured interface that, in addition to CRUD, save, and named queries, has every method including `Query()` (expression-tree query), raw SQL, and bulk insert.
Specifying `--generate-remote-contracts` additionally generates an interface for remote operations (`GenerateRemoteContracts` in quicker.json, or the "Generate Repository interfaces for remote operations" checkbox in the GUI's "Remote" row).

| Surface | Interface | Operations included |
|---|---|---|
| Remote surface (additionally generated) | `I{Entity}RemoteRepository` | CRUD (GetById / GetAll / Insert / Update / Delete), graph save (Save), named queries |
| Full-featured surface (as before) | `I{Entity}Repository` (inherits the remote surface) | The above plus `Query()` (expression tree), the three raw-SQL variants, bulk insert |

Every method of the remote surface has arguments and return values composed purely of data (entities, primary keys, counts), so it can in principle cross a network boundary.
If you keep the application body dependent only on the remote surface, the compiler catches any use of an operation that cannot cross the boundary, even when you later swap the repository's implementation for a web-service-backed remote one.
Processing that needs an expression tree or raw SQL just uses `I{Entity}Repository` as before, so "this part needs a direct DB connection" is readable from the type.

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

This option is purely additive.
Turning it ON leaves `I{Entity}Repository`, the implementation classes, and the DI implementation registrations unchanged.
The remote surface is merely added to DI as a forward to the same instance, so you can enable it at any time without breaking existing code, and `AddGenerated*Repositories` resolves either surface.

## Remote services (--generate-remote-services)

Specifying `--generate-remote-services` generates a client and server implementation that provides the remote surface over the network using HTTP + JSON (`GenerateRemoteServices` in quicker.json, or the "Generate HTTP client / server implementations" checkbox in the GUI's "Remote" row).
The remote surface `--generate-remote-contracts` is enabled automatically.

| Output | Location | Contents |
|---|---|---|
| HTTP client implementation | Bundled into the main output (the only dependency is the BCL `HttpClient`) | `Http{Entity}RemoteRepository` (implements `I{Entity}RemoteRepository`) plus `AddGeneratedHttpRemoteRepositories` |
| Server implementation | `{baseName}.RemoteServer.g.cs` (a separate file) | `MapGeneratedRemoteEndpoints` (Minimal API; `POST {prefix}/{entity}/{operation}`; prefix default `/quicker`) |

The recommended project layout is a shared class library, holding the main output of entities, contracts, and the client implementation, referenced by both the server (ASP.NET Core) and the client app (WPF, for instance), with only the server file placed in the server project.

```csharp
// ---- Server (ASP.NET Core, Microsoft.NET.Sdk.Web) ----
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGeneratedSqliteRepositories(connectionString);   // The real implementation can be the QuickER Repository or the EF Core Repository

var app = builder.Build();
// Whether the group requires authorization is stated explicitly - the RemoteAccess argument has no
// default (see the notes below). 500 responses hide the server-side error detail unless you say
// otherwise; passing IsDevelopment() gives you the real message while developing and the generic one
// in production
app.MapGeneratedRemoteEndpoints(
    RemoteAccess.RequireAuthorization,
    exposeErrorDetails: app.Environment.IsDevelopment()
);
app.Run();

// ---- Client app (switch direct ⇔ remote with one DI-registration line) ----
// Direct: services.AddGeneratedSqliteRepositories(connectionString);
// Remote: services.AddGeneratedHttpRemoteRepositories("https://server:5001/quicker");
// The application body injects and uses IOrderRemoteRepository either way (no code change)
```

A working example is in the repository at [samples/ec-order-remote](../samples/ec-order-remote/README.md).
It runs exactly this recommended layout as three projects across two real processes, and it also demonstrates remote transfer of named queries and type restoration of `SaveConflictException`.

### Authorization and the network boundary

**The generated endpoints assume a trusted network.**
Authorization covers the group as a whole and nothing finer: there is no per-row or per-tenant filtering anywhere in the generated code.
Any caller that gets past `RemoteAccess` can read, write and delete every row of every table a repository is generated for, which is every single-primary-key table of the diagram.
Narrowing that down, to one tenant's rows or to the rows a user owns, is yours to add, as a policy layered onto the returned `RouteGroupBuilder` or as your own endpoints in front of the generated ones.

The amplification to weigh while you do is `Save`.
A graph whose root carries `RowState.Removed` deletes the descendants loaded with it, whatever state *they* carry, when `cascadeDelete` is on, which it is by default.
Nothing in the generated code caps how many nodes one request may carry, so a single request can empty out everything under a master row.
Treat the server as an internal service behind authentication rather than as a public API.

Authentication and TLS are out of scope.
Whether the endpoints demand authorization is a required argument, `RemoteAccess`, with no default.
The wire format accepts every column an entity has, the primary key included, so the endpoints can read, write and delete any row a caller can name, and a surface with that power should not be opened or closed by a value nobody wrote, in either direction.

- `RemoteAccess.RequireAuthorization` applies the host's default authorization policy to the whole group, the health endpoint included.
  The generated code can only **demand** authorization, never **provide** it, so the host must have authentication and authorization configured or every request fails.
- `RemoteAccess.AllowAnonymous` states that this mapping itself demands nothing and attaches no metadata at all.
  It deliberately does not attach `[AllowAnonymous]`, so a host-level fallback authorization policy that secures everything by default keeps covering the generated surface.
  Use it for local development, or when authorization is layered on elsewhere.
- An undefined value fails fast at mapping time with `ArgumentOutOfRangeException`.

On the client, configure an authentication-handler-equipped HttpClient via `AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)`.

**The policy covering the group as a whole rather than hand-picked endpoints is deliberate**, because `Save` is as powerful as `Delete`.
A graph whose nodes carry `RowState.Removed` deletes those rows, so a policy that guards `Delete` and leaves `Save` open guards nothing.
Further policies can be layered onto the returned `RouteGroupBuilder`.

### What travels over the wire

Serialization uses the same semantics as the entity's JSON round trip (`ToJson` / `Clone`): a value object as the wrapped value, RowState included, and parent-reference navigation that does not cycle.
The client and server share `RemoteJson.Options`.
A cycle cannot arise in the generated model, since parent-reference navigations carry `[JsonIgnore]`.
Should one arise anyway, with `IncludeJsonIgnoreOnParentNavigation` turned off or a navigation of your own that closes a loop, `RemoteJson.Options` sets `ReferenceHandler.IgnoreCycles`, which writes `null` where the cycle closes rather than throwing.
The transfer then succeeds with that navigation silently missing on the receiving side.

Named queries can all be called through the remote surface regardless of implementation method (simple DSL, raw SQL, or manual implementation), and the real implementation lives in the server-side repository.

After a successful graph save (Save), the local RowState is also committed, the same behavior as the direct case.

**Optimistic concurrency travels over the wire.**
The `ConcurrencyMode` argument is part of the Update / Save request, and the Insert / Update / Save responses carry the row versions the save assigned, keyed by entity type and primary key, which the client writes back onto the local graph.
A remote client therefore ends up holding the same versions a direct connection would, and can keep saving the same entities without re-reading them.

### How errors are classified

**Exception types are restored.**
The server's `SaveConflictException` is thrown on the client as `SaveConflictException` too, via HTTP 409, so the same catch as in the direct case works.
Other server exceptions become `RemoteRepositoryException`, preserving the status code; what happens to the message is described below.

Two more cases are classified the same way.

- **A success response whose body is not the expected JSON.**
  Something other than the generated endpoint answered, most often a proxy's or a portal's 200 page, and that is a transport failure.
  It therefore belongs in the same catch as every other remote failure rather than surfacing as a raw `JsonException`.
- **A body that is the JSON literal `null`**, on every operation whose result is never null (`GetAll`, the save operations, the list and count queries).
  `null` is valid JSON, so without the check it would slip through and surface later as an obscure `NullReferenceException` far from the call.
  A 200 carrying `null` from an operation whose result is legitimately null (`GetById`, a single-row query, a nullable scalar query) stays what it always was: no such row.

**A request the server cannot interpret is answered with 400, not 500.**
Anything that fails while the request itself is being read is a fault in what the client sent, so it returns HTTP 400 with a `RemoteError` of type `"BadRequest"`, and the client throws `RemoteRepositoryException` with `StatusCode` 400.
That covers a malformed or empty JSON body, a non-JSON content type, a type mismatch, a value that fails value-object validation, a body that omits a required field (`{}` sent to `Insert` / `Update` / `Save` / `SaveMany`, or a reference-type key omitted from `GetById` / `Delete`), an undefined `ConcurrencyMode` value, a named query's paging arguments outside what the query pipeline accepts (`take` of zero or less, a negative `skip`), and a missing or unrestorable `?id=` key on the binary endpoints.

Neither the server-side logging nor the `OnServerError` hook runs for a 400, since both are reserved for 500.
**The message is not redacted the way a 500's is: `exposeErrorDetails` covers 500 only.**
What it carries is whatever the request-reading step failed with, verbatim.
For a malformed body that is the serializer's own text, the JSON path and byte position where it gave up, plus the name of the .NET type when the failure is a type mismatch; for a value-object violation it is that validation message.
Those describe the caller's payload rather than the server's state, but they do name internals, which is one more reason this surface belongs on a trusted network.
A request rejected by the server infrastructure (`BadHttpRequestException`, for example when the request body size limit is exceeded) keeps the status code it carries, such as 413.

The generated client never produces the paging form of that 400.
It rejects the same values up front, with the `ArgumentOutOfRangeException` a direct implementation raises (`SqlQuery.Skip` / `Take`, parameter `count`), so switching between the two implementations does not change the exception a caller catches.
The server-side 400 remains for hand-written callers.
The rejection applies to every named query with paging, whatever its implementation method: a raw-SQL or manually implemented query could reasonably treat `take: 0` as "no rows", but the remote surface answers 400 for it uniformly, exactly as the direct path raises `ArgumentOutOfRangeException` for it uniformly.

**What a 400 covers is the shape of the request, not the content of the entity.**
An entity body that deserializes cleanly is not validated on the way in.
A payload like `{"Entity":{}}` passes the envelope check, because the `Entity` field is present, reaches the repository with its non-null properties unset, and fails as a database constraint violation, which is a 500.
That is like every other content fault the server can only learn from the database: a foreign key pointing at a missing parent, a unique-constraint collision, an overflowing value.
This line is deliberate.
Classifying a missing property alone as 400 would split that class in two, and validating content up front would duplicate the database's rules on the server, including the columns where an unset value is legitimate, such as an excluded binary column or a rowversion.
Generated clients always serialize every property, so only a hand-written caller can produce such a payload in the first place.

**A 500 response hides the server-side error detail by default.**
The body carries a fixed message (`An unexpected error occurred on the server.`) plus a `CorrelationId`, which the client surfaces as `RemoteRepositoryException.CorrelationId`.
The full exception, including the stack trace, always goes to the server side through `ILoggerFactory` (category `QuickER.RemoteServer`, a no-op when the host has no logging provider), with the same correlation id in the log line.
A caller who reports the id therefore lets you find the complete record without the internal message, such as table and column names, connection strings, or file paths, ever crossing the trust boundary.

Pass `MapGeneratedRemoteEndpoints(exposeErrorDetails: true)` to send the message verbatim instead, in which case `CorrelationId` is null and the body is exactly what earlier versions sent.
The idiomatic form is `exposeErrorDetails: app.Environment.IsDevelopment()`, which is why it is a runtime argument rather than a generation-time option: one set of generated code covers both environments.
The switch changes only what the client sees on a 500.
Server-side logging and the `OnServerError` hook always receive the exception itself, and the classified responses are unaffected: a 400 carries the request-reading failure verbatim, as above, and the conflict detail on a 409 (`Reason` / `EntityType` / `Key`) is the material a reload-and-retry loop is built on, so both keep their own messages in either mode.
The binary transfer endpoints follow the same switch.

### Client registration and HttpClient

**Both registration overloads have a keyed form, for holding more than one back end at once.**
`AddGeneratedHttpRemoteRepositories(serviceKey, baseAddress)` and `AddGeneratedHttpRemoteRepositories(serviceKey, httpClientFactory)` register `I{Entity}RemoteRepository` under a service key, pairing with the keyed form the dialect extensions already have (`AddGeneratedSqliteRepositories(serviceKey, connectionString)`).
That is how a hybrid app is wired: the server over HTTP under one key, a local database under another, with each consumer asking for the side it wants.

```csharp
services.AddGeneratedHttpRemoteRepositories("server", "https://server:5001/quicker");
services.AddGeneratedSqliteRepositories("local", localConnectionString);

// Constructor parameters:
//   [FromKeyedServices("server")] IOrderRemoteRepository remote
//   [FromKeyedServices("local")]  IOrderRepository       local
```

The shared HttpClient the base-address form creates is registered under the same key, so it collides with neither the non-keyed registration nor another key's, and it stays owned by the container just as in the non-keyed form.
Keyed and non-keyed registrations are separate name lists, since a keyed one answers only `GetRequiredKeyedService`, and registering the same key twice leaves the last registration in effect.

**The HttpClient returned by the factory overload is owned by the caller.**
`AddGeneratedHttpRemoteRepositories(Func<IServiceProvider, HttpClient>)` invokes the factory every time a repository is resolved, once per scope and per entity, and the returned HttpClient is disposed by neither the generated code nor the DI container.
Return a shared instance, or one managed by `IHttpClientFactory`; creating a new HttpClient on every call exhausts sockets.
The base-address overload creates a single shared instance that the container owns, so the client is disposed together with the `ServiceProvider`, and a repository resolved from an already disposed provider therefore throws `ObjectDisposedException` on use.

**The client the base-address overload builds has no timeout of its own and recycles pooled connections every five minutes.**
`PooledConnectionLifetime` (a `SocketsHttpHandler`) is what makes a long-lived singleton follow DNS changes rather than pin the address it first resolved.
`Timeout` is `Timeout.InfiniteTimeSpan` because `HttpClient.Timeout` covers a whole request including the body, and the 100-second default would cut off a large blob transfer part-way through.
**Bound each call with the `CancellationToken` you already pass to it**; that token is the timeout.
If you would rather have a finite client-wide deadline, use the factory overload and hand in an HttpClient configured your way, which stays yours and whose settings the generated code does not touch.

### The health endpoint

`MapGeneratedRemoteEndpoints` also maps `GET {prefix}/health`, which answers 200 with an empty body as soon as the server is listening.
It deliberately does not touch the database, so it says only that the process is up and the endpoints are mapped.

On the client, `Http{Entity}RemoteRepository.PingAsync` calls it and returns `false`, rather than throwing, for every flavor of "not reachable": connection refused, DNS or TLS failure, the HttpClient's own timeout, any non-success status.
That makes it usable as the condition of a wait-for-startup loop, and cancelling the token you pass still throws, so your own timeout stays distinguishable from a server that is down.

"The HttpClient's own timeout" is not a path the default registration reaches, though.
The client the base-address overload builds has `Timeout.InfiniteTimeSpan` (above), so a host that accepts the connection and then never answers leaves `PingAsync` waiting rather than returning `false`.
Give the call a `CancellationToken` with a deadline of its own, or register through the factory overload with an HttpClient whose `Timeout` you set.

The endpoint is a member of the group, so authorization applied to the group covers it too.
The prefix and the health route are exposed as the constants `RemotePaths.DefaultPrefix` (`"/quicker"`) and `RemotePaths.HealthRoute`, which both sides read so the value is written down once.

### Server-side setup and extension

The server file requires the ASP.NET Core FrameworkReference (`Microsoft.AspNetCore.App`), and no extra setup is needed if the project's SDK is `Microsoft.NET.Sdk.Web`.
Its fixed engine is shared code, so under `--use-runtime-packages` it comes from `QuickER.Runtime.AspNetCore`, and only the project hosting the server file references that package (see [Runtime package reference mode](#runtime-package-reference-mode---use-runtime-packages)).

**The generated server class is extensible.**
`GeneratedRemoteEndpoints` is a `partial` class, so your own endpoint helpers can live alongside the generated ones.
You can also implement the `static partial void OnServerError(HttpContext, Exception)` hook in another part of the class to add custom handling whenever an endpoint responds with HTTP 500, such as notifications, metrics, or extra logging.
It runs after the built-in logging, and when you do not implement it the compiler removes the call itself.
An exception thrown inside the hook is isolated: it is written to the server log and swallowed, so it never gets in the way of the original error response.
Additional endpoints under the same prefix can also be mapped directly onto the `RouteGroupBuilder` returned by `MapGeneratedRemoteEndpoints()`.

### Operational notes

**The wire format is not promised to be stable while QuickER is at 0.x, so regenerate the client and the server together** and deploy them together.
A server updated on its own does not report the mismatch as a version error: a request its newer endpoints no longer recognize comes back as an ordinary transport failure, a 404 or a 400.
That is also why the binary endpoints mark the 404 they produce themselves (see below) instead of letting the client read every 404 as "no data".

**Do not put an HTTP-level retry policy (Polly and the like) on the mutating operations**: `Insert`, `Update`, `Save`, `SaveMany`, `Delete`.
They carry no idempotency key, so a request that in fact succeeded and lost its response on the way back would be applied a second time, producing a duplicate insert, or a second version bump that turns the next save into a spurious conflict.
Retrying the read-only operations (`GetById`, `GetAll`, the named queries) and the health endpoint is safe, so scope the policy to those rather than to the whole client.

### Binary transfer endpoints (Stream accessors for unbounded binary columns)

When combined with unbounded-binary exclusion (`--exclude-unbounded-binary-columns`), the excluded column's Stream accessors (`Read/Write{Column}Async`) are streamed over HTTP.
Because a JSON envelope (`POST` + Base64) cannot avoid the memory inflation of a huge blob, these intentionally use a second, REST-style form: verb separation, raw body, `application/octet-stream`.
The following three endpoints are generated per excluded column, where `{column}` is the C# property name.

| Verb / URL | Meaning | Response |
|---|---|---|
| `GET {prefix}/{entity}/{column}?id=` | Download (stream the body to the destination) | 200 + `application/octet-stream` (an empty blob is also 200) / no row or NULL is **404** (`false` on the client) / a missing or malformed `id` is **400** |
| `PUT {prefix}/{entity}/{column}?id=` | Upload (raw body, `Content-Length` required) | Success **204** / no row **404** (`false`) / missing `Content-Length` (chunked) is **411** / a declared `Content-Length` above the endpoint's limit is **413** / a missing or malformed `id` is **400** |
| `DELETE {prefix}/{entity}/{column}?id=` | Set the column to `NULL` (equivalent to `Write(id, null)`) | Success 204 / no row 404 / a missing or malformed `id` is **400** |

- **The 404 these endpoints produce themselves carries a `RemoteError` body of type `"NotFound"`**, and only a 404 with that marker becomes `false` on the client.
  A bare 404, from a base address or prefix that does not match the server's, a route that no longer exists, or a proxy answering on its own, would otherwise be indistinguishable from "no data".
  The client raises `RemoteRepositoryException` for it instead of hiding the misconfiguration behind an empty result.
  The 411 carries a `RemoteError` body as well, of type `"BadRequest"` like the other classified rejections.
- **The key is carried in the URL query `?id=`**, because the body is used for the blob itself.
  A VO key is serialized by the same rule as the JSON envelope, as the wrapped value.
- **A 0-byte PUT (empty body) and setting to `NULL` (DELETE) are structurally distinguished**: the former makes `Read` return `true` and empty, the latter `false`.
- **The declared length is checked before the body is read.**
  A write reserves storage for the declared `Content-Length` before reading anything, since SQLite allocates a `zeroblob` of that size first, while the host's own limit only fires once the body is actually read.
  The declaration is therefore compared against the endpoint's effective limit up front, and an oversized one is rejected with 413 without the write ever running.
  The effective limit is the endpoint's own size-limit metadata when it has any, which is how the opt-in below lifts it, otherwise the limit the host publishes.
  Where the host publishes none, as in an out-of-process IIS deployment, Kestrel's default of 30,000,000 bytes stands in, so a deployment that cannot report its limit still rejects the same declarations rather than accepting any size.
  A host that lifts its limit globally without the opt-in is measured against that same default; use the opt-in, or attach size-limit metadata to the group, to lift it for these endpoints.
  When a request is both oversized and carries a malformed `?id=`, the answer is the 413, because the key is restored inside the write, which this check runs ahead of.
- **Lifting the request-size limit on binary PUT is opt-in**: `MapGeneratedRemoteEndpoints(access, prefix, exposeErrorDetails, allowUnboundedUploads)`.
  It defaults to `false`, so the limit above applies to these endpoints and a larger upload is rejected with 413.
  Pass `allowUnboundedUploads: true` to stream GB-scale blobs, and **pair it with `RemoteAccess.RequireAuthorization`, because an endpoint that accepts a body of any size is a denial-of-service surface**.
  The opt-in also removes the declared-length check, so a caller can make the server reserve storage for an arbitrary declared length on every request without sending a single byte of it; the reservation is rolled back when the request fails, but the I/O is spent regardless.
  Only the binary PUT endpoints are affected, since JSON endpoints always keep the host limit.
  To set a limit of a different size, override the whole group via the returned `RouteGroupBuilder`.
- The client (`Http{Entity}RemoteRepository`) receives `GET` with `ResponseHeadersRead` and copies to the destination in O(chunks), and sends `PUT` with `StreamContent` including `Content-Length`.
  If you do not pass `length` for a non-seekable Stream, it throws `ArgumentException` before sending, which is the same length contract as the direct path.
  **The Stream you pass in stays yours**: the client neither closes nor disposes it, exactly as a direct connection does not, so switching between the two implementations does not change what happens to the stream.
  The HTTP layer disposes the request content after sending, so the stream is handed to `StreamContent` through a non-closing wrapper.
- **Making `WithUnboundedBinary()` / `Query()` / raw SQL remote is out of scope**, as before.

## Runtime package reference mode (--use-runtime-packages)

By default, the generated code is self-contained inline output that includes the runtime, meaning the schema-independent fixed code.
Specifying `--use-runtime-packages` omits the fixed code and relies instead on references to the following NuGet packages.
The required PackageReference is described in the generation header and the CLI output; add it to the csproj by hand.

| Package | Contents | Third-party dependencies |
|---|---|---|
| `QuickER.Runtime` | Shared foundation and dialect-neutral contracts | None |
| `QuickER.Runtime.SqlServer` | QuickER's SQL Server dialect engine | Microsoft.Data.SqlClient |
| `QuickER.Runtime.Sqlite` | QuickER's SQLite dialect engine | Microsoft.Data.Sqlite, SQLitePCLRaw.bundle_e_sqlite3 |
| `QuickER.Runtime.EntityFrameworkCore` | EF Core shared parts | Microsoft.EntityFrameworkCore.Relational |
| `QuickER.Runtime.InMemory` | The in-memory engine (for tests) | None |
| `QuickER.Runtime.AspNetCore` | The fixed server-side engine behind the generated remote endpoints | ASP.NET Core (a `FrameworkReference`, not a NuGet dependency) |
| `QuickER.Runtime.Sync` | The bidirectional sync engine (journal, table descriptors, conflict types) | None |

Every package except `QuickER.Runtime` also declares a dependency on `QuickER.Runtime` itself, so that is what nuget.org shows alongside the third-party packages above.

The package version and the tool version are published in lockstep, at the same version, so use the same version for both.
While the project is on 0.x, compatibility between minor versions is not promised (see the versioning policy in [CONTRIBUTING](../CONTRIBUTING.md)).
Schema-dependent items such as the DI-registration extensions, `QuickErDbContext`, and per-entity implementations are always emitted on the generation side, even in this mode.

### How generated files map to the packages (split output)

With file splitting, the fixed runtime and the schema-dependent code go into separate files, and the fixed-runtime files correspond one-to-one with the packages.
The naming follows a single rule: the file name and the namespace suffix are the suffix of the package name (`Runtime.SqlServer.g.cs` → namespace `{Runtime}.SqlServer` → package `QuickER.Runtime.SqlServer`).
`{Runtime}` below is the runtime namespace, `{RootNamespace}.Runtime` by default.

| Generated file (namespace) | Corresponding package | Contents |
|---|---|---|
| `Runtime.g.cs` (`{Runtime}`) | `QuickER.Runtime` | The shared foundation (base classes, attributes, VO bases, JSON converters) plus the dialect-neutral contracts (`IRepository`, the query pipeline, the remote client fixed part) |
| `Runtime.SqlServer.g.cs` / `Runtime.Sqlite.g.cs` (`{Runtime}.{dialect}`) | `QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite` | The dialect engine (repository base, expression-tree translation, executor, connection factory) |
| `Runtime.EntityFrameworkCore.g.cs` (`{Runtime}.EntityFrameworkCore`) | `QuickER.Runtime.EntityFrameworkCore` | EF Core shared parts (the `TContext : DbContext` generic repository base, VO translation plugins) |
| `Runtime.InMemory.g.cs` (`{Runtime}.InMemory`) | `QuickER.Runtime.InMemory` | The in-memory foundation (store, repository base, save staging) |
| `Runtime.AspNetCore.g.cs` (`{Runtime}.AspNetCore`) | `QuickER.Runtime.AspNetCore` | The fixed server-side engine (`RemoteServerEngine`: request reading, error classification, the error-detail exposure policy, the binary streaming helpers, and the generic endpoint mapping for CRUD, the graph saves, the uniqueness pre-check, and each binary column) |
| `Runtime.Sync.g.cs` (`{Runtime}.Sync`) | `QuickER.Runtime.Sync` | The sync engine (`SyncEngine`, `SyncJournal`, `SyncTable<,>`, `SyncTableDescriptor<,>`, `SyncGraphRecorder`, the options, results, and conflict types; with remote services, the sync envelopes and the HTTP source) |
| `Repositories.g.cs`, `Repositories.SqlServer.g.cs` / `Repositories.Sqlite.g.cs` / `Repositories.EntityFrameworkCore.g.cs` / `Repositories.InMemory.g.cs` / `Repositories.Sync.g.cs` / `Repositories.Http.g.cs`, `RemoteServer.g.cs` | None; always generated | Schema-dependent code only: per-entity contracts and implementations, DI registration, `QuickErDbContext` and its Fluent configuration, projection DTOs, the per-entity endpoints (`GeneratedRemoteEndpoints`), the per-table sync descriptors and journaling decorators. With remote services, the HTTP client (`Http{Entity}RemoteRepository` and its DI registration) is split into its own `Repositories.Http.g.cs`, keeping the contract file pure interfaces, and the namespace stays the contract namespace, so type names do not change |

`Runtime.g.cs` is always there, while the files below it are emitted only for a feature you actually enabled.
The dialect files come with the QuickER Repository, the EF Core file with `GenerateEfCoreRepositories`, the in-memory file with `GenerateInMemoryRepositories`, the ASP.NET Core file with `GenerateRemoteServices`, and the sync file with `GenerateSyncSupport`.
That is exactly the same set of packages you would have to reference.

Because of this layout, `--use-runtime-packages` means exactly one thing.
**No `Runtime*.g.cs` is emitted at all, and the `using` directives in the generated code point at the fixed package namespaces (`QuickER.Runtime`, `QuickER.Runtime.SqlServer`, and so on) instead of `{Runtime}…`.**
The `Repositories*` files are the same either way, since turning the mode on or off does not change their contents.

Note that the file and namespace suffix `EntityFrameworkCore` is about matching the package name.
The C# type names are unchanged: `EfCore{Entity}Repository`, `QuickErDbContext`, `AddGeneratedEfCoreRepositories`.

`Entities.g.cs`, `ValueObjects.g.cs`, `EditModels.g.cs`, and `Mappers.g.cs` are schema-dependent in their entirety and are unchanged by this mode.
`RemoteServer.g.cs` is schema-dependent too.
With split output the fixed engine behind it lives in `Runtime.AspNetCore.g.cs`, or in `QuickER.Runtime.AspNetCore` in package mode, and the file itself holds only the per-entity endpoints and the `OnServerError` hook.
With non-split inline output the engine is embedded in `RemoteServer.g.cs` itself.
Either way it stays a separate file, because it needs the ASP.NET Core `FrameworkReference`, while all the other files above are concatenated into one with non-split generation.

## Layered folder output (--layered-output)

`--layered-output` (config key `LayeredOutput`, default OFF) sorts the split files into layer subfolders under the output directory, so that each layer can be its own project.
That is a DDD-style domain, presentation, and infrastructure split, plus a server project when remote services are generated.
It implies `SplitFilesByCategory`, because a single file cannot be split across folders.

The bucket-to-layer mapping is fixed.

| Layer (default folder) | Files |
|---|---|
| Domain (`Domain/`) | `Entities.g.cs`, `ValueObjects.g.cs`, `Repositories.g.cs` (the contracts), `Runtime.g.cs` (inline runtime) |
| Presentation (`Presentation/`) | `EditModels.g.cs`, `Mappers.g.cs` |
| Infrastructure (`Infrastructure/`) | `Repositories.SqlServer.g.cs` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.Sync` / `.Http` and the matching `Runtime.{...}.g.cs` fixed-infrastructure files |
| Server (`Server/`) | `RemoteServer.g.cs`, `Runtime.AspNetCore.g.cs` (they need the ASP.NET Core `FrameworkReference`, so they cannot live in a plain class library) |
| Output directory root | The API reference (`*.g.md`), which belongs to no csproj. `--api-docs-subdir` can move it into a subfolder such as `docs`, independently of the layers |

Each layer's folder can be overridden with `--domain-layer-dir` / `--presentation-layer-dir` / `--infrastructure-layer-dir` / `--server-layer-dir` (config keys `DomainLayerDirectory`, `PresentationLayerDirectory`, `InfrastructureLayerDirectory`, `ServerLayerDirectory`).
A value is a relative path under the output directory and may have several segments (`MyApp.Domain/Generated`), so you can point the output directory at your solution's source folder and generate straight into the layer projects.
Absolute paths, drive letters, and `..` are rejected as a generation error, and a blank value falls back to the default folder name.

**The default namespaces follow the layer folders**, so folders and namespaces stay aligned.
Each layer's namespace root is its folder path with the separators turned into dots (folder `MyApp.Domain/Generated` gives the root `MyApp.Domain.Generated`), which matches the "project folder name = RootNamespace" csproj convention.
The buckets hang under it as `{root}.{suffix}`.

| Layer (folder `MyApp.Domain` etc.) | Namespaces |
|---|---|
| Domain | `MyApp.Domain.Entities` / `.ValueObjects` / `.Repositories` (contracts) / `.Runtime` |
| Presentation | `MyApp.Presentation.EditModels` / `.Mappers` |
| Infrastructure | `MyApp.Infrastructure.SqlServer` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.Sync` / `.Http`, where the fixed-infrastructure file (`Runtime.SqlServer.g.cs` etc.) and the per-entity file (`Repositories.SqlServer.g.cs` etc.) of each family share one namespace |
| Server | `MyApp.Server.RemoteServer` / `.AspNetCore` |

The explicit namespace options (`EntityNamespace`, `RepositoryNamespace`, and so on) still win over the derivation, exactly as before.
A layer folder that cannot form a C# namespace, one with a hyphen for instance, is a generation error, unless every namespace in that layer is set explicitly.
This also untangles the plain-split quirk where the dialect implementations hung under the contract namespace (`{contracts}.SqlServer`) even though they live in another project; with layered output they sit under the infrastructure root instead.
`RootNamespace` no longer appears in the derived defaults, since the layer folders take its place.

### The generated-code subfolder (--code-subdir)

`--code-subdir` (config key `CodeSubdirectory`, unset by default) puts the generated code (`.g.cs`) one level deeper: below the layer folder with layered output, and below the output directory without it.
It works in every output mode (single file, split, layered) and exists to keep generated and hand-written code apart inside one project.

**It never affects namespaces.**
Unlike a layer folder, this value does not feed the namespace derivation, which is what lets a hand-written partial class sit in the parent folder under the same namespace as the generated one.

```
MyApp.Domain/                        <- --domain-layer-dir
  Generated/                         <- --code-subdir
    Entities.g.cs                    namespace MyApp.Domain.Entities
    Repositories.g.cs                namespace MyApp.Domain.Repositories
  OrderEntity.Rules.cs               namespace MyApp.Domain.Entities (hand-written partial)
  Services/                          <- hand-written
MyApp.Infrastructure/
  Generated/
    Repositories.SqlServer.g.cs      namespace MyApp.Infrastructure.SqlServer
    Runtime.SqlServer.g.cs
EcOrder.g.md                         <- does not follow the subfolder (see below)
```

SDK-style projects glob `**/*.cs`, so digging a subfolder needs no csproj change.
What you gain is folder-level handling: wiping and regenerating in one go, or excluding the folder from analyzers.

The value may have several segments (`Generated/QuickER`).
Absolute paths, drive letters, and `..` are a generation error, but **it does not have to be a valid C# identifier** because it never reaches a namespace, so `generated-code` works.
The API reference (`.g.md`) does not follow it, because the only thing that decides where the documentation goes is `--api-docs-subdir`.

In the GUI it sits in the generation dialog's "Output destination" card, on the row right below the output path, as "Subfolder".
It is independent of the output mode and the layered-output checkbox.

Points worth knowing about layered output:

- **Only namespaces, file placement, and the fixed runtime's visibility change.**
  Apart from the `namespace` declarations and `using` directives, the schema-dependent code is identical to plain split output, and the API reference (`.g.md`) shows the actual, derived namespaces.
  The fixed runtime (`Runtime*.g.cs`) is emitted with `public` visibility, because the layers are separate assemblies, so the runtime surface follows the same rule as the NuGet packages, which publish the same types as `public` for the same reason.
  The generated projects therefore build with plain project references, and no `InternalsVisibleTo` is needed.
- The repository contracts sit in the domain layer as DDD-style ports.
  The presentation project, where edit models check uniqueness through `I{Entity}Repository`, only needs a reference to the domain project, and infrastructure implements the domain's contracts.
  The resulting project references are `presentation → domain ← infrastructure ← server`, and the server project also references infrastructure to wire up DI.
- The inline runtime (`Runtime.g.cs`) goes into the domain layer, mirroring package mode.
  With `--use-runtime-packages` the domain project references `QuickER.Runtime` instead, and the other layers see the runtime transitively either way.
- Switching the mode, or renaming a layer folder or the subfolder, does not delete files written to the previous location; remove them yourself.

## API reference (.g.md)

You can additionally output an API-reference Markdown that shares the base name of the generated code.
Enable it with the "Output an API reference (.g.md)" checkbox in the GUI's generation dialog, or the CLI's `--generate-api-docs` flag (default OFF).
It can always be chosen independently of the DB-access selection (None / QuickER Repository / EF Core Repository).

When enabled, the Markdown shares the base name of the `.g.cs`: the English version is `.g.md` and the Japanese version is `.ja.g.md`, so `EcOrder.g.cs` gives `EcOrder.g.md` / `EcOrder.ja.g.md`.
In per-category split mode they become the fixed names `ApiDocs.g.md` / `ApiDocs.ja.g.md`, in the same style as the fixed names such as `Entities.g.cs`.
The contents are as follows.

- A list of entities and, for each entity, a property table including the DB type token, such as `string(50)` / `decimal(10,2)`
- The repository contracts (`IRepository<TEntity, TKey>` and the per-entity interfaces), included only in a configuration that generates the Repository contracts
- Usage examples of DI registration, CRUD, and queries, likewise included only in a configuration that generates the Repository contracts (these sections are omitted when DB access is "None")
- A generated-file layout table

The language is chosen with the GUI's "Language" option (English / Japanese / Both), or the CLI's `--api-docs-lang` flag (config key `ApiDocsLanguage`, values `English` / `Japanese` / `Both`, default `English`, requires `--generate-api-docs`).

| `ApiDocsLanguage` | Files written |
|---|---|
| `English` | `EcOrder.g.md` |
| `Japanese` | `EcOrder.ja.g.md` |
| `Both` | `EcOrder.g.md` and `EcOrder.ja.g.md` |

The Japanese version keeps the `.ja.g.md` name even when it is the only one written, so switching the language never renames a file.
Both versions have the same structure and contents; only the headings and prose differ.
In the settings file the value is a name and is case-insensitive (`"ApiDocsLanguage": "Japanese"`).

By default the Markdown lands in the output directory itself.
`--api-docs-subdir` (config key `ApiDocsSubdirectory`) moves it into a subfolder, as a relative path under the output directory, such as `docs`; several segments are allowed, and absolute paths and `..` are rejected.
This works in every output mode, and with layered output it keeps the documentation out of the layer projects.

The file name can be changed with `--api-docs-file` (config key `ApiDocsFileName`), so `--api-docs-file Api.md` yields `Api.g.md`, and `Api.ja.g.md` for the Japanese version.
The extension is normalized to `.g.md`, so `Api`, `Api.md`, and `Api.g.md` all give the same result; overwriting is restricted to `.g.md` / `.g.cs`, so the extension is not left to the input.
An explicit name wins in every output mode, and when it is blank you get the derived name as before: the output file base name, or `ApiDocs.g.md` when files are split.
Only a file name is accepted, and a value containing path separators is a generation error, because choosing the folder is `--api-docs-subdir`'s job.
In the GUI it is the "Output file name" box below "Output subfolder", and when the box is empty, the name that will actually be used is shown in grey, following the output file name, the output mode, and the language.

`.g.md` / `.ja.g.md` are auto-generated files.
They are overwritten on regeneration, so do not edit them directly.

## Coexisting with an existing codebase

A running system already has entity and data-access assets, hand-written or scaffolded.
After you get a diagram via DB import, there are stages of pairing those assets with the generated code, and **stopping at any stage is a valid setup**.

- **Coexistence without generation**: use the diagram only for review, definition-document output, and diff sync, and never touch the code.
  Your existing data layer stays as it is.
  The value of a single source of truth for the schema is already available even at this stage: definition documents you can regenerate from the diagram whenever it changes, and diff detection against the DB.
- **Coexistence with basic generation only**: generate just Entity / EditModel / Mapper with DB access "None" and use them around your screens.
  Data access remains your existing asset, and the generated code takes no part in reads or writes.
- **Gradual adoption starting from new features**: use the generated QuickER Repository (or the EF Core Repository) only for newly built features, and migrate existing code when you touch it.
  The generated code is plain ADO or EF Core access to the same schema, so it can share the database with your existing data layer; designing the transaction boundaries and connection management across the two remains your responsibility.
  If your system is EF Core code-first, the generated `QuickErDbContext` connects to the existing schema only and takes no part in migrations, so it can live alongside your existing DbContext, the common pattern of multiple contexts over one database.

Two practical notes for coexistence:

- **Separate by namespace**: keep `RootNamespace`, and if needed the output project, apart from your existing code, and same-named classes can coexist.
  Where you use both, distinguish them with a namespace qualification or a `using` alias.
- **The way to bring existing assets into a diagram is DB import**: the GUI's "Import Code" (C# reverse) only accepts a `.g.cs` that QuickER generated with `IncludeDataAnnotations` ON, and hand-written POCOs are not eligible.
  Bring the structure of existing assets in from the live database, not from the code (see [Database round-tripping](database.md)).

## License note

The code-generation engine (`QuickER.CodeGen.CSharp` / `CodeGen.UI` / `Cli`) is covered by [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) plus additional grants.
Thanks to those grants, **the current releases are free for everyone, including commercial use**.
For the licensing and distribution policy, including possible future paid licensing, see the [licensing guide](../LICENSING.md).

**The generated code and the runtime packages (MIT) belong to you as part of your deliverable.**
[LICENSE-NC.md](../LICENSE-NC.md) grants everyone a perpetual, irrevocable license to use, modify, distribute, and sell generated output for any purpose, with no attribution required.
