# MCP server (quicker mcp)

*[日本語](mcp.ja.md) | English*

`quicker mcp` starts a [Model Context Protocol](https://modelcontextprotocol.io) server over the stdio transport (stdin/stdout, JSON-RPC). It exposes tools for editing ER diagrams and generating code, so an external AI agent (Claude Code, Codex, and so on) can build and evolve a QuickER diagram as part of its own workflow. The agent launches `quicker mcp` as a child process and talks to it over stdin/stdout.

The server is **stateless**: it takes no options and keeps no in-memory diagram. Every tool takes the target diagram file as its `file` argument, and each call is a complete "load → modify → save" cycle against that file. Concurrent agents (or a single agent working on several diagrams) simply pass different `file` paths.

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

Any MCP client that supports the stdio transport can use the server. Configure it to launch the command `quicker` with the single argument `mcp` (Codex, for example, takes the same command/args pair in its own MCP-server configuration). This requires the `quicker` command to be on `PATH` — see [installing the CLI](cli.md). Until the CLI is published to NuGet, build the CLI once (`dotnet build QuickER.slnx`) and point the command at the built assembly instead (`command: "dotnet"`, `args: ["<repo>/src/QuickER.Cli/bin/Debug/net10.0/QuickER.Cli.dll", "mcp"]`). Do not use `dotnet run` here: its build output goes to stdout, which is the JSON-RPC protocol channel.

## Tools

The server exposes 16 tools: 10 for ER diagram editing, 3 for named queries, and 3 for code generation. **Every tool requires a `file` argument** (the path to the diagram JSON, i.e. the GUI save format / `DiagramDocument`) — except `get_generation_config_schema`, the one information-only tool, which takes no arguments at all. The tables below list the other arguments. Required arguments are marked ✅.

### ER diagram editing

| Tool | Arguments | Description |
|---|---|---|
| `create_diagram` | `target_dbms` ✅ (`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`) | Create a new, empty diagram file for the given target DBMS. Fails if the file already exists (this tool only creates new diagrams) |
| `get_diagram_summary` | — | Return a text listing of the tables, columns, and relationships in the diagram |
| `add_entity` | `table_name` ✅, `description` | Add a new table (no columns are created) |
| `remove_entity` | `table_name` ✅ | Remove a table, along with its relationships |
| `add_column` | `table_name` ✅, `column_name` ✅, `data_type` ✅, `is_primary_key`, `is_nullable`, `description` | Add a column to a table. Each table has exactly one primary key column (composite PKs are not supported) |
| `remove_column` | `table_name` ✅, `column_name` ✅ | Remove a column from a table |
| `set_entity_property` | `table_name` ✅, `new_table_name`, `memo`, `description` | Change a table's name, memo, or description |
| `set_column_property` | `table_name` ✅, `column_name` ✅, `description`, `data_type`, `is_nullable` | Change a column's description, data type, or nullability (specify at least one) |
| `add_relationship` | `source_table` ✅, `target_table` ✅, `relationship_type` ✅ (`OneToOne` / `OneToMany` / `ManyToMany`), `source_column`, `target_column` | Add a foreign key between two tables. A relationship references exactly one column to one column (composite FKs are not supported) |
| `remove_relationship` | `source_table` ✅, `target_table` ✅ | Remove the relationship between two tables |

### Named queries

Named queries are stored on the diagram and become repository methods when C# code is generated (see [using the generated code](code-generation.md)). Entities and columns are referenced by name and resolved when the tool runs.

| Tool | Arguments | Description |
|---|---|---|
| `set_query` | `table_name` ✅, `query_name` ✅, `returns` ✅ (`list` / `single` / `count` / `scalar` / `projection`), `description`, `scalar_type`, `implementation` (`dsl` / `sql` / `manual`, default `dsl`), `condition`, `sql`, `parameters`, `order_by`, `paging`, `result_type_name`, `fields` | Define or replace (upsert) a query on a table. Matched by (`table_name`, `query_name`): an existing query is replaced wholesale (its id is preserved), otherwise a new one is added. The definition is validated before saving; on any error the file is left unchanged |
| `list_queries` | — | List the queries in the diagram, grouped by table, with each query's return shape, implementation, condition/SQL summary, and parameters |
| `remove_query` | `table_name` ✅, `query_name` ✅ | Remove a single query. Fails if no such query exists |

`set_query`'s nested arguments:

- `scalar_type` — required when `returns` = `scalar`; a dialect-neutral type token (e.g. `decimal(12,2)`).
- `condition` — a mini-DSL search condition (comparisons, `AND`/`OR`/`NOT`, parentheses, `IS [NOT] NULL`, `[NOT] LIKE`, `[NOT] IN`, `CONTAINS`/`STARTSWITH`/`ENDSWITH`), used when `implementation` = `dsl` (omit for no filter). Column names refer to the table's columns; `@name` refers to a declared parameter.
- `sql` — an object mapping a dialect name (`sqlserver` / `postgresql` / `mysql` / `oracle` / `sqlite`) to a raw SQL string, used when `implementation` = `sql`.
- `parameters` — an array of `{ name` ✅ `, type, source_column, is_list }`. Give exactly one of `type` (a dialect-neutral token) or `source_column` (a column of this table, whose generated type is used).
- `order_by` — an array of `{ column` ✅ `, descending }` (valid only when `returns` is `list`, `single`, or `projection`; with `single` it selects the first row).
- `paging` — a boolean; when true, `take`/`skip` parameters are added.
- `result_type_name` / `fields` — required when `returns` = `projection`. `fields` is an array of `{ name` ✅ `, type, source_column, is_nullable }` (again, exactly one of `type` or `source_column`).

Validation is strict about anything that would fail at runtime and lenient about hygiene warnings: a mini-DSL syntax error, an unknown column or undeclared `@parameter`, an undeclared parameter in raw SQL, or a structural mismatch (missing `scalar_type`/`fields`, both or neither of a parameter's `type`/`source_column`, misused `order_by`, an unknown SQL dialect) refuses the save; an unused parameter or a multi-statement SQL is reported as a warning and the save proceeds. Type-token contents are not checked here — they are validated at generation time.

### Code generation

| Tool | Arguments | Description |
|---|---|---|
| `generate_csharp` | `out_dir` ✅, `config`, `provider` | Generate C# code (Entity / EditModel / Mapper / Repository, etc.) into an output directory, using the same pipeline as `quicker generate`. `config` is a generation settings JSON (same semantics as `quicker generate --config`; see [CLI reference](cli.md#settings-file-quickerjson)). Call `get_generation_config_schema` for the full list of `config` keys |
| `generate_ddl` | `out_file` ✅, `provider` | Generate a DDL (CREATE TABLE / foreign key) SQL script and write it to a `.sql` file |
| `get_generation_config_schema` | *(none)* | Return a machine-readable JSON catalog of every key valid in the settings JSON (`quicker.json`) that `generate_csharp`'s `config` accepts: each key's name, type, default, category, allowed values, and description, plus cross-key rules and an example. Lets an agent write a config without external docs. This is the only tool that takes no `file` argument |

For the two file-based generation tools (`generate_csharp` / `generate_ddl`), `provider` is optional: when omitted it defaults to the diagram's target DBMS (or `sqlserver` if the diagram has none). Its accepted values are the same five dialects as `create_diagram`'s `target_dbms`.

## Typical flow

A diagram is built up one file-level call at a time. For example, to design a customer / order schema for SQLite and generate its DDL and C# code:

1. `create_diagram` — `file` = `shop.json`, `target_dbms` = `sqlite`
2. `add_entity` — `table_name` = `customers`; then `add_column` for `customer_id` (`data_type` = `integer`, `is_primary_key` = true) and the remaining columns
3. `add_entity` — `table_name` = `orders`; then `add_column` for `order_id` (PK), `customer_id`, and so on
4. `add_relationship` — `source_table` = `customers`, `target_table` = `orders`, `relationship_type` = `OneToMany`, `source_column` = `customer_id`, `target_column` = `customer_id`
5. `generate_ddl` — `out_file` = `shop.sql`, and/or `generate_csharp` — `out_dir` = `./Generated`

Call `get_diagram_summary` at any point to read back the current tables and relationships. Before writing a `config` for `generate_csharp`, call `get_generation_config_schema` to discover the available keys and their defaults.

## Notes

- **The GUI follows external changes.** When the server writes a diagram that is open in the GUI, the GUI notices and keeps up: if the GUI has no unsaved edits it reloads the file automatically (zoom and scroll position are preserved) and shows a brief status note. A conflict only arises when the GUI itself has unsaved changes and something else writes the file; in that single case the GUI asks whether to reload (discarding your unsaved edits) or keep editing. Keeping diagram files under git is still recommended so changes stay reviewable.
- **DiagramDocument validation.** The editing tools refuse a file that does not exist, JSON that is not a `DiagramDocument` (an object with `Version` and `Schema`), and documents saved in a newer format version than this tool supports (to avoid discarding unknown data). `get_diagram_summary` still reads a newer-format document, with a warning.
- **Layout is not written by the server.** A newly created file contains schema only (no coordinates), so opening it in the GUI auto-arranges all tables. Columns and tables added to an existing file are placed in free space the next time it is opened in the GUI.

## Related

- [CLI reference (generate / scaffold / reverse, quicker.json)](cli.md) — the `quicker generate` pipeline that the code generation tools reuse
- [Configuring AI chat](ai-chat.md) — the in-app AI chat, which edits the currently open diagram inside the GUI (in contrast to this external MCP server, which agents drive over stdio)
