using EcOrderRemoteSample.Generated;
using Microsoft.Data.Sqlite;

// A sample server that exposes, over HTTP + JSON against a real SQLite file DB, the server implementation
// QuickER generated from the diagram with "remote service generation" (GenerateRemoteServices).
// The remote surface (I{Entity}RemoteRepository) is backed by the SQLite-dialect QuickER repositories
// (AddGeneratedSqliteRepositories), and the generated MapGeneratedRemoteEndpoints exposes each operation as
// POST /quicker/{entity}/{operation}.
// The client (EcOrderRemote.Client) calls this server over HTTP.

// The listen URL can be overridden with the first argument (pass the same value as the client).
// The default is a fixed port on the local loopback.
var url = args.FirstOrDefault() ?? "http://127.0.0.1:5210";

// Place the DB file next to the executable (under bin) so the working directory (e.g. the repository root)
// stays clean.
var dbFilePath = Path.Combine(AppContext.BaseDirectory, "ec-order-remote.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbFilePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
}.ConnectionString;

// Delete any existing DB file and recreate it from the DDL so the server starts idempotently.
if (File.Exists(dbFilePath))
{
    // The connection pool may hold the file open and block deletion, so release the pool for this
    // connection string first (ClearAllPools would also drop pools this sample does not own).
    using (var pooled = new SqliteConnection(connectionString))
    {
        SqliteConnection.ClearPool(pooled);
    }

    File.Delete(dbFilePath);
}

// Create the schema from the generated DDL in a single call. The bootstrap opens its connection through the
// generated SqlConnectionFactory, which turns foreign key enforcement on by default (SQLite leaves it off
// unless the connection asks for it), so the constraints in the DDL are actually enforced.
var ddl = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "EcOrderRemote.sql"));
await SqliteSchemaBootstrap.ApplyDdlAsync(connectionString, ddl);
Console.WriteLine(
    "[Server] Created the SQLite file DB (ec-order-remote.db) from the EcOrderRemote.sql DDL."
);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(url);

// Register the SQLite-dialect QuickER repositories as the backing of the remote surface (the endpoints
// resolve the remote surface and delegate to them).
builder.Services.AddGeneratedSqliteRepositories(connectionString);

var app = builder.Build();

// Expose the generated remote endpoints (POST /quicker/{entity}/{operation}).
// The RemoteAccess argument has no default: the endpoints can read, write and delete every row, so whether
// they demand authorization is stated explicitly at the call site. This local sample runs without
// authentication and says so with AllowAnonymous; a real deployment should configure authentication and
// pass RemoteAccess.RequireAuthorization instead.
// A 500 response keeps the server-side exception message to itself by default and sends a correlation id
// instead, which also appears in the server log. Pass exposeErrorDetails: app.Environment.IsDevelopment()
// to read the real message while developing; this sample keeps the safe default.
app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);

Console.WriteLine($"[Server] Listening on {url}{RemotePaths.DefaultPrefix}. Press Ctrl+C to exit.");
app.Run();
