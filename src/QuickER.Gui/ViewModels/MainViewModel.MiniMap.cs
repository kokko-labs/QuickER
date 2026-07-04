using System.Windows;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>
/// ミニマップ（キャンバス全体の縮小投影）の表示状態と射影データを担う partial
/// </summary>
/// <remarks>
/// 図（エンティティ全体のバウンディングボックス）がビューポートに収まらないときだけ自動表示する
/// ナビゲーション補助。エンティティは小さな矩形（タイトル背景色反映）、リレーションは細い線として
/// 描き、現在の表示範囲を示すビューポート枠を重ねる。文字は描かない。
/// これらはすべて純粋な表示状態であり Undo 履歴・保存対象には含めない。
///
/// 再計算は 2 種類に分ける
/// <list type="bullet">
/// <item>軽い更新: スクロール（<see cref="ViewportContentBounds"/> 変更）ではビューポート枠と可視判定のみを更新する</item>
/// <item>全再計算: エンティティ座標/サイズ変更・コレクション変更・ズーム変更では射影自体と全描画データを作り直す</item>
/// </list>
/// 描画データ（数十要素）は毎回再生成し、<see cref="OnPropertyChanged"/> で差し替える。
/// </remarks>
public partial class MainViewModel
{
    /// <summary>ミニマップ枠の描画サイズ（px）。XAML の Canvas サイズと一致させること</summary>
    public const double MiniMapWidth = 200;

    /// <summary>ミニマップ枠の描画高さ（px）。XAML の Canvas サイズと一致させること</summary>
    public const double MiniMapHeight = 140;

    /// <summary>ミニマップ投影時にコンテンツ周囲へ確保する余白（論理座標 px）</summary>
    private const double MiniMapMargin = 40;

    /// <summary><see cref="IsMiniMapEnabled"/> のバッキングフィールド（既定は ON）</summary>
    private bool _isMiniMapEnabled = true;

    /// <summary>現在の射影（コンテンツ↔ミニマップ座標の変換）。逆写像でクリック地点をコンテンツ座標へ戻す</summary>
    private MiniMapProjection _miniMapProjection = new(1.0, 0, 0);

    /// <summary>コンテンツ全体（余白なし）のバウンディングボックス（論理座標）。空図なら空矩形</summary>
    private Rect _contentBounds = Rect.Empty;

    /// <summary>ミニマップのトグル状態（既定 ON。OFF なら図が収まらなくても常に非表示）</summary>
    /// <remarks>純粋な表示状態のため Undo 履歴・保存対象には含めない</remarks>
    public bool IsMiniMapEnabled
    {
        get => _isMiniMapEnabled;
        set
        {
            if (SetProperty(ref _isMiniMapEnabled, value))
            {
                OnPropertyChanged(nameof(IsMiniMapVisible));
            }
        }
    }

    /// <summary>ミニマップを表示中かどうか（導出値）</summary>
    /// <remarks>
    /// トグル ON かつ 空図でない かつ コンテンツ bbox が現在のビューポートに収まっていないときだけ true。
    /// 収まっている（ビューポートがコンテンツ全体を包含する）場合はナビゲーション不要のため非表示にする。
    /// </remarks>
    public bool IsMiniMapVisible
    {
        get
        {
            if (!_isMiniMapEnabled || _contentBounds.IsEmpty)
            {
                return false;
            }

            // ビューポート未確定（ヘッドレス）では収まり判定ができないため非表示扱いとする
            if (
                _viewportContentBounds.IsEmpty
                || _viewportContentBounds.Width <= 0
                || _viewportContentBounds.Height <= 0
            )
            {
                return false;
            }

            // コンテンツ全体が現在の表示領域に収まっているならナビゲーション不要 → 非表示
            return !_viewportContentBounds.Contains(_contentBounds);
        }
    }

    /// <summary>ミニマップに描くエンティティ矩形（ミニマップ枠座標へ投影済み）</summary>
    public IReadOnlyList<MiniMapEntity> MiniMapEntities { get; private set; } =
        Array.Empty<MiniMapEntity>();

    /// <summary>ミニマップに描くリレーション線（ミニマップ枠座標へ投影済み）</summary>
    public IReadOnlyList<MiniMapLine> MiniMapLines { get; private set; } =
        Array.Empty<MiniMapLine>();

    /// <summary>現在の表示範囲を示すビューポート枠（ミニマップ枠座標）</summary>
    public Rect MiniMapViewport { get; private set; } = Rect.Empty;

    /// <summary>
    /// 射影・全描画データを作り直す（エンティティ座標/サイズ変更・コレクション変更・ズーム変更時）
    /// </summary>
    /// <remarks>数十要素の再生成のため毎回作り直し、参照差し替えで変更通知する</remarks>
    private void RecalculateMiniMap()
    {
        _contentBounds = ComputeContentBounds();
        _miniMapProjection = ViewportCalculator.CalculateMiniMapProjection(
            _contentBounds,
            new Size(MiniMapWidth, MiniMapHeight),
            MiniMapMargin
        );

        // 空図では描画データを空にし、可視判定も false になる
        if (_contentBounds.IsEmpty)
        {
            MiniMapEntities = Array.Empty<MiniMapEntity>();
            MiniMapLines = Array.Empty<MiniMapLine>();
        }
        else
        {
            MiniMapEntities = BuildMiniMapEntities();
            MiniMapLines = BuildMiniMapLines();
        }

        OnPropertyChanged(nameof(MiniMapEntities));
        OnPropertyChanged(nameof(MiniMapLines));

        // 射影が変わったのでビューポート枠と可視判定も併せて更新する
        UpdateMiniMapViewport();
        OnPropertyChanged(nameof(IsMiniMapVisible));
    }

