# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

## [0.2.0] - 2026-09-24

Highlights of this release:

- A composite primary key now keeps its own column order across DB import, editing, DDL and generated code
- DB import now carries types and constraints more faithfully, and reports what it could not carry across
- The generated base classes can now be extended with `partial` in every output mode
- Fixed data loss in bidirectional sync, and safety issues in AI CLI launching and mock generation
- Check "Breaking changes" before upgrading (it includes where settings are now stored)

### Breaking changes

#### Distribution & settings

- **Settings and working state moved to `%LOCALAPPDATA%\QuickER`** — the old folder `%APPDATA%\QuickER` is no longer read, so the display language, saved connections (including passwords), API keys, code-generation settings and the recovery snapshot all start from their defaults on first launch. To carry them over, copy the old folder's contents into the new one before the first launch

#### Code generation dialog & CLI

- **The API reference language is now chosen with `ApiDocsLanguage` (CLI: `--api-docs-lang`)** — pick `English` / `Japanese` / `Both`, including a Japanese-only output. The old key `IncludeJapaneseApiDocs` and `--api-docs-ja` are not carried over, so replace them with `"ApiDocsLanguage": "Both"`. If the GUI had the Japanese version turned on as well, choose "Both" again
- **`generate` / `scaffold` and MCP's `generate_csharp` no longer overwrite generated files that were edited by hand** — when a `.g.cs` about to be replaced has been edited since it was generated, the CLI writes nothing, lists the files and exits with code `2`, and `generate_csharp` fails with the same list (the GUI asks instead). Reformatting and line-ending conversion do not count as edits, and files generated before this version are not checked. If a script or CI job rewrites the generated files after generating them, add `--force` (MCP: `force: true`) so regeneration keeps overwriting them. See [docs/code-generation.md](docs/code-generation.md#the-generated-file-header)

#### DB sync & DDL

- **What a column's type and name may contain is now restricted** — DDL generation and schema-sync script generation refuse, naming the place, a type carrying `;`, a quote, a comment marker or a line break, or one smuggling a column clause such as `int NOT NULL`. A line break or control character in a table, column, constraint or query name now stops every generator, including C# generation. Fix the type or the name in the affected diagram

#### Generated code

- **The runtime base types now carry a `Core` suffix** — `EntityBaseCore`, `EditModelBaseCore`, `ValueObjectBaseCore`, `IValueObjectCore`, `IRepositoryCore` / `IRemoteRepositoryCore`, each back end's `…RepositoryCore`, and `MapperBaseCore`. The names the generated code writes (`: EntityBase` and the like) are unchanged. Rename any code of your own that names a runtime type directly — a `where TEntity : EntityBase` constraint, an `is IValueObject` test — to the `Core` form
- **The message and display-name hooks are gone** — the edit model's `Customize…ErrorMessage` / `CustomizePropertyDisplayName`, the value object's `Customize…ErrorMessage` / `CustomizeDisplayName`, and the entity's `CustomizeDisplayName`. Replace wording through `EditModelMessages` / `ValueObjectValidationMessages` and a display name through `GeneratedDisplayNames.Resolve`, branching on the first argument each entry now receives (the property name or the display name) to vary it per type. The default wording is unchanged
- **The mapper's `includeRemoved` argument is now required** — pass `true` when building a graph to save and `false` when building one to display (the old default of `false` left deleted rows out of the save). Code that derived from `MapperBase<,>` by hand and overrode `CreateEntityCore` and the like should move that logic to `LoadColumns` / `CompleteLoad`
- **`CheckUniquenessAsync` is now declared on the common surface `IRemoteRepositoryCore<TEntity, TKey>`** — callers are unaffected. A hand-written test double or adapter implementing a repository contract now has to provide this member
- **With value objects on, same-named columns whose C# types differ are now a generation error** — a diagram where, say, one `payload` column is `varbinary` and another is `varchar` used to unify them into one value object with a warning, which made reading the narrower column throw at run time (or silently changed comparison semantics). Align the column types in the diagram, or rename the columns so they stay apart. A length or precision mismatch alone still warns and unifies as before
- **A column named `row_state` is now a generation error when edit models are generated** — the mapper's state transfer (`entity.RowState = editModel.RowState;`) binds by name, so such a column silently rerouted it to the column property and an edited row was saved as "unchanged". Rename the column in the diagram
- **A named query can no longer take the name of a remote operation** — `SaveMany`, `Ping`, `SyncCeiling` / `SyncChanges` / `SyncKeys` / `SyncPage`, in any letter case; and two queries whose names differ only in case are now rejected as duplicates. The remote endpoints put both on the same route (routes ignore case), so every call to either answered 500. Rename the query in the diagram

