using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using QuickER.Dialects;
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

    /// <summary>日時（長さは秒の小数部の精度。方言の既定の精度は精度を書かない綴り）</summary>
    DateTime,

    /// <summary>固定長の文字列（長さつき）</summary>
    FixedChar,

    /// <summary>可変長のバイト列（長さつき。長さを持たない方言は長さを書かない）</summary>
    Binary,
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
    /// <summary>報告に出す方言名（<see cref="DialectNames"/> の値）</summary>
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

    /// <summary>
    /// 同じ列の並びに一意制約を 2 つ張れるか。Oracle は ORA-02261 で断る（QuickER の外の制約）。
    /// </summary>
    public virtual bool AllowDuplicateUniqueConstraints => true;

    /// <summary>
    /// 同じ列の並びの制約（一意制約・外部キー）を名前で見分けるか。SQLite は取込が制約の名前を持ち帰らず、製品は名前の無い制約を
    /// 同じ列集合の DB の制約で満たされるとみなす（仕様）ので、目標の図の変更で同じ列の並びの制約を重ねない（重ねた 2 つは 1 つと
    /// 区別できない）
    /// </summary>
    public virtual bool DistinguishesSameSetConstraints => true;

    /// <summary>
    /// データのある日時の列の小数秒の精度を下げられるか。Oracle は ORA-30082（列を空にしないと精度を下げられない・DB の事実）で
    /// 断るので、生成器は行のある表の日時の精度を下げない
    /// </summary>
    public virtual bool AllowsDateTimePrecisionDecreaseWithData => true;

    /// <summary>日時の型の既定の精度（<see cref="TypeName"/> は、この精度なら精度を書かない綴りを返す）</summary>
    public abstract int DefaultDateTimePrecision { get; }

    /// <summary>
    /// 方言の上限ちょうどの長さを持つ型の種類と長さ（上限が無い方言は null）。DB が型として受け付ける上限ちょうどの型を、
    /// 型の変更の行き先に混ぜる（取込の表記の正規化と型の比較が上限の表記でも閉じているかを見る）
    /// </summary>
    public abstract (FuzzTypeKind Kind, int Length)? LimitType { get; }

    /// <summary>
    /// 同じ型の別の綴り（既定の精度を明示した綴り。無ければ null）。差分は両側を正規化して比べるので、
    /// 綴りだけを変えた目標の図は差分を出してはいけない
    /// </summary>
    public virtual string? RespelledTypeName(FuzzTypeKind kind, int length) => null;

    /// <summary>
    /// 識別子の引用の中で特別な扱いが要る文字（識別子の引用符・文字列の引用符）。列の名前に混ぜて、計画の文と
    /// 動的 SQL が名前を正しく引用しているかを見る
    /// </summary>
    public abstract char SymbolCharacter { get; }

    /// <summary>
    /// 既定と別のスキーマ（同じ名前の表を置いて、名前の逆引きが表の所有者で絞られているかを見る。使わない方言は null）
    /// </summary>
    public virtual string? OtherSchema => null;

    /// <summary>表の名前を引用する（スキーマで修飾した名前は方言の規則で分けて引用する）</summary>
    public virtual string QuoteTable(string table) => Quote(table);

    /// <summary>日時のリテラル（行の番号から作る）</summary>
    public virtual string DateTimeLiteral(int row) =>
        $"'2024-01-{row.ToString("00", CultureInfo.InvariantCulture)} 10:00:00'";

    /// <summary>バイト列のリテラル（16 進の文字列から作る）</summary>
    public abstract string BinaryLiteral(string hex);

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

    public override string Name => DialectNames.Sqlite;

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
            FuzzTypeKind.DateTime => "DATETIME",
            FuzzTypeKind.FixedChar => $"CHAR({length})",
            FuzzTypeKind.Binary => "BLOB",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override bool DistinguishesSameSetConstraints => false;

    /// <summary>SQLite は宣言の型をそのまま覚え、精度を持たない（既定の精度の別の綴りも無い）</summary>
    public override int DefaultDateTimePrecision => 0;

    /// <summary>SQLite は型の長さに上限を持たない（宣言は検査されない）</summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => null;

    public override char SymbolCharacter => '"';

    public override string BinaryLiteral(string hex) => $"X'{hex}'";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} INTEGER GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL;";

    public void Dispose() => _db.Dispose();
}

