using System;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成 EditModel と <see cref="EditModelCollection{T}"/> のライフサイクル・グラフ操作 API を、
/// コミット済みフィクスチャ（<c>GeneratedFixture.g.cs</c>）の実型に対して検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 検証対象は BeginEdit/EndEdit/CancelEdit（IEditableObject）、RevertInput、AcceptChanges、HasGraphChanges、
/// CollectErrors、兄弟／親ナビゲーション（GetNext/GetPrevious/MoveTo*/RemoveFromParent/IndexInParent/ParentModel）、
/// および <see cref="EditModelCollection{T}"/> の AddRange/InsertRange/RemoveAll/RemoveRange/MoveTo/削除追跡。
/// </remarks>
public sealed class EditModelLifecycleTests
{
    // ===== テストデータ生成 =====

    /// <summary>子（Orders×n・CustomerProfile）を持つ Unchanged な CustomerEntity を作る。</summary>
    private static CustomerEntity BuildCustomerEntity(
        int id,
        string name,
        bool active,
        int orderCount
    )
    {
        var entity = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            IsActive = IsActiveValue.Create(active),
            Balance = BalanceValue.Create(100m),
        };

        for (var i = 0; i < orderCount; i++)
        {
            entity.Orders.Add(
                new OrderEntity
                {
                    OrderId = OrderIdValue.Create(id * 100 + i),
                    CustomerId = CustomerIdValue.Create(id),
                    Amount = AmountValue.Create(10m + i),
                    Memo = MemoValue.Create($"memo{i}"),
                }
            );
        }

        entity.CustomerProfile = new CustomerProfileEntity
        {
            ProfileId = ProfileIdValue.Create(id),
            CustomerId = CustomerIdValue.Create(id),
            Bio = BioValue.Create("bio"),
        };

