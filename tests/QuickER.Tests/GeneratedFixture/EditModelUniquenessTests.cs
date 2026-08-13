using System.Collections;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// EditModel 側のコレクション内重複検証（生成された制約テーブル <c>UniquenessConstraints</c> ＋
/// <see cref="EditModelUniquenessValidator"/>）を固定フィクスチャ上で検証する。
/// </summary>
/// <remarks>
/// フィクスチャの orders には単一列制約 <c>UQ_orders_memo</c>（NULL 許容列）と複合制約
/// （<c>customer_id</c>＋<c>amount</c>）がある。DB 照合（<c>ValidateUniqueAsync</c>）は別スイート
/// （<c>EditModelValidateUniqueRuntimeTests</c>）が担当し、ここは実 DB を使わない要素間比較だけを扱う。
/// </remarks>
public class EditModelUniquenessTests
{
    /// <summary>入力済みの注文 EditModel を組み立てる（必須項目はすべて埋める）</summary>
    private static OrderEditModel NewOrder(int orderId, int customerId, decimal amount, string memo)
    {
        var model = new OrderEditModel
        {
            BindingOrderId = orderId.ToString(),
            BindingCustomerId = customerId.ToString(),
            BindingAmount = amount.ToString(),
            BindingMemo = memo,
        };
        return model;
    }

    /// <summary>指定プロパティのエラー一覧を取り出す</summary>
    private static string[] GetErrors(EditModelBase model, string propertyName) =>
        ((IEnumerable)model.GetErrors(propertyName)).Cast<string>().ToArray();

    /// <summary>クラスが図の UNIQUE 制約を制約テーブルとして宣言する（構成列は確定値プロパティ名・宣言順）</summary>
    [Fact(DisplayName = "EditModel クラスが UNIQUE 制約を宣言する")]
    public void UniquenessConstraints_AreDeclaredOnClass()
    {
        var constraints = new OrderEditModel().UniquenessConstraints;

        constraints
            .Select(constraint => constraint.ConstraintName)
            .Should()
            .Equal("UQ_orders_memo", "UQ_orders_customer_id_amount");
        constraints[0].PropertyNames.Should().Equal(nameof(OrderEditModel.Memo));
        constraints[1]
            .PropertyNames.Should()
            .Equal(nameof(OrderEditModel.CustomerId), nameof(OrderEditModel.Amount));
    }

    /// <summary>値アクセサは確定値プロパティを 1 呼び出しで宣言順に読む（リフレクション無しの照合入力）</summary>
    [Fact(DisplayName = "制約の値アクセサが確定値を宣言順で返す")]
    public void UniquenessConstraints_ValueAccessor_ReadsConfirmedValues()
    {
        var model = NewOrder(10, 7, 120m, "apple pie");
        var constraints = model.UniquenessConstraints;

        // 値は確定値プロパティそのもの（このフィクスチャは VO 有効なので VO インスタンスが並ぶ）
        constraints[0].GetValues(model).Should().Equal(model.Memo);
        constraints[1].GetValues(model).Should().Equal(model.CustomerId, model.Amount);
    }

    /// <summary>UNIQUE 制約の無いテーブルの EditModel は基底の既定（空リスト）のまま</summary>
    [Fact(DisplayName = "制約なしテーブルの EditModel は空の制約テーブルを返す")]
    public void UniquenessConstraints_WithoutConstraints_AreEmpty()
    {
        new CustomerEditModel().UniquenessConstraints.Should().BeEmpty();
    }

