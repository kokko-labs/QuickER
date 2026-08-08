# Import and Export

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
- Unique constraints: the `unique` column setting (a constraint over that one column) and `unique` indexes in an `Indexes` block (`(col, …) [unique, name: '…']` — composite and named constraints). Indexes that are not `unique` are skipped
- Relationships: `-` (one-to-one), `<` (one-to-many), and `<>` (many-to-many) on `Ref:` lines. `>` (many-to-one) is not supported. An endpoint can be a single column (`Parent.a`) or the composite Ref syntax (`Parent.(a, b)`), which restores a composite foreign key with its pairs in order. A line whose two endpoints list a different number of columns, or that names a column the table does not have, keeps the relationship but drops its column mapping (it can be completed in the properties panel)
- Not supported: `Project` / `Enum` / `TableGroup` / multi-line `Note` blocks

Tables with no columns get a default PK column (`ID int`). DBML carries no dialect information, so the diagram's target DB stays as it was before the import.

### Mermaid (.mmd / .mermaid)

Imports the `erDiagram` notation. Like DBML it carries no dialect information, so the target DB is kept. A `UK` key marker is imported as a unique constraint over that single column (Mermaid has no syntax for grouping several columns, so composite unique constraints cannot be expressed).

### Excel definition documents (.xlsx)

