# Why QuickER Uses the ER Model as the Source of Truth

*English | [日本語](overview.ja.md)*

QuickER is a Windows tool that supports .NET development of business applications.
It provides a GUI designer for ER diagrams, bidirectional synchronization with databases, and AI integration, all centered on the ER model.
From that model, QuickER can generate DDL, C# code, screen mockups, table definition documents, and more.

Why make the ER model, rather than the code or the database, the source of truth?

Business applications repeat the same schema information in many places.
Define a single "customer," and its columns, keys, and relationships appear in the DDL, entity classes, screen-binding models, and design documents.
When each representation is written by hand, small inconsistencies inevitably begin to appear.

Code-first development addressed this problem by making the code the source of truth.
That makes synchronization with the database easier, but reviewing the schema remains difficult.
Broken normalization and missing relationships are not easy to spot by reading class definitions alone.
Yet a representation for surveying an entire schema has existed for decades.
It is the ER diagram: tables shown as boxes, relationships as lines, and keys visible at a glance.
Putting the schema into a diagram makes design flaws easier for people to spot at a glance.
An ER model based on a diagram that has passed human review is precisely what should serve as the source of truth.

A diagram or model, however, cannot be created once and then left alone.
Unless every implementation-side change is reflected in it, the model gradually drifts from reality until no one trusts it.
QuickER imposes two conditions to avoid this familiar failure.
First, the model must round-trip with both the database and text formats.
Second, artifacts derived from the model must be generated mechanically rather than copied by hand.
When the model changes, its generated artifacts can always be rebuilt from it.
Changes made outside the model can be brought back into it through QuickER's import features.

QuickER's goal is to provide this model-centered development workflow so that teams stop writing the same schema information by hand in place after place, and build higher-quality business applications faster and at lower cost.

## Round-tripping ER models, databases, and text

If the ER model is the source of truth, it is natural to want its definition in text and under Git version control.
QuickER saves an ER diagram as a single JSON file.
Within the JSON, the model definition (meaning) is separated from coordinates and colors (appearance), and the model definition can also be saved on its own.
These files can be versioned in Git and reviewed through pull-request diffs just like source code.
QuickER also supports bidirectional import and export with other text formats, including DBML and Mermaid.
You can work in the format you know best, import the result, and verify it visually in the diagram.

Schema changes can round-trip with a live database in the same way.
QuickER supports five dialects—SQL Server, PostgreSQL, MySQL, Oracle, and SQLite—and can import a schema from a database.
In the other direction, it can detect differences between the model and the database and generate a synchronization script to apply them.
For an application developed code-first, the initial model can be imported from the existing database.

The same mechanism applies to table definition documents.
QuickER can export an Excel definition document from the model and import any manual edits back into the model.
For read-only distribution, it can also export a self-contained HTML definition document in a single file.
Because the documents can always be regenerated from the model, there is no need to hunt through them for stale sections and update those sections by hand.

## Dividing work with AI

What happens in a brand-new project with no database or other data source to import?
Creating dozens of tables on a blank canvas takes real effort.
QuickER's AI chat feature takes on that initial design work.
Ask the AI to "design the tables for order management on an e-commerce site," and it drafts an ER diagram that you can continue refining through conversation.
Depending on your environment, you can connect through an API key, a local LLM, Codex, Claude Code, or Copilot (the GitHub Copilot CLI).

The AI can also generate web screen mockups from the ER model.
The output is written live to a mock folder containing per-screen HTML and shared styles, where you can inspect it in a preview and refine it through conversation.
From the mock, QuickER mechanically generates a self-contained HTML file and design documents, including a screen list, a transition diagram, and a screen-by-entity CRUD matrix.
Sharing these artifacts with stakeholders helps align expectations, including the intended look of the screens, early in the design process.

As an optional additional step, QuickER can generate a Blazor Web App or WPF mock project from the mock folder.
The data layer is generated mechanically from the model, while the AI implements the screen UI.
QuickER then runs `dotnet build` itself to verify that the project actually builds.
Because the quality of the result depends on both the AI model and the connection mode (API key or agent), build errors may remain.
This feature is intended as an aid for proofs of concept and prototyping.

