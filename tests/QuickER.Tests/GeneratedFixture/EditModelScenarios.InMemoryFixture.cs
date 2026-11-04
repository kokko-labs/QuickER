using System;
using System.Linq;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝RowState / EditModelMessages が他フィクスチャの同名型と
// 曖昧にならないようにする（InMemoryFixtureDefinition.cs と同じ流儀）
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// VO 無効の EditModel（<c>InMemoryFixture.g.cs</c>）の空欄入力と削除行のうち、同じ関数・同じ準備で入力と期待だけが
/// 違うものを、決まった形のシナリオの表で確かめる（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 表は 5 つ＝em-blank-null（NULL 許容列の空欄は確定値を null にする）・em-blank-required（非 NULL 列の空欄は必須エラー）・
/// em-blank-parse（解析できない入力は変換エラー）・em-removed-graph（削除行の保存グラフ）・
/// em-removed-accept（削除確定行の受理）。行は <see cref="EditModelScenarioRows"/> にある。
/// </para>
/// <para>
/// 固定する規則は「空欄は変換エラーにせず確定値を null にし、null を許すかは基底の必須チェックが決める」と、
/// 「削除行はキーだけが保存に参加し、受理で削除確定行はコレクションから外れる」。手順のシナリオは
/// <see cref="EditModelBlankInputTests"/> と <see cref="EditModelRemovedRowTests"/> に残している。
/// </para>
/// </remarks>
public sealed class EditModelScenarioTests
{
    /// <summary>em-blank-null の行の Id</summary>
    public static TheoryData<string> BlankNullIds => EditModelScenarioRows.BlankNulls.Ids();

    /// <summary>em-blank-required の行の Id</summary>
    public static TheoryData<string> BlankRequiredIds => EditModelScenarioRows.BlankRequireds.Ids();

    /// <summary>em-blank-parse の行の Id</summary>
    public static TheoryData<string> BlankParseIds => EditModelScenarioRows.BlankParses.Ids();

    /// <summary>em-removed-graph の行の Id</summary>
    public static TheoryData<string> RemovedGraphIds => EditModelScenarioRows.RemovedGraphs.Ids();

    /// <summary>em-removed-accept の行の Id</summary>
    public static TheoryData<string> RemovedAcceptIds => EditModelScenarioRows.RemovedAccepts.Ids();

    [Theory(DisplayName = "[EM] NULL 許容列の空欄入力は確定値を null へ戻しエラーにしない")]
    [MemberData(nameof(BlankNullIds))]
    public void BlankNull(string id)
    {
        var input = EditModelScenarioRows.BlankNulls[id].Input;

        var model = EditModelScenarioRows.BuildLoadedModel();

        input.Set(model, string.Empty);

        input.Confirmed(model).Should().BeNull();
        model.GetErrors(input.BindingName).Cast<object>().Should().BeEmpty();
        model.Validate().Should().BeTrue();

        if (input.ExpectedRowState is { } rowState)
        {
            model.RowState.Should().Be(rowState);
        }
    }

    [Theory(DisplayName = "[EM] 非 NULL 列の空欄は必須エラーになり変換エラーにはならない")]
    [MemberData(nameof(BlankRequiredIds))]
    public void BlankRequired(string id)
    {
        var input = EditModelScenarioRows.BlankRequireds[id].Input;

        var model = input.Setup();
        input.Blank?.Invoke(model);

        input.Confirmed(model).Should().BeNull();
        model.Validate().Should().BeFalse();
        model
            .GetErrors(input.BindingName)
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(EditModelMessages.Required(input.PropertyName, input.PropertyName));

        if (input.ReEnter is { } reEnter)
        {
            // 値を入れ直せば必須エラーは基底が自分の分だけ取り下げる
            reEnter(model);
            model.Validate().Should().BeTrue();
            model.GetErrors(input.BindingName).Cast<object>().Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "[EM] 解析できない入力は従来どおり変換エラーになり確定値を動かさない")]
    [MemberData(nameof(BlankParseIds))]
    public void BlankParse(string id)
    {
        var input = EditModelScenarioRows.BlankParses[id].Input;

        var model = EditModelScenarioRows.BuildLoadedModel();

        input.Set(model, input.BadInput);

        EditModelScenarioRows
            .RenderValue(input.Confirmed(model))
            .Should()
            .Be(input.ExpectedRetained, "変換に失敗した入力は確定値を動かさない");
        model
            .GetErrors(input.BindingName)
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                EditModelMessages.ParseFailed(
                    input.PropertyName,
                    input.PropertyName,
                    input.BadInput,
                    input.TypeName
                )
            );

