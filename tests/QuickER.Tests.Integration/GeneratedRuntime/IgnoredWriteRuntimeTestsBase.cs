using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 版（rowversion）を持たない表への更新が、競合以外の理由（トリガー・行レベルセキュリティ）で 1 行も当たらなかったときの
/// 扱いを、QuickER 版と EF Core 版で同じ結果に固定する共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 版を持たない表では「他者が先に変えた」ことを判定する材料が無いため、0 行は「行が無い」として扱う
/// （単一の <c>UpdateAsync</c> は <c>false</c>・グラフ保存は <c>SaveConflictException</c>（NotFound））。
/// 強制上書き（<c>ForceOverwrite</c>）でも同じで、何度やり直しても当たらない文を繰り返さない。
/// </para>
/// <para>
/// 0 行を作るのは SQLite の <c>RAISE(IGNORE)</c> トリガー（更新を黙って捨てる）。行は残ったままになる。
/// クエリフィクスチャの <c>orders</c> は版を持たない表。インメモリにはトリガーが無いので対象外。
/// </para>
/// </remarks>
public abstract class IgnoredWriteRuntimeTestsBase : IDisposable
{
    /// <summary>テストのキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>一時 DB を破棄する</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>スキーマを作り、注文 1 件を投入してから、orders の更新を黙って捨てるトリガーを張る</summary>
    private async Task<OrderEntity> SeedWithIgnoringTriggerAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        await CreateCustomerRepository()
            .InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(1),
                    Name = NameValue.Create("Alice"),
                },
                Ct
            );
        await CreateOrderRepository()
            .InsertAsync(
                new OrderEntity
                {
                    OrderId = OrderIdValue.Create(10),
                    CustomerId = CustomerIdValue.Create(1),
                    Amount = AmountValue.Create(10m),
                    Memo = MemoValue.Create("original"),
                },
                Ct
            );

        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TRIGGER ignore_order_updates BEFORE UPDATE ON orders BEGIN SELECT RAISE(IGNORE); END;";
            await command.ExecuteNonQueryAsync(Ct);
        }

        var order = await CreateOrderRepository().GetByIdAsync(OrderIdValue.Create(10), Ct);
        order!.Memo = MemoValue.Create("edited");
        order.MarkUpdated();
        return order;
    }

    /// <summary>やり直しが止まらない退行で全体が止まらないよう、テストごとに上限時間を掛けたトークン</summary>
    private static CancellationTokenSource Bounded()
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        source.CancelAfter(TimeSpan.FromSeconds(30));
        return source;
    }

    [Theory(DisplayName = "版の無い表で更新が 1 行も当たらなければ UpdateAsync は false を返す")]
    [InlineData(ConcurrencyMode.Optimistic)]
    [InlineData(ConcurrencyMode.ForceOverwrite)]
    public async Task UpdateMatchingNoRow_ReturnsFalse(ConcurrencyMode mode)
    {
        var order = await SeedWithIgnoringTriggerAsync();
        using var bounded = Bounded();

        (await CreateOrderRepository().UpdateAsync(order, mode, bounded.Token)).Should().BeFalse();
    }

    [Theory(
        DisplayName = "版の無い表でグラフ保存の更新が 1 行も当たらなければ NotFound で失敗する"
    )]
    [InlineData(ConcurrencyMode.Optimistic)]
    [InlineData(ConcurrencyMode.ForceOverwrite)]
    public async Task SaveMatchingNoRow_ReportsNotFound(ConcurrencyMode mode)
    {
        var order = await SeedWithIgnoringTriggerAsync();
        using var bounded = Bounded();

        var act = () =>
            CreateOrderRepository().SaveAsync(order, mode: mode, cancellationToken: bounded.Token);

        var thrown = await act.Should().ThrowAsync<SaveConflictException>();
        thrown.Which.Reason.Should().Be(SaveConflictReason.NotFound);
    }
}
