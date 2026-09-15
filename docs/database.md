# Database round-tripping

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
- **Encryption (SSL Mode)** — for PostgreSQL and MySQL, how strongly the connection requires TLS. It defaults to "Unspecified", which leaves the keyword off the connection string and the decision to the driver; see the note below. It is saved per connection profile, and profiles written before this setting existed load as "Unspecified"
- **SQLite** — specify the file path via "Browse" (import works on existing files only)
- **Command timeout** — how long a single schema-import or sync statement may run. It defaults to 60 seconds and applies to every dialect including SQLite; `0` means no limit (the ADO.NET convention). A blank field, a non-numeric entry, or a negative value is rejected by both OK and *Test Connection*, so the dialog never falls back to the default behind your back. It is saved per connection profile, and profiles written before this setting existed load with the default
- **Test Connection** — runs a real schema fetch and reports the number of tables detected

> **Note:** "Trust the server certificate (TrustServerCertificate)" is checked by default so that local or containerized SQL Server instances with self-signed certificates work out of the box. This setting skips server certificate validation, so when connecting to a production or remote server that has a properly issued certificate, uncheck it to keep man-in-the-middle detection effective. The setting is saved per connection profile.

#### Encryption in transit, per dialect

Database credentials travel over this connection, so it is worth knowing what each dialect does when you change nothing.

| Dialect | Default when nothing is set | How to require certificate validation |
| --- | --- | --- |
| SQL Server | Encrypted, **certificate not validated** (TrustServerCertificate is checked) | Uncheck "Trust the server certificate" |
| PostgreSQL | Npgsql's own default, which encrypts when the server supports it but **does not validate the certificate** | Set **Encryption (SSL Mode)** to `VerifyCa` or `VerifyFull` |
| MySQL | MySqlConnector's own default, same shape: encrypts when it can, **no validation** | Set **Encryption (SSL Mode)** to `VerifyCa` or `VerifyFull` |
| Oracle | **Plaintext TCP** | Not offered in the dialog — see below |
| SQLite | Not applicable (a local file) | — |

The **Encryption (SSL Mode)** values mean:

- `Unspecified` — write no keyword at all and leave the decision to the driver (the default; the connection is exactly what it was before this setting existed)
- `Disable` — never encrypt
- `Prefer` — encrypt when the server supports it, without validating the certificate
- `Require` — refuse to connect unencrypted, still without validating the certificate
- `VerifyCa` — require encryption and validate that the certificate chains to a trusted CA
- `VerifyFull` — `VerifyCa` plus a check that the certificate belongs to the host you asked for

`Require` protects against passive eavesdropping only: without validation, a man in the middle can present any certificate. `VerifyFull` is the setting to use against a server that has a properly issued certificate.

**Oracle** is deliberately not offered this choice, because the managed driver's TLS does not have this shape. It is switched on by the protocol prefix of the data source (`tcps://host:2484/service`) rather than by a connection-string keyword, which means the listener port has to change with it; TCPS always validates the certificate chain, so there is no equivalent of `Require`; and there is no negotiated mode, so there is no equivalent of `Prefer`. When you need TLS to an Oracle database, use `quicker scaffold --connection`, which passes the connection string through untouched:

```
quicker scaffold --connection "Data Source=tcps://db.example.com:2484/ORCLPDB1?ssl_server_dn_match=true;User Id=app;Password=***" --out ./Generated --provider oracle
```

On Windows, the trusted CA is taken from the host's own certificate store (the Microsoft certificate store), so an Oracle wallet is not required; on other platforms the driver falls back to the system OpenSSL trust store, and a wallet may still be needed depending on how the host is set up. `ssl_server_dn_match=true` is what makes the driver check that the certificate belongs to the server you asked for. Its default has varied between driver versions — in the versions this was checked against it is off, which makes a bare `tcps://` closer to `VerifyCa` than to `VerifyFull` — so state it explicitly rather than relying on the default, and confirm the behaviour against the driver version you ship. Oracle's *native network encryption* is a separate mechanism that is configured outside the connection string and authenticates no server, so it is not a substitute.

For `quicker scaffold` against PostgreSQL or MySQL there is no separate option either: `SSL Mode` is an ordinary connection-string keyword, so put it in `--connection` (`...;SSL Mode=VerifyFull`).

### Connection profiles

