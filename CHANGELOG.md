# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

### Added

- **Hand-written value objects in three members** — the bodies of `Create` / `TryCreate` / `Validate` now live once on `ValueObjectBase<TSelf, TValue>`, so a value object with no diagram column behind it is written as a private constructor plus explicit `New` / `ValidateCore` implementations (see "Hand-written value objects" in docs/code-generation.md). Generated value objects shrink accordingly (identical behavior).

### Changed

- **Breaking**: `IValueObject<TSelf, TValue>` gained a required member `New` (and a `ValidateCore` default member), and the value-object base classes now constrain `TSelf` to implement `IValueObject<TSelf, TValue>`. A value object that implements the interface by hand adds one line (`static T IValueObject<T, V>.New(V v) => new(v);`); one that derives from a base class without declaring the interface adds the interface to its declaration. Regenerated code is unaffected.

### Fixed

- Reflection-based resolution of a value object's `Create` factory (SQL parameter rewrapping and the row-materializer fast path) now finds a factory inherited from a base class (`BindingFlags.FlattenHierarchy`); previously such a value object silently fell back to slower row reads and failed raw-SQL scalar/projection conversion.
- A remote client now classifies a 2xx response whose body is the JSON literal `null` as `RemoteRepositoryException` on operations whose result is never null (previously it surfaced later as an unclear `NullReferenceException`); `GetById` and single-row/nullable-scalar queries still return null as "no such row". Generated mappers also document all `ApplyToEntity` parameters, so builds with `GenerateDocumentationFile` no longer emit CS1573.

## [0.1.0] - 2026-08-02

Initial public release.

### Added

- **Visual ER design** — crow's foot notation, one-to-one / one-to-many / many-to-many, composite primary keys, FK referential actions, comprehensive undo/redo, and a large-diagram canvas UX (zoom / pan / search / minimap)
- **Multi-DB support** — schema import, diff sync, DDL generation, and automatic type conversion on dialect switch, across five dialects (SQL Server / PostgreSQL / MySQL / Oracle / SQLite)
- **C# code generation** — Entity / EditModel / Mapper, plus a 3-way data-access choice: none / the QuickER Repository / the EF Core Repository (the same interfaces, swappable with a single DI registration line). Value objects, named queries, remote contracts and HTTP + JSON services, and a runtime NuGet package reference mode are optional
- **Bidirectional sync** — optionally generates the engine that keeps a local SQLite copy in step with a SQL Server database, over a direct connection or HTTP, with a fast full reload for the first build and for recovery
- **AI chat and mock generation** — create and edit diagrams in conversation (OpenAI API / Anthropic API / an OpenAI-compatible local LLM / Codex / Claude Code / Copilot), generate web screen mockups from the ER model, and optionally scaffold a runnable Blazor or WPF mock project
- **MCP server** — `quicker mcp` exposes diagram editing and code generation to external AI agents over stdio
- **Import/export** — DBML / Mermaid / Excel definition documents / HTML definition documents / schema JSON / PNG / SVG / vector printing
- **CLI** — `quicker generate` / `quicker scaffold` / `quicker reverse` / `quicker mcp`
- **Working samples** — `samples/ec-order` (SQLite, no external database required) and `samples/ec-order-remote` (three-tier over HTTP + JSON)

Distributed as a GUI (Setup.exe and Portable zip, in a full self-contained channel and a lite framework-dependent one) and as NuGet packages (`QuickER.Cli` as a dotnet tool, plus the runtime packages `QuickER.Runtime` / `.SqlServer` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.AspNetCore` / `.Sync`).

The repository is mixed-license: the core is MIT, while the AI features, the code generation, the CLI, and the MCP tool-execution host (8 projects) are PolyForm Noncommercial 1.0.0 plus additional grants — currently free for everyone including commercial use, with commercial use of the basic code generation granted permanently. The terms are in [LICENSE-NC.md](LICENSE-NC.md); [LICENSING.md](LICENSING.md) explains them in plain language.
