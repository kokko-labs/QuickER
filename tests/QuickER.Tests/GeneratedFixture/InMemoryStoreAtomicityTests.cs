using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// インメモリストアの「保存単位の原子性」を、リポジトリ経由では作れない境界条件で直接検証する。
/// </summary>
/// <remarks>
/// <para>
/// 検証対象は 2 つ。(1) <c>ExecuteDeleteAsync</c> が途中で例外になったときにストアが 1 行も変わらないこと
/// （循環カスケードの <see cref="NotSupportedException"/> は走査の<b>途中</b>で投げられるため、staging を使わない
/// 実装では先に削除した子が残る＝実 DB のロールバックと乖離する）。(2) <c>Publish</c> が「更新として開始した行が
/// その間に削除されていた」場合に staged スナップショットを適用せず、<c>SaveConflictException</c>
/// （<see cref="SaveConflictReason.NotFound"/>）で失敗を述べること（実 DB の UPDATE は対象行が無ければ 0 行更新
/// ＝黙って捨てると「保存できた」と報告しながら行が無い状態になる）。staged 側が削除なら、行が既に無いのは
/// 実 DB と同じく no-op なので競合にしない。
/// </para>
/// <para>
/// 循環カスケードを持つ図は固定フィクスチャに無いため、生成された <c>EntityBase</c> ／
/// <c>NavigationReferenceAttribute</c> の上にテスト専用のエンティティ型を組み立てて再現する
/// （メタデータはリフレクション駆動なので、生成エンティティと同じ経路で扱われる）。
/// </para>
/// </remarks>
public sealed class InMemoryStoreAtomicityTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 循環カスケードで <c>ExecuteDeleteAsync</c> が中断しても、先行して削除された子ごとストアは不変（全か無か）。
    /// </summary>
    [Fact(
        DisplayName = "[InMemory/Atomicity] ExecuteDelete: 循環カスケードの中断でストアは 1 行も変わらない"
    )]
    public async Task ExecuteDelete_CyclicCascade_LeavesStoreUntouched()
    {
        var store = new InMemoryDataStore();
        store.Put(new CascadeRootEntity { Id = 1 });
        store.Put(new CascadeLeafEntity { Id = 10, RootId = 1 });

        // 前提: 葉への カスケード が先・自己参照（循環）が後。この順序でこそ「先に削除が成功してから中断する」経路になる
        EntitySaveMetadata
            .For(typeof(CascadeRootEntity))
            .CascadeNavigations.Select(navigation => navigation.ChildType)
            .Should()
            .Equal([typeof(CascadeLeafEntity), typeof(CascadeRootEntity)]);

        var query = new SqlQuery<CascadeRootEntity>(
            new InMemoryQueryExecutor<CascadeRootEntity>(store)
        );

        var act = async () => await query.ExecuteDeleteAsync(cascadeDelete: true, Ct);
        await act.Should().ThrowAsync<NotSupportedException>();

        store
            .Snapshot<CascadeLeafEntity>()
            .Should()
            .ContainSingle("中断した削除は葉の削除ごと巻き戻る（部分適用を残さない）");
        store.Snapshot<CascadeRootEntity>().Should().ContainSingle();
    }

    /// <summary>
    /// 更新として開始した行がその間に削除されていた場合、<c>Publish</c> は staged スナップショットを適用せず
    /// <c>SaveConflictException</c>（<see cref="SaveConflictReason.NotFound"/>）で失敗する（復活させないのは
    /// 実 DB の UPDATE と同じだが、黙って捨てると「保存できた」と報告しながら行が無い状態になる）。
    /// </summary>
    [Fact(
        DisplayName = "[InMemory/Atomicity] Publish: 削除された行への staged 更新は NotFound の競合になる"
    )]
    public void Publish_RowDeletedMeanwhile_ThrowsNotFound()
    {
        var store = new InMemoryDataStore();
        store.Put(new CustomerEntity { CustomerId = 1, Name = "Alice" });

        // 保存が「更新として」この行に触れる＝ベースラインを捕捉して staged 更新を積む
        var staging = new InMemorySaveStaging();
        store.Write(
            scope =>
            {
                scope.Put(new CustomerEntity { CustomerId = 1, Name = "Bob" });
                return 0;
            },
            staging
        );

        // 保存フックの After が走っている間に、別の書き手が同じ行を削除した
        store.Remove(typeof(CustomerEntity), 1).Should().BeTrue();

        var act = () => store.Publish(staging, ConcurrencyMode.Optimistic);

        act.Should()
            .Throw<SaveConflictException>("消えた行への更新は実 DB の UPDATE 0 行と同じ扱いになる")
            .Which.Reason.Should()
            .Be(SaveConflictReason.NotFound);

        store.Snapshot<CustomerEntity>().Should().BeEmpty("削除された行は staged 更新で復活しない");
    }

    /// <summary>
    /// 対照: staged 側が「削除」なら、行がその間に消えていても競合にしない（既に無い行の削除は実 DB でも no-op）。
    /// </summary>
    [Fact(DisplayName = "[InMemory/Atomicity] Publish: 削除された行への staged 削除は競合にしない")]
    public void Publish_StagedDeleteOfRowDeletedMeanwhile_IsNoOp()
    {
        var store = new InMemoryDataStore();
        store.Put(new CustomerEntity { CustomerId = 1, Name = "Alice" });

        var staging = new InMemorySaveStaging();
        store.Write(
            scope =>
            {
                scope.Remove(typeof(CustomerEntity), 1);
                return 0;
            },
            staging
        );

        store.Remove(typeof(CustomerEntity), 1).Should().BeTrue();

        var act = () => store.Publish(staging, ConcurrencyMode.Optimistic);

        act.Should().NotThrow("削除したい行が既に無いのは矛盾ではない");
        store.Snapshot<CustomerEntity>().Should().BeEmpty();
    }

    /// <summary>
    /// 版を持つ型でも「更新として開始した行が消えていた」は <see cref="SaveConflictReason.NotFound"/>。
    /// </summary>
    /// <remarks>
    /// 存否の判定は版の比較より先に行う（無くなった行に対して版を比べても何も言えない）。旧実装は版比較が先だったため、
    /// 版を持つ型では「行が消えている」事実に到達せず <see cref="SaveConflictReason.Modified"/> を返していた
    /// ＝呼び出し側が再取得しても行が無く、再試行の指示が空振りする。
    /// </remarks>
    [Fact(
        DisplayName = "[InMemory/Atomicity] Publish: 版あり型でも削除された行への staged 更新は NotFound"
    )]
    public void Publish_VersionedRowDeletedMeanwhile_ThrowsNotFound()
    {
        var store = new InMemoryDataStore();
        store.Put(new VersionedRowEntity { Id = 1, Name = "Alice" });

        var staging = new InMemorySaveStaging();
        store.Write(
            scope =>
            {
                scope.Put(new VersionedRowEntity { Id = 1, Name = "Bob" });
                return 0;
            },
            staging
        );

        store.Remove(typeof(VersionedRowEntity), 1).Should().BeTrue();

        var act = () => store.Publish(staging, ConcurrencyMode.Optimistic);

        act.Should()
            .Throw<SaveConflictException>()
            .Which.Reason.Should()
            .Be(
                SaveConflictReason.NotFound,
                "版の有無に関わらず、消えた行への更新は「変更された」ではなく「無くなった」"
            );

        store.Snapshot<VersionedRowEntity>().Should().BeEmpty();
    }

    /// <summary>
    /// 版を持つ型でも、staged 側が「削除」なら行が既に消えていることは競合にしない（実 DB でも no-op）。
    /// </summary>
    /// <remarks>
    /// 旧実装では版比較が先に成立してしまい、版を持つ型に限ってこの黙認が効かず
    /// <see cref="SaveConflictReason.Modified"/> になっていた。
    /// </remarks>
    [Fact(
        DisplayName = "[InMemory/Atomicity] Publish: 版あり型でも削除された行への staged 削除は競合にしない"
    )]
    public void Publish_VersionedStagedDeleteOfRowDeletedMeanwhile_IsNoOp()
    {
        var store = new InMemoryDataStore();
        store.Put(new VersionedRowEntity { Id = 1, Name = "Alice" });

        var staging = new InMemorySaveStaging();
        store.Write(
            scope =>
            {
                scope.Remove(typeof(VersionedRowEntity), 1);
                return 0;
            },
            staging
        );

        store.Remove(typeof(VersionedRowEntity), 1).Should().BeTrue();

        var act = () => store.Publish(staging, ConcurrencyMode.Optimistic);

        act.Should().NotThrow("削除したい行が既に無いのは版があっても矛盾ではない");
        store.Snapshot<VersionedRowEntity>().Should().BeEmpty();
    }

    /// <summary>
    /// 対照: 行が「残っていて」他者に更新されていた場合は、版を持つ型では
    /// <see cref="SaveConflictReason.Modified"/>（存否判定を先にしても版検証は失われない）。
    /// </summary>
    [Fact(
        DisplayName = "[InMemory/Atomicity] Publish: 版あり型で残っている行の他者更新は Modified"
    )]
    public void Publish_VersionedRowUpdatedMeanwhile_ThrowsModified()
    {
        var store = new InMemoryDataStore();
        store.Put(new VersionedRowEntity { Id = 1, Name = "Alice" });

        var staging = new InMemorySaveStaging();
        store.Write(
            scope =>
            {
                scope.Put(new VersionedRowEntity { Id = 1, Name = "Bob" });
                return 0;
            },
            staging
        );

        store.Put(new VersionedRowEntity { Id = 1, Name = "Carol" });

        var act = () => store.Publish(staging, ConcurrencyMode.Optimistic);

        act.Should()
            .Throw<SaveConflictException>()
            .Which.Reason.Should()
            .Be(SaveConflictReason.Modified);
    }

    /// <summary>
    /// 対照: 行が削除ではなく「更新」されていた場合は後勝ちで staged スナップショットが適用される
    /// （版を持たない型の契約は last-write-wins のまま）。
    /// </summary>
    [Fact(DisplayName = "[InMemory/Atomicity] Publish: 他者の更新は後勝ちで上書きされる")]
    public void Publish_RowUpdatedMeanwhile_StillWins()
    {
        var store = new InMemoryDataStore();
        store.Put(new CustomerEntity { CustomerId = 1, Name = "Alice" });

        var staging = new InMemorySaveStaging();
        store.Write(
            scope =>
            {
                scope.Put(new CustomerEntity { CustomerId = 1, Name = "Bob" });
                return 0;
            },
            staging
        );

        store.Put(new CustomerEntity { CustomerId = 1, Name = "Carol" });

        store.Publish(staging, ConcurrencyMode.Optimistic);

        store.Snapshot<CustomerEntity>().Should().ContainSingle().Which.Name.Should().Be("Bob");
    }
}

