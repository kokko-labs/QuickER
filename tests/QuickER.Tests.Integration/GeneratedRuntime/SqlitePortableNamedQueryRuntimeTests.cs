using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqliteFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQLite 方言フィクスチャ（<see cref="SqlitePortableFixtureDefinition"/>）の名前付きクエリ 3 種
/// （SearchMemoContains / GetMissingMemo / GetExpensive）を、実 SQLite（一時ファイル DB・Docker 不要）で
/// 検証するスイート。SQL Server 全カバレッジフィクスチャの同内容クエリを検証する
/// <see cref="GeneratedRuntimeParityTestsBase"/> の 17a/b/c と<b>対称の期待値</b>で、DSL→SQL 翻訳
/// （CONTAINS→LIKE エスケープ・IS NULL・decimal 比較）の方言対称性を担保する。
/// </summary>
/// <remarks>
/// <para>
/// 検証は QuickER の <c>SqliteRepository</c> 版（ADO）のみ。DSL→SQL 翻訳は ADO 側の責務であり、
/// EF Core 実装は DSL を C# ラムダとして EF に渡すため方言翻訳の検証にならない。また
/// <c>GetExpensive</c> は decimal 比較のため EF Core Sqlite では実行できない（decimal のサーバーサイド
/// 比較非対応）。EF Core での名前付きクエリ実行自体は SQL Server 側パリティと
/// <c>NamedQueryEfCoreRuntimeTests</c>（QueryFixture）が担う。
/// </para>
/// <para>
/// シードと期待値は <see cref="GeneratedRuntimeParityTestsBase"/> の <c>SeedNamedQueryOrdersAsync</c>／
/// 17a/b/c と同一（注文 10〜14・メモにワイルドカード文字を含む）。SQLite の LIKE に SQL Server の
/// 角括弧文字クラスは無いが、エスケープ実装は共通のため <c>"[a]"</c> もリテラル一致で同一結果になる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqlitePortableNamedQueryRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列</summary>
    private string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は一時 DB）</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    /// <summary>スキーマを初期化し、SQL Server 側パリティ 17 系と同一のシードを投入した注文リポジトリを返す</summary>
    private async Task<IOrderRepository> ResetAndSeedNamedQueryOrdersAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(SqlitePortableFixtureDefinition.Build(), Ct);

        var customers = Provider().GetRequiredService<ICustomerRepository>();
        await customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(1),
                Name = NameValue.Create("Alice"),
            },
            Ct
        );

        var orders = Provider().GetRequiredService<IOrderRepository>();
        await orders.InsertAsync(NewOrder(10, 100m, memo: "sale 100% off"), Ct);
        await orders.InsertAsync(NewOrder(11, 50.5m, memo: "sale 100x off"), Ct);
        await orders.InsertAsync(NewOrder(12, 200m, memo: "code_a"), Ct);
        await orders.InsertAsync(NewOrder(13, 75m, memo: "code[a]"), Ct);
        await orders.InsertAsync(NewOrder(14, 50.5m, memo: null), Ct);
        return orders;
    }

    /// <summary>顧客 1 の注文エンティティを組み立てる（VO は Create で検証生成）</summary>
    private static OrderEntity NewOrder(int orderId, decimal amount, string? memo) =>
        new()
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(1),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };

    /// <summary>
    /// CONTAINS→LIKE: ワイルドカード文字（% / _ / [）がリテラル扱いでエスケープされる
    /// （SQL Server 側 <see cref="GeneratedRuntimeParityTestsBase.NamedQuery_Contains_EscapesLikeWildcards"/> と対称）。
    /// </summary>
    [Fact(
        DisplayName = "[SQLite/NamedQuery] CONTAINS→LIKE がワイルドカードをエスケープする（17a 対称）"
    )]
    public async Task NamedQuery_Contains_EscapesLikeWildcards()
    {
        var orders = await ResetAndSeedNamedQueryOrdersAsync();

        // "100%" はリテラル一致（% エスケープ）＝10 のみ
        var percent = await orders.SearchMemoContainsAsync("100%", Ct);
        percent.Select(o => o.OrderId.Value).Should().Equal(10);

        // "code_" はリテラル一致（_ エスケープ）＝12 のみ
        var underscore = await orders.SearchMemoContainsAsync("code_", Ct);
        underscore.Select(o => o.OrderId.Value).Should().Equal(12);

        // "[a]" もリテラル一致＝13 のみ（SQLite の LIKE に文字クラスはないが、共通エスケープ実装で同一結果）
        var bracket = await orders.SearchMemoContainsAsync("[a]", Ct);
        bracket.Select(o => o.OrderId.Value).Should().Equal(13);

        // 通常の部分一致は該当全件・NULL メモ行は一致しない（NULL 意味論）
        var plain = await orders.SearchMemoContainsAsync("sale", Ct);
        plain.Select(o => o.OrderId.Value).Should().Equal(10, 11);

        (await orders.SearchMemoContainsAsync("off", Ct))
            .Select(o => o.OrderId.Value)
            .Should()
            .Equal(10, 11);
    }

    /// <summary>
    /// IS NULL: NULL 許容列の未設定行のみ返す
    /// （SQL Server 側 <see cref="GeneratedRuntimeParityTestsBase.NamedQuery_IsNull_ReturnsNullRows"/> と対称）。
    /// </summary>
    [Fact(DisplayName = "[SQLite/NamedQuery] IS NULL が NULL 行のみ返す（17b 対称）")]
    public async Task NamedQuery_IsNull_ReturnsNullRows()
    {
        var orders = await ResetAndSeedNamedQueryOrdersAsync();

        var missing = await orders.GetMissingMemoAsync(Ct);
        missing.Select(o => o.OrderId.Value).Should().Equal(14);
    }

    /// <summary>
    /// decimal 比較（VO 列 &gt;= パラメータ）: 境界値込みで正しい行を返す（QuickER の SQLite 版は decimal を
    /// 数値として扱うため EF Core Sqlite の制約は無関係。SQL Server 側
    /// <see cref="GeneratedRuntimeParityTestsBase.NamedQuery_DecimalComparison_ReturnsCorrectRows"/> と対称）。
    /// </summary>
    [Fact(DisplayName = "[SQLite/NamedQuery] decimal 比較が正しい行を返す（17c 対称）")]
    public async Task NamedQuery_DecimalComparison_ReturnsCorrectRows()
    {
        var orders = await ResetAndSeedNamedQueryOrdersAsync();

        // 境界値（50.5）ちょうどを含む＝ >= の意味論と decimal スケールの往復を確認
        var fromBoundary = await orders.GetExpensiveAsync(50.5m, Ct);
        fromBoundary.Select(o => o.OrderId.Value).Should().Equal(10, 11, 12, 13, 14);

        var expensive = await orders.GetExpensiveAsync(100m, Ct);
        expensive.Select(o => o.OrderId.Value).Should().Equal(10, 12);

        (await orders.GetExpensiveAsync(1000m, Ct)).Should().BeEmpty();
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
