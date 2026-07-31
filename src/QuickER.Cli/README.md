# QuickER.Cli

The command-line tool for QuickER (a Windows ER diagram designer that connects AI-assisted visual ER design × multi-DB round-tripping × C# code generation end to end). Without the GUI, it can generate C# code from an ER diagram JSON (`generate`), scaffold directly from a database (`scaffold`), and reverse a generated C# file back into an ER diagram JSON (`reverse`).

## Install

```powershell
dotnet tool install --global QuickER.Cli
```

## Usage

```powershell
# ER diagram JSON (the GUI save format) → C# code (Entity / EditModel / Mapper / Repository / EF Core)
quicker generate --schema diagram.json --out ./Generated --provider sqlserver

# Connect directly to a live DB and import the schema → C# code
quicker scaffold --connection "Server=.;Database=Shop;Integrated Security=true;TrustServerCertificate=true" --out ./Generated --provider sqlserver

# A C# file generated with IncludeDataAnnotations ON → a schema-only ER diagram JSON (no layout key)
quicker reverse --source ./Generated/Model.g.cs --out diagram.json --provider sqlserver
```

`reverse` parses a main `.g.cs` generated with `IncludeDataAnnotations` ON with Roslyn (syntax only; no compilation) and restores the ER diagram from the `[Table]` / `[Column]` / `[Key]` / `[Required]` / `[DbColumnMeta]` / `[DbTableMeta]` / `[NavigationReference]` attributes. Column types are expanded from the dialect-neutral type token into the `--provider` dialect's native type. Many-to-many relationships, `ON DELETE` / `ON UPDATE` actions, and FK constraint names are not present in the code, so a fresh diagram uses the defaults (import into an existing diagram in the GUI preserves them).

Main options:

| Option | Description |
|---|---|
| `--provider <name>` | Target DB. `sqlserver` (default) / `postgresql` / `mysql` / `oracle` / `sqlite` |
| `--config <file>` | Generation option settings file (quicker.json) |
| `--root-namespace <name>` / `--split-files-by-category` | Set the root namespace / split files by category |
| `--repository-dialects <list>` | Multi-target generation of the QuickER Repository (e.g. `sqlserver,sqlite`, keyed DI) |
| `--use-runtime-packages` | Do not emit the fixed runtime code; provide it via `QuickER.Runtime.*` package references instead |
| `--generate-api-docs` | Additionally output an API reference Markdown (`{base name}.g.md`, English canonical) |
| `--api-docs-ja` | Also output the Japanese API reference Markdown (`{base name}.ja.g.md`; requires `--generate-api-docs`) |

Every settings-file key is also available as a same-named kebab-case flag that overrides the settings file (priority: CLI flag &gt; settings file &gt; default; bool flags are three-valued: `--flag` / `--flag false`).

For the detailed CLI reference, how to use the generated code, and a working sample, see the repository documentation:

https://github.com/kokko-labs/QuickER

## License

PolyForm Noncommercial 1.0.0 (the LICENSE-NC.md bundled with the package). **It is currently free for everyone, including commercial use.** Future versions may introduce paid licensing for some features (basic generation—Entity / EditModel / Mapper—remains permanently free including commercial use / personal and non-commercial use of the existing features remains free / rights granted for a released version are never withdrawn retroactively / any move to paid licensing will be announced in advance, with a transition period for existing users). For details, see LICENSING.md in the repository.

**Code that the CLI generates (including the inlined runtime portion) is your work product**, and you may use, modify, and distribute it freely with no license restrictions.
