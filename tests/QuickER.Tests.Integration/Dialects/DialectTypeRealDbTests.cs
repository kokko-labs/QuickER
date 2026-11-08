using System.Data.Common;
using System.Globalization;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
using QuickER.Tests.Provider;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 方言の型の表（<see cref="DialectTypeRows"/>）の行を、方言の実 DB で確かめる共通基底（手順は
/// <see cref="DialectTypeRealDbRows"/>）
/// </summary>
/// <remarks>
/// <para>
/// 手順 1 つごとに DB を空にし、1 列だけの表 <c>type_probe</c> を作る（MySQL の <c>varbinary</c> は行全体の上限が効くので、
/// ほかの列を並べない）。取込はプロバイダの取込（<see cref="IDatabaseProvider.SchemaImporter"/>）を通す。
/// </para>
/// <para>
/// 1 つの手順の失敗で止めず、方言の全手順を流してから、失敗した手順を行の Id で名指しする。
/// </para>
/// </remarks>
public abstract class DialectTypeRealDbTestsBase
{
    /// <summary>派生クラスが受け持つ方言の軸の名前（値は <see cref="DialectNames"/>）</summary>
    public const string DialectAxis = "dialect";

    /// <summary>手順が作る表の名前</summary>
    private const string ProbeTable = "type_probe";