#### Bidirectional sync

- **The generated sync classes and interfaces changed shape** — the per-table sync classes (`{Entity}SyncTable` / `{Entity}DirectSyncSource` / `Http{Entity}SyncSource`) are no longer generated; construct the fixed classes instead, passing a descriptor from `GeneratedSyncTables` (no change needed if you use the DI registrations). `SyncTableBase.OnLastWriteWinsUploadedAsync` is gone, and the signatures of `ISyncTable.UploadAsync` and `PropagateDeletesAsync` changed. The local database gains an acknowledgement table, `quicker_sync_ack` (created by `SyncJournal.EnsureCreatedAsync`)
- **A sync download now stops at a row with an unsent local change** — so as not to overwrite it with the server's content. `SyncResult.Truncations` reports where it stopped; resolve the conflict, or drop the entry with `SyncJournal.RemoveTableAsync`, then run the sync again

### Added

#### Diagram editing & files

- **A composite primary key now keeps and can edit its own column order** — schema import for all five dialects reads the order `PRIMARY KEY (b, a)` declares, and DDL, sync scripts and EF Core's `HasKey` emit it in that order too. A table with two or more key columns gets a "Primary key order" card in the properties panel for reordering them, and the AI tools gain `set_primary_key`, which sets the primary key together with its column order. Once a diagram states the order explicitly, reordering columns no longer changes the key's order. The DBML / Mermaid / Excel documents and the C# code import do not carry the order, and opening and saving the diagram with 0.1.0 drops it

- **The side panels can now be collapsed** — "Toolbox" (F9) and "Property panel" (F10), in the toolbar's new "View" group, hide the panels on the left and right so the canvas can use the whole window. A width you set by dragging the property panel's edge comes back when you show it again, and both states are restored on the next launch. Collapsing the toolbox while a relationship is being created also cancels that mode, because its notice and cancel button live in that panel

- **F11 now switches to full screen** — the window frame is hidden and the window fills the screen. The toolbar and status bar stay, so combining it with the panel toggles (F9 / F10) leaves you with just the canvas. It is also in the toolbar's "View" group, and it is the one display state that is not restored on the next launch. With the title bar hidden, an "Exit" button takes its place at the right end of the toolbar

#### DB import & connections

- **PostgreSQL and MySQL connections can ask for a TLS level** — pick one in the connection dialog's "Encryption (SSL Mode)" field. The default, `Unspecified`, connects exactly as before. Both drivers default to encrypting without validating the server certificate, so choose `VerifyCa` or `VerifyFull` to have it checked. What each dialect does, Oracle included, is summarized in [docs/database.md](docs/database.md)

#### Code generation dialog

- **Regenerating asks before overwriting generated files that were edited by hand** — when a `.g.cs` about to be replaced has been edited since it was generated, the dialog lists those files and writes nothing unless you choose OK. Reformatting and line-ending conversion do not count as edits, and files generated before this version are not checked

#### Generated code

