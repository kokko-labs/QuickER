# MCP server (quicker mcp)

*English | [日本語](mcp.ja.md)*

`quicker mcp` starts a [Model Context Protocol](https://modelcontextprotocol.io) server over the stdio transport (stdin/stdout, JSON-RPC).
It exposes tools for editing ER diagrams and generating code, so an external AI agent (Claude Code, Codex, and so on) can build and evolve a QuickER diagram as part of its own workflow.
The agent launches `quicker mcp` as a child process and talks to it over stdin/stdout.

The server is **stateless**: it takes no options and keeps no in-memory diagram.
Nearly every tool takes the target diagram file as its `file` argument, and what a call does with that file depends on the kind of tool.

- **Editing tools** complete a whole "load → modify → save" cycle in the single call (`create_diagram` is the exception: it writes a new file without reading an existing one).
- **Read-only tools** (`get_diagram_summary` / `list_queries`) load the file without saving it.
- **Generation tools** (`generate_csharp` / `generate_ddl`) leave the diagram untouched and write their output to a separate destination.

Only `get_generation_config_schema` takes no `file` at all.
Concurrent agents (or a single agent working on several diagrams) simply pass different `file` paths.

Each call reads the file, changes what it was asked to change, and writes the whole document back.
Within one server, calls that arrive at the same time are handled one at a time, so tool calls an agent sends in parallel never lose each other's changes.
Statelessness has a price between processes, though: **two processes writing the same file are last-write-wins.**
There is no lock or version check across processes, so a call that loaded the file before another process saved it overwrites that save entirely: the other side's changes are gone, and nothing reports it.
Only `create_diagram` refuses to overwrite, and only because the file must not already exist.
This applies to two servers on the same file and to the server and the GUI, which reloads a file changed underneath it, silently when it has no unsaved changes of its own.
A diagram open in the GUI and edited over MCP at the same time can therefore lose whichever side saved first.
Have one writing process at a time on a given diagram.

## Setup

### Claude Code

Add the server to your project's `.mcp.json`:

```json
{
  "mcpServers": {
    "quicker": {
      "command": "quicker",
      "args": ["mcp"]
    }
  }
}
```

Or register it from the command line:

```powershell
claude mcp add quicker -- quicker mcp
```

### Other stdio clients

Any MCP client that supports the stdio transport can use the server.
Configure it to launch the command `quicker` with the single argument `mcp` (Codex, for example, takes the same command/args pair in its own MCP-server configuration).
This requires the `quicker` command to be on `PATH`, so see [installing the CLI](cli.md).
To run from source instead, build the CLI once (`dotnet build QuickER.slnx`) and point the command at the built assembly (`command: "dotnet"`, `args: ["<repo>/src/QuickER.Cli/bin/Debug/net10.0/QuickER.Cli.dll", "mcp"]`).
Do not use `dotnet run` here: its build output goes to stdout, which is the JSON-RPC protocol channel.

## Tools

The server exposes 19 tools: 13 for ER diagram editing, 3 for named queries, and 3 for code generation.
**Every tool requires a `file` argument** (the path to the diagram JSON, that is the GUI save format `DiagramDocument`).
The one exception is `get_generation_config_schema`, the only information-only tool, which takes no arguments at all.
The tables below list the other arguments, with required ones marked ✅.

### ER diagram editing

| Tool | Arguments | Description |
|---|---|---|
| `create_diagram` | `target_dbms` ✅ (`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`) | Create a new, empty diagram file for the given target DBMS. Fails if the file already exists (this tool only creates new diagrams), and also if the parent directory does not exist (directories are never created) |
| `get_diagram_summary` | — | Return a text listing of the tables, columns, unique constraints, and relationships in the diagram. The first line names the target DBMS (`Target DBMS: sqlserver`). Each relationship line shows its column pairs and constraint name (`Customer → Order (OneToMany, FK: (CustomerId → CustomerId)) [FK_Order_Customer]`), so a composite foreign key is listed with every pair in declaration order. When a composite primary key's own column order differs from the table's column order, the entity block adds a `Primary key order: …` line (omitted when the two agree) |
| `add_entity` | `table_name` ✅, `description` | Add a new table (no columns are created). An empty table name, or one that matches an existing table's name ignoring case, is an error |
| `remove_entity` | `table_name` ✅ | Remove a table, along with its relationships and the named queries defined on it. The result names the removed queries. No tool brings them back, and adding a table with the same name again does not restore them (in the GUI, Undo restores the table and its queries together) |
| `add_column` | `table_name` ✅, `column_name` ✅, `data_type` ✅, `is_primary_key`, `is_nullable`, `description` | Add a column to a table. Give each table exactly one primary key column: the tool does not reject a second one, but the code generator does not support composite primary keys. A column added with `is_primary_key=true` is always NOT NULL, whatever `is_nullable` says. To declare a composite key, or to change the key of an existing table, use `set_primary_key`. An empty `data_type`, or a column name that matches another column of the same table ignoring case, is an error |
| `remove_column` | `table_name` ✅, `column_name` ✅ | Remove a column from a table. A UNIQUE constraint that includes the column is removed together with it (a constraint is never silently narrowed to its remaining columns) |
| `set_entity_property` | `table_name` ✅, `new_table_name`, `memo`, `description` | Change a table's name, memo, or description (specify at least one). An empty `new_table_name`, or one that matches another table's name ignoring case, is an error; changing only the letter case of the table's own name is allowed. A call that fails changes none of the given properties |
| `set_column_property` | `table_name` ✅, `column_name` ✅, `description`, `data_type`, `is_nullable` | Change a column's description, data type, or nullability (specify at least one). A primary key column stays NOT NULL whatever `is_nullable` says (the same normalization as `add_column` and `set_primary_key`); take the column out of the key first if it really has to be nullable. When `is_nullable` = `true` is ignored this way, the call still succeeds and its result says so. An empty `data_type` is an error, and a call that fails changes none of the given properties |
| `add_relationship` | `source_table` ✅, `target_table` ✅, `relationship_type` ✅ (`OneToOne` / `OneToMany` / `ManyToMany`), `source_columns`, `target_columns`, `constraint_name` | Add a foreign key between two tables. `source_columns` and `target_columns` are parallel arrays of column names: entry *i* of `source_columns` (the parent's referenced column) is referenced by entry *i* of `target_columns` (the child's foreign key column), the two must have the same length, and their order is the declaration order of the foreign key. One entry each is the usual single-column foreign key; two or more entries define a composite one. Giving only one of the two arrays, a length mismatch, an unknown column name, or the same column twice is an error. When the parent columns you name are neither the parent's primary key (all of its columns, in any order) nor the columns of one of its unique constraints, the relationship is still added, and the result adds a note: most databases reject such a `FOREIGN KEY` when the DDL is applied (MySQL accepts it on indexed columns). **When both arrays are omitted**, every primary key column of the parent is paired with a child column inferred from the column names (same defaulting as the GUI's relationship-creation flow, including a child primary key column named after the parent, as in an identifying relationship); a parent column whose counterpart cannot be inferred is simply left out, and the relationship can then be completed in the GUI. The call still succeeds then, and its result adds a note saying how many of the parent's key columns were paired (or that the parent has no primary key): the generated DDL has no `FOREIGN KEY` for a relationship without column pairs, and an incomplete one when only some key columns are paired. A many-to-many relationship never carries column pairs (it stands for a design that goes through a junction table). A second relationship between the same parent and child over the same column pairs (in any order) is an error, because it would be the same foreign key again. `relationship_type` is matched ignoring case, and any other value is an error. The relationship gets a constraint name that no other constraint in the diagram uses: `FK_{child}_{parent}` by default, with the child's foreign key column names appended (and then a number) when that name is taken, so a second foreign key between the same two tables, such as a billing and a shipping customer, gets a distinct name. To choose the name yourself, give `constraint_name`; a name already used by a foreign key, primary key or unique constraint, ignoring case, is an error. The result reports the name, which `remove_relationship` accepts |
| `remove_relationship` | `source_table` ✅, `target_table` ✅, `constraint_name` | Remove the relationship between two tables. Only one relationship is removed per call. When several relationships run in the same direction between the same pair of tables, pass `constraint_name` to pick one; without it the call fails and lists the candidate constraint names instead of silently removing the first match (`get_diagram_summary` shows the names too). A child column that no longer belongs to any relationship loses its foreign key marker, as in the GUI; the same happens when `remove_entity` or `remove_column` takes a relationship away. A marker on a column that belongs to no relationship, such as one set by a DB import, is kept |
| `set_primary_key` | `table_name` ✅, `columns` ✅ (an array of column names, in key order) | Set the primary key of a table. Matched by `table_name` alone, because a table has at most one primary key, so the call always replaces the current one: the listed columns become the key in the order given, and every other column of the table stops being part of it. Column order matters (unlike `set_unique_constraint`): it is the order of the generated `PRIMARY KEY` constraint, and `get_diagram_summary` reports it whenever it differs from the table's column order. The listed columns are made NOT NULL; a column dropped from the key keeps the nullability it had. Nothing is checked against relationships: dropping a column that a relationship references leaves that foreign key pointing at a column no key or unique constraint covers, and the generated DDL then fails when applied, so keep such a column in the key or cover it with `set_unique_constraint`. At least one column is required, so the tool cannot drop a table's primary key altogether |
| `set_unique_constraint` | `table_name` ✅, `columns` ✅ (an array of column names, in declaration order), `name` | Define or replace (upsert) a UNIQUE constraint on a table. A `name` already used by a foreign key, primary key or unique constraint of the diagram, ignoring case, is an error (a constraint keeps its own name when you redefine it). Matched by (`table_name`, set of columns): if a constraint over the same columns exists it is redefined (its id is preserved; the name and column order come from this call), otherwise a new one is added. Column order and letter case affect neither the matching nor the meaning of the constraint. Omit `name` and the DDL generator synthesizes `UQ_{table}_{columns}`. The primary key already enforces uniqueness of its own columns, so a constraint over exactly those columns is normally unnecessary |
| `remove_unique_constraint` | `table_name` ✅, `columns` ✅ | Remove a UNIQUE constraint, identified by its set of columns (order and letter case do not matter). Fails if the table has no constraint over exactly those columns |

