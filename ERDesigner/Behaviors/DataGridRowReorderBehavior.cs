using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ERDesigner.ViewModels;

namespace ERDesigner.Behaviors;

/// <summary>
/// <see cref="DataGrid"/> の行ヘッダーをドラッグして、行順を入れ替える添付ビヘイビアです。
/// </summary>
/// <remarks>
/// <para>
/// 想定用途はエンティティのカラム一覧です。
/// <see cref="DataGrid.ItemsSource"/> が <see cref="ObservableCollection{T}"/> の場合に
/// <c>Move</c> で並び順を変更します。
/// </para>
/// <para>
/// ドラッグ開始は行ヘッダー上でのみ有効です（セル編集との競合を避けるため）。
/// </para>
/// </remarks>
public static class DataGridRowReorderBehavior
{
    /// <summary>ビヘイビア有効/無効を切り替える添付プロパティです。</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DataGridRowReorderBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary><see cref="IsEnabledProperty"/> を設定します。</summary>
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
    /// <summary><see cref="IsEnabledProperty"/> を取得します。</summary>
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    /// <summary>ドラッグ開始地点（マウス座標）を DataGrid ごとに保持します。</summary>
    private static readonly DependencyProperty DragStartPointProperty =
        DependencyProperty.RegisterAttached(
            "DragStartPoint",
            typeof(Point),
            typeof(DataGridRowReorderBehavior),
            new PropertyMetadata(default(Point)));

    private static void SetDragStartPoint(DependencyObject d, Point value) => d.SetValue(DragStartPointProperty, value);
    private static Point GetDragStartPoint(DependencyObject d) => (Point)d.GetValue(DragStartPointProperty);

    /// <summary>
    /// <see cref="IsEnabledProperty"/> 変更時にイベントハンドラを登録/解除します。
    /// </summary>
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid) return;

        if ((bool)e.NewValue)
        {
            dataGrid.AllowDrop = true;
            dataGrid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            dataGrid.MouseMove += OnMouseMove;
            dataGrid.DragOver += OnDragOver;
            dataGrid.Drop += OnDrop;
        }
        else
        {
            dataGrid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            dataGrid.MouseMove -= OnMouseMove;
            dataGrid.DragOver -= OnDragOver;
            dataGrid.Drop -= OnDrop;
        }
    }

    /// <summary>左ボタン押下時にドラッグ開始位置を記録します。</summary>
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        SetDragStartPoint(dataGrid, e.GetPosition(null));
    }

    /// <summary>
    /// マウス移動時に、閾値を超えかつ行ヘッダー上であればドラッグを開始します。
    /// </summary>
    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var start = GetDragStartPoint(dataGrid);
        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var header = FindVisualParent<DataGridRowHeader>(e.OriginalSource as DependencyObject);
        if (header is null) return;

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not ColumnViewModel sourceColumn) return;

        DragDrop.DoDragDrop(dataGrid, sourceColumn, DragDropEffects.Move);
    }

    /// <summary>ドラッグ中のカーソル効果を設定します。</summary>
    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ColumnViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// ドロップ時に移動元/移動先インデックスを求め、コレクション順を更新します。
    /// </summary>
    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (!e.Data.GetDataPresent(typeof(ColumnViewModel))) return;

        var sourceColumn = e.Data.GetData(typeof(ColumnViewModel)) as ColumnViewModel;
        if (sourceColumn is null) return;

        if (dataGrid.ItemsSource is not ObservableCollection<ColumnViewModel> columns) return;

        var sourceIndex = columns.IndexOf(sourceColumn);
        if (sourceIndex < 0) return;

        var targetRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        var targetColumn = targetRow?.Item as ColumnViewModel;
        var targetIndex = targetColumn is null ? columns.Count - 1 : columns.IndexOf(targetColumn);

        if (targetIndex < 0 || sourceIndex == targetIndex) return;

        columns.Move(sourceIndex, targetIndex);
        dataGrid.SelectedItem = sourceColumn;
        e.Handled = true;
    }

    /// <summary>
    /// 指定要素の親方向をたどり、最初に見つかった <typeparamref name="T"/> を返します。
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed) return typed;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