- **The generated base classes can now be extended with `partial` in every output mode** — `EntityBase`, `EditModelBase<TSelf>`, the value-object bases and the `IValueObject` marker, the repository contract and implementation bases, and `MapperBase<,>` are all emitted as source, so adding a member or an interface reaches the whole generated output. The same code compiles unchanged when the runtime is referenced as a package. Additions to a repository contract must carry a default implementation. See the "Extending the generated base classes" section of [docs/code-generation.md](docs/code-generation.md) for how to write one
- **An enumeration-like value object is now declared with one attribute (`[DeclaredInstance]`)** — mark the `static readonly` instances of a value object and every creation path, database reads and JSON restore included, returns the declared instances, any other value is rejected as a validation error, and `GetDeclaredInstances()` lists them in declaration order. A `[Display(Name = ...)]` on the field is available as `DeclaredDisplayName`, and adding `InputText` or `ClaimsAbsent` lets a fixed string or a blank cell import as that instance. The hand-written table, `GetDefinedInstance` and the membership `OnValidate` are no longer needed (a hook still wins the lookup when you implement one), and the fields are initialized with `new(...)`, not `Create`. See the value-object section of [docs/code-generation.md](docs/code-generation.md)
- **A value object can claim a blank import cell as a value of its own — the `ConvertAbsentInput` hook** — `TryCreateFrom` / `CreateFrom` still answer "success with null" for a blank input (`null`, `DBNull`, an empty string), but a type whose notation writes one of its values as a blank — a flag written as a mark or nothing, say — can now implement the `ConvertAbsentInput` partial hook (a hand-written type implements `TryConvertAbsentInput` on the interface) to import the blank as an instance such as `False` instead of null. The hook applies to `TryCreateFrom` / `CreateFrom` only — an edit model's blank input and a database NULL are unaffected — and a type that does not implement it behaves exactly as before. See the value-object section of [docs/code-generation.md](docs/code-generation.md)
- **Value objects implement `IFormattable`** — `price.ToString("N2")`, string interpolation, and a WPF binding's StringFormat now reach the underlying value
- **The value object validation rules are now public, and four general-purpose rules were added** — the required, max-length and precision checks the generated code uses can be called from `ValueObjectRules` and the like. For `OnValidate`, digit-count, range, ASCII-alphanumeric and email-address checks were added
- **Each generated file records the QuickER version and a content hash** — every `.g.cs` gains `// Generated by QuickER x.y.z` and `// Content hash: sha256:…` lines after `// <auto-generated />`, and the API reference gains the version line. The first regeneration adds these two lines to every file

#### Distribution & settings

- **The Settings button (⚙) has an About QuickER entry** — it shows the version, the .NET runtime, the copyright and links to the repository and the documentation, and copies the version details for bug reports with one click

### Changed

#### Diagram editing & files

- **The display toggles have moved into a "View" group on the toolbar** — "Descriptions," "Nullability" and "Compact" now live in the popup that button opens, together with the two new panel toggles. The popup stays open while you flip them, so the toolbar stays on one line as more toggles are added

- **Diagrams now open at 100%** — a diagram too large for the window used to be shrunk to 80%, which made the text hard to read. It now stays at actual size and you scroll to the rest. This covers opening, importing, DB import, AI generation and session restore. Rearranging, on the other hand, is about checking the result, so the three arrange commands and "Fit to window" shrink until the whole diagram fits (down to 50%, the same floor as manual zoom; "Fit to window" previously stopped at 80% and did not show all of a large diagram). A diagram that does not fit now starts at its top-left corner instead of its center

#### License

- **The permanent commercial-use grant for the basic generation of Entity / EditModel / Mapper is withdrawn for future versions** — the additional grants in [LICENSE-NC.md](LICENSE-NC.md) (QuickER-specific terms version 2.0) now fold into a single interim grant, so any feature may become paid in a future version. The current release remains free for everyone, commercial use included. QuickER 0.1.0 keeps the version 1.0 terms

#### Distribution & settings

- **The startup update check can now be turned off** — toggle it from the toolbar's Settings button (⚙), which also now holds the language choice. Turning it off means the app never contacts GitHub at startup

#### Code generation dialog

- **The completion dialog now lists the files it wrote**
- **The single-file output mode can now be picked while layer-folder output is still on** — picking it clears the layer-folder checkbox

#### Generated code