    /// <summary>同じ値の組を持つ要素すべてに重複エラーが登録される（登録先は構成列のバインディングプロパティ）</summary>
    [Fact(DisplayName = "コレクション内の重複が全要素へ登録される")]
    public void Duplicate_RegistersErrorOnEveryMember()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 2, 50m, "apple pie"),
            NewOrder(12, 3, 20m, "banana"),
        };

        collection.Validate().Should().BeFalse();

        GetErrors(collection[0], nameof(OrderEditModel.BindingMemo)).Should().ContainSingle();
        GetErrors(collection[1], nameof(OrderEditModel.BindingMemo)).Should().ContainSingle();
        GetErrors(collection[2], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();

        // 重複していない列（確定値プロパティそのもの）には登録しない
        GetErrors(collection[0], nameof(OrderEditModel.Memo)).Should().BeEmpty();
    }

    /// <summary>複合制約は構成列すべてが一致したときだけ、全構成列のバインディングプロパティへ登録される</summary>
    [Fact(DisplayName = "複合制約の重複は全構成列へ登録される")]
    public void CompositeDuplicate_RegistersErrorOnAllMembers()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 1, 100m, "banana"),
        };

        collection.Validate().Should().BeFalse();

        GetErrors(collection[0], nameof(OrderEditModel.BindingCustomerId)).Should().ContainSingle();
        GetErrors(collection[0], nameof(OrderEditModel.BindingAmount)).Should().ContainSingle();
        GetErrors(collection[1], nameof(OrderEditModel.BindingCustomerId)).Should().ContainSingle();

        // 単一列制約（memo）は重複していないのでエラーなし
        GetErrors(collection[0], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
    }

    /// <summary>構成列の値に null を含む組は判定対象外（未入力の memo が並んでも重複にならない）</summary>
    [Fact(DisplayName = "NULL を含む組は重複判定から外れる")]
    public void NullMember_IsNotTreatedAsDuplicate()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, string.Empty),
            NewOrder(11, 2, 50m, string.Empty),
        };

        collection.Validate().Should().BeTrue();

        GetErrors(collection[0], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
        GetErrors(collection[1], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
    }

    /// <summary>削除対象（RowState.Removed）の要素は比較から外れる</summary>
    [Fact(DisplayName = "削除対象の要素は重複判定から外れる")]
    public void RemovedElement_IsExcluded()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 2, 50m, "apple pie"),
        };
        collection[1].MarkRemoved();

        collection.Validate().Should().BeTrue();

        GetErrors(collection[0], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
    }

    /// <summary>重複を解消して再検証すると、前回登録された重複エラーが残らない</summary>
    [Fact(DisplayName = "再検証で古い重複エラーが残らない")]
    public void Revalidation_ClearsPreviousDuplicateErrors()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 2, 50m, "apple pie"),
        };

        collection.Validate().Should().BeFalse();

        collection[1].BindingMemo = "banana";
        collection.Validate().Should().BeTrue();

        GetErrors(collection[0], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
        GetErrors(collection[1], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
        collection[0].HasErrors.Should().BeFalse();
    }

    /// <summary>親の Validate(includeChildren) だけで、子コレクション内の兄弟重複まで検出される</summary>
    /// <remarks>
    /// ChildLink（コレクション）の検証は <see cref="EditModelCollection{T}.Validate"/> へ委譲されるため、
    /// 要素個別の検証だけでなく重複検出も親からの 1 回の Validate で走る。
    /// </remarks>
    [Fact(DisplayName = "親の Validate で子コレクション内の重複が検出される")]
    public void ParentValidate_DetectsDuplicatesAmongChildren()
    {
        var customer = new CustomerEditModel { BindingCustomerId = "1", BindingName = "Alice" };
        customer.Orders.Add(NewOrder(10, 1, 100m, "apple pie"));
        customer.Orders.Add(NewOrder(11, 2, 50m, "apple pie"));

        customer.Validate(includeChildren: true).Should().BeFalse();

        GetErrors(customer.Orders[0], nameof(OrderEditModel.BindingMemo)).Should().ContainSingle();
        GetErrors(customer.Orders[1], nameof(OrderEditModel.BindingMemo)).Should().ContainSingle();

        // 親からの収集にも重複エラーがパス付きで載る
        customer
            .CollectErrors(includeChildren: true)
            .Should()
            .Contain(e =>
                e.Path == "Orders[0]" && e.Property == nameof(OrderEditModel.BindingMemo)
            );
    }

    /// <summary>同一プロパティに変換エラーと重複エラーが同時に立つときは、別ストアなので 2 件とも返る</summary>
    /// <remarks>
    /// 旧実装は重複エラーを入力エラーと同じスロットへ <c>SetError</c> で上書きしていたため、変換エラーが消えていた。
    /// </remarks>
    [Fact(DisplayName = "同一プロパティの変換エラーと重複エラーは 2 件とも返る")]
    public void SameProperty_ReturnsBothErrorKinds()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 1, 100m, "banana"),
        };

        // 複合制約（customer_id + amount）の重複を保ったまま、同じ amount 欄へ不正入力を与える
        // （変換に失敗するので確定値 100 は据え置き＝重複したまま変換エラーだけが増える）
        collection[1].BindingAmount = "abc";

        collection.Validate().Should().BeFalse();

        GetErrors(collection[1], nameof(OrderEditModel.BindingAmount)).Should().HaveCount(2);
    }

    /// <summary>重複を解消して再検証しても、同じプロパティの入力エラーは巻き添えで消えない（false のまま）</summary>
    /// <remarks>
    /// 旧実装は重複エラーを入力エラーと同じスロットへ上書きし、次回検証冒頭の一括クリアでスロットごと削除していた。
    /// 変換エラーはバインディングのセッターからしか再生成されないため二度と復活せず、画面に不正入力が残ったまま
    /// Validate が true になり、古い確定値が黙って保存され得た。
    /// </remarks>
    [Fact(DisplayName = "重複解消後も同じ欄の変換エラーは残り Validate は false のまま")]
    public void ResolvingDuplicate_KeepsParseErrorOnSameProperty()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 1, 100m, "banana"),
        };
        collection[1].BindingAmount = "abc";

        collection.Validate().Should().BeFalse();

        // 重複だけを解消する（不正入力 "abc" は amount 欄に残ったまま）
        collection[1].BindingCustomerId = "2";

        collection.Validate().Should().BeFalse();

        GetErrors(collection[1], nameof(OrderEditModel.BindingAmount)).Should().ContainSingle();
        collection[1].HasErrors.Should().BeTrue();
    }

    /// <summary>再ロード（別エンティティの読み込み）は、両方の重複チェックが付けたエラーを落とす</summary>
    /// <remarks>
    /// ロードで値そのものが入れ替わるため、その値に対する重複判定は無効になる（次の検証まで結果を持たない）。
    /// <c>RevertInput</c> は入力エラーしか担当しないので、Mapper のロードが重複エラーを別途クリアする。
    /// </remarks>
    [Fact(DisplayName = "再ロードすると重複エラーが残らない")]
    public void Reload_ClearsDuplicateErrors()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 2, 50m, "apple pie"),
        };

        collection.Validate().Should().BeFalse();
        collection[1].HasErrors.Should().BeTrue();

        // DB 照合（リモート面）由来のエラーも同時に載せておく
        collection[1]
            .SetDuplicateError(
                nameof(OrderEditModel.BindingAmount),
                "already used",
                DuplicateErrorSource.Database
            );

        new OrderMapper().ApplyToEditModel(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(12),
                CustomerId = CustomerIdValue.Create(3),
                Amount = AmountValue.Create(20m),
                Memo = MemoValue.Create("banana"),
            },
            collection[1]
        );

        collection[1].HasErrors.Should().BeFalse();
        GetErrors(collection[1], nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
        GetErrors(collection[1], nameof(OrderEditModel.BindingAmount)).Should().BeEmpty();
    }

    /// <summary>兄弟間チェックの再実行は、DB 照合が登録したエラーを巻き添えで消さない（ソース分離）</summary>
    /// <remarks>
    /// 旧実装は重複エラーストアを一括クリアしていたため、直前の <c>ValidateUniqueAsync</c>（DB 突合）の結果が
    /// 親の <c>Validate</c> 1 回で静かに消えていた。実 DB を使う検証は EditModelValidateUniqueRuntimeTests 側。
    /// </remarks>
    [Fact(DisplayName = "兄弟間チェックの再実行で DB 由来の重複エラーが消えない")]
    public void SiblingValidation_KeepsDatabaseDuplicateErrors()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 2, 50m, "banana"),
        };

        // DB 照合が見つけた違反（兄弟間では重複していない値に載る）
        collection[1]
            .SetDuplicateError(
                nameof(OrderEditModel.BindingMemo),
                "already used",
                DuplicateErrorSource.Database
            );

        // 兄弟間では重複していないが、DB 由来のエラーが残るため全体としては無効のまま
        collection.Validate().Should().BeFalse();

        GetErrors(collection[1], nameof(OrderEditModel.BindingMemo))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("already used");
    }

    /// <summary>ルートの一覧（コレクションでない列挙）でも同じヘルパを直接呼べる</summary>
    [Fact(DisplayName = "ヘルパを一覧へ直接呼べる")]
    public void Validator_CanBeCalledOnPlainSequence()
    {
        var models = new[]
        {
            NewOrder(10, 1, 100m, "apple pie"),
            NewOrder(11, 2, 50m, "apple pie"),
        };

        EditModelUniquenessValidator.Validate(models).Should().BeFalse();

        GetErrors(models[0], nameof(OrderEditModel.BindingMemo)).Should().ContainSingle();
    }
}
