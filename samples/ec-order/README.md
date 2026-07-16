# EC order sample (ec-order)

*[日本語](README.ja.md) | English*

A minimal sample that lets you actually run the C# code QuickER generated from an ER diagram, with no external DB.
The subject is an e-commerce order domain (customers, products, orders, order lines), and it demonstrates
CRUD, graph save, Include, raw-SQL aggregation, and delete cascade against a SQLite file DB via the custom
Repository (`RepositoryDialect=sqlite`).

## Structure

| File | Role |
|---|---|
| `EcOrder.json` | The ER diagram (the GUI save format). You can open and edit it in the GUI |
| `EcOrder.sql` | The SQLite DDL generated from the diagram (checked in) |
| `quicker.json` | The CLI generation options (a minimal config with only the namespace and output file names) |
| `EcOrderSample/Generated/EcOrder.g.cs` | The C# code generated from the diagram (checked in) |
| `EcOrderSample/Generated/EcOrder.g.md` | The generated API reference Markdown (the bundled output of `--generate-api-docs`, checked in) |
| `EcOrderSample/Program.cs` | A console app that creates the DB from the DDL and demonstrates CRUD |

The console app references none of the QuickER main projects at all; like a user's own project, it references
only NuGet packages (`Microsoft.Data.Sqlite`, etc.).

## Run it

From the repository root, run the following (the .NET 10 SDK is required).

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

At startup it recreates a SQLite file DB (`ec-order.db`, created under the same `bin` folder as the executable)
from the `EcOrder.sql` DDL and prints the result of each scenario in Japanese. If a value differs from what is
expected, it exits with an exception (a non-zero exit code).

## Open the diagram in the GUI

`EcOrder.json` is exactly the save format of the GUI (QuickER.Gui). Launch the GUI and open
`samples/ec-order/EcOrder.json` to view and edit the diagram.

## Regenerate the generated code / DDL

### Regenerate with the real CLI

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json `
  --generate-api-docs
```

Adding `--generate-api-docs` also outputs the API reference Markdown `EcOrder.g.md` with the same base name as
`EcOrder.g.cs` (both are subject to the drift tests).

### Regenerate everything at once with the drift tests' regeneration mode

`EcOrderSampleDriftTests` verifies that the checked-in generated artifacts are byte-identical to what the
real CLI regenerates via the same path. After changing a template or the like, you can regenerate them with
the same single command as the existing fixtures.

```powershell
$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
```

After regenerating, run the same tests again without the environment variable and confirm they are green (no drift).