- **The in-memory repository now enforces the diagram's UNIQUE constraints** — a write a real database would refuse is now refused in memory too, with an `InvalidOperationException`. The user-defined checks in `CollectCustomUniquenessChecks` and seeding are not covered
- **A NOT NULL excluded binary column is now required input only while the row is new** — the edit model's validation now stops, naming the column, an INSERT that was always going to fail against the database. If you insert and then write the body with `Write{Column}Async`, give the insert an empty value
- **`[Column]` is now emitted regardless of `IncludeDataAnnotations`** — the runtime treats only a property carrying this attribute as a column
- **Repeated query execution is faster** — an expression tree is no longer compiled on every call. Measured against SQLite, a projection query or a query with a value-object condition is now roughly 2.5-3x faster
- **The amount of generated code is reduced** — boilerplate in the edit model and mapper (roughly 30% less), the repository, DI registration, the remote server endpoints and sync moved to the fixed runtime. No observable behavior changes

### Security

#### AI chat & mock generation

- **Launching an AI CLI installed as a `.cmd` shim through npm no longer lets an argument run as a separate command** — each argument is now protected from `cmd.exe`'s interpretation, and an argument carrying `%` or a line break, which cannot be protected, is refused. The CLI and `cmd.exe` are now launched by full path too. Copilot is launched by the SDK itself, so only the resolved executable path can be checked (see the "Notes" section of [docs/ai-chat.md](docs/ai-chat.md))
- **The Codex chat can no longer read the contents of the folder QuickER was started from** — it now gets a throwaway temporary folder, read-only, matching the Claude Code and Copilot chats
- **A Codex sign-in URL is now opened only when it is `http` or `https`** — any other URL no longer starts the default app for it; the chat's status line reports it instead
- **The mock project's final verification build no longer silently runs a build setting the AI left behind** — it now deletes `obj` / `bin` before building and closes further paths MSBuild loads automatically. For Codex and Copilot, if anything outside UI-layer sources and static assets was added or changed, it shows the full list and asks whether to run the build; cancelling finishes with the project unverified
- **What mock generation auto-approves for the AI is narrower now** — for Copilot, a command whose paths cannot be read is approved only when it is entirely `dotnet`, and a declined command is written to the generation log. For the API key method, a path spelled differently (a trailing `.` or space, or a short name such as `GENERA~1`) can no longer write into a protected folder

#### CLI & MCP

- **The MCP `generate_ddl` tool now writes only to a `.sql` file** — its parent folder must already exist too. The write is now atomic

#### Generated code

- **A binary-column upload now rejects an oversized declared length with a 413 before the write runs** — previously, declaring a huge length without sending a body was enough to trigger a storage reservation
- **A form feed character (U+000C) in a table or column description can no longer break the generated code across lines** — it is folded into a space, the same as a line break. It used to survive the folding and become a real newline during rendering, splitting the XmlDoc comment or attribute literal it sat in — which let a description place its own member declarations into the generated code in some configurations

#### Distribution

- **The release-building workflow's actions are now pinned to commit SHAs** — so a moved upstream tag cannot change what the build and publish steps do. The distributed artifacts are unchanged
- **`QUICKER_UPDATE_FEED`, which redirects the update feed, now only works in a Debug build** — a shipped build always reads the official update feed alone

### Fixed

#### Diagram editing & files

- **The open file's external changes are no longer missed or overwritten without asking** — saving now compares the disk content beforehand and asks if it changed externally, and asks as well before writing back over a file saved in a newer format — including when a Schema JSON export targets one. Also fixed: watching stopped after a burst of changes, and an external write landing right after a save could be missed
- **An external change arriving mid-drag no longer corrupts the diagram or the undo history** — an in-progress move, resize or rubber-band selection is now cancelled back to where it started. A dialog that is already open is not closed
- **A diagram file with a duplicated table or column identifier is now refused instead of being loaded** — it used to load but could not be saved, and switching the target DBMS ended the application. The message names the table, the column and the duplicated identifier. A file that cannot be loaded now also reports what kind of problem it has (invalid JSON, for one)
- **A file saved immediately before a power loss no longer comes back empty**
- **Fixed memory use growing and stray items appearing in the undo history each time a diagram was reopened or imported**
- **A failing Undo or Redo no longer removes the operation from history**

#### DB import

