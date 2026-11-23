# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

### Breaking changes

#### Generated code

- **Remote save, insert and update responses now carry row versions only in the column values** — The fields that carried versions alone (such as `RemoteSaveResult.RowVersions`) are gone, and a response without the values, or with a number of values that does not match the rows sent, is refused with an `InvalidOperationException`. Regenerate the remote server and client with the same version of QuickER

#### Bidirectional sync

- **The bidirectional sync engine is rebuilt, and local changes and sync state are kept differently** — Triggers on the synced tables record the changes, and the state lives in `quicker_sync_state`, `quicker_sync_row` and `quicker_sync_table`. The auxiliary tables of 0.4.0 (`quicker_sync_journal` and the others) are not read, so send every unsent change before you upgrade and rebuild the device with a refresh afterwards
- **The public sync types are trimmed** — `SyncJournal`, `JournalingRepository`, `SyncSession`, `ISyncTable`, `SyncTableBase`, `SaveHookContextWrites` and others are gone, along with the descriptor members for local SQL (`LocalKeysSql` and others), and `SyncJournalOperation` is now `SyncOperation`. The registration calls such as `AddGeneratedSyncEngine` are unchanged
- **A download no longer runs the local save hooks** — The server's rows are written to the device without going through the repository, so no hook runs for a row the sync brings down. Do what those rows need after the sync
- **A table with a schema-qualified name is left out of the sync** — A table whose name contains `.` is a table of another database in SQLite, and the triggers that record changes cannot be put on it. Generation names it in a warning, and fails when no table is left to sync
- **Registering the local connection factory again after `AddGeneratedSyncEngine` now throws when the engine is built** — The registration replaces the local `ISqlConnectionFactory` with a wrapper that checks the triggers each time a connection opens. Register the connection factory before `AddGeneratedSyncEngine`
- **A save from an entity that carries no version is no longer reported as a conflict** — An entity created on the device and kept, or built from its key alone, that is saved after a sync brought down another writer's content uploads without a conflict and overwrites that content (0.4.0 reported `DuplicateOnServer`). Read your entities again after a sync before you edit them
- **A sync is refused with `SyncLocalTableEmptiedException` when a table was emptied outside the sync** — The resume point is no longer derived from the device's rows; it is recorded per table. Rebuild an emptied table with a refresh

#### DB sync & DDL

- **On PostgreSQL, a foreign key created with the full default name is no longer treated as the same foreign key as the one in the diagram** — In a database created with the DDL of 0.2.0 or earlier, the differences show the foreign key with the name cut at 63 bytes dropped and the one with the shortened name added. Select both and sync once

#### DB import & connections

- **A `connections.json` holding a connection profile without the DBMS type is now treated as a corrupt file** — It used to be read as SQL Server. The file is backed up to `connections.json.corrupt` and the list starts empty, so create the connections again

#### Diagram files

- **An auto-save record without the diagram's hash (written by 0.2.0 or earlier) is no longer trusted** — On the first start after the update, the diagram you had open may be restored as untitled and unsaved. Save it to its file again

### Added

#### Bidirectional sync

- **Unsent changes can be listed and discarded row by row** — `SyncEngine.GetPendingChangesAsync` returns the unsent rows, and a row discarded with `DiscardPendingChangesAsync` or `DiscardAllPendingChangesAsync` is put back from the server's row by the next sync or refresh
- **`DownloadRowLimit` caps the rows one run brings down** — The default is 300,000 rows; a run that goes over it while unsent changes exist applies nothing and is refused with `SyncDownloadTooLargeException`. A large download with no unsent change is committed a chunk at a time
- **`SyncLocalTracking.WriteUntrackedAsync` runs writes that are not sent by the sync** — Rows written inside its single transaction on the connection you pass are not recorded as unsent

### Changed

#### Bidirectional sync

- **A download no longer stops a table at an unsent row** — An unsent row keeps the device's content while the rows after it come down, and `SyncResult.Truncations` now lists only the rows that could not be placed because of a foreign key or a unique value
- **Rows whose unique values were swapped or rotated now come down in one sync** — They used to need a refresh, or a sync with a temporary value in between
- **A refresh now replaces the rows of a table instead of emptying it first** — It removes only the rows the server no longer holds, and a refresh whose fetch fails leaves the device as it was (a large refresh is committed a chunk at a time). `SyncRefreshResult.DiscardedChanges` is the number of rows whose change was actually discarded
- **During a large refresh or sync, other connections' local writes now wait only briefly** — A 0.4.0 refresh held the local write lock until it finished. A refresh or sync committed a chunk at a time now leaves a gap between chunks, so another connection's write waits for about one chunk (around half a second), at the cost of roughly one to two seconds more for 100,000 rows
- **A row saved from an entity read before another writer's content came down is reported as `ModifiedOnServer` even after a change from a newer entity was sent** — 0.4.0 uploaded such a row without a conflict

#### Generated code

- **The keyed `AddGenerated{Dialect}Repositories` now resolves the connection factory from DI** — An `ISqlConnectionFactory` registered again later under the same key takes effect for the repositories and `SqlExecutor`

#### DB sync & DDL

- **A sync script keeps the other foreign-key rebuilds when one foreign key cannot be re-added** — A sync that fails on a foreign key with mismatched column types now lists the drop and re-add of the other foreign keys after the failing statement, and the confirmation shows the warnings about those re-adds too. The database ends up the same as before
- **A sync script is refused only for the names it writes** — A table that holds a column or constraint name too long for the dialect, or one containing a line break or control character, no longer stops a sync whose selected changes do not write that name. DDL generation still checks every name in the diagram

### Fixed

#### AI chat & mock generation

- **GitHub Copilot can connect through GitHub Copilot CLI 1.0.113 and later** — With these CLI versions, the Copilot connection (chat and mock generation) failed at startup with a protocol version mismatch. GitHub Copilot CLI 1.0.113 or later is now required, and with a CLI that QuickER cannot talk to, the message says whether to update the CLI or QuickER.

#### Diagram files

- **An external change to the open diagram file is no longer missed while another program holds the file open without sharing** — since 0.4.0, a backup or antivirus tool that opened the file right after an external save could make QuickER drop the notice of that change

#### DB sync & DDL

- **SQLite schema import and diff sync leave out the tables of bidirectional sync** — Selecting everything in a sync could drop the tables whose names start with `quicker_sync_`, losing the unsent changes recorded on the device. `quicker_sync_` is now a reserved prefix: your own tables whose names start with it are no longer imported either
- **The sync confirmation for dropping a unique constraint that a foreign key relies on now matches its list** — The note said "the unique constraint(s) below" but listed foreign keys; it now introduces the list as foreign keys. A foreign key relying on two dropped unique constraints is listed once, not twice.
- **On MySQL, a sync that rebuilds two or more unique constraints on columns a foreign key references now warns and re-adds foreign keys to match the plan after one rebuild is called off** — The sync confirmation kept warnings decided before the rebuild was called off, and foreign keys to re-add were placed after new foreign keys.
- **Repeated syncs to Oracle in one process no longer hit the open cursor limit (ORA-01000) partway through a script** — Oracle import, connection tests, and sync no longer use the ODP.NET statement cache. The cache could hold cursors up to just under the limit, and a script stopped partway through its DDL stayed partly applied.

#### Generated code

- **The blob uploads of the generated HTTP client now follow the HttpClient's `DefaultRequestVersion` and `DefaultVersionPolicy`** — until now, `Write{Column}Async` and the two-way sync's blob uploads went out as HTTP/1.1 even when the HttpClient was set to HTTP/2. Nothing changes unless HTTP/2 is configured.

## [0.4.0] - 2026-11-16

Highlights of this release:

- A graph save now refuses a graph whose navigations and foreign keys disagree, and the EF Core repositories now write alternate keys, so every backend saves the same rows
- On MySQL and Oracle, a sync the database refuses no longer leaves constraints dropped, and the confirmation names the constraints at risk; a SQLite sync leaves out changes that would break a view, a trigger or an index
- Named queries and LINQ conditions now compare a value the column's type cannot hold as C# does, on SQL Server, PostgreSQL and SQLite
- Fixed more cases where bidirectional sync stopped for good or lost changes
- Check "Breaking changes" before upgrading

### Breaking changes

#### Generated code