Definition documents exported by QuickER can be re-imported. In the key column, `UQ{n}` marks unique constraints and the same number means the same constraint, so the columns sharing a number are restored as one constraint (the constraint name is not carried by the document, so it is left unset and synthesized at DDL generation time). Sheet roles are identified by hidden definition tags, so the sheets can be renamed or translated and still import — but **Excel files created by other applications cannot be imported directly** (to migrate definition documents you already have, transcribe them into QuickER's document format once). The target DBMS is embedded in the document and restored, dialect and all, on import. Count mismatches, duplicates, and references to undefined tables are import errors.

### C# code (.cs)

From the "Import Code" button on the code-generation toolbar, choose a main `.g.cs` that QuickER generated **with `IncludeDataAnnotations` ON**; the diagram is recovered using Roslyn syntax analysis only (no compilation, no assembly loading) and merge-imported into the current diagram. Only classes that carry `[Table]` **and** have at least one column property carrying `[DbColumnMeta]` are considered; infrastructure classes such as Repository and hand-written POCOs are ignored (when no eligible class is found, an error suggests choosing the main generated `.g.cs`). The table name comes from `[Table]` and the column name from `[Column]`, the column type and description from `[DbColumnMeta]`, the table description from `[DbTableMeta]`, the primary key from `[Key]`, the nullability from whether the property type is `?` (`[Required]` is not used at all), the UNIQUE constraints from the class-level `[UniqueConstraint]`, and the relationships from `[NavigationReference]`. A column property without `[DbColumnMeta]` is skipped with a warning.

**UNIQUE constraints and FK metadata round-trip.** A constraint is restored from `[UniqueConstraint("PropA", "PropB", Name = "UQ_...")]` by mapping each property name back to its `[Column]`; the member order is the declaration order and a missing `Name` means "synthesize at DDL generation time". A constraint that refers to a property which could not be restored (or that declares no member) is skipped **as a whole** — never narrowed into a different constraint — and reported as a warning. The FK constraint name and the referential actions are restored from the named arguments `ConstraintName` / `OnDelete` / `OnUpdate` of `[NavigationReference]`; they are only written out when they differ from the defaults, and an unrecognized action token is warned about and treated as unspecified.

Column types are expanded from the dialect-neutral tokens into the current diagram's native types (tokens that cannot be expanded are adopted as-is with a warning). **Because the code is dialect-neutral, the diagram's target DB stays as it was before the import.** Many-to-many relationships do not exist in code, so a merge import into an existing diagram preserves the many-to-many relationships whose two endpoints both survive (ordinary relationships that disappeared from the code disappear from the diagram too). For FK metadata the merge is **fallback-only**: for relationships whose endpoints (table and column names on both ends) match, only the fields the code did not specify are filled in from the current diagram — code that does specify a value wins. This also covers code generated by an older version, which has no named arguments at all: every field counts as unspecified and the current diagram's values are preserved. **UNIQUE constraints, by contrast, are owned by the code** — nothing is preserved from the current diagram, because an absent attribute cannot be told apart from "this table has no constraint", and preserving it would make a constraint you deleted in code impossible to remove. As with the other imports, a replacement confirmation appears when there are structural differences or named queries that would break.

The CLI's `quicker reverse` writes a fresh, merge-free diagram (schema-only JSON) using the same analysis (see the [CLI reference](cli.md)).

## Export

Choose a format from the "Export" button.

### SQL DDL (.sql)

Outputs the full set of CREATE statements in the diagram's target dialect.

### DBML / Mermaid

Writes out the text formats. The DBML output is the same subset as the import above (`Table` blocks + `Ref:` lines), and the written file can be re-imported with the relationships' column mapping intact — including composite foreign keys, which are written with DBML's composite Ref syntax (`Ref: Parent.(a, b) < Child.(x, y)`; single-column foreign keys keep the plain `Parent.a < Child.x` form). Unique constraints are written as the `unique` column setting for unnamed single-column constraints, and as an `Indexes` block (`(col, …) [unique, name: '…']`) for composite and named ones. Mermaid's key column holds a single marker per column, so it is folded to `PK` > `FK` > `UK`, and **`UK` is written only for the columns of single-column constraints** (splitting a composite constraint per column would come back on import as N separate single-column constraints — a different meaning — so composite ones are not written). Useful for working with DBML tools such as dbdiagram.org, and with GitHub and documentation tools that render Mermaid.

### Excel definition documents (.xlsx)

Outputs a definition document consisting of a table list, a relationship list, and per-table detail sheets (column types, required flags, keys, descriptions). Besides `PK` and `FK{n}`, the key column shows unique constraints as `UQ{n}` (combined as in `PK/UQ1` or `FK1/UQ2`). `n` follows the order the constraints appear in, and **the same number means the same constraint**, so a composite constraint — and likewise a composite foreign key — puts the same number on every column it covers. The relationship list keeps one row per relationship: a composite foreign key lists its columns comma-separated in the referencing and referenced column cells, in declaration order (`TenantRef, RegionRef` / `TenantId, RegionCode`), and re-importing splits them back into column pairs (both sides must list the same number of columns). The target DBMS is embedded in the file, so this document can be turned back into a diagram by re-importing it into QuickER.

### HTML definition documents (.html)

Outputs a self-contained single HTML file with no external references and no JavaScript. It consists of a sidebar navigation, an overview (target DBMS, table count, relationship count), a table list, a relationship list (including ON DELETE / ON UPDATE), and per-table details. It can be viewed with nothing but a browser, which makes it a good handout for non-developer stakeholders.

Fix the diagram and re-export the documents, and they are up to date again — build the re-export into your workflow and the state where "only the documentation is stale" becomes much easier to avoid.

### Schema JSON (.json)

Outputs a JSON (`{ "Version": 1, "Schema": { ... } }` — the keys start with an uppercase letter and are case-sensitive) containing only the schema definition and the named-query definitions — the save format (see [ER diagram editing](er-editor.md)) minus the layout information (coordinates, colors, and so on). With no layout, the diff stays stable, which suits reviewing and versioning the table definitions themselves. The file can be loaded with "Open" in QuickER; having no layout, the whole diagram is auto-arranged (the schema and the named-query definitions round-trip, but the coordinates and colors are not restored). Use it, separately from a normal save (a `.json` with layout), when you want to share just the schema without layout churn.

### PNG / SVG (images)

Outputs the whole diagram as an image. Neither format is affected by the on-screen zoom level. SVG is drawn directly from the diagram's model, so it contains no selection frames or grid background; PNG rasterizes the canvas as it is drawn, so the selection state (selection frames and the dimming from relationship highlighting) and the grid background are captured as well. Clearing the selection before exporting a PNG is recommended.

### Print / PDF

"Print" on the toolbar (Ctrl+P) opens the print options dialog.

- **Diagram Title** — the title printed in the header
- **Print size** — "Scale to fit one page" (default) / "Print at actual size" (match the paper size to the diagram's real size; good for PDF output)
- **Print the timestamp in the header** (on by default)

Choose a PDF printer such as Microsoft Print to PDF and the print becomes a PDF export.

## Related pages

- [Database round-tripping](database.md) — importing from live DBs and diff sync
- [ER diagram editing](er-editor.md) — the save format (git-manageable JSON)