- **PostgreSQL import now reads a column's actual declared type** — `numeric(10,-2)`, the length of `bit(8)`, the unit of `interval`, and an array's element type are now imported correctly. A table whose connecting role has no column privileges no longer imports with zero columns, and a partitioned table no longer disappears whole. Affected columns show up once as a change when re-imported into an older diagram
- **Oracle import no longer imports a `DISABLE`d constraint, a temporary table or a materialized view, and now keeps `NUMBER(10,-2)` and `VARCHAR2(50 CHAR)`** — switching a column with a negative scale to SQL Server, MySQL or SQLite reports it as unconvertible
- **A foreign key pointing outside the imported scope, and two same-named tables differing only in letter case, are now handled correctly** — MySQL used to wire an out-of-scope foreign key to a same-named table by mistake, and PostgreSQL / Oracle / MySQL mixed the columns of same-named tables. Both cases are now skipped with a warning instead
- **The DB import now lists at completion what it could not carry across** — a flattened domain type, an out-of-scope foreign key, unreadable columns, a name collision, a lost partition, and a type that cannot be written into DDL. `quicker scaffold` writes the same list to standard error
- **An SQLite composite foreign key that omits its referenced columns is now imported with the correct column pairs**

#### DB sync & DDL

- **Syncing a description or a column order on MySQL no longer wipes the column's other attributes** — `DEFAULT`, `AUTO_INCREMENT`, the collation and the like are now rebuilt from the live definition. A type change you had not selected is no longer applied along with it either
- **A MySQL sync script no longer drops a foreign key nobody asked about**
- **A table created by a schema sync now carries its UNIQUE constraints** (SQL Server / PostgreSQL / MySQL / Oracle)
- **A sync script is no longer split apart by a description containing a line holding only `GO` or `/`** (SQL Server / Oracle)
- **A name containing a symbol or a line break no longer breaks the output** — a table or column name carrying a quote character, `'`, `$$`, `"`, `\`, `<` or a line break used to break DDL, sync scripts, generated code, XML doc comments, and the DBML / Mermaid output. A backslash in DBML now survives the round trip too

#### C# code import

- **What could not be imported is no longer silently dropped** — a table whose columns cannot be restored at all is now kept with zero columns, and dropping a primary key column or a relationship endpoint is now named in a warning. Also fixed: a referential action written as a number was misread, and a many-to-many relationship's metadata could carry over onto a one-to-many
- **A column's type spelling no longer changes across a C# code import round trip** — a type that collapses onto another spelling, such as `datetime` or `numeric`, now also records the original spelling in `[DbColumnMeta]`, and import restores it. It used to be rewritten, and the next DB sync then emitted a needless `ALTER COLUMN`

#### Generated code

- **CRUD against a schema-qualified table (such as `sales.orders`) no longer fails at run time**
- **A property added through `partial` no longer lands in the SQL as a column** — only a property carrying `[Column]` is treated as a column now. In a configuration that uses EF Core, add `[NotMapped]` to an added property
- **A sub-second part of a date/time column is no longer lost when editing a different column** — the edit model's input string now displays it too
- **A blank input now clears a numeric, date/time, bool or binary column without value objects** — the confirmed value becomes null and the required check decides whether that is acceptable, the same rule the value-object columns already followed. It used to become a conversion error (a binary column, an empty array), so a nullable column could not be set back to NULL from the screen
- **A removed row with an empty required field no longer makes the save throw** — the mapper now skips a removed row's absent non-key values instead of requiring them, so `Validate()` returning true and the save agreeing are one and the same. The key is still required: a delete needs it
- **`AcceptChanges` now cleans up after saved deletions** — a row left in the collection by `MarkRemoved()` leaves the collection instead of coming back as an ordinary row, and a row set aside by `Remove()` becomes Added again, cascade descendants included, so adding the same instance back inserts the whole graph (it used to stay Removed and never be saved). A single cascade child stays Removed on the navigation property; assign null to it after the save
- **A reload during a row edit no longer lets `CancelEdit` roll the loaded values back** — the mapper's load abandons an in-progress row edit, so a cancel arriving afterwards is a no-op instead of restoring the stale pre-load snapshot
- **A value object over MySQL's `tinyint` (`sbyte`) now carries the comparison operators** — it used to derive from the equality-only base, so `OrderBy` on such a column threw at run time
- **A binary value object can now be created from a Base64 string through `TryCreateFrom` / `CreateFrom`** — the same notation the edit models accept. A malformed string is an ordinary conversion error, not an exception
- **A diagram with a column named like `foo_snapshot` next to `foo` generates again** — the generator kept reserving a per-column snapshot member that no longer exists (row-edit snapshots live in one base array), so the legitimate pair was refused as a collision
- **`HasErrors` now raises `PropertyChanged`** — a binding to it (a save button's IsEnabled, say) updates together with the errors, the way `HasChanges` always did
- **An edit-model child collection whose accessor returns null counts as empty** — validation, error collection and accepting changes no longer throw while a lazily initialized collection is still null (the same rule a single child always had)
- **`CreateEntity(editModel, includeRemoved: false)` now leaves a removed single cascade child out as null** — collection elements were filtered, a single child was not
- **A culture clone with a customized `LongTimePattern` renders date/time input strings with its own pattern** — the pattern cache is keyed by the pattern text now, so the clone no longer collapses onto the original culture's cached entry
- **`GetDeclaredInstances()` no longer exposes the registry's internal array** — casting the list back to an array and writing into it can no longer corrupt the shared set
- **Fixed a row whose deletion had been undone still being deleted on save after `Clear()` on an edit-model collection**
- **`ValidateUniqueAsync` no longer queries the database for a row marked for removal**
- **An invalid paging argument on a named query is now a 400 instead of a 500**
- **Fixed the server file's name drifting from the main file's when generating remote services with single-file output**
- **A projection query on a table under bidirectional sync no longer breaks the generated code** — the journaling decorator rebuilt the query methods a second time, the projection's DTO name tripped the build-wide duplicate check, and the delegating method silently vanished (CS0535)
- **A foreign key referencing a column that is not the parent's primary key now joins to that column in EF Core too** — the Fluent configuration emits an explicit `HasPrincipalKey`. EF Core's default silently joined it to the primary key: with matching CLR types, `Include` returned wrong rows; with differing types, the whole DbContext failed model validation. The other backends already joined by the column pair
- **A table without a primary key no longer makes the whole EF Core DbContext throw on first use** — it is excluded from the model (no DbSet, and navigations to it are ignored) and a Warning diagnostic names it: the same line the repository generation already draws for such tables
- **Two named queries on different tables can no longer collide on the server-side request record name** — the record is now named `{Repository}_{Operation}Request` (it is private; nothing on the wire changes)
- **`RepositoryDialects` written in a different letter case (`SQLite`) no longer produces uncompilable output** — the spelling is normalized to the canonical one instead of mixing one dialect's base classes with the other's usings without a diagnostic
- **An IN search on a nullable value-type column now compiles** — the parameter list is lifted to the nullable element type once at the start of the method (`IReadOnlyList<int>` against an `int?` column used to be a CS1929 in the generated code)
- **A named query whose DSL condition cannot type-check no longer breaks the whole generated assembly** — a condition whose emitted C# could never compile (`name > 'M'` ordering a string column, a `CONTAINS` on a numeric column, a fractional literal against an integer-backed value object, a parameter typed by a different column's value object, and the like) is now detected at generation time: the query is skipped with a warning naming the query, the table, the column and the offending operand, and every other query and file generates as before — the same treatment a dangling column reference already received. Nothing that used to compile is newly rejected (an integer column compared with `1.5` still widens to double, for instance). The MCP `set_query` tool still saves such a condition — the file-based server has no dialect type mapper to resolve column types with — and the generation-time warning is where it surfaces (noted in [docs/mcp.md](docs/mcp.md))
- **When repositories are not generated, the dialect selection (`RepositoryDialects`) no longer affects the output** — an EF Core-only run now produces byte-identical output with or without dialect dictionaries; `[SqlColumnType]` attributes and type-unification Info diagnostics used to leak in
- **Ordered comparisons (`<` / `<=` / `>` / `>=`) on a nullable value-object column no longer return NULL rows on the in-memory executor** — value-object comparison operators order null as the smallest value, so only the in-memory backend disagreed with SQL and EF Core (where NULL rows drop out); the emitted condition now carries an explicit "column is not null" premise
- **Equality against a `byte[]` column now matches by content (byte-wise) on the in-memory repository** — `Query()` predicates, DSL equality and uniqueness checks over `byte[]` members are covered; reference comparison combined with the store handing out cloned arrays meant nothing ever matched
- **Raw-SQL parameter matching is now case-sensitive (the same rule the runtime binder uses)** — `@CustomerID` used to match a declared `customerId`, so generation passed and every execution failed; it is now reported as undeclared (warning, that query skipped) plus unused (warning only)
- **A wildcard-less `NOT LIKE 'abc'` no longer matches NULL rows** — it used to fold into a negated equality where the column-side NULL compensation applies, departing from LIKE semantics (a NULL row matches in neither direction); chained prefix `NOT`s now also fold by parity
- **`ValidateUniqueAsync` no longer checks the CLR default (0) when a value-type constraint member has no input** — an unset value-type member cannot carry its unset state onto the entity, so the check now answers true without querying (it is advisory while the required check blocks the save)
- **Named-query definitions that would emit uncompilable code are now diagnosed at generation time** — a parameter named after a C# reserved keyword (`class` and the like), a parameter named `Query` (it hides the repository's `Query()` member), and a projection field named after its result type are generation errors; an IN-list lift variable colliding with a parameter name (`idsValues`) is renamed automatically
- **Numeric literals in DSL conditions accept ASCII digits only** — a full-width digit (`５`) used to pass straight into the C# literal and break compilation; it is now a condition validation error. String literals also escape characters the renderer would turn into real line breaks (U+2028 and friends)
- **Referential actions (ON DELETE) now map to EF Core's DeleteBehavior according to measured behavior** — everything but Cascade used to collapse into `Restrict`; now `SET NULL` → `SetNull`, `NO ACTION` → `NoAction` (client behavior identical to Restrict), and `SET DEFAULT` → `ClientNoAction`. On a SET DEFAULT diagram, deleting a parent through EF used to update tracked children's foreign keys to NULL instead of letting the database apply the default
- **A SQL local variable declared with `DECLARE` in raw SQL is no longer reported as an undeclared parameter** — multi-declarations (`DECLARE @a int, @b int`), initializers, and table variables are recognized; valid queries used to be skipped on the false positive
- **A condition or ordering left on a raw-SQL / manual query is now announced with a warning instead of being silently ignored** — the WHERE / ORDER BY belong to the SQL (or the implementation); generation continues
- **Consecutive blank lines inside a raw SQL verbatim literal are no longer rewritten at generation time** — user SQL containing blank lines inside a SQL string literal used to change silently (blank-line runs outside literals still fold to one, as before)
- **An extremely complex condition (more than 200 ANDs / ORs / NOTs / parentheses combined) is now a validation error instead of a process-killing StackOverflow**
- **A DSL projection that declares a non-nullable field over a nullable value-type column no longer breaks the whole generated assembly** — the combination always fails with CS0266, so the query is skipped with a warning (same treatment as the DSL type check)
- **The raw-SQL single-row body's local (`items`) no longer collides with a parameter of the same name** — it is renamed with a trailing `_` when taken
- **API reference (.g.md) table cells no longer show C# literal escapes (`\"` and the like)** — table-name and description cells now carry the diagram's raw values with only Markdown hardening (pipe escaping, newline folding)
- **An unsupported repository dialect combined with layered output is now reported as a diagnostic instead of an exception** (reachable only through direct library calls)
- **An invalid `RepositoryNamespace` no longer blocks generation in single-dialect non-split layouts where it is unused** — category namespaces are validated only in configurations that actually consume them (split mode, or the non-split multi-dialect layout)

