# Database round-tripping

*English | [日本語](database.ja.md)*

QuickER imports the schema of a live database into a diagram, detects the differences between the diagram and the database and applies a sync script, and generates DDL from the diagram.
Because the diagram and the database round-trip in both directions, running an import and a diff sync lets you see how far the diagram and the live database have drifted apart, and close the gap.

## Supported DBMS

All five dialects support schema import, diff sync, DDL generation and dialect switching.
How diff sync differs between them is listed in [Detected differences](#detected-differences).

| DBMS | Supported versions |
|---|---|
| SQL Server | 2016 and later |
| PostgreSQL | 13 and later |
| MySQL | 8.0 and later (MariaDB is not supported) |
| Oracle | 19c and later |
| SQLite | File DB |

Import and sync against live databases have real-DB integration tests (SQL Server / PostgreSQL / MySQL / Oracle use Testcontainers containers; SQLite uses a real file DB).
The container-based tests run only in environments where Docker is available and are skipped automatically on CI.
Each DBMS is also verified against one representative version rather than across the whole supported range in the table.

## Schema import (DB → diagram)

The "DB Import" button on the toolbar opens the "Import from Database" connection dialog.

### Specifying the connection

- **Server-type DBMS**: specify the target DB, host, port (leave empty for the default port in the table below), database name, user name, and password.
  Only an empty port means the default; a value that is not a number or is outside 1 to 65535 is rejected by OK, *Test Connection*, and saving a profile (a typo never connects to another instance on the default port).
  With Microsoft Entra ID authentication (formerly Azure AD) on SQL Server, a user name signs in interactively, and leaving it empty uses the default credential chain (environment variables, managed identity and so on).
  The Oracle connection is built as EZConnect `host:port/service`
- **Encryption (SSL Mode)**: for PostgreSQL and MySQL, how strongly the connection requires TLS.
  It defaults to "Unspecified" mode, which leaves the keyword off the connection string and the decision to the driver (see the note below).
  It is saved per connection profile, and profiles written before this setting existed load as "Unspecified"
- **Command timeout**: how long a single schema-import or sync statement may run.
  It defaults to 60 seconds and applies to every dialect including SQLite; `0` means no limit (the ADO.NET convention).
  A blank field, a non-numeric entry, or a negative value is rejected by OK, *Test Connection*, and saving a profile, so the dialog never falls back to the default behind your back.
  It is saved per connection profile, and profiles written before this setting existed load with the default
- **Test Connection**: runs a real schema fetch and reports the number of tables detected.
  OK is disabled while a test is running: the test cannot be cancelled, so confirming and closing would leave it holding the connection on its own

<!-- dialect-table: connection-settings -->

| DBMS | Where to connect | Default port | Dialect-specific settings |
| --- | --- | --- | --- |
| SQL Server | Host and database | — (not written: the driver's default or an instance name) | Authentication (Windows / SQL Server / Microsoft Entra ID), TrustServerCertificate |
| PostgreSQL | Host and database | 5432 | Encryption (SSL Mode) |
| MySQL | Host and database | 3306 | Encryption (SSL Mode) |
| Oracle | Host and service name (the database-name field) | 1521 | — (a note that the connection is not encrypted; TLS through `quicker scaffold --connection`) |
| SQLite | File path via "Browse" (import: an existing file; sync: "Create new" also makes one) | — | — |

> **Note:** "Trust the server certificate (TrustServerCertificate)" is checked by default so that local or containerized SQL Server instances with self-signed certificates work out of the box. This setting skips server certificate validation, so when connecting to a production or remote server that has a properly issued certificate, uncheck it to keep man-in-the-middle detection effective. The setting is saved per connection profile.

#### Encryption in transit, per dialect

Database credentials travel over this connection, so it is worth knowing what each dialect does when you change nothing.

| Dialect | Default when nothing is set | How to require certificate validation |
| --- | --- | --- |
| SQL Server | Encrypted, **certificate not validated** (TrustServerCertificate is checked) | Uncheck "Trust the server certificate" |
| PostgreSQL | Npgsql's own default, which encrypts when the server supports it but **does not validate the certificate** | Set **Encryption (SSL Mode)** to `VerifyCa` or `VerifyFull` |
| MySQL | MySqlConnector's own default, same shape: encrypts when it can, **no validation** | Set **Encryption (SSL Mode)** to `VerifyCa` or `VerifyFull` |
| Oracle | **Plaintext TCP** | Not offered in the dialog (see below) |
| SQLite | Not applicable (a local file) | — |

The **Encryption (SSL Mode)** values mean:

- `Unspecified`: write no keyword at all and leave the decision to the driver (the default; the connection is exactly what it was before this setting existed)
- `Disable`: never encrypt
- `Prefer`: encrypt when the server supports it, without validating the certificate
- `Require`: refuse to connect unencrypted, still without validating the certificate
- `VerifyCa`: require encryption and validate that the certificate chains to a trusted CA
- `VerifyFull`: `VerifyCa` plus a check that the certificate belongs to the host you asked for

`Require` protects against passive eavesdropping only.
Without validation, a man in the middle can present any certificate.
`VerifyFull` is the setting to use against a server that has a properly issued certificate.

**Oracle** is deliberately not offered this choice, because the managed driver's TLS does not have this shape.
It is switched on by the protocol prefix of the data source (`tcps://host:2484/service`) rather than by a connection-string keyword, which means the listener port has to change with it.
TCPS always validates the certificate chain, so there is no equivalent of `Require`, and there is no negotiated mode, so there is no equivalent of `Prefer`.
When you need TLS to an Oracle database, use `quicker scaffold --connection`, which passes the connection string through untouched:

```
quicker scaffold --connection "Data Source=tcps://db.example.com:2484/ORCLPDB1?ssl_server_dn_match=true;User Id=app;Password=***" --out ./Generated --provider oracle
```

On Windows, the trusted CA is taken from the host's own certificate store (the Microsoft certificate store), so an Oracle wallet is not required.
On other platforms the driver falls back to the system OpenSSL trust store, and a wallet may still be needed depending on how the host is set up.
`ssl_server_dn_match=true` is what makes the driver check that the certificate belongs to the server you asked for.
Its default has varied between driver versions (in the versions this was checked against it is off, which makes a bare `tcps://` closer to `VerifyCa` than to `VerifyFull`), so state it explicitly rather than relying on the default, and confirm the behaviour against the driver version you ship.
Oracle's *native network encryption* is a separate mechanism that is configured outside the connection string and authenticates no server, so it is not a substitute.

For `quicker scaffold` against PostgreSQL or MySQL there is no separate option either.
`SSL Mode` is an ordinary connection-string keyword, so put it in `--connection` (`...;SSL Mode=VerifyFull`).

### Connection profiles

Connection settings can be saved under a name and recalled later from the "Saved Connections" list.
Profiles are stored in `%LOCALAPPDATA%\QuickER\connections.json`, and passwords are stored in separate files, **encrypted with Windows DPAPI (CurrentUser scope)**, only when the "Save" checkbox is on and never in plain text under the shipped configuration.
The last-used connection is remembered automatically and restored on the next launch.
The DB Sync dialog fixes the target DB to the diagram's dialect, so a last-used connection recorded for a different dialect is not restored (host, file path, and service name mean different things per dialect, and the restored values would not connect).
Saving with the "Save" checkbox on but the password box empty stores the checkbox as off as well, because there is nothing to save: the dialog never shows a password as saved when there is nothing to restore.
When the encrypted file of a saved password cannot be read (another program holding it for an instant, or a file encrypted by another user or machine), the password box is left empty and the status line says so.
Saving over that profile with the password box still empty and the "Save" checkbox on is refused, because saving would delete a password that merely could not be read.
Enter the password again, or clear the "Save" checkbox, and then save.
Saving is keyed on the profile name (spaces before and after it are ignored).
A profile with the same name and the same DB kind is overwritten; otherwise a new profile is created.
When the profile that would be overwritten is not the one selected in the list, QuickER asks first, because its connection settings and saved password would be replaced with no way back.
Saving over the profile you loaded is not confirmed.
Use the "Rename" button when all you want is a different name.
It changes only the name of the selected profile to the one in the box, keeping its connection settings and its saved password (the encrypted password file is not touched).
Typing a new name and pressing "Save" stores a separate profile instead of renaming.
Saving, renaming, and deleting all re-read `connections.json` before writing it back. When that read fails, for instance because another program is holding the file, the operation is **cancelled without writing anything** and says so in the status line (the saved profiles are left untouched; try again in a moment).
When a write fails partway (a full disk, a failure to write or delete an encrypted password file, and so on), the change may have been applied only partly, so the status line says so and the list is reloaded from the file.
A partly applied change never leaves a profile with the saved password of another connection.
The old encrypted password file is removed before `connections.json` is written, and the new one is written after it.
If `connections.json` then cannot be written, the profile keeps its old settings without a saved password, and you enter the password again.
When recording the last connection on OK fails, the connection still goes ahead, and a message tells you that it was not recorded.
If the file turns out to be corrupted, it is moved aside once to `connections.json.corrupt` before being overwritten.

### What gets imported

- Tables (views, materialized views, external tables, child partitions, temporary tables, and the container and log tables behind materialized views are excluded) and columns (type, length, precision, nullability)
- Primary keys, preserving a composite key's own column order.
  `PRIMARY KEY (b, a)` imports as exactly that, and generated DDL emits the constraint's declared order rather than the order the columns happen to appear in the table, so the column order of the backing index survives the round trip
- Foreign keys (including the constraint name and the ON DELETE / ON UPDATE referential actions).
  When the FK columns on the referencing side themselves form that table's primary key or a unique constraint, the relationship is classified as one-to-one.
  A foreign key made up of multiple columns is imported with every column pair, in declaration order.
  SQLite does not persist FK constraint names, so a constraint name is synthesized on import
- Table and column descriptions (where each DBMS keeps them is in the table under [Description sync](#description-sync))
- Two column marks: computed (generated) columns and identity columns that reject explicit values (the expression and the seed are not imported)

Nothing else is imported.
See [Modeling fidelity](#modeling-fidelity) for the full boundary and what it means for DDL generation.

#### What the import looks at

Each dialect imports one scope, and objects outside it are not visible to the import at all:

| DBMS | Imported scope |
|---|---|
| SQL Server | Every schema of the connected database |
| PostgreSQL | The `public` schema only |
| MySQL | The connected database (MySQL's "schema" *is* the database) |
| Oracle | The connecting user's own schema (the `USER_*` views) |
| SQLite | The single connected database file |

On Oracle, a column the user made `INVISIBLE` is imported as well (it is a column deliberately hidden, not a column that does not exist).
Oracle gives such a column no ordinal, so it comes after the visible columns in the diagram, with several such columns in the order they were created.
The diagram does not model the `INVISIBLE` attribute itself, so DDL generated from the diagram declares the column as an ordinary visible column.
The system-generated columns a function-based index adds to the table (`SYS_NC…$`) are not imported.

A foreign key that points outside this scope cannot become a relationship, because its parent table is not in the diagram.
Such a key is skipped and reported (see below) rather than dropped silently.
SQLite does not check that a foreign key's parent table exists when the table is created, so a database can carry keys that point at a table that is not there; those are treated the same way.
MySQL keys whose parent table is not in the connected database (created with `FOREIGN_KEY_CHECKS = 0`, or left behind when the parent table was dropped that way) are treated the same way too.
A table whose name contains a dot is not imported, on SQLite as on the other DBMSs, because the diagram reads the first dot of a table name as a schema qualifier (on SQLite, the name of an attached database).
See the import warnings below.

Constraints that are disabled are not imported either (`DISABLE` on Oracle, `NOCHECK CONSTRAINT` on SQL Server): a disabled constraint enforces nothing, so importing it would make the diagram claim a guarantee the database does not provide.
A SQL Server foreign key added `WITH NOCHECK` but currently enabled *is* imported, because it is enforced from then on.

#### Import warnings

The import always succeeds, but some parts of a schema cannot be carried into the diagram exactly as they were declared.
When that happens the completion dialog lists what was affected (`quicker scaffold` writes the same list to standard error), so the difference is visible instead of silent.
The list has one heading per kind with the count, followed by one line per place.

<!-- dialect-table: import-warnings -->

| Warning | SQL Server | PostgreSQL | MySQL | Oracle | SQLite | In the diagram |
| --- | :-: | :-: | :-: | :-: | :-: | --- |
| Domain or alias type | ✅ (alias type) | ✅ (domain) | — | — | — | Base type |
| Column that declares no type | — | — | — | — | ✅ | `BLOB` |
| Default value | ✅ | ✅ | ✅ | ✅ | ✅ | Not imported |
| Automatic numbering that accepts given values | — | ✅ | ✅ | ✅ | ✅ | Not imported |
| Collation, `INVISIBLE`, `ON UPDATE`, `SRID` | — | — | ✅ | — | — | Not imported |
| Name with spaces around it | ✅ | ✅ | ✅ | ✅ | ✅ | Spaces removed |
| Foreign key outside the import scope | — (every schema is imported) | ✅ | ✅ | ✅ | ✅ | Skipped |
| Disabled constraint | ✅ | — | — | ✅ | — | Skipped |
| Table with no readable column | — | ✅ | ✅ | — | — | Imported without columns |
| Table names differing only in case or surrounding spaces | ✅ | ✅ | ✅ | ✅ | ✅ | One kept |
| Column names differing only in case or surrounding spaces | ✅ | ✅ | ✅ | ✅ | ✅ | One kept |
| Table name containing a dot | ✅ | ✅ | ✅ | ✅ | ✅ | Skipped |
| Computed or generated column | ✅ | ✅ | ✅ | ✅ | ✅ | Read-only column without its expression |
| Automatic numbering that refuses given values | ✅ | ✅ | — | ✅ | — | Marked as a column the database numbers |
| View | ✅ | ✅ | ✅ | ✅ | ✅ | Skipped, listed by name |
| Materialized view | — | ✅ | — | ✅ | — | Skipped, listed by name |
| External table | ✅ | ✅ | — | ✅ | — | Skipped, listed by name |
| Child partition | — | ✅ | — | — | — | Skipped, listed by name |
| Temporary table | — | — | — | ✅ | — | Skipped, listed by name |
| Virtual table | — | — | — | — | ✅ | Skipped, listed by name |
| Temporal history table | ✅ | — | — | — | — | Skipped, listed by name |
| Foreign key to a table the import left out | ✅ | ✅ | ✅ | ✅ | ✅ | Skipped |
| Partitioned table | ✅ | ✅ | ✅ | ✅ | — | Imported unpartitioned |
| Data type that cannot be written into SQL safely | ✅ | ✅ | ✅ | ✅ | ✅ | Kept; DDL and sync refuse the whole diagram |
| Primary key that allows NULL | — | — | — | — | ✅ | Made NOT NULL |

The table lists every kind; the notes below add what a cell cannot say.

- A PostgreSQL **domain type** column is imported as its base type.
  The diagram has no notion of domains, so the domain's `CHECK` constraint is lost
- A SQLite **column that declares no type** is imported as `BLOB`, and each such column is named.
  SQLite stores values of any type in such a column, while generated code reads it as `byte[]`, so a row holding another kind of value fails to read
- A column's **default value** is not imported, and each such column is named with its default.
  The diagram has no notion of defaults, so generated DDL creates the column without `DEFAULT`.
  A default of `NULL` means the same as having no default and is not reported.
  On Oracle, where an empty string is `NULL`, this includes `DEFAULT ''` and a `NULL` or `''` in parentheses such as `DEFAULT (NULL)`
- **Automatic numbering that also accepts given values** is not imported: MySQL `AUTO_INCREMENT`, PostgreSQL `serial` and `GENERATED BY DEFAULT AS IDENTITY`, Oracle `GENERATED BY DEFAULT AS IDENTITY` and SQLite `AUTOINCREMENT`.
  The column is named once as automatic numbering, not again as a default value
- On MySQL, a column's own **collation** (when it differs from the table default), **`INVISIBLE`**, **`ON UPDATE`** and **`SRID`** are not imported, and each column is named
- A **name with spaces around it** (a table, column, foreign key or unique constraint name) is imported without the spaces, and each name is reported.
  MySQL does not allow trailing spaces, so only leading ones occur there
- A **foreign key that points outside the imported scope** is skipped (on SQLite and MySQL this covers a key whose parent table does not exist)
- One of two tables whose **names differ only in letter case or surrounding spaces** is skipped, because the diagram cannot keep them apart (PostgreSQL and Oracle allow both through quoted identifiers, MySQL does when `lower_case_table_names` is 0, SQL Server does in a database with a case-sensitive collation, and SQLite, which folds only ASCII letters, does for names such as `Äpfel` and `äpfel`)
- A **table whose name contains a dot** is not imported, and each such table is named.
  The diagram reads the first dot of a table name as the separator between a schema and a table, so `Order.Lines` would be read as the table `Lines` in the schema `Order`.
  Imported as it is, the table would be another table to the rest of QuickER: the generated QuickER Repository would report that the table does not exist, and DDL and schema sync would create another table, or fail when that schema does not exist.
  On SQL Server this covers a table in `dbo` whose name contains a dot and every table in a schema whose name contains a dot.
  A dotted table name in another schema reads back as the same table (`a.b.t` is the table `b.t` in the schema `a`), so it is imported
- Likewise, one of two **columns of a table whose names differ only in letter case or surrounding spaces** is skipped (the later one in declaration order).
  The key flags, descriptions, unique constraints and foreign keys of the skipped column do not attach to the one that is kept (a unique constraint or foreign key that includes the skipped column is not imported)
- A **computed or generated column** (a SQL Server computed column, `GENERATED ALWAYS AS` on MySQL / PostgreSQL / SQLite, an Oracle virtual column) is imported as a read-only column without its expression.
  The diagram does not model expressions, so generated DDL creates an ordinary column (with a comment asking for the expression to be added by hand).
  Generated code leaves the column out of INSERT / UPDATE (see [the code generation notes](code-generation.md#computed--generated-columns))
- A column with **automatic numbering that refuses given values** (SQL Server `IDENTITY`, PostgreSQL and Oracle `GENERATED ALWAYS AS IDENTITY`) is imported with a mark, as a column the database numbers, without its numbering definition (seed and increment), and is named column by column.
  The diagram does not model the numbering definition, so generated DDL creates an ordinary column (with a comment asking for the identity property to be added by hand).
  Generated code leaves such a column out of INSERT / UPDATE unless it is part of the primary key (see [the code generation notes](code-generation.md#columns-the-database-numbers))
- A SQLite **virtual table** (FTS5, R*Tree and the like) is not imported.
  It cannot be represented as an ordinary table; its shadow tables are excluded along with it, and each virtual table is reported once
- **Objects the diagram does not hold** are left out and listed by name.
  External tables are PostgreSQL foreign tables, Oracle `ORGANIZATION EXTERNAL` tables and SQL Server external tables, and a PostgreSQL child partition is listed with the parent table it belongs to (the parent itself is imported as described below).
  Objects the database or an extension owns (system tables, the container and log tables behind materialized views, tables an extension installs) are left out without a mention
- A SQL Server **temporal table** is imported as its current-data table only.
  The history table is an attachment the system maintains for it rather than an entity of its own.
  The current table's period columns (`GENERATED ALWAYS AS ROW START / END`) are values the database produces, so they are treated as computed columns, and a `HIDDEN` period column is imported the same way.
  Generated code leaves those columns out of its writes, so inserts and updates against the current table work as they are (see [computed columns](code-generation.md#computed--generated-columns))
- A column whose **data type cannot be written into SQL safely** is named, along with the type text.
  The import keeps it, but DDL generation and schema sync refuse the *whole diagram* until it is corrected, because a type is neither an identifier nor a string literal and there is nowhere to quote it (see [What a data type may contain](#what-a-data-type-may-contain)).
  SQLite stores whatever type text a table was declared with, and PostgreSQL reports a type name that needs quoting with the quotes attached, so either can produce one
- A **partitioned table** is imported as its parent, and the partitioning itself is not modelled.
  Generated DDL creates the table unpartitioned
- A SQLite **primary key that allows NULL** is imported as NOT NULL, and the adjusted columns are named.
  In a rowid table, any primary key other than a single column declared exactly `INTEGER` can hold NULL, while the diagram and the editor always treat a primary key as NOT NULL.
  Only the columns actually adjusted are named: a key whose columns are already declared `NOT NULL` is not reported.
  If existing rows carry a NULL primary key, a sync from this diagram (which rebuilds the table) can fail while copying the data across

When nothing is affected, the dialog is the usual one-line completion message.

#### How imported type names are spelled

The import reads each column's declared type from the database's own catalog and then writes it in QuickER's shorter vocabulary.
Only the spelling changes; the type does not.

<!-- dialect-table: imported-type-spelling -->

| Dialect | Declared as | Imported as |
| --- | --- | --- |
| SQL Server | `datetime2(7)` / `datetime2(3)` | `datetime2` / `datetime2(3)` |
| PostgreSQL | `character varying(50)` / `timestamp(3) with time zone` | `varchar(50)` / `timestamptz(3)` |
| MySQL | `decimal(12)` | `decimal(12,0)` |
| Oracle | `INTEGER` / `SMALLINT` / `NUMBER(*,0)` | `NUMBER(*,0)` |
| SQLite | `VARCHAR(50)` (kept as declared) | `VARCHAR(50)` |

In a diagram imported by an earlier version, columns whose spelling this changed show up once as a type change in the next diff sync.
Import the diagram again, or change the types in the diagram to match the database.

The import result is merged into the current diagram.
Tables and columns are matched by name, and matching elements take over the identity of the current diagram's elements, so their layout, width, color, and notes are preserved, along with the named queries that reference them.
A name that differs only in letter case also matches, as long as it is unique on both sides ignoring case (MySQL on Windows, for example, returns table names in lower case); the imported spelling then replaces the diagram's.
When names differing only in case exist side by side, an exact match is taken first, and a name that stays ambiguous is treated as a new table or column.
Only newly imported tables are placed in free space; the whole diagram is auto-arranged only when nothing in the current diagram matches (a completely new import).
A confirmation appears when the current diagram differs structurally, and also when the import would break named queries, in which case the queries to be removed are listed by name.

For how to pair your existing entity assets with the generated code after importing, see ["Coexisting with an existing codebase" in Using the generated code](code-generation.md#coexisting-with-an-existing-codebase).

## Modeling fidelity

The diagram is a design model, not a copy of the database.
What it models is exactly what round-trips.

- Tables and columns (type, length, precision, nullability)
- Primary keys, including a composite key's own column order
- Foreign keys with their column pairs, constraint name, and referential actions
- UNIQUE constraints
- Table and column descriptions
- Two column marks: computed (generated) columns and identity columns that reject explicit values (the expression and the seed are not modeled)

Everything else is outside the model.
It is not imported, it never appears in generated DDL, and the diff sync neither detects nor touches it.

<!-- dialect-table: modeling-fidelity -->

| Schema element | On import | In generated DDL | In a SQLite table rebuild |
| --- | --- | --- | --- |
| `DEFAULT` | Not imported (named) | Not emitted | Lost |
| `CHECK` constraint | Not imported | Not emitted | Lost |
| `COLLATE` | Not imported (named on MySQL) | Not emitted | Lost |
| Generated / computed column | Imported as a read-only column without its expression, carrying the computed mark, and reported | Emitted as an ordinary column, with a note | Lost (rebuilt as an ordinary column, with a note) |
| Automatic numbering that refuses given values | Imported as the mark of a column the database numbers, and reported (SQL Server, PostgreSQL, Oracle; the numbering definition is lost) | Emitted as an ordinary column, with a note | Not applicable (SQLite has none) |
| Automatic numbering that accepts given values | Not imported (named) | Not emitted | Lost (a primary key whose declared type is `INTEGER` stays a rowid alias, so implicit auto-numbering survives; see the note below) |
| Non-unique index | Not imported | Not emitted | Preserved (captured as its `CREATE` statement and replayed) |
| Trigger | Not imported | Not emitted | Preserved (same) |
| Table options after the column list (`WITHOUT ROWID`, `STRICT`) | Not imported | Not emitted | Lost |
| Constraint clauses (`ON CONFLICT`, `DEFERRABLE INITIALLY DEFERRED`) | Not imported | Not emitted | Lost |
| View | Not imported (named) | Not emitted | Preserved (it keeps pointing at the rebuilt table) |
| Stored procedure, function, sequence | Not imported | Not emitted | Not applicable |

**The rowid alias hangs on the declared type token, not on the column being an integer.**
SQLite treats a primary key as a rowid alias only when its declared type is spelled exactly `INTEGER`.
The table-constraint form is not what decides it (`x INTEGER, PRIMARY KEY (x)` and the form QuickER emits, `x INTEGER NOT NULL, CONSTRAINT PK_t PRIMARY KEY (x)`, are both aliases), but `INT` is not one.
A diagram imported from SQLite carries the declared types it came with, so the guarantee above holds for it.
A diagram created in QuickER, or one whose dialect was switched away from SQLite and back, emits `INT` for a 32-bit integer and `BIGINT` for a 64-bit one.
A key declared that way is an ordinary column: it has no implicit auto-numbering to preserve, and an insert without a key value stores NULL (QuickER's generated repositories assign keys in the application, so this changes nothing for them).

Outside a SQLite table rebuild, these objects are simply left alone: the diff sync does not diff them, so an index or a `DEFAULT` you created by hand stays in the database.
The rebuild is the one operation that recreates a table from the diagram's definition, which is why it can drop what the diagram cannot express.
QuickER lists the attributes it found on the table in the execution confirmation (see [SQLite diff sync](#sqlite-diff-sync-table-rebuilds)).

**DDL generation creates a new schema from a design; it does not reproduce a database you imported.**
Unless the source database happens to use nothing outside the modeled set above, the generated `CREATE` statements are a different schema: one that shares the tables, columns, keys, and constraints, and has none of the defaults, checks, collations, auto-numbering, indexes, or triggers.
To move an existing database, use its own tooling; use QuickER's DDL to stand up a new one from the design.

## Diff sync (diagram → DB)

The "DB Sync" button on the toolbar opens the "DB Schema Sync (Apply Diff)" dialog.
It compares the current state of the database with the contents of the diagram, shows the list of differences and a preview of the generated SQL, and executes only the items you select.

### Detected differences

- Adding and dropping tables
- Adding, altering (type, nullability), and dropping columns
- Adding, dropping, and recreating foreign keys (a foreign key whose referential actions, column pairs, or referenced table alone changed)
- Adding, dropping, and recreating unique constraints (a named unique constraint whose columns alone changed)
- Changing the primary key (adding or removing it, or changing its column set or column order)
- Setting, updating, and removing table and column descriptions
- Changes to the column order

Elements with the same name are treated as the same element, so a rename is detected as "drop + add."
A table or column whose name differs from the database's only in letter case is the same element too, as long as it is unique on both sides ignoring case, and the script then writes the database's spelling.
This matters where quoted names are case-sensitive: Oracle stores a table created without quotes as `ORDERS` and PostgreSQL as `orders`, so a diagram's `Orders` would otherwise name a table that does not exist.
A table or column that exists only in the diagram is created with the diagram's spelling.
A foreign key of an unnamed relationship is matched by the same default name the DDL gives it (`FK_{child table}_{parent table}`).

Column types are compared after bringing both sides to the spelling the database stores.
A database does not always remember a declaration as written: a column declared `numeric(18,0)` comes back as `numeric(18)`.
Two spellings of the same type are therefore not a difference.

<!-- dialect-table: same-type-spellings -->

| Dialect | Treated as the same type | Still a difference |
| --- | --- | --- |
| SQL Server | `numeric(18,0)` = `numeric(18)`, `datetime2(7)` = `datetime2`, `float(24)` = `real`, `float(53)` = `float`, `rowversion` = `timestamp` | `datetime2(3)` and `datetime2` |
| PostgreSQL | `timestamp` = `timestamp(6)`, `numeric(18,0)` = `numeric(18)` | |
| MySQL | `decimal(12)` = `decimal(12,0)`, `decimal` = `decimal(10,0)` | |
| Oracle | `TIMESTAMP` = `TIMESTAMP(6)`, `INTEGER` = `NUMBER(*,0)` | |
| SQLite | (compared as declared) | |

Only omitted defaults and aliases the database does not remember are aligned; types that differ in meaning (`datetime2(3)` and `datetime2`) are still a difference.

On PostgreSQL, a type change that crosses type families (`varchar` to `integer`, `text` to `date`, `integer` to `boolean` and the like) is written with `USING column::new_type`, so it goes through when every value converts.
A change to a string type is written without it, so a value too long for a shorter `varchar(n)` makes the sync fail instead of being cut off.
A type the import cannot classify (PostGIS types, user-defined types) is also written without it.

<!-- dialect-table: referential-actions -->

| Dialect | `ON UPDATE` | `SET DEFAULT` | `SET NULL` on a NOT NULL referencing column | In the diff |
| --- | --- | --- | --- | --- |
| SQL Server | ✅ | ✅ (a NOT NULL referencing column needs a DEFAULT constraint) | — (cannot be created; error 1761) | As declared |
| PostgreSQL | ✅ | ✅ | ✅ (deleting or updating a referenced parent row fails) | As declared |
| MySQL | ✅ | — (accepted but not applied; left out with a note) | — (cannot be created; error 1830) | `SET DEFAULT` compared as `NO ACTION` |
| Oracle | — (left out with a note) | — (left out with a note) | ✅ (`ON DELETE` only; deleting a referenced parent row fails) | `ON UPDATE` and `SET DEFAULT` compared as `NO ACTION` |
| SQLite | ✅ | ✅ | ✅ (deleting or updating a referenced parent row fails) | As declared |

Where the table shows an action is missing or not applied, the diff compares it as `NO ACTION`, and the DDL and the sync script create the foreign key without the clause and add a comment saying so.
MySQL (InnoDB) accepts `SET DEFAULT` in `ON DELETE` and `ON UPDATE` but does not apply it: deleting the parent row fails, as with `NO ACTION`.
SQL Server cannot create a `SET DEFAULT` foreign key when a NOT NULL referencing column has no DEFAULT constraint (error 1762).
The diagram does not carry column defaults, so the DDL adds a comment before such a foreign key, and the sync names the foreign keys it adds or recreates in the confirmation dialog.
It does not stop the sync, because the foreign key is created when the database column has a DEFAULT constraint.
A `SET NULL` foreign key on a NOT NULL referencing column cannot set the column to NULL.
SQL Server and MySQL refuse to create it (errors 1761 and 1830), so the sync leaves out such foreign keys that you add or recreate, and does not drop and re-add one that the database already has.
In MySQL, a column change that makes the referencing column of such a foreign key NOT NULL then fails before any foreign key is dropped.
In SQL Server, that column change goes through, and deleting or updating a referenced parent row then fails.
PostgreSQL, Oracle and SQLite create the foreign key, but deleting or updating a referenced parent row fails.
In every dialect, the DDL adds a comment before such a foreign key, and the sync names it in the confirmation dialog.

<!-- dialect-table: column-alteration -->

| | SQL Server | PostgreSQL | MySQL | Oracle | SQLite |
| --- | :-: | :-: | :-: | :-: | :-: |
| Altering a column reads the collation back and writes it again | ✅ | ✅ (only when it differs from the type's default) | ✅ (from the live definition) | — | — (lost in the table rebuild) |
| Altering a column keeps the `DEFAULT` | ✅ (dropped and added back under the same name) | ✅ (kept by the database) | ✅ (from the live definition) | ✅ (kept by the database) | — (lost in the table rebuild) |
| Changing the kind of type of a column with a default is confirmed | ✅ | ✅ | ✅ | ✅ | — |

Where the table above says so, altering a column writes the collation back when the new type is a string type, because without it the column goes back to the database's default collation.
On SQL Server, the DEFAULT constraint is dropped and added back around a type change because keeping it would make the change fail with error 5074.
Dropping a column on SQL Server drops its DEFAULT constraint first, for the same reason.
A CHECK constraint or an index of your own that refers to the column is not dropped: the diagram does not carry them, so the drop fails with error 5074 and the sync is rolled back.
A sync that changes the kind of type of a column with a default value is shown in the confirmation dialog where the table above says so.
When the default does not fit the new type, the sync fails on PostgreSQL and Oracle, the sync fails with error 1067 on MySQL, and an insert that uses the default fails on SQL Server.
When the sync fails on MySQL or Oracle, DDL is not rolled back, so the statements before it stay applied.
Recreating a foreign key is a single diff item that combines the drop and the add, and it is unselected by default (it drops the foreign key and adds it back under the same name, so on MySQL / Oracle a failed re-add can leave the foreign key missing).
Recreating a unique constraint works the same way: when a unique constraint keeps its name (or the generated name `UQ_{table}_{columns}` matches the database's name) but its columns change, the drop and the add become one diff item, unselected by default.
Adding a constraint under the name the database already uses would fail, and re-adding it can fail on duplicate values.
Unique constraints are paired in two steps.
First, each unique constraint you named in the diagram is paired with the database's constraint of the same name, so the same columns make no difference and other columns make a recreate.
The remaining constraints are then paired by their set of columns, so a unique constraint without a name in the diagram matches a database constraint on the same columns whatever its name.
Names generated for unnamed constraints are not used in the first step.
A constraint without a name in the database (SQLite) is paired by its columns only.
On every dialect, a composite foreign key whose column pairs differ only in their order is not a difference, because the pairs map parent columns to child columns and their order carries no meaning (Oracle does not even keep the declared order and returns the pairs in the order of the referenced key).
SQL Server and MySQL require the referencing columns to follow the order of the referenced key, so the DDL writes the pairs in the order of the referenced key (the primary key, or else a unique constraint).
The sync also writes every foreign key it adds or re-adds in the order of the referenced key as it will be after the sync.
Changing only the column order of a referenced composite primary key therefore re-adds its foreign keys in the new order.
SQLite reads a table name with a dot such as `a.b` as table b in an attached database a, so the DDL and the sync for that table fail unless that database is attached.
The DDL adds a comment before the table, and the sync lists such tables in the confirmation dialog.

Column order is compared as the relative order of the columns common to both sides, excluding additions and deletions.
Adding a column in the middle is therefore not a column-order difference by itself: the new column goes to the end of the table, and the next sync shows the order difference.
When existing columns are reordered at the same time, a single sync also puts the new column in place.
Where the table below shows that column order can be synced, it is a selectable diff item (unselected by default), and MySQL moves as few columns as possible.
MySQL's `MODIFY` re-specifies the column from the live catalog, so moving a column keeps its attributes and its comment (see [what MySQL's `MODIFY COLUMN` re-specifies](#what-mysqls-modify-column-re-specifies)).
On the other dialects it is shown for information only and is not synced.

A computed column's nullability is never compared, because the database derives it from the expression and the diagram does not hold the expression.
A computed column's type change can be selected where the table below shows it, and selecting it never changes the column's nullability.
PostgreSQL's `ALTER COLUMN ... TYPE` keeps the expression, and MySQL's `MODIFY` writes the expression and the `VIRTUAL` or `STORED` kind back from the live catalog.
On the other dialects it is shown for information only, because the type cannot be changed without the expression (SQL Server rejects the change and a SQLite rebuild would drop the expression).
Where the table below shows that a unique constraint on exactly the primary key's columns is not kept, such a constraint is not compared and does not become a diff item.
SQLite does not keep such a constraint, and Oracle rejects it; the primary key already guarantees uniqueness.
The DDL and the tables the sync creates leave that unique constraint out and add a comment saying so.
On Oracle, a sync that moves the primary key onto the columns of a unique constraint the database already has fails with ORA-02261.
Remove that unique constraint from the diagram and select its drop as well, or drop it in the database first.

<!-- dialect-table: sync-capabilities -->

| | SQL Server | PostgreSQL | MySQL | Oracle | SQLite |
| --- | :-: | :-: | :-: | :-: | :-: |
| Column order can be synced | — | — | ✅ (`MODIFY … AFTER`) | — | ✅ (rebuild) |
| A computed column's type change can be selected | — | ✅ | ✅ | — | — |
| A unique constraint on exactly the primary key's columns is kept | ✅ | ✅ | ✅ | — (left out with a note) | — (left out with a note) |
| A table name with a dot is a schema-qualified name | ✅ | ✅ | ✅ | ✅ | — (an attached database) |
| Foreign key names are stored | ✅ | ✅ | ✅ | ✅ | — (synthesized on import) |
| Descriptions are synced | ✅ | ✅ | ✅ | ✅ | — |
| A failed script rolls back | ✅ | ✅ | — | — | ✅ |
| Changes to an existing table run as statements | ✅ | ✅ | ✅ | ✅ | — (the table is rebuilt) |

### Safety by design

- **Destructive differences (drops and type changes) are unselected by default**, and executing them shows a confirmation stating that destructive changes are included
- The generated script is ordered by dependency (drop FKs → drop unique constraints → drop tables → add tables → add columns → drop old primary keys → alter columns → add new primary keys → drop columns → add unique constraints → add FKs → descriptions), with a heading comment per section.
  Tables are dropped before tables are added, so a table you rename in the diagram can keep the unique constraint names of the old table.
  On MySQL and Oracle, where DDL is committed implicitly, the old table stays dropped if adding the new one fails; running the sync again adds it.
  SQLite rebuilds tables and uses its own order
- A foreign-key addition whose FK column cannot be resolved is emitted as a skip comment instead of invalid DDL
- Dropping a table or a column also drops every foreign key that loses its table or column, even when dropping that foreign key is not selected (on SQLite the surviving child table is rebuilt without it).
  This also lets a parent table and its child be dropped together whatever order they are listed in
- Dropping a column also drops every unique constraint that uses the column, even when dropping that constraint is not selected, and the confirmation names each one.
  The constraint is not re-created on the remaining columns, so add a unique constraint to the diagram if you still need one
- A selected change that needs a change you did not select (a new table or column, or a primary key change) is left out of the script, and the confirmation names it together with what it needs.
  Dropping a column of the primary key needs the primary key change that removes the column.
  A foreign key you add or change needs the primary key change when it refers to the parent's new primary key, and that key is neither the primary key nor a unique constraint without the change.
  QuickER does not select the needed change for you
- Composite (multi-column) foreign keys are handled as first-class: a relationship holds an ordered list of column pairs, so import, DDL generation, and sync all carry every pair.
  That includes the implicit drop-and-recreate triggered by a primary-key change and SQLite's table rebuilds, and a composite foreign key is never silently narrowed to a single column
- On SQL Server, PostgreSQL, and SQLite the script runs in a transaction and rolls back on failure.
  On MySQL / Oracle, DDL is implicitly committed by design, so a warning explains that a mid-script failure can leave changes partially applied
- The Oracle script and DDL can also be run by hand in SQL*Plus.
  Ordinary statements end with `;`, and a `/` line follows only a PL/SQL block, so each statement runs exactly once (SQL*Plus runs the previous statement again on a `/` line)
- A SQLite run that involves rebuilds shows a dedicated confirmation listing the tables to be rebuilt.
  The list of tables to rebuild and notes such as recreated foreign keys appear in full in a scrollable details area of the confirmation
- When the initial diff detection finds no differences at all, the dialog reports that and stays open.
  Once a sync has been applied, it re-reads the diff and closes automatically if no differences remain.
  After a failed run the diff is also re-read automatically, so the list reflects any partially applied changes (relevant on MySQL / Oracle).
  If re-reading the diff itself fails (for example, the connection drops), the diff and the script are cleared and nothing can be run until a recalculation succeeds, so a stale script is never run
- While the diff is being recalculated or the script is running, Recalculate Diff, Execute, Select All, and Deselect All are disabled.
  While the sync script is running the dialog cannot be closed either, because the run would carry on outside the dialog. It can be closed during a recalculation

### Primary key sync

Changing a table's primary key in the diagram (adding or removing it, or changing the set or order of its key columns) is detected as a single per-table diff item, unselected by default.
The old primary-key constraint is located by querying the database catalog at run time, so imported databases with arbitrary constraint names work.
The script drops the old key first, applies any selected column alterations, and then adds the new key, so a change that also makes the old key column nullable succeeds in one sync.
A column that joins the new key becomes `NOT NULL` as part of the primary-key change, even when that column's own change is not selected (its type stays as it is in the database).

Foreign keys that rely on the changed primary key (they refer to the same columns as the old key) are dropped and re-created automatically around the change, while foreign keys that refer to a unique constraint are left alone.

<!-- dialect-table: foreign-key-rebuilds -->

| | SQL Server | PostgreSQL | MySQL | Oracle | SQLite |
| --- | :-: | :-: | :-: | :-: | :-: |
| Altering a column drops and re-adds its foreign keys | ✅ | — | ✅ | — | — (the table is rebuilt) |
| Altering a key column drops and re-adds the primary key and unique constraints | ✅ | — | — | — | — (the table is rebuilt) |
| A foreign key resting on the index of a dropped primary key or unique constraint is re-added | — | — | ✅ | — | — |
| Referencing columns must follow the order of the referenced key | ✅ | — | ✅ | — | — |
| String types differing only in length are allowed on the two ends | — (error 1753) | ✅ | ✅ | ✅ (same length unit) | ✅ |
| A failed re-add can leave the foreign key dropped | — | — | ✅ | ✅ | — |

A primary key dropped and re-added by a key column change (see the table above) is named `PK_{table}`.
On MySQL, a foreign key of the table whose primary key changes is also dropped and re-created when its columns lead the old primary key, because the primary key cannot be dropped while it is the only index supporting that foreign key.
The same applies when dropping a unique constraint whose index is the only one supporting a foreign key (MySQL refuses with error 1553 otherwise).
When a change leaves the two ends of a foreign key with different types after the sync (altering the type of only one side, for example), the foreign key is not dropped and re-added, and the column change fails as it is (the database stays unchanged).
QuickER treats such a type difference as one that can make re-adding the foreign key fail, and on MySQL / Oracle a failed re-add leaves the foreign key missing.
PostgreSQL and Oracle accept some of these combinations, such as integer types of different widths (Oracle also accepts `NUMBER` columns of different precisions).
An addition or a recreate of a foreign key that you selected is left out of the script for the same reason, and the confirmation names it.
A recreate is left out together with its drop, so the foreign key keeps its current definition.
SQLite rebuilds the table and does not check the types of a foreign key, so it applies the addition or the recreate as selected.
String types that differ only in length (`varchar(50)` and `varchar(100)`, for example) are not a mismatch where the table above allows them on the two ends; on Oracle the length unit (`CHAR` / `BYTE`) must be the same on both ends.
Changing the length of one end drops and re-adds the foreign key as usual.
SQL Server cannot create a foreign key whose ends differ in length (error 1753), so the DDL adds a comment before such a foreign key.
The confirmation dialog lists those foreign keys so that you can change both ends to the same type.
In that case no other foreign key is dropped and re-added either, because the column change fails anyway and the re-adding would never run.
If a referenced column is no longer part of the new key, the confirmation dialog warns that re-creating those foreign keys may fail.
Such a failure rolls the whole script back, or, where the table above says so, leaves the foreign keys dropped (partially applied).
The same warning appears when a foreign key you add or change refers to columns that are not exactly a primary key or a unique constraint after the sync.
Such a foreign key is written in the diagram's order, because there is no key to follow.
Only the set of referenced columns is compared, so a different column order alone is never warned about: where the table above says the referencing columns must follow the order of the referenced key, the sync writes them in that order.

### Description sync

Table and column descriptions sync on the four dialects that have a description mechanism.

<!-- dialect-table: descriptions -->

| Dialect | Imported from | Written with | Characters outside the Basic Multilingual Plane |
| --- | --- | --- | --- |
| SQL Server | `MS_Description` extended properties | `MS_Description` extended properties (the sync checks for an existing one to choose between add and update) | ✅ |
| PostgreSQL | `obj_description` / `col_description` | `COMMENT ON` | ✅ |
| MySQL | `TABLE_COMMENT` / `COLUMN_COMMENT` | `COMMENT` clauses | — (saved as `?`) |
| Oracle | `user_tab_comments` / `user_col_comments` | `COMMENT ON` | ✅ |
| SQLite | — (no description mechanism) | — (the DDL writes comment lines; not diffed or synced) | |

Where the table shows `COMMENT ON` or `COMMENT` clauses, the sync simply re-runs the statement or clause, which is idempotent in itself.
Clearing a description in the diagram removes it.
A description made only of spaces counts as no description: the DDL does not write it, the diff treats it as empty, and syncing it removes the description from the database.
A description is written only by its own description item, so adding a column does not set the column's description unless that item is selected too (on every dialect, including MySQL).
Where the table shows a character outside the Basic Multilingual Plane (such as an emoji) is saved as `?`, the comparison turns those characters in the diagram's description into `?` first, so syncing or applying the DDL does not leave a difference behind.
The sync confirmation names each such description, and the DDL writes a note before it.

On SQL Server, a table name without a schema refers to the connecting login's default schema, as it does in `CREATE TABLE`, and its description goes to the same table (the schema is taken from `SCHEMA_NAME()`).
The import writes tables outside `dbo` with their schema (`app.Widget`), so when your login's default schema is not `dbo`, write the schema into the diagram's table names as well; otherwise the next diff sees `Widget` and `app.Widget` as two different tables.

#### What MySQL's `MODIFY COLUMN` re-specifies

MySQL has no statement that changes only a column's comment, and none that changes only its position.
Both go through `ALTER TABLE ... MODIFY COLUMN`, which re-states the **whole** column definition: whatever the new definition leaves out is dropped from the column.
MySQL commits DDL implicitly, so there is no rolling that back.

QuickER therefore rebuilds the definition from `information_schema.COLUMNS` at execution time and replaces only what the change is about.

- **Description sync and column-order sync** replace only the trailing clause they exist to change (the comment, or `AFTER` / `FIRST`), and **altering a column** (a type or nullability change) replaces only the type and the nullability.
  Everything the diagram does not model is carried over untouched: `DEFAULT` (literal and expression, `BIT` and binary literals included), `AUTO_INCREMENT`, the collation and with it the character set, `ON UPDATE CURRENT_TIMESTAMP`, `INVISIBLE`, a spatial `SRID`, and generated (`VIRTUAL` / `STORED`) columns.
  Column-order sync and altering a column preserve the column's existing comment as well (changing it is the job of the description sync item).
- **Altering a column** writes the collation only when the new type is a string type (`JSON` and some other types do not accept `COLLATE`).
  When a kept attribute does not fit the new type (a default of `'abc'` kept while changing to `int`, for example), the change fails with a MySQL error.
  Such diffs are unselected by default.
- **Changing the primary key** removes `AUTO_INCREMENT` from an old primary key column before dropping the key, because MySQL refuses to drop it otherwise (error 1075).
  `AUTO_INCREMENT` is not added back because the diagram cannot represent it (the comment, default, and other attributes are carried over from the live definition).
- **Dropping a foreign key** also drops a non-unique index with the same name as the foreign key, unless another foreign key needs it.
  MySQL creates such an index for a foreign key that has no suitable index and keeps it after the foreign key is dropped, so re-creating a foreign key of the same name on other columns would fail (error 1061).
  When another foreign key needs the index, the index is renamed to that foreign key's name instead, unless the table already has an index of that name.
  An index you created yourself with the same name as the foreign key is dropped or renamed too, because the catalog cannot tell the two apart.
  A unique index is never dropped this way.
  When two foreign keys of the same table are dropped, the index they share is checked again after both are dropped, because the other foreign key still uses it when the first one is dropped.
  Re-adding them in another column order then does not fail with error 1061.

Two things do not come through the rebuild, both because the catalog does not record them the way they were written:

- `COLUMN_FORMAT` and `STORAGE` (NDB Cluster) have no `information_schema` column to read them from, so they cannot be carried over.
- A character-set introducer on a string literal inside a generated-column expression (`_latin1'x'`) is re-parsed and written back under the connection's character set (`_utf8mb4'x'`).
  The expression still means the same thing; only the text the catalog records for it changes.

For description sync and column-order sync, if the column no longer exists when the script runs, its statement folds into a no-op, and the run is still reported as successful, since nothing failed.
Altering a column fails when the column does not exist, because a selected change that silently does nothing would be wrong.
If the column does exist but its definition cannot be rebuilt, the statement fails instead, so a half-applied column order is never reported as a success.

Before running the script, the sync clears `NO_BACKSLASH_ESCAPES` from its own session's `sql_mode`; the global setting is not touched.
QuickER escapes the literals it writes under MySQL's default rule, and under that mode the same text would be stored with its backslashes doubled.

The live rebuild is one more reason MariaDB is out of scope.
It records `COLUMN_DEFAULT` with different semantics, so a definition rebuilt from its catalog cannot be guaranteed to reproduce the column.

### SQLite diff sync (table rebuilds)

Because SQLite's `ALTER TABLE` supports only limited changes, a table is rebuilt when the sync changes or drops one of its columns, changes its primary key, adds or drops a unique constraint or a foreign key, or reorders its columns.
Adding a column runs as `ALTER TABLE ADD COLUMN` when nothing else in the same sync rebuilds that table, and otherwise the new column becomes part of the rebuild.
A new table is created from the combined definition of "the database's current state + the selected differences," the data is migrated, the old table is swapped out, and auxiliary objects such as indexes are recreated.
The rebuild runs inside a transaction, and before committing it checks foreign-key integrity (`PRAGMA foreign_key_check`); if the sync added violations it rolls back and reports the violating tables.
SQLite does not check foreign keys by default, so databases that hold child rows without a parent are common, and violations that existed before the sync do not make it fail.
A view that refers to a rebuilt table does not stop the rebuild, and it keeps pointing at the rebuilt table (`PRAGMA legacy_alter_table` is turned on for the duration).
If the sync breaks a view, for example one that refers to a dropped column, it rolls back and names the view.

The rebuilt table is created from the diagram's definition, so anything outside [what the diagram models](#modeling-fidelity) is **not** reproduced and is silently lost.
That covers every element the table there marks as lost in a rebuild, which is exactly why the `DEFAULT` values you set outside QuickER disappear after a rebuild.
Implicit auto-numbering is also safe as long as the diagram's declared type is `INTEGER`, because such a primary key stays a rowid alias in the rebuilt table (a key declared `INT`, which is what a diagram created in QuickER or converted through another dialect holds, was never an alias to begin with; see [what the diagram models](#modeling-fidelity)).

To make the loss visible, QuickER reads the live `CREATE TABLE` statement of every table it is about to rebuild and lists the attributes it found in the execution-confirmation dialog.
If the table needs those attributes, re-apply them yourself after the sync, or run the change as hand-written SQL.
A table that depends on `WITHOUT ROWID` or `STRICT` comes back as an ordinary rowid, non-strict table, so do not sync it through a rebuild; apply the change as hand-written SQL instead.
`NOT DEFERRABLE` and `DEFERRABLE INITIALLY IMMEDIATE` check immediately, the same as the default, so losing them changes nothing and they are not listed.

The sync-target SQLite file can also be created fresh with the "Create new" button in the connection dialog.
Apply the whole diagram to an empty database to set it up from scratch.

## Dialect switching

The "Target DB:" combo on the right of the toolbar switches the diagram's target DBMS at any time.

- Each column type is converted automatically along the path "source dialect → neutral canonical type → new dialect" where a mapping exists
- Columns that could not be converted keep their original types and are listed in a warning dialog
- The switch and the type conversions are undone together with a single Undo

The columns in this table convert only to some dialects, or convert with a change noted in the cell.
A column marked ✅ is converted (a column already in the target's dialect stays as it is), and the others are listed as unconvertible.

<!-- dialect-table: unconvertible-types -->

| Column in the source diagram | SQL Server | PostgreSQL | MySQL | Oracle | SQLite |
| --- | :-: | :-: | :-: | :-: | :-: |
| Negative scale (PostgreSQL `numeric(10,-2)`, Oracle `NUMBER(10,-2)`) | — | ✅ | — | ✅ | — |
| String or binary without a length (PostgreSQL `varchar`, SQLite `VARCHAR` / `VARBINARY`) | — | ✅ | — | — | ✅ |
| `decimal` without a precision (Oracle `NUMBER`, PostgreSQL `numeric`) | — | ✅ | — | ✅ | ✅ |
| No precision limit with a nonzero scale (Oracle `NUMBER(*,2)`) | — | — | — | ✅ | ✅ |
| No precision limit with a scale of 0 (Oracle `INTEGER`, `SMALLINT`, `NUMBER(*,0)`) | ✅ (`decimal(38,0)`, up to 38 digits) | ✅ (`numeric(38,0)`, up to 38 digits) | ✅ (`decimal(38,0)`, up to 38 digits) | ✅ | ✅ |
| Row version (SQL Server `rowversion` / `timestamp`) | ✅ | — | — | — | ✅ (`BLOB`, NOT NULL lifted) |

- A negative scale rounds the value, to hundreds for `-2`.
  SQL Server and MySQL reject it outright, and SQLite's `DECIMAL` is an affinity that carries no rounding at all, so such a column is reported as unconvertible rather than written out as DDL that will not run
- MySQL and Oracle reject a `varchar` / `VARCHAR2` with no length, and SQL Server quietly creates a **one-character** column when the length is omitted.
  Give the column a length in the source diagram before switching
- A bare `decimal` means `decimal(18,0)` on SQL Server and `decimal(10,0)` on MySQL, a column that quietly rounds away the fractional part
- Oracle's `INTEGER` and `SMALLINT` are a `NUMBER(*,0)`, an integer with no precision limit.
  A dialect that cannot declare it without a precision gets a 38-digit decimal, and the confirmation lists the column, because a value of more than 38 digits no longer fits
- PostgreSQL's `numeric` cannot declare a scale without a precision, and a bare `numeric` would drop the scale

A length or precision beyond the target's limit is listed as unconvertible, because writing it out as is would make the DDL fail when applied.

<!-- dialect-table: target-limits -->

| Dialect | Strings and binaries | Decimal precision | Decimal scale | Fractional seconds |
| --- | --- | --- | --- | --- |
| SQL Server | `nvarchar` / `nchar` 4000, `varchar` / `char` / `binary` / `varbinary` 8000 | 38 | 0 to the precision | 7 |
| PostgreSQL | `varchar` / `char` 10,485,760 | 1000 | -1000 to 1000 | 6 |
| MySQL | `char` / `binary` 255, `varchar` 16,383, `varbinary` 65,532 | 65 | 0 to 30 | 6 |
| Oracle (`MAX_STRING_SIZE = STANDARD`) | `NVARCHAR2` 2000, `VARCHAR2` 4000, `NCHAR` 1000, `CHAR` / `RAW` 2000 | 38 | -84 to 127 | 9 |

- PostgreSQL accepts fractional seconds beyond 6 places (`timestamp(7)`) but rounds them to 6, so they are listed as well
- MySQL's `varchar` limit is that of `utf8mb4` (16,383 characters), and whether the whole row fits in 65,535 bytes is decided by MySQL when the DDL is applied
- Oracle's limits for strings and `RAW` are those of `MAX_STRING_SIZE = STANDARD`, the default.
  QuickER does not know the connected database's setting, so a column beyond these limits is listed as unconvertible even for a `MAX_STRING_SIZE = EXTENDED` database

A MySQL integer is converted by the range of values it holds, sign included.
A signed `tinyint` (-128 to 127) becomes a 16-bit integer, because the 8-bit integer the other dialects share is unsigned (0 to 255, like SQL Server's `tinyint`).
A `tinyint(1)` is read as a Boolean, since that is how MySQL usually declares one.

<!-- dialect-table: mysql-integers -->

| MySQL | Neutral type | SQL Server |
| --- | --- | --- |
| `tinyint unsigned` | 8-bit integer (unsigned) | `tinyint` |
| `tinyint` | 16-bit integer | `smallint` |
| `smallint unsigned` | 32-bit integer | `int` |
| `mediumint`, `mediumint unsigned` | 32-bit integer | `int` |
| `int unsigned` | 64-bit integer | `bigint` |
| `bigint unsigned` | `decimal(20,0)` | `decimal(20,0)` |
| `tinyint(1)` | Boolean | `bit` |

- A name that the target dialect would refuse (longer than its limit, or holding a character it cannot store) is not changed by the switch.
  The confirmation dialog lists such names, with their place and the reason, together with the columns above, because DDL generation and sync stop on them until they are changed.
  See [Identifier length limits](#identifier-length-limits)
- On SQLite, SQL Server's `rowversion` / `timestamp` loses its NOT NULL, because SQLite assigns nothing, so a locally created row has no version until a sync writes one.
  The conversion is one-way: converting that `BLOB` back to SQL Server yields `varbinary(max)`, not a row version.
  See [Multi-target repositories](code-generation.md#multi-target-repositories-sqlserver--sqlite) for what the column means on each side, and [Bidirectional sync support](code-generation.md#bidirectional-sync-support---generate-sync-support) for generating the code that keeps the two databases in step
- The mark of a column the database numbers (automatic numbering that refuses given values) is removed when the target dialect cannot hold such a column.
  The confirmation lists those columns, and the single Undo brings the marks back as well.
  See [Columns the database numbers](code-generation.md#columns-the-database-numbers)

## DDL generation

Choose SQL DDL from "Export" on the toolbar to output the full set of CREATE statements in the diagram's target dialect.
Table and column descriptions are included in the DDL as well.
The mechanism differs per dialect: sp_addextendedproperty on SQL Server, COMMENT ON for PostgreSQL and Oracle, COMMENT clauses on MySQL, and comment lines on SQLite.
For export operations in general, see [Import and export](import-export.md).

### What a data type may contain

A column's data type is written into the SQL exactly as the diagram holds it, because a type is neither an identifier nor a string literal and there is nowhere to quote it.
Both DDL generation and diff sync therefore accept only a plain type expression, and refuse the whole output when a column holds anything else, naming the `table.column` and the type text.
A type may contain:

- Words separated by single spaces, each made of letters, digits, `_`, and `$`: `int`, `double precision`, `LONG RAW`, `UNSIGNED BIG INT`
- Parenthesised arguments after any word. An argument is a number, a word (letters, digits, `_`), or `*`, one or two of them, optionally followed by Oracle's unit keyword.
  Examples: `nvarchar(max)`, `decimal(10,2)`, `decimal(10,-2)`, `NUMBER(*,2)`, `VARCHAR2(50 CHAR)`, `TIMESTAMP(6) WITH LOCAL TIME ZONE`, `INTERVAL DAY(2) TO SECOND(6)`, `decimal(10,2) unsigned`, and PostGIS's `geometry(Point,4326)` / `geography(MultiPolygon)`
- A trailing `[]` for a PostgreSQL array, repeatable for more dimensions: `integer[]`, `integer[][]`
- A MySQL value list: `enum('a','b')`, `set('x','y')`

A type may be at most 200 characters long.
Only a MySQL value list has no length limit, because an `enum` with many values can exceed 200 characters and is a real declaration.

Everything the five dialect catalogues offer, and everything schema import builds from a live database, is inside this.
What falls outside it:

- A type carrying `;`, a quote, a backtick, a backslash, a comment marker (`--`, `/*`, or MySQL's `#`), or a line break.
  None of them is needed by any dialect, and each would change the statement rather than the column: MySQL's `#` comments out the rest of the line and would silently swallow the `NOT NULL`, `COMMENT` or `AFTER` that follows a type in `MODIFY COLUMN`
- A type with leading or trailing whitespace.
  What is validated has to be exactly what is written out, and trimming first breaks that: `"\nGO\n"` reads as the single word `GO` once trimmed, but written out it can trip SQL Server's batch splitting.
  An empty or whitespace-only type still means "not set" and passes as before
- A type whose second or later word is a column clause keyword: `NOT`, `NULL`, `DEFAULT`, `PRIMARY`, `KEY`, `REFERENCES`, `CHECK`, `GENERATED`, `IDENTITY`, `COLLATE`, `UNIQUE`, `ON`, `AS`, `AUTO_INCREMENT`, `AUTOINCREMENT`, `INVISIBLE`, `VISIBLE`, `CONSTRAINT`.
  A type is interpolated into the middle of a column definition, so `int NOT NULL` or `varchar(50) COLLATE Latin1_General_BIN` produces perfectly valid DDL whose nullability, default or collation disagrees with what the diagram declares, and no syntax error tells you.
  The same goes for `int AS (price)` (the short form of a generated column) or `int AUTO_INCREMENT`: a column the diagram says is neither computed nor auto-numbered becomes one in the database, and the generated code's INSERT fails at run time.
  None of these words appears in any real type notation the catalogues or schema import produce

Two known exclusions are worth naming because a database can produce them.
PostgreSQL's one-byte `"char"` type (quoted, which the SQL it would land in cannot carry), and the placeholder `user-defined` that import falls back to when it cannot resolve a type name.
Both would fail as DDL anyway; you now find out at generation time with the column named.

One more edge comes from SQLite, which accepts any words as a declared type and hands them back verbatim on import.
A SQLite database created with SQL Server-flavoured DDL can carry a declaration like `INT IDENTITY(1,1)`, whose second word is on the clause-keyword list above and is refused, again by name, at generation time.

This is separate from whether QuickER *understands* a type.
A type the dialect catalogue cannot parse is still carried as it is; it simply gets no neutral type token and does not convert on a dialect switch.

### Line breaks and control characters in names

A table, column or constraint name holding a line break or a control character is refused by DDL generation and by schema-sync script generation, which name the place (`TABLE 'x'`, `COLUMN 'x'.'y'`) and produce nothing.
The set is the C0 controls, DEL, and the three Unicode line separators U+0085, U+2028 and U+2029.
C# code generation reports the same names as a pre-generation error.

Quote characters are deliberately fine: `"`, `'`, `]` and backticks occur in real databases and are escaped where they are written.
Descriptions are not refused either, because a line break in one is legitimate and is folded to a space where it lands in a `--` comment line.

### Identifier length limits

Each dialect caps how long a table, column or constraint name may be, and the unit it counts in differs.

<!-- dialect-table: identifier-length-limits -->

| Dialect | Limit | Counted in | Characters it cannot store | A longer name is |
| --- | --- | --- | --- | --- |
| SQL Server | 128 | UTF-16 code units | — | Rejected |
| PostgreSQL | 63 | UTF-8 bytes | — | Silently cut |
| MySQL | 64 (a table name is also limited to 251 bytes as a file name, see below) | characters | Outside the Basic Multilingual Plane (replaced with `?`) | Rejected |
| Oracle | 128 (12.2 or later) | UTF-8 bytes | — | Rejected |
| SQLite | none | | — | |

- **Names QuickER assigns** (`PK_{table}`, `FK_{child}_{parent}`, `UQ_{table}_{columns}` and the numbered names that keep them unique) are kept within the limit by the target dialect.
  A name already within the limit is unchanged.
  A longer one is cut and ends with `_` and 8 hex digits taken from the full name, so two names that differ only after the cut stay different.
  The result is the same every time
- **Names you type** are never shortened.
  DDL generation and schema-sync script generation refuse a name beyond the limit, naming the place and the limit (`TABLE 'x' is longer than the limit of 63 UTF-8 bytes`), and produce nothing.
  Without this check the DDL either fails when it is applied or, where the table above shows the name is silently cut, the table you import back has a different name.
  Two names that differ only beyond the cut would even collapse into one
- **The editor and the AI tools warn when you enter such a name.**
  The status bar shows a message when you confirm a table name, column name, foreign key name or unique constraint name that the target dialect would refuse, and the AI and MCP tools add a note to their response.
  The name is accepted as typed, so you can fix it later.
  Undo and Redo, and loading a file, do not warn
- **MySQL cannot store a character outside the Basic Multilingual Plane** (emoji and some rare kanji).
  QuickER treats it the same as a name that is too long: the warning above, and refusal at generation
- **A MySQL table name is limited a second time, as a file name.**
  InnoDB stores each table in its own file named after the table, with every character other than ASCII letters, digits and `_` written as an escape (3 bytes for letters such as `Ä`, 5 bytes for most others, including all kanji).
  The file name may be at most 251 bytes, so a table name made of kanji can be at most 50 characters although 64 are allowed for a column.
  This applies when `innodb_file_per_table` is on (the default); QuickER always applies it
- **Oracle before 12.2** (a database whose `COMPATIBLE` is below 12.2) limits names to 30 bytes.
  QuickER does not know the connected database's setting and checks against 128 bytes, so shorten names yourself for such a database

#### Databases created by earlier versions

A PostgreSQL foreign key whose default name was longer than 63 bytes was created by earlier versions under the cut name, because PostgreSQL silently cuts it.
Diff sync treats such a foreign key (one without a name of your own) as the same as the one in your diagram, so nothing is added or dropped.

## Related pages

- [Tutorial (from design to running code)](getting-started.md)
- [Import and export](import-export.md)
- [CLI reference](cli.md): `quicker scaffold` (generating code directly from a DB)
