using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>総当たりの生成器が扱う列の型の種類（方言ごとの型名は <see cref="SyncPlanFuzzDialect.TypeName"/>）</summary>
internal enum FuzzTypeKind
{
    /// <summary>32 ビット整数</summary>
    Int,

    /// <summary>64 ビット整数</summary>
    BigInt,

    /// <summary>可変長の文字列（長さつき）</summary>
    VarChar,

    /// <summary>固定小数点数（精度つき・スケール 2）</summary>
    Decimal,
}

/// <summary>
/// 同期計画の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）が方言ごとに差し替える部分。
/// </summary>
/// <remarks>
/// 図の DB への作成・データの投入・計算列の追加は、すべて方言の同期の実行器（<see cref="ISchemaSyncExecutor"/>）へ
/// スクリプトとして渡す（方言ごとの文の区切り・トランザクションの扱いを総当たりの側で持たない）。
/// </remarks>
internal abstract class SyncPlanFuzzDialect
{
    /// <summary>報告に出す方言名</summary>
    public abstract string Name { get; }

    /// <summary>方言のプロバイダ（取込・差分の能力・DDL・同期スクリプト・実行器）</summary>
    public abstract IDatabaseProvider Provider { get; }

    /// <summary>取込に使う接続文字列</summary>
    public abstract string ImportConnectionString { get; }

    /// <summary>同期の実行器へ渡す接続設定</summary>
    public abstract DbConnectionSettings Settings { get; }

    /// <summary>
    /// 参照アクション（CASCADE / SET NULL）を生成してよいか。SQL Server は循環・複数経路の連鎖を
    /// CREATE の時点で断る（QuickER の外の制約）ため、NO ACTION だけを使う。
    /// </summary>
    public virtual bool AllowCascade => true;

    /// <summary>
    /// 同じ列ペアから同じ親へ外部キーを 2 本張れるか。Oracle は ORA-02275 で断る（QuickER の外の制約）。
    /// </summary>
    public virtual bool AllowDuplicateForeignKeys => true;

    /// <summary>行数を数えるための接続を作る（開くのは呼び出し側）</summary>
    public abstract DbConnection CreateConnection();

    /// <summary>DB を空にする（種ごとに呼ぶ）</summary>
    public abstract Task ResetAsync(CancellationToken ct);

    /// <summary>識別子を引用する</summary>
    public abstract string Quote(string identifier);

    /// <summary>種類と長さ（精度）から方言の型名を作る</summary>
    public abstract string TypeName(FuzzTypeKind kind, int length);

    /// <summary>
    /// 整数の列 <paramref name="source"/> に 1 を足す計算列（生成列）を後から足す文。
    /// QuickER の DDL は式を持たないので、計算列は DB を作った後でこの文で足す。
    /// </summary>
    public abstract string AddComputedColumnSql(string table, string column, string source);

    /// <summary>文字列のリテラル</summary>
    public virtual string StringLiteral(string value) => $"'{value.Replace("'", "''")}'";

    /// <summary>セットアップのスクリプトを同期の実行器で流す（失敗は例外）</summary>
    public async Task ExecuteSetupAsync(string script, CancellationToken ct)
    {
        var result = await Provider
            .SyncExecutor.ExecuteAsync(Settings, script, ct)
            .ConfigureAwait(false);

        if (!result.Committed)
        {
            throw new InvalidOperationException(
                $"[{Name}] setup script failed: {result.Error}\n{script}"
            );
        }
    }
}

/// <summary>SQLite（実ファイル・Docker 不要）</summary>
internal sealed class SqliteSyncPlanFuzzDialect : SyncPlanFuzzDialect, IDisposable
{
    private SqliteTempDatabase _db = SqliteTempDatabase.Create();

    public override string Name => "sqlite";

    public override IDatabaseProvider Provider { get; } = new SqliteProvider();

    public override string ImportConnectionString => _db.ReadOnlyConnectionString;

    public override DbConnectionSettings Settings => new() { FilePath = _db.FilePath };

    public override DbConnection CreateConnection() =>
        new SqliteConnection(_db.ReadOnlyConnectionString);

    /// <summary>
    /// 種ごとに新しいファイルを作る（前の種の接続プール・ジャーナルを持ち越さない）。
    /// 同期の実行器は既存のファイルしか開かないので、ここで空のファイルを作っておく。
    /// </summary>
    public override async Task ResetAsync(CancellationToken ct)
    {
        _db.Dispose();
        _db = SqliteTempDatabase.Create();
        await using var connection = new SqliteConnection(_db.ReadWriteCreateConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
    }

    public override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "INTEGER",
            FuzzTypeKind.BigInt => "BIGINT",
            FuzzTypeKind.VarChar => $"VARCHAR({length})",
            FuzzTypeKind.Decimal => $"NUMERIC({length},2)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} INTEGER GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL;";

    public void Dispose() => _db.Dispose();
}

/// <summary>MySQL 8.4（Testcontainers）</summary>
internal sealed class MySqlSyncPlanFuzzDialect(MySqlContainerFixture fixture) : SyncPlanFuzzDialect
{
    public override string Name => "mysql";

    public override IDatabaseProvider Provider { get; } = new MySqlProvider();

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override DbConnection CreateConnection() =>
        new MySqlConnection(fixture.ConnectionString);

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"`{identifier.Replace("`", "``")}`";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "int",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"varchar({length})",
            FuzzTypeKind.Decimal => $"decimal({length},2)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} int GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL;";
}

/// <summary>SQL Server 2022（Testcontainers）</summary>
internal sealed class SqlServerSyncPlanFuzzDialect(SqlServerContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => "sqlserver";

    public override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override bool AllowCascade => false;

    public override DbConnection CreateConnection() => new SqlConnection(fixture.ConnectionString);

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "int",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"nvarchar({length})",
            FuzzTypeKind.Decimal => $"decimal({length},2)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override string StringLiteral(string value) => "N" + base.StringLiteral(value);

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD {Quote(column)} AS ({Quote(source)} + 1);";
}

/// <summary>PostgreSQL（Testcontainers）</summary>
internal sealed class PostgreSqlSyncPlanFuzzDialect(PostgreSqlContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => "postgresql";

    public override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

    public override DbConnection CreateConnection() =>
        new NpgsqlConnection(fixture.ConnectionString);

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "integer",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"varchar({length})",
            FuzzTypeKind.Decimal => $"numeric({length},2)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} integer GENERATED ALWAYS AS ({Quote(source)} + 1) STORED;";
}

/// <summary>Oracle（Testcontainers・既定では流さない＝<see cref="OracleSyncPlanFuzzTests"/> を参照）</summary>
internal sealed class OracleSyncPlanFuzzDialect(OracleContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => "oracle";

    public override bool AllowDuplicateForeignKeys => false;

    public override IDatabaseProvider Provider { get; } = new OracleProvider();

    public override DbConnection CreateConnection() =>
        new OracleConnection(fixture.ConnectionString);

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "NUMBER(10)",
            FuzzTypeKind.BigInt => "NUMBER(19)",
            FuzzTypeKind.VarChar => $"NVARCHAR2({length})",
            FuzzTypeKind.Decimal => $"NUMBER({length},2)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD ({Quote(column)} NUMBER(10) GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL);";
}
