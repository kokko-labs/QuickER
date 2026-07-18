using System;
using System.ComponentModel;
using System.Windows.Input;
using FluentAssertions;
using QuickER.Extensibility;

namespace QuickER.Tests.FeatureModules;

/// <summary>
/// <see cref="FeatureToolbarItem"/> が <see cref="INotifyPropertyChanged"/> として
/// <see cref="FeatureToolbarItem.Tooltip"/> / <see cref="FeatureToolbarItem.BeginsGroup"/> の
/// 動的切替を通知することを検証するテストクラス。
/// </summary>
public class FeatureToolbarItemTests
{
    /// <summary>ICommand を必要とするだけの最小スタブ（挙動は持たない）</summary>
    private sealed class NoOpCommand : ICommand
    {
        // 実行可否は不変のため、購読はどこにも保持しない（未使用イベント警告を避ける空アクセサ）
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) { }
    }

    /// <summary>Tooltip の変更で PropertyChanged が発火することを検証する</summary>
    [Fact(DisplayName = "Tooltip 変更で PropertyChanged が発火する")]
    public void Tooltip_Change_RaisesPropertyChanged()
    {
        var item = new FeatureToolbarItem("🔌", "Label", "初期", new NoOpCommand());
        var raised = new System.Collections.Generic.List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.Tooltip = "切替後";

        item.Tooltip.Should().Be("切替後");
        raised.Should().Contain(nameof(FeatureToolbarItem.Tooltip));
    }

    /// <summary>同一値の代入では PropertyChanged が発火しないことを検証する</summary>
    [Fact(DisplayName = "同一値の Tooltip 代入では PropertyChanged が発火しない")]
    public void Tooltip_SameValue_DoesNotRaise()
    {
        var item = new FeatureToolbarItem("🔌", "Label", "同じ", new NoOpCommand());
        var raised = 0;
        item.PropertyChanged += (_, _) => raised++;

        item.Tooltip = "同じ";

        raised.Should().Be(0);
    }

    /// <summary>BeginsGroup の変更で PropertyChanged が発火することを検証する</summary>
    [Fact(DisplayName = "BeginsGroup 変更で PropertyChanged が発火する")]
    public void BeginsGroup_Change_RaisesPropertyChanged()
    {
        var item = new FeatureToolbarItem(
            "🔌",
            "Label",
            null,
            new NoOpCommand(),
            beginsGroup: true
        );
        var raised = new System.Collections.Generic.List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.BeginsGroup = false;

        item.BeginsGroup.Should().BeFalse();
        raised.Should().Contain(nameof(FeatureToolbarItem.BeginsGroup));
    }
}
