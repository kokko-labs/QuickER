using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Runtime.EntityFrameworkCore;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core のランタイムの <see cref="SqliteBusyTimeoutInterceptor"/> が、開いた SQLite の接続の busy timeout を待ち時間へ
/// 合わせ、ほかのプロバイダの接続には触れないこと、生成の DI 登録がこれを足すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// 待ち時間は DbContext のコマンドタイムアウト（設定したとき）か、接続文字列の <c>Default Timeout</c>（設定しないとき）で、
/// <c>0</c> とミリ秒で表せない値は上限なしにする（QuickER 版の <c>SqlConnectionFactory</c> と同じ規則）。実際にロックを待つことは
/// <c>EfCoreSqliteBusyTimeoutRuntimeTests</c>（Integration）が確かめる。
/// </remarks>
public class SqliteBusyTimeoutInterceptorTests
{
    [Theory(
        DisplayName = "開いた SQLite の接続の busy timeout は、コマンドタイムアウトか Default Timeout に合わせる"
    )]
    [InlineData(null, null, false, 30_000)]
    [InlineData(7, null, false, 7_000)]
    [InlineData(null, 5, false, 5_000)]
    [InlineData(7, 5, false, 5_000)]
    [InlineData(0, null, false, int.MaxValue)]
    [InlineData(null, 0, false, int.MaxValue)]
    [InlineData(null, int.MaxValue / 1000 + 1, false, int.MaxValue)]
    [InlineData(7, null, true, 7_000)]
    [InlineData(null, 0, true, int.MaxValue)]
    public async Task OpenedSqliteConnection_GetsBusyTimeout(
        int? defaultTimeout,
        int? commandTimeout,
        bool openAsync,
        int expected
    )
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = ":memory:" };

        if (defaultTimeout is { } seconds)
        {
            builder.DefaultTimeout = seconds;
        }

        await using var context = CreateContext(options =>
            options
                .UseSqlite(
                    builder.ConnectionString,
                    sqlite =>
                    {
                        if (commandTimeout is { } timeout)
                        {
                            sqlite.CommandTimeout(timeout);
                        }
                    }
                )
                .IgnoreServiceProviderCacheLimit()
        );

        (await OpenAndReadBusyTimeoutAsync(context, openAsync)).Should().Be(expected);
    }

    /// <summary>
    /// <c>UseSqlite</c> のコマンドタイムアウトは EF Core が接続の <c>Default Timeout</c> にも写すので、DbContext の値と接続の値を
    /// 見分けられるのは <c>Database.SetCommandTimeout</c> で変えたときだけ（接続文字列の <c>Default Timeout</c> は 30 秒のまま）。
    /// </summary>
    [Theory(
        DisplayName = "SetCommandTimeout で変えたコマンドタイムアウトは接続の Default Timeout より優先する"
    )]
    [InlineData(4, 4_000)]
    [InlineData(0, int.MaxValue)]
    public async Task SetCommandTimeout_TakesPrecedenceOverDefaultTimeout(int seconds, int expected)
    {
        await using var context = CreateContext(options =>
            options.UseSqlite("Data Source=:memory:").IgnoreServiceProviderCacheLimit()
        );
        context.Database.SetCommandTimeout(seconds);

        (await OpenAndReadBusyTimeoutAsync(context, openAsync: true)).Should().Be(expected);
    }

    [Fact(
        DisplayName = "開いている接続でコマンドタイムアウトを変えても、その接続の busy timeout は開いたときのまま"
    )]
    public async Task SetCommandTimeout_OnOpenConnection_AppliesFromNextConnection()
    {
        await using var context = CreateContext(options =>
            options.UseSqlite("Data Source=:memory:").IgnoreServiceProviderCacheLimit()
        );

        (await OpenAndReadBusyTimeoutAsync(context, openAsync: false)).Should().Be(30_000);
        context.Database.SetCommandTimeout(6);
        (await ReadBusyTimeoutAsync(context))
            .Should()
            .Be(30_000, "値を合わせるのは接続を開いたとき");

        await context.Database.CloseConnectionAsync();
        (await OpenAndReadBusyTimeoutAsync(context, openAsync: false))
            .Should()
            .Be(6_000, "次に開く接続から効く");
    }

    [Fact(DisplayName = "SqliteConnection の派生の接続にも busy timeout を当てる")]
    public async Task DerivedSqliteConnection_GetsBusyTimeout()
    {
        await using var connection = new DerivedSqliteConnection(
            "Data Source=:memory:;Default Timeout=3"
        );
        await using var context = CreateContext(options =>
            options.UseSqlite(connection).IgnoreServiceProviderCacheLimit()
        );

        (await OpenAndReadBusyTimeoutAsync(context, openAsync: false)).Should().Be(3_000);
    }

    [Fact(DisplayName = "SQLite でない接続にはコマンドを作らない（同期・非同期）")]
    public async Task OtherConnection_IsLeftUntouched()
    {
        var connection = new CountingConnection();

        SqliteBusyTimeoutInterceptor.Instance.ConnectionOpened(connection, null!);
        await SqliteBusyTimeoutInterceptor.Instance.ConnectionOpenedAsync(
            connection,
            null!,
            TestContext.Current.CancellationToken
        );

        connection.CreatedCommands.Should().Be(0);
    }

    [Theory(
        DisplayName = "生成の EF Core の DI 登録は、configureDbContext の後に busy timeout のインターセプターを足す"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedRegistration_AddsInterceptorAfterConfigure(bool useRuntimePackages)
    {
        var provider = new SqliteProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            new ErDiagram
            {
                TargetDbms = provider.Name,
                Entities =
                {
                    new Entity
                    {
                        TableName = "items",
                        Columns =
                        {
                            new Column
                            {
                                Name = "item_id",
                                DataType = "INTEGER",
                                IsPrimaryKey = true,
                                IsNullable = false,
                            },
                        },
                    },
                },
            },
            new CodeGenerationOptions
            {
                RootNamespace = "Test.BusyTimeout",
                GenerateEfCoreRepositories = true,
                UseRuntimePackages = useRuntimePackages,
            }
        );
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        var code = string.Concat(result.Files.Select(file => file.Content))
            .ReplaceLineEndings("\n");

        code.Should()
            .Contain(
                """
                        services.AddDbContextFactory<QuickErDbContext>(options =>
                        {
                            configureDbContext(options);
                            options.AddInterceptors(SqliteBusyTimeoutInterceptor.Instance);
                        });
                """.ReplaceLineEndings("\n")
            );

        // インラインでは型を自分で出し、パッケージ参照では QuickER.Runtime.EntityFrameworkCore の型を使う
        code.Contains("public sealed class SqliteBusyTimeoutInterceptor", StringComparison.Ordinal)
            .Should()
            .Be(!useRuntimePackages);
    }

    /// <summary>インターセプターだけを足した素の DbContext を作る</summary>
    private static DbContext CreateContext(Action<DbContextOptionsBuilder> configure)
    {
        var builder = new DbContextOptionsBuilder();
        configure(builder);
        builder.AddInterceptors(SqliteBusyTimeoutInterceptor.Instance);
        return new DbContext(builder.Options);
    }

    private static async Task<int> OpenAndReadBusyTimeoutAsync(DbContext context, bool openAsync)
    {
        if (openAsync)
        {
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            context.Database.OpenConnection();
        }

        return await ReadBusyTimeoutAsync(context);
    }

    private static async Task<int> ReadBusyTimeoutAsync(DbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA busy_timeout";

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>SqliteConnection の派生（型の名前でなく継承をたどって判定することを確かめる）</summary>
    private sealed class DerivedSqliteConnection(string connectionString)
        : SqliteConnection(connectionString);

    /// <summary>コマンドを作った回数を数える、SQLite でない接続</summary>
    private sealed class CountingConnection : DbConnection
    {
        public int CreatedCommands { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close() { }

        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
        {
            CreatedCommands++;
            throw new InvalidOperationException("SQLite でない接続にコマンドを作った");
        }
    }
}