    /// <summary>スクロール連動の軽い更新: ビューポート枠と可視判定のみを更新する（射影データは作り直さない）</summary>
    private void OnViewportContentBoundsChanged()
    {
        UpdateMiniMapViewport();
        OnPropertyChanged(nameof(IsMiniMapVisible));
    }

    /// <summary>現在のビューポート（コンテンツ論理座標）を射影してミニマップ枠座標のビューポート枠を求める</summary>
    private void UpdateMiniMapViewport()
    {
        MiniMapViewport = _viewportContentBounds.IsEmpty
            ? Rect.Empty
            : _miniMapProjection.ToMiniMap(_viewportContentBounds);

        OnPropertyChanged(nameof(MiniMapViewport));
    }

    /// <summary>全エンティティを包含するバウンディングボックス（論理座標）を求める。空図なら空矩形</summary>
    /// <remarks>View 側の fit 計算と同じ「X..X+Width / Y..Y+DisplayHeight」の範囲を用いる</remarks>
    private Rect ComputeContentBounds()
    {
        if (Entities.Count == 0)
        {
            return Rect.Empty;
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var entity in Entities)
        {
            minX = Math.Min(minX, entity.X);
            minY = Math.Min(minY, entity.Y);
            maxX = Math.Max(maxX, entity.X + entity.Width);
            maxY = Math.Max(maxY, entity.Y + entity.DisplayHeight);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>各エンティティを矩形（タイトル背景色付き）としてミニマップ枠座標へ投影する</summary>
    private List<MiniMapEntity> BuildMiniMapEntities()
    {
        var result = new List<MiniMapEntity>(Entities.Count);

        foreach (var entity in Entities)
        {
            var rect = _miniMapProjection.ToMiniMap(
                new Rect(entity.X, entity.Y, entity.Width, entity.DisplayHeight)
            );

            result.Add(
                new MiniMapEntity(
                    rect.X,
                    rect.Y,
                    rect.Width,
                    rect.Height,
                    entity.TitleBackgroundColor
                )
            );
        }

        return result;
    }

    /// <summary>各リレーションの端点（X1/Y1/X2/Y2）を細い線としてミニマップ枠座標へ投影する</summary>
    private List<MiniMapLine> BuildMiniMapLines()
    {
        var result = new List<MiniMapLine>(Relationships.Count);

        foreach (var relationship in Relationships)
        {
            var p1 = _miniMapProjection.ToMiniMap(new Point(relationship.X1, relationship.Y1));
            var p2 = _miniMapProjection.ToMiniMap(new Point(relationship.X2, relationship.Y2));

            result.Add(new MiniMapLine(p1.X, p1.Y, p2.X, p2.Y));
        }

        return result;
    }

    /// <summary>
    /// ミニマップ上の押下・ドラッグ地点（枠座標）を視点中心へ据えるスクロールオフセットを求める
    /// </summary>
    /// <param name="miniMapPoint">ミニマップ枠座標での押下位置（px）</param>
    /// <param name="viewport">ビューポート（表示領域）のサイズ（px）</param>
    /// <returns>現在のズーム倍率を保ったまま、押下地点をビューポート中央へ据えるスクロールオフセット</returns>
    /// <remarks>
    /// 逆写像でミニマップ枠座標をコンテンツ論理座標へ戻し、既存の
    /// <see cref="ViewportCalculator.CenterOnPoint"/> で中央寄せオフセットを計算する。ズーム倍率は変えない。
    /// </remarks>
    public Vector CalculateMiniMapPan(Point miniMapPoint, Size viewport)
    {
        var contentPoint = _miniMapProjection.ToContent(miniMapPoint);

        return ViewportCalculator.CenterOnPoint(contentPoint, ZoomLevel, viewport);
    }
}

/// <summary>ミニマップに描くエンティティ矩形（ミニマップ枠座標へ投影済み）</summary>
/// <param name="X">左上 X（ミニマップ枠座標 px）</param>
/// <param name="Y">左上 Y（ミニマップ枠座標 px）</param>
/// <param name="Width">幅（px）</param>
/// <param name="Height">高さ（px）</param>
/// <param name="TitleBackgroundColor">タイトル帯背景色（エンティティの見出し色を反映）</param>
public readonly record struct MiniMapEntity(
    double X,
    double Y,
    double Width,
    double Height,
    string TitleBackgroundColor
);

/// <summary>ミニマップに描くリレーション線（ミニマップ枠座標へ投影済み）</summary>
/// <param name="X1">起点 X（px）</param>
/// <param name="Y1">起点 Y（px）</param>
/// <param name="X2">終点 X（px）</param>
/// <param name="Y2">終点 Y（px）</param>
public readonly record struct MiniMapLine(double X1, double Y1, double X2, double Y2);
