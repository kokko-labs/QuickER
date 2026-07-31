# Why QuickER Uses the ER Model as the Source of Truth

*English | [日本語](overview.ja.md)*

QuickER is a Windows tool for teams that build data-centric business applications on .NET. You review one ER model; from that model, QuickER regenerates the DDL and database diffs, C# data-access code, remote APIs, design documents, and screen mockups.

Why gather everything around an ER model? Because a business application repeats the same schema knowledge: define one "customer," and its columns, keys, and relationships appear in the DDL, in an entity class, in the model bound to the screen, and in the design documents. Each copy is written by hand, and hand-written copies diverge, because not every change reaches every copy.

Code-first development answers by making the code the source of truth, which keeps the database in sync but leaves schema review hard: normalization mistakes and missing relationships are difficult to spot in class definitions. The representation humans use to survey a schema at a glance has long existed — the ER diagram, with tables as boxes, relationships as lines, and the keys visible at once.

Declaring the diagram the source of truth is not enough by itself, though. The failure mode is well known: without a way to pull implementation-side changes back in, the diagram falls behind reality and stops being trusted. QuickER addresses that failure mode with two conditions. The model must round-trip with text and with the database, and everything downstream of the model must be generated rather than copied by hand. What is generated can always be reproduced from the model instead of being edited into divergence, and changes that still happen outside the model can be surfaced by running diff detection.

## Round-tripping between the model, text, and the database

QuickER stores and versions a semantic ER model: a single JSON file that keeps the table definitions (meaning) separate from coordinates and colors (appearance). The diff is readable in git, the model can be reviewed in pull requests like source code, and import and export with DBML and Mermaid are built in for those who prefer writing text.

Schemas round-trip with live databases, too. You can import the schema of a running database (five dialects: SQL Server / PostgreSQL / MySQL / Oracle / SQLite), and you can detect the differences between the model and the database and generate a sync script. A code-first system yields its initial model by import, and later divergence shows up as a diff instead of going unnoticed.

Documents round-trip as well: build a model from an Excel definition document, output the document from the model, or export a single self-contained HTML document for non-developer stakeholders. Because the documents are regenerated from the model, updating stale documentation shrinks from hunting through prose to re-exporting.

## What comes out of a reviewed model

For its deterministic code generation, QuickER draws the line here: what is mechanically determined by the schema is generated, and what is application-specific is not.

- **Schema definitions**: entity classes, optional per-column value objects (a customer ID and a product ID become distinct types, so a mixed-up ID fails to compile), and validation code derived from the column definitions
- **Data operations**: a Repository in two styles behind the same interfaces — the lightweight QuickER Repository (currently SQL Server and SQLite) or an EF Core implementation — so for code that depends on the interfaces, swapping is one line of DI registration. Named queries saved in the model become typed methods in either style. Repository generation assumes single-column, application-assigned primary keys
- **UI binding models**: an EditModel you can bind straight to screens, plus the Mapper that converts to and from the entities

DDL comes out in the five dialects as well; the model's target DB can be switched, with types converted automatically where possible and warnings for the rest. The four copies from the opening now all have a source, and what developers write by hand is the production screens and the business logic.

## The division of labor with AI

A brand-new project has no database to import, and raising dozens of tables on a blank canvas is real work in itself. That is what the AI chat takes on: ask it to "design the tables for order management on an e-commerce site" and it drafts a model you refine in conversation (for the available connections, from API keys to local LLMs, Codex, and Claude Code, see [the AI chat guide](ai-chat.md)).

The AI also generates web screen mockups from the ER model, saved live as a mock folder — per-screen HTML plus a shared stylesheet — in which each screen declares its transitions and which entities it reads and writes. You follow the transitions in a preview, bundle everything into a single HTML file to share with stakeholders, and QuickER derives a design document (a screen list, a transition diagram, and a screen-by-entity CRUD table) from the folder without any AI involved.

As an optional second step, QuickER generates a Blazor Web App or WPF mock project from the folder. The data layer is scaffolded deterministically from the model, the AI implements the screen UI, and QuickER checks the outcome itself by running `dotnet build` — the build is verified, not assumed (agent backends iterate on build failures; the API-key mode applies at most one fix, so a failing build can remain).

The probabilistic work, then, does not end at the model — the mockups and the mock UI are AI output as well. What the flow controls is where each output lands: every AI step deposits its result into an artifact you can inspect and keep, and humans review the model itself as a diagram, at the level of boxes and lines. Downstream of that review, deterministic generators reproduce the data layer, the DDL, and the documents from the same input every time.

| Step | Mainly done by | What you check |
| --- | --- | --- |
| Drafting the ER model | AI, or you | Tables, columns, keys, relationships |
| Finalizing the ER model | You | The schema, against the requirements |
| DDL, entities, Repository, documents | Deterministic generators | Diffs, build, tests |
| Screen mockups, Blazor / WPF UI | AI | Screens, transitions, CRUD declarations |
| Mock-project data layer, final build | Deterministic scaffold and `dotnet build` | The build result |

The generators are continuously verified with Roslyn compilation checks and integration tests against real databases ([tests/QuickER.Tests](../tests/QuickER.Tests)). That makes the generated layers reproducible from a reviewed model and catches generator regressions; it does not replace your review of whether the generated behavior meets the requirements.

## The independence of the generated code

Code generation tools come with a well-worn worry: the generated code is shaped by the generator's convenience, and customizing it gets expensive. QuickER's generated code has these properties:

- **UI-framework independent**: no dependency on any particular UI library; usable in desktop and web applications alike
- **Cross-platform**: QuickER itself is a Windows tool, but the generated code runs on .NET on Windows, Mac, and Linux
- **Self-contained**: by default QuickER's runtime is inlined into the generated code instead of being pulled from packages (a NuGet package-reference mode is also available)
- **Extensible through partial classes**: add validation or change display names without touching the generated files
- **Extensible to three tiers**: remote interfaces and an HTTP + JSON client/server can be generated additionally, and code that depends on the remote interfaces switches away from direct DB access with one line of DI

The generated code is also, by license, your work product: use, modify, and redistribute it without restriction. For anything the generated methods do not cover, raw SQL and the partial-class extension points remain available.

## Scope, and how to try it

QuickER is developed as a Windows tool for .NET, and the current target of code generation is C#. Even without code generation it works as an ER diagramming tool, and the five-dialect DDL generation and the full round trip come with that usage as well.

The model itself is language-independent — tables, columns, and relationships — so supporting another language means adding that language's generation rules and verification, not rebuilding the model.

The repository includes a working sample ([samples/ec-order](../samples/ec-order)) that walks from design through generation to running code; it uses a SQLite file DB, so with the .NET 10 SDK it runs right after cloning. The "customer" from the opening is written there exactly once — in the model.
