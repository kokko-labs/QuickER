# Database Round-Tripping

*English | [日本語](database.ja.md)*

QuickER imports the schema of a live database into a diagram, detects the differences between the diagram and the database and applies a sync script, and generates DDL from the diagram. Because the diagram and the database round-trip in both directions, running an import and a diff sync lets you see how far the diagram and the live database have drifted apart, and close the gap.

## Supported DBMS

| DBMS | Schema import | Diff sync | DDL generation | Dialect switch | Notes |
|---|:-:|:-:|:-:|:-:|---|
| SQL Server | ✅ | ✅ | ✅ | ✅ | Descriptions sync with extended properties (MS_Description) |
| PostgreSQL | ✅ | ✅ | ✅ | ✅ | 13 and later. Descriptions sync with `COMMENT ON` |
| MySQL | ✅ | ✅ | ✅ | ✅ | 8.0 and later (MariaDB is not supported). Descriptions sync with `COMMENT` clauses |
| Oracle | ✅ | ✅ | ✅ | ✅ | 19c and later. Descriptions sync with `COMMENT ON` |
| SQLite | ✅ | ✅ | ✅ | ✅ | File DB. Diff sync that involves column changes, drops, or FK changes uses table rebuilds. No description mechanism |

Import and sync against live databases have real-DB integration tests (SQL Server / PostgreSQL / MySQL / Oracle use Testcontainers containers; SQLite uses a real file DB). The container-based tests run only in environments where Docker is available and are skipped automatically on CI, and each DBMS is verified against one representative version rather than across the whole supported range in the table.

## Schema import (DB → diagram)

The "DB Import" button on the toolbar opens the "Import from Database" connection dialog.

### Specifying the connection

