# Database round-tripping

*English | [日本語](database.ja.md)*

QuickER imports the schema of a live database into a diagram, detects the differences between the diagram and the database and applies a sync script, and generates DDL from the diagram.
Because the diagram and the database round-trip in both directions, running an import and a diff sync lets you see how far the diagram and the live database have drifted apart, and close the gap.

## Supported DBMS

| DBMS | Schema import | Diff sync | DDL generation | Dialect switch | Notes |
|---|:-:|:-:|:-:|:-:|---|
| SQL Server | ✅ | ✅ | ✅ | ✅ | 2016 and later. Descriptions sync with extended properties (MS_Description) |
| PostgreSQL | ✅ | ✅ | ✅ | ✅ | 13 and later. Descriptions sync with `COMMENT ON` |
| MySQL | ✅ | ✅ | ✅ | ✅ | 8.0 and later (MariaDB is not supported). Descriptions sync with `COMMENT` clauses |
| Oracle | ✅ | ✅ | ✅ | ✅ | 19c and later. Descriptions sync with `COMMENT ON` |
| SQLite | ✅ | ✅ | ✅ | ✅ | File DB. Diff sync that involves column changes, drops, or FK changes uses table rebuilds. No description mechanism |

Import and sync against live databases have real-DB integration tests (SQL Server / PostgreSQL / MySQL / Oracle use Testcontainers containers; SQLite uses a real file DB).
The container-based tests run only in environments where Docker is available and are skipped automatically on CI.
Each DBMS is also verified against one representative version rather than across the whole supported range in the table.

## Schema import (DB → diagram)

The "DB Import" button on the toolbar opens the "Import from Database" connection dialog.

### Specifying the connection