Connection settings can be saved under a name and recalled later from "Saved Connections". Profiles are stored in `%LOCALAPPDATA%\QuickER\connections.json`, and passwords are stored in separate files, **encrypted with Windows DPAPI (CurrentUser scope)** — only when the "Save" checkbox is on, and never in plain text under the shipped configuration. The last-used connection is remembered automatically and restored on the next launch.

### What gets imported

- Tables (views, temporary tables, and the container tables behind materialized views are excluded) and columns (type, length, precision, nullability)
- Primary keys. A composite key is modeled as a flag on each column rather than as an ordered list of its own, so generated DDL emits its columns in the order they are declared in the table — which is not necessarily the order the original constraint used
- Foreign keys (including the constraint name and the ON DELETE / ON UPDATE referential actions). When the FK columns on the referencing side themselves form that table's primary key or a unique constraint, the relationship is classified as one-to-one. A foreign key made up of multiple columns is imported with every column pair, in declaration order. SQLite does not persist FK constraint names, so a constraint name is synthesized on import
- Table and column descriptions (SQL Server's MS_Description extended properties, PostgreSQL's `obj_description` / `col_description`, MySQL's `TABLE_COMMENT` / `COLUMN_COMMENT`, and Oracle's `user_tab_comments` / `user_col_comments`). SQLite has no description mechanism and is out of scope