- **The generated `DbContext` now gives every relationship except `ON DELETE CASCADE` the delete behavior `ClientNoAction`** — a graph save that deletes a parent still referenced through a `NO ACTION` foreign key by a child in the same save is refused (EF Core used to empty the child's foreign key). Code that uses `QuickErDbContext` directly and relied on that should change the children's foreign keys itself before deleting the parent
- **A graph save now refuses a graph whose navigations and foreign keys disagree, on every backend** — it throws an `InvalidOperationException` before it writes anything. Set the foreign key to the row the navigation holds, or leave it unset on a new row
- **A new child whose foreign key is unset now takes its parent's key in a graph save, on every backend** — the QuickER repositories and the in-memory repository (and the remote services in front of them) used to write the unset value, which the foreign key refused. Code that read the unset value in the child's Before hook now sees the parent's key
- **A graph save now refuses a row changed through two instances, on every backend** — the QuickER repositories and the in-memory repository used to write the row twice (when only one instance carries changes, EF Core now saves it as well). Make the changes on a single instance
- **A graph save now refuses one instance with changes that it reaches through two paths, on every backend** — holding the same instance in two collections of the save, or passing it twice in the list of a save of many roots, now throws an `InvalidOperationException` before anything is written. Take the row out of one of the collections, or pass it once
- **The EF Core repositories now write a changed alternate key (a column a foreign key references other than the primary key) instead of dropping it, and changing one that other rows still reference fails** — each updated row of a table with an alternate key costs one extra query, and `UpdateAsync` of such a table runs in a transaction. To change a value other rows refer to, point those rows at another row in one save and change the value in the next
- **A primary key column is now generated with a non-nullable type (`int`, not `int?`) even when the diagram marks it as nullable** — code that assigns null to the key or reads it through `.Value` or `HasValue` no longer compiles after regenerating (to make the diagram agree, clear the nullable mark on the columns an Info message names). With such a diagram, `AddGeneratedInMemoryRepositories()` no longer throws at startup, and code with bidirectional sync compiles
- **The code generated for a MySQL diagram now types a `timestamp` column as `DateTime` instead of `DateTimeOffset`** — MySql.EntityFrameworkCore used to drop the fractional seconds when reading, and saving the row wrote the truncated value back. The column's property and a named query parameter whose type token is `datetimeoffset` become `DateTime`, so change the code that uses them
- **The code generated for a MySQL diagram now types a `char(36)` column as `Guid` instead of `string`** — a named query parameter whose type token is `guid` becomes `Guid` as well. With MySql.EntityFrameworkCore, a `char(36)` column that holds values other than UUIDs can no longer be read, so change its type in the diagram to `varchar(36)` or another type (an uppercase UUID comes back lowercase once the row is saved)
- **On PostgreSQL, `ExecuteDeleteAsync` of the EF Core repositories now refuses a condition with a `TimeSpan` outside the range of a `time` column** — without `cascadeDelete` it fails with `22008: time out of range`. Pass `cascadeDelete: true`, or keep the compared value within 00:00:00 to 24:00:00
- **The generated `QuickErDbContext` now overrides `ConfigureConventions`** — an override you wrote no longer compiles (CS0111); move its body into `ConfigureConventionsPartial(ModelConfigurationBuilder)`. On SQL Server and Oracle, the EF Core repositories convert an integer in a projection or a calculation to `decimal(38,19)`, so the results have more fractional digits
- **A named query that mixes `DateTimeOffset` and `DateTime` between a column and a parameter or list is now skipped with a warning** — such a query gave answers that depended on the time zone of the machine running it. Its method disappears when you regenerate, so declare the parameter with the type token that matches the column (`datetimeoffset` or `datetime`), or type it by the column

#### Bidirectional sync

- **When a write to the server fails and the lookup of why fails as well, the sync now throws an `AggregateException`** — a connection dropped while uploading often reaches the caller in this form. Catch `AggregateException` too where you catch the exception of a failed connection, and look into its inner exceptions (the write's failure first, the lookup's second)

### Added

#### Generated code

- **`ISqlBatchExecutor` runs several raw SQL statements as one transaction** — `ExecuteSqlBatchAsync` commits when every statement succeeds and rolls back when one fails. The generated SQLite `SqlExecutor` implements it

#### Bidirectional sync

- **`SyncTableDescriptor.CopyUniqueValues` writes the values of a table's unique constraints from one copy of a row into another** — a download uses it to settle rows whose unique values collide. When you build a descriptor yourself, set it together with `LocalUniqueLookups`

### Changed

#### AI chat & mock generation

- **The API key connection to Claude (Anthropic) now uses API version 2026-10-21 instead of 2023-06-01** — the Anthropic SDK moved to 13.3.0
- **The model candidates now include `gpt-6.1-terra` for OpenAI and `claude-fable-5-2` for Claude** — `claude-fable-5-2` replaces `claude-fable-5-1`. The defaults stay `gpt-6.1-sol` and `claude-sonnet-5-5`

#### DB sync & DDL

- **A SQLite sync now leaves out table rebuilds that a collation or function registered outside the database would make fail, and names them in the confirmation** — one index that uses such a collation also leaves out the rebuilds of the other tables. Drop the named index or generated column, or switch it to a built-in collation or function, then import again
- **A SQLite sync now leaves out changes that would break an index, a trigger or a view in the database, and names them** — a change that broke an index or a view used to roll back the whole sync, and one that broke a trigger was committed and failed when the trigger fired. To sync the left-out changes (every column drop on the table, when an index is the cause), change or drop the named object in the database first
- **DDL generation now refuses a diagram that has a table without columns, naming the table, and a sync leaves out adding such a table and names it** — the DDL used to fail with a syntax error on every dialect but PostgreSQL, which created a table that cannot be imported back. Add at least one column to the table in the diagram before generating DDL
- **DDL generation and diff sync now refuse a diagram that has two tables with the same name, or two columns with the same name in one table, naming them** — names that differ only in letter case or in leading and trailing spaces count as the same name. Rename one of them in the diagram

#### Import and export

- **Excel definition document relationship rows that share the same tables and columns are now each read as a relationship instead of being refused as duplicates** — a diagram with two foreign keys over the same columns could not be read back from its export

#### Generated code

- **`[SqlColumnType]` on a `datetime2`, `time`, or `datetimeoffset` column now carries the column's fractional seconds as `Scale`** — regenerating adds `Scale = n` to the attribute (`Scale = 7` when the column declares none)
- **The documentation now states that SQLite stores `decimal` values rounded** — a value with more than about 15 significant digits is stored rounded, and a value near the end of the `decimal` range cannot be read back once stored
- **EF Core generation now warns about a foreign key whose referenced column is neither the primary key nor the only column of a unique constraint** — EF Core fails when it tracks two rows with the same value. Give the column a unique constraint or refer to the primary key
- **A named query that compares a `bigint` (`long`) column with a literal holding a decimal point is now generated instead of skipped with a warning** — a value object column that wraps an integer still refuses the literal with a warning
- **The code generation guide now states how a `DateTimeOffset` column compares** — by the instant, as in C#, but the result can differ on SQLite and with PostgreSQL's `timestamptz` and `timetz`
- **The documentation now states that comparing an integer column with a decimal column in the EF Core repositories on SQL Server, PostgreSQL and Oracle can fail with an arithmetic overflow** — the QuickER repositories and the EF Core repositories on MySQL do not fail

#### Distribution

- **The launcher `QuickER.exe` and `Update.exe` that the installer and the portable zip place next to the app are now x64 instead of x86** — an installed QuickER switches to them when it updates to this version

### Fixed

#### Names in a diagram

- **Duplicating or pasting a table no longer copies the name of a named unique constraint when it collides with another constraint in the diagram** — the DDL created the same constraint name twice, which SQL Server, PostgreSQL and Oracle refused. The copied constraint gets a name made from the new table name
- **The property panel and `set_unique_constraint` now refuse a constraint name that another constraint in the diagram already uses** — the property panel compares unique constraint names and relationship constraint names, ignoring case. When a hand-edited diagram has two unique constraints with the same name, give one of them a free name

#### Diagram editing

- **A table added with the toolbar now gets an ID column of the target DBMS's default type** — the column was always `int`, which Oracle's type catalog does not read (Oracle now gets `NUMBER(10)`)
- **Holding Space to pan the canvas no longer presses a button that gets the focus during the pan** — the key's auto-repeat could press or toggle a button, check box or grid cell the focus moved to, with Tab for example
- **Removing a column that a named query refers to now names the query** — the GUI names it in the status bar, and `remove_column` in MCP and the built-in chat names it in the response. The queries are not rewritten, so undoing the removal in the GUI brings the references back

#### CLI & MCP

- **`get_diagram_summary` now shows a primary key column marked as nullable as `NOT NULL`, and writes a table's or column's description on one line** — only MCP showed such a column differently from the DDL and the built-in chat's summary. In both MCP and the built-in chat, a line break in a description becomes a space

#### AI chat & mock generation

- **GitHub Copilot no longer loses the conversation when the runtime returns an ordinary error** — only a lost connection ends the conversation, and then the message says the next message starts a new conversation
- **With an API key, a response cut off at the output limit no longer runs its unfinished tool call, and a response that ends without any text or tool call now says why** — the tool call ran with incomplete arguments, and the empty response looked like a success
- **With an API key, Claude's output limit is now the model's own limit, up to 64,000 tokens, instead of 8,192** — a long answer such as a mock project page could stop before any text. If the model's limit cannot be read, 8,192 is used as before
- **Mock project generation with an API key now tells the AI the namespaces of the generated data layer, and `README-QuickER.md` names them correctly** — the README pointed to `<project>.Generated`, which declares no types, so the generated pages did not compile
- **Before generating a mock project, QuickER asks for confirmation when the tables in the diagram differ from the tables the mock design was made from** — a screen that uses a table no longer in the diagram was not made, but generation still reported success
- **Mock project generation no longer reports success when screens were not made** — with an API key, a turn that submitted no files fails the run, and for Blazor the `@page` routes are counted against the screens in the design. For WPF a shortfall is only logged as a warning
- **Mock project generation with an API key now passes the text of the additional-instructions field to the AI** — the other backends passed it, but the API key backend silently dropped it
- **With Codex, a request whose message Codex is not reading now ends when you cancel it or when its 30-second timeout passes** — a large message sent while the app was busy could not be cancelled, so the turn or the mock generation waited until Codex itself exited
- **A mock folder whose `mock.json` lists the same screen file twice no longer opens, and the message names the file** — this happens only when `mock.json` is edited by hand. File names that differ only in case count as the same
- **The mock generation dialog no longer writes the design document (`README.md`) into the mock folder while a mock project is being generated** — the button was enabled during generation. Export the design document after generation ends
- **Choosing a mock folder whose contents cannot be listed (for example, a folder whose permissions deny listing) no longer stops the mock generation dialog with an error** — the dialog now says it could not open the folder and does not start the conversation
- **Mock project generation now asks before writing to an output folder that cannot be listed, and its overwrite confirmation lists solutions and projects with the hidden or system attribute** — an output folder that was a reparse point threw an exception before generation started, and a folder whose listing was denied let generation go ahead without asking, even with another project in it
- **A hand-edited `mock.json` now has the entity declarations of its screens normalized when it is opened, and declarations of the same entity on one screen are merged** — before, the CRUD table of the design document could put operations in the wrong column, and the resume prompt passed broken declarations to the AI. `save_screen` also merges an entity declared twice and warns

#### Diagram files

- **A diagram file broken by hand editing is no longer loaded, and the message names where** — this covers cases such as a missing name or type key, a missing table or column identifier, and two relationships that share an identifier, so a file that used to open is now refused. A file with a missing key used to open with defaults or new identifiers in its place, so the original contents or the table's position and colors on the canvas were lost
- **A diagram file in which a relationship, a column pair or a unique constraint refers to a table or column that is not in the diagram is no longer loaded, and the message names the reference and the identifier** — on opening, the relationship or column pair was dropped without a word, and the next save removed it from the file. A broken reference in the primary key order, named queries or canvas layout is still accepted
- **When the open diagram file is written several times in a row from outside (by an MCP tool, for example), the last notice of the change now always carries the final contents** — a notice for earlier contents could arrive later, and QuickER reloaded the diagram once more or asked again whether to discard your unsaved changes

#### Import and export

- **A DBML file or an Excel definition document exported from a diagram with a column that has no type can now be imported** — the import refused the file the export wrote. DBML now writes such a column with an empty quoted type (`id "" [pk, not null]`)
- **The Mermaid export's notices now match what changes on re-import** — it now reports single-column unique constraints on primary key columns, foreign key markers on primary key columns that the import does not infer back, and the foreign key column mappings the import would infer, and it no longer reports nullability when the only NOT NULL columns are primary key columns. The first two disappeared on re-import without a notice
- **Importing C# code into a diagram keeps a column type that differs from the current diagram only in letter case** — on a SQLite diagram, `int` came back as `INT` and every import asked to confirm the replacement
- **Importing an Excel definition document or C# code now reads primary key columns as NOT NULL** — a primary key row with an empty required cell, and a primary key property whose type carries `?`, came into the diagram as a nullable primary key. A merge import into a diagram whose primary key is marked nullable asks to confirm the replacement

#### DB import & connections

- **On Windows, a SQLite database file can now be moved, renamed or deleted right after QuickER creates, imports or syncs it** — these were refused as a sharing violation. This also applies after the connection test
- **The "Saved Connections" list in the DB Sync dialog now shows only the profiles for the diagram's dialect** — after choosing a profile of another dialect, Rename and Delete acted on a profile that was not shown, without asking
- **Saving or renaming a profile in the connection dialog no longer creates a second profile with the same name** — this happened when the dialog could not read `connections.json`, and when another program saved a profile with the same name after the dialog opened. Saving now reads the file again first and asks before overwriting, and the status line says when the file cannot be read
- **When `connections.json` is corrupted, the connection dialog now says so in the status line, with where the file is and where a copy of it is kept** — the dialog opened with an empty list and no message, so the saved connections looked lost

#### DB sync & DDL

- **A sync no longer fails on a new constraint that takes the name of a constraint left in the database** — with the default selection, the addition was refused for a diagram that moved a named foreign key or unique constraint to another table, among others. The sync now leaves such an addition out and names it in the confirmation; select the drop too to apply both
- **On PostgreSQL, the DDL and the tables a sync creates keep a unique constraint on the same columns as the primary key or as another unique constraint** — the table had one constraint fewer than the diagram, and the next comparison showed it as an addition
- **On MySQL and Oracle, the confirmation now names the constraints dropped before a statement the existing rows may refuse** — when a statement such as a primary key or unique constraint on columns with duplicate values or `NOT NULL` was refused, the constraints stayed dropped without a word. On Oracle, this also covers lowering the precision, scale or fractional seconds of a column with rows and changing its type kind (ORA-01440, ORA-30082, ORA-01439)
- **A SQLite sync no longer commits after skipping the rest of the script when a statement is refused** — the sync reported success and committed only the statements before it. It now fails and rolls back the whole script
- **On SQLite, a sync that makes a column holding NULL the primary key no longer replaces the NULL with new row numbers** — when the new primary key was a single column declared `INTEGER`, the sync committed without a warning. The sync now fails and rolls back
- **On MySQL and Oracle, a sync that the database refuses no longer leaves constraints dropped** — constraints were left dropped by, for example, dropping every unique constraint a foreign key refers to (MySQL, 1553). The statement the database refuses now runs first, so the database stays unchanged
- **On SQL Server, dropping one of two unique constraints on the same columns and changing the type of those columns no longer fails**
- **On Oracle, moving the primary key onto the columns of an existing unique constraint no longer fails with ORA-02261 and leaves the table without a primary key** — the sync drops that unique constraint and names it in the confirmation
- **On SQLite, after a sync that changes only one of two foreign keys on the same columns, the diff shows the other one as a change** — the next sync left a single foreign key
- **A sync whose selected column drops would leave a table without any column now leaves those drops out and names the table and the columns in the confirmation** — PostgreSQL created a table without columns that could not be imported back, and the other dialects failed
- **On SQLite, a sync that drops all the old columns of a table and adds new ones no longer deletes every row** — the rows are kept and the new columns are NULL, as on the other dialects
- **On MySQL, a column whose type spells a zero fractional-second precision (`datetime(0)`, `time(0)`, `timestamp(0)`) no longer shows a change that syncing never clears**
- **On PostgreSQL, adding an unnamed foreign key to a table whose name is long no longer fails with 42710 on a database an earlier version created**
- **A unique constraint without a name no longer takes a name another constraint in the diagram already uses** — the generated name could match the name of another constraint, and the DDL and the sync then failed with a duplicate name. The generated name now gets `_2` (or the next free number) only when it is taken
- **When the diff cannot be recomputed after a committed sync, the DB sync dialog no longer closes as if the diagram and the database matched** — it stays open and shows the failure in an error message
- **The warnings in the sync confirmation now name the foreign key you add, not an existing one** — this happened when you added a second unnamed foreign key between the same two tables
- **A foreign key whose two ends have different types that the database accepts is no longer reported as a type mismatch** — a sync that changed only the nullability of its columns, or the type of another pair of a composite foreign key, made a change to the primary key it refers to fail (PostgreSQL 2BP01, Oracle ORA-02273). SQL Server and MySQL still need the same type on both ends
- **On PostgreSQL and Oracle, a column change that leaves a kept foreign key with ends that do not match is now named in the confirmation** — it failed without a warning (PostgreSQL 42804, Oracle ORA-02267), and on Oracle a foreign key the sync re-creates was left dropped. The column change now runs first, so the database stays unchanged
- **On PostgreSQL and Oracle, changing both ends of a foreign key to the same other type no longer fails** — the database refused the first column change (PostgreSQL 42804, Oracle ORA-02267). On Oracle, the confirmation names such a foreign key when the change alters the column's type kind
- **Adding a foreign key between existing columns whose types do not match is now left out of the sync and named in the confirmation** — it failed without a warning (MySQL error 3780, SQL Server Msg 1778, PostgreSQL 42804, Oracle ORA-02267)
- **On MySQL, dropping a unique constraint whose leading columns are the only key a foreign key refers to is now named in the confirmation** — MySQL refuses the drop (1553) while such a foreign key remains. Moving the primary key onto the referenced columns while dropping that unique constraint now re-creates the foreign key on the new primary key
- **On MySQL and Oracle, adding a foreign key that existing rows refuse no longer leaves the foreign keys the sync re-adds dropped** — when rows with values missing from the parent refused a new foreign key (MySQL 1452, Oracle ORA-02298), the re-adds never ran. Re-creating a foreign key whose referenced columns may no longer be a key now runs last and is named; if the database refuses it, only that foreign key is left dropped
- **When table or column names differ only in case between the diagram and the database, foreign keys and unique constraints whose default names are shortened to the identifier limit no longer show up as re-created in the sync diff**
- **The type and nullability changes of a column that is computed only in the diagram can now be selected in the sync** — it was compared as a computed column, so a nullability difference was not shown, and a type change could not be selected on SQL Server, Oracle and SQLite
- **Syncing a diagram that moves a named unique constraint to other columns and adds an unnamed one on its old columns no longer leaves two unique constraints on the same columns with the default selection** — Oracle refused it with ORA-02261. The addition is now left out and named in the confirmation
- **On SQLite, the sync no longer rebuilds a table that holds something the import left out of the diagram** — such columns and foreign keys were dropped together with their data. Fix what the import warning names in the database and import it again to sync the table
- **On MySQL, the sync no longer refuses to reorder the columns of a table that has a column whose imported type cannot be written out**
- **On SQLite, the DDL and the sync now create a foreign key whose parent table name has a dot, when the child is in the same database** — it was a syntax error, so the DDL of such a diagram could not be run. A foreign key between tables of different databases is left out, with a comment before the child table
- **On SQL Server, the warning about a `SET DEFAULT` foreign key on a NOT NULL column now looks at the column as it will be after the sync** — it looked at the diagram, so the database could refuse the foreign key without a warning, or a warning that did not apply was shown

#### Generated code

- **On SQLite, the QuickER repositories now compare an integer column with a floating-point value, and a floating-point column with an integer value, as `REAL` values** — an integer beyond 2^53 gave a different answer from C#
- **A named query that compares a `float` column with a number literal now gives the C# answer on the PostgreSQL EF Core repositories** — the literal was compared as a double-precision number, so some matching rows were missed
- **On SQL Server, the QuickER repositories now compare binary values with their lengths in queries** — values that differed only in trailing `0x00` bytes were treated as equal. The EF Core repositories keep the SQL Server comparison on a variable-length column
- **On SQL Server, the QuickER repositories now compare a `time` column with a `TimeSpan` outside its range (a negative value or a value of a day or more)** — such a comparison failed with an `OverflowException`. The EF Core repositories still fail (a known limitation in the generated code guide)
- **On SQL Server, `BulkInsertAsync` of the QuickER repositories now stores the same fractional seconds as `InsertAsync`** — the fractional seconds were truncated. It now also stores a value whose excess over the column length is only trailing spaces or `0x00` bytes instead of failing the whole copy, and still refuses only an excess that a non-Unicode column's code page turns into spaces
- **On SQL Server, the QuickER repositories now compare a column with a value its type cannot hold as C# does** — a fraction or an out-of-range value against an integer column, a decimal with more digits, a time of day against a `date` column, a character outside ASCII against a `varchar` column, and so on matched other rows or threw an exception. An index on the column may not be used for such a comparison
- **On SQL Server, the EF Core repositories now compare a column with a value its type cannot hold as C# does** — such a value matched other rows, and `ExecuteDeleteAsync` deleted them (a string constant written in hand-written LINQ is not covered). When you build the `DbContextOptions` yourself, add `SqlServerQueryParameterInterceptor.Instance` with `AddInterceptors`
- **On SQL Server and Oracle, the EF Core repositories no longer fail with an overflow when a `bigint` column is compared with a decimal** — one row with more than 16 integral digits made the query fail with an arithmetic overflow (ORA-01438 on Oracle) (see Breaking changes)
- **On PostgreSQL, the EF Core repositories now compare a `date` column with a `DateTime` that has a time of day as C# does** — the time of day was dropped before the comparison. When you build the `DbContextOptions` yourself, add `PostgreSqlQueryParameterInterceptor.Instance` with `AddInterceptors`
- **On PostgreSQL, the EF Core repositories no longer throw a `NullReferenceException` for an IN over a value object column whose values hold `null`** — a query written on `QuickErDbContext` directly is not covered
- **On SQL Server, the EF Core code now stores the four fractional digits of a `money` or `smallmoney` column** — the values were stored rounded to two fractional digits
- **On PostgreSQL, the EF Core repositories can now query a `money` column** — a compared value is rounded to two fractional digits. PostgreSQL refuses a negative number written as a literal against the column, so pass such a value as a parameter
- **On SQLite, the EF Core repositories now find the rows where an integer column equals a decimal holding a whole number, in equalities and IN lists** — the two were compared as text, so `!=` returned every row
- **In a graph save of the EF Core repositories, a row a save hook's Before edits and then declines with `false` is left unwritten with the values the hook wrote, as in the QuickER repositories** — a skipped row had its edits reset, was written as an UPDATE, or failed the whole save
- **In a graph save over the remote services, the values a server-side save hook's Before writes to a skipped or deleted row now reach the client's entities too** — the save response has a new field, `RemoteSaveResult.Unsaved`. A response from an earlier server still brings nothing back for these rows
- **A bulk insert and a graph save of the EF Core repositories no longer change the navigations of the entities they are given** — a bulk insert writes each row's own foreign keys in the order given, so pass a self-referencing parent before the rows that refer to it. As in the QuickER repositories, the one change a graph save makes is that the children it deleted leave the collections of the parents it kept
- **`AddGeneratedInMemoryRepositories()` no longer throws at startup when the diagram has a table whose primary key is not a single column** — sample data no longer goes into such a table, or into any table that refers to it directly or through other tables. `seedSampleData: false` is no longer needed as a workaround
- **Generated code for a diagram with no table that can have a repository (every table has a composite primary key, or none) now compiles** — this happened with the QuickER repositories, the EF Core repositories or the in-memory repository turned on
- **When a named query on a table whose primary key is not a single column (a composite key or no key) is not generated, a warning now names each such query** — only the warning that skips the table's repository was shown, which did not tell that the queries were gone
- **The EF Core code generated for a PostgreSQL diagram can now write `date`, `timestamp`, `time with time zone`, `json`, `jsonb` and `xml` columns, and arrays of them** — a `DateTime` of `DateTimeKind.Utc` still cannot be written to a `timestamp` column
- **The EF Core code generated for a PostgreSQL diagram no longer stores a `TimeSpan` outside the range of a `time` column without its days** — PostgreSQL refuses such a value with `22008: time out of range`, and `ExecuteDeleteAsync` without `cascadeDelete` refuses it as well (see Breaking changes)
- **A named query whose simple DSL condition is invalid is now skipped with a warning instead of stopping C# code generation** — `quicker generate` and `quicker scaffold` finish with exit code 0, so a CI job that relied on the exit code no longer stops. Code that calls the skipped query's method stops compiling instead
- **The EF Core repositories now write a row of a table whose other columns are all produced by the database (a table with only a key, a key and a row version, or an alternate key and a row version)** — `UpdateAsync` returned `true` for a row another writer had deleted, and a stale row version went unnoticed
- **On SQLite, the stream accessors of an excluded unbounded binary column of the QuickER repositories now work on a table whose name has a dot** — they failed with "no such table"

#### Code generation dialog

- **The query definition dialog now names the query whose condition has errors** — when a query referred to a removed column, the dialog refused OK for every query until it was fixed, without saying which query

#### Bidirectional sync

- **A sync no longer stops for good when a local row holds a unique value the server gave to another row** — when the rows keeping the holder have nothing to send, they and the holder are removed and listed in `LocalRowChanges` with the new reason `SyncLocalRowChangeReason.RemovedForUniqueValue`. A download also no longer stops with a unique constraint error after it has read back the holder's new parent
- **A sync no longer stops for good when the server moves a row with children to a new parent and gives the new parent the unique value of the parent it deleted** — a new table, `quicker_sync_row_pending`, is created in the local database, including one from an earlier version. The same holds when the server moves a row to another parent and gives it the unique value of a row it deleted, or, with `ServerWins`, moves and renames rows in a cycle
- **A graph save that removes a saved row together with a new child no longer fails through the journaling decorator** — a new child whose key was not set yet, for example, made it throw a `FormatException`, only with sync support registered. A later sync also no longer deletes a versionless row that another device created on the server under the same key
- **A graph save through the repository of a table excluded with `excludeFromSync` now records the synchronized rows it cascades into** — a child added under an excluded parent was never recorded, and the next sync removed it from the local database without sending it. A graph save through any repository no longer records the rows of an excluded table
- **A change another device made on the server is no longer lost without a conflict after a sync or a refresh that read a row back while applying an older copy of it** — the device kept the batch's older copy, and a later edit overwrote the server's newer content. The next run now brings the newer version down, and an edit made before it is reported as a conflict
- **`AddGeneratedSyncEngine` now works over local repositories registered as singletons, and `AddGeneratedSyncEngine(null)` over ones registered without a key** — with singletons, a provider that validates scopes refused it or one journal was shared across every scope, and without a key it threw. A registration by type or by instance is now wrapped as well
- **`UpdateAsync` through the journaling decorator no longer fails on a row whose key is not set** — recording the update threw before the repository was called. The update now ends with the repository's own result
- **A download batch the local database refused no longer leaves blob copies owed for its rows** — the next run copied the server's blobs into rows that still held their older content
- **With `ServerWins` and delete propagation off, a row put back from the server whose parent stays out for a unique value no longer stops the sync on the local foreign key** — the row is held back with its parent
- **An upload now sends again, in the same run, a write the server refused for a foreign key or a unique value once another write has gone through** — such a write went up only on a later run, or was discarded in a last-write-wins `ServerWins` run
- **A `ServerWins` run that removes rows ahead of the download drops their entries at the same time**
- **A delete that fails is reported as `DeleteRefusedOnServer` only when a row on the server points at it** — a passing failure is now thrown instead of reported as a conflict, and the next run sends the delete again (an `AggregateException` when the check cannot read the server's rows either; see Breaking changes)
- **A new row whose key a Before hook sets while a sync is uploading is no longer dropped from the journal** — the row never reached the server. A graph save of a new row whose value-object key a Before hook sets no longer fails when the repository is wrapped for sync, and with `ServerWins`, `LocalRowChanges` no longer names a row that does not exist
- **A row whose planned delete is recreated under the same key before the upload sends it now goes up as an update** — it was sent as an insert on every run and got a spurious `DuplicateOnServer` (a `ServerWins` run overwrote the recreation instead). A row deleted and recreated, then deleted again while a sync is uploading, is now deleted on the server as well
- **A `ServerWins` run no longer drops the earlier entries of a row the application edits while the run is in flight** — a blob written before the run never reached the server, and a row deleted and recreated before the run was refused as a duplicate on every run
- **A refresh no longer fails on every run when the synchronized tables refer to each other in a foreign key cycle** — with `Force`, the unsent changes were dropped first. The fix covers the generated SQLite executor (see Added); a cycle among tables that use a wrapped executor or an `ISyncTable` of your own still fails as before
- **A sync started from a local save hook that another run fires while applying the server's rows is now refused at once** — it used to fail with `database is locked` after the busy timeout. It now throws `InvalidOperationException`, the run that fired the hook stops with it, and the batch it was applying is rolled back

## [0.3.0] - 2026-10-25

Highlights of this release:

- Names in a diagram (surrounding spaces and letter case) and identifier length limits are now handled the same way across import, editing, DDL and generated code
- DB sync no longer fails on changes you did not select or on foreign keys whose types do not match, and no longer loses foreign keys
- Fixed bidirectional sync stopping again and again on foreign keys and unique values, and losing changes
- The backends of the generated code (QuickER Repository, EF Core repository, in-memory and remote) now give the same results
- Check "Breaking changes" before upgrading

### Breaking changes

#### Code generation dialog & CLI

- **Path names such as the output folders and file names now reject more forms** — reserved device names (`CON` and so on), segments longer than 255 characters, whitespace at the start of a segment, a `.` or whitespace at the end of a segment, and segments containing `~` give an error naming the rule, so rename them
- **An output file name (`OutputFileName`) that is not a single file name now fails before generation** — choose the folder with the output folder and `CodeSubdirectory`
- **`quicker generate` now resolves column types in the diagram's target DBMS when `--provider` is omitted** — generating a Repository for a PostgreSQL, MySQL or Oracle diagram without `--repository-dialects`, and a diagram whose target DBMS names no supported dialect, now fail before generation. Scripts that relied on the old default (`sqlserver`) should pass `--provider sqlserver` explicitly
- **`quicker reverse` no longer overwrites an existing output file without `--force`** — pass `--force` to overwrite
- **A named query whose type token is a fixed-length string or binary without a length now fails generation** — give the token a length, as in `fixedstring(10)`
- **MySQL unsigned integer columns now generate unsigned C# types (`byte`, `ushort`, `uint`, `ulong`)** — a signed `mediumint` also changes from `string` to `int`. Update code that uses the properties of such columns

#### Generated code

- **A diagram whose tables or columns give the same generated type name, or the name of a type the generated code already uses, now stops at generation with an error** — rename the tables or columns it names
- **Two tables whose generated names differ only in case, such as `order_item` and `orderitem`, are now refused** — rename one of the tables
- **Entity member names that differ only in case now stop generation with an error** — rename the columns
- **A graph save now takes the children it deleted out of their parent's collection** — code that looked for the deleted rows in the collection after a save should keep its own reference to them before saving
- **`QueryBySqlAsync` on the EF Core repository now refuses a query that leaves out an excluded unbounded binary column** — select the excluded columns too, or use `SELECT *`
- **A named query whose mini DSL condition compares an integer column with a number outside the column's type is now skipped with a warning** — compare with a value inside the range
- **A projection whose result type name is a type name of the generated code is now refused** — rename the result type
- **A mini DSL string match on a GuidKey column, other than an exact `LIKE` or `=`, is now skipped with a warning** — compare with an exact match
- **PostgreSQL `time with time zone` columns now generate `DateTimeOffset`, and array columns an array of the element type (`integer[]` becomes `int[]`, and so on)** — update code that uses the properties of such columns. To write an offset other than UTC to `timetz`, set the column type in `OnModelCreatingPartial` of `QuickErDbContext`. Array columns are left out of edit models and value objects
- **Oracle `TIMESTAMP WITH LOCAL TIME ZONE` columns now generate `DateTime`** — the property type changes, but reading such a column always failed before
- **Oracle `INTEGER` and `SMALLINT` columns now generate `decimal`** — the property type changes, but reading such a column always failed before
- **PostgreSQL `interval` columns now generate `TimeSpan`, `inet` columns `System.Net.IPAddress`, and `cidr` columns `System.Net.IPNetwork`** — update code that uses the properties of such columns. `inet` columns are left out of edit models and value objects
- **SQLite `REAL` columns (types with REAL affinity) now generate `double`** — change code that uses the `float` properties to `double`
- **SQL Server `float(1)` to `float(24)` columns now generate `float`** — change code that assumes `double` to `float`
- **A stored value a value object rejects is now reported as `InvalidOperationException` naming its column on every read** — code that catches `ValueObjectValidationException` should catch `InvalidOperationException` and look at `InnerException`
- **The in-memory backend now rejects a navigation property in a query condition or ordering with `NotSupportedException`** — filter on the foreign-key column instead
- **`RemoteEntityGraph.CascadeNavigations` was removed from `QuickER.Runtime`** — call `EnumerateCascadeChildren()` on the entity instead
- **`InMemoryCascade`, `IncludeAttacher`, `InMemoryReadScope` and `InMemoryWriteScope` in `QuickER.Runtime.InMemory` are no longer public** — use the generated repositories or `InMemoryDataStore.Put` / `Snapshot` / `Find`
- **`SelectMaterializer` and `MapEntity` were removed from the `EntitySaveMetadata` of `QuickER.Runtime.EntityFrameworkCore` and of code generated for EF Core only** — use `MapEntityFromRawSql` or `ISqlExecutor.QueryBySqlAsync`
- **Type and property names made from a name that mixes kanji or kana with ASCII letters or digits now keep the kanji and kana part (`顧客ID` becomes `顧客Id`)** — update code that uses such names
- **A mini DSL `NOT` now matches a row whose column is `NULL`, as C#'s negation does** — to keep `NULL` rows out, write `qty IS NOT NULL AND qty >= 10`
- **A new row's edit model now reads an empty string or an empty byte array of a column that is not a value object as no input (null)** — a new row with no input stops at the required check of a NOT NULL column, and an empty string an `OnEntityCreated` hook sets is saved as NULL in a nullable column

#### Import and export

- **A DBML line that cannot be interpreted is now reported as an import error with its line number** — the block form `Ref { ... }` is rejected as well. Blocks such as `Project` and `Enum` are still skipped
- **The DBML export now writes standard DBML, and DBML in QuickER's earlier form no longer imports** — for an earlier file, move the settings block after the endpoints and turn `note: 'constraint name'` into `Ref "constraint name": …`

#### DB sync & DDL

- **A `SET NULL` foreign key whose referencing column is NOT NULL after the sync is now left out of the sync on SQL Server and MySQL** — on PostgreSQL, Oracle and SQLite the sync names it in the confirmation, and the DDL adds a comment before it
- **A foreign key addition or recreate whose two ends would have different types after the sync is now left out of the sync (SQL Server, PostgreSQL, MySQL, Oracle)** — make the types of both ends of the foreign key named in the confirmation match
- **On MySQL, one of two foreign keys on the same column can now be moved to another column under the same name** — no action needed
- **A change that needs a change you did not select is now left out of the sync and named in the confirmation** — select the needed change as well to apply it

#### Bidirectional sync

- **The local sync journal table gained a column, and an existing one has to be recreated** — before upgrading, run a sync so the journal is empty, then drop the table with `DROP TABLE quicker_sync_journal`
- **`SyncTableBase.CopyBinaryColumnsUpAsync` now takes the columns to send (`IReadOnlyList<string> columns`)** — code that derives from it and calls it should pass the columns to copy
- **A local save hook that skips rows the sync engine applies now stops that table's download** — return `true` without doing anything while `SyncSession.IsSuppressed` is true

#### Names in a diagram

- **Every name a diagram holds now has its surrounding spaces removed, and names that match ignoring letter case are the same name** — a diagram file with a name that has surrounding spaces is read and saved with the spaces removed
- **The properties panel no longer accepts a name that another table or column already has** — enter a different name
- **DBML, Mermaid and Excel definition document imports now reject two columns of one table with the same name** — rename the columns before importing
- **`set_unique_constraint` rejects a `name` that a constraint of the diagram already uses** — give a different name
- **DDL generation and schema-sync script generation now refuse a name the dialect cannot take (longer than the identifier limit, or on MySQL a character outside the Basic Multilingual Plane)** — shorten the name. See [Identifier length limits](docs/database.md#identifier-length-limits) for details
- **A default constraint name (`PK_`, `FK_`, `UQ_`) now ends in 8 hex digits to fit the dialect's limit, only when it would exceed the limit** — sync with a PostgreSQL database created by an earlier version shows no difference either
- **C# code generation refuses tables, columns of one table and projection fields that differ only in letter case or surrounding spaces, with an error naming them** — rename them

#### AI chat & MCP

- **The ER diagram tools now refuse a missing required argument (`table_name`, `data_type`, `relationship_type` and so on) instead of filling in a default** — pass the required arguments
- **Every tool now refuses an argument of the wrong JSON type (a number where a string is expected, `"true"` where a boolean is expected, and so on)** — pass arguments of the types the schema gives
- **`quicker mcp` no longer reads `appsettings.json` from the folder it is started in** — no action needed

#### DB import & connections

- **DB import no longer imports external tables (Oracle and SQL Server)** — the import warnings name them, and importing again lists them among the tables that would disappear in the replacement confirmation

### Added

#### Code generation dialog & CLI

- **Generation now names the columns whose C# type fell back to `string` (`geometry` and the like) and the columns declared with a negative scale**
- **Generation now warns when a NOT NULL computed column cannot be written on the mirror side (multi-target build, bidirectional sync)**
- **`quicker generate` now warns when `--provider` differs from the diagram's target DBMS**

#### Generated code

- **Generated type names that differ only in case are reported as a warning**
- **A composite primary key's column order is now recorded in `[DbTableMeta(PrimaryKeyOrder = …)]` and round-trips through C# code import and DBML** — the Mermaid export reports it as lost
- **Columns the database numbers and that refuse given values (SQL Server `IDENTITY`, PostgreSQL and Oracle `GENERATED ALWAYS AS IDENTITY`) are now left out of writes with `[IdentityColumn]`** — see [code generation](docs/code-generation.md#columns-the-database-numbers)
- **The generated mappers' `AcceptSaved(editModel, entity)` now takes the result of a save back into the edit model** — call it in place of `AcceptChanges()`, and the next save does not conflict

#### Import and export

- **DBML import now reads inline references, `primary key`, and many-to-one `Ref` lines**

#### AI chat & mock generation

- **AI answers are now shown as formatted Markdown** — the "Markdown" check box switches back to the original text

#### Bidirectional sync

- **Code generation now warns when a synchronized table's primary key is a fixed-length string (SQL Server `nchar` / `char`)**
- **`SyncConflictReason` gains `ParentMissingOnServer`, `DeleteRefusedOnServer` and `UniqueValueTakenOnServer`**
- **`SyncDownloadTruncation.MissingParentTableName` and `SyncResult.LocalRowChanges` were added** — they show the parent's table where the download stopped and the local rows the sync changed on its own

#### DB import & connections

- **A connection profile can now be renamed** — the connection settings and the saved password carry over

### Changed

#### DB import & connections

- **Import warnings are now listed by kind, in the GUI and in `quicker scaffold`** — scripts that matched the old one-line warnings in stderr need updating
- **DB import now names the objects it leaves out of the diagram (such as views) and the definitions it imports in a reduced form (such as default values and automatic numbering)**
- **DB import no longer imports a table whose name contains a dot, and names it**
- **The connection dialog shows each authentication method by name, such as "Windows" and "Microsoft Entra ID"**

#### Code generation dialog & CLI

- **Value object warnings are now shown in a scrollable list**

#### Generated code

- **A graph save that deletes rows now writes the new and updated rows, then the deletions, then the rows held back, and its save hooks fire in that order** — the generated EF Core `DbContext` declares `HasIndex(...).IsUnique()` for each unique constraint
- **`GetByIdAsync`, `DeleteAsync` and the stream accessors now refuse a null key, and `ExecuteDeleteAsync` refuses a query with `Skip` or `Take`** — with `ArgumentNullException` and `NotSupportedException` respectively
- **A named-query parameter can now be named `Query` and the like, and a name that becomes a member of the request record, such as `toString`, is refused**
- **The generated entity's metadata now matches the DDL** — a bare `decimal` (SQL Server, MySQL) records the default precision, and a unique constraint on a schema-qualified table records `UQ_dbo_Orders_code`
- **The in-memory sample data now keeps the diagram's primary keys, unique constraints and foreign keys**
- **Writing an unbounded binary column from a stream now throws `ArgumentException` when the stream does not yield exactly `length` bytes** — a synchronous write to the stream of a binary download follows `AllowSynchronousIO`
- **SQL Server `BulkInsertAsync` now stores `null` even in a column that has a DEFAULT**
- **A SQLite date and time with an offset is now read as UTC on every path, and the in-memory `StartsWith` / `EndsWith` compare ordinally**
- **On SQL Server, `Query()` without `Include` now reads with a plain SELECT** — reading 200,000 rows went from 2.2 s to 0.76 s

#### AI chat & MCP

- **Links in AI answers that carry a user name (`https://user@host/`) are no longer clickable**
- **The AI CLIs are launched differently** — `codex app-server` starts in `%TEMP%\QuickER\codex`, a CLI installed in more than one form in the same folder is picked in `PATHEXT` order, and a `.cmd` shim whose path contains `%` (for Copilot also `!`) is not launched
- **A failed response from Claude Code, Codex or GitHub Copilot now shows a summary and a `Details:` line**
- **The ER diagram tools and `set_query` now note in the result the inputs they ignored or found problematic**
- **The ER diagram tools refuse empty or duplicate names and a second foreign key over the same column pairs, and give the same default constraint name as the GUI**
- **The MCP code generation tools now return results in English, and warn when `provider` differs from the diagram**

#### AI chat & mock generation

- **The mock preview keeps its data in `%LOCALAPPDATA%\QuickER\WebView2`, and a mock project can no longer be named `Design`, `Bin`, `Obj` or `Generated`**
- **In mock generation, Copilot no longer auto-approves a command that references a URL or redirects, and `save_screen` keeps the existing declarations when `entities` is invalid**

#### DB sync & DDL

- **On SQL Server and PostgreSQL, changing a primary key no longer fails on a constraint name containing symbols or on a table with the same name in another schema**
- **On Oracle, DDL and sync scripts with multi-line descriptions now run in SQL*Plus**
- **A computed column's type change can now be selected only on PostgreSQL and MySQL, and its nullability is no longer compared**
- **The sync confirmation shows the tables to rebuild and the notes in a scrollable area**
- **Sync now drops first the foreign keys that lose an end, and a primary key change makes the new primary key columns `NOT NULL`**
- **Changing the columns of a named unique constraint is now one change, and on SQLite and Oracle a unique constraint on exactly the primary key's columns is left out** — on Oracle, a sync that moves the primary key onto the columns of a unique constraint the database already has fails unless that unique constraint is dropped first
- **DDL and sync now name a SQL Server `SET DEFAULT` (1762) and a foreign key whose ends differ in length (1753), an Oracle `ON DELETE SET DEFAULT`, and an SQLite table name with a dot**
- **A composite foreign key whose column pairs differ only in order is no longer a difference, and SQL Server and MySQL write the pairs in the order of the referenced key**
- **On MySQL, altering a column keeps the column attributes the diagram does not model, and dropping a foreign key also drops the index with the same name**
- **A change in the kind of type of a column with a default value is now shown in the sync confirmation (except on SQLite), and a length written with non-ASCII digits (`varchar(５０)`) is refused**

#### Diagram editing

- **Switching the target database converts Oracle `INTEGER` and the like to `decimal(38,0)`, and warns about names the target database would refuse**
- **A new relationship now also pairs the child's primary key column that is the foreign key**
- **Deleting a table also deletes its named queries**
- **Ctrl+clicking a table while creating a relationship now cancels the creation**

#### Import and export

- **Exports, generated code, mock output and saved secrets are now written atomically** — a `name.<GUID>.tmp` file appears briefly in the target folder, and a symbolic link is replaced by a regular file
- **Re-importing from a database or an Excel definition document now keeps a table or column whose name differs only in letter case as the same one**
- **An Excel definition document written in a format this version cannot read is no longer imported**
- **Importing Mermaid or DBML into a diagram that is not empty now always asks first**

#### Diagram files

- **Table names are loaded without leading and trailing spaces, and a diagram file whose required names or types are `null` is no longer loaded**

#### Bidirectional sync

- **A table whose download stops part way no longer holds back the tables after it** — `TruncatedByTableName` is always null
- **A `DateTime` primary key is written to the journal without its kind** — a local database from an earlier version goes on working as it is

#### Settings

- **Choosing the display language that is already shown no longer asks to restart**

#### Dependencies

- **The runtime packages and mock projects require newer versions of the database libraries** — `Microsoft.Data.SqlClient` 7.1.1, `Microsoft.Data.Sqlite` 10.0.13 and `Microsoft.EntityFrameworkCore.Relational` 10.0.13. A project that pins an older version needs to raise it

### Fixed

#### DB sync & DDL

- **On Oracle, changing the primary key of a schema-qualified table, dropping its foreign key, and altering only a column's type no longer fail** — they stopped with ORA-02260, ORA-01442 and similar errors
- **On SQL Server, dropping or changing the type of a column with a DEFAULT constraint, and changing the type of a primary key column, no longer fail** — they were refused with error 5074
- **Dropping a column of a primary key or a unique constraint no longer silently drops or narrows the constraint** — this happened on PostgreSQL, MySQL and SQLite. The confirmation names each unique constraint that is dropped
- **Differences that stayed after the sync no longer appear** — they came from another spelling of the same type, a description made only of spaces, a MySQL description with emoji, an Oracle foreign key with `ON UPDATE`, and similar cases
- **DDL and sync no longer fail on duplicate foreign key names** — this happened with two unnamed relationships and with a foreign key being recreated (the new `AlterForeignKey` item)
- **On SQLite, a view or child rows without a parent no longer make the sync fail**
- **A sync that changes a column's type no longer loses the column's comment (MySQL) or collation (SQL Server, PostgreSQL)**
- **On MySQL, a sync that changes the columns at either end of a foreign key, the primary key, or a unique constraint supporting a foreign key no longer fails** — it stopped with errors 1553, 1075, 1832 and similar
- **Sync no longer drops a foreign key it cannot add back** — a foreign key whose two ends get different types (MySQL) and a change to one of two foreign keys on the same column pair (SQLite) made foreign keys disappear
- **A SQLite rebuild no longer changes a unique constraint or primary key you did not select** — changing a column's type dropped a unique constraint or changed the primary key or its order
- **Switching the target DBMS no longer produces types the DDL fails on or types that cannot hold the values** — lengths beyond the limits and `nchar(max)` are listed as unconvertible, and MySQL's unsigned integers keep their range
- **On PostgreSQL, a sync that changes a column to a type of another family (`varchar` to `integer`, for example) no longer fails**
- **Fixed a sync failure when a table is renamed in the diagram and the new table keeps the unique constraint names (SQL Server, PostgreSQL, Oracle)**
- **Fixed DDL and sync generation stopping for the whole diagram on MySQL when an `enum` / `set` column is longer than 200 characters**
- **After recalculating the diff or running the sync fails, the stale sync script can no longer be run by mistake**
- **A change the SQLite rebuild cannot apply (a unique constraint on a column dropped in the same sync, for example) is no longer reported as done** — it was dropped silently, the sync succeeded, and the same difference came back next time
- 34 more fixes (mainly dialect-specific failures, warnings and display)

#### Diagram editing

- **Delete in the column list no longer removes a column in a way that cannot be undone and does not count as an unsaved change**
- **Switching the target DBMS and importing are now abandoned when the diagram changed while the confirmation was open** — they could discard the changes made in the meantime
- **Large diagrams and long descriptions no longer freeze the window** — "Grid", undo, deleting many tables at once and other operations stalled
- **Fixed the app closing when you redo (Ctrl+Y) the removal of a unique constraint while a relationship that references it is selected**
- **Turning NULL on or off for a column that is not in the primary key can now be undone** — it was not recorded in the history and could be lost without asking, for example by the automatic reload after an outside change
- **A relationship to a table with a composite primary key no longer pairs a key column with the wrong child column** — a foreign key column went missing and the DDL rejected it, or a key column was paired with another column
- **Search no longer selects a deleted or reloaded table from stale matches** — deleting it after a reload also removed the named queries of the table with the same name
- 22 more fixes (mainly undo, creating relationships, and editing operations)

#### Diagram files

- **Restoring the auto-saved diagram no longer leads to overwriting the previous file with another diagram without asking**
- **Saving to a file whose name is 219 to 255 characters long no longer fails**
- **Save no longer overwrites a diagram file changed outside QuickER without asking** — this happened when the file was written while QuickER was reading it, or when it was put back to a version you had chosen to continue over
- 6 more fixes (mainly watching for outside changes and saving)

#### Settings

- **Removing a model from the history with × no longer closes the app when `ai-settings.json` cannot be written**
- **Settings are no longer lost when an older version of QuickER saves them, or reset to their defaults by a momentary read failure**
- 6 more fixes (mainly reporting save failures and reading settings)

#### Distribution

- **Applying an update no longer cuts off a running QuickER without the normal shutdown** — edits were not written to the recovery file, and the AI processes were left running

#### Crash handling

- **A crash now stops the processes started by AI chat and mock generation before exiting**
- 2 more fixes (mainly the crash report dialog and the crash log)

#### Startup & restart

- **QuickER no longer stays closed after you switch the display language or choose "restart now" for an update**
- 2 more fixes (mainly handling the window of a running QuickER)

#### Database import & generated code

- **Bidirectional sync no longer loses a blob a save hook wrote or a row a save hook skipped** — a skipped row stops that table's download and is reported as `SkippedBySaveHook`
- **A remote save no longer loses the values the server set, nor, on a server using the EF Core repository, the excluded blobs**
- **A graph save that deletes rows no longer fails depending on the order of the rows or the backend**
- **Queries no longer return wrong results** — the "no filter when null" condition (SQL Server, SQLite), and `CountAsync` / `AnyAsync` ignoring `Skip` / `Take`
- **Saves in the EF Core repository no longer keep failing** — an insert with key `0`, a retrying execution strategy, and a graph save that resolves 10 or more rows
- **Relationships and computed columns no longer stop generation or produce code that does not compile** — two relationships between the same pair of tables or in both directions, and the mapper for a NOT NULL value-type computed column
- **Named queries no longer produce code that does not compile** — `IN` on a value-object column, an out-of-range integer literal, a name C# cannot use, and more
- **Computed and generated columns, and temporal tables, can now be written to** — the generated INSERT / UPDATE always failed on them
- **The import no longer merges tables of the same name or fails as a whole** — tables whose names differ only in letter case on SQL Server with a case-sensitive collation and on SQLite with non-ASCII names, and SQLite tables whose names contain a dot
- **SQL Server's fractional-second precision and Oracle's `INTEGER` and similar types are now imported correctly** — in a diagram imported by an earlier version, these columns show up as a type change in the next sync
- **Queries and updates no longer always fail on a column named like a keyword (SQL Server) or on a table with no column to update**
- **EF Core no longer rewrites rows or tables outside its target** — a graph save that deleted outside the cascade, an `ExecuteDeleteAsync` that deleted only the lines, and table names with a schema
- **`Read{Column}ToFileAsync` and the HTTP blob transfer no longer damage existing content**
- **SQLite writes no longer fail with "database is locked" while another connection reads**
- **A save whose save hook writes an unbounded binary column no longer makes the next save a conflict (SQL Server) or leaves a half-written column (SQLite)**
- **A SQL Server `Query()` no longer returns `float` / `real` digits and NULL columns differently from `GetByIdAsync` / `GetAllAsync`**
- **`Include` no longer leaves navigations empty** — a primary key and a foreign key with different CLR types (SQLite, in-memory), and a `binary(n)` primary key
- **Conditions no longer give a result different from C#** — a `NOT LIKE` without wildcards and NULL rows, string matches on fixed-length SQL Server columns, SQLite's `.Date`, and `IsNullOrEmpty` / `IsNullOrWhiteSpace`
- **Switching to SQL Server no longer silently turns a string column with no length into a one-character column**
- **A `TimeOnly` value with seconds is no longer cut down to the minute when another field of an edit model is edited and saved**
- **SQLite's `Read{Column}Async` no longer returns another row's blob**
- 65 more fixes (mainly import notices, reading and writing types, and remote responses)

#### Bidirectional sync

- **A narrowed refresh no longer deletes rows outside its scope or fails every time** — it is refused with `SyncRefreshScopeException` before anything is deleted
- **Rows written to the local database while a sync runs are no longer lost to delete propagation or the download**
- **The upload no longer loses changes because of the entries of a failed write or of a row a save hook skipped**
- **A foreign key on the server or in the local database no longer stops every sync run** — this covers deleting and moving parents and children, rows whose parent is not yet in the local database, and self-referencing tables
- **A unique-value collision no longer stops every sync run** — this covers a row recreated with the same value, a value moved to another row on the server, and a second NULL in a SQL Server unique column
- **Syncing with `IncludeUnboundedBinary` no longer clears or empties the server's blobs or fails to bring them down**
- **Tables whose primary key is a `decimal`, a `TimeSpan` or a `varchar` (in a SQL Server versionless table) no longer fail every sync, fail to compile, or skip rows**
- **A delete made with a key-only stub or a stale object is no longer dropped before it reaches the server**
- 8 more fixes (mainly conflict reports, recording speed, and settings validation)

#### Code generation dialog & CLI

- **The app no longer crashes when the code generation dialog cannot save its settings, or when imported C# code has two primary-key properties that map to the same column name**
- **Editing in the query definition dialog no longer loses the PostgreSQL, MySQL or Oracle SQL added with the MCP `set_query` tool**
- 10 more fixes (mainly dialog validation and CLI reports)

#### Import and export

- **Importing an Excel definition document no longer drops columns, computed-column settings, or the order of unique constraints and primary keys** — the columns from a row with an empty column name onward, and the order in a merge import, were lost
- **DBML no longer breaks or loses names and types that contain spaces or symbols, or descriptions that contain `//`, on a round trip**
- **Mermaid no longer breaks its output or drops the rest of a name or type that contains spaces or symbols**
- **Importing Mermaid, DBML, an Excel definition document or C# code no longer gives foreign keys the same constraint name, which made the DDL fail**
- **Exporting onto the open diagram file no longer replaces the diagram with the exported content**
- 12 more fixes (mainly reading back, exporting large diagrams, and notices)

#### DB import & connections

- **A column skipped because its name differs from another only in letter case or surrounding spaces no longer passes its unique constraint or foreign key to the column that is kept**
- **Connection profiles and saved passwords no longer vanish silently when saving under the same name or after a momentary read failure**
- **A password or port is no longer mixed up so that it is sent to, or connects to, a different server**
- **A failure while writing connection profiles, or a hand-edited `connections.json`, no longer closes the app**
- 10 more fixes (mainly import warnings and connection dialog operations)

#### AI chat & MCP

- **MCP tool calls that overlap with each other or with another process no longer lose changes or fail**
- **`add_relationship` no longer gives a second foreign key between the same two tables the same constraint name, which made the DDL fail**
- **`add_relationship` now refuses an unknown `relationship_type` instead of making it one-to-many, and removing a relationship over MCP also clears the foreign key marker on the child column**
- **A saved API key that cannot be read is no longer deleted silently, for example when saving the connection settings**
- **With the API key method, the conversation no longer keeps failing after an interruption or an empty response, and tools no longer run twice**
- **Child processes such as Codex, Copilot and `claude` no longer remain after the app closes or after a failure**
- **Codex, Claude Code and Copilot responses no longer keep waiting or keep failing after a crash or a stall**
- **A failed drop, Ctrl+V or Claude Code "Recheck" no longer closes the app** — an image file larger than 32 MB is refused
- **Switching connection tabs while a response is in progress no longer leaves the window unable to send**
- 42 more fixes (mainly `set_query` checks, connection display, and attachments)

#### AI chat & mock generation

- **`mock.json` is no longer broken by a failed save, and no longer overwrites one created at the same moment**
- **A hand-edited `mock.json` no longer closes the app or makes later steps fail**
- **The mock generation window no longer freezes or closes because of an operation during a response or a failed connection check**
- **Switching mock generation conversations or closing the app no longer leaves AI back-end processes behind**
- **When the first mock generation message fails or is interrupted, the next message starts the conversation again**
- **Links in the single-HTML mock export and the design document no longer break because of screen names or the form of a link**
- **Mock project generation no longer fails with an output folder at the root of a drive (Copilot), a name with surrounding spaces, or regeneration after switching to another database**
- 21 more fixes (mainly the preview, the screen list display, and generation reports)

### Security

#### Distribution

- **The SQL Server and Oracle data access no longer brings in a vulnerable `System.Security.Cryptography.Pkcs`** — `Microsoft.Data.SqlClient` and `Oracle.ManagedDataAccess.Core` bring in versions 9.0.13 and 8.0.1, which have known denial-of-service vulnerabilities (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7). QuickER, its CLI, and the `QuickER.Runtime.SqlServer` package reference the fixed version 10.0.13 to override them. A mock project for SQL Server with the Blazor target references the same version. A WPF mock project does not need the reference, because its shared framework provides the package

#### AI chat & mock generation

- **The mock project's final build no longer runs build settings the AI wrote without asking, the preview blocks downloads and external resources in embedded frames, and files outside the mock folder are no longer read**

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

[Unreleased]: https://github.com/kokko-labs/QuickER/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.4.0
[0.3.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.3.0
[0.2.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.2.0
[0.1.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.1.0