/// <summary>MySQL 8.4（Testcontainers）</summary>
internal sealed class MySqlSyncPlanFuzzDialect(MySqlContainerFixture fixture) : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.MySql;

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
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "datetime",
            FuzzTypeKind.DateTime => $"datetime({length})",
            FuzzTypeKind.FixedChar => $"char({length})",
            FuzzTypeKind.Binary => $"varbinary({length})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 0;

    /// <summary>
    /// <c>char(255)</c>（固定長の文字列の上限）。可変長の文字列の上限（<c>varchar(16383)</c>）は主キーの列と合わせると
    /// 行の上限 65,535 バイトを超えるので、主キーの列を必ず持つ総当たりの表では作れない（DB の事実）
    /// </summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.FixedChar, 255);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "datetime(0)" : null;

    public override char SymbolCharacter => '`';

    public override string BinaryLiteral(string hex) => $"X'{hex}'";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} int GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL;";
}

/// <summary>SQL Server 2022（Testcontainers）</summary>
internal sealed class SqlServerSyncPlanFuzzDialect(SqlServerContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.SqlServer;

    public override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override bool AllowCascade => false;

    public override DbConnection CreateConnection() => new SqlConnection(fixture.ConnectionString);

    /// <summary>表を全部消し、別のスキーマ（<see cref="OtherSchema"/>）が無ければ作る（スキーマは消さずに使い回す）</summary>
    public override async Task ResetAsync(CancellationToken ct)
    {
        await fixture.ResetSchemaAsync(ct).ConfigureAwait(false);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF SCHEMA_ID(N'{OtherSchema}') IS NULL EXEC(N'CREATE SCHEMA {Quote(OtherSchema)}');";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public override string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    public override string OtherSchema => "fz";

    public override string QuoteTable(string table) =>
        SchemaQualifiedName.Split(table) is ({ } schema, var name)
            ? $"{Quote(schema)}.{Quote(name)}"
            : Quote(table);

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "int",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"nvarchar({length})",
            FuzzTypeKind.Decimal => $"decimal({length},2)",
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "datetime2",
            FuzzTypeKind.DateTime => $"datetime2({length})",
            FuzzTypeKind.FixedChar => $"nchar({length})",
            FuzzTypeKind.Binary => $"varbinary({length})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 7;

    /// <summary>
    /// <c>nvarchar(4000)</c>（可変長の Unicode 文字列の上限）。固定長の <c>nchar(4000)</c> は 8,000 バイトの固定長の列で、
    /// ほかの列と合わせると行の上限 8,060 バイトを CREATE の時点で超えるので使わない（DB の事実）
    /// </summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.VarChar, 4000);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "datetime2(7)" : null;

    public override char SymbolCharacter => ']';

    public override string BinaryLiteral(string hex) => $"0x{hex}";

    public override string StringLiteral(string value) => "N" + base.StringLiteral(value);

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {QuoteTable(table)} ADD {Quote(column)} AS ({Quote(source)} + 1);";
}

/// <summary>PostgreSQL（Testcontainers）</summary>
internal sealed class PostgreSqlSyncPlanFuzzDialect(PostgreSqlContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.PostgreSql;

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
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "timestamp",
            FuzzTypeKind.DateTime => $"timestamp({length})",
            FuzzTypeKind.FixedChar => $"char({length})",
            FuzzTypeKind.Binary => "bytea",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 6;

    /// <summary><c>varchar(10485760)</c>（長さの上限）</summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.VarChar, 10485760);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "timestamp(6)" : null;

    public override char SymbolCharacter => '"';

    public override string BinaryLiteral(string hex) => $"decode('{hex}', 'hex')";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} integer GENERATED ALWAYS AS ({Quote(source)} + 1) STORED;";
}

/// <summary>Oracle（Testcontainers・既定では流さない＝<see cref="OracleSyncPlanFuzzTests"/> を参照）</summary>
internal sealed class OracleSyncPlanFuzzDialect(OracleContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.Oracle;

    public override bool AllowDuplicateForeignKeys => false;

    public override bool AllowDuplicateUniqueConstraints => false;

    public override bool AllowsDateTimePrecisionDecreaseWithData => false;

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
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "TIMESTAMP",
            FuzzTypeKind.DateTime => $"TIMESTAMP({length})",
            // 長さの単位語つき（取込は char_used = 'C' の列にだけ単位語を書く）
            FuzzTypeKind.FixedChar => $"CHAR({length} CHAR)",
            FuzzTypeKind.Binary => $"RAW({length})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 6;

    /// <summary><c>NVARCHAR2(2000)</c>（<c>MAX_STRING_SIZE = STANDARD</c> の上限）</summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.VarChar, 2000);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "TIMESTAMP(6)" : null;

    public override char SymbolCharacter => '\'';

    public override string DateTimeLiteral(int row) => "TIMESTAMP " + base.DateTimeLiteral(row);

    public override string BinaryLiteral(string hex) => $"HEXTORAW('{hex}')";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD ({Quote(column)} NUMBER(10) GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL);";
}