Nothing else is imported — see [Modeling fidelity](#modeling-fidelity) for the full boundary and what it means for DDL generation.

#### What the import looks at

Each dialect imports one scope, and objects outside it are not visible to the import at all:

| DBMS | Imported scope |
|---|---|
| SQL Server | Every schema of the connected database |
| PostgreSQL | The `public` schema only |
| MySQL | The connected database (MySQL's "schema" *is* the database) |
| Oracle | The connecting user's own schema (the `USER_*` views) |
| SQLite | The single connected database file |

A foreign key that points outside this scope cannot become a relationship, because its parent table is not in the diagram. Such a key is skipped and reported (see below) rather than dropped silently. On Oracle, constraints that are `DISABLE`d are not imported either: a disabled constraint enforces nothing, so importing it would make the diagram claim a guarantee the database does not provide.

#### Import warnings

The import always succeeds, but some parts of a schema cannot be carried into the diagram exactly as they were declared. When that happens the completion dialog lists what was affected (`quicker scaffold` writes the same list to standard error), so the difference is visible instead of silent:

- A PostgreSQL **domain type** column is imported as its base type — the diagram has no notion of domains, and the domain's `CHECK` constraint is lost
- A **foreign key that points outside the imported scope** is skipped
- A table whose **columns could not be read at all** is imported without columns
- One of two tables whose **names differ only in letter case** is skipped, because the import cannot keep them apart (PostgreSQL and Oracle allow both through quoted identifiers, and so does MySQL when `lower_case_table_names` is 0)
- A column whose **data type cannot be written into SQL safely** is named, along with the type text. The import keeps it, but DDL generation and schema sync refuse the *whole diagram* until it is corrected — a type is neither an identifier nor a string literal, so there is nowhere to quote it (see [What a data type may contain](#what-a-data-type-may-contain)). SQLite stores whatever type text a table was declared with, and PostgreSQL reports a type name that needs quoting with the quotes attached, so either can produce one
- A **partitioned table** is imported as its parent, and the partitioning itself is not modelled — generated DDL creates the table unpartitioned

When nothing is affected, the dialog is the usual one-line completion message.

#### How imported type names are spelled

The import reads each column's declared type from the database's own catalog and then writes it in QuickER's shorter vocabulary — `varchar(50)` rather than PostgreSQL's canonical `character varying(50)`, `timestamptz(3)` rather than `timestamp(3) with time zone`. Only the spelling changes; the type does not.

On PostgreSQL this also fixes type names that used to come back wrong, because the previous source of type information could not express certain modifiers. A column declared `numeric(10,-2)` was imported as `numeric(10,2046)` — a spelling PostgreSQL itself rejects, so the diagram could not even generate DDL for it. `bit(8)` and `bit varying(16)` lost their length, `interval day to second(3)` lost its unit, `time(3) with time zone` lost its precision, and an array came back as an internal name such as `_int4` — or, for `varchar(20)[]`, as `_varchar`, dropping the length. These columns now carry their real declared type.

**If you import a PostgreSQL database into a diagram created by an older version, expect a one-time diff.** Any column of the kinds above will show up once as a column change in the diff sync, because the diagram holds the old broken spelling and the database reports the correct one. Healthy columns are unaffected: their spelling is unchanged.

The import result is merged into the current diagram. Tables and columns are matched by name, and matching elements take over the identity of the current diagram's elements, so their layout, width, color, and notes — along with the named queries that reference them — are preserved. Only newly imported tables are placed in free space; the whole diagram is auto-arranged only when nothing in the current diagram matches (a completely new import). A confirmation appears when the current diagram differs structurally, and also when the import would break named queries — in which case the queries to be removed are listed by name.

For how to pair your existing entity assets with the generated code after importing, see ["Coexisting with an existing codebase" in Using the generated code](code-generation.md#coexisting-with-an-existing-codebase).

## Modeling fidelity

The diagram is a design model, not a copy of the database. What it models is exactly what round-trips:

- Tables and columns (type, length, precision, nullability)
- Primary keys, as a flag on each column (a composite key's own column order is not modeled — see [What gets imported](#what-gets-imported))
- Foreign keys with their column pairs, constraint name, and referential actions
- UNIQUE constraints
- Table and column descriptions

Everything else is outside the model. It is not imported, it never appears in generated DDL, and the diff sync neither detects nor touches it:

| Schema element | On import | In generated DDL | In a SQLite table rebuild |
|---|---|---|---|
| `DEFAULT` | Not imported | Not emitted | Lost |
| `CHECK` constraint | Not imported | Not emitted | Lost |
| `COLLATE` | Not imported | Not emitted | Lost |
| Generated / computed column | Imported as an ordinary column; the expression is lost | Emitted as an ordinary column | Lost |
| `IDENTITY` / `AUTO_INCREMENT` / `AUTOINCREMENT` | Not imported | Not emitted | Lost (a primary key whose declared type is `INTEGER` stays a rowid alias, so implicit auto-numbering survives — see the note below) |
| Non-unique index | Not imported | Not emitted | Preserved (captured as its `CREATE` statement and replayed) |
| Trigger | Not imported | Not emitted | Preserved (same) |
| View, stored procedure, function, sequence | Not imported | Not emitted | Not applicable |

**The rowid alias hangs on the declared type token, not on the column being an integer.** SQLite treats a primary key as a rowid alias only when its declared type is spelled exactly `INTEGER`; the table-constraint form is not what decides it (`x INTEGER, PRIMARY KEY (x)` and `x INTEGER NOT NULL, CONSTRAINT PK_t PRIMARY KEY (x)` — the form QuickER emits — are both aliases), but `INT` is not one. A diagram imported from SQLite carries the declared types it came with, so the guarantee above holds for it. A diagram created in QuickER, or one whose dialect was switched away from SQLite and back, emits `INT` for a 32-bit integer and `BIGINT` for a 64-bit one, and a key declared that way is an ordinary column: it has no implicit auto-numbering to preserve, and an insert without a key value stores NULL. QuickER's generated repositories assign keys in the application, so this changes nothing for them.

Outside a SQLite table rebuild, these objects are simply left alone: the diff sync does not diff them, so an index or a `DEFAULT` you created by hand stays in the database. The rebuild is the one operation that recreates a table from the diagram's definition, which is why it can drop what the diagram cannot express — QuickER lists the attributes it found on the table in the execution confirmation (see [SQLite diff sync](#sqlite-diff-sync-table-rebuilds)).

**DDL generation creates a new schema from a design; it does not reproduce a database you imported.** Unless the source database happens to use nothing outside the modeled set above, the generated `CREATE` statements are a different schema — one that shares the tables, columns, keys, and constraints, and has none of the defaults, checks, collations, auto-numbering, indexes, or triggers. To move an existing database, use its own tooling; use QuickER's DDL to stand up a new one from the design.

## Diff sync (diagram → DB)

The "DB Sync" button on the toolbar opens the "DB Schema Sync (Apply Diff)" dialog. It compares the current state of the database with the contents of the diagram, shows the list of differences and a preview of the generated SQL, and executes only the items you select.

### Detected differences

- Adding and dropping tables
- Adding, altering (type, nullability), and dropping columns
- Adding and dropping foreign keys
- Changing the primary key (adding or removing it, or changing its column set)
- Setting, updating, and removing table and column descriptions (SQLite is out of scope)
- Changes to the column order (syncable on SQLite / MySQL)

Elements with the same name are treated as the same element, so a rename is detected as "drop + add."

Column order is compared as the relative order of the columns common to both sides, excluding additions and deletions. On SQLite and MySQL it can be synced as a selectable diff item (unselected by default): SQLite folds it into the table rebuild, and MySQL uses `ALTER TABLE ... MODIFY ... AFTER`, moving as few columns as possible. MySQL's `MODIFY` re-specifies the column from the live catalog, so moving a column keeps its attributes and its comment — see [what MySQL's `MODIFY COLUMN` re-specifies](#what-mysqls-modify-column-re-specifies). On the other three dialects it is shown for information only and is not synced.

### Safety by design

- **Destructive differences (drops and type changes) are unselected by default**, and executing them shows a confirmation stating that destructive changes are included
- The generated script is ordered by dependency (add tables → add columns → drop FKs → drop old primary keys → alter columns → add new primary keys → drop columns → drop tables → add FKs → descriptions), with a heading comment per section
- A foreign-key addition whose FK column cannot be resolved is emitted as a skip comment instead of invalid DDL
- Composite (multi-column) foreign keys are handled as first-class: a relationship holds an ordered list of column pairs, so import, DDL generation, and sync all carry every pair. That includes the implicit drop-and-recreate triggered by a primary-key change and SQLite's table rebuilds — a composite foreign key is never silently narrowed to a single column
- On SQL Server, PostgreSQL, and SQLite the script runs in a transaction and rolls back on failure. On MySQL / Oracle, DDL is implicitly committed by design, so a warning explains that a mid-script failure can leave changes partially applied
- A SQLite run that involves rebuilds shows a dedicated confirmation listing the tables to be rebuilt
- When the initial diff detection finds no differences at all, the dialog reports that and stays open; once a sync has been applied, it re-reads the diff and closes automatically if no differences remain. After a failed run the diff is also re-read automatically, so the list reflects any partially applied changes (relevant on MySQL / Oracle)

### Primary key sync

Changing a table's primary key in the diagram (adding or removing it, or changing the set of key columns) is detected as a single per-table diff item, unselected by default. The old primary-key constraint is located by querying the database catalog at run time, so imported databases with arbitrary constraint names work. The script drops the old key first, applies any selected column alterations, and then adds the new key, so a change that also makes the old key column nullable succeeds in one sync.

Foreign keys that reference the changed primary key are dropped and re-created automatically around the change (on SQL Server, the same applies when altering a column that participates in a foreign key). If a referenced column is no longer part of the new key, the confirmation dialog warns that re-creating those foreign keys may fail: on SQL Server and PostgreSQL such a failure rolls the whole script back, while on MySQL / Oracle it can leave the foreign keys dropped (partially applied).

### Description sync

Table and column descriptions sync on the four dialects that have a description mechanism: SQL Server uses MS_Description extended properties, PostgreSQL and Oracle use `COMMENT ON`, and MySQL uses `COMMENT` clauses. Checking for an existing description to choose between add and update applies to SQL Server's extended properties only; PostgreSQL and Oracle simply re-run `COMMENT ON`, and MySQL re-runs the `COMMENT` clause (both idempotent in themselves). Clearing a description in the diagram removes it. SQLite has no description mechanism, so descriptions are neither diffed nor synced there.

#### What MySQL's `MODIFY COLUMN` re-specifies

MySQL has no statement that changes only a column's comment, and none that changes only its position. Both go through `ALTER TABLE ... MODIFY COLUMN`, which re-states the **whole** column definition: whatever the new definition leaves out is dropped from the column. MySQL commits DDL implicitly, so there is no rolling that back.

QuickER therefore takes the definition from two different places, depending on what the change is about.

- **Description sync and column-order sync** rebuild the definition from `information_schema.COLUMNS` at execution time and replace only the trailing clause they exist to change — the comment, or `AFTER` / `FIRST`. Everything the diagram does not model is carried over untouched: `DEFAULT` (literal and expression, `BIT` and binary literals included), `AUTO_INCREMENT`, the collation and with it the character set, `ON UPDATE CURRENT_TIMESTAMP`, `INVISIBLE`, a spatial `SRID`, and generated (`VIRTUAL` / `STORED`) columns. Column-order sync preserves the column's existing comment as well.
- **Altering a column** — a type or nullability change — re-specifies from the diagram, because the diagram is what that change expresses. It therefore **does** drop the attributes listed above, none of which the diagram can represent. Such diffs are unselected by default.

Two things do not come through the rebuild, both because the catalog does not record them the way they were written:

- `COLUMN_FORMAT` and `STORAGE` (NDB Cluster) have no `information_schema` column to read them from, so they cannot be carried over.
- A character-set introducer on a string literal inside a generated-column expression (`_latin1'x'`) is re-parsed and written back under the connection's character set (`_utf8mb4'x'`). The expression still means the same thing; only the text the catalog records for it changes.

If the column no longer exists when the script runs, its statement folds into a no-op — and the run is still reported as successful, since nothing failed. If the column does exist but its definition cannot be rebuilt, the statement fails instead, so a half-applied column order is never reported as a success.

Before running the script, the sync clears `NO_BACKSLASH_ESCAPES` from its own session's `sql_mode`; the global setting is not touched. QuickER escapes the literals it writes under MySQL's default rule, and under that mode the same text would be stored with its backslashes doubled.

The live rebuild is one more reason MariaDB is out of scope: it records `COLUMN_DEFAULT` with different semantics, so a definition rebuilt from its catalog cannot be guaranteed to reproduce the column.

### SQLite diff sync (table rebuilds)

Because SQLite's `ALTER TABLE` supports only limited changes, sync operations that involve column changes or deletions use a table-rebuild approach. A new table is created from the combined definition of "the database's current state + the selected differences," the data is migrated, the old table is swapped out, and auxiliary objects such as indexes are recreated. The rebuild runs inside a transaction, and before committing it checks foreign-key integrity (`PRAGMA foreign_key_check`); on violations it rolls back and reports the violating tables.

The rebuilt table is created from the diagram's definition, so anything outside [what the diagram models](#modeling-fidelity) — `AUTOINCREMENT`, `DEFAULT`, `CHECK`, `COLLATE`, and generated columns — is **not** reproduced and is silently lost, which is exactly why the `DEFAULT` values you set outside QuickER disappear after a rebuild. Indexes and triggers are safe: they are captured as their full `CREATE` statements and replayed. Implicit auto-numbering is also safe as long as the diagram's declared type is `INTEGER`, because such a primary key stays a rowid alias in the rebuilt table (a key declared `INT` — what a diagram created in QuickER or converted through another dialect holds — was never an alias to begin with; see [what the diagram models](#modeling-fidelity)). To make the loss visible, QuickER reads the live `CREATE TABLE` statement of every table it is about to rebuild and lists the attributes it found in the execution-confirmation dialog; if the table needs those attributes, re-apply them yourself after the sync (or run the change as hand-written SQL).

The **table options** `WITHOUT ROWID` and `STRICT` are lost the same way, but **without a warning**: they sit after the closing parenthesis of the column list, the detector behind that confirmation only reads what is inside it, and neither word is in its keyword list either. A rebuilt table therefore comes back as an ordinary rowid, non-strict table with nothing said about it. If a table depends on either option, do not sync it through a rebuild — apply the change as hand-written SQL instead.

The sync-target SQLite file can also be created fresh with the "Create new" button in the connection dialog — apply the whole diagram to an empty database to set it up from scratch.

## Dialect switching

The "Target DB:" combo on the right of the toolbar switches the diagram's target DBMS at any time.

- Each column type is converted automatically along the path "source dialect → neutral canonical type → new dialect" where a mapping exists
- Columns that could not be converted keep their original types and are listed in a warning dialog
- The switch and the type conversions are undone together with a single Undo
- A `numeric(10,-2)` / `NUMBER(10,-2)` column — a scale that rounds to hundreds — converts only between PostgreSQL and Oracle. SQL Server and MySQL reject a negative scale outright, and SQLite's `DECIMAL` is an affinity that carries no rounding at all, so such a column is reported as unconvertible on those three rather than written out as DDL that will not run
- SQL Server's `rowversion` / `timestamp` becomes a plain `BLOB` on SQLite, and its NOT NULL is lifted at the same time (SQLite assigns nothing, so a locally created row has no version until a sync writes one). The conversion is one-way: converting that `BLOB` back to SQL Server yields `varbinary(max)`, not a row version — see [Multi-target repositories](code-generation.md#multi-target-repositories-sqlserver--sqlite) for what the column means on each side, and [Bidirectional sync support](code-generation.md#bidirectional-sync-support---generate-sync-support) for generating the code that keeps the two databases in step. Other dialects have no equivalent at all, so a `rowversion` column is reported as unconvertible there

## DDL generation

Choose SQL DDL from "Export" on the toolbar to output the full set of CREATE statements in the diagram's target dialect. Table and column descriptions are included in the DDL as well (sp_addextendedproperty on SQL Server, COMMENT ON for PostgreSQL / Oracle, COMMENT clauses on MySQL, and comment lines on SQLite). For export operations in general, see [Import and export](import-export.md).

### What a data type may contain

A column's data type is written into the SQL exactly as the diagram holds it — a type is neither an identifier nor a string literal, so there is nowhere to quote it. Both DDL generation and diff sync therefore accept only a plain type expression, and refuse the whole output when a column holds anything else, naming the `table.column` and the type text. A type may contain:

- Words separated by single spaces, each made of letters, digits, `_`, and `$` — `int`, `double precision`, `LONG RAW`, `UNSIGNED BIG INT`
- Parenthesised arguments after any word. An argument is a number, a word (letters, digits, `_`), or `*`, one or two of them, optionally followed by Oracle's unit keyword — `nvarchar(max)`, `decimal(10,2)`, `decimal(10,-2)`, `NUMBER(*,2)`, `VARCHAR2(50 CHAR)`, `TIMESTAMP(6) WITH LOCAL TIME ZONE`, `INTERVAL DAY(2) TO SECOND(6)`, `decimal(10,2) unsigned`, and PostGIS's `geometry(Point,4326)` / `geography(MultiPolygon)`
- A trailing `[]` for a PostgreSQL array, repeatable for more dimensions — `integer[]`, `integer[][]`
- A MySQL value list — `enum('a','b')`, `set('x','y')`

Everything the five dialect catalogues offer, and everything schema import builds from a live database, is inside this. What falls outside it:

- A type carrying `;`, a quote, a backtick, a backslash, a comment marker (`--`, `/*`, and MySQL's `#`, which comments out the rest of the line and would silently swallow the `NOT NULL`, `COMMENT` or `AFTER` that follows a type in `MODIFY COLUMN`), or a line break — none of which any dialect needs, and each of which would change the statement rather than the column
- A type with leading or trailing whitespace. What is validated has to be exactly what is written out, and trimming first breaks that: `"\nGO\n"` reads as the single word `GO` once trimmed, but written out it can trip SQL Server's batch splitting. An empty or whitespace-only type still means "not set" and passes as before
- A type whose second or later word is a column clause keyword — `NOT`, `NULL`, `DEFAULT`, `PRIMARY`, `KEY`, `REFERENCES`, `CHECK`, `GENERATED`, `IDENTITY`, `COLLATE`, `UNIQUE`, `ON`. A type is interpolated into the middle of a column definition, so `int NOT NULL` or `varchar(50) COLLATE Latin1_General_BIN` produces perfectly valid DDL whose nullability, default or collation disagrees with what the diagram declares — no syntax error tells you. None of these words appears in any real type notation the catalogues or schema import produce

Two known exclusions are worth naming because a database can produce them: PostgreSQL's one-byte `"char"` type (quoted, which the SQL it would land in cannot carry), and the placeholder `user-defined` that import falls back to when it cannot resolve a type name. Both would fail as DDL anyway; you now find out at generation time with the column named. One more edge comes from SQLite, which accepts any words as a declared type and hands them back verbatim on import: a SQLite database created with SQL Server-flavoured DDL can carry a declaration like `INT IDENTITY(1,1)`, whose second word is on the clause-keyword list above and is refused — again by name, at generation time.

### Line breaks and control characters in names

A table, column or constraint name holding a line break or a control character is refused by DDL generation and by schema-sync script generation, which name the place (`TABLE 'x'`, `COLUMN 'x'.'y'`) and produce nothing. The set is the C0 controls, DEL, and the three Unicode line separators U+0085, U+2028 and U+2029. C# code generation reports the same names as a pre-generation error.

Quote characters are deliberately fine: `"`, `'`, `]` and backticks occur in real databases and are escaped where they are written. Descriptions are not refused either — a line break in one is legitimate, and it is folded to a space where it lands in a `--` comment line.

This is separate from whether QuickER *understands* a type. A type the dialect catalogue cannot parse is still carried as it is — it simply gets no neutral type token and does not convert on a dialect switch.

## Related pages

- [Tutorial (from design to running code)](getting-started.md)
- [Import and export](import-export.md)
- [CLI reference](cli.md) — `quicker scaffold` (generating code directly from a DB)