    /// <summary>ドライバの CLR 型を C# の名前へ直す（マッパーの <c>TypeName</c> と突き合わせる）</summary>
    internal static readonly IReadOnlyDictionary<Type, string> CSharpNames = new Dictionary<
        Type,
        string
    >
    {
        [typeof(bool)] = "bool",
        [typeof(sbyte)] = "sbyte",
        [typeof(byte)] = "byte",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(byte[])] = "byte[]",
        [typeof(DateTime)] = "DateTime",
        [typeof(DateTimeOffset)] = "DateTimeOffset",
        [typeof(TimeSpan)] = "TimeSpan",
    };

    private static readonly MethodInfo GetFieldValue = typeof(DbDataReader).GetMethod(
        nameof(DbDataReader.GetFieldValue)
    )!;

    /// <summary>方言のプロバイダ（DB には触れない）</summary>
    protected abstract IDatabaseProvider Provider { get; }

    /// <summary>方言の名前（プロバイダの名前。DB に触れないので、Docker なしで読める）</summary>
    internal string DialectName => Provider.Name;

    /// <summary>DB が使えるか（Docker の無い環境では false）</summary>
    protected abstract bool IsAvailable { get; }

    /// <summary>使えないときの理由</summary>
    protected virtual string UnavailableReason => string.Empty;

    /// <summary>DB が DDL を断ったときの例外の型（ドライバの例外）</summary>
    protected abstract Type RejectionException { get; }

    /// <summary>取込の接続文字列</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>DB を空にする</summary>
    protected abstract Task ResetAsync(CancellationToken ct);

    /// <summary>SQL の文を 1 つ流す</summary>
    protected abstract Task ExecuteAsync(string sql, CancellationToken ct);

    /// <summary>開いた接続</summary>
    protected abstract Task<DbConnection> OpenAsync(CancellationToken ct);

    /// <summary>EF Core の手順のプロバイダ（生成 EF Core のコードの利用者が <c>AddGeneratedEfCoreRepositories</c> へ渡すもの）</summary>
    protected abstract void ConfigureEfCore(DbContextOptionsBuilder options);

    /// <summary>EF Core の手順の表のキーの列の型</summary>
    protected virtual string KeyColumnType => "int";

    /// <summary>引用せずに作った表・列の DB 上の名前（EF Core は引用して名前を書くので、DB が畳んだ綴りで指す）</summary>
    protected virtual string StoreName(string name) => name;

    [Fact(
        DisplayName = "[Integration] 方言の型の表: 上限の対は DB の上限と一致し、マッパーの型はドライバの実測の CLR 型と一致する"
    )]
    public async Task Probes_MatchTheDatabase()
    {
        Assert.SkipUnless(IsAvailable, UnavailableReason);

        var ct = TestContext.Current.CancellationToken;
        var probes = DialectTypeRealDbRows.For(DialectName);
        var failures = new List<string>();

        probes.Should().NotBeEmpty($"{DialectName} の手順がある");

        foreach (var probe in probes)
        {
            try
            {
                failures.AddRange(
                    (await RunAsync(probe, ct)).Select(problem => $"{probe.Describe()}: {problem}")
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{probe.Describe()}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        failures
            .Should()
            .BeEmpty(
                $"{DialectName} の実 DB の手順 {probes.Count} 件のうち {failures.Count} 件が食い違う"
            );
    }

    /// <summary>手順を 1 つ流して、食い違いを並べる（無ければ空）</summary>
    private async Task<IReadOnlyList<string>> RunAsync(
        DialectTypeRealDbProbe probe,
        CancellationToken ct
    )
    {
        var adapter = DialectTypeAxis.Get(DialectName);
        var row = DialectTypeRows.Table[probe.RowId].Input;
        var spelling = probe.Spelling ?? RowSpelling(row, adapter);
        var problems = new List<string>();

        await ResetAsync(ct);

        switch (probe.Kind)
        {
            case DialectTypeRealDbProbeKinds.AtLimit:
            {
                await ExecuteAsync($"CREATE TABLE {ProbeTable} (c {spelling})", ct);
                Expect(
                    problems,
                    "取り込み直した表記",
                    Provider.SyncCapabilities.NormalizeTypeNotation(spelling),
                    await ImportedTypeAsync(ct)
                );
                break;
            }

            case DialectTypeRealDbProbeKinds.OverLimit when probe.Imported is null:
            {
                var rejected = await RejectedAsync($"CREATE TABLE {ProbeTable} (c {spelling})", ct);

                if (rejected is not null)
                {
                    problems.Add(rejected);
                }

                break;
            }

            case DialectTypeRealDbProbeKinds.OverLimit:
            {
                await ExecuteAsync($"CREATE TABLE {ProbeTable} (c {spelling})", ct);
                Expect(
                    problems,
                    $"{spelling} を DB が丸めて取り込ませた表記",
                    probe.Imported,
                    await ImportedTypeAsync(ct)
                );
                break;
            }

            case DialectTypeRealDbProbeKinds.Driver:
            {
                problems.AddRange(await DriverProblemsAsync(probe, adapter, spelling, ct));
                break;
            }

            case DialectTypeRealDbProbeKinds.EfCore:
            {
                problems.AddRange(await EfCoreProblemsAsync(probe, adapter, spelling, ct));
                break;
            }

            default:
                throw new InvalidOperationException($"知らない手順の種類 {probe.Kind}");
        }

        return KnownGapProblems(probe, problems);
    }

    /// <summary>
    /// 読み書きできないと分かっている組（<see cref="DialectTypeRealDbProbe.GapSymptom"/>）は、今の症状が出ていれば食い違いに
    /// しない。症状が出なくなったら（直った・別の壊れ方になった）食い違いにして、手順の書き換えを促す
    /// </summary>
    private static IReadOnlyList<string> KnownGapProblems(
        DialectTypeRealDbProbe probe,
        IReadOnlyList<string> problems
    )
    {
        if (probe.GapSymptom is not { } symptom)
        {
            return problems;
        }

        if (problems.Any(problem => problem.Contains(symptom, StringComparison.Ordinal)))
        {
            return [];
        }

        var actual = problems.Count == 0 ? "食い違いなし" : string.Join(" / ", problems);

        return
        [
            $"読み書きできない組として残した手順の症状「{symptom}」が出ない（直ったなら GapReason と GapSymptom を外す。実際：{actual}）",
        ];
    }

    /// <summary>
    /// EF Core の手順：表記の列に値を入れ、生成 DbContext と同じ構成のモデル（<see cref="EfCoreProbeContext{T}"/>）で
    /// マッパーの型として読み、同じ型の値を書いて読み戻す
    /// </summary>
    private async Task<IReadOnlyList<string>> EfCoreProblemsAsync(
        DialectTypeRealDbProbe probe,
        DialectTypeAdapter adapter,
        string spelling,
        CancellationToken ct
    )
    {
        var mapped = adapter.Map(spelling);
        var type = CSharpNames.SingleOrDefault(pair => pair.Value == mapped.TypeName).Key;

        if (type is null || mapped.IsFallbackType)
        {
            return
            [
                $"Map({spelling}) の C# 型 {mapped.TypeName} を EF Core の手順で読めない（代替の型か、表に無い型）",
            ];
        }

        await ExecuteAsync(
            $"CREATE TABLE {ProbeTable} (id {KeyColumnType} NOT NULL PRIMARY KEY, c {spelling} NOT NULL)",
            ct
        );
        await ExecuteAsync($"INSERT INTO {ProbeTable} (id, c) VALUES (1, {probe.Value})", ct);

        var run = typeof(DialectTypeRealDbTestsBase)
            .GetMethod(
                nameof(EfCoreReadWriteAsync),
                BindingFlags.NonPublic | BindingFlags.Instance
            )!
            .MakeGenericMethod(type);

        return await (Task<IReadOnlyList<string>>)run.Invoke(this, [probe, ct])!;
    }

    /// <summary>EF Core で入れた行を読み、書いて読み戻す（例外は食い違いの文にする）</summary>
    private async Task<IReadOnlyList<string>> EfCoreReadWriteAsync<T>(
        DialectTypeRealDbProbe probe,
        CancellationToken ct
    )
    {
        var problems = new List<string>();
        var builder = new DbContextOptionsBuilder();
        ConfigureEfCore(builder);
        var options = builder.Options;

        try
        {
            await using var context = new EfCoreProbeContext<T>(options, StoreName);
            var read = (await context.Rows.AsNoTracking().SingleAsync(ct)).Value;
            ExpectValue(problems, "EF Core で読んだ値", probe.ReadBack, read);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Add($"EF Core で読めない（{Describe(ex)}）");
        }

        try
        {
            await using (var context = new EfCoreProbeContext<T>(options, StoreName))
            {
                context.Rows.Add(new EfCoreProbeRow<T> { Id = 2, Value = (T)probe.Write! });
                await context.SaveChangesAsync(ct);
            }

            await using (var context = new EfCoreProbeContext<T>(options, StoreName))
            {
                var back = (
                    await context.Rows.AsNoTracking().SingleAsync(row => row.Id == 2, ct)
                ).Value;
                ExpectValue(problems, "EF Core で書いて読み戻した値", probe.Write, back);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Add($"EF Core で書けない（{Describe(ex)}）");
        }

        return problems;
    }

    /// <summary>例外の型と 1 行目の文言（内側の例外があれば添える）</summary>
    private static string Describe(Exception ex)
    {
        var text = $"{ex.GetType().Name}: {FirstLine(ex.Message)}";

        return ex.InnerException is { } inner
            ? $"{text} ← {inner.GetType().Name}: {FirstLine(inner.Message)}"
            : text;
    }

    private static string FirstLine(string message) => message.Split('\n', 2)[0].TrimEnd('\r');

    private static void ExpectValue(
        List<string> problems,
        string what,
        object? expected,
        object? actual
    )
    {
        if (!Equals(expected, actual))
        {
            problems.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{what}: 期待 {Show(expected)}・実際 {Show(actual)}"
                )
            );
        }
    }

    private static string Show(object? value) =>
        value switch
        {
            null => "(null)",
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>マッパーの行の手順：表記の列に値を入れ、取込の表記・ドライバの型・読める値・マッパーの型を突き合わせる</summary>
    private async Task<IReadOnlyList<string>> DriverProblemsAsync(
        DialectTypeRealDbProbe probe,
        DialectTypeAdapter adapter,
        string spelling,
        CancellationToken ct
    )
    {
        var problems = new List<string>();

        await ExecuteAsync(
            $"CREATE TABLE {ProbeTable} (c {spelling} {(probe.InsertsNull ? "NULL" : "NOT NULL")})",
            ct
        );
        await ExecuteAsync($"INSERT INTO {ProbeTable} (c) VALUES ({probe.Value})", ct);

        var imported = await ImportedTypeAsync(ct);

        if (probe.Imported is not null)
        {
            Expect(problems, "取り込んだ表記", probe.Imported, imported);
        }

        if (probe.Driver is { } driver)
        {
            problems.AddRange(await DriverReadProblemsAsync(probe, driver, ct));
        }

        // 生成コードは行の表記からも取り込み直した表記からもマッパーの型で読む（どちらもドライバと同じ型・代替の型でない）
        var expectedType = probe.MapperType ?? CSharpNames[probe.Driver!];

        foreach (var text in new[] { spelling, imported }.Distinct(StringComparer.Ordinal))
        {
            var mapped = adapter.Map(text);

            Expect(problems, $"Map({text}) の C# 型", expectedType, mapped.TypeName);

            if (mapped.IsFallbackType)
            {
                problems.Add($"Map({text}) が代替の型");
            }
        }

        return problems;
    }

    /// <summary>入れた行を読み、ドライバの <c>GetFieldType</c> と、その型で読める値を確かめる</summary>
    private async Task<IReadOnlyList<string>> DriverReadProblemsAsync(
        DialectTypeRealDbProbe probe,
        Type driver,
        CancellationToken ct
    )
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT c FROM {ProbeTable}";
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return ["入れた行を読めない"];
        }

        var problems = new List<string>();

        Expect(problems, "ドライバの CLR 型", driver.FullName, reader.GetFieldType(0).FullName);

        if (!probe.InsertsNull)
        {
            problems.AddRange(ReadProblems(probe, reader, driver));
        }

        return problems;
    }

    /// <summary>ドライバの型で読み（生成コードの速い読み取りと同じ型の getter）、値の期待を確かめる</summary>
    private static IEnumerable<string> ReadProblems(
        DialectTypeRealDbProbe probe,
        DbDataReader reader,
        Type driver
    )
    {
        object? value;

        try
        {
            value = GetFieldValue.MakeGenericMethod(driver).Invoke(reader, [0]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return
            [
                $"{CSharpNames[driver]} で読めない（{ex.InnerException.GetType().Name}: {ex.InnerException.Message}）",
            ];
        }

        var problems = new List<string>();

        if (probe.ReadBack is { } expected && !Equals(expected, value))
        {
            problems.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"読んだ値: 期待 {expected}・実際 {value}"
                )
            );
        }

        if (probe.NarrowingLoses && value is double stored && (double)(float)stored == stored)
        {
            problems.Add("入れた値が単精度でも変わらない（倍精度を要する値でない）");
        }

        return problems;
    }

    /// <summary>DDL を流し、DB がドライバの例外で断ることを確かめる（断らなければ食い違いの文）</summary>
    private async Task<string?> RejectedAsync(string sql, CancellationToken ct)
    {
        try
        {
            await ExecuteAsync(sql, ct);
        }
        catch (Exception ex) when (RejectionException.IsInstanceOfType(ex))
        {
            return null;
        }

        return $"DB が断らなかった（{sql}）";
    }

    /// <summary>手順の表を取り込み、ただ 1 つの列の型の表記を返す</summary>
    private async Task<string> ImportedTypeAsync(CancellationToken ct)
    {
        var import = await Provider.SchemaImporter.ImportAsync(ConnectionString, 0, ct);
        var entity = import
            .Entities.Should()
            .ContainSingle(e =>
                string.Equals(e.TableName, ProbeTable, StringComparison.OrdinalIgnoreCase)
            )
            .Subject;

        return entity.Columns.Should().ContainSingle().Subject.DataType;
    }

    /// <summary>行の出力（書き出しの行は TryFormat・パラメータの書き出しの行は TryFormatParameterType・マッパーの行は入力）</summary>
    private static string RowSpelling(DialectTypeCase row, DialectTypeAdapter adapter) =>
        row.Facet switch
        {
            DialectTypeFacets.Format
                when adapter.Catalog.TryFormat((CanonicalType)row.Input!, out var text) => text,
            DialectTypeFacets.FormatParam
                when adapter.Catalog.TryFormatParameterType(
                    (CanonicalType)row.Input!,
                    out var text
                ) => text,
            DialectTypeFacets.Map => (string)row.Input!,
            _ => throw new InvalidOperationException(
                $"行 {row.DescribeInput()}（{row.Facet}）から DDL の表記を作れない（表記を手順に書く）"
            ),
        };

    private static void Expect(List<string> problems, string what, string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            problems.Add($"{what}: 期待 {expected ?? "(null)"}・実際 {actual ?? "(null)"}");
        }
    }
}