### Named queries

Named queries are stored on the diagram and become Repository methods when C# code is generated (see [using the generated code](code-generation.md)). Entities and columns are referenced by name and resolved when the tool runs.

| Tool | Arguments | Description |
|---|---|---|
| `set_query` | `table_name` ✅, `query_name` ✅, `returns` ✅ (`list` / `single` / `count` / `scalar` / `projection`), `description`, `scalar_type`, `implementation` (`dsl` / `sql` / `manual`, default `dsl`), `condition`, `sql`, `parameters`, `order_by`, `paging`, `result_type_name`, `fields` | Define or replace (upsert) a query on a table. Matched by (`table_name`, `query_name`): if a query already exists it is replaced wholesale (its id is preserved); otherwise a new one is added. The definition is validated before saving; on any error the file is left unchanged |
| `list_queries` | — | List the queries in the diagram, grouped by table, with each query's return shape, implementation, condition/SQL summary, and parameters |
| `remove_query` | `table_name` ✅, `query_name` ✅ | Remove a single query. Fails if no such query exists |

`set_query`'s nested arguments:

- `scalar_type`: required when `returns` = `scalar`; a dialect-neutral type token, for example `decimal(12,2)`.
  Give string and binary types a length (for example `string(50)`).
  A fixed-length token (`fixedstring`, `ansifixedstring`, `fixedbinary`) without one makes code generation fail.
  A variable-length token (`string`, `ansistring`, `binary`) without one is treated as unbounded.
