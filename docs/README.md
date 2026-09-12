# QuickER documentation

*English | [日本語](README.ja.md)*

The order below is a suggested first read: the two documents under "Start here" cover what QuickER is for and what one full cycle feels like, and the rest is reference material to reach for when you need it.

## Start here

- [Why QuickER uses the ER model as the source of truth](overview.md) — the reasoning behind the tool: what goes wrong when the same schema is restated in DDL, entity classes and design documents; how this differs from code-first; and where AI fits in. Background reading, no procedures.
- [Tutorial (from design to running code)](getting-started.md) — one full loop with the bundled EC order sample: edit the diagram, write the DDL, generate the code, run the application. It uses a SQLite file, so no database server is needed.

## Working with diagrams

- [ER diagram editing](er-editor.md) — the editor itself: entities and columns, relationships, multi-select and bulk edits, undo/redo, switching the target DBMS, file operations and auto-save, keyboard shortcuts.
- [Database round-tripping](database.md) — importing the schema of a live database, syncing the differences back to it, and generating DDL, across the five supported dialects. Includes how faithfully each dialect is modeled.
- [Import and export](import-export.md) — the other formats (DBML, Mermaid, Excel and HTML definition documents, images, generated C#): which directions each one supports, and what it carries or drops.

## Generating code

- [Using the generated code](code-generation.md) — the reference for the generated C#: entities, edit models, repositories, value objects, named queries, EF Core mode, remote services, bidirectional sync, and the options that turn each one on. By far the largest document; search it for the feature you are using rather than reading it end to end.
- [CLI reference (quicker)](cli.md) — `quicker generate`, `scaffold`, `reverse` and `mcp`, their options, and the keys of the `quicker.json` settings file.

## AI integration

- [Configuring AI chat](ai-chat.md) — setting up the in-app chat that edits the diagram you have open (four connection tabs, six methods in total), and AI mock generation, which turns the diagram into mockup screens.
- [MCP server (quicker mcp)](mcp.md) — running QuickER as a stdio MCP server so an external agent (Claude Code, Codex, and so on) can edit diagrams and generate code as part of its own workflow.

## Elsewhere in the repository

- [README](../README.md) — what QuickER is, with screenshots.
- [The EC order sample](../samples/ec-order/README.md) and [the three-tier sample](../samples/ec-order-remote/README.md) — projects you can build and run.
- [Changelog](../CHANGELOG.md), [contributing](../CONTRIBUTING.md), [licensing](../LICENSING.md), [security policy](../SECURITY.md).
