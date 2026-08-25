using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqliteFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成リポジトリが DI に登録済みの <see cref="ISqlExecutor"/> を実際に使うことを、実 SQLite（一時ファイル DB・
/// Docker 不要）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 従来はリポジトリ基底がフィールド初期化子で <c>new SqlExecutor(connectionFactory)</c> を固定生成しており、
/// DI に別実装（ログ・計測・再試行を挟んだラッパー）を登録しても生 SQL メソッドはそれを一切通らなかった
/// （<c>ISqlExecutor</c> を直接解決した場合だけ効く、という分かりにくい非対称）。省略可能な第 3 引数として
/// 受け取り、DI 登録がそれを渡す形にしたことで、登録した実装が生 SQL 経路にも効く。
/// </para>
/// <para>
/// 手で <c>new</c> するコード（引数省略）は既定実装を組むため、既存の呼び出しは無変更で動く。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqlExecutorInjectionRuntimeTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    public void Dispose() => _db.Dispose();

    /// <summary>呼び出しを記録して内側の実装へ委譲するだけの <see cref="ISqlExecutor"/> ラッパー</summary>
    private sealed class RecordingSqlExecutor(ISqlExecutor inner) : ISqlExecutor
    {
        /// <summary>委譲した SQL の記録（呼ばれたことの証拠）</summary>
        public List<string> Calls { get; } = new();

        public Task<IReadOnlyList<TEntity>> QueryBySqlAsync<TEntity>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
            where TEntity : EntityBase, new()
        {
            Calls.Add(sql);
            return inner.QueryBySqlAsync<TEntity>(sql, parameters, cancellationToken);
        }

        public Task<IReadOnlyList<TResult>> QueryProjectionBySqlAsync<TResult>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(sql);
            return inner.QueryProjectionBySqlAsync<TResult>(sql, parameters, cancellationToken);
        }

        public Task<int> ExecuteSqlAsync(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(sql);
            return inner.ExecuteSqlAsync(sql, parameters, cancellationToken);
        }

        public Task<TResult?> ExecuteScalarSqlAsync<TResult>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(sql);
            return inner.ExecuteScalarSqlAsync<TResult>(sql, parameters, cancellationToken);
        }
    }

    [Fact(
        DisplayName = "[SQLite] DI へ登録したカスタム ISqlExecutor をリポジトリの生 SQL が経由する"
    )]
    public async Task Repository_RawSql_UsesRegisteredSqlExecutor()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(SqlitePortableFixtureDefinition.Build(), Ct);

        var connectionString = _db.ReadWriteCreateConnectionString;
        var recorder = new RecordingSqlExecutor(
            new SqlExecutor(new SqlConnectionFactory(connectionString))
        );

        // 生成 DI 登録のあとにカスタム実装を登録する（後勝ちで ISqlExecutor が差し替わる）
        using var provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(connectionString)
            .AddSingleton<ISqlExecutor>(recorder)
            .BuildServiceProvider();

        var customers = provider.GetRequiredService<ICustomerRepository>();
        await customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(1),
                Name = NameValue.Create("Alice"),
            },
            Ct
        );

        var rows = await customers.QueryBySqlAsync("SELECT * FROM \"customers\"", null, Ct);

        rows.Should().ContainSingle();
        recorder
            .Calls.Should()
            .ContainSingle()
            .Which.Should()
            .Be("SELECT * FROM \"customers\"", "リポジトリは登録済みの実装へ委譲する");
    }

    [Fact(DisplayName = "[SQLite] 手で new したリポジトリは既定の SqlExecutor を組む（互換）")]
    public async Task Repository_ConstructedByHand_StillWorksWithoutExecutor()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(SqlitePortableFixtureDefinition.Build(), Ct);

        // 第 2・第 3 引数を省略した従来の呼び出し（DI なし経路の公式サポート）
        var customers = new CustomerRepository(
            new SqlConnectionFactory(_db.ReadWriteCreateConnectionString)
        );

        await customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(1),
                Name = NameValue.Create("Alice"),
            },
            Ct
        );

        (await customers.QueryBySqlAsync("SELECT * FROM \"customers\"", null, Ct))
            .Should()
            .ContainSingle();
    }
}
