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
    // The connection pool may hold the file open and block deletion, so release the pools first.
    SqliteConnection.ClearAllPools();
    File.Delete(dbFilePath);
}

await CreateSchemaAsync(connectionString);
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
app.MapGeneratedRemoteEndpoints();

Console.WriteLine($"[Server] Listening on {url}/quicker. Press Ctrl+C to exit.");
app.Run();

// Read the DDL (EcOrderRemote.sql) and apply it to the SQLite file DB to create the schema.
static async Task CreateSchemaAsync(string connectionString)
{
    var ddlPath = Path.Combine(AppContext.BaseDirectory, "EcOrderRemote.sql");
    var ddl = await File.ReadAllTextAsync(ddlPath);

    await using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();

    // Enable the foreign key constraints in the generated DDL (SQLite disables FK enforcement by default).
    await using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
    }

    // Microsoft.Data.Sqlite can execute multiple semicolon-separated statements in a single ExecuteNonQuery.
    await using var command = conn.CreateCommand();
    command.CommandText = ddl;
    await command.ExecuteNonQueryAsync();
}
