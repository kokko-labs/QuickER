# Import and export

*English | [日本語](import-export.ja.md)*

Diagram input and output run from the "Import" and "Export" buttons on the toolbar (importing from a live database is a separate button, "DB Import" — see [Database round-tripping](database.md)).

| Format | Import | Export |
|---|:-:|:-:|
| Live databases (5 dialects) | ✅ | — (written back via diff sync / DDL) |
| C# code (generated `.g.cs`) | ✅ | — (written out by code generation) |
| SQL DDL | — | ✅ |
| DBML | ✅ | ✅ |
| Mermaid (erDiagram) | ✅ | ✅ |
| Excel definition documents | ✅ | ✅ |
| HTML definition documents | — | ✅ |
| Schema JSON (no layout) | — | ✅ |
| PNG / SVG | — | ✅ |
| Print / PDF | — | ✅ |

## Import

Choosing a file from the "Import" button brings the contents into the diagram. DBML and Mermaid replace the whole diagram and auto-arrange it afterwards; **Excel definition documents are merge-imported** — matching tables and columns take over the identity of the current diagram's elements, so the named-query definitions and hand-placed layout are preserved and no auto-arrange runs. The confirmation before a replacement is not unconditional: when the current diagram is empty, or its structure is identical to what is being imported, the import continues without asking.

### DBML (.dbml)

Imports DBML consisting of `Table` blocks and `Ref:` lines. The supported syntax is the subset that QuickER's DBML export writes, and both the syntax and the relationships' column mapping round-trip: the column names written on a `Ref:` line are taken as they are, so a relationship comes back connecting exactly the columns the file names.

- Column settings: `pk` / `ref` / `unique` / `null` / `not null` / `note: '...'`
- Table description: a `Note: '...'` line inside the `Table` block (the standard DBML form, so descriptions are restored from files written by other tools too)
- Unique constraints: the `unique` column setting (a constraint over that one column) and `unique` indexes in an `Indexes` block (`(col, …) [unique, name: '…']` — composite and named constraints). Indexes that are not `unique` are skipped
- Relationships: `-` (one-to-one), `<` (one-to-many), and `<>` (many-to-many) on `Ref:` lines. `>` (many-to-one) is not supported. An endpoint can be a single column (`Parent.a`) or the composite Ref syntax (`Parent.(a, b)`), which restores a composite foreign key with its pairs in order. A line whose two endpoints list a different number of columns, or that names a column the table does not have, keeps the relationship but drops its column mapping (it can be completed in the properties panel)
- Constraint name and referential actions: restored from the settings block right after `Ref:` (`[note: 'FK name', delete: cascade, update: set null]`). DBML's `restrict` has no counterpart in QuickER, so it is imported as `NO ACTION`
- Not supported: `Project` / `Enum` / `TableGroup` / multi-line `Note` blocks

Tables with no columns get a default PK column (`ID int`). DBML carries no dialect information, so the diagram's target DB stays as it was before the import.

### Mermaid (.mmd / .mermaid)

Imports the `erDiagram` notation. Like DBML it carries no dialect information, so the target DB is kept. A `UK` key marker is imported as a unique constraint over that single column (Mermaid has no syntax for grouping several columns, so composite unique constraints cannot be expressed).

### Excel definition documents (.xlsx)

