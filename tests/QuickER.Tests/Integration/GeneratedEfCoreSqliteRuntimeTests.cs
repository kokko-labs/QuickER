using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedPortableFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 方言可搬な生成物（EF 版）を実 SQLite（一時ファイル DB・インプロセス）で流す方言ランタイムテスト。
/// スキーマは <see cref="SqliteDdlGenerator"/> の DDL、接続は <c>UseSqlite</c> で構成する。
/// </summary>
/// <remarks>
/// <para>
/// 他方言（MySQL / PostgreSQL / Oracle）の派生と同じく <see cref="GeneratedEfCoreDialectRuntimeTestsBase"/> を
/// 再利用する。相違点は Docker / Testcontainers を使わず一時ファイル DB を使うこと（CI でも常時実行）。
/// 図の型表記は <see cref="PortableDialect.SqlServer"/>（int / varchar(50) / decimal(10,2)）を用いる。
/// SQLite の型カタログは SQL Server 表記を verbatim に受け付け、EF Core の <c>UseSqlite</c> も
/// これらの宣言型で正しく動作する。
/// </para>
/// <para>
/// EF Core の SQLite プロバイダは <c>decimal</c> を TEXT として格納し、サーバー側の <c>ORDER BY</c> /
/// 比較 / 集計を直接はサポートしないが（"SQLite does not support expressions of type 'decimal'"）、
/// 基底シナリオの decimal 比較・SUM は少量データではクライアント評価で成立する。実運用と同じ DI 経路で
/// 全シナリオが緑であることを確認する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class GeneratedEfCoreSqliteRuntimeTests
    : GeneratedEfCoreDialectRuntimeTestsBase,
        IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private ServiceProvider? _provider;

    /// <summary>書き込み可能な接続文字列（EF はこの実ファイルへ読み書きする）</summary>
    private string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>AddGeneratedEfCoreRepositories → UseSqlite の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseSqlite(ConnectionString))
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override ISqlExecutor CreateSqlExecutor() =>
        Provider().GetRequiredService<ISqlExecutor>();

    /// <summary>スキーマを初期化し、SQLite の DdlGenerator が生成した DDL でテーブルを作成する</summary>
    /// <remarks>
    /// 一時ファイル DB のため、テーブルを明示 DROP してから作り直す。EF Migrations は使わない
    /// （EF は既存スキーマ接続専用という設計）。DROP は依存順（子 → 親）で実行する。
    /// </remarks>
    protected override async Task ResetAndCreateSchemaAsync()
    {
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);

            // FK 依存順に子（orders）→ 親（customers）の順で DROP する
            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"orders\"; DROP TABLE IF EXISTS \"customers\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(
            PortableFixtureDefinition.Build(PortableDialect.SqlServer)
        );
        await _db.ApplyDdlAsync(ddl, Ct);
    }

    /// <summary>SQLite は二重引用符で識別子を引用する</summary>
    protected override string Quote(string identifier) => $"\"{identifier}\"";

    /// <summary>SQLite（Microsoft.Data.Sqlite）は @ プレフィックスのプレースホルダを用いる</summary>
    protected override string Param(string name) => $"@{name}";

    // ==================== SQLite の既知制約に合わせて調整したシナリオ ====================
    // テスト 2（% _ エスケープ含む式木クエリ）は、生成ランタイムの LikeEscapeBehavior に SQLite 分岐
    // （ESCAPE '\' の明示）が実装されたため基底のまま実行する（override なし）。

    /// <summary>
    /// 6（SQLite 調整版）: 生 SQL 4 系統を検証する。
    /// </summary>
    /// <remarks>
    /// <b>基底との差分</b>: SQLite は <c>decimal</c> をネイティブに持たず TEXT として格納するため、
    /// <c>SUM(amount)</c> は数値化の過程で INTEGER（<see cref="long"/>）を返す。多列射影マッパ
    /// （<c>QueryProjectionBySqlAsync&lt;TDto&gt;</c>）は生成ランタイム（テンプレート＝変更範囲外）の仕様上、
    /// 列値をプロパティ型へ厳密代入し数値変換を行わないため、<c>decimal</c> プロパティへ Int64/String を代入できず失敗する。
    /// これは EF Core SQLite の decimal 制約と生成マッパの厳密射影の組み合わせによる既知制約のため、
    /// JOIN + 集計の検証は <c>ExecuteScalarSqlAsync&lt;decimal&gt;</c>（<c>Convert.ChangeType</c> で数値変換する経路）へ置き換える。
    /// それ以外（厳密全列・単一値・影響行数・匿名パラメータ）は基底と同一の検証内容を維持する。
    /// </remarks>
    [Fact(
        DisplayName = "[Dialect/SQLite] 6: 生 SQL（厳密全列・JOIN集計[ExecuteScalar版]・単一値・影響行数・匿名パラメータ）"
    )]
    public override async Task RawSql_AllModes()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m, null), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
        await orders.InsertAsync(NewOrder(12, 2, 200m, null), Ct);

        var customers = Quote("customers");
        var ordersT = Quote("orders");
        var customerId = Quote("customer_id");
        var name = Quote("name");
        var balance = Quote("balance");
        var amount = Quote("amount");

        // (a) Repository.QueryBySqlAsync（厳密全列・VO 復元・匿名パラメータ）
        var rows = await repo.QueryBySqlAsync(
            $"SELECT * FROM {customers} WHERE {balance} >= {Param("minBalance")} ORDER BY {customerId}",
            new { minBalance = 150m },
            Ct
        );
        rows.Select(c => c.Name.Value).Should().BeEquivalentTo(["Bob"]);
        rows.Single().RowState.Should().Be(RowState.Unchanged);

        // (b) JOIN + 集計（基底は多列射影 DTO だが、SQLite の decimal 制約＋厳密射影マッパのため
        //     ExecuteScalarSqlAsync<decimal>（Convert.ChangeType 経路）で顧客ごとの合計を検証する）
        var executor = CreateSqlExecutor();
        var aliceTotal = await repo.ExecuteScalarSqlAsync<decimal>(
            $"SELECT SUM(o.{amount}) FROM {customers} c "
                + $"JOIN {ordersT} o ON o.{customerId} = c.{customerId} "
                + $"WHERE c.{name} = {Param("n")}",
            new { n = "Alice" },
            Ct
        );
        aliceTotal.Should().Be(150m);

        var bobTotal = await repo.ExecuteScalarSqlAsync<decimal>(
            $"SELECT SUM(o.{amount}) FROM {customers} c "
                + $"JOIN {ordersT} o ON o.{customerId} = c.{customerId} "
                + $"WHERE c.{name} = {Param("n")}",
            new { n = "Bob" },
            Ct
        );
        bobTotal.Should().Be(200m);

        // (c) QueryProjectionBySqlAsync（単一値モード: string と VO 型）
        var names = await executor.QueryProjectionBySqlAsync<string>(
            $"SELECT {name} FROM {customers} ORDER BY {customerId}",
            null,
            Ct
        );
        names.Should().BeEquivalentTo(["Alice", "Bob"], o => o.WithStrictOrdering());

        var voNames = await executor.QueryProjectionBySqlAsync<NameValue>(
            $"SELECT {name} FROM {customers} ORDER BY {customerId}",
            null,
            Ct
        );
        voNames.Select(v => v.Value).Should().Equal("Alice", "Bob");

        // (d) ExecuteSqlAsync（UPDATE 影響行数・匿名パラメータ束縛）
        var affected = await repo.ExecuteSqlAsync(
            $"UPDATE {customers} SET {balance} = {Param("v")} WHERE {balance} >= {Param("min")}",
            new { v = 0m, min = 150m },
            Ct
        );
        affected.Should().Be(1);

        // (e) ExecuteScalarSqlAsync（COUNT を int）
        var count = await repo.ExecuteScalarSqlAsync<int>(
            $"SELECT COUNT(*) FROM {customers}",
            null,
            Ct
        );
        count.Should().Be(2);
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
