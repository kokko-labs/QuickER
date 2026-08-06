# ER Diagram Editing

*English | [日本語](er-editor.ja.md)*

QuickER's main window has three panes. On the left is the "Toolbox" (creating entities and relationships), in the middle the canvas, and on the right the "Properties" panel (editing the selected element); the boundary between the middle and the right can be resized by dragging. Elements are edited not through dialogs but directly in the properties panel after selecting them.

## Entities

### Creating and deleting

"Add Entity" in the toolbox adds a new table to the canvas (created as `NewTable` with a PK column `ID`, and selected). Delete with the "Delete Entity" button or the Delete key. With multiple entities selected, this becomes a bulk delete, and the connected relationships are deleted together.

Entities move by dragging. Copy and paste with Ctrl+C / Ctrl+V, and duplicate with Ctrl+D (or "Duplicate" on the toolbar).

### Editing properties

Select an entity and the properties panel lets you edit the following.

| Item | Contents |
|---|---|
| Table Name | The physical name of the table |
| Description | The table description. On SQL Server it syncs with the extended property (MS_Description) |
| Title Background | The card's title color (Blue / Green / Yellow / Purple / Pink / Gray) |
| Columns | The column list (next section) |
| Notes | A free-form field that goes neither to the database nor to the generated code. It is written out to the Excel and HTML definition documents, and re-importing an Excel definition document restores it |

### Editing columns

Columns are edited inline in the "Columns" grid of the properties panel.

