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

    /// <summary>キャンバスのズーム倍率（1.0 = 100%）。設定時に範囲へクランプする</summary>
    /// <remarks>ScaleTransform の ScaleX/ScaleY にバインドされる。10%〜200% の範囲を保証する</remarks>
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
            }
        }
    }

    /// <summary>ステータスバーに表示する倍率文字列（例 "100%"）</summary>
    public string ZoomPercentText => $"{Math.Round(_zoomLevel * 100)}%";

    /// <summary>1 ノッチ分ズームインする（ビューポート中央基準。補正は View 側）</summary>
    [RelayCommand]
    private void ZoomIn() => ZoomLevel = _zoomLevel * ViewportCalculator.ZoomStep;

    /// <summary>1 ノッチ分ズームアウトする（ビューポート中央基準。補正は View 側）</summary>
    [RelayCommand]
    private void ZoomOut() => ZoomLevel = _zoomLevel / ViewportCalculator.ZoomStep;

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
