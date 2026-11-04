using System;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// EditModel の重複エラー（<c>QueryFixture.g.cs</c> の orders）のうち、同じ関数・同じ準備で入力と期待だけが違うものを、
/// 決まった形のシナリオの表で確かめる（実 DB を使わない・CI 常時実行）。
/// </summary>
/// <remarks>
/// 表は 2 つ＝em-sibling（コレクション内の重複の検出と、行と列ごとのエラーの件数）・
/// em-db-error（確定値の編集で DB 照合の重複エラーが取り下げられる）。
/// 手順のシナリオは <see cref="EditModelUniquenessTests"/> に残している。
/// </remarks>
public sealed class EditModelScenarioTests
{
    /// <summary>em-sibling の行の Id</summary>
    public static TheoryData<string> SiblingIds => EditModelScenarioRows.Siblings.Ids();

    /// <summary>em-db-error の行の Id</summary>
    public static TheoryData<string> DbErrorIds => EditModelScenarioRows.DbErrors.Ids();

    [Theory(DisplayName = "[EM] コレクション内の重複が期待の行と列へ登録される")]
    [MemberData(nameof(SiblingIds))]
    public void Sibling(string id)
    {
        var input = EditModelScenarioRows.Siblings[id].Input;

        // 元のコレクション初期化子と同じく Add で 1 行ずつ入れる
        var collection = new EditModelCollection<OrderEditModel>();

        foreach (var order in input.Orders)
        {
            collection.Add(
                EditModelUniquenessTests.NewOrder(
                    order.OrderId,
                    order.CustomerId,
                    order.Amount,
                    order.Memo
                )
            );
        }

        if (input.RemovedIndex is { } removedIndex)
        {
            collection[removedIndex].MarkRemoved();
        }

        collection.Validate().Should().Be(input.ExpectedValid);

        foreach (var expected in input.ExpectedErrors)
        {
            EditModelUniquenessTests
                .GetErrors(collection[expected.Index], expected.Property)
                .Should()
                .HaveCount(
                    expected.Count,
                    $"{expected.Index} 行目の {expected.Property} のエラーの件数"
                );
        }
    }

    [Theory(DisplayName = "[EM] 確定値を編集すると DB 由来の重複エラーが取り下げられる")]
    [MemberData(nameof(DbErrorIds))]
    public void DbError(string id)
    {
        var input = EditModelScenarioRows.DbErrors[id].Input;

        var model = EditModelUniquenessTests.NewOrder(10, 1, 100m, "apple pie");

        model.SetDuplicateError(
            input.ErrorProperty,
            "already used in the database",
            DuplicateErrorSource.Database
        );

        if (input.CheckValidity)
        {
            model.HasErrors.Should().BeTrue();
            model.Validate().Should().BeFalse();
        }

        input.Edit(model);

        EditModelUniquenessTests.GetErrors(model, input.ErrorProperty).Should().BeEmpty();
        model.HasErrors.Should().BeFalse();

        if (input.CheckValidity)
        {
            model.Validate().Should().BeTrue("値が変われば DB 突合の結論は持ち越さない");
        }
    }
}

/// <summary>注文 EditModel の入力（必須項目はすべて埋める）</summary>
internal sealed record EditModelOrderSpec(int OrderId, int CustomerId, decimal Amount, string Memo);

/// <summary>行と列ごとのエラーの件数の期待</summary>
/// <param name="Index">コレクションの何行目か</param>
/// <param name="Property">エラーを引くプロパティ名</param>
/// <param name="Count">エラーの件数（1＝元の ContainSingle・0＝元の BeEmpty）</param>
internal sealed record EditModelErrorCount(int Index, string Property, int Count);

/// <summary>em-sibling の行の入力</summary>
/// <param name="Orders">コレクションへ入れる注文</param>
/// <param name="RemovedIndex">Validate の前に削除マークする行。null はしない</param>
/// <param name="ExpectedValid">コレクションの Validate の戻り値</param>
/// <param name="ExpectedErrors">行と列ごとのエラーの件数</param>
internal sealed record EditModelSiblingCase(
    EditModelOrderSpec[] Orders,
    int? RemovedIndex,
    bool ExpectedValid,
    EditModelErrorCount[] ExpectedErrors
);

/// <summary>em-db-error の行の入力</summary>
/// <param name="ErrorProperty">DB 照合の重複エラーを載せるプロパティ名</param>
/// <param name="Edit">確定値を変える編集（バインディング経由）</param>
/// <param name="CheckValidity">編集の前後の HasErrors・Validate も確かめるか</param>
internal sealed record EditModelDbErrorCase(
    string ErrorProperty,
    Action<OrderEditModel> Edit,
    bool CheckValidity
);
