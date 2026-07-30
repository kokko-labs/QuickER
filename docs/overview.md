# The Design Philosophy of QuickER

*English | [日本語](overview.ja.md)*

The codebase of a business application repeats the same knowledge over and over. Define one "customer," and the knowledge of its columns, keys, and relationships shows up in the DDL, in an entity class, in the model bound to the screen, and in the design documents. The same knowledge is copied four times, in four different shapes. This repetition is the same whether your team starts from an ER diagram or starts code-first. Even when migrations take the DDL off your hands, the binding models and the documents remain. And copied knowledge drifts: not every change reaches every copy, so the copies slowly diverge.

The question is where to put the single source of truth. Code-first answered: the code. That settles synchronization with the database — change an entity and a migration follows. But what about reviewing the schema? Design mistakes such as broken normalization or a missing relationship are hard to spot by reading lines of class definitions. Humans have long had a representation for surveying a schema at a glance: the ER diagram. Tables are boxes, relationships are lines, and the keys are visible at once.

That does not mean "just make the diagram the source of truth." Development centered on diagrams has been attempted many times, and it has failed the same way every time: the diagram gets left behind by reality and decays into a picture nobody trusts.

QuickER puts the ER model back in place as the single source of truth by creating the conditions under which that decay does not happen. There are two conditions. The diagram must move both ways between text and the database. And the code, the DDL, and the definition documents downstream of the diagram must all come out automatically (where there is no copying, there is no drift). The aim is to build business applications faster, at lower cost, with the least hand-written code.

## Round-tripping between the diagram, text, and the database

Say "the diagram is the source of truth" and many developers will answer: "I want my schema definitions in text, managed in git." A fair demand — and QuickER does not stand in its way.

A QuickER diagram is a single JSON file that keeps the table definitions (meaning) separate from the coordinates and colors (appearance). The diff is readable in git, so diagrams can be versioned and reviewed in pull requests just like source code. Import and export with DBML and Mermaid are built in, so those who prefer text can write DBML, import it, and verify it as a diagram.

The database round-trips the same way. You can import the schema of a live database (five dialects: SQL Server / PostgreSQL / MySQL / Oracle / SQLite) into a diagram, and you can detect the differences between the diagram and the database and generate a sync script. If you have a system grown code-first, point QuickER at the running database and the first diagram is in your hands right away — no need to redraw anything from a blank canvas. And when the diagram and reality drift apart, diff detection brings the drift into view. If the diagram is ever left behind, it is left behind where you can see it.

Documents are part of the round trip, too. In teams that hand designs around as Excel definition documents, you can both build a diagram from the document at hand and output the document from the diagram. For non-developer stakeholders, export a single HTML definition document. Fix the diagram and the documents follow — the state where "only the documentation is stale" simply stops existing.

Fix the diagram and push it to the database. Pull database-side changes back into the diagram. Take a design received as DBML, verify it as a diagram, and return DDL and definition documents. Start anywhere, move in any direction — and at the center of that loop sits the diagram.

## The division of labor with AI

What about a brand-new project, with no database and no code yet? Raising dozens of tables on a blank canvas is real work in itself.

That is what the AI takes on. Tell the AI chat "design the tables for order management on an e-commerce site" and it drafts a diagram you can refine in conversation. Connections include OpenAI / Anthropic API keys, locally hosted LLMs (OpenAI-compatible APIs such as Ollama and LM Studio), and the account authentication of Codex and Claude Code. There is also a feature that raises web screen mockups from the ER diagram. They are saved live as a "mock folder" — per-screen HTML plus a shared stylesheet — so you can build up multiple screens in conversation, follow the transitions in a preview, and bundle everything into a single HTML file to share with stakeholders. You align on the screens, too, at the earliest stage of design.