#### Bidirectional sync

- **Fixed sync losing server changes or local edits** — an uploaded row's new version could step over an unfetched change, so that change never came down again. Also fixed: the same run's download overwriting the local edit of a row it had just reported as a conflict, and an upload interrupted part way causing a false conflict or overwriting with a stale value
- **`SyncConflictPolicy.ServerWins` now restores a discarded row's content from the server** — a local edit to a row nobody else had touched used to survive being discarded
- **Delete propagation and acknowledgement matching no longer miss rows on tables with a decimal primary key** — the key-to-text conversion now normalizes the scale (no trailing zeros); SQL Server returns `1.50` rescaled to the declared scale while the local SQLite mirror returns `1.5` as written, so the same row counted as two different keys
- **A table name with two schema qualifiers (the `dbo.sync.orders` shape) is now quoted in sync SQL by the same rule as CRUD and DDL (split at the first dot only)**

#### AI chat & mock generation

- **Fixed the Claude Code chat starting a new session on every turn with `claude` installed through npm** — a multi-line system prompt was cut short, taking the `--resume` / `--model` arguments after it with it
- **The AI tools now make a primary key column NOT NULL through every host** — only the external MCP server used to be able to write a nullable primary key column into the diagram
- **Generating a mock project into an output folder that already holds a project now asks before overwriting**
- **Mock project generation now handles interruption, timeout and app exit correctly** — an interruption under Claude Code is now reported as an interruption, and the final verification build is aborted after 10 minutes. Closing the app now interrupts the AI chat and mock generation child processes too
- **Fixed the mock project artifact check and log display** — the check now looks only inside the generated project folder and no longer fails on an unreadable subfolder. The log display no longer slows down during a long generation

