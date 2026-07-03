using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Behaviors;

/// <summary>
/// <see cref="ScrollViewer"/> にズーム・パン操作を付与する添付ビヘイビア
/// </summary>
/// <remarks>
/// <para>操作:</para>
/// <list type="bullet">
///   <item>Ctrl+ホイール: マウス位置を中心にズーム（<see cref="ViewportCalculator.ZoomAtPoint"/> でオフセット補正）</item>
///   <item>Shift+ホイール: 横スクロール。修飾なしのホイールは既定の縦スクロールへ素通し</item>
///   <item>中ボタンドラッグ / Space+左ドラッグ: パン</item>
/// </list>
/// <para>
/// 実際のズーム倍率は <see cref="MainViewModel.ZoomLevel"/> が保持し、
/// LayoutTransform(ScaleTransform) 経由でキャンバスへ反映される。
/// パン中は <see cref="IsPanActive"/> が true になり、<see cref="DragBehavior"/> の
/// エンティティ移動を抑止する。
/// </para>
/// </remarks>
public static class CanvasViewportBehavior
{
    /// <summary>1 ホイールノッチあたりの横スクロール量（px）</summary>
    private const double HorizontalScrollStep = 48.0;

    /// <summary>
    /// Space 押下中またはパン中かどうか（<see cref="DragBehavior"/> がエンティティ移動を抑止するために参照する）
    /// </summary>
    public static bool IsPanActive { get; private set; }

    /// <summary>
    /// ホイールズームがオフセットを自前補正している間、コードビハインドの中央基準補正を抑止するフラグ
    /// </summary>
    /// <remarks>
    /// ホイールズームはマウス位置を中心に補正するため、View 側のビューポート中央補正と競合させない。
    /// ボタン・キー由来のズームでは false のままとし、中央基準補正を有効にする。
    /// </remarks>
    public static bool SuppressCenterZoomCorrection { get; set; }

    // 内部状態は静的フィールドで保持する（同時にパン可能なビューは 1 つに限られる前提）

    /// <summary>Space キーが押下されているかどうか</summary>
    private static bool _isSpaceDown;

    /// <summary>ドラッグによるパンの実行中かどうか</summary>
    private static bool _isPanning;

    /// <summary>パン開始時のマウス位置（ScrollViewer 基準）</summary>
    private static Point _panStartMouse;

    /// <summary>パン開始時のスクロールオフセット</summary>
    private static double _panStartHorizontal;
    private static double _panStartVertical;

    /// <summary>対象 ScrollViewer</summary>
    private static ScrollViewer? _scrollViewer;

    // ---------- 添付プロパティ ----------

    /// <summary>ズーム・パン機能の有効・無効を表す添付プロパティ</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(CanvasViewportBehavior),
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
        if (d is not ScrollViewer sv)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            _scrollViewer = sv;
            sv.PreviewMouseWheel += OnPreviewMouseWheel;
            sv.PreviewMouseDown += OnPreviewMouseDown;
            sv.PreviewMouseMove += OnPreviewMouseMove;
            sv.PreviewMouseUp += OnPreviewMouseUp;

            // パン中に他要素へキャプチャを奪われた場合に状態が残留しないようにする
            sv.LostMouseCapture += OnLostMouseCapture;

