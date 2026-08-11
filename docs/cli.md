# CLI reference (quicker)

*English | [日本語](cli.ja.md)*

The QuickER CLI provides subcommands for generating code (`generate` / `scaffold`), reverse-engineering a diagram from generated C# (`reverse`), and running an MCP server for AI agents (`mcp`, see [MCP server](mcp.md)).

| Command | Input | Output |
|---|---|---|
| `quicker generate` | ER diagram JSON (the GUI save format) | C# code |
| `quicker scaffold` | Database connection string (imports the schema directly) | C# code |
| `quicker reverse` | Generated C# source (a `.g.cs` produced with `IncludeDataAnnotations` enabled) | Schema-only ER diagram JSON |

The CLI display language follows the OS language setting (Japanese / English).

Once published to NuGet, you can install it with `dotnet tool install --global QuickER.Cli`. Until then, run it from source:

```powershell
dotnet run --project src/QuickER.Cli -- generate --schema diagram.json --out ./Generated
```

## quicker generate

Generates C# code (Entity / EditModel / Mapper / Repository, and so on) from an ER diagram JSON.

```powershell
quicker generate --schema diagram.json --out ./Generated --provider sqlserver --config quicker.json
```

| Option | Required | Description |
|---|:-:|---|
| `--schema <file>` | ✅ | The input ER diagram JSON file (the application's save format) |
| `--out <dir>` | ✅ | The output folder for the generated code |
| `--provider <name>` | | Target database type. `sqlserver` (default) / `postgresql` / `mysql` / `oracle` / `sqlite` |
| `--config <file>` | | Generation option settings file (quicker.json). See below |

In addition to these, **every key in the settings file (quicker.json) can be specified as a same-named kebab-case flag**, and such flags take precedence over the settings file (priority: **CLI flag > settings file > default**). A flag name is the mechanical kebab-case conversion of the key; for example `rootNamespace` → `--root-namespace`, `generateRepositories` → `--generate-repositories`, `splitFilesByCategory` → `--split-files-by-category`, `outputPath` → `--output-path`. There are two exceptions to the mechanical conversion: `IncludeJapaneseApiDocs` is spelled `--api-docs-ja`, and the canonical key `OutputFileName` has no flag of its own — use `--output-path` (only its file-name part is used). Bool keys are **three-valued**: `--flag` (no value) = `true`, `--flag false` = `false`, and omitting it = the value from the settings file. For the meaning of each key, see the "Settings file" table below (`--repository-dialects` is a comma-separated list of dialects; when omitted, a single dialect is derived from the `--provider` dialect).

## quicker scaffold

Connects directly to a database, imports the schema, and generates code. The options are shared with `generate`; instead of `--schema`, you specify `--connection`.

```powershell
quicker scaffold --connection "Server=.;Database=Shop;Integrated Security=true;TrustServerCertificate=true" --out ./Generated --provider sqlserver
```

| Option | Required | Description |
|---|:-:|---|
| `--connection <string>` | ✅ | The connection string (the format follows the DBMS of `--provider`) |

The other options (`--out` / `--config` / `--provider`, and the kebab-case flags named after the settings keys) are the same as for `generate`.

## quicker reverse

Recovers an ER diagram from C# code that QuickER generated, and writes it out as a schema-only diagram JSON. The input must be a main `.g.cs` generated with `IncludeDataAnnotations` enabled, because the DB-definition meta attributes (`[DbTableMeta]` / `[DbColumnMeta]`) are what carry the schema information; hand-written POCOs are out of scope.

```powershell
quicker reverse --source ./Generated/Model.g.cs --out diagram.json --provider sqlserver
```

| Option | Required | Description |
|---|:-:|---|
| `--source <file>` | ✅ | The input C# source file (a main `.g.cs` generated with `IncludeDataAnnotations` enabled) |
| `--out <file>` | ✅ | The output ER diagram JSON file (schema only; no `layout` key) |
| `--provider <name>` | | The dialect used to expand column types, and the `TargetDbms` recorded in the diagram. `sqlserver` (default) / `postgresql` / `mysql` / `oracle` / `sqlite` |

The result is written as a new diagram; it is never merged into an existing one. Non-fatal findings are reported as warnings on standard error, and the command fails when the source contains no eligible class.

## quicker mcp

Starts a stdio MCP (Model Context Protocol) server that exposes ER diagram editing and code generation tools to AI agents (Claude Code, Codex, and so on). It takes no options and is stateless: each tool takes the target diagram file as its `file` argument — except `get_generation_config_schema`, the one information-only tool, which takes no `file`.

```powershell
quicker mcp
```

The agent launches this as a child process and communicates over stdin/stdout (JSON-RPC). For the full tool list, client setup, and notes, see [MCP server](mcp.md).

## Settings file (quicker.json)

The JSON passed via `--config` lets you specify generation options in bulk. It uses the **same schema** as the GUI's settings save file (`codegen-settings.json`) (the GUI writes it in camelCase, but the CLI interprets it case-insensitively, so you can pass it as is). **Each key in the table below can also be specified from the CLI as a same-named kebab-case flag, which takes precedence over the settings file** (priority: CLI flag > settings file > default; bool keys are three-valued: `--flag` / `--flag false`).

> **Note**: the default of `GenerateRepositories` changed from `true` to `false`. Previously, omitting the key generated a QuickER Repository; **now, generating DB-access code requires explicitly specifying `GenerateRepositories: true` (or `GenerateEfCore: true`)** (to align with the GUI's DB-access "None" default).

The keys are ordered by category (output mode → namespaces → generation targets → value objects → DB access → remote support → runtime & documentation → attributes → output path).

```json
{
  "SplitFilesByCategory": false,
  "RootNamespace": "MyApp.Generated",
  "GenerateEditModels": true,
  "GenerateMappers": true,
  "GenerateValueObjects": false,
  "GenerateRepositories": true,
  "GenerateEfCore": false,
  "IncludeDataAnnotations": true,
  "OutputPath": "MyAppEntities.g.cs"
}
```

Main keys (the default is in parentheses; category order):

| Key | Description |
|---|---|
| `SplitFilesByCategory` (`false`) | Output each category in a separate file and namespace. You can specify namespaces individually with `EntityNamespace` / `RepositoryNamespace`, and so on |
| `RootNamespace` (`Generated`) | The root namespace of the generated code |
| `GenerateEditModels` / `GenerateMappers` (both `true`) | Whether to generate each category. **Entity classes are always generated**, and there is no dedicated key for them |
| `GenerateValueObjects` (`false`) | Generate a per-column value object type (such as `CustomerIdValue`) (see [Using the generated code](code-generation.md#value-objects-generatevalueobjects)) |
| `UseGuidKeyForStringPrimaryKey` (`false`) | Make a string primary key a GUID value object (only when `GenerateValueObjects` is enabled) |
| `GenerateRepositories` (`false`) | Generate a QuickER Repository (a lightweight mini-ORM). **By default, no DB-access code is generated** (the same default as the GUI) |
| `RepositoryDialects` (unspecified) | The multi-target dialect list for the QuickER Repository (e.g. `["sqlserver", "sqlite"]`). Only `sqlserver` and `sqlite` are supported; combining any other dialect with `GenerateRepositories` fails before generation. When unspecified, the effective value is resolved in this order: `--repository-dialects` > this key in the settings file > a single dialect derived from `--provider` |
| `ExcludeUnboundedBinaryColumns` (`false`) | Exclude unbounded binary columns from the QuickER Repository's SELECT / UPDATE (corresponds to the CLI's `--exclude-unbounded-binary-columns`; see [Using the generated code](code-generation.md#excluding-unbounded-binary-columns-excludeunboundedbinarycolumns)) |
| `GenerateEfCore` (`false`) | Generate the `QuickErDbContext` for EF Core plus EF Core Repository implementations. Cannot be combined with multi-targeting (two or more effective dialects) |
| `GenerateInMemoryRepositories` (`false`) | Generate an in-memory Repository implementation for testing |
| `GenerateRemoteContracts` (`false`) | Additionally generate the remote-operation interface `I{Entity}RemoteRepository` (corresponds to the CLI's `--generate-remote-contracts`; requires a QuickER Repository, an EF Core Repository, or an in-memory Repository — that is, `GenerateRepositories`, `GenerateEfCore`, or `GenerateInMemoryRepositories`; see [Using the generated code](code-generation.md)) |
| `GenerateRemoteServices` (`false`) | Generate HTTP client/server implementations for the remote surface (automatically implies `GenerateRemoteContracts`; corresponds to the CLI's `--generate-remote-services`; see [Using the generated code](code-generation.md)) |
| `UseRuntimePackages` (`false`) | Do not emit the fixed runtime code; provide it via NuGet package references instead (see [Using the generated code](code-generation.md)) |
| `GenerateApiDocs` (`false`) | Additionally output an API reference Markdown (`{base name}.g.md`, English canonical) (corresponds to the CLI's `--generate-api-docs`; see [Using the generated code](code-generation.md)) |
| `IncludeJapaneseApiDocs` (`false`) | Also produce the Japanese API reference Markdown (`{base name}.ja.g.md`) (requires `GenerateApiDocs`; corresponds to the CLI's `--api-docs-ja`) |
| `IncludeDataAnnotations` (`true`) | Apply DataAnnotations such as `[Required]` / `[MaxLength]`, and the DB-definition meta attributes (`[DbTableMeta]` / `[DbColumnMeta]`) |
| `IncludeJsonIgnoreOnParentNavigation` (`true`) | Apply `[JsonIgnore]` to parent-reference navigations (to guard against circular references during JSON serialization) |
| `OutputFileName` (`QuickEREntities.g.cs`) — the alias `OutputPath` is also accepted | The file name for single-file output (`.g.cs` is appended when missing; ignored when `SplitFilesByCategory` is true). The canonical key is `OutputFileName`, which is what `get_generation_config_schema` reports; `OutputPath` is its alias, and only its file-name part is used (the output directory is always `--out`). In the GUI, `OutputPath` may hold the full output path (a file when not split, a folder when split), but the CLI interprets it by the same rule. **This is the one exception to "CLI flag > settings file"**: `--output-path` only takes effect when the settings file does not already define `OutputFileName`; if it does, the settings file's `OutputFileName` wins |

## Example — regenerating the sample bundled with the repository

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json `
  --generate-api-docs
```

With `--generate-api-docs`, an API reference Markdown named `EcOrder.g.md` (English canonical) is also produced alongside the generated code `EcOrder.g.cs`, sharing the same base name (checked in and subject to drift detection). If you also want the Japanese version `{base name}.ja.g.md`, add `--api-docs-ja` (which requires `--generate-api-docs`).

## License note

The CLI (`QuickER.Cli`), the code generation engine, and the MCP tool-execution host (`QuickER.Mcp.Tools`) are licensed under [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md) **plus additional grants**; thanks to those grants, **the current releases are free for everyone, including commercial use**. The tool-definition catalog and stdio hosting project (`QuickER.Mcp`) is MIT. Eight projects in total are NC-covered — for the full mapping and the licensing and distribution policy, see the [licensing guide](../LICENSING.md). **Code that is generated is your work product**: [LICENSE-NC.md](../LICENSE-NC.md) grants everyone a perpetual, irrevocable license to use, modify, distribute, and sell generated output for any purpose, with no attribution required.