/// <summary>EF Core の手順の行（キーとマッパーの型の値 1 つ）</summary>
internal sealed class EfCoreProbeRow<T>
{
    public int Id { get; set; }

    public T Value { get; set; } = default!;
}

/// <summary>
/// EF Core の手順のモデル。生成 DbContext（<c>_08_EfCore.scriban</c>）と同じく、列名だけを指定し、列の型
/// （<c>HasColumnType</c>）・精度・Unicode を指定しない（だからプロバイダがマッパーの型から選ぶ既定の対応で読み書きする）
/// </summary>
internal sealed class EfCoreProbeContext<T>(
    DbContextOptions options,
    Func<string, string> storeName
) : DbContext(options)
{
    public DbSet<EfCoreProbeRow<T>> Rows => Set<EfCoreProbeRow<T>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<EfCoreProbeRow<T>>();
        row.ToTable(storeName("type_probe"));
        row.HasKey(r => r.Id);
        row.Property(r => r.Id).HasColumnName(storeName("id")).ValueGeneratedNever();
        row.Property(r => r.Value).HasColumnName(storeName("c"));
    }
}

/// <summary>SQLite（実ファイル・Docker 不要）の方言の型の表の実 DB の段</summary>
[Trait("Category", "Integration")]
[MatrixAxisValue(DialectTypeRealDbTestsBase.DialectAxis, DialectNames.Sqlite)]
public sealed class SqliteDialectTypeRealDbTests : DialectTypeRealDbTestsBase, IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    protected override IDatabaseProvider Provider { get; } = new SqliteProvider();

    protected override string KeyColumnType => "INTEGER";

    protected override void ConfigureEfCore(DbContextOptionsBuilder options) =>
        options.UseSqlite(_db.ReadWriteConnectionString).IgnoreServiceProviderCacheLimit();

    protected override bool IsAvailable => true;

    protected override Type RejectionException => typeof(SqliteException);

    protected override string ConnectionString => _db.ReadWriteConnectionString;

    protected override Task ResetAsync(CancellationToken ct) => _db.ResetSchemaAsync(ct);

    protected override Task ExecuteAsync(string sql, CancellationToken ct) =>
        _db.ApplyDdlAsync(sql, ct);

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_db.ReadWriteConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>SQL Server（Testcontainers）の方言の型の表の実 DB の段</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
[MatrixAxisValue(DialectTypeRealDbTestsBase.DialectAxis, DialectNames.SqlServer)]
public sealed class SqlServerDialectTypeRealDbTests(SqlServerContainerFixture fixture)
    : DialectTypeRealDbTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    protected override void ConfigureEfCore(DbContextOptionsBuilder options) =>
        options.UseSqlServer(fixture.ConnectionString).IgnoreServiceProviderCacheLimit();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Type RejectionException => typeof(SqlException);

    protected override string ConnectionString => fixture.ConnectionString;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ExecuteAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct) =>
        await fixture.OpenConnectionAsync(ct);
}

