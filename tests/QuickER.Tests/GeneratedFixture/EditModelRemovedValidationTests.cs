using System.Collections;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 削除マーク（<c>MarkRemoved</c>）した EditModel が検証・エラー収集の対象外になることを、
/// コミット済みフィクスチャ（<c>GeneratedFixture.g.cs</c>）の実型に対して検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 削除される行の入力は保存に使われない（キーだけが使われる）。にもかかわらず
/// <see cref="EditModelCollection{T}.Validate"/> と <c>EditModelBase.Validate</c> には削除行の除外が無く、
/// コレクション内重複の検証（<see cref="EditModelUniquenessValidator"/>）だけが <c>IsRemoved</c> を除外していた
/// ＝同じコレクションの中で「重複は見逃すが変換エラーは保存全体を止める」という非対称になっていた。
/// ここはその線引き（読み側でのフィルタ・エラーストアは不変）を固定する。
/// </para>
/// <para>
/// 行自身の <c>HasErrors</c> / <c>GetErrors</c>（INotifyDataErrorInfo＝画面の行単位表示）は触らないため、
/// 削除を取り消して行が復活すれば同じエラーが検証へ戻ってくる。
/// </para>
/// </remarks>
public sealed class EditModelRemovedValidationTests
{
    /// <summary>必須項目を埋めた注文 EditModel を作る（このフィクスチャは VO 有効）</summary>
    private static OrderEditModel NewOrder(int orderId, int customerId, decimal amount, string memo)
    {
        var model = new OrderEditModel
        {
            BindingOrderId = orderId.ToString(),
            BindingCustomerId = customerId.ToString(),
            BindingAmount = amount.ToString(),
            BindingMemo = memo,
        };
        // 入力しただけの新規行は Added になるため、DB から読んだ既存行として扱えるよう Unchanged へ戻す
        model.MarkUnchanged();
        return model;
    }

    /// <summary>必須項目を埋めた顧客プロフィール EditModel を作る（UNIQUE 制約 UQ_customer_profiles_customer_id を持つ）</summary>
    private static CustomerProfileEditModel NewProfile(int profileId, int customerId, string bio)
    {
        var model = new CustomerProfileEditModel
        {
            BindingProfileId = profileId.ToString(),
            BindingCustomerId = customerId.ToString(),
            BindingBio = bio,
        };
        model.MarkUnchanged();
        return model;
    }

    /// <summary>指定プロパティのエラー一覧を取り出す</summary>
    private static string[] GetErrors(EditModelBase model, string propertyName) =>
        ((IEnumerable)model.GetErrors(propertyName)).Cast<string>().ToArray();

