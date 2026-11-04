using System;
using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成 EditModel・Mapper（<c>GeneratedFixture.g.cs</c>）と固定 infra の書式ヘルパーのうち、
/// 同じ関数・同じ準備で入力と期待だけが違うものを、決まった形のシナリオの表で確かめる（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 表は 7 つ＝em-format（<see cref="EditModelInputFormat"/> の書式・カルチャ×型の場合）・em-blank（名前欄の空白のみ・null 入力）・
/// em-range（RemoveRange の範囲外指定）・em-removed（Remove→（Clear）→Add した要素の保存グラフ）・
/// em-mapper-null（NULL の列のロードと往復）・em-mapper-removed（削除済み要素の includeRemoved の両分岐）・
/// em-sibling（削除マークした行の除外）。行は <see cref="EditModelScenarioRows"/> にある。
/// </para>
/// <para>
/// 手の並びや表明の形がメソッドごとに違う手順のシナリオは、元のクラス（<see cref="EditModelLifecycleTests"/> など）に残している。
/// </para>
/// </remarks>
public sealed class EditModelScenarioTests
{
    /// <summary>em-format の行の Id</summary>
    public static TheoryData<string> FormatIds => EditModelScenarioRows.Formats.Ids();

    /// <summary>em-blank の行の Id</summary>
    public static TheoryData<string> BlankIds => EditModelScenarioRows.Blanks.Ids();

    /// <summary>em-range の行の Id</summary>
    public static TheoryData<string> RangeIds => EditModelScenarioRows.Ranges.Ids();

    /// <summary>em-removed の行の Id</summary>
    public static TheoryData<string> RemovedIds => EditModelScenarioRows.Removeds.Ids();

    /// <summary>em-mapper-null の行の Id</summary>
    public static TheoryData<string> MapperNullIds => EditModelScenarioRows.MapperNulls.Ids();

    /// <summary>em-mapper-removed の行の Id</summary>
    public static TheoryData<string> MapperRemovedIds => EditModelScenarioRows.MapperRemoveds.Ids();

    /// <summary>em-sibling の行の Id</summary>
    public static TheoryData<string> SiblingIds => EditModelScenarioRows.Siblings.Ids();

