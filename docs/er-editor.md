# ER diagram editing

*English | [日本語](er-editor.ja.md)*

QuickER's main window has three panes.
On the left is the "Toolbox" (creating entities and relationships), in the middle the canvas, and on the right the "Properties" panel (editing the selected element); drag the boundary between the middle and the right to resize them.
Editing an element opens no dialog: you select it and edit it in the properties panel.
Both side panels collapse from the toolbar (F9 / F10), so you can hide them and keep only the canvas when you want a wide view of the diagram.

## Entities

### Creating and deleting

"Add Entity" in the toolbox adds a new table to the canvas (created as `NewTable` with a PK column `ID`, and selected).
Delete with the "Delete Entity" button or the Delete key.
With multiple entities selected, this becomes a bulk delete, and the connected relationships go with them.

Entities move by dragging.
Copy and paste with Ctrl+C / Ctrl+V, and duplicate with Ctrl+D (or "Duplicate" on the toolbar).

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

- **Add / delete**: add a column with the "+" at the top right of the grid (created with the current target DB's default type); delete with the "−" or the delete button at the end of a row
- **Editable items**: the PK / FK / NULL checkboxes, the column name, the type (choose from the target DB's type list, or type freely), and the description
- **Reordering**: drag rows to change the order
- **Copy / paste**: Ctrl+C / Ctrl+V work per column on the grid

Express a composite primary key by checking PK on several columns.
Which columns make up the key is always the PK checkbox's business; the key's own column order is edited separately, in a "Primary key order" card that the properties panel places between the column grid and the unique constraints.
The card appears only while the selected table has two or more primary key columns, and holds one row per column in key order, with ↑ / ↓ to move a row (the buttons are disabled at the ends).
That order is the order of the generated `PRIMARY KEY` constraint and of the index behind it.
One move is one undo step.

Until the order has been stated explicitly it follows the column order in the grid, so in a diagram built in the editor you drag rows and the key follows.
Stating it once pins it, whether by moving a row in the card, by the `set_primary_key` AI tool, or by importing the table from a database.
From then on, reordering columns in the grid no longer changes the key's order, and a column whose PK checkbox you tick afterwards joins the key at the end (a column that was part of the pinned order before returns to its old place instead).

### Unique constraints

Below the column grid, the properties panel has a "Unique Constraints" card per table.
The card body is collapsible: it opens automatically when the selected table has constraints (or when you add one) and stays collapsed otherwise, with a toggle in the header.
Add a constraint with "+", then build it up one column row at a time.
The "+" under the constraint appends an empty row, and picking a column from its drop-down commits it (one column makes that column unique, several make the combination unique, and the row order is the declaration order).
A drop-down offers only the columns that the other rows of the same constraint do not already use, so you cannot pick the same column twice.
"×" takes a row back out.

Leave the name empty and `UQ_{table}_{columns}` is synthesized at DDL generation time (while the box is empty it shows that synthesized name as its placeholder).
A name imported from a database is kept as it is.
Deleting a column deletes the constraints that include it, together with the column, so a constraint is never silently narrowed to its remaining columns.
Every operation is undoable.

On the canvas, a column that belongs to a constraint is marked `UQ` in the key column.
One column can be a primary key, a foreign key, and part of a unique constraint at once, so the marker is folded to a single one in the order `PK` > `FK` > `UQ`.

## Relationships

### Creating

Press "One-to-One," "One-to-Many," or "Many-to-Many" in the toolbox to enter creation mode, then click the two entities in turn to commit it.
Clicking the same entity twice creates a self-referencing relationship.
On creation, **every primary key column of the source is paired with a matching column on the target**, each looked up by name, and the constraint name is generated in the form `FK_<target>_<source>`.
A source column with no matching target column stays out of the mapping (fill it in from the properties panel), and no target column is used twice.

While creation mode is active the toolbox says so and offers a "Cancel" button for it.
Collapsing the toolbox during creation mode also cancels the mode, because neither the notice nor the cancel button is visible once the panel is gone.

If a relationship with the same start and end points already exists, the new one is rejected as a duplicate, so select and edit the existing relationship instead.
Only the direction that matches counts as a duplicate, so B → A can still be created when A → B exists.

### Editing properties

Select a relationship and the properties panel lets you edit the following.

| Item | Contents |
|---|---|
| Type | One-to-one / one-to-many / many-to-many (changeable later) |
| Constraint Name | The FK constraint name |
| Key Columns | Rows of column pairs. Each row pairs a referenced column on the source side (primary key columns and unique-constraint columns are the candidates) with a foreign key column on the target side. "+" adds a row, "×" removes one, and a row takes effect once both sides are chosen. Two or more rows make a composite foreign key, and the row order is the column order of the constraint |
| ON DELETE / ON UPDATE | Referential actions: NoAction / Cascade / SetNull / SetDefault |

- **Many-to-many does not auto-generate a junction table.**
  A many-to-many line represents the concept of "a design that goes through a junction table," and the FK column and referential-action settings are disabled for it.
  To bring it down to a physical design, add the junction table yourself and express it as two one-to-many relationships
- **A relationship holds an ordered list of column pairs.**
  Composite (multi-column) foreign keys are represented as they are and survive import, DDL generation, and schema sync.
  Note that C# code generation (navigation properties and EF Core) targets single-column foreign keys only; a composite one is skipped there with a warning

## Display and navigation

- **Zoom**: the "−" and "+" on the status bar, click the percentage for 100%, and "⛶" to fit the whole diagram.
  The shortcuts Ctrl+- / Ctrl++ / Ctrl+0 / Ctrl+Shift+0 also work
- **Minimap**: enabled with the status-bar toggle.
  Shown at the bottom right when the diagram does not fit in the viewport; click / drag to move the view
- **Search**: Ctrl+F searches table and column names by partial match (case-insensitive).
  Enter moves to the next match, clicking a candidate jumps to it, and Esc closes the search
- **Relationship highlighting**: selecting an entity or relationship emphasizes the connected elements and dims the unrelated ones
- **Display toggles**: "View" on the toolbar opens a popup with six toggles: "Toolbox," "Property panel," "Descriptions," "Nullability," "Compact" (collapses column rows other than PK / FK), and "Full screen."
  The popup stays open as you flip them and closes when you click outside it.
  Every state except full screen is restored on the next launch
- **Full screen**: "Full screen" (F11) hides the window frame and fills the screen.
  The toolbar and status bar stay, so combining it with the panel toggles below leaves you with just the canvas.
  Exit with F11 or the same toggle in the "View" group; this is the one state that is not restored on the next launch.
  Since the title bar is hidden, an "Exit" button appears at the right end of the toolbar in its place (it closes the app exactly as the title bar's close button does: no confirmation, auto-save first)
- **Panel visibility**: "Toolbox" (F9) and "Property panel" (F10) collapse the left and right panels.
  The canvas takes over the space, and if you have resized the property panel by dragging, that width comes back when you show it again.
  Selecting an entity while the panel is hidden does not reopen it.
  Both states are restored on the next launch
- **Auto-arrange**: the toolbar's "Grid," "Tree," and "Free" (places entities with a force-directed model, arranging them so that relationship lines come close to horizontal or vertical), plus "Auto Width" (adjusts widths so column names and types do not overlap)

## Multi-select and bulk operations

- **Ctrl+click**: toggles the selection.
- **Dragging on the canvas**: makes a rubber-band selection (elements intersecting the rectangle).
- **Ctrl+A**: selects all entities (any relationship selection is cleared).
- **Esc**: clears the entity selection (a relationship selection, and a relationship creation still in progress, are kept).

With two or more elements selected, the properties panel switches to a bulk-operations card.

- Bulk change of the title background color
- Delete everything selected
- Group move by dragging

Each of these is undone with a single Undo.

## Undo / Redo

Ctrl+Z / Ctrl+Y ("Undo" and "Redo" on the toolbar) cover the individual editing operations on the diagram's contents.
That means adding, deleting, and changing entities, columns, and relationships; moving; duplicating; color changes; and switching the target DB, and the AI chat's individual editing tools go into the same history.
Operations that swap out the whole diagram (importing a file, importing from a database, and having the AI generate an entire diagram) clear the history instead, so Undo cannot bring them back.

Operations that only change how things look (selection state, zoom and pan, minimap visibility) do not enter the history.

## Switching the target DB

The "Target DB:" combo on the right of the toolbar switches the diagram's target DBMS at any time.
Existing column types convert automatically to the new dialect's types, and columns that could not be converted are listed in a warning dialog (keeping their original types).
The switch can be undone.
See [Database round-tripping](database.md) for details.

## File operations and auto-save

New (Ctrl+N), open (Ctrl+O), save (Ctrl+S).
The save format is a single JSON file that keeps the semantic model (table definitions) separate from the visual information (coordinates and colors), which makes it well suited to diff review in git.

When the window is closed normally, the work in progress is auto-saved and the next launch restores it.
That does not cover a forced termination or a failed write, so save explicitly at each milestone.

When the file of the open diagram is modified externally (by the MCP server or another program), the GUI detects the change and picks it up.
With no unsaved changes it reloads automatically (keeping the zoom and scroll position) and shows an unobtrusive status-bar notification.
Only when there are unsaved changes does it ask whether to reload (discarding the changes) or keep going.

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
| F9 / F10 | Show or hide the toolbox / property panel |
| F11 | Toggle full screen |
| Ctrl+0 / Ctrl+Shift+0 | Zoom 100% / fit to window |
| Ctrl++ / Ctrl+- | Zoom in / zoom out |

While a text box has focus, the control's standard behavior takes precedence.

## Related pages

- [Database round-tripping](database.md) — DB import, diff sync, DDL generation, dialect switching
- [Import and export](import-export.md) — DBML / Mermaid / definition documents / images / printing
- [Configuring AI chat](ai-chat.md) — creating and editing diagrams in conversation