Using AI this way has a different structure from asking it to write an entire application.
When AI writes an application from scratch, probabilistic output spreads across every layer of the codebase, leaving people to catch up afterward through code review.
In QuickER's workflow, probabilistic AI work is limited to drafting the ER diagram and generating design-supporting mockups.
People inspect each artifact and make any necessary corrections.
Once the ER model has passed review, QuickER generates code derived from it with the same content every time.
As long as the ER model is sound, this generated code does not unpredictably change from one day to the next.
The division of labor is not to leave everything to AI, but to move development forward through checkpoints where people can inspect the results.

## What the generated code takes on

What code comes from an ER model that has passed review?
QuickER generates code that can be determined mechanically from the model definition.
Application-specific behavior remains clearly separated as the developer's responsibility.
The generated code falls into three categories.

- **Schema definitions**: QuickER generates entity classes, per-column value objects, and validation code derived from the column definitions.
  With value objects, a customer ID and a product ID become different types, so mixing them up is caught at compile time.
  For a value object representing a string primary key, QuickER can also generate the key value as a GUID.
- **Data operations**: QuickER generates two Repository implementations behind the same interfaces.
  One is the lightweight QuickER implementation, currently supporting SQL Server and SQLite, and the other is an EF Core implementation.
  Because both sit behind abstracted interfaces, switching implementations requires changing only one line of DI registration.
  Queries involving grouping, aggregation, and similar operations can be created and stored with the ER diagram as named queries.
  Each named query is generated as a Repository method.
  The Repository also provides general-purpose methods for running raw SQL.
  QuickER can additionally generate a Repository that accesses a remote database through a Web API instead of connecting to the database directly.
  In that mode, it also generates remote-access interfaces and the client and server code that communicate over HTTP and JSON.
  Switching to remote access likewise takes only one line of DI registration.
- **UI binding models**: QuickER generates an EditModel that can be bound directly to a screen and a Mapper that converts between the EditModel and its entity.
  The EditModel accepts screen input as strings, retains values that pass validation as confirmed values, and stores error information when validation fails.
  The Mapper applies only the EditModel's confirmed values and change state to the entity, preventing invalid input from entering it.
  By handling the conversion, the Mapper also eliminates the need to repeat value conversion and copying logic for every screen.

Generating this code frees developers from repeatedly writing data definitions and data-access code, allowing them to focus on production screen design and business logic.

## Independence of the generated code

Code generation tools come with a familiar concern: generated code may be constrained by the tool, becoming harder to maintain as it is customized.
QuickER's generated code addresses that concern through the following design.

- **UI-framework independent**: The generated code does not depend on a particular UI library and can be used in both desktop and web applications.
- **Cross-platform**: QuickER itself is a Windows tool, but its generated code runs on .NET on Windows, macOS, and Linux.
- **Self-contained**: By default, the runtime—the base classes and shared components used by the generated code—is inlined, allowing the generated code to stand on its own.
  To minimize the amount of generated code, you can instead reference the runtime as a NuGet package.
- **Extensible through partial classes**: You can add validation, change display names, and make other extensions in separate partial-class files without touching the generated files.
  Regenerating the code does not overwrite the partial-class code written by developers.

Under the license, the generated code is also the user's work product and can be used, modified, and redistributed without restriction.
If the generated features are not enough, extend them freely through partial classes.

## Where QuickER stands and what comes next

QuickER is developed as a Windows tool for .NET, and its current code-generation target is C#.
The ER model itself consists of language-independent tables, columns, and relationships; C# generation is only one capability built on top of that model.
Support for other languages is an area we intend to expand in future development.

Even without code generation, QuickER still provides value as an ER diagramming tool through its bidirectional synchronization with databases and text formats.
There is room to make this workflow even easier to use in future development.

The QuickER repository includes a sample ([samples/ec-order](../samples/ec-order)) that walks through ER model design, code generation, and execution.
It uses a SQLite file database, so with the .NET 10 SDK installed, you can run it immediately after cloning the repository.
Start with this sample to experience QuickER's workflow for yourself.
