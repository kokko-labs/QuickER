using System.Collections;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// EditModel 側のコレクション内重複検証（<c>[UniqueConstraint]</c> 属性＋<see cref="EditModelUniquenessValidator"/>）を
/// 固定フィクスチャ上で検証する。
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

    /// <summary>クラスへ図の UNIQUE 制約が属性として刻まれる（構成列は確定値プロパティ名・宣言順）</summary>
    [Fact(DisplayName = "EditModel クラスへ UNIQUE 制約が属性として刻まれる")]
    public void UniqueConstraintAttributes_AreDeclaredOnClass()
    {
        var constraints = EditModelUniquenessValidator.For(typeof(OrderEditModel));

        constraints
            .Select(constraint => constraint.Name)
            .Should()
            .Equal("UQ_orders_memo", "UQ_orders_customer_id_amount");
        constraints[0].PropertyNames.Should().Equal(nameof(OrderEditModel.Memo));
        constraints[1]
            .PropertyNames.Should()
            .Equal(nameof(OrderEditModel.CustomerId), nameof(OrderEditModel.Amount));
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
