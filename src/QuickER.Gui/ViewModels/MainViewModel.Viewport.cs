using System.Windows;
using CommunityToolkit.Mvvm.Input;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>
/// キャンバスのズーム倍率・fit-to-window に関する状態とコマンドを担う partial
/// </summary>
/// <remarks>
/// ズーム／パンは純粋な表示状態であり Undo 対象外・保存フォーマット無改変とする。
/// スクロールオフセットの実操作は View 側（ScrollViewer）が持つため、
/// fit の実行は <see cref="FitToWindowRequested"/> イベントでコードビハインドへ委譲する。
/// ビューポート中央基準のズームイン／アウトの座標補正もビヘイビア／View 側で行う。
/// </remarks>
public partial class MainViewModel
{
    /// <summary>ズーム倍率のバッキングフィールド（既定は等倍）</summary>
    private double _zoomLevel = 1.0;

    /// <summary>fit-to-window の実行を View（コードビハインド）へ要求するイベント</summary>
    /// <remarks>スクロールオフセットとビューポート実寸を持つのは View 側のため、計算・適用はそこで行う</remarks>
    public event EventHandler? FitToWindowRequested;

    /// <summary><see cref="ViewportContentBounds"/> のバッキングフィールド</summary>
    private Rect _viewportContentBounds = Rect.Empty;

    /// <summary>現在表示中のキャンバス領域（コンテンツ論理座標）。View が ScrollChanged のたびに更新する</summary>
    /// <remarks>
    /// 新規エンティティを「いま見えている場所」へ配置するための入力。
    /// View が無いヘッドレス実行では空矩形のままとなり、追加位置は従来のカスケード配置へフォールバックする。
    /// スクロールのたびに設定されるため、setter ではミニマップのビューポート枠・可視判定だけを軽く更新し、
    /// 全射影の再計算（射影データ）は行わない（<c>MainViewModel.MiniMap.cs</c> 側で分離）
    /// </remarks>
    public Rect ViewportContentBounds
    {
        get => _viewportContentBounds;
        set
        {
            _viewportContentBounds = value;

            // スクロール連動: ミニマップのビューポート枠と自動表示の可視判定のみを軽く更新する
            OnViewportContentBoundsChanged();
        }
    }

    /// <summary>キャンバスのズーム倍率（1.0 = 100%）。設定時に範囲へクランプする</summary>
    /// <remarks>ScaleTransform の ScaleX/ScaleY にバインドされる。50%〜200% の範囲を保証する</remarks>
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            var clamped = ViewportCalculator.ClampZoom(value);

            if (SetProperty(ref _zoomLevel, clamped))
            {
                // 倍率表示ラベル（"100%" など）も併せて更新する
                OnPropertyChanged(nameof(ZoomPercentText));

                // 倍率変更で表示範囲が変わるため、ミニマップのビューポート枠・可視判定を更新する
                // （射影データはズームに依存しないため軽い更新で足りる。ScrollChanged が届かない
                //  ヘッドレス／タイミングでも枠が追従するようここでも呼ぶ）
                OnViewportContentBoundsChanged();
            }
        }
    }

    /// <summary>ステータスバーに表示する倍率文字列（例 "100%"）</summary>
    public string ZoomPercentText => $"{Math.Round(_zoomLevel * 100)}%";

    /// <summary>10% 刻みでズームインする（次の 10% の倍数へスナップ。ビューポート中央基準・補正は View 側）</summary>
    [RelayCommand]
    private void ZoomIn() => ZoomLevel = ViewportCalculator.ZoomInStep(_zoomLevel);

    /// <summary>10% 刻みでズームアウトする（前の 10% の倍数へスナップ。ビューポート中央基準・補正は View 側）</summary>
    [RelayCommand]
    private void ZoomOut() => ZoomLevel = ViewportCalculator.ZoomOutStep(_zoomLevel);

    /// <summary>ズーム倍率を 100% へ戻す</summary>
    [RelayCommand]
    private void ResetZoom() => ZoomLevel = 1.0;

    /// <summary>fit-to-window を要求する（実計算・適用は View 側で行う）</summary>
    [RelayCommand]
    private void FitToWindow() => RequestFitToWindow();

    /// <summary><see cref="FitToWindowRequested"/> を発火して View へ fit を要求する</summary>
    /// <remarks>ファイル読込・取込・自動整列・AI 生成の直後にも共通で呼び出す</remarks>
    private void RequestFitToWindow() => FitToWindowRequested?.Invoke(this, EventArgs.Empty);
}