- **Add / delete** — add a column with the "+" at the top right of the grid (created with the current target DB's default type); delete with the "−" or the delete button at the end of a row
- **Editable items** — the PK / FK / NULL checkboxes, the column name, the type (choose from the target DB's type list, or type freely), and the description
- **Reordering** — drag rows to change the order
- **Copy / paste** — Ctrl+C / Ctrl+V work per column on the grid

Express a composite primary key by checking PK on multiple columns.

### Unique constraints

Below the column grid, the properties panel has a "Unique Constraints" card per table. The card body is collapsible: it opens automatically when the selected table has constraints (or when you add one) and stays collapsed otherwise, with a toggle in the header. Add a constraint with "+", then build it up one column row at a time: the "+" under the constraint appends an empty row, and picking a column from its drop-down commits it. One column makes that column unique, several make the combination unique, and the row order is the declaration order. A drop-down only offers columns the other rows of the same constraint do not already use, so the same column cannot be picked twice; "×" takes a row back out. Leave the name empty and `UQ_{table}_{columns}` is synthesized at DDL generation time — the name box shows that synthesized name as its placeholder while it is empty; a name imported from a database is kept as it is. Deleting a column deletes the constraints that include it, together with the column (a constraint is never silently narrowed to its remaining columns), and every operation is undoable.

On the canvas, a column that belongs to a constraint is marked `UQ` in the key column. One column can be a primary key, a foreign key, and part of a unique constraint at once, so the marker is folded to a single one in the order `PK` > `FK` > `UQ`.

## Relationships

### Creating

Press "One-to-One," "One-to-Many," or "Many-to-Many" in the toolbox to enter creation mode, then click the two entities in turn to commit it. Clicking the same entity twice creates a self-referencing relationship. On creation, the source PK column and the target FK column are matched automatically, and the constraint name is generated in the form `FK_<target>_<source>`.

If a relationship with the same start and end points already exists, the new one is rejected as a duplicate — select and edit the existing relationship instead. Only the direction that matches is treated as a duplicate, so B → A can still be created when A → B exists.

### Editing properties

Select a relationship and the properties panel lets you edit the following.

| Item | Contents |
|---|---|
| Type | One-to-one / one-to-many / many-to-many (changeable later) |
| Constraint Name | The FK constraint name |
| Referenced Column | The column on the source (referenced) side. Only primary key columns are candidates |
| Foreign Key Column | The column on the target (FK-holding) side |
| ON DELETE / ON UPDATE | Referential actions: NoAction / Cascade / SetNull / SetDefault |

Two supplementary notes:

- **Many-to-many does not auto-generate a junction table.** A many-to-many line represents the concept of "a design that goes through a junction table," and the FK column and referential-action settings are disabled for it. To bring it down to a physical design, add the junction table yourself and express it as two one-to-many relationships
- **A relationship maps a single column pair.** Composite primary keys themselves can be expressed, but one relationship cannot carry a multi-column FK mapping

## Display and navigation

- **Zoom** — the "−" and "+" on the status bar, click the percentage for 100%, and "⛶" to fit the whole diagram. The shortcuts Ctrl+- / Ctrl++ / Ctrl+0 / Ctrl+Shift+0 also work
- **Minimap** — enabled with the status-bar toggle. Shown at the bottom right when the diagram does not fit in the viewport; click / drag to move the view
- **Search** — Ctrl+F searches table and column names by partial match (case-insensitive). Enter moves to the next match, clicking a candidate jumps to it, and Esc closes the search
- **Relationship highlighting** — selecting an entity or relationship emphasizes the connected elements and dims the unrelated ones
- **Display toggles** — "Compact" on the toolbar (collapses column rows other than PK / FK), "Descriptions," and "Nullability." All three states are restored on the next launch
- **Auto-arrange** — the toolbar's "Grid," "Tree," and "Free" (places entities with a force-directed model, arranging them so that relationship lines come close to horizontal or vertical), plus "Auto Width" (adjusts widths so column names and types do not overlap)

## Multi-select and bulk operations

Ctrl+click toggles selection, dragging on the canvas makes a rubber-band selection (elements intersecting the rectangle), Ctrl+A selects all entities (any relationship selection is cleared), and Esc clears the entity selection (a relationship selection, and a relationship creation still in progress, are kept). With two or more elements selected, the properties panel switches to a bulk-operations card.

- Bulk change of the title background color
- Delete everything selected
- Group move by dragging

Each of these is undone with a single Undo.

## Undo / Redo

Ctrl+Z / Ctrl+Y ("Undo" and "Redo" on the toolbar) cover the individual editing operations on the diagram's contents: adding, deleting, and changing entities, columns, and relationships; moving; duplicating; color changes; and switching the target DB. The AI chat's individual editing tools go into the same history. Operations that swap out the whole diagram, however — importing a file, importing from a database, and having the AI generate an entire diagram — clear the history, so they cannot be reverted with Undo.

Operations that only change how things look — selection state, zoom and pan, minimap visibility — do not enter the history.

## Switching the target DB

The "Target DB:" combo on the right of the toolbar switches the diagram's target DBMS at any time. Existing column types are converted automatically to the new dialect's types, and columns that could not be converted are listed in a warning dialog (keeping their original types). The switch can be undone. See [Database round-tripping](database.md) for details.

## File operations and auto-save

New (Ctrl+N), open (Ctrl+O), save (Ctrl+S). The save format is a single JSON file that keeps the semantic model (table definitions) separate from the visual information (coordinates and colors), which makes it well suited to diff review in git.

When the window is closed normally, the work in progress is auto-saved and the next launch restores it. That does not cover a forced termination or a failed write, so saving explicitly at each milestone is still recommended.

When the file of the open diagram is modified externally (by the MCP server or another program), the GUI detects the change and picks it up. With no unsaved changes it reloads automatically (keeping the zoom and scroll position) and shows an unobtrusive status-bar notification. Only when there are unsaved changes does it ask whether to reload (discarding the changes) or keep going.

## Keyboard shortcuts

| Key | Action |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+S | New / open / save |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C / Ctrl+V | Copy / paste an entity (on the column grid, copy / paste a column) |
| Ctrl+D | Duplicate the entity |
| Ctrl+A | Select all entities |
| Delete | Delete the selection |
| Esc | Clear the entity selection / close the search |
| Ctrl+F | Search |
| Ctrl+P | Print |
| Ctrl+0 / Ctrl+Shift+0 | Zoom 100% / fit to window |
| Ctrl++ / Ctrl+- | Zoom in / zoom out |

While a text box has focus, the control's standard behavior takes precedence.

## Related pages

- [Database round-tripping](database.md) — DB import, diff sync, DDL generation, dialect switching
- [Import and export](import-export.md) — DBML / Mermaid / definition documents / images / printing
- [Configuring AI chat](ai-chat.md) — creating and editing diagrams in conversation
