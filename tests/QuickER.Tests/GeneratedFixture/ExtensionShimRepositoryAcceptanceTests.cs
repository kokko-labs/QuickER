using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// Repository / Mapper の拡張シムへ利用者が書く partial の受け入れ条件を、実フィクスチャ
/// （<c>InMemoryFixture.g.cs</c>＝DB 不要）の上で検証する。
/// </summary>
/// <remarks>
/// <para>
/// このファイル自身が「利用者が書く拡張」の実物で、下の partial 宣言 2 つ（<see cref="IRepository{TEntity, TKey}"/> と
/// <see cref="MapperBase{TEntity, TEditModel}"/>）が拡張面である。契約シムへ<b>既定実装付き</b>のメンバーを足すと、
/// DI で受け取った <c>I{Entity}Repository</c> 参照から——エンティティにもバックエンドにも依らず——そのまま呼べる。
/// </para>
/// <para>
/// 既定実装は仮想ディスパッチなので、デコレータで包んでも「デコレータ自身の実装」へ落ちる。ここでは呼び出し回数を
/// 数えるデコレータ越しに同じ既定実装を呼び、転送が効いていることを実測で固定する（ジャーナリングデコレータと
/// 同じ形＝契約を実装して内側へ委譲するクラス）。
/// </para>
/// </remarks>
public sealed class ExtensionShimRepositoryAcceptanceTests
{
    [Fact(DisplayName = "IRepository の partial へ足した既定実装が生成リポジトリ参照から呼べる")]
    public async Task RepositoryContractShim_DefaultImplementation_IsCallableThroughGeneratedContract()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new InMemoryDataStore();
        ICustomerRepository repository = new InMemoryCustomerRepository(store);

        await repository.InsertAsync(
            new CustomerEntity { CustomerId = 1, Name = "Ada" },
            cancellationToken
        );

        // 生成契約（I{Entity}Repository）は拡張シム経由で固定ランタイムへ着地するため、
        // シムへ足した既定実装がそのまま生えている
        var found = await repository.GetRequiredAsync(1, cancellationToken);
        found.Name.Should().Be("Ada");

