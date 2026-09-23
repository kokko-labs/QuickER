using System.Linq;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝RowState 等が他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// 削除行（Removed）の保存・受理まわりの意味論を、コミット済みフィクスチャ
/// （<c>InMemoryFixture.g.cs</c> の customers / orders / customer_profiles）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則は 3 つ。
/// (1) 削除行はキーだけが保存に参加する＝未入力の非キー列が <c>Required</c> で保存グラフ生成を止めない
/// （キーは削除に必要なので載る）。
/// (2) <c>AcceptRemoved</c> は退避要素を Added へ戻す＝保存確定後に同じインスタンスを戻すと挿入対象になる
/// （旧: Removed のまま放置され、戻した行が永久に INSERT されなかった）。
/// (3) コレクションの <c>AcceptChanges</c> は削除確定行（MarkRemoved でコレクションに残した行）を
/// 削除追跡を経由せずに外し、外れた行は Removed のまま・_removed にも残らない
/// （旧: Unchanged へ戻り、消したはずの行が通常行として画面に復活していた）。単一カスケード子は外す先が
/// 無いので Removed のまま残る＝アプリ側で null にする（docs / XmlDoc の明記とセット）。
/// </remarks>
public sealed class EditModelRemovedRowTests
{
    /// <summary>注文 1 件と 1:1 プロファイルを持つ Unchanged な CustomerEntity を作る。</summary>
    private static CustomerEntity BuildCustomerEntity()
    {
        var entity = new CustomerEntity
        {
            CustomerId = 1,
            Name = "customer",
            Balance = 100m,
        };

        entity.Orders.Add(
            new OrderEntity
            {
                OrderId = 100,
                CustomerId = 1,
                Amount = 5m,
                Memo = "memo",
            }
        );

        entity.CustomerProfile = new CustomerProfileEntity
        {
            ProfileId = 10,
            CustomerId = 1,
            Bio = "bio",
        };

        entity.MarkUnchanged();

        foreach (var order in entity.Orders)
        {
            order.MarkUnchanged();
        }

        entity.CustomerProfile.MarkUnchanged();
        return entity;
    }

    /// <summary>ロード済み（Unchanged）の CustomerEditModel を作る。</summary>
    private static CustomerEditModel BuildLoadedModel() =>
        new CustomerMapper().CreateEditModel(BuildCustomerEntity());

    [Fact]
    public void 削除行の必須欄が空でも保存用のグラフ生成は失敗せずキーが載る()
    {
        var model = BuildLoadedModel();
        var row = model.Orders[0];

        // 必須列 Amount を空欄にしてから行を削除する（レビュー再現プローブと同じ手順）
        row.BindingAmount = string.Empty;
        model.Orders.Remove(row);

        model.Validate().Should().BeTrue("削除行は検証対象外");

        var entity = new CustomerMapper().CreateEntity(model, includeRemoved: true);

        var removed = entity.Orders.Should().ContainSingle().Subject;
        removed.RowState.Should().Be(RowState.Removed);
        removed.OrderId.Should().Be(100, "削除にはキーが要るため、キーは削除行でも必ず載る");
    }

    [Fact]
    public void コレクションに残した削除マーク行も未入力の非キー列で保存グラフ生成が止まらない()
    {
        var model = BuildLoadedModel();
        var row = model.Orders[0];

        row.BindingAmount = string.Empty;
        row.MarkRemoved();

        model.Validate().Should().BeTrue();

        var entity = new CustomerMapper().CreateEntity(model, includeRemoved: true);

        var removed = entity.Orders.Should().ContainSingle().Subject;
        removed.RowState.Should().Be(RowState.Removed);
        removed.OrderId.Should().Be(100);
    }

    [Fact]
    public void 削除行でも入力済みの値とキーは写り未入力の非キー列だけが初期値に残る()
    {
        var model = BuildLoadedModel();
        var row = model.Orders[0];

        row.BindingAmount = string.Empty;
        model.Orders.Remove(row);

        var entity = new CustomerMapper().CreateEntity(model, includeRemoved: true);
        var removed = entity.Orders.Single();

        removed.CustomerId.Should().Be(1, "入力済みの値は削除行でも従来どおり写る");
        removed.Memo.Should().Be("memo");
        removed
            .Amount.Should()
            .Be(0m, "未入力の非キー列は Required で止めずスキップし、初期値のまま残る");
    }

    [Fact]
    public void 保存確定後に同じインスタンスを戻すと挿入対象になる()
    {
        var model = BuildLoadedModel();
        var row = model.Orders[0];

        model.Orders.Remove(row);
        model.AcceptChanges();

        model.Orders.RemovedItems.Should().BeEmpty("退避リストは受理で解放される");
        row.RowState.Should()
            .Be(RowState.Added, "行の実体は消えたので、戻すなら新しい行の挿入になる");

        model.Orders.Add(row);

        row.RowState.Should()
            .Be(RowState.Added, "受理済みの行は退避リストに居ないため復元では上書きされない");
        model.Orders.HasChanges.Should().BeTrue("戻した行は次の保存で INSERT される");
    }

    [Fact]
    public void AcceptChangesは削除確定行をコレクションから外しRemovedのまま追跡にも残さない()
    {
        var model = BuildLoadedModel();
        var row = model.Orders[0];

        row.MarkRemoved();
        model.Orders.AcceptChanges();

        model.Orders.Should().NotContain(row, "削除が確定した行を通常行として画面に残さない");
        row.RowState.Should()
            .Be(RowState.Removed, "外れた行は保存済みの削除を表したまま＝受理で Added にしない");
        model
            .Orders.RemovedItems.Should()
            .BeEmpty("削除追跡を経由せずに外す＝次の保存の削除対象にならない");
    }

    [Fact]
    public void 親のAcceptChanges経由でも削除確定行はコレクションから外れる()
    {
        var model = BuildLoadedModel();
        var row = model.Orders[0];

        row.MarkRemoved();
        model.AcceptChanges(includeChildren: true);

        model.Orders.Should().NotContain(row);
        row.RowState.Should().Be(RowState.Removed);
        model.Orders.RemovedItems.Should().BeEmpty();
    }

    [Fact]
    public void includeRemovedがfalseなら削除マークした単一子は結果グラフに載らない()
    {
        var model = BuildLoadedModel();
        model.CustomerProfile!.MarkRemoved();

        var forDisplay = new CustomerMapper().CreateEntity(model, includeRemoved: false);
        var forSave = new CustomerMapper().CreateEntity(model, includeRemoved: true);

        forDisplay
            .CustomerProfile.Should()
            .BeNull("表示用のグラフに削除予定の単一子を載せない（XmlDoc の宣言どおり）");
        forSave.CustomerProfile.Should().NotBeNull("保存用のグラフには削除対象として載る");
        forSave.CustomerProfile!.RowState.Should().Be(RowState.Removed);
    }

    [Fact]
    public void 削除マークした単一カスケード子は受理後もRemovedのまま残る()
    {
        var model = BuildLoadedModel();
        var child = model.CustomerProfile!;

        child.MarkRemoved();
        model.AcceptChanges(includeChildren: true);

        model.CustomerProfile.Should().BeSameAs(child, "単一子には外す先のコレクションが無い");
        child
            .RowState.Should()
            .Be(
                RowState.Removed,
                "Unchanged へ戻すと消した行が幽霊として次の保存に載る。null へ落とすのはアプリ側の役目（XmlDoc / docs 明記）"
            );
    }
}
