using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;

namespace ERDesigner.Behaviors;

/// <summary>親 <see cref="Canvas"/> 上の要素をドラッグで移動・リサイズ可能にする添付ビヘイビア</summary>
/// <remarks>
/// <para>使い方（XAML）:</para>
/// <code>
/// &lt;Border beh:DragBehavior.IsEnabled="True"
///         beh:DragBehavior.UndoRedoManager="{Binding DataContext.UndoRedo, RelativeSource={RelativeSource AncestorType=Window}}" /&gt;
/// </code>
/// <para>動作:</para>
/// <list type="number">
///   <item>マウス押下時に現在位置を保存し、対象要素へマウスをキャプチャする</item>
///   <item>マウス移動中は <see cref="EntityViewModel.X"/> / <see cref="EntityViewModel.Y"/> を直接更新する（リレーション線を追従させるため）</item>
///   <item>マウス解放時、移動量が閾値以上なら <see cref="MoveEntityCommand"/> を Undo スタックへ登録する（既に位置適用済みのため再 Execute はしない）</item>
///   <item>移動量が閾値未満ならクリックとみなし、<see cref="MainViewModel.OnEntityClicked(EntityViewModel)"/> で選択状態へ切り替える</item>
/// </list>
/// </remarks>
public static class DragBehavior
{
    /// <summary>クリック判定の許容ピクセル数（この範囲内の移動はクリック扱い）</summary>
    private const double ClickThreshold = 3.0;

    // ---------- 添付プロパティ ----------

