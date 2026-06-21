using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;

namespace ERDesigner.Behaviors;

/// <summary><see cref="DataGrid"/> の行ヘッダーをドラッグして行順を入れ替える添付ビヘイビア</summary>
/// <remarks>
/// <para>
/// 想定用途はエンティティのカラム一覧 <see cref="DataGrid.ItemsSource"/> が
/// <see cref="ObservableCollection{T}"/> の場合に <c>Move</c> で並び順を変更する
/// </para>
/// <para>セル編集との競合を避けるため、ドラッグ開始は行ヘッダー上でのみ有効とする</para>
/// </remarks>
public static class DataGridRowReorderBehavior
{
    /// <summary>ビヘイビアの有効・無効を切り替える添付プロパティ</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DataGridRowReorderBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged)
        );

    /// <summary>行並び替えを履歴登録する Undo / Redo マネージャーを保持する添付プロパティ</summary>
    public static readonly DependencyProperty UndoRedoManagerProperty =
        DependencyProperty.RegisterAttached(
            "UndoRedoManager",
            typeof(UndoRedoManager),
            typeof(DataGridRowReorderBehavior),
            new PropertyMetadata(null)
        );

    /// <summary><see cref="IsEnabledProperty"/> を設定する</summary>
    public static void SetIsEnabled(DependencyObject d, bool value) =>
        d.SetValue(IsEnabledProperty, value);

    /// <summary><see cref="IsEnabledProperty"/> を取得する</summary>
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    /// <summary><see cref="UndoRedoManagerProperty"/> を設定する</summary>
    public static void SetUndoRedoManager(DependencyObject d, UndoRedoManager? value) =>
        d.SetValue(UndoRedoManagerProperty, value);

    /// <summary><see cref="UndoRedoManagerProperty"/> を取得する</summary>
    public static UndoRedoManager? GetUndoRedoManager(DependencyObject d) =>
        (UndoRedoManager?)d.GetValue(UndoRedoManagerProperty);

    /// <summary>ドラッグ開始地点（マウス座標）を DataGrid ごとに保持する添付プロパティ</summary>
    private static readonly DependencyProperty DragStartPointProperty =
        DependencyProperty.RegisterAttached(
            "DragStartPoint",
            typeof(Point),
            typeof(DataGridRowReorderBehavior),
            new PropertyMetadata(default(Point))
        );

    /// <summary><see cref="DragStartPointProperty"/> を設定する</summary>
    private static void SetDragStartPoint(DependencyObject d, Point value) =>
        d.SetValue(DragStartPointProperty, value);

    /// <summary><see cref="DragStartPointProperty"/> を取得する</summary>
    private static Point GetDragStartPoint(DependencyObject d) =>
        (Point)d.GetValue(DragStartPointProperty);

    /// <summary><see cref="IsEnabledProperty"/> 変更時にイベントハンドラと <see cref="UIElement.AllowDrop"/> を登録・解除する</summary>
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

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

    /// <summary>左ボタン押下時にドラッグ判定の基準となる開始位置を記録する</summary>
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        SetDragStartPoint(dataGrid, e.GetPosition(null));
    }

    /// <summary>移動量がシステム既定の閾値を超え、かつ行ヘッダー上の操作のときにドラッグを開始する</summary>
    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var start = GetDragStartPoint(dataGrid);
        var current = e.GetPosition(null);

        // 微小な手ぶれを誤ってドラッグ開始としないよう、OS 既定のドラッグ開始閾値で判定する
        if (
            Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance
        )
        {
            return;
        }

        // セル編集と競合しないよう、起点が行ヘッダー上の場合のみドラッグを許可する
        var header = FindVisualParent<DataGridRowHeader>(e.OriginalSource as DependencyObject);

        if (header is null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);

        if (row?.Item is not ColumnViewModel sourceColumn)
        {
            return;
        }

        DragDrop.DoDragDrop(dataGrid, sourceColumn, DragDropEffects.Move);
    }

    /// <summary>カラムのドラッグ中のみ移動カーソル効果を表示する</summary>
    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ColumnViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>ドロップ時に移動元・移動先インデックスを求め、カラムの並び順を更新する</summary>
    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (!e.Data.GetDataPresent(typeof(ColumnViewModel)))
        {
            return;
        }

        var sourceColumn = e.Data.GetData(typeof(ColumnViewModel)) as ColumnViewModel;

        if (sourceColumn is null)
        {
            return;
        }

        if (dataGrid.ItemsSource is not ObservableCollection<ColumnViewModel> columns)
        {
            return;
        }

        var sourceIndex = columns.IndexOf(sourceColumn);

        if (sourceIndex < 0)
        {
            return;
        }

        // 行以外（空白部分）へのドロップは末尾移動として扱う
        var targetRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        var targetColumn = targetRow?.Item as ColumnViewModel;
        var targetIndex = targetColumn is null ? columns.Count - 1 : columns.IndexOf(targetColumn);

        if (targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        // マネージャーがあれば Undo 可能なコマンド経由、無ければ直接 Move する
        var undoRedo = GetUndoRedoManager(dataGrid);

        if (undoRedo is not null)
        {
            undoRedo.Execute(new MoveColumnOrderCommand(columns, sourceColumn, targetIndex));
        }
        else
        {
            columns.Move(sourceIndex, targetIndex);
        }

        dataGrid.SelectedItem = sourceColumn;
        e.Handled = true;
    }

    /// <summary>指定要素から視覚ツリーを上方向にたどり、最初に見つかった <typeparamref name="T"/> を返す</summary>
    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
            {
                return typed;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
