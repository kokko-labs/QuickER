# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

### Breaking changes

#### Code generation dialog & CLI

- **A named query whose type token is a fixed-length string or binary without a length now fails generation** — `fixedstring`, `ansifixedstring` and `fixedbinary` mean nothing without one, and every dialect that needs a length writes them out as a type that does not exist. Give the token a length (for example `fixedstring(10)`), which the diagnostic message now says as well. Variable-length tokens (`string`, `ansistring`, `binary`) are unaffected: a parameter constrains no length, so they keep resolving as before
- **MySQL unsigned integer columns now generate unsigned C# types** — `unsigned` used to be stripped during type resolution, so an `int unsigned` column became `int` and reading a value above the signed range threw `OverflowException`. `tinyint` now becomes `byte`, `smallint` `ushort`, `mediumint` and `int` `uint`, and `bigint` `ulong`, matching what both MySqlConnector and the EF Core provider return (measured against MySQL 8.4). `mediumint` also used to fall through to the "unknown type" case and become `string`; it is now `int`. The property type on generated entities changes for such columns, so code that uses them may need updating. A signed `tinyint(1)` is still `bool`; an unsigned one is not, because MySQL drops the display width from unsigned `tinyint` and the driver returns `byte`

#### Generated code

- **PostgreSQL `time with time zone` columns now generate `DateTimeOffset`, and array columns generate an array of the element type** — `timetz` used to become `TimeSpan` and an array column (`integer[]`, `varchar(20)[]`, `bigint[]`) used to become `string`. Both were types the driver never produces, so such a column could be neither read nor written; because an entity is materialized from all of its columns at once, **one such column made the whole table unusable through EF Core**. The property type on generated entities changes for these columns, so code that uses them may need updating — but no working code can break, because none of it worked at run time.

  | Column type | Before | After |
  |---|---|---|
  | `time with time zone` / `timetz` | `TimeSpan` (read threw `InvalidCastException`) | `DateTimeOffset` |
  | `timestamp(n) with time zone` | `DateTime` (the time zone was dropped) | `DateTimeOffset` |
  | `integer[]` | `string` (read threw `InvalidCastException`) | `int[]` |
  | `bigint[]` | `string` | `long[]` |
  | `varchar(n)[]` | `string` (silently, with no diagnostic) | `string[]` |

  Two limits come with this. Writing a `timetz` value whose offset is not UTC needs the column type spelled out, which QuickER does not generate (EF Core is for connecting to an existing schema); add it in the generated `QuickErDbContext`'s `OnModelCreatingPartial` if you need it. And array columns are left out of edit models and value objects, and the length of an array's elements is not carried into the generated code; generation names the affected columns.

#### Import and export

- **A DBML line that cannot be interpreted is now reported instead of being dropped** — every unrecognized top-level line used to be skipped in silence, so a `Table` line that did not match the expected form took its whole table with it. Such a line now fails the import, naming the line and its number. Blocks that only carry information QuickER cannot represent (`Project`, `Enum`, `TableGroup`, `TablePartial`, `Note`, `records`) are still skipped, so files written by other tools keep importing; a relationship written in a form QuickER does not read (a named `Ref name: ...` or the block form `Ref { ... }`) is rejected by name rather than skipped, because skipping it loses the relationship

### Added

#### Code generation dialog & CLI

- **Generation now names the columns whose C# type fell back to `string`** — a DB type the type catalog cannot parse (`geometry`, `hierarchyid`, `sql_variant` and the like) becomes `string` so that generation keeps going, which the generated code gave no hint of. Such columns are now listed at generation time as "table.column (original type text)" on all five dialects. The generated code itself is unchanged
- **Generation now names the columns declared with a negative scale** — `numeric(10,-2)` (PostgreSQL) and `NUMBER(10,-2)` (Oracle) make the database round values (1234 is stored as 1200), and the generated code carries no precision or scale for such a column: no digit validation on the value object, no Precision/Scale on `[SqlColumnType]`, and no `HasPrecision` in the EF Core model. Neither fact was visible from the generated code. Such columns are now listed at generation time as "table.column (original type text)". This is a note rather than a warning, because a negative scale is written on purpose. The generated code itself is unchanged
- **Generation now warns when a NOT NULL computed column cannot be written on the mirror side** — in a multi-target build (SQL Server as the server, SQLite locally) or with bidirectional sync, the side that does not evaluate the expression gets an ordinary column, and generated code never writes it, so inserting a row there fails on the NOT NULL constraint. The affected columns are now named at generation time, with the two ways out: make the column nullable in the diagram (the mirror then keeps NULL for it) or leave the table out of synchronization. The note the DDL carries next to such a column says the same

