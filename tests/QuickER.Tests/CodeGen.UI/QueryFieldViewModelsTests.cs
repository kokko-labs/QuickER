using System.Collections.Generic;
using System.ComponentModel;
using AwesomeAssertions;
using QuickER.CodeGen.UI;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// クエリの子行 ViewModel（<see cref="QueryFieldViewModelBase" /> ＝ <see cref="QueryParameterViewModel" /> /
/// <see cref="ProjectionFieldViewModel" />・<see cref="QueryOrderingViewModel" />・<see cref="ColumnChoice" />）の
/// 型トークン追従・編集可否・既定値・変更通知を検証するテストクラス
/// </summary>
public class QueryFieldViewModelsTests
{
    /// <summary>変更通知の名前を記録しながら購読するヘルパ</summary>
    private static List<string> TrackPropertyChanges(INotifyPropertyChanged source)
    {
        var changed = new List<string>();
        source.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        return changed;
    }

    /// <summary>型トークン導出関数が null だと構築時に例外になることを検証する（両派生とも必須契約）</summary>
    [Fact(DisplayName = "型トークン導出関数が null なら構築で例外")]
    public void Constructor_NullDeriveToken_Throws()
    {
        var parameterAct = () => new QueryParameterViewModel(null!);
        var fieldAct = () => new ProjectionFieldViewModel(null!);

        parameterAct.Should().Throw<ArgumentNullException>();
        fieldAct.Should().Throw<ArgumentNullException>();
    }

    /// <summary>パラメータ VM の既定値（名前 param・型 int32・列参照なし・編集可・非リスト）を検証する</summary>
    [Fact(DisplayName = "パラメータ VM の既定値")]
    public void QueryParameter_Defaults()
    {
        var parameter = new QueryParameterViewModel(_ => null);

        parameter.Name.Should().Be("param");
        parameter.Type.Should().Be("int32");
        parameter.SourceColumnId.Should().BeNull();
        parameter.IsTypeEditable.Should().BeTrue();
        parameter.IsList.Should().BeFalse();
    }

    /// <summary>射影フィールド VM の既定値（名前 Field・型 int32・列参照なし・編集可・IsNullable 未指定）を検証する</summary>
    [Fact(DisplayName = "射影フィールド VM の既定値")]
    public void ProjectionField_Defaults()
    {
        var field = new ProjectionFieldViewModel(_ => null);

        field.Name.Should().Be("Field");
        field.Type.Should().Be("int32");
        field.SourceColumnId.Should().BeNull();
        field.IsTypeEditable.Should().BeTrue();
        field.IsNullable.Should().BeNull();
    }

    /// <summary>参照元列を選ぶと型トークンが列由来に追従し、手入力不可（IsTypeEditable=false）になることを検証する</summary>
    [Fact(DisplayName = "列参照選択で型が列由来へ追従し編集不可になる")]
    public void SourceColumn_Selected_TypeFollowsColumn_AndBecomesReadOnly()
    {
        var columnId = Guid.NewGuid();
        var parameter = new QueryParameterViewModel(id => id == columnId ? "string(50)" : null);

        parameter.SourceColumnId = columnId;

        parameter.Type.Should().Be("string(50)", "型トークンは導出関数の返す列の宣言型に追従する");
        parameter.IsTypeEditable.Should().BeFalse("列参照時は手入力できない");
    }

    /// <summary>導出関数が型を返さない列を選んだ場合、型トークンは据え置きで、編集可否だけが列参照状態になることを検証する</summary>
    [Fact(DisplayName = "導出不能な列選択でも編集不可にはなる（型は据え置き）")]
    public void SourceColumn_WithNullToken_KeepsTypeButBecomesReadOnly()
    {
        var parameter = new QueryParameterViewModel(_ => null) { Type = "int32" };

        parameter.SourceColumnId = Guid.NewGuid();

        parameter.Type.Should().Be("int32", "導出関数が null を返すと型トークンは変更しない");
        parameter.IsTypeEditable.Should().BeFalse("列参照（SourceColumnId 非 null）なら編集不可");
    }

    /// <summary>参照元列を「なし」へ戻すと、型トークンは直近の列由来値を保持したまま編集可へ復帰することを検証する</summary>
    [Fact(DisplayName = "列参照を外すと型を保持したまま編集可へ戻る")]
    public void SourceColumn_ResetToNull_RetainsTypeAndBecomesEditable()
    {
        var columnId = Guid.NewGuid();
        var field = new ProjectionFieldViewModel(id => id == columnId ? "decimal(12,2)" : null);

        field.SourceColumnId = columnId;
        field.Type.Should().Be("decimal(12,2)");

        field.SourceColumnId = null;

        field.Type.Should().Be("decimal(12,2)", "「なし」へ戻しても直近の列由来トークンは保持する");
        field.IsTypeEditable.Should().BeTrue();
    }

    /// <summary>SourceColumnId の変更で IsTypeEditable の変更通知が発火することを検証する（UI の有効化に必要）</summary>
    [Fact(DisplayName = "SourceColumnId 変更で IsTypeEditable の通知が出る")]
    public void SourceColumnId_Change_RaisesIsTypeEditableNotification()
    {
        var parameter = new QueryParameterViewModel(_ => "int32");
        var changes = TrackPropertyChanges(parameter);

        parameter.SourceColumnId = Guid.NewGuid();

        changes.Should().Contain(nameof(QueryParameterViewModel.IsTypeEditable));
        changes.Should().Contain(nameof(QueryParameterViewModel.SourceColumnId));
    }

    /// <summary>IsList の変更通知が発火することを検証する（IN 条件用のリスト型トグル）</summary>
    [Fact(DisplayName = "IsList の変更通知が出る")]
    public void QueryParameter_IsList_RaisesNotification()
    {
        var parameter = new QueryParameterViewModel(_ => null);
        var changes = TrackPropertyChanges(parameter);

        parameter.IsList = true;

        parameter.IsList.Should().BeTrue();
        changes.Should().Contain(nameof(QueryParameterViewModel.IsList));
    }

    /// <summary>並び順 VM の ColumnId・Descending が変更通知つきで更新されることを検証する</summary>
    [Fact(DisplayName = "並び順 VM は ColumnId・Descending を通知つきで更新する")]
    public void QueryOrdering_Properties_RaiseNotifications()
    {
        var ordering = new QueryOrderingViewModel();
        var changes = TrackPropertyChanges(ordering);
        var columnId = Guid.NewGuid();

        ordering.ColumnId = columnId;
        ordering.Descending = true;

        ordering.ColumnId.Should().Be(columnId);
        ordering.Descending.Should().BeTrue();
        changes.Should().Contain(nameof(QueryOrderingViewModel.ColumnId));
        changes.Should().Contain(nameof(QueryOrderingViewModel.Descending));
    }

    /// <summary>ColumnChoice の値等価と「なし」選択肢（Id=null・表示名あり）を検証する</summary>
    [Fact(DisplayName = "ColumnChoice の値等価と None 選択肢")]
    public void ColumnChoice_ValueEquality_AndNone()
    {
        var id = Guid.NewGuid();

        new ColumnChoice(id, "Amount").Should().Be(new ColumnChoice(id, "Amount"));
        new ColumnChoice(id, "Amount").Should().NotBe(new ColumnChoice(id, "Other"));

        ColumnChoice.None.Id.Should().BeNull("「なし＝自由フィールド」は列 ID を持たない");
        ColumnChoice.None.Name.Should().NotBeNullOrEmpty("表示名は resx から与えられる");
    }
}