Definition documents exported by QuickER can be re-imported. In the key column, `UQ{n}` marks unique constraints and the same number means the same constraint, so the columns sharing a number are restored as one constraint (the constraint name is not carried by the document, so it is left unset and synthesized at DDL generation time). Sheet roles are identified by hidden definition tags, so the sheets can be renamed or translated and still import — but **Excel files created by other applications cannot be imported directly** (to migrate definition documents you already have, transcribe them into QuickER's document format once). The target DBMS is embedded in the document and restored, dialect and all, on import. Count mismatches, duplicates, and references to undefined tables are import errors. The import has no size limit of its own: every worksheet other than the two role sheets is read as a table-detail sheet, and rows are scanned until the first blank one. It also runs on the UI thread, so a very large workbook leaves the window unresponsive until it finishes, with nothing to cancel it.

### C# code (.cs)

From the "Import Code" button on the code-generation toolbar, choose the `.g.cs` that holds the generated entities (`Entities.g.cs` with split or layered output, the single output file otherwise), generated **with `IncludeDataAnnotations` ON**; the diagram is recovered using Roslyn syntax analysis only (no compilation, no assembly loading) and merge-imported into the current diagram. Only classes that carry `[Table]` are considered; infrastructure classes such as Repository, EditModel, and Mapper never carry it, and hand-written POCOs are ignored (when no such class is found, an error suggests choosing the `.g.cs` that holds the entities). The table name comes from `[Table]` and the column name from `[Column]`, the column type and description from `[DbColumnMeta]`, the table description from `[DbTableMeta]`, the primary key from `[Key]`, the nullability from whether the property type is `?` (`[Required]` is not used at all), the UNIQUE constraints from the class-level `[UniqueConstraint]`, and the relationships from `[NavigationReference]`.

**Everything that is lost or changed is reported as a warning**, listed after the import completes. A column property without `[DbColumnMeta]` is skipped — with a dedicated warning when it is the primary key, because the table then comes back without one. A table whose columns could none of them be restored is imported **with no columns** rather than dropped, so that the relationships touching it do not disappear along with it. A relationship whose endpoint column cannot be resolved is restored **without a column pair** (it then takes no part in DDL generation or schema diffs) and is named in a warning, as is a relationship whose endpoint table is not in the file at all, and a `[NavigationReference]` whose endpoint arguments cannot be read (missing, not string literals, or `name:` arguments in another order). Duplicate table names and duplicate column names are accepted as they are and reported. When one class is split into several `partial` declarations **inside the same file**, only the part carrying `[Table]` is read, and the number of column properties left behind in the other parts is reported — move them into the part that carries `[Table]`.

**UNIQUE constraints and FK metadata round-trip.** A constraint is restored from `[UniqueConstraint("PropA", "PropB", Name = "UQ_...")]` by mapping each property name back to its `[Column]`; the member order is the declaration order and a missing `Name` means "synthesize at DDL generation time". A constraint that refers to a property which could not be restored (or that declares no member) is skipped **as a whole** — never narrowed into a different constraint — and reported as a warning. The FK constraint name and the referential actions are restored from the named arguments `ConstraintName` / `OnDelete` / `OnUpdate` of `[NavigationReference]`; they are only written out when they differ from the defaults, and an unrecognized action token is warned about and treated as unspecified.

Column types are expanded from the dialect-neutral tokens into the current diagram's native types (tokens that cannot be expanded are adopted as-is with a warning). A token carries the meaning of a type only, so several spellings of one meaning (`numeric` and `decimal`, `ntext` and `nvarchar(max)`, `datetime` and `datetime2`) collapse onto a single representative. For the columns where that would change the spelling, code generation therefore records the original text in `[DbColumnMeta]` as well (`NativeType`), and names those columns in an information diagnostic; the import adopts the recorded text whenever parsing it in the current dialect yields the same type as the token, so **a `datetime` column comes back as `datetime`** instead of turning into `datetime2` and making the next database sync emit an `ALTER COLUMN`. When the two disagree — the file was hand-edited, or it was generated for another dialect — the token wins and a warning says so. **Because the code is dialect-neutral, the diagram's target DB stays as it was before the import.** Many-to-many relationships do not exist in code, so a merge import into an existing diagram preserves the many-to-many relationships whose two endpoints both survive (ordinary relationships that disappeared from the code disappear from the diagram too). For FK metadata the merge is **fallback-only**: for relationships whose endpoints (table and column names on both ends) match, only the fields the code did not specify are filled in from the current diagram — code that does specify a value wins. This also covers code generated by an older version, which has no named arguments at all: every field counts as unspecified and the current diagram's values are preserved. **UNIQUE constraints, by contrast, are owned by the code** — nothing is preserved from the current diagram, because an absent attribute cannot be told apart from "this table has no constraint", and preserving it would make a constraint you deleted in code impossible to remove. As with the other imports, a replacement confirmation appears when there are structural differences or named queries that would break.

**What code cannot say.** A composite primary key's own column order is not written into the code: `[Key]` marks each key column where the property is declared (= the diagram's column order), so a diagram whose composite key order differs from its column order comes back with the key in column order. A description that spans several lines is folded into single spaces when the code is generated (a `///` summary and an attribute literal cannot carry the line breaks), so the import restores the folded text and the original layout of the description is gone. The FK constraint name and the referential actions are written out only when they differ from the defaults, so an unnamed constraint with `NO ACTION` on both sides is indistinguishable from "not stated": a merge import keeps whatever the current diagram holds for those fields, and the CLI reverse leaves them at their defaults. Entities declared as `record` are not analysed (generated entities are always `class`), and the `Schema` argument of `[Table]` is not read (generation does not write it; a schema-qualified table name lives in the table name itself).

The CLI's `quicker reverse` writes a fresh, merge-free diagram (schema-only JSON) using the same analysis, and creates the parent directory of `--out` when it is missing (see the [CLI reference](cli.md)).

## Export

Choose a format from the "Export" button.

### SQL DDL (.sql)

Outputs the full set of CREATE statements in the diagram's target dialect.

### DBML / Mermaid

Writes out the text formats. The DBML output is the same subset as the import above (`Table` blocks + `Ref:` lines), and the written file can be re-imported with the relationships' column mapping intact — including composite foreign keys, which are written with DBML's composite Ref syntax (`Ref: Parent.(a, b) < Child.(x, y)`; single-column foreign keys keep the plain `Parent.a < Child.x` form). Unique constraints are written as the `unique` column setting for unnamed single-column constraints, and as an `Indexes` block (`(col, …) [unique, name: '…']`) for composite and named ones. Mermaid's key column holds a single marker per column, so it is folded to `PK` > `FK` > `UK`, and **`UK` is written only for the columns of single-column constraints** (splitting a composite constraint per column would come back on import as N separate single-column constraints — a different meaning — so composite ones are not written). Useful for working with DBML tools such as dbdiagram.org, and with GitHub and documentation tools that render Mermaid. Neither format states a composite primary key's own column order — each key column simply carries its `pk` / `PK` marker — so a key whose order differs from the table's column order comes back in column order on re-import.