    [Theory(DisplayName = "[EM] 入力欄の表示文字列が既定書式・往復の規則を守る")]
    [MemberData(nameof(FormatIds))]
    public void Format(string id)
    {
        var input = EditModelScenarioRows.Formats[id].Input;

        // 行のカルチャを現在カルチャにする（インバリアントは空文字で指す）。期待も同じカルチャの中で作る
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = string.IsNullOrEmpty(input.Culture)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(input.Culture);

        try
        {
            var text = input.Format();

            if (input.ExpectedText is { } expectedText)
            {
                text.Should().Be(expectedText());
            }

            if (input.MustContain is { } mustContain)
            {
                text.Should().Contain(mustContain);
            }

            if (input.MustNotContain is { } mustNotContain)
            {
                text.Should().NotContain(mustNotContain);
            }

            if (input.Parse is { } parse)
            {
                // TryParse の成否（失敗は null）と、比べる形へ写した値
                var parsed = parse(text);
                parsed.Should().NotBeNull(text);
                parsed.Should().Be(input.ExpectedParsed!(), text);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory(
        DisplayName = "[EM] 名前欄の空白のみ・null の入力は確定値を null にしてエラーを出さない"
    )]
    [MemberData(nameof(BlankIds))]
    public void Blank(string id)
    {
        var input = EditModelScenarioRows.Blanks[id].Input;

        var model = new CustomerEditModel { BindingName = "abc" };

        if (input.CheckNameSetBefore)
        {
            model.Name.Should().NotBeNull();
        }

        model.BindingName = input.Input!;

        model.BindingName.Should().Be(input.ExpectedBinding);
        model.Name.Should().BeNull();
        model.HasErrors.Should().BeFalse();
    }

    [Theory(DisplayName = "[EM] RemoveRange の範囲外指定は ArgumentOutOfRangeException で断る")]
    [MemberData(nameof(RangeIds))]
    public void Range(string id)
    {
        var input = EditModelScenarioRows.Ranges[id].Input;

        var col = input.Setup();
        var act = () => col.RemoveRange(input.Index, input.Count);

        var thrown = act.Should().Throw<ArgumentOutOfRangeException>();

        if (input.ExpectedParamName is { } paramName)
        {
            thrown.Which.ParamName.Should().Be(paramName);
        }

        if (input.ExpectedCountAfter is { } countAfter)
        {
            col.Should().HaveCount(countAfter);
        }

        if (input.CheckRemovedItemsEmpty)
        {
            col.RemovedItems.Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "[EM] 削除して戻した要素は保存対象（includeRemoved）の削除分に現れない")]
    [MemberData(nameof(RemovedIds))]
    public void Removed(string id)
    {
        var input = EditModelScenarioRows.Removeds[id].Input;

        var customer = EditModelLifecycleTests.LoadedCustomer(orderCount: 2);
        var order = customer.Orders[0];

        customer.Orders.Remove(order);

        if (input.ClearBetween)
        {
            customer.Orders.Clear();
        }

        customer.Orders.Add(order);

        var entity = new CustomerMapper().CreateEntity(customer, includeRemoved: true);

        entity.Orders.Should().HaveCount(input.ExpectedCount);
        entity.Orders.Should().NotContain(o => o.RowState == RowState.Removed);
    }

    [Theory(DisplayName = "[EM] NULL の列は null のまま往復し、表示文字列は空になる")]
    [MemberData(nameof(MapperNullIds))]
    public void MapperNull(string id)
    {
        var input = EditModelScenarioRows.MapperNulls[id].Input;

        var mapper = new OrderMapper();

        var em = mapper.CreateEditModel(MapperRoundTripTests.BuildOrder(1));

        input.Confirmed(em).Should().BeNull();
        input.Binding(em).Should().BeEmpty();
        input.Rebuilt(mapper.CreateEntity(em, includeRemoved: false)).Should().BeNull();
    }

    [Theory(DisplayName = "[EM] 削除済み要素を includeRemoved で除外／包含する")]
    [MemberData(nameof(MapperRemovedIds))]
    public void MapperRemoved(string id)
    {
        var input = EditModelScenarioRows.MapperRemoveds[id].Input;

        // 準備は 1 回だけ（同じ EditModel から両分岐を作る）
        var create = input.Prepare();

        create(false).Should().HaveCount(1);

        var withRemoved = create(true);
        withRemoved.Should().HaveCount(2);
        withRemoved.Should().Contain(RowState.Removed);
    }

    [Theory(DisplayName = "[EM] 削除マークした行は重複検証でも変換エラーでも検証から外れる")]
    [MemberData(nameof(SiblingIds))]
    public void Sibling(string id)
    {
        var input = EditModelScenarioRows.Siblings[id].Input;

        // 元のコレクション初期化子と同じく Add で 1 行ずつ入れる
        var collection = new EditModelCollection<CustomerProfileEditModel>();

        foreach (var (profileId, customerId, bio) in input.Profiles)
        {
            collection.Add(EditModelRemovedValidationTests.NewProfile(profileId, customerId, bio));
        }

        if (input.BrokenProfileId is { } broken)
        {
            collection[1].BindingProfileId = broken;
        }

        collection.Validate().Should().BeFalse();

        collection[1].MarkRemoved();

        collection.Validate().Should().BeTrue();
    }
}

/// <summary>em-format の行の入力</summary>
/// <param name="Culture">現在カルチャにするカルチャの名前（空文字＝インバリアント）</param>
/// <param name="Format">書式ヘルパーの呼び出し</param>
/// <param name="ExpectedText">完全一致の期待（行のカルチャの中で作る）。null は照合しない</param>
/// <param name="MustContain">含むべき文字列。null は照合しない</param>
/// <param name="MustNotContain">含まないべき文字列。null は照合しない</param>
/// <param name="Parse">表示文字列をバインド setter と同じパースへ通し、比べる形へ写す（失敗は null）。null は照合しない</param>
/// <param name="ExpectedParsed">パースした値の期待（<paramref name="Parse"/> と同じ形）</param>
internal sealed record EditModelFormatCase(
    string Culture,
    Func<string> Format,
    Func<string>? ExpectedText,
    string? MustContain,
    string? MustNotContain,
    Func<string, string?>? Parse,
    Func<string>? ExpectedParsed
);

/// <summary>em-blank の行の入力</summary>
/// <param name="Input">名前欄へ入れる文字列</param>
/// <param name="ExpectedBinding">入力後の名前欄の文字列</param>
/// <param name="CheckNameSetBefore">入力前に確定値が入っていることを確かめるか</param>
internal sealed record EditModelBlankNameCase(
    string? Input,
    string? ExpectedBinding,
    bool CheckNameSetBefore
);

/// <summary>em-range の行の入力</summary>
/// <param name="Setup">RemoveRange を呼ぶ前のコレクション</param>
/// <param name="Index">RemoveRange の index</param>
/// <param name="Count">RemoveRange の count</param>
/// <param name="ExpectedParamName">例外の ParamName。null は照合しない</param>
/// <param name="ExpectedCountAfter">例外の後の件数。null は照合しない</param>
/// <param name="CheckRemovedItemsEmpty">例外の後に削除追跡が空であることを確かめるか</param>
internal sealed record EditModelRemoveRangeCase(
    Func<EditModelCollection<OrderEditModel>> Setup,
    int Index,
    int Count,
    string? ExpectedParamName,
    int? ExpectedCountAfter,
    bool CheckRemovedItemsEmpty
);

/// <summary>em-removed の行の入力</summary>
/// <param name="ClearBetween">Remove と Add の間に Clear をはさむか</param>
/// <param name="ExpectedCount">保存用のグラフの注文の件数</param>
internal sealed record EditModelRemoveAddCase(bool ClearBetween, int ExpectedCount);

/// <summary>em-mapper-null の行の入力</summary>
/// <param name="Confirmed">EditModel の確定値</param>
/// <param name="Binding">EditModel の表示文字列</param>
/// <param name="Rebuilt">EditModel から作り直したエンティティの値</param>
internal sealed record EditModelMapperNullCase(
    Func<OrderEditModel, object?> Confirmed,
    Func<OrderEditModel, string> Binding,
    Func<OrderEntity, object?> Rebuilt
);

/// <summary>em-mapper-removed の行の入力</summary>
/// <param name="Prepare">削除済み要素を持つ EditModel を準備し、includeRemoved を受けて結果の要素の RowState を返す関数を作る</param>
internal sealed record EditModelMapperRemovedCase(
    Func<Func<bool, IReadOnlyList<RowState>>> Prepare
);

/// <summary>em-sibling の行の入力</summary>
/// <param name="Profiles">コレクションへ入れる顧客プロフィール（Unchanged）</param>
/// <param name="BrokenProfileId">2 行目の ProfileId 欄へ入れる変換できない入力。null は入れない</param>
internal sealed record EditModelRemovedSkipCase(
    (int ProfileId, int CustomerId, string Bio)[] Profiles,
    string? BrokenProfileId
);