        // 既定実装の中身（GetByIdAsync が null なら例外）も期待どおりに効く
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.GetRequiredAsync(99, cancellationToken)
        );
        missing.Message.Should().Contain("CustomerEntity");

        // 別エンティティの契約からも同じメンバーが見える（面はエンティティに依らない）
        IOrderRepository orders = new InMemoryOrderRepository(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orders.GetRequiredAsync(42, cancellationToken)
        );
    }

    [Fact(DisplayName = "既定実装はデコレータ越しでもデコレータ自身の実装へ転送される")]
    public async Task RepositoryContractShim_DefaultImplementation_DispatchesThroughDecorator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new InMemoryDataStore();
        var inner = new InMemoryCustomerRepository(store);
        await inner.InsertAsync(
            new CustomerEntity { CustomerId = 7, Name = "Grace" },
            cancellationToken
        );

        var decorator = new CountingCustomerRepository(inner);
        ICustomerRepository decorated = decorator;

        var found = await decorated.GetRequiredAsync(7, cancellationToken);

        found.Name.Should().Be("Grace");
        decorator.GetByIdCalls.Should().Be(1, "既定実装は this のインターフェイス呼びで転送される");
    }

    [Fact(DisplayName = "MapperBase の partial へ足したメンバーが全 Mapper で呼べる")]
    public void MapperShim_AddedMember_ReachesEveryMapper()
    {
        new CustomerMapper().DescribePair().Should().Be("CustomerEntity->CustomerEditModel");
        new OrderMapper().DescribePair().Should().Be("OrderEntity->OrderEditModel");
    }

    /// <summary>
    /// 生成契約を実装して内側へ委譲するだけのデコレータ（ジャーナリングデコレータと同じ形）。
    /// 既定実装が経由する <c>GetByIdAsync</c> だけ回数を数える。
    /// </summary>
    private sealed class CountingCustomerRepository(ICustomerRepository inner) : ICustomerRepository
    {
        public int GetByIdCalls { get; private set; }

        public Task<CustomerEntity?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            GetByIdCalls++;
            return inner.GetByIdAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<CustomerEntity>> GetAllAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllAsync(cancellationToken);

        public Task InsertAsync(
            CustomerEntity entity,
            CancellationToken cancellationToken = default
        ) => inner.InsertAsync(entity, cancellationToken);

        public Task<bool> UpdateAsync(
            CustomerEntity entity,
            ConcurrencyMode mode = ConcurrencyMode.Optimistic,
            CancellationToken cancellationToken = default
        ) => inner.UpdateAsync(entity, mode, cancellationToken);

        public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task<int> SaveAsync(
            CustomerEntity entity,
            bool cascadeSave = true,
            bool cascadeDelete = true,
            bool insertWhenUpdateMissing = false,
            ConcurrencyMode mode = ConcurrencyMode.Optimistic,
            CancellationToken cancellationToken = default
        ) =>
            inner.SaveAsync(
                entity,
                cascadeSave,
                cascadeDelete,
                insertWhenUpdateMissing,
                mode,
                cancellationToken
            );

        public Task<int> SaveAsync(
            IEnumerable<CustomerEntity> entities,
            bool cascadeSave = true,
            bool cascadeDelete = true,
            bool insertWhenUpdateMissing = false,
            ConcurrencyMode mode = ConcurrencyMode.Optimistic,
            CancellationToken cancellationToken = default
        ) =>
            inner.SaveAsync(
                entities,
                cascadeSave,
                cascadeDelete,
                insertWhenUpdateMissing,
                mode,
                cancellationToken
            );

        public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
            CustomerEntity entity,
            CancellationToken cancellationToken = default
        ) => inner.CheckUniquenessAsync(entity, cancellationToken);

        public SqlQuery<CustomerEntity> Query() => inner.Query();

        public Task<int> BulkInsertAsync(
            IEnumerable<CustomerEntity> entities,
            CancellationToken cancellationToken = default
        ) => inner.BulkInsertAsync(entities, cancellationToken);

        public Task<IReadOnlyList<CustomerEntity>> QueryBySqlAsync(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        ) => inner.QueryBySqlAsync(sql, parameters, cancellationToken);

        public Task<int> ExecuteSqlAsync(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        ) => inner.ExecuteSqlAsync(sql, parameters, cancellationToken);

        public Task<TResult?> ExecuteScalarSqlAsync<TResult>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        ) => inner.ExecuteScalarSqlAsync<TResult>(sql, parameters, cancellationToken);
    }
}

/// <summary>
/// Repository 契約シムの利用者 partial。<b>既定実装付き</b>のメンバーを足すと、全エンティティ・全バックエンドの
/// リポジトリ参照から呼べる（abstract なメンバーを足すと全実装が壊れるため、追加は既定実装付きに限る）。
/// </summary>
/// <remarks>型引数リストは繰り返すが、制約は生成側の part が宣言済みなので省略する。</remarks>
public partial interface IRepository<TEntity, TKey>
{
    /// <summary>主キーで 1 件取得し、見つからなければ例外にする（全リポジトリ共通で使える）。</summary>
    async Task<TEntity> GetRequiredAsync(TKey id, CancellationToken cancellationToken = default) =>
        await GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} '{id}' was not found.");
}

/// <summary>
/// Mapper 拡張シムの利用者 partial。全 Mapper へメンバーを 1 箇所で足す。
/// </summary>
/// <remarks>型引数リストは繰り返すが、制約は生成側の part が宣言済みなので省略する。</remarks>
public partial class MapperBase<TEntity, TEditModel>
{
    /// <summary>変換の両端を 1 行で表す（全 Mapper 共通で使える）。</summary>
    public string DescribePair() => $"{typeof(TEntity).Name}->{typeof(TEditModel).Name}";
}