DBML also writes table descriptions as `Note:` lines and foreign key referential actions in the settings block of the `Ref:` line (`delete` / `update`; the default `NO ACTION` is not written), so those round-trip as well.

Information the chosen format cannot represent is listed in the completion dialog (once per format per session, so it does not become noise). Mermaid cannot express descriptions, memos, nullability, composite unique constraints, unique constraint names, foreign key column mappings, referential actions, or named queries, so it reports whichever of those the diagram actually contains. For DBML only table memos and named queries are reported (`Note` is used for the description, so carrying a memo would need a non-standard extension — interoperability with other tools was given priority). SQL DDL and the definition documents report nothing, because the only things they drop are the ones that format is not meant to carry.

### Excel definition documents (.xlsx)

Outputs a definition document consisting of a table list, a relationship list, and per-table detail sheets (column types, required flags, keys, descriptions). Besides `PK` and `FK{n}`, the key column shows unique constraints as `UQ{n}` (combined as in `PK/UQ1` or `FK1/UQ2`). `PK` carries no ordinal, so a composite primary key's own column order is not carried by the document either — re-importing restores the key in column order. `n` follows the order the constraints appear in, and **the same number means the same constraint**, so a composite constraint — and likewise a composite foreign key — puts the same number on every column it covers. The relationship list keeps one row per relationship: a composite foreign key lists its columns comma-separated in the referencing and referenced column cells, in declaration order (`TenantRef, RegionRef` / `TenantId, RegionCode`), and re-importing splits them back into column pairs (both sides must list the same number of columns). The target DBMS is embedded in the file, so this document can be turned back into a diagram by re-importing it into QuickER.

Every cell is written as a text value — QuickER never writes a formula — so a table or column name that begins with `=` stays literal text in the workbook, and Excel does not evaluate it on open. QuickER has no CSV output at all; if you convert a document to CSV yourself, remember that a spreadsheet opening a CSV *does* read a leading `=`, `+`, `-` or `@` as the start of a formula, so quote such values as you convert.

### HTML definition documents (.html)

Outputs a self-contained single HTML file with no external references and no JavaScript. It consists of a sidebar navigation, an overview (target DBMS, table count, relationship count), a table list, a relationship list (including ON DELETE / ON UPDATE), and per-table details. It can be viewed with nothing but a browser, which makes it a good handout for non-developer stakeholders.

Fix the diagram and re-export the documents, and they are up to date again — build the re-export into your workflow and the state where "only the documentation is stale" becomes much easier to avoid.

### Schema JSON (.json)

Outputs a JSON (`{ "Version": 1, "Schema": { ... } }` — the keys start with an uppercase letter and are case-sensitive) containing only the schema definition and the named-query definitions — the save format (see [ER diagram editing](er-editor.md)) minus the layout information (coordinates, colors, and so on). With no layout, the diff stays stable, which suits reviewing and versioning the table definitions themselves. The file can be loaded with "Open" in QuickER; having no layout, the whole diagram is auto-arranged (the schema and the named-query definitions round-trip, but the coordinates and colors are not restored). Use it, separately from a normal save (a `.json` with layout), when you want to share just the schema without layout churn.

### PNG / SVG (images)

Outputs the whole diagram as an image. Neither format is affected by the on-screen zoom level. SVG is drawn directly from the diagram's model, so it contains no selection frames or grid background; PNG rasterizes the canvas as it is drawn, so the selection state (selection frames and the dimming from relationship highlighting) and the grid background are captured as well. Clearing the selection before exporting a PNG is recommended.

Every name and description the SVG carries is XML-escaped, so nothing written in the diagram can break out of the markup. A control character in a name is the one exception: it is written through as it stands, and XML forbids those in content, so the file is written without complaint and then fails to open in every SVG viewer. Nothing rejects such a name on the way out — the entry check for control characters in names guards DDL and code generation, not the exports — so keep them out of the names themselves.

### Print / PDF

"Print" on the toolbar (Ctrl+P) opens the print options dialog.

- **Diagram Title** — the title printed in the header
- **Print size** — "Scale to fit one page" (default) / "Print at actual size" (match the paper size to the diagram's real size; good for PDF output)
- **Print the timestamp in the header** (on by default)

Choose a PDF printer such as Microsoft Print to PDF and the print becomes a PDF export.

## Related pages

- [Database round-tripping](database.md) — importing from live DBs and diff sync
- [ER diagram editing](er-editor.md) — the save format (git-manageable JSON)