- **Server-type DBMS**: specify the target DB, host, port (leave empty for the dialect's default), database name, user name, and password.
  Only an empty port means the default; a value that is not a number or is outside 1 to 65535 is rejected by OK, *Test Connection*, and saving a profile (a typo never connects to another instance on the default port).
  For SQL Server you can also choose the authentication mode (Windows / SQL Server) and the "Trust the server certificate (TrustServerCertificate)" checkbox.
  For Oracle, the value of the database-name field is used as the service name (the connection is built as EZConnect `host:port/service`)
- **Encryption (SSL Mode)**: for PostgreSQL and MySQL, how strongly the connection requires TLS.
  It defaults to "Unspecified" mode, which leaves the keyword off the connection string and the decision to the driver (see the note below).
  It is saved per connection profile, and profiles written before this setting existed load as "Unspecified"
- **SQLite**: specify the file path via "Browse" (import works on existing files only)
- **Command timeout**: how long a single schema-import or sync statement may run.
  It defaults to 60 seconds and applies to every dialect including SQLite; `0` means no limit (the ADO.NET convention).
  A blank field, a non-numeric entry, or a negative value is rejected by OK, *Test Connection*, and saving a profile, so the dialog never falls back to the default behind your back.
  It is saved per connection profile, and profiles written before this setting existed load with the default
- **Test Connection**: runs a real schema fetch and reports the number of tables detected.
  OK is disabled while a test is running: the test cannot be cancelled, so confirming and closing would leave it holding the connection on its own

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
If the file turns out to be corrupted, it is moved aside once to `connections.json.corrupt` before being overwritten.

### What gets imported

- Tables (views, materialized views, external tables, partitions, temporary tables, and the container and log tables behind materialized views are excluded) and columns (type, length, precision, nullability)
- Primary keys, preserving a composite key's own column order.
  `PRIMARY KEY (b, a)` imports as exactly that, and generated DDL emits the constraint's declared order rather than the order the columns happen to appear in the table, so the column order of the backing index survives the round trip
- Foreign keys (including the constraint name and the ON DELETE / ON UPDATE referential actions).
  When the FK columns on the referencing side themselves form that table's primary key or a unique constraint, the relationship is classified as one-to-one.
  A foreign key made up of multiple columns is imported with every column pair, in declaration order.
  SQLite does not persist FK constraint names, so a constraint name is synthesized on import
- Table and column descriptions (SQL Server's MS_Description extended properties, PostgreSQL's `obj_description` / `col_description`, MySQL's `TABLE_COMMENT` / `COLUMN_COMMENT`, and Oracle's `user_tab_comments` / `user_col_comments`).
  SQLite has no description mechanism and is out of scope

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
A dot in a SQLite table name is part of the name rather than a schema qualifier, because the import only ever looks at the single connected file.

Constraints that are disabled are not imported either (`DISABLE` on Oracle, `NOCHECK CONSTRAINT` on SQL Server): a disabled constraint enforces nothing, so importing it would make the diagram claim a guarantee the database does not provide.
A SQL Server foreign key added `WITH NOCHECK` but currently enabled *is* imported, because it is enforced from then on.

#### Import warnings

The import always succeeds, but some parts of a schema cannot be carried into the diagram exactly as they were declared.
When that happens the completion dialog lists what was affected (`quicker scaffold` writes the same list to standard error), so the difference is visible instead of silent.
The list has one heading per kind with the count, followed by one line per place.

- A PostgreSQL **domain type** column is imported as its base type.
  The diagram has no notion of domains, so the domain's `CHECK` constraint is lost
- A SQL Server **alias type** column (`CREATE TYPE … FROM`) is likewise imported as its base type
- A column's **default value** is not imported, and each such column is named with its default (all five DBMSs).
  The diagram has no notion of defaults, so generated DDL creates the column without `DEFAULT`.
  A default of `NULL` means the same as having no default and is not reported
- **Automatic numbering that also accepts given values** is not imported: MySQL `AUTO_INCREMENT`, PostgreSQL `serial` and `GENERATED BY DEFAULT AS IDENTITY`, Oracle `GENERATED BY DEFAULT AS IDENTITY` and SQLite `AUTOINCREMENT`.
  The column is named once as automatic numbering, not again as a default value
- On MySQL, a column's own **collation** (when it differs from the table default), **`INVISIBLE`**, **`ON UPDATE`** and **`SRID`** are not imported, and each column is named
- A **name with spaces around it** (a table, column, foreign key or unique constraint name) is imported without the spaces, and each name is reported.
  MySQL does not allow trailing spaces, so only leading ones occur there
- A **foreign key that points outside the imported scope** is skipped (on SQLite this covers a key whose parent table does not exist)
- A **disabled constraint** (`DISABLE` on Oracle, a foreign key turned off with `NOCHECK CONSTRAINT` on SQL Server) is not imported
- A table whose **columns could not be read at all** is imported without columns
- One of two tables whose **names differ only in letter case or surrounding spaces** is skipped, because the diagram cannot keep them apart (PostgreSQL and Oracle allow both through quoted identifiers, MySQL does when `lower_case_table_names` is 0, SQL Server does in a database with a case-sensitive collation, and SQLite, which folds only ASCII letters, does for names such as `Äpfel` and `äpfel`)
- Likewise, one of two **columns of a table whose names differ only in letter case or surrounding spaces** is skipped (the later one in declaration order), and the key flags, descriptions and foreign keys of the skipped column do not attach to the one that is kept
- A **computed or generated column** (a SQL Server computed column, `GENERATED ALWAYS AS` on MySQL / PostgreSQL / SQLite, an Oracle virtual column) is imported as a read-only column without its expression.
  The diagram does not model expressions, so generated DDL creates an ordinary column (with a comment asking for the expression to be added by hand).
  Generated code leaves the column out of INSERT / UPDATE (see [the code generation notes](code-generation.md#computed--generated-columns))
- A column with **automatic numbering that refuses given values** (SQL Server `IDENTITY`, PostgreSQL and Oracle `GENERATED ALWAYS AS IDENTITY`) is imported with a mark, as a column the database numbers, without its numbering definition (seed and increment), and is named column by column.
  The diagram does not model the numbering definition, so generated DDL creates an ordinary column (with a comment asking for the identity property to be added by hand).
  Generated code leaves such a column out of INSERT / UPDATE unless it is part of the primary key (see [the code generation notes](code-generation.md#columns-the-database-numbers))
- A SQLite **virtual table** (FTS5, R*Tree and the like) is not imported.
  It cannot be represented as an ordinary table; its shadow tables are excluded along with it, and each virtual table is reported once
- **Objects the diagram does not hold** are left out and listed by name.
  These are views (all five DBMSs), materialized views (PostgreSQL, Oracle), external tables (PostgreSQL foreign tables, Oracle `ORGANIZATION EXTERNAL` tables, SQL Server external tables), partitions (PostgreSQL, with the parent table each belongs to; the parent itself is imported as described below) and temporary tables (Oracle).
  Objects the database or an extension owns (system tables, the container and log tables behind materialized views, tables an extension installs) are left out without a mention
- A **foreign key that references a table the import left out** (a PostgreSQL partition, an SQLite virtual table or view) is skipped and reported
- A SQL Server **temporal table** is imported as its current-data table only.
  The history table is an attachment the system maintains for it rather than an entity of its own.
  The current table's period columns (`GENERATED ALWAYS AS ROW START / END`) are values the database produces, so they are treated as computed columns, and a `HIDDEN` period column is imported the same way.
  Generated code leaves those columns out of its writes, so inserts and updates against the current table work as they are (see [computed columns](code-generation.md#computed--generated-columns))
- A column whose **data type cannot be written into SQL safely** is named, along with the type text.
  The import keeps it, but DDL generation and schema sync refuse the *whole diagram* until it is corrected, because a type is neither an identifier nor a string literal and there is nowhere to quote it (see [What a data type may contain](#what-a-data-type-may-contain)).
  SQLite stores whatever type text a table was declared with, and PostgreSQL reports a type name that needs quoting with the quotes attached, so either can produce one
- A **partitioned table** (SQL Server, PostgreSQL, MySQL, Oracle) is imported as its parent, and the partitioning itself is not modelled.
  Generated DDL creates the table unpartitioned
- A SQLite **primary key that allows NULL** is imported as NOT NULL, and the adjusted columns are named.
  In a rowid table, any primary key other than a single column declared exactly `INTEGER` can hold NULL, while the diagram and the editor always treat a primary key as NOT NULL.
  Only the columns actually adjusted are named: a key whose columns are already declared `NOT NULL` is not reported.
  If existing rows carry a NULL primary key, a sync from this diagram (which rebuilds the table) can fail while copying the data across

When nothing is affected, the dialog is the usual one-line completion message.

#### How imported type names are spelled

The import reads each column's declared type from the database's own catalog and then writes it in QuickER's shorter vocabulary.
PostgreSQL's canonical `character varying(50)` is written as `varchar(50)`, and `timestamp(3) with time zone` as `timestamptz(3)`.
Only the spelling changes; the type does not.

On PostgreSQL this also fixes type names that used to come back wrong, because the previous source of type information could not express certain modifiers.
A column declared `numeric(10,-2)` was imported as `numeric(10,2046)`, a spelling PostgreSQL itself rejects, so the diagram could not even generate DDL for it.
`bit(8)` and `bit varying(16)` lost their length, `interval day to second(3)` lost its unit, and `time(3) with time zone` lost its precision.
An array came back as an internal name such as `_int4`, or, for `varchar(20)[]`, as `_varchar`, dropping the length.
These columns now carry their real declared type.

**If you import a PostgreSQL database into a diagram created by an older version, expect a one-time diff.**
Any column of the kinds above shows up once as a column change in the diff sync, because the diagram holds the old broken spelling and the database reports the correct one.
Healthy columns are unaffected: their spelling is unchanged.

On SQL Server the import also reads the fractional-second precision of `datetime2` / `time` / `datetimeoffset`.
The default precision of 7 is written without parentheses as `datetime2`, and any other precision is written out, as in `datetime2(3)`.
Earlier versions did not read the precision and imported a `datetime2(3)` column as `datetime2`.
Syncing from such a diagram could change the column's precision to 7.

**In a diagram imported from SQL Server by an earlier version, columns whose fractional-second precision is not 7 show up as a type change in the next sync.**
Import the diagram again, or change the types in the diagram to match the database's declarations.

On Oracle, a `NUMBER` with no precision and a scale of 0 (a column declared `INTEGER`, `SMALLINT` or `NUMBER(*,0)`) is imported as `NUMBER(*,0)`.
Earlier versions imported it as a bare `NUMBER`, so DDL generated from that diagram made a column that accepts fractions.
In a diagram imported from Oracle by an earlier version, such columns show up as a type change in the next sync.

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

Everything else is outside the model.
It is not imported, it never appears in generated DDL, and the diff sync neither detects nor touches it.

| Schema element | On import | In generated DDL | In a SQLite table rebuild |
|---|---|---|---|
| `DEFAULT` | Not imported | Not emitted | Lost |
| `CHECK` constraint | Not imported | Not emitted | Lost |
| `COLLATE` | Not imported | Not emitted | Lost |
| Generated / computed column | Imported as an ordinary column; the expression is lost | Emitted as an ordinary column | Lost |
| `IDENTITY` / `AUTO_INCREMENT` / `AUTOINCREMENT` | Automatic numbering that refuses given values (SQL Server `IDENTITY`, PostgreSQL and Oracle `GENERATED ALWAYS AS IDENTITY`) is imported as the mark of a column the database numbers (the numbering definition is lost); the rest is not imported | Not emitted (a marked column gets a note) | Lost (a primary key whose declared type is `INTEGER` stays a rowid alias, so implicit auto-numbering survives; see the note below) |
| Non-unique index | Not imported | Not emitted | Preserved (captured as its `CREATE` statement and replayed) |
| Trigger | Not imported | Not emitted | Preserved (same) |
| View, stored procedure, function, sequence | Not imported | Not emitted | Not applicable |

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
- Setting, updating, and removing table and column descriptions (SQLite is out of scope)
- Changes to the column order (syncable on SQLite / MySQL)

Elements with the same name are treated as the same element, so a rename is detected as "drop + add."
A table or column whose name differs from the database's only in letter case is the same element too, as long as it is unique on both sides ignoring case, and the script then writes the database's spelling.
This matters where quoted names are case-sensitive: Oracle stores a table created without quotes as `ORDERS` and PostgreSQL as `orders`, so a diagram's `Orders` would otherwise name a table that does not exist.
A table or column that exists only in the diagram is created with the diagram's spelling.
A foreign key of an unnamed relationship is matched by the same default name the DDL gives it (`FK_{child table}_{parent table}`).

Column types are compared after bringing both sides to the spelling the database stores.
A database does not always remember a declaration as written: a column declared `numeric(18,0)` comes back as `numeric(18)`.
Two spellings of the same type, such as `numeric(18,0)` and `numeric(18)` or `datetime2(7)` and `datetime2`, are therefore not a difference.
PostgreSQL's `timestamp` and `timestamp(6)` or `numeric(18,0)` and `numeric(18)`, MySQL's `decimal(12)` and `decimal(12,0)`, and Oracle's `TIMESTAMP` and `TIMESTAMP(6)` or `INTEGER` and `NUMBER(*,0)`, are not a difference for the same reason.
Only omitted defaults and aliases the database does not remember are aligned; types that differ in meaning (`datetime2(3)` and `datetime2`) are still a difference.

On PostgreSQL, a type change that crosses type families (`varchar` to `integer`, `text` to `date`, `integer` to `boolean` and the like) is written with `USING column::new_type`, so it goes through when every value converts.
A change to a string type is written without it, so a value too long for a shorter `varchar(n)` makes the sync fail instead of being cut off.
A type the import cannot classify (PostGIS types, user-defined types) is also written without it.

Oracle foreign keys have no `ON UPDATE` and no `ON DELETE SET DEFAULT`, so those settings in the diagram are compared as `NO ACTION`.
The DDL and the sync script create the foreign key without the clause and add a comment saying so.
MySQL (InnoDB) accepts `SET DEFAULT` in `ON DELETE` and `ON UPDATE` but does not apply it: deleting the parent row fails, as with `NO ACTION`.
It is handled the same way: compared as `NO ACTION`, and left out of the DDL and the sync script with a comment.
SQL Server cannot create a `SET DEFAULT` foreign key when a NOT NULL referencing column has no DEFAULT constraint (error 1762).
The diagram does not carry column defaults, so the DDL adds a comment before such a foreign key, and the sync names the foreign keys it adds or recreates in the confirmation dialog.
It does not stop the sync, because the foreign key is created when the database column has a DEFAULT constraint.
Recreating a foreign key is a single diff item that combines the drop and the add, and it is unselected by default (it drops the foreign key and adds it back under the same name, so on MySQL / Oracle a failed re-add can leave the foreign key missing).
Recreating a unique constraint works the same way: when a unique constraint keeps its name (or the generated name `UQ_{table}_{columns}` matches the database's name) but its columns change, the drop and the add become one diff item, unselected by default.
Adding a constraint under the name the database already uses would fail, and re-adding it can fail on duplicate values.
On every dialect, a composite foreign key whose column pairs differ only in their order is not a difference, because the pairs map parent columns to child columns and their order carries no meaning (Oracle does not even keep the declared order and returns the pairs in the order of the referenced key).
SQL Server and MySQL require the referencing columns to follow the order of the referenced key, so the DDL writes the pairs in the order of the referenced key (the primary key, or else a unique constraint).
A foreign key the sync adds or recreates is written in the diagram's order, so on SQL Server and MySQL it fails when that order differs from the referenced key (the confirmation dialog shows it).
SQLite reads a table name with a dot such as `a.b` as table b in an attached database a, so the DDL and the sync for that table fail unless that database is attached.
The DDL adds a comment before the table, and the sync lists such tables in the confirmation dialog.

Column order is compared as the relative order of the columns common to both sides, excluding additions and deletions.
Adding a column in the middle is therefore not a column-order difference by itself: the new column goes to the end of the table, and the next sync shows the order difference.
When existing columns are reordered at the same time, a single sync also puts the new column in place.
On SQLite and MySQL it can be synced as a selectable diff item (unselected by default): SQLite folds it into the table rebuild, and MySQL uses `ALTER TABLE ... MODIFY ... AFTER`, moving as few columns as possible.
MySQL's `MODIFY` re-specifies the column from the live catalog, so moving a column keeps its attributes and its comment (see [what MySQL's `MODIFY COLUMN` re-specifies](#what-mysqls-modify-column-re-specifies)).
On the other three dialects it is shown for information only and is not synced.

A computed column's nullability is never compared, because the database derives it from the expression and the diagram does not hold the expression.
A computed column's type change can be selected only on PostgreSQL, where `ALTER COLUMN ... TYPE` keeps the expression, and selecting it never changes the column's nullability.
On the other dialects it is shown for information only, because the type cannot be changed without the expression (SQL Server rejects the change; MySQL turns a `STORED` generated column into an ordinary column without the expression and rejects the change for a `VIRTUAL` one; a SQLite rebuild would drop the expression).
On SQLite and Oracle, a unique constraint on exactly the primary key's columns is not compared and does not become a diff item.
SQLite does not keep such a constraint, and Oracle rejects it; the primary key already guarantees uniqueness.
The DDL and the tables the sync creates leave that unique constraint out and add a comment saying so.
On Oracle, a sync that moves the primary key onto the columns of a unique constraint the database already has fails with ORA-02261.
Remove that unique constraint from the diagram and select its drop as well, or drop it in the database first.

### Safety by design

- **Destructive differences (drops and type changes) are unselected by default**, and executing them shows a confirmation stating that destructive changes are included
- The generated script is ordered by dependency (add tables → add columns → drop FKs → drop old primary keys → alter columns → add new primary keys → drop columns → drop tables → add FKs → descriptions), with a heading comment per section
- A foreign-key addition whose FK column cannot be resolved is emitted as a skip comment instead of invalid DDL
- Dropping a table or a column also drops every foreign key that loses its table or column, even when dropping that foreign key is not selected (on SQLite the surviving child table is rebuilt without it).
  This also lets a parent table and its child be dropped together whatever order they are listed in
- A selected change that needs a change you did not select (a new table or column, or a primary key change) is left out of the script, and the confirmation names it together with what it needs.
  A foreign key you add or change needs the primary key change when it refers to the parent's new primary key, and that key is neither the primary key nor a unique constraint without the change (also when the primary key change is left out on MySQL because only its column order changes).
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

Foreign keys that rely on the changed primary key (they refer to the same columns as the old key; on MySQL also to its leading columns) are dropped and re-created automatically around the change, while foreign keys that refer to a unique constraint are left alone (on SQL Server and MySQL, the same drop and re-create applies when altering a column that participates in a foreign key).
On MySQL, a foreign key of the table whose primary key changes is also dropped and re-created when its columns lead the old primary key, because the primary key cannot be dropped while it is the only index supporting that foreign key.
The same applies when dropping a unique constraint whose index is the only one supporting a foreign key (MySQL refuses with error 1553 otherwise).
On SQL Server, altering the type of a primary key column also drops the primary key and adds it back with the same columns (the re-added key is named `PK_{table}`).
When a change leaves the two ends of a foreign key with different types after the sync (altering the type of only one side, for example), the foreign key is not dropped and re-added, and the column change fails as it is (the database stays unchanged).
Re-adding it would always fail and, on MySQL / Oracle, leave the foreign key missing.
On every dialect but SQL Server, string types that differ only in length (`varchar(50)` and `varchar(100)`, for example) are not a mismatch, because PostgreSQL, MySQL, Oracle and SQLite accept them on the two ends of a foreign key; on Oracle the length unit (`CHAR` / `BYTE`) must be the same on both ends.
Changing the length of one end drops and re-adds the foreign key as usual.
SQL Server cannot create a foreign key whose ends differ in length (error 1753), so the DDL adds a comment before such a foreign key.
The confirmation dialog lists those foreign keys so that you can change both ends to the same type.
In that case no other foreign key is dropped and re-added either, because the column change fails anyway and the re-adding would never run.
If a referenced column is no longer part of the new key, the confirmation dialog warns that re-creating those foreign keys may fail.
On SQL Server and PostgreSQL such a failure rolls the whole script back, while on MySQL / Oracle it can leave the foreign keys dropped (partially applied).
The same warning appears when a foreign key you add or change refers to columns that are not exactly a primary key or a unique constraint after the sync.
SQL Server and MySQL also require the referencing columns to follow the order of the referenced key, so a change that only reorders a referenced composite key, or a foreign key change that only reorders its columns against the key, is warned about too.
On MySQL such a change is not run at all: it would drop the foreign key and then fail to add it back, leaving it dropped.
The confirmation dialog lists the skipped changes; to apply one, also select the removal of the foreign key so that it is dropped first, or change it by hand.
This also skips the change when the re-add would in fact succeed: on MySQL 8.0, or on 8.4 with `restrict_fk_on_non_standard_key=OFF`, a non-unique index you created yourself on the referenced columns in the foreign key's order can support it, but QuickER does not import non-unique indexes and cannot tell.

### Description sync

Table and column descriptions sync on the four dialects that have a description mechanism.
SQL Server uses MS_Description extended properties, PostgreSQL and Oracle use `COMMENT ON`, and MySQL uses `COMMENT` clauses.
Checking for an existing description to choose between add and update applies to SQL Server's extended properties only; PostgreSQL and Oracle simply re-run `COMMENT ON`, and MySQL re-runs the `COMMENT` clause (both idempotent in themselves).
Clearing a description in the diagram removes it.
SQLite has no description mechanism, so descriptions are neither diffed nor synced there.

On SQL Server, a table name without a schema refers to the connecting login's default schema, as it does in `CREATE TABLE`, and its description goes to the same table (the schema is taken from `SCHEMA_NAME()`).
The import writes tables outside `dbo` with their schema (`app.Widget`), so when your login's default schema is not `dbo`, write the schema into the diagram's table names as well; otherwise the next diff sees `Widget` and `app.Widget` as two different tables.

#### What MySQL's `MODIFY COLUMN` re-specifies

MySQL has no statement that changes only a column's comment, and none that changes only its position.
Both go through `ALTER TABLE ... MODIFY COLUMN`, which re-states the **whole** column definition: whatever the new definition leaves out is dropped from the column.
MySQL commits DDL implicitly, so there is no rolling that back.

QuickER therefore takes the definition from two different places, depending on what the change is about.

- **Description sync and column-order sync** rebuild the definition from `information_schema.COLUMNS` at execution time and replace only the trailing clause they exist to change: the comment, or `AFTER` / `FIRST`.
  Everything the diagram does not model is carried over untouched: `DEFAULT` (literal and expression, `BIT` and binary literals included), `AUTO_INCREMENT`, the collation and with it the character set, `ON UPDATE CURRENT_TIMESTAMP`, `INVISIBLE`, a spatial `SRID`, and generated (`VIRTUAL` / `STORED`) columns.
  Column-order sync preserves the column's existing comment as well.
- **Altering a column** (a type or nullability change) re-specifies from the diagram, because the diagram is what that change expresses.
  It therefore **does** drop the attributes listed above, none of which the diagram can represent.
  The comment alone is re-stated from the live database and kept (changing it is the job of the description sync item).
  Such diffs are unselected by default.
- **Changing the primary key** removes `AUTO_INCREMENT` from an old primary key column before dropping the key, because MySQL refuses to drop it otherwise (error 1075).
  `AUTO_INCREMENT` is not added back because the diagram cannot represent it (the comment, default, and other attributes are carried over from the live definition).
- **Dropping a foreign key** also drops a non-unique index with the same name as the foreign key, unless another foreign key needs it.
  MySQL creates such an index for a foreign key that has no suitable index and keeps it after the foreign key is dropped, so re-creating a foreign key of the same name on other columns would fail (error 1061).
  An index you created yourself with the same name as the foreign key is dropped too, because the catalog cannot tell the two apart.
  A unique index is never dropped this way.
  When two foreign keys that start with the same column are both dropped, the index they share can be left behind, because the other foreign key still uses it when the first one is dropped.
  Re-creating a foreign key with that index's name on other columns can then fail with error 1061.

Two things do not come through the rebuild, both because the catalog does not record them the way they were written:

- `COLUMN_FORMAT` and `STORAGE` (NDB Cluster) have no `information_schema` column to read them from, so they cannot be carried over.
- A character-set introducer on a string literal inside a generated-column expression (`_latin1'x'`) is re-parsed and written back under the connection's character set (`_utf8mb4'x'`).
  The expression still means the same thing; only the text the catalog records for it changes.

If the column no longer exists when the script runs, its statement folds into a no-op, and the run is still reported as successful, since nothing failed.
If the column does exist but its definition cannot be rebuilt, the statement fails instead, so a half-applied column order is never reported as a success.

Before running the script, the sync clears `NO_BACKSLASH_ESCAPES` from its own session's `sql_mode`; the global setting is not touched.
QuickER escapes the literals it writes under MySQL's default rule, and under that mode the same text would be stored with its backslashes doubled.

The live rebuild is one more reason MariaDB is out of scope.
It records `COLUMN_DEFAULT` with different semantics, so a definition rebuilt from its catalog cannot be guaranteed to reproduce the column.

### SQLite diff sync (table rebuilds)

Because SQLite's `ALTER TABLE` supports only limited changes, sync operations that involve column changes or deletions use a table-rebuild approach.
A new table is created from the combined definition of "the database's current state + the selected differences," the data is migrated, the old table is swapped out, and auxiliary objects such as indexes are recreated.
The rebuild runs inside a transaction, and before committing it checks foreign-key integrity (`PRAGMA foreign_key_check`); if the sync added violations it rolls back and reports the violating tables.
SQLite does not check foreign keys by default, so databases that hold child rows without a parent are common, and violations that existed before the sync do not make it fail.
A view that refers to a rebuilt table does not stop the rebuild, and it keeps pointing at the rebuilt table (`PRAGMA legacy_alter_table` is turned on for the duration).
If the sync breaks a view, for example one that refers to a dropped column, it rolls back and names the view.

The rebuilt table is created from the diagram's definition, so anything outside [what the diagram models](#modeling-fidelity) is **not** reproduced and is silently lost.
That covers `AUTOINCREMENT`, `DEFAULT`, `CHECK`, `COLLATE`, and generated columns, which is exactly why the `DEFAULT` values you set outside QuickER disappear after a rebuild.
Conflict clauses on constraints (`ON CONFLICT`), deferred foreign keys (`DEFERRABLE INITIALLY DEFERRED`), and the table options after the closing parenthesis of the column list (`WITHOUT ROWID` and `STRICT`) are lost the same way.
Indexes and triggers are safe: they are captured as their full `CREATE` statements and replayed.
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
- A `numeric(10,-2)` / `NUMBER(10,-2)` column (a scale that rounds to hundreds) converts only between PostgreSQL and Oracle.
  SQL Server and MySQL reject a negative scale outright, and SQLite's `DECIMAL` is an affinity that carries no rounding at all, so such a column is reported as unconvertible on those three rather than written out as DDL that will not run
- A string or binary column declared without a length (PostgreSQL's `varchar`, SQLite's `TEXT` and the like) cannot move to SQL Server, MySQL or Oracle and is listed as well.
  MySQL and Oracle reject a `varchar` / `VARCHAR2` with no length, and SQL Server quietly creates a **one-character** column when the length is omitted.
  Give the column a length in the source diagram before switching
- A length or precision beyond the target's limit (SQL Server's `nvarchar(5000)`, `decimal(50,2)`, `datetime2(9)`, Oracle's `NVARCHAR2(4000)`, `NUMBER(39)`, MySQL's `datetime(7)`, `decimal(66,2)` and the like) is listed as unconvertible.
  Writing it out as is would make the DDL fail when applied.
  Oracle's limits for strings and `RAW` are those of `MAX_STRING_SIZE = STANDARD`, the default (`NVARCHAR2` 2000, `VARCHAR2` 4000, `NCHAR` 1000, `CHAR` and `RAW` 2000).
  QuickER does not know the connected database's setting, so a column beyond these limits is listed as unconvertible even for a `MAX_STRING_SIZE = EXTENDED` database
- A `decimal` without a precision (Oracle's `NUMBER`, PostgreSQL's unconstrained `numeric`) cannot move to SQL Server or MySQL and is listed as well.
  A bare `decimal` means `decimal(18,0)` on SQL Server and `decimal(10,0)` on MySQL, a column that quietly rounds away the fractional part
- Oracle's `NUMBER(*,2)` (no precision limit, a scale of 2) can be written out only for Oracle and is listed for every other dialect except SQLite.
  PostgreSQL's `numeric` cannot declare a scale without a precision, and a bare `numeric` would drop the rounding to two places
- A MySQL integer is converted by the range of values it holds, sign included.
  `int unsigned` becomes a 64-bit integer (`bigint` on SQL Server), `smallint unsigned` a 32-bit one, and `bigint unsigned` becomes `decimal(20,0)`.
  A signed `tinyint` (-128 to 127) becomes a 16-bit integer, because the 8-bit integer the other dialects share is unsigned (0 to 255, like SQL Server's `tinyint`)
- A name that the target dialect would refuse (longer than its limit, or holding a character it cannot store) is not changed by the switch.
  The confirmation dialog lists such names, with their place and the reason, together with the columns above, because DDL generation and sync stop on them until they are changed.
  See [Identifier length limits](#identifier-length-limits)
- SQL Server's `rowversion` / `timestamp` becomes a plain `BLOB` on SQLite, and its NOT NULL is lifted at the same time (SQLite assigns nothing, so a locally created row has no version until a sync writes one).
  The conversion is one-way: converting that `BLOB` back to SQL Server yields `varbinary(max)`, not a row version.
  See [Multi-target repositories](code-generation.md#multi-target-repositories-sqlserver--sqlite) for what the column means on each side, and [Bidirectional sync support](code-generation.md#bidirectional-sync-support---generate-sync-support) for generating the code that keeps the two databases in step.
  Other dialects have no equivalent at all, so a `rowversion` column is reported as unconvertible there

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

### Line breaks and control characters in names

A table, column or constraint name holding a line break or a control character is refused by DDL generation and by schema-sync script generation, which name the place (`TABLE 'x'`, `COLUMN 'x'.'y'`) and produce nothing.
The set is the C0 controls, DEL, and the three Unicode line separators U+0085, U+2028 and U+2029.
C# code generation reports the same names as a pre-generation error.

Quote characters are deliberately fine: `"`, `'`, `]` and backticks occur in real databases and are escaped where they are written.
Descriptions are not refused either, because a line break in one is legitimate and is folded to a space where it lands in a `--` comment line.

### Identifier length limits

Each dialect caps how long a table, column or constraint name may be, and the unit it counts in differs.

| Dialect | Limit | Counted in |
| --- | --- | --- |
| SQL Server | 128 | UTF-16 code units |
| PostgreSQL | 63 | UTF-8 bytes |
| MySQL | 64 | characters (a table name is also limited to 251 bytes as a file name, see below) |
| Oracle | 128 | UTF-8 bytes (12.2 or later) |
| SQLite | none | |

- **Names QuickER assigns** (`PK_{table}`, `FK_{child}_{parent}`, `UQ_{table}_{columns}` and the numbered names that keep them unique) are kept within the limit by the target dialect.
  A name already within the limit is unchanged.
  A longer one is cut and ends with `_` and 8 hex digits taken from the full name, so two names that differ only after the cut stay different.
  The result is the same every time
- **Names you type** are never shortened.
  DDL generation and schema-sync script generation refuse a name beyond the limit, naming the place and the limit (`TABLE 'x' is longer than the limit of 63 UTF-8 bytes`), and produce nothing.
  Without this check SQL Server, MySQL and Oracle reject the DDL when it is applied, while PostgreSQL silently cuts the name at 63 bytes and the table you import back has a different name.
  Two names that differ only after the 63rd byte would even collapse into one
- **The editor and the AI tools warn when you enter such a name.**
  The status bar shows a message when you confirm a table name, column name, foreign key name or unique constraint name that the target dialect would refuse, and the AI and MCP tools add a note to their response.
  The name is accepted as typed, so you can fix it later.
  Undo and Redo, and loading a file, do not warn
- **MySQL cannot store a character outside the Basic Multilingual Plane** (emoji and some rare kanji).
  MySQL does not refuse such a name; it silently replaces the character with `?`.
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

This is separate from whether QuickER *understands* a type.
A type the dialect catalogue cannot parse is still carried as it is; it simply gets no neutral type token and does not convert on a dialect switch.

## Related pages

- [Tutorial (from design to running code)](getting-started.md)
- [Import and export](import-export.md)
- [CLI reference](cli.md): `quicker scaffold` (generating code directly from a DB)