    /// <summary>削除マークした行の変換エラーはコレクションの検証を止めず、収集にも現れない</summary>
    [Fact(
        DisplayName = "削除マークした行の変換エラーはコレクション Validate を止めず CollectErrors にも出ない"
    )]
    public void RemovedItem_IsExcludedFromCollectionValidateAndCollectErrors()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple"),
            NewOrder(11, 2, 50m, "banana"),
        };

        // 2 行目に変換不能な入力を与える（この時点では保存全体が止まる）
        collection[1].BindingAmount = "not-a-number";
        collection.Validate().Should().BeFalse("変換エラーのある行が残っている間は検証が通らない");

        // コレクションに残したまま削除マークする（保存時に削除される行＝入力は保存に使われない）
        collection[1].MarkRemoved();

        collection
            .Validate()
            .Should()
            .BeTrue("削除される行の入力は保存に使われないため、検証の対象外になる");
        collection
            .CollectErrors()
            .Should()
            .BeEmpty("収集も同じ線引き＝削除される行のエラーは集約されない");
    }

    /// <summary>読み側のフィルタであり、行自身のエラー（画面の行単位表示）はそのまま残る</summary>
    [Fact(DisplayName = "削除マークした行自身の HasErrors / GetErrors はそのまま残る")]
    public void RemovedItem_KeepsItsOwnErrorsForPerRowDisplay()
    {
        var order = NewOrder(11, 2, 50m, "banana");
        order.BindingAmount = "not-a-number";
        order.HasErrors.Should().BeTrue();

        order.MarkRemoved();

        order
            .HasErrors.Should()
            .BeTrue("除外は読み側のフィルタで、エラーストアには触れない（画面の行単位表示は残る）");
        GetErrors(order, nameof(OrderEditModel.BindingAmount)).Should().NotBeEmpty();
    }

    /// <summary>削除を取り消して行を戻すと、同じエラーが検証・収集へ戻ってくる</summary>
    [Fact(DisplayName = "削除した行を戻すとエラーが再出現し Validate が再び false になる")]
    public void RestoredItem_BringsItsErrorsBackIntoValidation()
    {
        var collection = new EditModelCollection<OrderEditModel>
        {
            NewOrder(10, 1, 100m, "apple"),
            NewOrder(11, 2, 50m, "banana"),
        };

        var broken = collection[1];
        broken.BindingAmount = "not-a-number";

        // Remove はコレクションから外して削除追跡する（削除前の状態を控えて MarkRemoved する）
        collection.Remove(broken);
        collection.Validate().Should().BeTrue();

        // 同一インスタンスを戻すと削除追跡が解除され、削除前の状態（Unchanged）へ復元される
        collection.Add(broken);
        broken.IsRemoved.Should().BeFalse();

        collection
            .Validate()
            .Should()
            .BeFalse("エラーストアには触れていないため、復活した行のエラーがそのまま効く");
        collection.CollectErrors().Should().NotBeEmpty();
    }

    /// <summary>削除マークした親は自分の検証も子への再帰も行わない（削除される部分木の検証は死に仕事）</summary>
    [Fact(
        DisplayName = "削除マークした親の Validate(includeChildren: true) は子の検証もスキップして true"
    )]
    public void RemovedModel_SkipsItsOwnAndItsChildrenValidation()
    {
        var customer = new CustomerEditModel
        {
            BindingCustomerId = "1",
            BindingName = "Alice",
            BindingIsActive = "True",
            BindingBalance = "100",
        };
        customer.Orders.Add(NewOrder(10, 1, 100m, "apple"));
        customer.CustomerProfile = NewProfile(1, 1, "bio");

        // コレクション子・単一参照子の双方に検証が落ちる入力を与える
        customer.Orders[0].BindingAmount = "not-a-number";
        customer.CustomerProfile.BindingProfileId = "not-a-number";
        customer.Validate().Should().BeFalse("削除マーク前は子のエラーが親の検証へ伝わる");

        customer.MarkRemoved();

        customer
            .Validate()
            .Should()
            .BeTrue("削除される部分木は自分も子孫も検証しない（ChildLink 経由の単一子も含む）");
        customer
            .CollectErrors()
            .Should()
            .BeEmpty("収集も部分木ごと除外される（行自身の HasErrors は別）");
        customer.Orders[0].HasErrors.Should().BeTrue("子のエラーストアは触られない");
        customer.CustomerProfile!.HasErrors.Should().BeTrue();
    }

    /// <summary>
    /// 削除行のスキップと、コレクション内重複検証（<see cref="EditModelUniquenessValidator"/>）の
    /// <c>IsRemoved</c> 除外が同じ結果になる（＝非対称の解消）。
    /// </summary>
    [Fact(DisplayName = "削除行は重複検証でも変換エラーでも同じく除外される（非対称の解消）")]
    public void RemovedItem_IsExcludedByBothUniquenessAndInputChecks()
    {
        // 重複側: 同じ customer_id を持つ 2 行のうち片方を削除マークすると重複ではなくなる
        var duplicates = new EditModelCollection<CustomerProfileEditModel>
        {
            NewProfile(1, 7, "bio-a"),
            NewProfile(2, 7, "bio-b"),
        };
        duplicates.Validate().Should().BeFalse("同じ customer_id の 2 行は重複");

        duplicates[1].MarkRemoved();
        duplicates.Validate().Should().BeTrue("削除される行は重複検証の対象外（従来からの挙動）");

        // 入力エラー側: 同じコレクション形で、削除マークした行の変換エラーも検証を止めない
        var invalid = new EditModelCollection<CustomerProfileEditModel>
        {
            NewProfile(1, 7, "bio-a"),
            NewProfile(2, 8, "bio-b"),
        };
        invalid[1].BindingProfileId = "not-a-number";
        invalid.Validate().Should().BeFalse();

        invalid[1].MarkRemoved();
        invalid
            .Validate()
            .Should()
            .BeTrue("入力エラーも重複と同じく削除行では見ない（2 つの検査で線引きが揃う）");
    }
}
