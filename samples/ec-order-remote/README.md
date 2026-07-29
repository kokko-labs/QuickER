# EC order remote sample (ec-order-remote)

*English | [日本語](README.ja.md)*

A minimal sample that runs a three-tier setup (client → HTTP + JSON → server → SQLite) using only the code
QuickER generated from an ER diagram with "remote service generation" (`GenerateRemoteServices`).
The subject is the same e-commerce order domain as [ec-order](../ec-order/README.md), which covers the
demonstrations of basic CRUD, graph save, Include, raw SQL, and so on. This sample's scenarios are limited to
the points where going over HTTP actually matters:

- **Switching with a single DI line** — the client's calling code stays on exactly the same interfaces
  (`I{Entity}RemoteRepository`) as the DB-direct case; the only difference is that DI registration becomes a
  single `AddGeneratedHttpRemoteRepositories` line
- **RowState settles after save** — once a graph save succeeds, the local `HasChanges` settles on the client
  too, with the same semantics as `EntityGraphSaver.AcceptChanges` in the DB-direct case
- **Remote transfer of named queries** — a projection DTO (`OrderSummaryRow`) reaches the client as JSON
- **Type restoration of `SaveConflictException`** — an optimistic conflict on the server is restored on the
  client as the same exception type via an HTTP 409 plus structured JSON (you can write exactly the same
  `catch` as in the DB-direct case)

## Structure

| File | Role |
|---|---|
| `EcOrderRemote.json` | The ER diagram (includes two named queries). Open and edit it in the GUI |
| `quicker.json` | The CLI generation options (`GenerateRemoteServices=true`, etc.) |
| `EcOrderRemote.sql` | The SQLite DDL generated from the diagram (checked in) |
| `Generated/EcOrderRemote.g.cs` | The main generated code (entities, repositories, remote contracts, HTTP client, DI extensions; checked in) |
| `Generated/EcOrderRemote.RemoteServer.g.cs` | The server generated code (`MapGeneratedRemoteEndpoints`, Minimal API; checked in) |
| `EcOrderRemote.Shared/` | A shared class library that links only the main generated code (the base for both client and server) |
| `EcOrderRemote.Server/` | A web app (`Microsoft.NET.Sdk.Web`) that links the server generated code and listens over SQLite |
| `EcOrderRemote.Client/` | A console app that verifies the remote-specific scenarios using only the HTTP client implementations |

Because the server generated code requires ASP.NET Core, of the two files the CLI writes to the same output
directory only the main generated code is linked in Shared, while the server generated code is linked in the
Server project (which uses the Web SDK) — a useful reference for placing the two files in your own projects.
None of the projects reference the QuickER main projects; they reference only NuGet packages and the ASP.NET Core
FrameworkReference (server only).

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

The client prints the result of each scenario and exits with code 0 when they all succeed. If a value
differs from what is expected, it exits with an exception (a non-zero exit code). It retries automatically while
waiting for the server to start.

To change the port, pass the same base URL as the first argument to both the server and the client
(e.g. `http://127.0.0.1:5299`).

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Server -- http://127.0.0.1:5299
dotnet run --project samples/ec-order-remote/EcOrderRemote.Client -- http://127.0.0.1:5299
```

## Regenerate the generated code / DDL

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order-remote/EcOrderRemote.json `
  --out samples/ec-order-remote/Generated `
  --provider sqlite `
  --config samples/ec-order-remote/quicker.json
```

`GenerateRemoteServices` is already set in `quicker.json`; both the main generated file and the server generated
file are written to the same `--out`. The procedure for regenerating everything at once with the drift tests'
regeneration mode is shared with [ec-order](../ec-order/README.md).

## Further documentation

For the specification of remote service generation, see the remote services section of
[`docs/code-generation.md`](../../docs/code-generation.md).