/// <summary>
/// 循環カスケード再現用のルート型。葉への カスケード（削除できる）と自己参照のカスケード（固定走査では表現できず
/// <see cref="NotSupportedException"/> になる）をこの順で宣言する。
/// </summary>
[Table("cascade_roots")]
public sealed class CascadeRootEntity : EntityBase
{
    /// <summary>主キー</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>葉の子コレクション（カスケード削除の対象・先に走査される）</summary>
    [NavigationReference("cascade_roots", "Id", "cascade_leaves", "RootId", true, true, false)]
    public ICollection<CascadeLeafEntity> Leaves { get; set; } = new List<CascadeLeafEntity>();

    /// <summary>自分自身への子コレクション＝循環カスケード（走査が到達した時点で中断する）</summary>
    [NavigationReference("cascade_roots", "Id", "cascade_roots", "ParentId", true, true, false)]
    public ICollection<CascadeRootEntity> Children { get; set; } = new List<CascadeRootEntity>();

    /// <summary>自己参照 FK 列</summary>
    public int? ParentId { get; set; }
}

/// <summary>
/// 版列（rowversion 相当）を持つテスト専用型。<c>InMemoryFixture</c> の図には版列が無いため、
/// <c>Publish</c> の「版あり／版なし」の 4 象限を同じストア型のまま並べるために用意する。
/// </summary>
/// <remarks>
/// 版列の判定は <c>EntitySaveMetadata</c> がリフレクションで <c>[StoreGeneratedColumn]</c> を読むだけなので、
/// 生成エンティティとまったく同じ経路で「版を持つ型」として扱われる。
/// </remarks>
[Table("versioned_rows")]
public sealed class VersionedRowEntity : EntityBase
{
    /// <summary>主キー</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>任意の値列（更新の有無を観測するために使う）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>版列（DB 採番相当＝ストアが単調増加の擬似版を書き込む）</summary>
    [StoreGeneratedColumn]
    public byte[]? RowVer { get; set; }
}

/// <summary>循環カスケード再現用の葉型（削除自体は成功する側）。</summary>
[Table("cascade_leaves")]
public sealed class CascadeLeafEntity : EntityBase
{
    /// <summary>主キー</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>親への FK 列</summary>
    public int RootId { get; set; }
}
