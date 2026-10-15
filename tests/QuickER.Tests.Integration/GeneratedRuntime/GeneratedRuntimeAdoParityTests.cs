using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// パリティスイートを<b>QuickER の SQL Server 実装</b>ランタイム（<see cref="SqlServerRepository{TEntity, TKey}"/> /
/// <see cref="SqlExecutor"/>）で実行する派生。リポジトリ・エグゼキュータは <see cref="ISqlConnectionFactory"/>
/// を渡して直接 new する。
/// </summary>
[Trait("RequiresDocker", "true")]
public sealed class GeneratedRuntimeAdoParityTests(SqlServerContainerFixture fixture)
    : GeneratedRuntimeParityTestsBase(fixture)
{
    /// <summary>接続ファクトリを生成する（コンテナの接続文字列を使う）</summary>
    private SqlConnectionFactory Factory() => new(Fixture.ConnectionString);

    protected override ICustomerRepository CreateCustomerRepository() =>
        new CustomerRepository(Factory());

    protected override IOrderRepository CreateOrderRepository() => new OrderRepository(Factory());

    protected override ISqlExecutor CreateSqlExecutor() => new SqlExecutor(Factory());

    /// <summary>
    /// 追加（QuickER の SQL Server のみ）: BulkInsertAsync は DEFAULT 制約のある列にも null を NULL として入れる。
    /// SqlBulkCopy は既定で null の代わりに DEFAULT を入れるため、KeepNulls が無いと行単位の InsertAsync と結果が割れる
    /// </summary>
    [Fact(
        DisplayName = "[Parity] 追加: BulkInsertAsync は DEFAULT のある列にも null を NULL として入れる（InsertAsync と同じ）"
    )]
    public async Task BulkInsert_NullIntoColumnWithDefault_StoresNull()
    {
        await ResetAndCreateSchemaAsync();

        // QuickER の DDL は DEFAULT を出さないので、既存 DB を取り込んだ構成を模して後から付ける
        await Fixture.ExecuteAsync(
            "ALTER TABLE customers ADD CONSTRAINT DF_customers_balance DEFAULT 99 FOR balance;",
            Ct
        );

        var repo = CreateCustomerRepository();
        (await repo.BulkInsertAsync([NewCustomer(1, "Alice")], Ct)).Should().Be(1);
        await repo.InsertAsync(NewCustomer(2, "Bob"), Ct);

        var all = await repo.GetAllAsync(Ct);
        all.Should().HaveCount(2);
        all.Should()
            .OnlyContain(c => c.Balance == null, "null を渡した列は DEFAULT でなく NULL になる");
    }

    /// <summary>
    /// 追加（QuickER の SQL Server のみ）: BulkInsertAsync は接続文字列の Command Timeout に従う。
    /// SqlBulkCopy は独自の BulkCopyTimeout（既定 30 秒）を持ち、接続文字列の値は届かない
    /// </summary>
    [Fact(
        DisplayName = "[Parity] 追加: BulkInsertAsync は接続文字列の Command Timeout でタイムアウトする"
    )]
    public async Task BulkInsert_FollowsConnectionStringCommandTimeout()
    {
        await ResetAndCreateSchemaAsync();

        var connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            Fixture.ConnectionString
        )
        {
            CommandTimeout = 1,
        }.ToString();
        var repo = new CustomerRepository(new SqlConnectionFactory(connectionString));

        // 別の接続が表を排他ロックで握ったままにして、一括挿入を待たせる
        await using var blocker = await Fixture.OpenConnectionAsync(Ct);
        await using var transaction = (Microsoft.Data.SqlClient.SqlTransaction)
            await blocker.BeginTransactionAsync(Ct);
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "SELECT COUNT(*) FROM customers WITH (TABLOCKX, HOLDLOCK);";
            await lockCommand.ExecuteScalarAsync(Ct);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var bulkInsert = async () => await repo.BulkInsertAsync([NewCustomer(1, "Alice")], Ct);

        await bulkInsert.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        stopwatch
            .Elapsed.Should()
            .BeLessThan(
                TimeSpan.FromSeconds(15),
                "接続文字列の 1 秒で打ち切られる（既定の 30 秒を待たない）"
            );

        await transaction.RollbackAsync(Ct);
    }

    /// <summary>列判定用のプローブ（プロパティ名 A/B がそのまま列名になる）</summary>
    private sealed class ProbeRow
    {
        public string A { get; set; } = string.Empty;
        public string B { get; set; } = string.Empty;
    }

    /// <summary>
    /// 追加（QuickER の SQL Server のみ）: 翻訳器が「値の位置に列参照」を置いた式へ生成する条件文字列
    /// （SQL Server 分岐の <c>+</c> 連結による列同士の LIKE 系・等値）が、実 SQL Server に対して意図どおりの
    /// 意味論（部分一致・ワイルドカードのリテラル扱い・引数列 NULL は不一致・Equals/IgnoreCase）で判定される
    /// ことを検証する。SQLite 側の <c>ColumnArgumentConditions_HaveCorrectSemanticsOnRealData</c> と対をなす。
    /// </summary>
    [Fact(
        DisplayName = "[Parity] 追加: 翻訳器が生成する列同士の LIKE 系・等値条件が実 SQL Server で正しい意味論を返す"
    )]
    public async Task ColumnArgumentConditions_HaveCorrectSemanticsOnRealData()
    {
        await ResetAndCreateSchemaAsync();

        var executor = CreateSqlExecutor();

        // 2 文字列列（A/B。ProbeRow のプロパティ名と一致）を持つ検証専用テーブルを用意する
        await executor.ExecuteSqlAsync(
            "IF OBJECT_ID('dbo.probe', 'U') IS NOT NULL DROP TABLE dbo.probe;",
            null,
            Ct
        );
        await executor.ExecuteSqlAsync(
            "CREATE TABLE dbo.probe ([A] NVARCHAR(50) NULL, [B] NVARCHAR(50) NULL);",
            null,
            Ct
        );

        // 翻訳器（SQL Server 方言）が述語本体から生成する条件文字列を取り出す
        static string Condition(Expression<Func<ProbeRow, bool>> predicate) =>
            SqlExpressionTranslator.ToCondition(predicate.Body, new List<SqlQueryParameter>());

        // 単一行を差し替え、条件に一致するかを COUNT で確かめる（引数列 B は NULL も渡せるよう分岐する）
        async Task<int> MatchesAsync(string condition, string a, string? b)
        {
            await executor.ExecuteSqlAsync("DELETE FROM dbo.probe;", null, Ct);

            if (b is null)
            {
                await executor.ExecuteSqlAsync(
                    "INSERT INTO dbo.probe ([A], [B]) VALUES (@a, NULL);",
                    new { a },
                    Ct
                );
            }
            else
            {
                await executor.ExecuteSqlAsync(
                    "INSERT INTO dbo.probe ([A], [B]) VALUES (@a, @b);",
                    new { a, b },
                    Ct
                );
            }

            return await executor.ExecuteScalarSqlAsync<int>(
                $"SELECT COUNT(*) FROM dbo.probe WHERE {condition}",
                null,
                Ct
            );
        }

        // --- Contains（部分一致・ワイルドカードのリテラル扱い・引数列 NULL は不一致） ---
        var contains = Condition(p => p.A.Contains(p.B));
        (await MatchesAsync(contains, "foobar", "oob")).Should().Be(1, "B の値が A に含まれる");
        (await MatchesAsync(contains, "x10%y", "10%"))
            .Should()
            .Be(1, "% はリテラル扱いなので A に含まれる");
        (await MatchesAsync(contains, "10a", "10%"))
            .Should()
            .Be(0, "% がワイルドカードなら誤って一致してしまう");
        (await MatchesAsync(contains, "xa_cy", "a_c"))
            .Should()
            .Be(1, "_ はリテラル扱いなので A に含まれる");
        (await MatchesAsync(contains, "xabcy", "a_c"))
            .Should()
            .Be(0, "_ がワイルドカードなら誤って一致してしまう");
        (await MatchesAsync(contains, "hello", null)).Should().Be(0, "引数列が NULL の行は不一致");

        // --- StartsWith / EndsWith ---
        var startsWith = Condition(p => p.A.StartsWith(p.B));
        (await MatchesAsync(startsWith, "abcdef", "abc")).Should().Be(1);
        (await MatchesAsync(startsWith, "xabc", "abc")).Should().Be(0);

        var endsWith = Condition(p => p.A.EndsWith(p.B));
        (await MatchesAsync(endsWith, "abcdef", "def")).Should().Be(1);
        (await MatchesAsync(endsWith, "defx", "def")).Should().Be(0);

        // --- Equals / Equals(IgnoreCase） ---
#pragma warning disable CA1309 // 翻訳器が受け付ける呼び出しの形（Equals(string)）そのものを式木で確かめる
        var equals = Condition(p => p.A.Equals(p.B));
#pragma warning restore CA1309
        (await MatchesAsync(equals, "same", "same")).Should().Be(1);
        (await MatchesAsync(equals, "abc", "abcd")).Should().Be(0);

        var equalsIgnoreCase = Condition(p => p.A.Equals(p.B, StringComparison.OrdinalIgnoreCase));
        (await MatchesAsync(equalsIgnoreCase, "ABC", "abc"))
            .Should()
            .Be(1, "IgnoreCase は両辺を LOWER で畳むため一致する");

        // 後始末（検証専用テーブルを落とす）
        await executor.ExecuteSqlAsync("DROP TABLE dbo.probe;", null, Ct);
    }

    /// <summary>
    /// 追加（QuickER の SQL Server のみ）: <c>BulkInsertAsync</c>（SqlBulkCopy 経路）が外部キー制約を検査し、
    /// 親の無い子行を黙って書き込まないことを検証する。
    /// </summary>
    /// <remarks>
    /// SqlBulkCopy は既定で FK / CHECK 制約を検査しない。行単位の INSERT（<c>InsertAsync</c>）は当然に弾かれるため、
    /// 既定のままだと「同じ図・同じ制約なのに一括追加だけが不正な行を通す」非対称になっていた。
    /// <c>SqlBulkCopyOptions.CheckConstraints</c> を常時付けることで両経路が揃う。
    /// </remarks>
    [Fact(
        DisplayName = "[Parity] 追加: BulkInsertAsync は外部キー違反の行を検査して弾く（CheckConstraints）"
    )]
    public async Task BulkInsert_ChecksForeignKeyConstraints()
    {
        await ResetAndCreateSchemaAsync();

        var orders = CreateOrderRepository();

        // 顧客 999 は存在しない＝FK 違反の子行。行単位 INSERT と同じく一括追加でも例外になる
        var bulk = async () =>
            await orders.BulkInsertAsync([NewOrder(1, customerId: 999, 10m, memo: null)], Ct);
        await bulk.Should().ThrowAsync<Exception>();

        // 例外で終わった＝1 行も入っていない（検査なしだと黙って挿入されてしまう）
        (await orders.GetAllAsync(Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// 追加（QuickER の SQL Server のみ）: 生 SQL のコレクション IN 展開が、同名で始まる <c>@@</c> システム変数を
    /// 巻き込まないことを検証する（<c>@@ROWCOUNT</c> が <c>@ROWCOUNT</c> の展開で壊されない）。
    /// </summary>
    /// <remarks>
    /// 置換の正規表現は右境界しか見ていなかったため、パラメータ名が <c>ROWCOUNT</c> のとき <c>@@ROWCOUNT</c> の
    /// 末尾 <c>@ROWCOUNT</c> が一致して展開され、構文が壊れていた。左境界（直前が識別子文字でも <c>@</c> でもない）を
    /// 加えて解消している。SQL Server 固有の <c>@@</c> 変数を使うため SQL Server 専用テスト。
    /// </remarks>
    [Fact(DisplayName = "[Parity] 追加: 生 SQL の IN 展開は同名の @@ システム変数を書き換えない")]
    public async Task RawSql_CollectionExpansion_LeavesSystemVariableIntact()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await customers.InsertAsync(NewCustomer(3, "Carol"), Ct);

        var executor = CreateSqlExecutor();

        // パラメータ名を ROWCOUNT にして @@ROWCOUNT と同居させる。展開が左境界を見ていないと @@ROWCOUNT の
        // 末尾が置換されて「@1, 3」のような壊れたトークンになり、実行そのものが構文エラーになる
        var matched = await executor.ExecuteScalarSqlAsync<int>(
            "DECLARE @previous INT = @@ROWCOUNT; "
                + "SELECT COUNT(*) FROM customers WHERE customer_id IN (@ROWCOUNT);",
            new { ROWCOUNT = new[] { 1, 3 } },
            Ct
        );

        matched.Should().Be(2, "IN (@ROWCOUNT) は 2 行に一致し、@@ROWCOUNT はそのまま残る");
    }
}
