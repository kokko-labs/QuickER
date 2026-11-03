using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedQueryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ保存の、NULL を許す一意な列の並べ方を<b>実ファイル SQLite</b>（Docker 不要＝CI 常時実行）で確かめる共通基底。
/// QuickER の <c>SqliteRepository</c> 版と EF Core Sqlite 版がリポジトリの解決だけを差し込む。
/// </summary>
/// <remarks>
/// 入力はクエリフィクスチャ（注文のメモが NULL を許す一意制約を持つ）。グラフ保存のほかのシナリオ（カスケードの閉包だけを
/// 保存する・付け替えと元の親の削除・削除する行の一意な値を取る更新）は、実装先どうしの一致のエンジン
/// （<see cref="BackendParityRuntimeTests"/> の <c>graph-include-*</c>・<c>graph-move-order-and-delete-old-customer-*</c>・
/// <c>graph-updated-row-takes-unique-value-of-deleted-row-with-children-*</c>）が持つ。この形だけは、その図に NULL を許す一意な
/// 列が無いので、ここで流す。
/// </remarks>
public abstract class GraphSaveSqliteFileRuntimeTestsBase : IDisposable
{
    /// <summary>テストのキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>注文明細リポジトリを生成する</summary>
    protected abstract IOrderLineRepository CreateOrderLineRepository();

    /// <summary>保存先のスキーマを空の状態へ戻す</summary>
    private async Task ResetStorageAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);
    }

    /// <summary>
    /// NULL を許す一意な列（注文のメモ）が NULL の行どうしは、一意な値を受け渡す並べ方に影響しない。NULL のメモの注文を消し、
    /// 同じく NULL のメモの新しい注文へ明細を移す保存は、新しい注文 → 明細の付け替え → 削除の順で通る（EF Core が NULL を
    /// 同じ値とみなすと、削除 → 挿入の辺が加わって循環になる）。SQL Server の一意制約は NULL を 1 つしか許さないので、
    /// この形は SQLite でだけ流す
    /// </summary>
    [Fact(
        DisplayName = "グラフ保存: NULL の一意な値どうしは、削除する注文の明細を新しい注文へ移す保存の並べ方に影響しない"
    )]
    public async Task NullUniqueValues_DoNotOrderTheSave()
    {
        await ResetStorageAsync();
        await CreateCustomerRepository()
            .InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(1),
                    Name = NameValue.Create("Alice"),
                },
                Ct
            );
        var orders = CreateOrderRepository();
        await orders.InsertAsync(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(12),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(3m),
            },
            Ct
        );
        var lines = CreateOrderLineRepository();
        await lines.InsertAsync(
            new OrderLineEntity
            {
                LineId = LineIdValue.Create(100),
                OrderId = OrderIdValue.Create(12),
                ItemName = ItemNameValue.Create("pen"),
                Quantity = QuantityValue.Create(1),
            },
            Ct
        );

        var deleted = (await orders.GetByIdAsync(OrderIdValue.Create(12), Ct))!;
        deleted.MarkRemoved();
        var added = new OrderEntity
        {
            OrderId = OrderIdValue.Create(13),
            CustomerId = CustomerIdValue.Create(1),
            Amount = AmountValue.Create(4m),
        };
        added.MarkAdded();
        var line = (await lines.GetByIdAsync(LineIdValue.Create(100), Ct))!;
        line.OrderId = OrderIdValue.Create(13);
        line.MarkUpdated();
        added.OrderLines.Add(line);

        await orders.SaveAsync([deleted, added], cancellationToken: Ct);

        (await orders.Query().ToListAsync(Ct)).Select(o => o.OrderId.Value).Should().Equal(13);
        (await lines.GetByIdAsync(LineIdValue.Create(100), Ct))!.OrderId.Value.Should().Be(13);
    }

    /// <summary>一時 DB を破棄する</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
