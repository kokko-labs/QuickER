# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

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

#### Bidirectional sync

- **The generated sync classes and interfaces changed shape** — the per-table sync classes (`{Entity}SyncTable` / `{Entity}DirectSyncSource` / `Http{Entity}SyncSource`) are no longer generated; construct the fixed classes instead, passing a descriptor from `GeneratedSyncTables` (no change needed if you use the DI registrations). `SyncTableBase.OnLastWriteWinsUploadedAsync` is gone, and the signatures of `ISyncTable.UploadAsync` and `PropagateDeletesAsync` changed. The local database gains an acknowledgement table, `quicker_sync_ack` (created by `SyncJournal.EnsureCreatedAsync`)
- **A sync download now stops at a row with an unsent local change** — so as not to overwrite it with the server's content. `SyncResult.Truncations` reports where it stopped; resolve the conflict, or drop the entry with `SyncJournal.RemoveTableAsync`, then run the sync again

### Added

#### Diagram editing & files

- **A composite primary key now keeps and can edit its own column order** — schema import for all five dialects reads the order `PRIMARY KEY (b, a)` declares, and DDL, sync scripts and EF Core's `HasKey` emit it in that order too. A table with two or more key columns gets a "Primary key order" card in the properties panel for reordering them, and the AI tools gain `set_primary_key`, which sets the primary key together with its column order. Once a diagram states the order explicitly, reordering columns no longer changes the key's order. The DBML / Mermaid / Excel documents and the C# code import do not carry the order, and opening and saving the diagram with 0.1.0 drops it

#### DB import & connections

- **PostgreSQL and MySQL connections can ask for a TLS level** — pick one in the connection dialog's "Encryption (SSL Mode)" field. The default, `Unspecified`, connects exactly as before. Both drivers default to encrypting without validating the server certificate, so choose `VerifyCa` or `VerifyFull` to have it checked. What each dialect does, Oracle included, is summarized in [docs/database.md](docs/database.md)

#### Code generation dialog

- **Regenerating asks before overwriting generated files that were edited by hand** — when a `.g.cs` about to be replaced has been edited since it was generated, the dialog lists those files and writes nothing unless you choose OK. Reformatting and line-ending conversion do not count as edits, and files generated before this version are not checked

#### Generated code

- **The generated base classes can now be extended with `partial` in every output mode** — `EntityBase`, `EditModelBase<TSelf>`, the value-object bases and the `IValueObject` marker, the repository contract and implementation bases, and `MapperBase<,>` are all emitted as source, so adding a member or an interface reaches the whole generated output. The same code compiles unchanged when the runtime is referenced as a package. Additions to a repository contract must carry a default implementation. See the "Extending the generated base classes" section of [docs/code-generation.md](docs/code-generation.md) for how to write one
- **Value objects implement `IFormattable`** — `price.ToString("N2")`, string interpolation, and a WPF binding's StringFormat now reach the underlying value
- **The value object validation rules are now public, and four general-purpose rules were added** — the required, max-length and precision checks the generated code uses can be called from `ValueObjectRules` and the like. For `OnValidate`, digit-count, range, ASCII-alphanumeric and email-address checks were added
- **Each generated file records the QuickER version and a content hash** — every `.g.cs` gains `// Generated by QuickER x.y.z` and `// Content hash: sha256:…` lines after `// <auto-generated />`, and the API reference gains the version line. The first regeneration adds these two lines to every file

### Changed

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

#### Distribution

- **The release-building workflow's actions are now pinned to commit SHAs** — so a moved upstream tag cannot change what the build and publish steps do. The distributed artifacts are unchanged
- **`QUICKER_UPDATE_FEED`, which redirects the update feed, now only works in a Debug build** — a shipped build always reads the official update feed alone

### Fixed

#### Diagram editing & files

- **The open file's external changes are no longer missed or overwritten without asking** — saving now compares the disk content beforehand and asks if it changed externally, and asks as well before writing back over a file saved in a newer format. Also fixed: watching stopped after a burst of changes, and an external write landing right after a save could be missed
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
- **Fixed a row whose deletion had been undone still being deleted on save after `Clear()` on an edit-model collection**
- **`ValidateUniqueAsync` no longer queries the database for a row marked for removal**
- **An invalid paging argument on a named query is now a 400 instead of a 500**
- **Fixed the server file's name drifting from the main file's when generating remote services with single-file output**

#### Bidirectional sync

- **Fixed sync losing server changes or local edits** — an uploaded row's new version could step over an unfetched change, so that change never came down again. Also fixed: the same run's download overwriting the local edit of a row it had just reported as a conflict, and an upload interrupted part way causing a false conflict or overwriting with a stale value
- **`SyncConflictPolicy.ServerWins` now restores a discarded row's content from the server** — a local edit to a row nobody else had touched used to survive being discarded

#### AI chat & mock generation

- **Fixed the Claude Code chat starting a new session on every turn with `claude` installed through npm** — a multi-line system prompt was cut short, taking the `--resume` / `--model` arguments after it with it
- **The AI tools now make a primary key column NOT NULL through every host** — only the external MCP server used to be able to write a nullable primary key column into the diagram
- **Generating a mock project into an output folder that already holds a project now asks before overwriting**
- **Mock project generation now handles interruption, timeout and app exit correctly** — an interruption under Claude Code is now reported as an interruption, and the final verification build is aborted after 10 minutes. Closing the app now interrupts the AI chat and mock generation child processes too
- **Fixed the mock project artifact check and log display** — the check now looks only inside the generated project folder and no longer fails on an unreadable subfolder. The log display no longer slows down during a long generation

#### CLI & MCP

- **A failed MCP tool call is now returned as an error (`isError`)**
- **`quicker reverse` now creates the output folder**

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

[Unreleased]: https://github.com/kokko-labs/QuickER/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.1.0
