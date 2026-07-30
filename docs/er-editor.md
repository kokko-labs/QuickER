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
| Notes | A free-form field kept only in the diagram |

### Editing columns

Columns are edited inline in the "Columns" grid of the properties panel.

- **Add / delete** — add a column with the "+" at the top right of the grid (created with the current target DB's default type); delete with the "−" or the delete button at the end of a row
- **Editable items** — the PK / FK / NULL checkboxes, the column name, the type (choose from the target DB's type list, or type freely), and the description
- **Reordering** — drag rows to change the order
- **Copy / paste** — Ctrl+C / Ctrl+V work per column on the grid

Express a composite primary key by checking PK on multiple columns.

## Relationships

### Creating

Press "One-to-One," "One-to-Many," or "Many-to-Many" in the toolbox to enter creation mode, then click two entities in order to confirm. Clicking the same entity twice creates a self-referencing relationship. On creation, the source PK column and the target FK column are matched automatically, and the constraint name is generated in the form `FK_<target>_<source>`.

If a relationship already exists between the same two entities, the new one is rejected as a duplicate — select and edit the existing relationship instead.

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
- **Auto-arrange** — the toolbar's "Grid," "Tree," and "Free" (roughly places entities by their relationship connections, then snaps to a grid), plus "Auto Width" (adjusts widths so column names and types do not overlap)

## Multi-select and bulk operations

Ctrl+click toggles selection, dragging on the canvas makes a rubber-band selection (elements intersecting the rectangle), Ctrl+A selects everything, and Esc clears the selection. With two or more elements selected, the properties panel switches to a bulk-operations card.

- Bulk change of the title background color
- Delete everything selected
- Group move by dragging

Each of these is undone with a single Undo.

## Undo / Redo

Ctrl+Z / Ctrl+Y ("Undo" and "Redo" on the toolbar) cover every operation on the diagram's contents: adding, deleting, and changing entities, columns, and relationships; moving; duplicating; color changes; and switching the target DB. Edits made by the AI chat go into the same history, so they can always be undone.

Operations that only change how things look — selection state, zoom and pan, minimap visibility — do not enter the history.

## Switching the target DB

The "Target DB:" combo on the right of the toolbar switches the diagram's target DBMS at any time. Existing column types are converted automatically to the new dialect's types, and columns that could not be converted are listed in a warning dialog (keeping their original types). The switch can be undone. See [Database round-tripping](database.md) for details.

## File operations and auto-save

New (Ctrl+N), open (Ctrl+O), save (Ctrl+S). The save format is a single JSON file that keeps the semantic model (table definitions) separate from the visual information (coordinates and colors), which makes it well suited to diff review in git.

Closing the window auto-saves the work in progress, and the next launch restores it. Forgetting to save explicitly does not lose your work.

When the file of the open diagram is modified externally (by the MCP server or another program), the GUI detects it and follows. With no unsaved changes it reloads automatically (keeping the zoom and scroll position) and shows an unobtrusive status-bar notification. Only when there are unsaved changes does it ask whether to reload (discarding the changes) or keep going.

## Keyboard shortcuts

| Key | Action |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+S | New / open / save |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C / Ctrl+V | Copy / paste an entity (on the column grid, copy / paste a column) |
| Ctrl+D | Duplicate the entity |
| Ctrl+A | Select all entities |
| Delete | Delete the selection |
| Esc | Clear the selection / close the search |
| Ctrl+F | Search |
| Ctrl+P | Print |
| Ctrl+0 / Ctrl+Shift+0 | Zoom 100% / fit to window |
| Ctrl++ / Ctrl+- | Zoom in / zoom out |

While a text box has focus, the control's standard behavior takes precedence.

## Related pages

- [Database round-tripping](database.md) — DB import, diff sync, DDL generation, dialect switching
- [Import and export](import-export.md) — DBML / Mermaid / definition documents / images / printing
- [Configuring AI chat](ai-chat.md) — creating and editing diagrams in conversation