- **Server-type DBMS** — specify the target DB, host, port (leave empty for the dialect's default), database name, user name, and password. For SQL Server you can also choose the authentication mode (Windows / SQL Server) and "Trust the server certificate (TrustServerCertificate)". Oracle has a service-name field
- **SQLite** — specify the file path via "Browse" (import works on existing files only)
- **Test Connection** — runs a real schema fetch and reports the number of tables detected

### Connection profiles

Connection settings can be saved under a name and recalled later from "Saved Connections". Profiles are stored in `%AppData%\QuickER\connections.json`, and passwords are stored in separate files, **encrypted with Windows DPAPI (CurrentUser scope)** — only when the "Save" checkbox is on, and never in plain text under the shipped configuration. The last-used connection is remembered automatically and restored on the next launch.

### What gets imported

- Tables (views are excluded) and columns (type, length, precision, nullability)
- Primary keys (preserving the column order of composite keys)
- Foreign keys (including the constraint name and the ON DELETE / ON UPDATE referential actions). When the FK columns on the referencing side themselves form that table's primary key or a unique constraint, the relationship is classified as one-to-one. A foreign key made up of multiple columns is imported as a relationship, but the column-to-column mapping is not restored (one relationship carries a single column pair). SQLite does not persist FK constraint names, so a constraint name is synthesized on import
- Table and column descriptions (SQL Server's MS_Description extended properties, PostgreSQL's `obj_description` / `col_description`, MySQL's `TABLE_COMMENT` / `COLUMN_COMMENT`, and Oracle's `user_tab_comments` / `user_col_comments`). SQLite has no description mechanism and is out of scope

The import result is merged into the current diagram. Tables and columns are matched by name, and matching elements take over the identity of the current diagram's elements, so their layout, width, color, and notes — along with the named queries that reference them — are preserved. Only newly imported tables are placed in free space; the whole diagram is auto-arranged only when nothing in the current diagram matches (a completely new import). A confirmation appears when the current diagram differs structurally, and also when the import would break named queries — in which case the queries to be removed are listed by name.

For how to pair your existing entity assets with the generated code after importing, see ["Coexisting with an existing codebase" in Using the generated code](code-generation.md#coexisting-with-an-existing-codebase).

## Diff sync (diagram → DB)

The "DB Sync" button on the toolbar opens the "DB Schema Sync (Apply Diff)" dialog. It compares the current state of the database with the contents of the diagram, shows the list of differences and a preview of the generated SQL, and executes only the items you select.

### Detected differences

- Adding and dropping tables
- Adding, altering (type, nullability), and dropping columns
- Adding and dropping foreign keys
- Setting, updating, and removing table and column descriptions (SQLite is out of scope)
- Changes to the column order (syncable on SQLite / MySQL)

Elements with the same name are treated as the same element, so a rename is detected as "drop + add."

Column order is compared as the relative order of the columns common to both sides, excluding additions and deletions. On SQLite and MySQL it can be synced as a selectable diff item (unselected by default): SQLite folds it into the table rebuild, and MySQL uses `ALTER TABLE ... MODIFY ... AFTER`, moving as few columns as possible. On the other three dialects it is shown for information only and is not synced.

### Safety by design

- **Destructive differences (drops and type changes) are unselected by default**, and executing them shows a confirmation stating that destructive changes are included
- The generated script is ordered by dependency (add tables → add columns → alter columns → drop FKs → drop columns → drop tables → add FKs → descriptions), with a heading comment per section
- A foreign-key addition whose FK column cannot be resolved is emitted as a skip comment instead of invalid DDL, and the diff list says so as well
- On SQL Server, PostgreSQL, and SQLite the script runs in a transaction and rolls back on failure. On MySQL / Oracle, DDL is implicitly committed by design, so a warning explains that a mid-script failure can leave changes partially applied
- A SQLite run that involves rebuilds shows a dedicated confirmation listing the tables to be rebuilt
- When the initial diff detection finds no differences at all, the dialog reports that and stays open; once a sync has been applied, it re-reads the diff and closes automatically if no differences remain

### Description sync

Table and column descriptions sync on the four dialects that have a description mechanism: SQL Server uses MS_Description extended properties, PostgreSQL and Oracle use `COMMENT ON`, and MySQL uses `COMMENT` clauses. Checking for an existing description to choose between add and update applies to SQL Server's extended properties only; PostgreSQL and Oracle simply re-run `COMMENT ON`, and MySQL re-runs the `COMMENT` clause (both idempotent in themselves). Clearing a description in the diagram removes it. SQLite has no description mechanism, so descriptions are neither diffed nor synced there.

### SQLite diff sync (table rebuilds)

Because SQLite's `ALTER TABLE` supports only limited changes, sync operations that involve column changes or deletions use a table-rebuild approach. A new table is created from the combined definition of "the database's current state + the selected differences," the data is migrated, the old table is swapped out, and auxiliary objects such as indexes are recreated. The rebuild runs inside a transaction, and before committing it checks foreign-key integrity (`PRAGMA foreign_key_check`); on violations it rolls back and reports the violating tables.

The sync-target SQLite file can also be created fresh with the "Create new" button in the connection dialog — apply the whole diagram to an empty database to set it up from scratch.

## Dialect switching

The "Target DB:" combo on the right of the toolbar switches the diagram's target DBMS at any time.

- Each column type is converted automatically along the path "source dialect → neutral canonical type → new dialect" where a mapping exists
- Columns that could not be converted keep their original types and are listed in a warning dialog
- The switch and the type conversions are undone together with a single Undo

## DDL generation

Choose SQL DDL from "Export" on the toolbar to output the full set of CREATE statements in the diagram's target dialect. Table and column descriptions are included in the DDL as well (sp_addextendedproperty on SQL Server, COMMENT ON for PostgreSQL / Oracle, COMMENT clauses on MySQL, and comment lines on SQLite). For export operations in general, see [Import and export](import-export.md).

## Related pages

- [Tutorial (from design to running code)](getting-started.md)
- [Import and export](import-export.md)
- [CLI reference](cli.md) — `quicker scaffold` (generating code directly from a DB)
