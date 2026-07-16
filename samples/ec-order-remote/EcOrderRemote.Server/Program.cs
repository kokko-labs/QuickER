using System.Text;
using EcOrderRemoteSample.Generated;
using Microsoft.Data.Sqlite;

// QuickER の「リモートサービス生成（GenerateRemoteServices）」で図から生成したサーバー実装を、
// 実際の SQLite ファイル DB に対して HTTP + JSON で公開するサンプルサーバー。
// リモート面（I{Entity}RemoteRepository）の実体は SQLite 方言の QuickER 版 Repository（AddGeneratedSqliteRepositories）で、
// 生成された MapGeneratedRemoteEndpoints が各操作を POST /quicker/{エンティティ}/{操作} として公開する。
// クライアント（EcOrderRemote.Client）は HTTP 越しにこのサーバーを呼び出す。

// 日本語出力の文字化けを避けるため標準出力を UTF-8 にする（リダイレクト時の失敗は無視）
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // 出力がリダイレクトされている場合などは設定できないが、致命的ではないため無視する
}

// 待ち受け URL は第 1 引数で差し替え可能（クライアントと同じ値を渡すこと）。既定はローカルループバックの固定ポート。
var url = args.FirstOrDefault() ?? "http://127.0.0.1:5210";

// DB ファイルは実行ファイルと同じ場所（bin 配下）に置く。作業ディレクトリ（リポジトリ直下等）を汚さないため。
var dbFilePath = Path.Combine(AppContext.BaseDirectory, "ec-order-remote.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbFilePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
}.ConnectionString;

// 冪等に起動できるよう、既存の DB ファイルを削除してから DDL で作り直す
if (File.Exists(dbFilePath))
{
    // 接続プールがファイルを掴んでいると削除できないため、先にプールを解放する
    SqliteConnection.ClearAllPools();
    File.Delete(dbFilePath);
}

await CreateSchemaAsync(connectionString);
Console.WriteLine(
    "[サーバー] EcOrderRemote.sql の DDL で SQLite ファイル DB（ec-order-remote.db）を作成しました。"
);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(url);

// リモート面の実体として SQLite 方言の QuickER 版 Repository 群を登録する（エンドポイントがリモート面を解決して委譲する）
builder.Services.AddGeneratedSqliteRepositories(connectionString);

var app = builder.Build();

// 生成されたリモートエンドポイント（POST /quicker/{エンティティ}/{操作}）を公開する
app.MapGeneratedRemoteEndpoints();

Console.WriteLine($"[サーバー] {url}/quicker で待機しています。Ctrl+C で終了します。");
app.Run();

// DDL（EcOrderRemote.sql）を読み込み、SQLite ファイル DB へ適用してスキーマを作成する。
static async Task CreateSchemaAsync(string connectionString)
{
    var ddlPath = Path.Combine(AppContext.BaseDirectory, "EcOrderRemote.sql");
    var ddl = await File.ReadAllTextAsync(ddlPath);

    await using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();

    // 生成 DDL の外部キー制約を有効化する（SQLite は既定で FK 制約を無効にしている）
    await using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
    }

    // Microsoft.Data.Sqlite は 1 回の ExecuteNonQuery で複数文（セミコロン区切り）をまとめて実行できる
    await using var command = conn.CreateCommand();
    command.CommandText = ddl;
    await command.ExecuteNonQueryAsync();
}
