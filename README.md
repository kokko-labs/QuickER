# QuickER

*English | [日本語](README.ja.md)*

[![CI](https://github.com/kokko-labs/QuickER/actions/workflows/ci.yml/badge.svg)](https://github.com/kokko-labs/QuickER/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/kokko-labs/QuickER)](https://github.com/kokko-labs/QuickER/releases)
[![License](https://img.shields.io/badge/license-MIT%20%2B%20PolyForm%20NC-blue)](#license)

## Design once. Generate the rest.

**Make the ER model the single source of truth for your database, source code, and design documents**

QuickER is a Windows tool for building .NET business applications around an ER model.
QuickER lets you create and edit ER models through its GUI or AI chat, and bidirectionally synchronize schema changes with live databases.

QuickER generates DDL, C# code, screen mockups, and table definition documents from the ER model.
This reduces the work of copying the same schema into entity classes, UI models, and design documents, helping keep diagrams and implementation in sync.

**No more stale diagrams. No more duplicated definitions.**

![QuickER main screen (the EC order sample ER model)](docs/images/sample-ec-order.png)

- Schema import and diff sync from live databases
- Supports SQL Server / PostgreSQL / MySQL / Oracle / SQLite
- Generates C# code (Entity / EditModel / Mapper / ValueObject / Repository)
- Creates and edits ER models through AI chat, plus AI mock-screen generation
- Integrates with AI agents via an MCP server (Claude Code, Codex, etc.) 
- Import/export with DBML / Mermaid / Excel definition documents
- A git-friendly JSON save format
- Available from both the GUI and the CLI

## Quick start

### 1. Launch QuickER and open a diagram

Get the Setup.exe or the Portable zip from [GitHub Releases](https://github.com/kokko-labs/QuickER/releases) and launch it (see [Install](#install) for details; to run from source, use `dotnet run --project src/QuickER.Gui`).

Clone the repository and open the bundled sample ER model `samples/ec-order/EcOrder.json` — the exact diagram in the screenshot above.

```powershell
git clone https://github.com/kokko-labs/QuickER.git
cd QuickER
```

### 2. Run the generated code

The DDL and C# code generated from this diagram are checked in, and they run as-is with no external database (the .NET 10 SDK is required).

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

```text
[Setup] Created the SQLite file DB (ec-order.db) from the EcOrder.sql DDL.
[1] Registered 2 customers and 2 products.
[2] Graph-saved 1 order + 2 order lines (records saved: 3).
[3] Fetched the order with a Where expression tree + Include:
...
All scenarios succeeded.
```

What's in the sample:

- [EcOrder.json](samples/ec-order/EcOrder.json) — the ER model you can edit in the GUI
- [EcOrder.sql](samples/ec-order/EcOrder.sql) — the SQLite DDL generated from the ER model
- [EcOrder.g.cs](samples/ec-order/EcOrderSample/Generated/EcOrder.g.cs) — the generated C# code
- [Program.cs](samples/ec-order/EcOrderSample/Program.cs) — runnable examples of CRUD, graph save, Include, editing through the EditModel / Mapper, raw SQL, and delete cascade

See [the EC order sample](samples/ec-order/README.md) for details. To walk through the loop from editing the diagram to generating code with your own hands, continue to the [tutorial](docs/getting-started.md).

## Design ER models visually

Design tables, columns, primary keys, foreign keys, unique constraints, and relationships visually in crow's foot notation.

- One-to-one / one-to-many / many-to-many
- Composite primary keys
- Unique constraints (single-column and composite)
- Cascade / SetNull / NoAction
- Undo / Redo
- Zoom, pan, and minimap
- Entity search
- Relationship highlighting
- Multi-select with bulk operations
- A compact view showing PK / FK columns only

See [ER diagram editing](docs/er-editor.md) for details.

![Editing demo (add entities → move → one-to-many relationship → undo / redo)](docs/images/er-editing.gif)

## Round-trip with live databases

Import the schema from an existing database and edit it as an ER model.
You can also detect the differences between the ER model and the database and generate a sync SQL script.

| DBMS       | Schema import | Diff sync | DDL generation | Dialect switch |
| ---------- | :-: | :-: | :-: | :-: |
| SQL Server | ✅ | ✅ | ✅ | ✅ |
| PostgreSQL | ✅ | ✅ | ✅ | ✅ |
| MySQL      | ✅ | ✅ | ✅ | ✅ |
| Oracle     | ✅ | ✅ | ✅ | ✅ |
| SQLite     | ✅ | ✅ | ✅ | ✅ |

Each diagram keeps its target DBMS, and you can switch to another SQL dialect at any time. Types are converted automatically where possible, and types that cannot be converted are flagged with a warning.

See [Database round-tripping](docs/database.md) for details.

![The diff-sync dialog (diff list and generated SQL preview; destructive changes are unselected by default)](docs/images/db-sync.png)

## Generate C# code

From the ER model, generate the C# code your application development needs.

Always generated:

- Entity
- EditModel
- The Mapper between Entity and EditModel

DataAnnotations and DB definition metadata attributes (dialect-neutral type tokens and descriptions) are added by default; they are required whenever a Repository is generated, since the runtime reads them by reflection.

Optionally generated:

- The QuickER Repository (a lightweight, ADO-based implementation)
- An EF Core DbContext and the EF Core Repository implementation
- Per-column value objects
- Named queries
- Remote Repository interfaces
- An HTTP + JSON client
- An ASP.NET Core Minimal API server

The EditModel accepts screen input as strings, keeps values that pass validation as confirmed values, and holds error information for those that fail. The Mapper applies only the confirmed values and change state to the entity, preventing invalid input from entering the entity.

The generated code does not depend on any particular UI framework; use it from any .NET application — WPF, Blazor, ASP.NET Core, and so on.
You can see the EditModel and Mapper in action by running the bundled sample's [Program.cs](samples/ec-order/EcOrderSample/Program.cs).

See [Using the generated code](docs/code-generation.md) for details.

## Data access options

The generation dialog lets you choose the data-access layer from three options.

| Option | Target DB | Use |
|---|---|---|
| **None** | — | Generate Entity / EditModel / Mapper only, and implement data access yourself |
| **QuickER Repository** | SQL Server / SQLite | Use a lightweight ADO-based Repository |
| **EF Core Repository** | The 5 supported DBMS | Use a DbContext and LINQ |

> **Prerequisite**: Repository generation targets tables whose primary key is a single, application-assigned column. Tables with a composite primary key or DB auto-numbering can use the Entity / EditModel only.

The QuickER Repository ships with:

- Expression-tree queries
- `Include` / `ThenInclude`
- Graph save
- Bulk operations
- Conflict detection on graph save (when the target row no longer exists; concurrency control by `rowversion` comparison is out of scope)
- Raw SQL execution

The QuickER Repository and the EF Core Repository implement the same interfaces. Keep your application code dependent on the interfaces, and you can switch implementations by changing the DI registration (the GUI generates one of the two; generating both at once is available through the CLI or a config file).

```csharp
// QuickER Repository
services.AddGeneratedSqliteRepositories(connectionString);

// EF Core Repository
services.AddGeneratedEfCoreRepositories(
    options => options.UseSqlite(connectionString));
```

## Value objects

Enable value-object generation and a dedicated type is generated per column.
For example, a customer ID and a product ID are both integers in the database, but they become distinct types in C#.

```csharp
CustomerIdValue customerId;
ProductIdValue productId;
```

Passing the wrong kind of ID by mistake becomes a compile-time error.
For a value object representing a string primary key, enabling `UseGuidKeyForStringPrimaryKey` mints new keys as GUIDs, which satisfies the application-assigned key prerequisite without any numbering logic of your own.

Validation code that can be derived from the column definitions — maximum lengths, `decimal` precision, and so on — is generated as well. Add custom validation and display names through partial classes.

## Named queries

Store search conditions, ordering, paging, and projections in the ER model, and generate them as typed Repository methods.

```text
CustomerId = @customerId AND Memo LIKE @keyword
```

From this definition, a method like the following is generated.

```csharp
GetByCustomerAsync(
    int customerId,
    string keyword,
    CancellationToken cancellationToken = default);
```

The same named query is generated for both the QuickER Repository and the EF Core Repository.

## Three-tier architecture

Enable the option and, in addition to the direct-database configuration, QuickER generates a configuration that goes through a web service.

```text
Client
  │
  │ HTTP + JSON
  ▼
ASP.NET Core Minimal API
  │
  ▼
Database
```

The following code is generated:

- Remote Repository interfaces
- An `HttpClient`-based client
- ASP.NET Core Minimal API endpoints (delegating to the DI-registered Repository)
- Conversion and restoration of exception information

As long as your application code depends on the remote interfaces, you can switch between direct DB access and going through the web service by changing the DI registration.

See [the three-tier sample](samples/ec-order-remote/README.md) for a working example.

## AI chat

Create and edit ER models in conversation with an AI.
For example, you can say:

```text
Design the tables needed for order management on an e-commerce site
```

The generated ER model can be reviewed and refined with the normal editing operations.

Supported connection methods:

- OpenAI API
- Anthropic API
- Local LLMs (OpenAI-compatible APIs: Ollama, LM Studio, vLLM, etc.)
- Codex
- Claude Code
- Copilot (GitHub Copilot CLI)

![Generating a full set of order-management tables through AI chat (Claude Code connection)](docs/images/ai-chat.png)

See [Configuring AI chat](docs/ai-chat.md) for how to set it up.

## AI mock generation

Use AI to generate HTML mockups of business screens from the ER model through conversation.
The generated files are written to a "mock folder" (mock.json + one HTML per screen + a shared style.css) as the conversation proceeds, and the in-dialog preview lets you follow the transitions between screens.

![Generating 7 order-management screens with AI mock generation (previewing the dashboard)](docs/images/ai-mock.png)

- The conversation proceeds as "propose the screen structure → agree → generate," and you refine the screens with follow-up instructions
- For sharing with stakeholders, export a single HTML that bundles every screen, and a design document with the screen list, a transition diagram, and a CRUD matrix
- As a second step, you can generate a WPF / Blazor mock project from the mock folder: QuickER scaffolds the data layer from the ER model, the AI implements the screen UI, and QuickER checks the result by running `dotnet build`. This step is an aid for PoCs and prototyping — build errors may remain, depending on the AI model and the connection mode

The connection methods are shared with the AI chat.

## Import and export

### Import

- Live databases
- DBML
- Mermaid
- Excel table definition documents
- C# code (a main `.g.cs` generated with `IncludeDataAnnotations` ON)

### Export

- SQL DDL
- DBML
- Mermaid
- Excel table definition documents
- HTML table definition documents
- Schema JSON (layout-free, re-importable)
- PNG
- SVG
- Print / PDF

Update the ER model and re-export the definition documents to keep the design and the documentation from drifting apart.

See [Import and export](docs/import-export.md) for what each format covers.

## A save format you can manage with git

A QuickER ER model is saved as a single JSON file.
Inside the JSON, the semantic model (tables and columns) is separated from the visual information (coordinates and colors).

This lets you keep ER models in the same repository as your source code and review changes through commit history and pull requests.
Via DBML and Mermaid, it also combines with text-centric workflows.

## Install

### GUI

GitHub Releases provides the following packages.

| Channel | Setup | Portable | Required runtime |
| ------------ | ---------------------------- | ------------------------------- | -------------------------------------------- |
| **Full** (recommended) | `QuickER-win-full-Setup.exe` | `QuickER-win-full-Portable.zip` | none |
| **Lite**     | `QuickER-win-lite-Setup.exe` | `QuickER-win-lite-Portable.zip` | .NET 10 Desktop Runtime and ASP.NET Core Runtime |

For the Portable edition, extract the ZIP and run `QuickER.exe`.

To run from source:

```powershell
dotnet run --project src/QuickER.Gui
```

### CLI

Once published to NuGet, you can install it as a dotnet tool.

```powershell
dotnet tool install --global QuickER.Cli
```

Generating code from an ER model:

```powershell
quicker generate `
  --schema diagram.json `
  --out ./Generated `
  --provider sqlserver
```

Generating code directly from a live database:

```powershell
quicker scaffold `
  --provider sqlserver `
  --connection "..." `
  --out ./Generated
```

Recovering an ER diagram JSON (schema only, no layout key) from generated C# code:

```powershell
quicker reverse `
  --source ./Generated/Model.g.cs `
  --out diagram.json `
  --provider sqlserver
```

Until it is published, run it from source.

```powershell
dotnet run --project src/QuickER.Cli -- generate ...
```

See the [CLI reference](docs/cli.md) for details.

## Why the ER model is the source of truth

QuickER treats the ER model as the single source of truth for the database, the code, and the documents.
For the background, how this differs from code-first, and the division of labor between AI and humans, see [Why QuickER uses the ER model as the source of truth](docs/overview.md).

## Documentation

- [Why QuickER uses the ER model as the source of truth](docs/overview.md)
- [Tutorial (from design to running code)](docs/getting-started.md)
- [ER diagram editing](docs/er-editor.md)
- [Database round-tripping](docs/database.md)
- [Import and export](docs/import-export.md)
- [CLI reference](docs/cli.md)
- [Using the generated code](docs/code-generation.md)
- [Configuring AI chat](docs/ai-chat.md)
- [MCP server (quicker mcp)](docs/mcp.md)
- [The EC order sample](samples/ec-order/README.md)
- [The three-tier sample](samples/ec-order-remote/README.md)
- [Changelog](CHANGELOG.md)

## Development

Building and testing QuickER itself requires Windows and the .NET 10 SDK.

```powershell
dotnet build QuickER.slnx
dotnet test QuickER.slnx
```

The integration tests for SQL Server, PostgreSQL, MySQL, and Oracle use Docker. In environments where Docker is not available, those tests are skipped automatically.
The SQLite tests use a real file database.

## Support & contributing

QuickER is developed by a single person.
Support is best-effort and, as a rule, covers the latest version only.

Please file bug reports and feature requests as GitHub Issues; both Japanese and English are welcome.
Before opening a pull request, please discuss the change in an Issue first.

- [Contributing guide](CONTRIBUTING.md)
- [Security policy](SECURITY.md)

## License

Code that QuickER generates — including the inlined runtime portion — is your work product. Generated code is not restricted by QuickER's own licenses, and you may use, modify, and distribute it freely, commercially or otherwise (codified as an explicit grant in [LICENSE-NC.md](LICENSE-NC.md)).

QuickER itself is a mixed-license repository — the licenses apply per project, so the single license label GitHub displays does not tell the whole story.

| Scope | License |
| ---------------------------------------- | --------------------------------------------------- |
| The ER designer, import/export, DDL generation, DB import/sync, the runtime packages, and so on | [MIT License](LICENSE) |
| The AI features, the code-generation projects, and the MCP tool-execution host | [PolyForm Noncommercial 1.0.0](LICENSE-NC.md) + additional grants |

The current releases are free for everyone, including commercial use of the official GUI and CLI. Note that the additional grants cover **using** QuickER: modifying the covered source code or redistributing modified versions for commercial purposes is not included.

Future versions may introduce paid licensing for some features (for example, separately licensed Pro features). Whatever changes, the following commitments stand:

- The basic generation of Entity / EditModel / Mapper remains free permanently, including commercial use.
- Personal and non-commercial use of the existing features remains free.
- Rights granted for a released version are never withdrawn retroactively.
- Any move to paid licensing will be announced in advance, with a transition period for existing users.

These commitments are codified as the "Additional Grants" section of [LICENSE-NC.md](LICENSE-NC.md), and commercial use today rests on the grants in the license file itself.

For a plain-language guide — which license applies to which download, and what you can and cannot do — see [LICENSING.md](LICENSING.md). For the formal terms, always refer to [LICENSE](LICENSE) and [LICENSE-NC.md](LICENSE-NC.md).
