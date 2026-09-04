# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

### Added

- **The validation rules a generated value object runs are now public and reusable** — `ValueObjectRules.ValidateRequired`, `ValueObjectStringRules.ValidateMaxLength` and `ValueObjectDecimalRules.Validate` live on the fixed infra (the `QuickER.Runtime` package in package-reference mode) instead of being inlined into every value object, so a generated `ValidateCore` is now one line per rule and a hand-written value object can call the same rules
- **Four general-purpose validation rules to call from `OnValidate`** — `ValueObjectNumberRules.ValidateMaxDigits` (digit count of an integer), `ValueObjectNumberRules.ValidateRange` (closed interval, any `IComparable<T>`), `ValueObjectStringRules.ValidateAsciiAlphanumeric` (ASCII letters and digits plus the symbols you allow) and `ValueObjectStringRules.ValidateEmailAddress` (a practical minimum, deliberately not RFC 5322). Their messages are new `ValueObjectValidationMessages` entries (`DigitsExceeded` / `OutOfRange` / `InvalidCharacters` / `InvalidEmailAddress`)

### Changed

- **Breaking: the per-value-object message and display-name hooks are gone** — generated value objects no longer declare `CustomizeDisplayName`, `CustomizeMaxLengthErrorMessage`, `CustomizeScaleErrorMessage`, `CustomizePrecisionErrorMessage` or `CustomizeValueRequiredErrorMessage`, so an existing implementation of one stops compiling. Customize a display name through `GeneratedDisplayNames.Resolve` and a message through the matching `ValueObjectValidationMessages` entry, branching on the name for a single type (`memberName == nameof(CustomerEntity.Name)`, `displayName == NameValue.DisplayName`). The wording then lives in one place per message rather than one place per type, and nothing has to be re-implemented on the generated class after a regeneration. `OnValidate`, `GetDefinedInstance` and `ConvertCustomInput` are unaffected
- **Breaking: every `ValueObjectValidationMessages` entry takes the display name as its first argument** — `MaxLengthExceeded` is now `(displayName, maxLength, actualLength)`, `ScaleExceeded` and `PrecisionExceeded` take `(displayName, …)`, `ValueRequired` takes `(displayName)`, and `InputNotConvertible`'s arguments are reordered to `(displayName, raw)`. This is what makes a single replacement able to vary the wording per type. The default wording is unchanged, so an application that does not replace these sees no difference
- **Breaking: `IRemoteRepository<TEntity, TKey>` declares `CheckUniquenessAsync`** — the uniqueness pre-check used to be declared on each generated `I{Entity}RemoteRepository` (or `I{Entity}Repository`) and implemented on every generated repository class; the declaration now sits on the common runtime surface and the implementation on each back end's repository base class (the dialect, in-memory, EF Core and HTTP client bases). Code that calls the check is unaffected — `I{Entity}Repository` still provides it through inheritance and the signature, the result and the `CollectCustomUniquenessChecks` hook are unchanged, and an existing hand-written hook keeps working untouched. What breaks is a *hand-written implementation* of `IRemoteRepository<,>` or `IRepository<,>` (a test double or an adapter), which now has to provide the member; deriving from a generated base class instead gets it for free. A generated repository now contributes only the constraint table (`{Entity}UniquenessConstraints.Set`) and a two-line bridge to its hook, which is roughly 50 fewer lines per entity per back end
- **DI registration is one line per entity** — `AddGenerated{SqlServer,Sqlite}Repositories` and `AddGeneratedEfCoreRepositories` register the repository as `services.AddScoped<IContract, Implementation>()` and let the container build it from the dependencies the same call registers (the idiom `AddGeneratedInMemoryRepositories` already used), while the keyed overload and `AddGeneratedHttpRemoteRepositories` fold the wiring every entity shares into one local function. The registered lifetimes, the keyed / non-keyed resolution of `ISqlExecutor`, the `TryAddScoped<ISaveHookRegistry>` default and the remote surface forwarding to the same instance are all unchanged
- **Breaking: the generated mapper's `includeRemoved` is now a required argument** — `CreateEntity(editModel, includeRemoved)`, `CreateEntities(collection, includeRemoved)` and `ApplyToEntity(editModel, entity, includeRemoved)` no longer default to `false`, so every call site has to state whether it is building an entity graph to save (`true`) or to display (`false`). Omitting the argument used to leave the deletion-tracked rows out of the graph, and the save then silently kept the rows the user had removed. To migrate, regenerate the code and add `includeRemoved: true` on save paths and `includeRemoved: false` on display paths at the call sites the compiler now flags

## [0.1.0] - 2026-08-30

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

[Unreleased]: https://github.com/kokko-labs/QuickER/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kokko-labs/QuickER/releases/tag/v0.1.0