- `condition`: a mini-DSL search condition (comparisons, `AND`/`OR`/`NOT`, parentheses, `IS [NOT] NULL`, `[NOT] LIKE`, `[NOT] IN`, `CONTAINS`/`STARTSWITH`/`ENDSWITH`), used when `implementation` = `dsl` (omit for no filter).
  Column names refer to the table's columns; `@name` refers to a declared parameter.
- `sql`: an object mapping a dialect name (`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`) to a raw SQL string, used when `implementation` = `sql`.
- `parameters`: an array of `{ name` ✅ `, type, source_column, is_list }`.
  Give exactly one of `type` (a dialect-neutral token) or `source_column` (a column of this table, whose generated type is used).
- `order_by`: an array of `{ column` ✅ `, descending }` (valid only when `returns` is `list`, `single`, or `projection`; with `single` it selects the first row).
- `paging`: a boolean; when true, `take`/`skip` parameters are added (applies to `list` and `projection`).
- `result_type_name` / `fields`: required when `returns` = `projection`.
  `fields` is an array of `{ name` ✅ `, type, source_column, is_nullable }` (again, exactly one of `type` or `source_column`).

Validation is strict about anything that would fail at runtime and lenient about hygiene warnings.
A mini-DSL syntax error, an unknown column or undeclared `@parameter`, an undeclared parameter in raw SQL, or a structural mismatch (missing `scalar_type`/`fields`, both or neither of a parameter's `type`/`source_column`, misused `order_by`, an unknown SQL dialect, or a `sql` value that is not a string) refuses the save.
An unused parameter or a multi-statement SQL is reported as a warning and the save proceeds.
An input that the chosen `implementation` does not use is reported as a warning: `sql` with `dsl` or `manual` (the SQL is not saved, as in the GUI's query definition dialog), and `condition` with `sql` or `manual` (the condition is not saved).
With `implementation` = `sql` and no SQL for any dialect, a warning says that the generated method is left to be written by hand in every dialect.
A `sql` value that is empty or only spaces counts as no SQL for that dialect and is not saved.

A tool that takes a name (`add_entity`, `add_column`, `set_entity_property`, `set_column_property`, `add_relationship` with `constraint_name`, `set_unique_constraint`) saves a name that the diagram's target database would refuse (longer than its identifier limit, or holding a character it cannot store) and adds a `Note:` to the response naming the place and the reason.
The note appears once, in the call that introduced the name.
DDL generation and sync refuse such a name until it is changed (see [Identifier length limits](database.md#identifier-length-limits)).
A definition that code generation always rejects is refused with the generator's own message, by the same checks the generator runs: a query name that does not make a method name, or whose method name (the name in PascalCase with `Async`) is a standard repository member such as `GetByIdAsync` or matches another query on the same table; a parameter, field or result type name that is not a C# identifier, a parameter named `Query`, `QueryBySqlAsync` or `await`, a parameter named `cancellationToken`, or `take`/`skip` with paging, or the same parameter or field name twice; a result type name already used by another query, or one that collides with a type of the generated code (a generated class or interface such as `OrderEntity` or `IOrderRepository`, a runtime type, or a library type the generated code uses by its simple name, such as `Task`); `scalar` with `dsl`; `paging` outside `list` and `projection`; and, with `dsl`, a projection field without a `source_column`.
The GUI's query definition dialog runs the same checks before it lets you confirm.

Type-token contents are not checked here; they are validated at generation time.
The same goes for the type consistency of a DSL condition against the columns (`name > 'M'`, a `CONTAINS` on a numeric column, and the like).
Resolving what C# type a column becomes takes the dialect's type mapper, which this file-based server deliberately does not carry.
Such a condition therefore saves here and is caught at generation time, where the query is skipped with a warning that names it (the other queries and the rest of the generation are unaffected).

### Code generation

| Tool | Arguments | Description |
|---|---|---|
| `generate_csharp` | `out_dir` ✅, `config`, `provider`, `force` | Generate C# code (Entity / EditModel / Mapper / Repository, etc.) into an output directory, using the same pipeline as `quicker generate`. `config` is the **path** to an existing generation settings JSON file, not its contents (same semantics as `quicker generate --config`; a path that does not exist fails; see [CLI reference](cli.md#settings-file-quickerjson)). Call `get_generation_config_schema` for the full list of `config` keys. If a `.g.cs` about to be replaced has been edited by hand since it was generated, the call writes nothing and fails with the list of edited files; pass `force: true` to overwrite them (see [The generated file header](code-generation.md#the-generated-file-header)) |
| `generate_ddl` | `out_file` ✅, `provider` | Generate a DDL (CREATE TABLE / foreign key) SQL script and write it to a `.sql` file |
| `get_generation_config_schema` | *(none)* | Return a machine-readable JSON catalog of every key valid in the settings JSON (`quicker.json`) that `generate_csharp`'s `config` accepts: each key's name, type, default, category, allowed values, and description, plus cross-key rules and an example. Lets an agent write a config without external docs. This is the only tool that takes no `file` argument |

For the two file-based generation tools (`generate_csharp` / `generate_ddl`), `provider` is optional: when omitted it defaults to the diagram's target DBMS (or `sqlserver` if the diagram has none). Its accepted values are the same five dialects as `create_diagram`'s `target_dbms`. A `provider` that differs from the diagram's target DBMS is accepted, but the result begins with a warning: column types are not converted and are read as written under that dialect's rules (to change the dialect, switch the target DBMS in the GUI, which converts the types first). The results of both tools are in English whatever the OS language, like every other tool on this server. `generate_ddl` enforces its destination: `out_file` must end in `.sql` and its parent directory must already exist (the tool does not create directories), and the script is written atomically. `generate_csharp` is deliberately not symmetric here: its `out_dir` is still created if it does not exist, and its files are not written atomically.

## Typical flow

A diagram is built up one file-level call at a time. For example, to design a customer / order schema for SQLite and generate its DDL and C# code:

1. `create_diagram`: `file` = `shop.json`, `target_dbms` = `sqlite`
2. `add_entity`: `table_name` = `Customer`; then `add_column` for `CustomerId` (`data_type` = `integer`, `is_primary_key` = true) and the remaining columns
3. `add_entity`: `table_name` = `Order`; then `add_column` for `OrderId` (PK), `CustomerId`, and so on
4. `add_relationship`: `source_table` = `Customer`, `target_table` = `Order`, `relationship_type` = `OneToMany`, `source_columns` = `["CustomerId"]`, `target_columns` = `["CustomerId"]`
5. `generate_ddl`: `out_file` = `shop.sql`, and/or `generate_csharp` with `out_dir` = `./Generated`

Call `get_diagram_summary` at any point to read back the current tables and relationships.
Before writing a `config` for `generate_csharp`, call `get_generation_config_schema` to discover the available keys and their defaults.

## Notes

- **The GUI follows external changes.**
  When the server writes a diagram that is open in the GUI, the GUI detects the change and follows it.
  If the GUI has no unsaved edits and the new content is readable, it reloads the file automatically (zoom and scroll position are preserved) and shows a brief status note.
  Content it cannot load (malformed JSON, something that is not a `DiagramDocument`, or a newer format version) is not loaded: the GUI keeps what it has and reports the problem.
  The confirmation dialog appears only when the GUI itself has unsaved changes and something else writes the file; in that case the GUI asks whether to reload (discarding your unsaved edits) or keep editing.
  Further writes while that dialog is open do not stack another confirmation: after it closes, the GUI checks the file once more.
  If the folder holding the diagram becomes briefly unreachable (a dropped network share or USB drive), the GUI keeps retrying the watch and, once the folder is back, picks up any change made in the meantime (the status bar says once that watching stopped and once that it resumed).
  A mouse gesture in progress (moving or resizing a table, or a rubber-band selection) is cancelled back to where it started on either path.
  Dialogs that are already open (database sync, query definitions, and so on) are **not** closed, however: they still show the diagram as it was when they opened, so confirming one after a reload can act on stale content.
  Keeping diagram files under git is still recommended so changes stay reviewable.
- **DiagramDocument validation.**
  The editing tools refuse a file that does not exist, JSON that is not a `DiagramDocument` (an object with `Version` and `Schema`), and documents saved in a newer format version than this tool supports (to avoid discarding unknown data).
  It also refuses two kinds of damage that only a hand-edited diagram can carry: the same identifier (`Id`) used by more than one table or column, and an explicit `null` where a required string belongs (a table name, a column name, a column type, a query name).
  Both are refused by name and location; neither is repaired, because guessing a value would change what the diagram means.
  A `null` in an optional string such as a description or a memo is read as empty text.
  `get_diagram_summary` still reads a newer-format document, with a warning.
- **Design defaults via server instructions.**
  On initialization the server sends MCP instructions carrying default design guidelines: PascalCase singular table names unless the user asks for a different convention (matching the existing diagram's style when there is one), exactly one primary-key column per table (the diagram and the generated DDL do support a composite key, and only the C# code generator does not; `set_primary_key` is how to declare one and its column order when the user asks for it), the foreign-key workflow, and the note that unique constraints are for columns other than the primary key.
  Clients that support instructions, such as Claude Code, surface these to the agent automatically.
  They are guidance, not enforcement: the tools still accept diagrams that follow other conventions.
- **Layout is not written by the server.**
  A newly created file contains schema only (no coordinates), so opening it in the GUI auto-arranges all tables.
  Tables added to an existing file are placed in free space the next time it is opened in the GUI.
  Columns added to an existing table simply appear inside that table's card, and the table itself is not moved.

## Related

- [CLI reference (generate / scaffold / reverse, quicker.json)](cli.md): the `quicker generate` pipeline that the code generation tools reuse
- [Configuring AI chat](ai-chat.md): the in-app AI chat, which edits the currently open diagram inside the GUI (in contrast to this external MCP server, which agents drive over stdio)

## License note

The external MCP server ships as part of the CLI (`QuickER.Cli`), and its file-based tool-execution host (`QuickER.Mcp.Tools`) is likewise covered by [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) **plus additional grants**; thanks to those grants, **the current releases are free for everyone, including commercial use**.
The `QuickER.Mcp` project, which contains the tool-definition catalog and the stdio host, is MIT-licensed.
Eight projects in total are NC-covered; for the full mapping and the licensing and distribution policy, see the [licensing guide](../LICENSING.md).

**Code that these tools generate is your work product**, granted to you perpetually and irrevocably for any purpose, with no attribution required.
