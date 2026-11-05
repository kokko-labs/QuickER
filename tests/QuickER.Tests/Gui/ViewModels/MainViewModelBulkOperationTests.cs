using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> の一括操作（S2: プロパティパネル切替・一括削除・一括色変更）を検証するテストクラス
/// </summary>
/// <remarks>
/// 選択数に追従する派生プロパティ（<c>IsMultiSelectionActive</c> / <c>SelectedEntityCountText</c>）の通知と、
/// 実変更のない一括色変更が履歴を積まないことを確認する。一括削除・一括色変更が「複合 1 エントリ」で Undo できること
/// （履歴の分裂・二重登録が無いこと）は、操作の往復の表（<see cref="QuickER.Tests.Gui.UndoRedo.UndoOperationRows"/> の
/// <c>entity-bulk-*</c>・<c>entity-single-selection-*</c> の行）が流す。
/// </remarks>
public class MainViewModelBulkOperationTests
{
    /// <summary>指定名・色のエンティティ ViewModel を生成してキャンバスへ追加する</summary>
    private static EntityViewModel AddEntity(
        MainViewModel vm,
        string tableName,
        string? titleColor = null
    )
    {
        var model = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var layout = new EntityLayout
        {
            X = 0,
            Y = 0,
            Width = 200,
        };

        if (titleColor is not null)
        {
            layout.TitleBackgroundColor = titleColor;
        }

        var entity = new EntityViewModel(model, layout);
        vm.Entities.Add(entity);
        return entity;
    }

    /// <summary>指定 ViewModel のプロパティ変更通知を記録するトラッカーを取り付ける</summary>
    private static List<string> TrackPropertyChanges(MainViewModel vm)
    {
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                raised.Add(e.PropertyName);
            }
        };

        return raised;
    }

    // ---------------- 派生プロパティの追従・通知 ----------------

    [Fact(DisplayName = "一括切替: トグルで 2 個目到達時に IsMultiSelectionActive が真＋通知")]
    public void MultiSelectionActive_TogglesWithSelectionCount()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");

        var raised = TrackPropertyChanges(vm);

        vm.ToggleEntitySelection(a);
        vm.IsMultiSelectionActive.Should().BeFalse();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 1));

        raised.Clear();
        vm.ToggleEntitySelection(b);

        vm.IsMultiSelectionActive.Should().BeTrue();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 2));
        raised.Should().Contain(nameof(MainViewModel.IsMultiSelectionActive));
        raised.Should().Contain(nameof(MainViewModel.SelectedEntityCountText));
    }

    [Fact(DisplayName = "一括切替: 全選択・全解除・ラバーバンドでも件数と通知が追従")]
    public void MultiSelectionActive_FollowsAllSelectionGestures()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");

        var raised = TrackPropertyChanges(vm);

        // 全選択 → 3 個
        vm.SelectAllEntitiesCommand.Execute(null);
        vm.IsMultiSelectionActive.Should().BeTrue();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 3));
        raised.Should().Contain(nameof(MainViewModel.IsMultiSelectionActive));

        // 全解除 → 0 個
        raised.Clear();
        vm.ClearSelectionCommand.Execute(null);
        vm.IsMultiSelectionActive.Should().BeFalse();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 0));
        raised.Should().Contain(nameof(MainViewModel.IsMultiSelectionActive));

        // ラバーバンドで全域選択 → 3 個
        raised.Clear();
        vm.ApplyRubberBandSelection(new Rect(-100, -100, 10000, 10000), additive: false);
        vm.IsMultiSelectionActive.Should().BeTrue();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 3));
        raised.Should().Contain(nameof(MainViewModel.SelectedEntityCountText));
    }

    // ---------------- 一括色変更（実変更なしは履歴を積まない） ----------------

    /// <summary>実変更のない一括色変更（全員同色・同じ色の別表記）が、履歴を積まず未保存にもならないことを検証する</summary>
    /// <param name="existingColor">全員が持っている見出しの色</param>
    /// <param name="firstColor">1 回目に指定する色</param>
    /// <param name="secondColor">2 回目に指定する色（null は指定しない）</param>
    [Theory(
        DisplayName = "一括色変更: 全員同色なら、同じ色の別表記（小文字・色名・前後の空白）でも履歴を積まず未保存にならない"
    )]
    [InlineData("#ABCDEF", "#ABCDEF", null)] // 全員同色なら履歴を積まない（実変更なし）
    [InlineData("#FFCC00", "#ffcc00", " #FFCC00 ")] // 同じ色の別表記（小文字・前後の空白）を指定しても、履歴を積まず未保存にならない
    public void BulkColorChange_NoOpWhenAlreadySameColor(
        string existingColor,
        string firstColor,
        string? secondColor
    )
    {
        var vm = new MainViewModel();
        AddEntity(vm, "A", titleColor: existingColor);
        AddEntity(vm, "B", titleColor: existingColor);
        vm.SelectAllEntitiesCommand.Execute(null);
        var generation = vm.UndoRedo.ChangeGeneration;

        vm.BulkChangeTitleColorCommand.Execute(firstColor);

        if (secondColor is not null)
        {
            vm.BulkChangeTitleColorCommand.Execute(secondColor);
        }

        vm.UndoRedo.CanUndo.Should().BeFalse("実変更のない履歴を積まない");
        vm.UndoRedo.ChangeGeneration.Should().Be(generation, "未保存の判定も動かさない");
    }
}