#### CLI & MCP

- **A failed MCP tool call is now returned as an error (`isError`)**
- **`quicker reverse` now creates the output folder**
- **`quicker reverse` no longer overwrites a diagram saved in a newer format** — it writes nothing and exits with code `2`; add `--force` to overwrite it anyway

## [0.1.0] - 2026-08-30

Initial public release.

### Added

- **Visual ER design** — crow's foot notation, one-to-one / one-to-many / many-to-many, composite primary keys, FK referential actions, comprehensive undo/redo, and a large-diagram canvas UX (zoom / pan / search / minimap)
- **Multi-DB support** — schema import, diff sync, DDL generation, and automatic type conversion on dialect switch, across five dialects (SQL Server / PostgreSQL / MySQL / Oracle / SQLite)
- **C# code generation** — Entity / EditModel / Mapper, plus a 3-way data-access choice: none / the QuickER Repository / the EF Core Repository (the same interfaces, swappable with a single DI registration line). Value objects, named queries, remote contracts and HTTP + JSON services, and a runtime NuGet package reference mode are optional
- **Bidirectional sync** — optionally generates the engine that keeps a local SQLite copy in step with a SQL Server database, over a direct connection or HTTP, with a fast full reload for the first build and for recovery
- **AI chat and mock generation** — create and edit diagrams in conversation (OpenAI API / Anthropic API / an OpenAI-compatible local LLM / Codex / Claude Code / Copilot), generate web screen mockups from the ER model, and optionally scaffold a runnable Blazor or WPF mock project
- **MCP server** — `quicker mcp` exposes diagram editing and code generation to external AI agents over stdio
- **Import/export** — DBML / Mermaid / Excel definition documents / HTML definition documents / schema JSON / PNG / SVG / vector printing
- **CLI** — `quicker generate` / `quicker scaffold` / `quicker reverse` / `quicker mcp`
- **Working samples** — `samples/ec-order` (SQLite, no external database required) and `samples/ec-order-remote` (three-tier over HTTP + JSON)

Distributed as a GUI (Setup.exe and Portable zip, in a full self-contained channel and a lite framework-dependent one) and as NuGet packages (`QuickER.Cli` as a dotnet tool, plus the runtime packages `QuickER.Runtime` / `.SqlServer` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.AspNetCore` / `.Sync`).

The repository is mixed-license: the core is MIT, while the AI features, the code generation, the CLI, and the MCP tool-execution host (8 projects) are PolyForm Noncommercial 1.0.0 plus additional grants — currently free for everyone including commercial use, with commercial use of the basic code generation granted permanently. The terms are in [LICENSE-NC.md](LICENSE-NC.md); [LICENSING.md](LICENSING.md) explains them in plain language.

[Unreleased]: https://github.com/kokko-labs/QuickER/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.2.0
[0.1.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.1.0
