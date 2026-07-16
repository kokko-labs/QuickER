# EC order remote sample (ec-order-remote)

*[日本語](README.ja.md) | English*

A sample that runs a three-tier setup (client → HTTP + JSON → server → SQLite) using only the code
QuickER generated from an ER diagram with "remote service generation" (`GenerateRemoteServices`).
The subject is the same e-commerce order domain as [ec-order](../ec-order/README.md) (customers, products,
orders, order lines), plus two named queries (search orders by customer, and a projected order summary).

The client's calling code is written against exactly the same interfaces (`I{Entity}RemoteRepository`) as the
DB-direct case; the only difference is that DI registration becomes a single `AddGeneratedHttpRemoteRepositories`
line. The generated `Http{Entity}RemoteRepository` (client) and `MapGeneratedRemoteEndpoints` (server) wire the
two sides together over HTTP + JSON automatically.

## Structure

| File | Role |
|---|---|
| `EcOrderRemote.json` | The ER diagram (the GUI save format). Includes two named queries. Open and edit it in the GUI |
| `quicker.json` | The CLI generation options (namespace, output file name, and `GenerateRemoteServices=true`) |
| `EcOrderRemote.sql` | The SQLite DDL generated from the diagram (checked in) |
| `Generated/EcOrderRemote.g.cs` | The main generated code (entities, custom SQLite repositories, remote contracts, HTTP client, DI extensions; checked in) |
| `Generated/EcOrderRemote.RemoteServer.g.cs` | The server generated code (`MapGeneratedRemoteEndpoints`, Minimal API). A separate file requiring ASP.NET Core; checked in |
| `EcOrderRemote.Shared/` | A shared class library that links only the main generated code (the base for both client and server) |
| `EcOrderRemote.Server/` | A web app (`Microsoft.NET.Sdk.Web`) that links the server generated code and listens over SQLite |
| `EcOrderRemote.Client/` | A console app that uses only the HTTP client implementations and verifies each scenario |

None of the projects reference the QuickER main projects at all; like a user's own project, they reference only
NuGet packages (`Microsoft.Data.Sqlite`, etc.) and the ASP.NET Core FrameworkReference (server only). Because the
server generated code requires ASP.NET Core, of the two files the CLI writes to the same output directory only the
main generated code is linked in Shared, while the server generated code is linked in the Server project (which
uses the Web SDK).

## Run it

From the repository root, run the following in two terminals in order (the .NET 10 SDK is required).

Terminal 1 (server):

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Server
```

At startup it recreates a SQLite file DB (`ec-order-remote.db`, created under the same `bin` folder as the
executable) from the `EcOrderRemote.sql` DDL and listens on `http://127.0.0.1:5210/quicker`.

Terminal 2 (client):

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Client
```

The client prints the result of each scenario in Japanese and exits with code 0 when they all succeed. If a value
differs from what is expected, it exits with an exception (a non-zero exit code). It retries automatically while
waiting for the server to start.

To change the port, pass the same base URL as the first argument to both the server and the client
(e.g. `http://127.0.0.1:5299`).

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Server -- http://127.0.0.1:5299
dotnet run --project samples/ec-order-remote/EcOrderRemote.Client -- http://127.0.0.1:5299
```

## Highlights

- **The calling code is identical to the DB-direct case**: the client just calls CRUD, save, and queries on
  interfaces like `ICustomerRemoteRepository`. Swap the DI registration for `AddGeneratedSqliteRepositories` (DB-direct)
  and the same code runs locally instead.
- **Remote transfer of named queries**: `GetByCustomerAsync` (a DSL condition plus paging) and `GetSummariesAsync`
  (returning a projection DTO) return the same results over HTTP (the projection `OrderSummaryRow` round-trips as
  JSON as well).
- **Type restoration of `SaveConflictException`**: an update-save of a non-existent order becomes an optimistic
  conflict on the server, and via an HTTP 409 plus structured JSON it is restored on the client as the same
  `SaveConflictException` — so you can write exactly the same `catch` as in the DB-direct case.
- **RowState settles after save**: once a graph save succeeds, the local `HasChanges` settles to `false` on the
  client too (the same semantics as `EntityGraphSaver.AcceptChanges` in the DB-direct case).

## Open the diagram in the GUI

`EcOrderRemote.json` is exactly the save format of the GUI (QuickER.Gui). Launch the GUI and open
`samples/ec-order-remote/EcOrderRemote.json` to view and edit the diagram (including its named queries).

## Regenerate the generated code / DDL

### Regenerate with the real CLI

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order-remote/EcOrderRemote.json `
  --out samples/ec-order-remote/Generated `
  --provider sqlite `
  --config samples/ec-order-remote/quicker.json
```

`GenerateRemoteServices` is already set in `quicker.json` (the `--generate-remote-services` flag is equivalent). Both the
main generated file and the server generated file are written to the same `--out`.

### Regenerate everything at once with the drift tests' regeneration mode

After changing a template or the like, you can regenerate them with the same single command as the existing
fixtures.

```powershell
$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
```

After regenerating, run the same tests again without the environment variable and confirm they are green (no drift).

## Further documentation

- For details on code generation, including remote service generation, see [`docs/code-generation.md`](../../docs/code-generation.md).