### Changed

#### DB sync & DDL

- **A difference in nullability on a computed column no longer produces an `ALTER COLUMN`** — the nullability of a computed column follows from its expression, which the diagram does not model and therefore cannot impose on a database. Without this, taking the way out above (making the column nullable in the diagram) would have made the sync offer a meaningless `ALTER COLUMN` against the database the diagram was imported from. Differences in the column's type are still detected

#### Import and export

- **An Excel definition document written in a format this version cannot read is no longer imported** — the document embeds its format version, but the import never looked at it. A document carrying information a future version added is now reported instead of imported, so nothing it could not read is silently dropped (the same stance the diagram file takes towards a newer format). Older documents that carry no version are imported as before

#### Diagram files

- **A diagram file that leaves a required name or type empty (`null`) is no longer loaded** — a table name, a column name, a column type or a query name written as `null` used to be replaced with a default (an empty name, the type `int`), and saving over the file then made that replacement permanent. Such a file is now refused by name and location instead, in the GUI, the CLI and the MCP server alike, because filling the value in would change what the diagram means. A `null` in an optional string such as a description or a memo is still read as empty text. Only a hand-edited file can carry these, since nothing in QuickER writes one

### Fixed

#### Diagram editing

- **A long description no longer makes the diagram stall while it redraws** — laying out a wrapped description searched for each line break by measuring text up to the whole remaining paragraph, so the cost grew with the square of the description's length (measured at 200px wide: 65 ms for 10,000 characters, 2.3 s for 50,000). The search now brackets the line length first and measures only around it, which is 7 to 47 times faster (measured: 9 ms and 49 ms) and wraps at exactly the same places
- **Typing in a table's memo no longer takes one undo per keystroke** — the memo box committed on every character, so undoing five characters took five presses of Ctrl+Z and the undo history filled up with them. It now commits when you leave the box, like every other field in the property panel, so what you typed is one step. Collapsing the panel still commits what you typed
- **Auto-arranging a large diagram on a grid no longer freezes the window** — "Grid" searches for an arrangement with fewer crossing relationship lines, but the search was exhaustive and grew roughly with the cube of the table count (measured: 6.2 s at 200 tables, 88.7 s at 400; creating a diagram from a database, from the AI or from C# code waits for the arrangement to finish). Pairs that cannot possibly interact are now ruled out before the check, which made it 4 to 9 times faster, and the search has a cap on how many checks it may make: when the cap is reached it keeps the improvements found so far and stops (measured: under 1.5 s at every size). The cap counts checks rather than time, so the same diagram always comes out the same way. Diagrams that stay under the cap (measured up to about 200 tables and 200 relationships) are arranged exactly as before; above it more crossings remain (measured at 400 tables: 223 crossings became 868, total line length +40%, occupied area unchanged)

#### Settings

- **Settings written by a newer version are no longer lost when an older one saves** — settings files are saved by writing the whole object back, so any key this version does not know about disappeared on the next save. Unknown keys, including ones nested inside a section, are now carried over (the display language, AI settings and code-generation settings all go through the same store). Keys inside arrays are still not carried over. The connection profiles file (`connections.json`) does not go through this store and is unchanged
- **A momentary read failure no longer resets the settings to their defaults** — saving settings is a read-modify-write, so one failed read wrote the defaults back and took the display language, the update check and the code-generation settings (output path, namespaces, target DBs and the rest) with it. When the file cannot be read, QuickER now starts from the defaults but **does not save over it**, and resumes saving as soon as a read succeeds (the auto-save re-reads the file each time, so in practice the next save recovers on its own)
- **A corrupted settings or connections file is now moved aside before being overwritten** — to `{file name}.corrupt`, once only (a later corruption does not overwrite it, so what remains is the copy closest to the last good state). Previously such a file was simply overwritten with the defaults, leaving nothing to investigate afterwards

#### Database import & generated code

- **Computed and generated columns are now imported as read-only and left out of generated writes** — a SQL Server computed column, a MySQL / PostgreSQL / SQLite generated column or an Oracle virtual column used to be imported as an ordinary column, so every INSERT / UPDATE the generated repository issued against that table failed at runtime. The import now marks the column and warns that the expression is not carried into the diagram, generated code excludes the column from INSERT / UPDATE on every backend (EF Core mode maps it as store-generated), generated DDL notes next to the column that the expression must be added by hand, and generation reports the excluded columns. A SQLite generated column used to be missing from the import entirely, and a diagram whose primary key is a computed column is now refused at generation time
- **SQL Server tables whose names differ only in letter case no longer merge into one entity** — in a database with a case-sensitive collation, `Dup` and `dup` collapsed into a single entity with both tables' columns mixed together and no warning. One table is now kept and the other is skipped with a warning, the same way the other dialects already behaved
- **SQLite virtual tables (FTS5, R*Tree and the like) are no longer imported as broken ordinary tables** — a virtual table and its shadow tables used to appear as entities full of BLOB columns. They are now excluded from the import, and each virtual table is reported once
- **Disabled constraints are no longer imported as if they were enforced** — a SQL Server foreign key turned off with `NOCHECK CONSTRAINT` used to become a relationship in the diagram even though it enforces nothing. It is now skipped and reported. A key added `WITH NOCHECK` but currently enabled is still imported, because it is enforced from then on. Oracle already skipped its `DISABLE`d constraints, but did so silently; those are now reported the same way
- **SQL Server temporal tables can now be imported and written to** — the history table is excluded and reported instead of arriving as a duplicate entity, and the current table's period columns (`GENERATED ALWAYS AS ROW START / END`) are treated as columns the database produces, so generated code leaves them out of INSERT / UPDATE. Writing to such a table used to be impossible: the period columns were sent like ordinary columns and SQL Server rejected the statement because they are `GENERATED ALWAYS`. Period columns declared `HIDDEN` work the same way, because generated code lists its columns explicitly rather than relying on `SELECT *`
- **A generated mapper no longer fails to compile when a computed column is a NOT NULL value type** — with `datetime2`, `int`, `decimal` and the like, generating edit models produced code that did not compile (CS0266: `System.DateTime?` cannot be converted to `System.DateTime`). An edit model's confirmed value is always `Nullable<T>` for a value type, and the three kinds of column that are only assigned when an input is present (row version, computed, excluded unbounded binary) were missing that unwrap. Row version and excluded binary columns are always `byte[]`, so only a computed column — which can have any type — could reach it. Diagrams without a computed column, and those whose computed columns are reference types, generate byte-identical output
- **A SQLite database whose table name contains a dot can be imported again** — the import quoted such a name as if the dot were a schema qualifier, which made the `PRAGMA` calls a syntax error and failed the whole import
- **A SQLite foreign key whose parent table does not exist is now reported** — SQLite does not check the parent table when a table is created, and such a key used to vanish from the diagram without a word
- **A partitioned MySQL table is now reported the way a PostgreSQL one already was** — the diagram does not model partitioning, so DDL generated from it creates the table unpartitioned; the import now says so instead of staying silent
- **A SQLite primary key that allows NULL is now reported** — in a rowid table, any primary key other than a single column declared exactly `INTEGER` can hold NULL. The import still corrects such a column to NOT NULL (the diagram and the editor always treat a primary key that way), but it now names the columns it adjusted, because existing rows with a NULL key can make a later sync fail while copying the data across. Only the columns that were actually adjusted are named: a key whose columns are already declared `NOT NULL` is left alone
- **A string or binary column with no length is no longer written out for SQL Server, MySQL or Oracle** — switching a diagram to one of those dialects used to produce DDL that MySQL and Oracle refuse to run, while SQL Server quietly created a one-character column. Such a column is now listed as unconvertible, so a length can be given before switching. C# reverse engineering reports the same case per column instead of expanding the token, and generated entities now record the original spelling in `[DbColumnMeta(..., NativeType = "...")]`, so code generated by QuickER still round-trips
- **MySQL `bit(n)` (n greater than 1) and `year` columns now map to `ulong` and `int`** — both used to fall through to the "unknown type" case and become `string`, so reading such a column from generated code threw. `bit(1)` is still `bool`, the counterpart of the `tinyint(1)` convention

#### Code generation dialog & CLI

- **A query that references a deleted column is now reported in the query definition dialog** — references from the sort order, a projection field or a parameter to a column that no longer exists could be confirmed with OK and were only reported at generation time (which skips that query with a warning). The dialog now names the query and the item and keeps OK disabled
- **The "exclude unbounded binary columns" setting is no longer saved while it is hidden** — switching DB access back to "none" or to EF Core hides the row, but a checkbox ticked earlier was still written back to the settings file. Besides the columns QuickER repositories read and write, the setting also drives **whether an EditModel treats such a column as required**, so a hidden tick changed the generated code even in configurations that generate no QuickER repository: a NOT NULL unbounded binary column was left out of the edit model's required inputs (required only on a new row) and its mapper assigned it only when a value was supplied. In those configurations such columns are now required like any other column. While the row is hidden the value is only dropped from the file, not from the screen, so choosing QuickER repositories again brings the original tick back. The sync-support checkbox follows the same rule
- **`--repository-dialects` with a value that names no dialect is now an error** — commas or spaces only produced an empty list, which skipped the "derive a single dialect from the provider" fallback and quietly emitted SQL Server code with exit code 0 even for `--provider sqlite`. A shell variable that failed to expand in CI hits this

#### Import and export

- **Printing a very large diagram at actual size no longer produces an unusable page** — at actual size the paper is the diagram itself, so a diagram with tables placed far apart asked for a page over 1,000 inches on a side, which no PDF or XPS output can produce (their limit is 200 inches). Such a diagram is now reported with its actual size before the print dialog opens, offering to print it scaled to fit one page instead; cancelling prints nothing. Diagrams within the limit are unaffected (an auto-arranged diagram of 1,000 tables measures 100 x 61 inches)
- **Exporting a very large diagram as PNG no longer fails** — the rasterizer refuses anything past its own size limit, so a diagram big enough simply could not be exported as an image (the error said nothing about size). PNG is now capped at 100 million pixels in total: a diagram above that is scaled down to fit, keeping its aspect ratio, and the completion dialog reports the full size and the size actually saved. SVG has no such cap, so export SVG when you need the full resolution
- **DBML now carries names that contain spaces or symbols** — export writes such a table or column name as a DBML quoted identifier (`"Order Details"`). Without the quotes a table name containing a space did not match the `Table` line's form and the table vanished from the import without a word, while a column name broke at the first space and the rest ran into the type (`Order Date date` became the column `Order` of type `Date date`). Names made only of letters, digits and underscores — Japanese included — are written exactly as before
- **DBML no longer mangles a type that contains a space or brackets** — `double precision` and `integer[]` are now quoted on export, as DBML's own rules require, and the import no longer mistakes the brackets of an unquoted array type for the start of the settings block (which turned `integer[]` into `integer`)
- **A `//` inside a DBML string is no longer treated as a comment** — a description holding a URL used to be cut at `//` and lost. DBML block comments (`/* ... */`), including ones spanning several lines, are now recognized as well, and keywords are accepted in any letter case (a lowercase `ref:` line used to fail to parse)
- **A DBML `Ref:` line written before the table it points at can now be imported** — DBML produced by other tools often groups the Ref lines at the top of the file, and in that order the whole import failed with "referenced table is not defined". Ref lines are now resolved once every table has been read; a line that references a table defined nowhere is still an import error reported with its line number
- **An Excel definition document with more than one foreign key between the same two tables can be read back** — a diagram with, say, a shipping address and a billing address both pointing at the same table exported fine but always failed to import, because duplicates were judged by the pair of table names alone. The check now includes the columns that make up the key; two rows that are identical down to their columns are still rejected
- **Mermaid no longer drops part of a column name that contains a space** — a Mermaid attribute line separates type, name and marker by spaces, so `Order Date` used to lose everything after the space. Such names are now folded to `_` (the same treatment the type already gets) and reported among the information the format cannot carry. If folding would collide with another column in the same table, the name that needs no folding keeps its spelling and the folded one gets a number

#### DB import & connections

- **Saving a connection profile now asks before overwriting a different profile** — the save button picks its target by the profile name, so typing a name that an existing profile already had replaced that profile's server, user and saved password without a word, and with no way back. Overwriting a profile other than the one currently selected now asks first; saving over the profile you loaded is unchanged, and so is saving under a new name (which still creates another profile)
- **A momentary read failure no longer wipes every saved connection profile** — saving, renaming, and deleting re-read `connections.json` before writing it back, so another program holding the file for an instant turned into "read nothing, write nothing back" and every saved profile vanished without a word (only the orphaned secret files remained). Reads now tell "the file is missing", "the file is corrupted" and "the file cannot be read right now" apart: a transient failure is retried as persistently as the write side does, and if it still cannot be read the operation is **cancelled without writing anything** and reported in the status line
- **OK is disabled while a connection test is running** — the test cannot be cancelled, so confirming and closing left it holding the connection on its own, hitting the same database at the same time as the import or sync that started right after
- **The DB Sync dialog no longer restores a last-used connection from a different dialect** — sync fixes the target DB to the diagram's dialect, yet it still copied the host, file path and service name of the last connection into the fields. Those fields mean different things per dialect, so values that could not connect were left in place under a "restored" message
- **Saving with "Save password" ticked but the box empty now clears the tick as well** — no encrypted file is written for an empty password, so only the tick was stored and the dialog showed a password as saved when there was nothing to restore. Overwriting a saved password with an empty box now drops both the encrypted file and the tick
- **A connection profile can now be renamed** — the save button saves the current input under the name in the box, so typing a new name made a copy and left the old profile (and its saved password) behind. A "Rename" button next to it changes only the name of the selected profile, keeping its connection settings and its saved password; renaming onto a name another profile already uses asks first, the same way saving does

#### AI chat & mock generation

- **Interrupting an AI chat response during a tool call no longer breaks the conversation** — with the API key method, an interrupted tool call left the history inconsistent and every later message failed until "New conversation". The calls that did not run are now recorded in the history as interrupted, so the AI also knows which of its changes reached the diagram
- **The connection tabs, the API key connection settings and "New conversation" are disabled while a response is in progress** — switching tabs mid-response left the window unable to send anything, while the old response kept editing the diagram, and changing the provider, model, API key or endpoint took effect on the running response. Interrupt the response first; the "Interrupt" button stays available
- **A Codex crash or an unanswered interrupt no longer leaves the chat stuck as processing** — when the Codex App Server exits or its connection is lost, the turn now fails with a message, and when an interrupt request goes unanswered or fails, the server is stopped by force. Both lose the conversation context, which the message states, and the next message starts a new conversation. Mock project generation on Codex now also fails right away instead of waiting out its 30-minute limit
- **A hand-edited `mock.json` with an invalid screen file name no longer ends the application** — opening such a mock folder is now refused as corrupted, and a failure while producing the single-HTML bundle or the design document is reported in the status bar instead
- **A runaway AI chat response now stops at 50 tool round trips** — with the API key method, a response that kept calling tools could only be stopped by hand; a response that reaches the limit now ends as a failed turn with a message
- **A tool that throws no longer leaves a Codex, Claude Code or Copilot response waiting forever** — the error now goes back to the AI as a failed tool result, and a tool response that cannot be delivered is reported in the status bar
- **The mock design document now survives awkward names** — a screen name containing a line break no longer breaks its heading, entity names differing only in case now share one CRUD column, and a screen file name containing parentheses no longer breaks its link
- **Switching the API provider mid-conversation now announces attachments that would be dropped** — when the history holds an attachment kind the newly selected provider cannot resend (such as a PDF on OpenAI), the chat says so instead of dropping it silently
- **Closing the application now shuts down the AI back ends** — the Codex App Server, the Copilot runtime and a running Claude Code process are disposed on exit, with a three-second cap so a hung shutdown cannot stall the application
- **A failure during shutdown no longer leaves AI child processes running** — closing the application saves the chat and mock-generation settings before disposing the back ends, so one failed save (a full disk, a permission error, a file sitting where the settings folder should be) stopped the shutdown right there and left the Codex App Server, the Copilot runtime and `claude` / `dotnet` child processes behind, with no trace of why. Each step of the shutdown — interrupt, save settings, dispose the back ends, close the window — now runs independently: a failure is written to `%LOCALAPPDATA%\QuickER\shutdown-*.log` (separate from `crash-*.log`, since the application did not crash) naming the step, and the remaining steps still run. The same holds across feature modules: one module failing no longer keeps the others from cleaning up. The application now also exits once the main window closes, rather than once the last window closes: those AI windows hide instead of closing, so a window left behind used to keep the process alive with nothing visible on screen
- **Interrupting Codex just as a turn starts is now reported as an interruption, not an error**
- **Switching or resetting a mock-generation conversation now disposes the previous AI back end** — starting a new conversation, changing the mock folder or switching the connection method used to leave the old session's engine, including a CLI back end's resident child process, alive until the application closed
- **A tool that throws no longer fails the whole turn with the API key method** — the error is now returned to the AI as a failed tool result and the remaining tools still run, the way the Claude Code, Codex and Copilot back ends already behaved. Interrupting still ends the turn as before
- **Reconnecting to Codex now cleans up what the previous connection left behind** — after a dropped connection or a failed start, the exited (or not fully stopped) process, its reader loop and its standard input are still around. They used to be dropped on the floor as a new process was started on top of them
- **Closing the application right after switching a mock-generation conversation now waits for the previous session's engine** — the disposal of a session that has just been detached runs on the thread pool, and shutdown could not see it, so that back end's resident child process was orphaned

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