This division of labor is structurally different from having an AI write the whole application. When an AI writes from scratch, probabilistic output spreads across every layer of the codebase, and human verification chases after it in the form of code review. In QuickER's flow, the probabilistic step ends at the draft of the diagram. Humans look at the diagram and verify the schema at the level of boxes and lines. Downstream of a reviewed diagram, a deterministic generator emits the same code every time. The generator itself is continuously verified with Roslyn compilation checks and integration tests against real databases ([tests/QuickER.Tests](../tests/QuickER.Tests)), so as long as the diagram is right, the layers below it do not crumble from day to day. Start from natural language, focus human eyes on a single diagram, and hand everything after it to the machine. The point is not to give the AI less to do — it is to gather the AI's output in a place where humans can verify it.

## What the generated code takes on

So what comes out of a reviewed diagram? QuickER draws the line like this: everything mechanically determined by the schema is generated, and nothing application-specific is.

Three layers fall on the generated side:

- **Schema definitions**: entity classes, plus per-column value objects (optional). With value objects, a customer ID and a product ID become distinct types, and a mixed-up ID fails to compile. Validation code derived from the column definitions — maximum lengths, decimal precision — is generated as well
- **Data operations**: choose between two repository styles. The lightweight, minimal-dependency QuickER Repository (expression-tree queries, Include, graph save, optimistic concurrency, and a raw-SQL escape hatch), or the EF Core implementation (developers who know EF Core keep their usual DbContext and LINQ). Both implement the same interfaces, so swapping is a single line of DI registration. Search conditions saved in the diagram (named queries) are generated as typed methods for both
- **UI binding models**: an EditModel you can bind straight to screens, plus the Mapper that converts to and from the entities

DDL comes out in the five dialects as well. The diagram's target DB can be switched at any time, with types converted automatically.

The four copies from the opening now all have somewhere to go. Code generation takes the DDL, the entities, and the binding models — three of the four — and the definition-document export takes the fourth. Nothing is left to copy by hand. What developers write is the screens and the business logic.

## The independence of the generated code

Code generation tools come with a well-worn worry: the generated code is shaped by the generator's convenience, and the moment you step off the path you hit a dead end.

QuickER's generated code answers that worry with the following design.

- **UI-framework independent**: no dependency on any particular UI library; usable in desktop and web applications alike
- **Cross-platform**: QuickER itself is a Windows tool, but the generated code runs on .NET on Windows, Mac, and Linux. The application you build is not tied to the tool's environment
- **Self-contained**: by default the required runtime is inlined into the code, and the build succeeds with no additional packages (a NuGet package-reference mode is also available)
- **Extension through partial classes**: add validation or change display names without touching the generated files
- **Extension to three tiers**: remote interfaces and an HTTP + JSON client/server can be generated additionally, and switching away from direct DB access is, again, one line of DI

And the generated code is, by license as well, your work product: use, modify, and redistribute it without restriction. Where a dead end might have been, the raw-SQL escape hatch and the partial-class extension points are always open.

## Scope, and what comes next

QuickER is developed as a Windows tool for .NET, and the current target of code generation is C#.

That said, even without code generation it earns its keep as an ER diagramming tool. Draw, output the DDL and the definition documents, export images — that usage alone still comes with DDL generation for five dialects and the full round trip with databases and text formats.

What the diagram holds is a language-independent semantic model (tables, columns, relationships). The conversion to C# is just one generator sitting on top of it, so support for other languages remains a matter of adding generators. That is where we want to take future development.

Return to the "customer" from the opening. From now on, that knowledge is written once, in the diagram. The DDL, the entities, the binding models, the definition documents — they all come out of it. Changes work the same way: fix the diagram and everything follows. The time once spent hunting down drifted copies goes back to the screens and the business logic — the actual body of your application.

The repository includes a working sample ([samples/ec-order](../samples/ec-order)) that goes once around from design through generation to running code. It uses a SQLite file DB, so with the .NET 10 SDK it runs right after cloning. Start there, and try development where you write it once.