            // Space 監視はウィンドウ全体で行う（キャンバスにフォーカスがなくても効かせるため）
            sv.Loaded += OnScrollViewerLoaded;
        }
        else
        {
            sv.PreviewMouseWheel -= OnPreviewMouseWheel;
            sv.PreviewMouseDown -= OnPreviewMouseDown;
            sv.PreviewMouseMove -= OnPreviewMouseMove;
            sv.PreviewMouseUp -= OnPreviewMouseUp;
            sv.LostMouseCapture -= OnLostMouseCapture;
            sv.Loaded -= OnScrollViewerLoaded;
        }
    }

    /// <summary>ScrollViewer ロード時に所属ウィンドウへ Space キー監視を登録する</summary>
    private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv)
        {
            return;
        }

        var window = Window.GetWindow(sv);

        if (window is null)
        {
            return;
        }

        // 二重登録を避けるため一度解除してから登録する
        window.PreviewKeyDown -= OnWindowPreviewKeyDown;
        window.PreviewKeyUp -= OnWindowPreviewKeyUp;
        window.Deactivated -= OnWindowDeactivated;
        window.PreviewKeyDown += OnWindowPreviewKeyDown;
        window.PreviewKeyUp += OnWindowPreviewKeyUp;
        window.Deactivated += OnWindowDeactivated;
    }

    /// <summary>ウィンドウ非アクティブ化時に Space 押下状態を破棄する</summary>
    /// <remarks>
    /// Space 押下中に Alt+Tab やダイアログ表示でフォーカスを失うと KeyUp を取り逃し、
    /// パン待機状態が固着して以降の左ドラッグがすべてパン扱いになる（エンティティを動かせなくなる）。
    /// パン実行中のキャプチャ喪失は <see cref="OnLostMouseCapture"/> が後始末する。
    /// </remarks>
    private static void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _isSpaceDown = false;

        if (!_isPanning)
        {
            IsPanActive = false;

            if (_scrollViewer is not null)
            {
                _scrollViewer.Cursor = null;
            }
        }
    }

    // ---------- ズーム / 横スクロール ----------

    /// <summary>Ctrl+ホイールでズーム、Shift+ホイールで横スクロールする</summary>
    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;

        // Ctrl 押下時: マウス位置を中心にズームする
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (sv.DataContext is not MainViewModel vm)
            {
                return;
            }

            var oldZoom = vm.ZoomLevel;

            // 1 ノッチ = ×1.1（乗算）。ホイールの符号で拡大・縮小を切り替える
            var factor =
                e.Delta > 0 ? ViewportCalculator.ZoomStep : 1.0 / ViewportCalculator.ZoomStep;
            var newZoom = ViewportCalculator.ClampZoom(oldZoom * factor);

            ApplyZoomAtMouse(sv, vm, oldZoom, newZoom, e.GetPosition(sv));
            e.Handled = true;
            return;
        }

        // Shift 押下時: 横スクロールへ振り替える
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            var delta = e.Delta > 0 ? -HorizontalScrollStep : HorizontalScrollStep;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset + delta);
            e.Handled = true;
            return;
        }

        // 修飾なしは既定の縦スクロールへ素通しする
    }

    /// <summary>指定マウス位置を中心にズーム倍率とスクロールオフセットを適用する</summary>
    private static void ApplyZoomAtMouse(
        ScrollViewer sv,
        MainViewModel vm,
        double oldZoom,
        double newZoom,
        Point mouse
    )
    {
        // 倍率が変わらない（クランプ境界）なら何もしない
        if (newZoom == oldZoom)
        {
            return;
        }

        var newOffset = ViewportCalculator.ZoomAtPoint(
            oldZoom,
            newZoom,
            mouse,
            new Vector(sv.HorizontalOffset, sv.VerticalOffset)
        );

        // ホイールズーム中はコードビハインドの中央基準補正を止め、マウス位置基準の補正のみを効かせる
        SuppressCenterZoomCorrection = true;
        vm.ZoomLevel = newZoom;
        SuppressCenterZoomCorrection = false;

        // LayoutTransform 適用後にスクロール範囲が確定するため、1 拍置いてからオフセットを反映する
        sv.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                sv.ScrollToHorizontalOffset(newOffset.X);
                sv.ScrollToVerticalOffset(newOffset.Y);
            })
        );
    }

    // ---------- パン ----------

    /// <summary>中ボタン、または Space+左ボタンの押下でパンを開始する</summary>
    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer sv)
        {
            return;
        }

        // キャンバス内にはフォーカス可能要素がなく、クリックしてもプロパティパネル等の
        // TextBox からフォーカスが戻らない。フォーカスが編集コントロールに残ったままだと
        // Space パンがテキスト入力保護（IsTextInputFocused）に阻まれて効かなくなるため、
        // キャンバスへのマウス操作を「編集を離れた」合図としてフォーカスを引き取る
        if (!sv.IsKeyboardFocusWithin)
        {
            sv.Focus();
        }

        var isMiddle = e.MiddleButton == MouseButtonState.Pressed;
        var isSpaceLeft = _isSpaceDown && e.LeftButton == MouseButtonState.Pressed;

        if (!isMiddle && !isSpaceLeft)
        {
            return;
        }

        _isPanning = true;
        IsPanActive = true;
        _panStartMouse = e.GetPosition(sv);
        _panStartHorizontal = sv.HorizontalOffset;
        _panStartVertical = sv.VerticalOffset;

        sv.Cursor = Cursors.ScrollAll;
        sv.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>パン中はマウス移動量に応じてスクロールオフセットを直接操作する</summary>
    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || sender is not ScrollViewer sv)
        {
            return;
        }

        var current = e.GetPosition(sv);

        // マウスを右へ動かすと内容が右へ動く（＝オフセットは減る）ため符号を反転する
        sv.ScrollToHorizontalOffset(_panStartHorizontal - (current.X - _panStartMouse.X));
        sv.ScrollToVerticalOffset(_panStartVertical - (current.Y - _panStartMouse.Y));
        e.Handled = true;
    }

    /// <summary>ボタン解放でパンを終了する</summary>
    private static void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning || sender is not ScrollViewer sv)
        {
            return;
        }

        EndPan(sv);
        e.Handled = true;
    }

    /// <summary>パン実行中にマウスキャプチャを失った場合（ポップアップ表示等）に状態を後始末する</summary>
    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isPanning && sender is ScrollViewer sv)
        {
            EndPan(sv);
        }
    }

    /// <summary>パン状態を解除し、カーソルとマウスキャプチャを元に戻す</summary>
    private static void EndPan(ScrollViewer sv)
    {
        _isPanning = false;

        // Space 押下が継続している間はパン待機カーソル（Hand）を維持する
        IsPanActive = _isSpaceDown;
        sv.Cursor = _isSpaceDown ? Cursors.Hand : null;

        if (sv.IsMouseCaptured)
        {
            sv.ReleaseMouseCapture();
        }
    }

    // ---------- Space キー監視 ----------

    /// <summary>Space 押下でパン待機状態へ入る（編集コントロールへの入力中は横取りしない）</summary>
    private static void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || _isSpaceDown)
        {
            return;
        }

        // テキスト入力中の Space はプロパティパネルの編集を壊さないため横取りしない
        if (IsTextInputFocused())
        {
            return;
        }

        _isSpaceDown = true;
        IsPanActive = true;

        if (_scrollViewer is not null)
        {
            _scrollViewer.Cursor = Cursors.Hand;
        }

        // キャンバス操作としての Space はスクロール等の既定動作を抑止する
        e.Handled = true;
    }

    /// <summary>Space 解放でパン待機状態を解除する</summary>
    private static void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_isSpaceDown)
        {
            return;
        }

        _isSpaceDown = false;

        // ドラッグ中でなければパン状態を解除し、カーソルを戻す
        if (!_isPanning)
        {
            IsPanActive = false;

            if (_scrollViewer is not null)
            {
                _scrollViewer.Cursor = null;
            }
        }
    }

    /// <summary>現在のフォーカスがテキスト編集コントロールにあるかどうかを判定する</summary>
    /// <remarks>TextBoxBase（TextBox / RichTextBox）と PasswordBox のとき true を返す</remarks>
    private static bool IsTextInputFocused()
    {
        return Keyboard.FocusedElement is TextBoxBase or PasswordBox;
    }
}
