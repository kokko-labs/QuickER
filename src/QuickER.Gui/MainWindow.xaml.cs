using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuickER.Behaviors;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER;

/// <summary>アプリケーションのメインウィンドウ（MainWindow.xaml のコードビハインド）</summary>
public partial class MainWindow : Window
{
    /// <summary>ウィンドウ全体で参照する主 ViewModel</summary>
    private readonly MainViewModel _viewModel;

    /// <summary>fit-to-window でコンテンツ周囲に確保する余白（コンテンツ座標 px）</summary>
    private const double FitMargin = 80.0;

    /// <summary>中央基準ズーム補正の直前に記録したズーム倍率（ボタン・キー由来のズーム用）</summary>
    private double _lastZoomLevel = 1.0;

    /// <summary>ミニマップ上でドラッグ追従中かどうか（押下でジャンプ→そのまま連続追従）</summary>
    private bool _isMiniMapDragging;

    /// <summary>DI から注入された ViewModel を DataContext に結び、起動処理を行う</summary>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // 外部変更監視（スレッドプール発火）を UI スレッドへ載せ替えるデリゲートを供給する。
        // Initialize 前に設定し、起動時チェックの一時通知・再読込も UI スレッドで処理させる。
        _viewModel.SetUiPost(action => Dispatcher.BeginInvoke(action));

        // fit-to-window 要求（読込・取込・整列・AI 生成の後）を購読して実行する
        _viewModel.FitToWindowRequested += OnFitToWindowRequested;

        // エンティティ検索のジャンプ要求を購読し、現在の倍率のまま該当エンティティを中央へ据える
        _viewModel.ScrollToEntityRequested += OnScrollToEntityRequested;

        // ボタン・キー由来のズームをビューポート中央基準へ補正するため倍率変更を監視する
        _lastZoomLevel = _viewModel.ZoomLevel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        viewModel.Initialize();
        Closing += MainWindow_Closing;