        if (input.BlankAfter)
        {
            // 空欄は変換エラーを取り下げて null を確定する
            input.Set(model, string.Empty);
            input.Confirmed(model).Should().BeNull();
            model.GetErrors(input.BindingName).Cast<object>().Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "[EM] 削除行の必須欄が空でも保存用のグラフ生成は失敗せずキーが載る")]
    [MemberData(nameof(RemovedGraphIds))]
    public void RemovedGraph(string id)
    {
        var input = EditModelScenarioRows.RemovedGraphs[id].Input;

        var model = EditModelRemovedRowTests.BuildLoadedModel();
        var row = model.Orders[0];

        // 必須列 Amount を空欄にしてから行を削除する（レビュー再現プローブと同じ手順）
        row.BindingAmount = string.Empty;
        input.Remove(model, row);

        model.Validate().Should().BeTrue("削除行は検証対象外");

        var entity = new CustomerMapper().CreateEntity(model, includeRemoved: true);

        var removed = entity.Orders.Should().ContainSingle().Subject;
        removed.RowState.Should().Be(RowState.Removed);
        removed.OrderId.Should().Be(100, "削除にはキーが要るため、キーは削除行でも必ず載る");
    }

    [Theory(
        DisplayName = "[EM] 受理は削除確定行をコレクションから外し Removed のまま追跡にも残さない"
    )]
    [MemberData(nameof(RemovedAcceptIds))]
    public void RemovedAccept(string id)
    {
        var input = EditModelScenarioRows.RemovedAccepts[id].Input;

        var model = EditModelRemovedRowTests.BuildLoadedModel();
        var row = model.Orders[0];

        row.MarkRemoved();
        input.Accept(model);

        model.Orders.Should().NotContain(row, "削除が確定した行を通常行として画面に残さない");
        row.RowState.Should()
            .Be(RowState.Removed, "外れた行は保存済みの削除を表したまま＝受理で Added にしない");
        model
            .Orders.RemovedItems.Should()
            .BeEmpty("削除追跡を経由せずに外す＝次の保存の削除対象にならない");
    }
}

/// <summary>em-blank-null の行の入力</summary>
/// <param name="Set">欄へ文字列を入れる</param>
/// <param name="Confirmed">確定値</param>
/// <param name="BindingName">エラーを引くバインディングのプロパティ名</param>
/// <param name="ExpectedRowState">入力後の RowState。null は照合しない</param>
internal sealed record EditModelBlankNullCase(
    Action<BlankProbeEditModel, string> Set,
    Func<BlankProbeEditModel, object?> Confirmed,
    string BindingName,
    RowState? ExpectedRowState
);

/// <summary>em-blank-required の行の入力</summary>
/// <param name="Setup">EditModel（読み込んだ行か新しい行）</param>
/// <param name="Blank">欄を空欄にする。null は何もしない（新しい行の未入力）</param>
/// <param name="Confirmed">確定値</param>
/// <param name="BindingName">エラーを引くバインディングのプロパティ名</param>
/// <param name="PropertyName">必須エラーの文言に入る確定値のプロパティ名</param>
/// <param name="ReEnter">値を入れ直す。null は入れ直さない</param>
internal sealed record EditModelBlankRequiredCase(
    Func<BlankProbeEditModel> Setup,
    Action<BlankProbeEditModel>? Blank,
    Func<BlankProbeEditModel, object?> Confirmed,
    string BindingName,
    string PropertyName,
    Action<BlankProbeEditModel>? ReEnter
);

/// <summary>em-blank-parse の行の入力</summary>
/// <param name="Set">欄へ文字列を入れる</param>
/// <param name="BadInput">解析できない入力</param>
/// <param name="Confirmed">確定値</param>
/// <param name="ExpectedRetained">変換に失敗した後の確定値（<see cref="EditModelScenarioRows.RenderValue"/> の形）</param>
/// <param name="BindingName">エラーを引くバインディングのプロパティ名</param>
/// <param name="PropertyName">変換エラーの文言に入る確定値のプロパティ名</param>
/// <param name="TypeName">変換エラーの文言に入る型の名前</param>
/// <param name="BlankAfter">続けて空欄を入れ、変換エラーが取り下がって null になることも確かめるか</param>
internal sealed record EditModelBlankParseCase(
    Action<BlankProbeEditModel, string> Set,
    string BadInput,
    Func<BlankProbeEditModel, object?> Confirmed,
    string ExpectedRetained,
    string BindingName,
    string PropertyName,
    string TypeName,
    bool BlankAfter
);

/// <summary>em-removed-graph の行の入力</summary>
/// <param name="Remove">行を削除する（コレクションから外すか、残したまま削除マークするか）</param>
internal sealed record EditModelRemovedGraphCase(Action<CustomerEditModel, OrderEditModel> Remove);

/// <summary>em-removed-accept の行の入力</summary>
/// <param name="Accept">受理する（コレクションの AcceptChanges か、親の AcceptChanges か）</param>
internal sealed record EditModelRemovedAcceptCase(Action<CustomerEditModel> Accept);