/// <summary>PostgreSQL（Testcontainers）の方言の型の表の実 DB の段</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
[MatrixAxisValue(DialectTypeRealDbTestsBase.DialectAxis, DialectNames.PostgreSql)]
public sealed class PostgreSqlDialectTypeRealDbTests(PostgreSqlContainerFixture fixture)
    : DialectTypeRealDbTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

    protected override void ConfigureEfCore(DbContextOptionsBuilder options) =>
        options.UseNpgsql(fixture.ConnectionString).IgnoreServiceProviderCacheLimit();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Type RejectionException => typeof(PostgresException);

    protected override string ConnectionString => fixture.ConnectionString;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ExecuteAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct) =>
        await fixture.OpenConnectionAsync(ct);
}

/// <summary>MySQL（Testcontainers）の方言の型の表の実 DB の段</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
[MatrixAxisValue(DialectTypeRealDbTestsBase.DialectAxis, DialectNames.MySql)]
public sealed class MySqlDialectTypeRealDbTests(MySqlContainerFixture fixture)
    : DialectTypeRealDbTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new MySqlProvider();

    /// <summary>MySql.EntityFrameworkCore（ドライバは MySql.Data）。Integration はこのプロバイダだけを持つ（Pomelo は確かめていない）</summary>
    protected override void ConfigureEfCore(DbContextOptionsBuilder options) =>
        options.UseMySQL(fixture.ConnectionString).IgnoreServiceProviderCacheLimit();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Type RejectionException => typeof(MySqlException);

    protected override string ConnectionString => fixture.ConnectionString;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ExecuteAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct) =>
        await fixture.OpenConnectionAsync(ct);
}

/// <summary>Oracle（Testcontainers）の方言の型の表の実 DB の段</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
[MatrixAxisValue(DialectTypeRealDbTestsBase.DialectAxis, DialectNames.Oracle)]
public sealed class OracleDialectTypeRealDbTests(OracleContainerFixture fixture)
    : DialectTypeRealDbTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new OracleProvider();

    protected override string KeyColumnType => "NUMBER(10)";

    /// <summary>引用しない名前は大文字へ畳まれる</summary>
    protected override string StoreName(string name) => name.ToUpperInvariant();

    protected override void ConfigureEfCore(DbContextOptionsBuilder options) =>
        options.UseOracle(fixture.ConnectionString).IgnoreServiceProviderCacheLimit();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Type RejectionException => typeof(OracleException);

    protected override string ConnectionString => fixture.ConnectionString;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ExecuteAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct) =>
        await fixture.OpenConnectionAsync(ct);
}