        return entity;
    }

    /// <summary>ロード済み（RowState=Unchanged）の CustomerEditModel を作る。</summary>
    private static CustomerEditModel LoadedCustomer(int orderCount = 2) =>
        new CustomerMapper().CreateEditModel(BuildCustomerEntity(1, "Alice", true, orderCount));

    /// <summary>ロード済み（RowState=Unchanged）の OrderEditModel を作る。</summary>
    private static OrderEditModel LoadedOrder(int orderId = 1) =>
        new OrderMapper().CreateEditModel(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(orderId),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(5m),
                Memo = MemoValue.Create("m"),
            }
        );

    // ===== BeginEdit / EndEdit / CancelEdit =====

    [Fact(
        DisplayName = "CancelEdit: 編集をキャンセルすると入力値も RowState もスナップショットへ戻る"
    )]
    public void CancelEditで値とRowStateが戻る()
    {
        var m = LoadedCustomer();
        m.RowState.Should().Be(RowState.Unchanged);

        m.BeginEdit();
        m.BindingName = "Bob";
        m.Name!.Value.Should().Be("Bob");
        m.RowState.Should().Be(RowState.Updated);

        m.CancelEdit();

        m.BindingName.Should().Be("Alice");
        m.Name!.Value.Should().Be("Alice");
        m.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(DisplayName = "EndEdit: コミットすると変更は保持される")]
    public void EndEditで変更が保持される()
    {
        var m = LoadedCustomer();

        m.BeginEdit();
        m.BindingName = "Bob";
        m.EndEdit();

        m.BindingName.Should().Be("Bob");
        m.Name!.Value.Should().Be("Bob");
        m.RowState.Should().Be(RowState.Updated);
    }

    [Fact(
        DisplayName = "BeginEdit していない状態の CancelEdit/EndEdit は no-op（例外なし・値不変）"
    )]
    public void 未編集のキャンセルは何もしない()
    {
        var m = LoadedCustomer();

        m.CancelEdit();
        m.EndEdit();

        m.BindingName.Should().Be("Alice");
        m.RowState.Should().Be(RowState.Unchanged);
    }

    // ===== RevertInput =====

    [Fact(DisplayName = "RevertInput: 確定値を各バインディングへ書き戻し、変換エラーを消す")]
    public void RevertInputでエラーが消え値が戻る()
    {
        var m = LoadedCustomer();

        // 数値列に変換不能な入力 → エラーが立つが確定値 CustomerId=1 は据え置き
        m.BindingCustomerId = "abc";
        m.HasErrors.Should().BeTrue();
        m.CustomerId!.Value.Should().Be(1);

        m.RevertInput();

        m.BindingCustomerId.Should().Be("1");
        m.HasErrors.Should().BeFalse();
        m.CustomerId!.Value.Should().Be(1);
    }

    // ===== AcceptChanges =====

    [Fact(DisplayName = "AcceptChanges(false): 自身だけ Unchanged に戻し、子は据え置く")]
    public void AcceptChanges_自身のみ()
    {
        var m = LoadedCustomer();
        m.BindingName = "Bob";
        m.Orders[0].BindingMemo = "changed";
        m.RowState.Should().Be(RowState.Updated);
        m.Orders[0].RowState.Should().Be(RowState.Updated);

        m.AcceptChanges(includeChildren: false);

        m.RowState.Should().Be(RowState.Unchanged);
        // 子はそのまま
        m.Orders[0].RowState.Should().Be(RowState.Updated);
    }

    [Fact(DisplayName = "AcceptChanges(true): グラフ全体を Unchanged にし、削除追跡もクリアする")]
    public void AcceptChanges_グラフ全体()
    {
        var m = LoadedCustomer();
        m.Orders[0].BindingMemo = "changed";
        m.Orders.RemoveAt(1); // 既存要素の削除 → 削除追跡
        m.Orders.RemovedItems.Should().HaveCount(1);
        m.HasGraphChanges().Should().BeTrue();

        m.AcceptChanges(includeChildren: true);

        m.RowState.Should().Be(RowState.Unchanged);
        m.Orders[0].RowState.Should().Be(RowState.Unchanged);
        m.Orders.RemovedItems.Should().BeEmpty();
        m.HasGraphChanges().Should().BeFalse();
    }

    // ===== HasGraphChanges =====

    [Fact(DisplayName = "HasGraphChanges: 子だけ変更があると true=検出、false=自身のみで未検出")]
    public void HasGraphChanges_子の変更()
    {
        var m = LoadedCustomer();
        m.HasGraphChanges().Should().BeFalse();

        m.Orders[0].BindingMemo = "x";

        m.HasGraphChanges(includeChildren: true).Should().BeTrue();
        m.HasGraphChanges(includeChildren: false).Should().BeFalse();
    }

    // ===== CollectErrors =====

    [Fact(DisplayName = "CollectErrors: 根と子（Orders[0]）の必須エラーがパス付きで収集される")]
    public void CollectErrors_パス付き収集()
    {
        var m = new CustomerEditModel();
        m.Orders.Add(new OrderEditModel());

        m.Validate(includeChildren: true).Should().BeFalse();

        var errors = m.CollectErrors(includeChildren: true).ToList();

        // 根の必須エラー（パス空）
        errors.Should().Contain(e => e.Path == string.Empty && e.Property == "BindingName");
        errors.Should().Contain(e => e.Path == string.Empty && e.Property == "BindingCustomerId");
        // 子 Order の必須エラー（パス Orders[0]）
        errors.Should().Contain(e => e.Path == "Orders[0]" && e.Property == "BindingOrderId");
        errors.Should().Contain(e => e.Path == "Orders[0]" && e.Property == "BindingAmount");
    }

    [Fact(DisplayName = "CollectErrors(false): 子を辿らず自身のエラーのみ")]
    public void CollectErrors_自身のみ()
    {
        var m = new CustomerEditModel();
        m.Orders.Add(new OrderEditModel());

        m.Validate(includeChildren: true).Should().BeFalse();

        var ownErrors = m.CollectErrors(includeChildren: false).ToList();

        ownErrors.Should().OnlyContain(e => e.Path == string.Empty);
    }

    // ===== 再ロードでの子コレクション差し替え（ChildLink の遅延解決） =====

    [Fact(
        DisplayName = "再ロード: Orders が別インスタンスへ差し替わっても親 Validate/CollectErrors は新しいコレクションを見る"
    )]
    public void 再ロード後のValidateは新しい子コレクションを見る()
    {
        var mapper = new CustomerMapper();
        var m = mapper.CreateEditModel(BuildCustomerEntity(1, "Alice", true, 1));

        // ここで ChildLinks が確定する（旧実装はこの時点の Orders インスタンスを捕捉していた）
        m.Validate().Should().BeTrue();

        // ApplyToEditModel は Orders を丸ごと新しい EditModelCollection へ差し替える
        mapper.ApplyToEditModel(BuildCustomerEntity(2, "Bob", true, 1), m);

        // 差し替え後の子に必須エラーを作る（Amount は必須・空入力で確定値が null になる）
        m.Orders[0].BindingAmount = string.Empty;

        m.Validate().Should().BeFalse();
        m.CollectErrors()
            .Should()
            .Contain(e => e.Path == "Orders[0]" && e.Property == "BindingAmount");
    }

    [Fact(
        DisplayName = "再ロード: HasGraphChanges / AcceptChanges も差し替え後のコレクションを対象にする"
    )]
    public void 再ロード後の変更追跡は新しい子コレクションを対象にする()
    {
        var mapper = new CustomerMapper();
        var m = mapper.CreateEditModel(BuildCustomerEntity(1, "Alice", true, 2));

        // ここで ChildLinks が確定する
        m.HasGraphChanges().Should().BeFalse();

        mapper.ApplyToEditModel(BuildCustomerEntity(2, "Bob", true, 2), m);

        // 差し替え後のコレクションで「要素の変更」と「削除追跡」の両方を起こす
        m.Orders[0].BindingMemo = "changed";
        m.Orders.RemoveAt(1);

        m.HasGraphChanges().Should().BeTrue();

        m.AcceptChanges();

        m.Orders[0].RowState.Should().Be(RowState.Unchanged);
        m.Orders.RemovedItems.Should().BeEmpty();
        m.HasGraphChanges().Should().BeFalse();
    }

    // ===== GetNext / GetPrevious =====

    [Fact(DisplayName = "GetNext/GetPrevious: 兄弟を辿り、端・非所有では null")]
    public void GetNextとGetPrevious()
    {
        var a = new CustomerEditModel();
        var b = new CustomerEditModel();
        var c = new CustomerEditModel();
        var col = new EditModelCollection<CustomerEditModel> { a, b, c };
        col.Should().HaveCount(3);

        a.GetNext().Should().BeSameAs(b);
        b.GetPrevious().Should().BeSameAs(a);
        c.GetNext().Should().BeNull();
        a.GetPrevious().Should().BeNull();

        new CustomerEditModel().GetNext().Should().BeNull();
    }

    // ===== 位置プロパティ・移動 =====

    [Fact(DisplayName = "IndexInParent/IsFirstInParent/IsLastInParent が現在位置を反映する")]
    public void 位置プロパティ()
    {
        var a = new CustomerEditModel();
        var b = new CustomerEditModel();
        var c = new CustomerEditModel();
        var col = new EditModelCollection<CustomerEditModel> { a, b, c };
        col.Should().HaveCount(3);

        a.IndexInParent.Should().Be(0);
        a.IsFirstInParent.Should().BeTrue();
        a.IsLastInParent.Should().BeFalse();
        b.IndexInParent.Should().Be(1);
        b.IsFirstInParent.Should().BeFalse();
        c.IsLastInParent.Should().BeTrue();

        // 非所有は -1・false
        var orphan = new CustomerEditModel();
        orphan.IndexInParent.Should().Be(-1);
        orphan.IsFirstInParent.Should().BeFalse();
        orphan.IsLastInParent.Should().BeFalse();
    }

    [Fact(DisplayName = "MoveToFirst/Last/Next/Previous が所有コレクション内で順序を並べ替える")]
    public void 移動メソッド()
    {
        var a = new CustomerEditModel();
        var b = new CustomerEditModel();
        var c = new CustomerEditModel();
        var col = new EditModelCollection<CustomerEditModel> { a, b, c };
        col.IndexOf(b).Should().Be(1);

        c.MoveToFirst().Should().BeTrue();
        col.IndexOf(c).Should().Be(0); // [c,a,b]

        c.MoveToLast().Should().BeTrue();
        col.IndexOf(c).Should().Be(2); // [a,b,c]

        a.MoveToNext().Should().BeTrue();
        col.IndexOf(a).Should().Be(1); // [b,a,c]

        c.MoveToPrevious().Should().BeTrue();
        col.IndexOf(c).Should().Be(1); // [b,c,a]

        // 既に端にいる場合は false
        col[0].MoveToPrevious().Should().BeFalse();
        col[^1].MoveToNext().Should().BeFalse();
    }

    // ===== RemoveFromParent =====

    [Fact(DisplayName = "RemoveFromParent: 所有コレクションから外し、非所有では false")]
    public void RemoveFromParent()
    {
        var a = new CustomerEditModel();
        var b = new CustomerEditModel();
        var col = new EditModelCollection<CustomerEditModel> { a, b };

        b.RemoveFromParent().Should().BeTrue();
        col.Should().Contain(a).And.NotContain(b);
        // 既に外れているので false
        b.RemoveFromParent().Should().BeFalse();
        new CustomerEditModel().RemoveFromParent().Should().BeFalse();
    }

    // ===== ParentModel（型付き） =====

    [Fact(
        DisplayName = "ParentModel: 子（Orders 要素・CustomerProfile）がカスケード親 Customer を型付きで指す"
    )]
    public void ParentModel_型付き親参照()
    {
        var m = LoadedCustomer();

        CustomerEditModel? orderParent = m.Orders[0].ParentModel;
        orderParent.Should().BeSameAs(m);

        CustomerEditModel? profileParent = m.CustomerProfile!.ParentModel;
        profileParent.Should().BeSameAs(m);
    }

    // ===== EditModelCollection: AddRange / InsertRange =====

    [Fact(DisplayName = "AddRange/InsertRange: 複数要素の末尾追加・指定位置挿入")]
    public void AddRangeとInsertRange()
    {
        var col = new EditModelCollection<OrderEditModel>();
        var o1 = new OrderEditModel();
        var o2 = new OrderEditModel();
        var o3 = new OrderEditModel();
        col.AddRange(new[] { o1, o2, o3 });
        col.Should().HaveCount(3);

        var i1 = new OrderEditModel();
        var i2 = new OrderEditModel();
        col.InsertRange(1, new[] { i1, i2 });

        col.Should().HaveCount(5);
        col[0].Should().BeSameAs(o1);
        col[1].Should().BeSameAs(i1);
        col[2].Should().BeSameAs(i2);
        col[3].Should().BeSameAs(o2);
        col[4].Should().BeSameAs(o3);
        // 挿入要素も所有者が設定される
        i1.IndexInParent.Should().Be(1);
    }

    // ===== EditModelCollection: 削除追跡・AcceptRemoved =====

    [Fact(
        DisplayName = "RemovedItems/AcceptRemoved: 既存要素は削除追跡され Added 要素は追跡されない"
    )]
    public void 削除追跡()
    {
        var existing = new OrderEditModel(); // 既定 Unchanged → 追跡対象
        var added = new OrderEditModel();
        added.MarkAdded(); // Added → 追跡対象外
        var col = new EditModelCollection<OrderEditModel> { existing, added };

        col.Remove(existing);
        col.RemovedItems.Should().ContainSingle().Which.Should().BeSameAs(existing);
        existing.RowState.Should().Be(RowState.Removed);
        col.HasChanges.Should().BeTrue();

        col.Remove(added);
        col.RemovedItems.Should().ContainSingle(); // added は追跡されない

        col.AcceptRemoved();
        col.RemovedItems.Should().BeEmpty();
        col.HasChanges.Should().BeFalse();
    }

    // ===== EditModelCollection: RemoveAll / RemoveRange =====

    [Fact(DisplayName = "RemoveAll: 全既存要素を削除追跡付きで外す")]
    public void RemoveAll()
    {
        var col = new EditModelCollection<OrderEditModel>
        {
            new OrderEditModel(),
            new OrderEditModel(),
            new OrderEditModel(),
        };

        col.RemoveAll();

        col.Should().BeEmpty();
        col.RemovedItems.Should().HaveCount(3);
    }

    [Fact(DisplayName = "RemoveRange: 指定範囲を削除し、範囲外指定は ArgumentOutOfRangeException")]
    public void RemoveRange()
    {
        var a = new OrderEditModel();
        var b = new OrderEditModel();
        var c = new OrderEditModel();
        var d = new OrderEditModel();
        var col = new EditModelCollection<OrderEditModel> { a, b, c, d };

        col.RemoveRange(1, 2); // b,c を削除

        col.Should().HaveCount(2);
        col[0].Should().BeSameAs(a);
        col[1].Should().BeSameAs(d);

        var bad = () => col.RemoveRange(-1, 1);
        bad.Should().Throw<ArgumentOutOfRangeException>();
        var bad2 = () => col.RemoveRange(0, 99);
        bad2.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ===== EditModelCollection: MoveTo =====

    [Fact(DisplayName = "MoveTo: 指定要素を指定位置へ移動、同位置・非所有は false")]
    public void MoveTo()
    {
        var a = new OrderEditModel();
        var b = new OrderEditModel();
        var c = new OrderEditModel();
        var col = new EditModelCollection<OrderEditModel> { a, b, c };

        col.MoveTo(c, 0).Should().BeTrue();
        col.IndexOf(c).Should().Be(0);
        col.IndexOf(a).Should().Be(1);
        col.IndexOf(b).Should().Be(2);

        col.MoveTo(c, 0).Should().BeFalse(); // 既に同位置
        col.MoveTo(new OrderEditModel(), 0).Should().BeFalse(); // 非所有
    }

    // ===== EditModelCollection: AcceptChanges / Validate / HasChanges =====

    [Fact(DisplayName = "コレクションの HasChanges/AcceptChanges: 要素変更を検出し受理で解消する")]
    public void コレクションのHasChangesとAcceptChanges()
    {
        var col = new EditModelCollection<OrderEditModel> { LoadedOrder(1), LoadedOrder(2) };
        col.HasChanges.Should().BeFalse();

        col[0].BindingMemo = "x";
        col.HasChanges.Should().BeTrue();

        col.AcceptChanges();

        col.HasChanges.Should().BeFalse();
        col[0].RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(DisplayName = "コレクションの Validate: 全要素が有効なら true、1 つでも無効なら false")]
    public void コレクションのValidate()
    {
        var invalid = new EditModelCollection<OrderEditModel> { new OrderEditModel() };
        invalid.Validate().Should().BeFalse();

        var valid = new EditModelCollection<OrderEditModel> { LoadedOrder() };
        valid.Validate().Should().BeTrue();
    }
}
