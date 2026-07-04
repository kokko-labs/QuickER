using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QuickER.ViewModels;

namespace QuickER.Behaviors;

/// <summary>
/// キャンバス空白部の左ドラッグで矩形選択（ラバーバンド）を行う添付ビヘイビア
/// </summary>
/// <remarks>
/// <para>使い方（XAML）: 選択対象を載せる <see cref="System.Windows.Controls.Grid"/>（DiagramCanvas）へ付与する。</para>
/// <code>
/// &lt;Grid beh:RubberBandBehavior.IsEnabled="True" /&gt;
/// </code>
/// <para>動作:</para>
/// <list type="number">
///   <item>押下は <c>PreviewMouseLeftButtonDown</c>（トンネル）で受ける。バブリング段の
///     <c>MouseLeftButtonDown</c> では、キャンバスの MouseBinding（CanvasClickCommand）が
///     クラスハンドラ段階で先に発火・Handled にするためインスタンスハンドラへ届かない（実機で確認済みの罠）。
///     エンティティ／リレーション要素配下の押下はビジュアルツリー判定で除外し <see cref="DragBehavior"/> 等へ委ねる</item>
///   <item>押下時点では矩形を出さず・キャプチャもせず・Handled にもしない（Ctrl 押下の追加選択時のみ
///     Handled にし、CanvasClickCommand の全解除から既存選択を保護する）。閾値を超えて動いた時のみ
///     ラバーバンドを開始する。閾値未満のクリックは従来どおり CanvasClickCommand の全選択解除に委ねる</item>
///   <item>ドラッグ中は VM の矩形状態（<see cref="MainViewModel.IsRubberBandVisible"/> ほか）を更新する。
///     矩形は DiagramCanvas 内に描画されるため LayoutTransform（ズーム）に透過的に追従する</item>
///   <item>解放時、ラバーバンドが成立していれば <see cref="MainViewModel.ApplyRubberBandSelection"/> で
///     交差選択を確定し、イベントを Handled にして <c>CanvasClickCommand</c> の解除を抑止する</item>
/// </list>
/// <para>パン中（<see cref="CanvasViewportBehavior.IsPanActive"/>）はラバーバンドを開始しない。</para>
/// </remarks>
public static class RubberBandBehavior
{
    /// <summary>ドラッグ開始とみなす移動量の閾値（px。<see cref="DragBehavior"/> のクリック閾値と揃える）</summary>
    private const double DragThreshold = 3.0;

    // 内部状態は静的フィールドで保持する（同時にラバーバンド可能な面は 1 つに限られる前提）

    /// <summary>ドラッグ対象の要素（DiagramCanvas）</summary>
    private static FrameworkElement? _surface;

    /// <summary>押下時のキャンバス座標</summary>
    private static Point _origin;

    /// <summary>ボタン押下後・閾値判定待ちかどうか</summary>
    private static bool _pending;

    /// <summary>ラバーバンドが成立して選択矩形を描画中かどうか</summary>
    private static bool _active;

    /// <summary>Ctrl 押下（既存選択への追加）でドラッグを開始したかどうか</summary>
    private static bool _additive;

    // ---------- 添付プロパティ ----------

