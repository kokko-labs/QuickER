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

Choosing a file from the "Import" button replaces the current diagram with the imported contents (with a confirmation before it runs). Auto-arrange is applied after the import.

### DBML (.dbml)

Imports DBML consisting of `Table` blocks and `Ref:` lines. The supported range is the subset that round-trips with QuickER's DBML export.

- Column settings: `pk` / `ref` / `null` / `not null` / `note: '...'`
- Relationships: `-` (one-to-one), `<` (one-to-many), and `<>` (many-to-many) on `Ref:` lines. `>` (many-to-one) is not supported
- Not supported: `Project` / `Enum` / `Indexes` / `TableGroup` / multi-line `Note` blocks

Tables with no columns get a default PK column (`ID int`). DBML carries no dialect information, so the diagram's target DB stays as it was before the import.

### Mermaid (.mmd / .mermaid)

Imports the `erDiagram` notation. Like DBML it carries no dialect information, so the target DB is kept.

### Excel definition documents (.xlsx)

Definition documents exported by QuickER can be re-imported. Sheet roles are identified by hidden definition tags, so the sheets can be renamed or translated and still import — but **Excel files created by other applications cannot be imported directly** (to migrate definition documents you already have, transcribe them into QuickER's document format once). The target DBMS is embedded in the document and restored, dialect and all, on import. Count mismatches, duplicates, and references to undefined tables are import errors.

### C# code (.cs)

From the "Import Code" button on the code-generation toolbar, choose a main `.g.cs` that QuickER generated **with `IncludeDataAnnotations` ON**; the diagram is recovered using Roslyn syntax analysis only (no compilation, no assembly loading) and merge-imported into the current diagram. Only classes carrying the `[Table]` / `[Column]` / `[Key]` / `[Required]` / `[DbColumnMeta]` / `[DbTableMeta]` / `[NavigationReference]` attributes are considered; infrastructure classes such as repositories and hand-written POCOs are ignored (when no eligible class is found, an error suggests choosing the main generated `.g.cs`).

Column types are expanded from the dialect-neutral tokens into the current diagram's native types (tokens that cannot be expanded are adopted as-is with a warning). **Because the code is dialect-neutral, the diagram's target DB stays as it was before the import.** Many-to-many relationships, `ON DELETE` / `ON UPDATE`, and FK constraint names do not exist in code, so a merge import into an existing diagram preserves, from the current diagram, the referential actions and constraint names of relationships whose endpoints (table and column names on both ends) match, as well as many-to-many relationships whose both ends survive (ordinary relationships that disappeared from the code disappear from the diagram too). As with the other imports, a replacement confirmation appears when there are structural differences or named queries that would break.

The CLI's `quicker reverse` writes a fresh, merge-free diagram (schema-only JSON) using the same analysis (see the [CLI reference](cli.md)).

## Export

Choose a format from the "Export" button.

### SQL DDL (.sql)

Outputs the full set of CREATE statements in the diagram's target dialect.

### DBML / Mermaid

Writes out the text formats. The DBML output is the same subset as the import above (`Table` blocks + `Ref:` lines), and the written file re-imports as-is. Useful for working with DBML tools such as dbdiagram.org, and with GitHub and documentation tools that render Mermaid.

### Excel definition documents (.xlsx)

Outputs a definition document consisting of a table list, a relationship list, and per-table detail sheets (column types, required flags, keys, descriptions). The target DBMS is embedded in the file, so this document can be turned back into a diagram by re-importing it into QuickER.

### HTML definition documents (.html)

Outputs a self-contained single HTML file with no external references and no JavaScript. It consists of a sidebar navigation, an overview (target DBMS, table count, relationship count), a table list, a relationship list (including ON DELETE / ON UPDATE), and per-table details. It can be viewed with nothing but a browser, which makes it a good handout for non-developer stakeholders.

Fix the diagram and re-export the documents, and they are up to date again — avoiding the state where "only the documentation is stale."

### Schema JSON (.json)

Outputs a JSON (`{ version, schema }`) containing only the schema definition and the named-query definitions — the save format (see [ER diagram editing](er-editor.md)) minus the layout information (coordinates, colors, and so on). With no layout, the diff stays stable, which suits reviewing and versioning the table definitions themselves. The file can be loaded with "Open" in QuickER; having no layout, the whole diagram is auto-arranged (so the format is reversible). Use it, separately from a normal save (a `.json` with layout), when you want to share just the schema without layout churn.

### PNG / SVG (images)

Outputs the whole diagram as an image, unaffected by the on-screen zoom state or selection display.

### Print / PDF

"Print" on the toolbar (Ctrl+P) opens the print options dialog.

- **Diagram Title** — the title printed in the header
- **Print size** — "Scale to fit one page" (default) / "Print at actual size" (match the paper size to the diagram's real size; good for PDF output)
- **Print the timestamp in the header** (on by default)

Choose a PDF printer such as Microsoft Print to PDF and the print becomes a PDF export.

## Related pages

- [Database round-tripping](database.md) — importing from live DBs and diff sync
- [ER diagram editing](er-editor.md) — the save format (git-manageable JSON)
