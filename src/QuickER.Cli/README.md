# QuickER.Cli

The command-line tool for QuickER (a Windows ER diagram designer that connects AI-assisted visual ER design × multi-DB round-tripping × C# code generation end to end). Without the GUI, it can generate C# code from an ER diagram JSON (`generate`) and scaffold directly from a database (`scaffold`).

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
```

Main options:

| Option | Description |
|---|---|
| `--provider <name>` | Target DB. `sqlserver` (default) / `postgresql` / `mysql` / `oracle` / `sqlite` |
| `--config <file>` | Generation option settings file (quicker.json) |
| `--namespace <name>` / `--split` | Set the root namespace / split files by category |
| `--repository-dialects <list>` | Multi-target generation of the custom Repository (e.g. `sqlserver,sqlite`, keyed DI) |
| `--runtime-packages` | Do not emit the fixed runtime code; provide it via `QuickER.Runtime.*` package references instead |
| `--api-docs` | Additionally output an API reference Markdown (`{base name}.g.md`) |

For the detailed CLI reference, how to use the generated code, and a working sample, see the repository documentation:

https://github.com/kokko-labs/QuickER

## License

PolyForm Noncommercial 1.0.0 (the LICENSE-NC.md bundled with the package). **It is currently free for everyone, including commercial use.** In the future, commercial use only may become paid-licensed (personal and non-commercial use remains permanently free / basic generation—Entity / EditModel / Mapper—remains permanently free including commercial use / if we introduce paid licensing, we will announce it in advance and provide a transition period for existing users).

**Code that the CLI generates (including the inlined runtime portion) is your work product**, and you may use, modify, and distribute it freely with no license restrictions.