    /// <summary>ラバーバンド機能の有効・無効を表す添付プロパティ</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(RubberBandBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged)
        );

    /// <summary><see cref="IsEnabledProperty"/> の値を設定する</summary>
    public static void SetIsEnabled(DependencyObject d, bool value) =>
        d.SetValue(IsEnabledProperty, value);

    /// <summary><see cref="IsEnabledProperty"/> の値を取得する</summary>
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    /// <summary>添付プロパティ変更時にハンドラの登録・解除を行う</summary>
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            // バブリング段はキャンバスの MouseBinding（CanvasClickCommand）がクラスハンドラで
            // 先に Handled にするため届かない。トンネル段（Preview）で受け、対象判定は自前で行う
            fe.PreviewMouseLeftButtonDown += OnMouseDown;
            fe.PreviewMouseMove += OnMouseMove;
            fe.PreviewMouseLeftButtonUp += OnMouseUp;
            fe.LostMouseCapture += OnLostCapture;
        }
        else
        {
            fe.PreviewMouseLeftButtonDown -= OnMouseDown;
            fe.PreviewMouseMove -= OnMouseMove;
            fe.PreviewMouseLeftButtonUp -= OnMouseUp;
            fe.LostMouseCapture -= OnLostCapture;
        }
    }

    /// <summary>押下位置がエンティティ要素の内側かどうかをビジュアルツリーで判定する</summary>
    /// <remarks>
    /// Preview 段はエンティティ上の押下でも発火するため、DataContext に
    /// <see cref="EntityViewModel"/> を持つ要素が press 元の祖先にあれば
    /// ラバーバンドを開始せず <see cref="DragBehavior"/>（移動・リサイズ・クリック選択）へ委ねる。
    /// </remarks>
    private static bool IsInsideEntity(object? originalSource, FrameworkElement surface)
    {
        var current = originalSource as DependencyObject;

        while (current is not null && !ReferenceEquals(current, surface))
        {
            if (current is FrameworkElement { DataContext: EntityViewModel })
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>空白部押下でラバーバンドの起点を記録する（この時点では矩形もキャプチャも出さない）</summary>
    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // パン中はラバーバンドを開始しない
        if (CanvasViewportBehavior.IsPanActive)
        {
            return;
        }

        if (sender is not FrameworkElement fe || fe.DataContext is not MainViewModel)
        {
            return;
        }

        // エンティティ内側の押下は DragBehavior（移動・リサイズ・クリック選択）の領分
        if (IsInsideEntity(e.OriginalSource, fe))
        {
            return;
        }

        _surface = fe;
        _origin = e.GetPosition(fe);
        _pending = true;
        _active = false;
        _additive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        // 通常はここで Handled にしない（閾値未満のクリックは CanvasClickCommand の全解除へ委ねる）。
        // Ctrl 押下＝既存選択への追加意図のときだけ Handled にし、押下時点の全解除から選択を保護する
        if (_additive)
        {
            e.Handled = true;
        }
    }

    /// <summary>閾値を超えたらラバーバンドを開始し、矩形の位置・サイズを更新する</summary>
    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_surface is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (_surface.DataContext is not MainViewModel vm)
        {
            return;
        }

        var current = e.GetPosition(_surface);

        // 未成立時は閾値超えでラバーバンドを開始する（キャプチャして矩形を表示）
        if (_pending && !_active)
        {
            if ((current - _origin).Length < DragThreshold)
            {
                return;
            }

            _active = true;
            _pending = false;
            _surface.CaptureMouse();
            vm.IsRubberBandVisible = true;
        }

        if (!_active)
        {
            return;
        }

        UpdateRectangle(vm, current);
        e.Handled = true;
    }

    /// <summary>解放時、ラバーバンドが成立していれば交差選択を確定する</summary>
    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var surface = _surface;
        var active = _active;
        var additive = _additive;

        _pending = false;
        _active = false;
        _surface = null;

        if (surface is null)
        {
            return;
        }

        if (surface.IsMouseCaptured)
        {
            surface.ReleaseMouseCapture();
        }

        if (surface.DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.IsRubberBandVisible = false;

        if (!active)
        {
            // 閾値未満のクリックはラバーバンド不成立。CanvasClickCommand へ委ね全選択解除させる
            return;
        }

        var area = new Rect(
            vm.RubberBandX,
            vm.RubberBandY,
            vm.RubberBandWidth,
            vm.RubberBandHeight
        );

        vm.ApplyRubberBandSelection(area, additive);

        // ラバーバンド成立時は Handled にして CanvasClickCommand の解除を抑止する
        e.Handled = true;
    }

    /// <summary>外部要因でキャプチャを失った場合に矩形状態を後始末する</summary>
    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_surface?.DataContext is MainViewModel vm)
        {
            vm.IsRubberBandVisible = false;
        }

        _pending = false;
        _active = false;
        _surface = null;
    }

    /// <summary>起点と現在位置から正規化した矩形を VM へ反映する</summary>
    private static void UpdateRectangle(MainViewModel vm, Point current)
    {
        var x = Math.Min(_origin.X, current.X);
        var y = Math.Min(_origin.Y, current.Y);
        var width = Math.Abs(current.X - _origin.X);
        var height = Math.Abs(current.Y - _origin.Y);

        vm.RubberBandX = x;
        vm.RubberBandY = y;
        vm.RubberBandWidth = width;
        vm.RubberBandHeight = height;
    }
}