    /// <summary>ドラッグ機能の有効・無効を表す添付プロパティ</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DragBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged)
    );

    /// <summary><see cref="IsEnabledProperty"/> の値を設定する</summary>
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    /// <summary><see cref="IsEnabledProperty"/> の値を取得する</summary>
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    /// <summary>移動コマンドの登録先 <see cref="UndoRedoManager"/> を保持する添付プロパティ</summary>
    public static readonly DependencyProperty UndoRedoManagerProperty = DependencyProperty.RegisterAttached(
        "UndoRedoManager",
        typeof(UndoRedoManager),
        typeof(DragBehavior),
        new PropertyMetadata(null)
    );

    /// <summary><see cref="UndoRedoManagerProperty"/> の値を設定する</summary>
    public static void SetUndoRedoManager(DependencyObject d, UndoRedoManager value) => d.SetValue(UndoRedoManagerProperty, value);

    /// <summary><see cref="UndoRedoManagerProperty"/> の値を取得する</summary>
    public static UndoRedoManager? GetUndoRedoManager(DependencyObject d) => (UndoRedoManager?)d.GetValue(UndoRedoManagerProperty);

    // 内部状態は静的フィールドで保持する 同時にドラッグ可能な要素は 1 つに限られる前提

    /// <summary>ドラッグ開始時のキャンバス座標系マウス位置</summary>
    private static Point _startMouse;

    /// <summary>ドラッグ開始時の対象 X / Y 座標</summary>
    private static double _startX;
    private static double _startY;

    /// <summary>移動ドラッグ中かどうか</summary>
    private static bool _isDragging;

    /// <summary>右端グリップによるリサイズ中かどうか</summary>
    private static bool _isResizing;

    /// <summary>リサイズ開始時の幅</summary>
    private static double _startWidth;

    /// <summary>ドラッグ中の要素</summary>
    private static FrameworkElement? _draggedElement;

    /// <summary>ドラッグ中要素に対応するエンティティ ViewModel</summary>
    private static EntityViewModel? _draggedVm;

    /// <summary>右端リサイズグリップの幅 (px)</summary>
    private const double GripWidth = 8;

    /// <summary>添付プロパティ <see cref="IsEnabledProperty"/> 変更時にハンドラの登録・解除を行う</summary>
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            // MouseBinding が MouseUp を Handled にする場合があるため handledEventsToo:true で登録。
            fe.AddHandler(UIElement.MouseLeftButtonDownEvent, (MouseButtonEventHandler)OnMouseDown, handledEventsToo: true);
            fe.AddHandler(UIElement.MouseMoveEvent, (MouseEventHandler)OnMouseMove, handledEventsToo: true);
            fe.AddHandler(UIElement.MouseLeftButtonUpEvent, (MouseButtonEventHandler)OnMouseUp, handledEventsToo: true);
            fe.AddHandler(UIElement.LostMouseCaptureEvent, (MouseEventHandler)OnLostCapture, handledEventsToo: true);
        }
        else
        {
            fe.RemoveHandler(UIElement.MouseLeftButtonDownEvent, (MouseButtonEventHandler)OnMouseDown);
            fe.RemoveHandler(UIElement.MouseMoveEvent, (MouseEventHandler)OnMouseMove);
            fe.RemoveHandler(UIElement.MouseLeftButtonUpEvent, (MouseButtonEventHandler)OnMouseUp);
            fe.RemoveHandler(UIElement.LostMouseCaptureEvent, (MouseEventHandler)OnLostCapture);
        }
    }

    /// <summary>指定要素から視覚ツリーを上方向にたどり最初に見つかった <see cref="Canvas"/> を返す</summary>
    private static Canvas? FindCanvas(DependencyObject? d)
    {
        while (d is not null and not Canvas)
        {
            d = VisualTreeHelper.GetParent(d);
        }

        return d as Canvas;
    }

    /// <summary>マウス押下時にドラッグ開始位置を記録し、移動・リサイズの判定とマウスキャプチャを行う</summary>
    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe)
        {
            return;
        }

        if (fe.DataContext is not EntityViewModel vm)
        {
            return;
        }

        var canvas = FindCanvas(fe);

        if (canvas is null)
        {
            return;
        }

        _draggedElement = fe;
        _draggedVm = vm;
        _startMouse = e.GetPosition(canvas);
        _startX = vm.X;
        _startY = vm.Y;

        // 右端グリップ範囲の押下はリサイズ、それ以外は移動として扱う
        var local = e.GetPosition(fe);

        if (local.X >= fe.ActualWidth - GripWidth && fe.ActualWidth > 0)
        {
            _isResizing = true;
            _isDragging = false;
            _startWidth = vm.Width;
        }
        else
        {
            _isDragging = true;
            _isResizing = false;
        }

        fe.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>マウス移動時にリサイズなら幅を、移動なら座標を ViewModel へ反映する</summary>
    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedElement is null || _draggedVm is null)
        {
            return;
        }

        // ドラッグ・リサイズ未開始時は右端グリップ上でカーソル形状のみ切り替える
        if (!_isDragging && !_isResizing && sender is FrameworkElement cursorFe)
        {
            var lp = e.GetPosition(cursorFe);
            cursorFe.Cursor = (lp.X >= cursorFe.ActualWidth - GripWidth && cursorFe.ActualWidth > 0) ? Cursors.SizeWE : null;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(treatAsClick: false);
            return;
        }

        var canvas = FindCanvas(_draggedElement);

        if (canvas is null)
        {
            return;
        }

        var pos = e.GetPosition(canvas);

        if (_isResizing)
        {
            var delta = pos.X - _startMouse.X;
            var newWidth = Math.Max(120, _startWidth + delta);
            _draggedVm.Width = newWidth;
        }
        else if (_isDragging)
        {
            var newX = _startX + (pos.X - _startMouse.X);
            var newY = _startY + (pos.Y - _startMouse.Y);
            // キャンバスは自動拡大するが負座標は許容しないため下限 0 で丸める
            _draggedVm.X = Math.Max(0, newX);
            _draggedVm.Y = Math.Max(0, newY);
        }
    }

    /// <summary>マウス解放時に移動量からクリックかドラッグかを判定し、後処理を <see cref="EndDrag"/> へ委譲する</summary>
    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging && !_isResizing)
        {
            return;
        }

        var canvas = FindCanvas(_draggedElement);
        var movedPx = double.PositiveInfinity;

        if (canvas is not null)
        {
            var pos = e.GetPosition(canvas);
            movedPx = (pos - _startMouse).Length;
        }

        EndDrag(treatAsClick: !_isResizing && movedPx <= ClickThreshold);
    }

    /// <summary>外部要因などでマウスキャプチャを失った場合に状態をリセットする</summary>
    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging || _isResizing)
        {
            EndDrag(treatAsClick: false);
        }
    }

    /// <summary>ドラッグ状態を終了し、必要に応じて Undo 登録・選択処理を行う</summary>
    /// <param name="treatAsClick">true の場合は移動を取り消して ViewModel のクリックハンドラを呼ぶ</param>
    private static void EndDrag(bool treatAsClick)
    {
        var element = _draggedElement;
        var vm = _draggedVm;
        var oldX = _startX;
        var oldY = _startY;
        var wasResizing = _isResizing;

        _isDragging = false;
        _isResizing = false;
        _draggedElement = null;
        _draggedVm = null;

        if (element is null || vm is null)
        {
            return;
        }

        if (element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        element.Cursor = null;

        if (wasResizing)
        {
            // 幅変更は軽微なため Undo 登録は行わない
            return;
        }

        if (treatAsClick)
        {
            // クリック扱い時はドラッグ中の微小な座標変化を取り消し、選択処理を ViewModel へ委譲する
            vm.X = oldX;
            vm.Y = oldY;

            if (Window.GetWindow(element)?.DataContext is MainViewModel main)
            {
                main.OnEntityClicked(vm);
            }

            return;
        }

        // 実際に座標が変わった場合のみ、適用済み移動を履歴へ登録する（再 Execute は不要）
        if (vm.X != oldX || vm.Y != oldY)
        {
            var mgr = GetUndoRedoManager(element);
            mgr?.Push(new MoveEntityCommand(vm, oldX, oldY, vm.X, vm.Y));
        }

        // 移動後の到達範囲に合わせてキャンバスサイズを再計算する
        if (Window.GetWindow(element)?.DataContext is MainViewModel mainVm)
        {
            mainVm.RefreshCanvasSize();
        }
    }
}
