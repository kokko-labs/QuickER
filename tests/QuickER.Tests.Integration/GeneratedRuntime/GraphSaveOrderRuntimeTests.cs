using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedBackendParityFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// QuickER 版 Repository のグラフ保存が、削除する行と値の重ならない新しい行をグラフの順のまま書き、子を別の親へ
/// 移してから元の親を消す保存や、新しい行を別のルートが参照する保存を壊さないことを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 後回しにするのは、同じ保存で削除する行の主キーか一意な値を取る新しい行と、その下の行だけ。ここで流す保存の新しい行は
/// どれも削除する行と値が重ならないので、どれも並びどおりに書かれる。新しい行を後回しにすると、元の親の削除が子の移動
/// （UPDATE）より先に走るか、新しい行を指す更新が新しい行の挿入より先に走り、NO ACTION の外部キーに阻まれる。
/// </para>
/// <para>
/// EF Core は子を別の親へ移して元の親を消す保存を、並び順に依らず断るので、ここでは流さない（実装先どうしの一致は別の区切りで扱う）。
/// サーバーが QuickER 版のリモートは同じ保存の処理を通るので、一緒に流す。
/// </para>
/// </remarks>
public sealed class GraphSaveOrderRuntimeTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    public static TheoryData<ParityBackendKind> Kinds =>
        new() { ParityBackendKind.Ado, ParityBackendKind.Remote };

    [Theory(
        DisplayName = "グラフ保存の順序: 子を既存の別の親へ移してから元の親を消す保存は、移す更新が先に並べば成功する"
    )]
    [MemberData(nameof(Kinds))]
    public async Task MoveChildToExistingParent_ThenDeleteOldParent_Succeeds(ParityBackendKind kind)
    {
        await using var backend = await SeedAsync(kind);

        var oldParent = Customer(1, "A", null, RowState.Removed);
        var newParent = Customer(3, "C", null, RowState.Unchanged);
        newParent.Customers.Add(Customer(2, "B", 3, RowState.Updated));

        await backend.Customers.SaveAsync([newParent, oldParent], cancellationToken: Ct);

        (await ReadReferrerAsync(backend, 2)).Should().Be(3);
        (await backend.Customers.GetByIdAsync(CustomerIdValue.Create(1), Ct)).Should().BeNull();
    }

    [Theory(
        DisplayName = "グラフ保存の順序: 子を新しい親へ移してから元の親を消す保存も、新しい親を後回しにせず成功する"
    )]
    [MemberData(nameof(Kinds))]
    public async Task MoveChildToNewParent_ThenDeleteOldParent_Succeeds(ParityBackendKind kind)
    {
        await using var backend = await SeedAsync(kind);

        var oldParent = Customer(1, "A", null, RowState.Removed);
        var newParent = Customer(4, "D", null, RowState.Added);
        newParent.Customers.Add(Customer(2, "B", 4, RowState.Updated));

        await backend.Customers.SaveAsync([newParent, oldParent], cancellationToken: Ct);

        (await ReadReferrerAsync(backend, 2)).Should().Be(4);
        (await backend.Customers.GetByIdAsync(CustomerIdValue.Create(1), Ct)).Should().BeNull();
    }

    /// <summary>新しい行を別のルートが参照する保存で、削除する行に値の重ならない新しい行は後回しにしないことを確かめる形</summary>
    public static TheoryData<ParityBackendKind, int> ReferencedNewRowCases =>
        new()
        {
            { ParityBackendKind.Ado, 1 },
            { ParityBackendKind.Ado, 5 },
            { ParityBackendKind.Remote, 1 },
            { ParityBackendKind.Remote, 5 },
        };

    [Theory(
        DisplayName = "グラフ保存の順序: 新しい親と、それを指すよう更新した別のルートを、行の削除と一緒に保存しても、新しい親を後回しにせず成功する"
    )]
    [MemberData(nameof(ReferencedNewRowCases))]
    public async Task NewRowReferencedByAnotherRoot_IsNotHeldBack(
        ParityBackendKind kind,
        int deletedId
    )
    {
        await using var backend = await SeedAsync(kind);
        await backend.Outside.Customers.InsertAsync(
            (CustomerEntity)new CustomerRow(5, "E").Build(),
            Ct
        );

        // 消すのは元の親 A(1) か、関係のない E(5)。どちらも D とは値が重ならない。
        // 子 B は D のコレクションに入れず、別のルートとして渡す（新しい親を後回しにすると、B の更新が D の挿入より先に走る）
        var newParent = Customer(4, "D", null, RowState.Added);
        var child = Customer(2, "B", 4, RowState.Updated);
        var deleted = Customer(deletedId, deletedId == 1 ? "A" : "E", null, RowState.Removed);

        await backend.Customers.SaveAsync([newParent, child, deleted], cancellationToken: Ct);

        (await ReadReferrerAsync(backend, 2)).Should().Be(4);
        (await backend.Customers.GetByIdAsync(CustomerIdValue.Create(deletedId), Ct))
            .Should()
            .BeNull();
    }

    /// <summary>顧客 A(1)・C(3) と、A を紹介者に持つ B(2)</summary>
    private static async Task<ParityBackend> SeedAsync(ParityBackendKind kind)
    {
        var backend = await ParityBackend.CreateAsync(
            kind,
            new ParityBackendOptions(WithHooks: false),
            Ct
        );
        await backend.Outside.Customers.InsertAsync(
            (CustomerEntity)new CustomerRow(1, "A").Build(),
            Ct
        );
        await backend.Outside.Customers.InsertAsync(
            (CustomerEntity)new CustomerRow(3, "C").Build(),
            Ct
        );
        await backend.Outside.Customers.InsertAsync(
            (CustomerEntity)new CustomerRow(2, "B", Referrer: 1).Build(),
            Ct
        );

        return backend;
    }

    private static CustomerEntity Customer(int id, string name, int? referrer, RowState state)
    {
        var entity = (CustomerEntity)new CustomerRow(id, name, Referrer: referrer).Build();
        NewRootOp.ApplyState(entity, state);

        return entity;
    }

    private static async Task<int?> ReadReferrerAsync(ParityBackend backend, int id) =>
        (await backend.Outside.Customers.GetByIdAsync(CustomerIdValue.Create(id), Ct))
            ?.ReferrerId
            ?.Value;
}