        // フィーチャーモジュールのツールバーボタンを、グループ区切り単位の折返しで表示する
        BuildFeatureToolbarGroupHosts();
    }

    /// <summary>
    /// フィーチャーモジュールのツールバーボタン群を、グループ区切り（BeginsGroup）のくくりごとに
    /// 1 つの ItemsControl として生成し、ツールバー WrapPanel のアンカー位置へ挿入する。
    /// </summary>
    /// <remarks>
    /// WrapPanel は 1 つの子要素を 1 塊として折り返すため、全ボタンを単一の ItemsControl に入れると
    /// モジュール群全体（7 ボタン）が丸ごと折り返されてしまう。グループごとに ItemsControl を分けて
    /// WrapPanel の直接の子にすることで、「DB 系」「AI 系」「コード生成系」のくくりを崩さず、
    /// 収まらないくくりだけが次の段へ折り返される。モジュール構成は App が起動時に一度だけ設定するため、
    /// ウィンドウ生成時の一回構築でよい（動的な再構成は不要）。
    /// </remarks>
    private void BuildFeatureToolbarGroupHosts()
    {
        var itemTemplate = (DataTemplate)FindResource("FeatureToolbarItemTemplate");
        var groupPanel = (ItemsPanelTemplate)FindResource("FeatureToolbarGroupPanel");
        var anchorIndex = ToolbarWrapPanel.Children.IndexOf(FeatureToolbarGroupsAnchor);

        for (var i = 0; i < _viewModel.FeatureToolbarItemGroups.Count; i++)
        {
            var host = new ItemsControl
            {
                ItemsSource = _viewModel.FeatureToolbarItemGroups[i],
                ItemTemplate = itemTemplate,
                ItemsPanel = groupPanel,
            };

            ToolbarWrapPanel.Children.Insert(anchorIndex + 1 + i, host);
        }
    }

    /// <summary>言語切替ボタン押下で、その場に言語選択の ContextMenu を開く</summary>
    /// <remarks>
    /// 左クリックでも開けるよう明示的に開く（ContextMenu は既定では右クリックで開くため）。
    /// PlacementTarget を通じてメニューはボタンの DataContext（MainViewModel）を引き継ぐ。
    /// </remarks>
    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    /// <summary>ウィンドウ終了時に自動保存を行う</summary>
    /// <remarks>
    /// フィーチャーモジュール（AI チャット・モック生成など）のモードレスウィンドウ後始末は、
    /// 合成ルート（App）が購読する <c>Closing</c> 経由で各モジュールの
    /// <see cref="Extensibility.IFeatureModule.OnMainWindowClosing"/> が担う。
    /// </remarks>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.AutoSave();
    }

    /// <summary>fit-to-window 要求を受けてバウンディングボックスから倍率とスクロール位置を計算・適用する</summary>
    private void OnFitToWindowRequested(object? sender, EventArgs e)
    {
        // 読込直後などレイアウト未確定のタイミングに備え、1 拍置いてから実寸で計算する
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyFitToWindow));
    }

    /// <summary>現在のエンティティ全体が余白込みで収まる倍率とスクロール位置を適用する</summary>
    private void ApplyFitToWindow()
    {
        var bounds = ComputeEntitiesBounds();
        var viewport = new Size(
            DiagramScrollViewer.ViewportWidth,
            DiagramScrollViewer.ViewportHeight
        );

        var fit = ViewportCalculator.CalculateFit(bounds, viewport, FitMargin);

        // 中央基準補正が二重に走らないよう、fit の倍率適用はマウス補正と同じ抑止フラグで囲う
        CanvasViewportBehavior.SuppressCenterZoomCorrection = true;
        _viewModel.ZoomLevel = fit.Zoom;
        _lastZoomLevel = _viewModel.ZoomLevel;
        CanvasViewportBehavior.SuppressCenterZoomCorrection = false;

        // 倍率とオフセットは同一フレーム内で確定させる（遅延反映は中間フレームのちらつきを生む）。
        // UpdateLayout でスクロール範囲を即時確定してから続けてオフセットを適用する
        DiagramScrollViewer.UpdateLayout();
        DiagramScrollViewer.ScrollToHorizontalOffset(fit.Offset.X);
        DiagramScrollViewer.ScrollToVerticalOffset(fit.Offset.Y);
    }

    /// <summary>全エンティティを包含するバウンディングボックス（論理座標）を求める。空図なら空矩形を返す</summary>
    private Rect ComputeEntitiesBounds()
    {
        var entities = _viewModel.Entities;

        if (entities.Count == 0)
        {
            return Rect.Empty;
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var entity in entities)
        {
            minX = Math.Min(minX, entity.X);
            minY = Math.Min(minY, entity.Y);
            maxX = Math.Max(maxX, entity.X + entity.Width);
            maxY = Math.Max(maxY, entity.Y + entity.DisplayHeight);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>ボタン・キー由来のズーム倍率変更をビューポート中央基準へ補正する</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 検索オーバーレイが表示されたら検索ボックスへフォーカスし、既存クエリを全選択する
        if (e.PropertyName == nameof(MainViewModel.IsSearchOverlayVisible))
        {
            if (_viewModel.IsSearchOverlayVisible)
            {
                // 表示直後はまだ可視化前のため、1 拍置いてからフォーカス・全選択する
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        SearchTextBox.Focus();
                        SearchTextBox.SelectAll();
                    })
                );
            }

            return;
        }

        if (e.PropertyName != nameof(MainViewModel.ZoomLevel))
        {
            return;
        }

        var oldZoom = _lastZoomLevel;
        var newZoom = _viewModel.ZoomLevel;
        _lastZoomLevel = newZoom;

        // ホイールズーム・fit はそれぞれ自前でオフセットを補正するため、ここでは中央補正しない
        if (CanvasViewportBehavior.SuppressCenterZoomCorrection || oldZoom == newZoom)
        {
            return;
        }

        // ビューポート中央を固定点として、ボタン・キー由来のズームでも視点がずれないよう補正する
        var center = new Point(
            DiagramScrollViewer.ViewportWidth / 2,
            DiagramScrollViewer.ViewportHeight / 2
        );
        var newOffset = ViewportCalculator.ZoomAtPoint(
            oldZoom,
            newZoom,
            center,
            new Vector(DiagramScrollViewer.HorizontalOffset, DiagramScrollViewer.VerticalOffset)
        );

        // 倍率とオフセットは同一フレーム内で確定させる（遅延反映は中間フレームのちらつきを生む）。
        // UpdateLayout でスクロール範囲を即時確定してから続けてオフセットを適用する
        DiagramScrollViewer.UpdateLayout();
        DiagramScrollViewer.ScrollToHorizontalOffset(newOffset.X);
        DiagramScrollViewer.ScrollToVerticalOffset(newOffset.Y);
    }

    /// <summary>スクロール・ズーム・リサイズのたびに、表示中のコンテンツ領域（論理座標）を VM へ知らせる</summary>
    /// <remarks>新規エンティティを「いま見えている場所」へ配置するための入力（<see cref="MainViewModel.ViewportContentBounds"/>）</remarks>
    private void DiagramScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // ZoomLevel は 50% 以上が保証されているためゼロ除算は起きない
        var zoom = _viewModel.ZoomLevel;
        _viewModel.ViewportContentBounds = new Rect(
            DiagramScrollViewer.HorizontalOffset / zoom,
            DiagramScrollViewer.VerticalOffset / zoom,
            DiagramScrollViewer.ViewportWidth / zoom,
            DiagramScrollViewer.ViewportHeight / zoom
        );
    }

    /// <summary>検索ボックスの Esc でオーバーレイを閉じる</summary>
    /// <remarks>
    /// KeyDown 段の KeyBinding では Esc が発火しないケースが実機で確認されたため
    /// （ToolTip 表示中など他機構による横取りの影響を受けない）Preview 段で確実に処理する。
    /// Enter（次候補へ巡回）は KeyDown 段で問題なく発火するため InputBindings に残している。
    /// </remarks>
    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CloseSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>検索ジャンプ要求を受けて、現在の倍率を保ったまま該当エンティティを中央へ据える</summary>
    private void OnScrollToEntityRequested(object? sender, EntityViewModel entity)
    {
        // 図置換・整列直後などレイアウト未確定のタイミングに備え、1 拍置いて実寸で計算する
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                // エンティティ中心（論理座標）を現在の倍率のままビューポート中央へ据える
                var contentCenter = new Point(
                    entity.X + entity.Width / 2,
                    entity.Y + entity.DisplayHeight / 2
                );
                var viewport = new Size(
                    DiagramScrollViewer.ViewportWidth,
                    DiagramScrollViewer.ViewportHeight
                );
                var offset = ViewportCalculator.CenterOnPoint(
                    contentCenter,
                    _viewModel.ZoomLevel,
                    viewport
                );

                DiagramScrollViewer.ScrollToHorizontalOffset(offset.X);
                DiagramScrollViewer.ScrollToVerticalOffset(offset.Y);
            })
        );
    }

    /// <summary>ミニマップ押下: その地点を視点中心へジャンプし、以降のドラッグ追従を開始する（ズーム倍率は変えない）</summary>
    private void MiniMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isMiniMapDragging = true;
        MiniMapSurface.CaptureMouse();
        PanFromMiniMap(e.GetPosition(MiniMapSurface));
        e.Handled = true;
    }

    /// <summary>ミニマップドラッグ中: 押下地点の移動に追従して視点中心を連続的に動かす</summary>
    private void MiniMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMiniMapDragging)
        {
            return;
        }

        PanFromMiniMap(e.GetPosition(MiniMapSurface));
    }

    /// <summary>ミニマップ押下解除: ドラッグ追従を終了しマウスキャプチャを解放する</summary>
    private void MiniMap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMiniMapDragging)
        {
            return;
        }

        _isMiniMapDragging = false;
        MiniMapSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>ミニマップドラッグ中にマウスキャプチャを失った場合（ポップアップ表示等）に状態を後始末する</summary>
    private void MiniMap_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isMiniMapDragging = false;
    }

    /// <summary>ミニマップ枠座標の点を逆写像し、現在の倍率を保ったままその地点を視点中央へ据える</summary>
    private void PanFromMiniMap(Point miniMapPoint)
    {
        var viewport = new Size(
            DiagramScrollViewer.ViewportWidth,
            DiagramScrollViewer.ViewportHeight
        );
        var offset = _viewModel.CalculateMiniMapPan(miniMapPoint, viewport);

        DiagramScrollViewer.ScrollToHorizontalOffset(offset.X);
        DiagramScrollViewer.ScrollToVerticalOffset(offset.Y);
    }
}
