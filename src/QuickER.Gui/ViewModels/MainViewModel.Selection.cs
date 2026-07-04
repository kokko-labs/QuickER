using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.UndoRedo;

namespace QuickER.ViewModels;

/// <summary>
/// 複数選択（Ctrl+クリック・ラバーバンド・Ctrl+A・Esc）とグループ移動を担う partial
/// </summary>
/// <remarks>
/// 選択の正は各 <see cref="EntityViewModel.IsSelected"/> フラグであり、<see cref="MainViewModel.SelectedEntity"/> は
/// そのうち最後に操作した 1 個（主選択）を表す。選択の変化は Undo 履歴・保存対象には含めない
/// （<see cref="QuickER.UndoRedo.DiagramChangeTracker"/> は IsSelected を追跡しないため、選択操作で履歴は汚れない）。
/// </remarks>
public partial class MainViewModel
{
    /// <summary>ラバーバンド（矩形選択）の表示状態のバッキングフィールド</summary>
    [ObservableProperty]
    private bool _isRubberBandVisible;

    /// <summary>ラバーバンド矩形の左上 X（キャンバス座標・px）</summary>
    [ObservableProperty]
    private double _rubberBandX;

    /// <summary>ラバーバンド矩形の左上 Y（キャンバス座標・px）</summary>
    [ObservableProperty]
    private double _rubberBandY;

    /// <summary>ラバーバンド矩形の幅（px）</summary>
    [ObservableProperty]
    private double _rubberBandWidth;

    /// <summary>ラバーバンド矩形の高さ（px）</summary>
    [ObservableProperty]
    private double _rubberBandHeight;

    /// <summary>現在選択中のエンティティ集合（<see cref="EntityViewModel.IsSelected"/> が正）</summary>
    /// <remarks>順序は <see cref="Entities"/> の並び順に従う。派生ヘルパのため都度評価する</remarks>
    public IReadOnlyList<EntityViewModel> SelectedEntities =>
        Entities.Where(e => e.IsSelected).ToList();

    /// <summary>エンティティが 2 個以上選択されているか（一括操作パネルへの切替条件）</summary>
    /// <remarks>
    /// 派生値のため素の getter とし、変更通知は選択操作の集約点
    /// （<see cref="NotifySelectionChanged"/>）から明示的に発火する。
    /// </remarks>
    public bool IsMultiSelectionActive => Entities.Count(e => e.IsSelected) >= 2;

    /// <summary>一括操作パネルの件数表示テキスト（例: "3 個選択中"）</summary>
    public string SelectedEntityCountText => $"{Entities.Count(e => e.IsSelected)} 個選択中";

    /// <summary>選択状態の変化を派生プロパティへ通知する（選択操作の各経路から呼ぶ集約点）</summary>
    /// <remarks>
    /// 選択の正は各 <see cref="EntityViewModel.IsSelected"/> フラグであり、
    /// <see cref="SelectedEntity"/> 変更通知だけでは既存選択への追加・除外（主選択が変わらないケース）を
    /// 拾えないため、選択を変化させる全経路（Toggle / SelectAll / Clear / ラバーバンド）から本メソッドを呼ぶ。
    /// </remarks>
    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedEntities));
        OnPropertyChanged(nameof(IsMultiSelectionActive));
        OnPropertyChanged(nameof(SelectedEntityCountText));
    }

    /// <summary>選択中の全エンティティのタイトル背景色を一括変更する（Undo は複合 1 エントリ）</summary>
    /// <param name="colorHex">適用するタイトル背景色（<see cref="EntityTitleColorOption.ColorHex"/> 相当）</param>
    /// <remarks>
    /// 色プロパティは <see cref="QuickER.UndoRedo.DiagramChangeTracker"/> が個別自動記録するため、
    /// 適用は <see cref="GroupChangeTitleColorCommand"/> 内で <c>RunWithoutTracking</c> し、
    /// 履歴には複合コマンドのみを Push する（履歴の分裂・二重登録を防ぐ）。
    /// 既に同色のメンバーは変更対象から外し、実変更が無ければ履歴を積まない。
    /// </remarks>
    [RelayCommand]
    private void BulkChangeTitleColor(string? colorHex)
    {
        if (string.IsNullOrEmpty(colorHex))
        {
            return;
        }

        var changes = SelectedEntities
            .Where(e => e.TitleBackgroundColor != colorHex)
            .Select(e => (Entity: e, OldColor: e.TitleBackgroundColor, NewColor: colorHex))
            .ToList();

        if (changes.Count == 0)
        {
            return;
        }

        var command = new GroupChangeTitleColorCommand(
            changes,
            action => _changeTracker.RunWithoutTracking(action)
        );
        UndoRedo.Execute(command);
    }

    /// <summary>指定エンティティの選択をトグル（追加／除外）する（Ctrl+クリック用）</summary>
    /// <remarks>
    /// リレーション選択とは排他とし、リレーション選択・保留リレーションはこの操作で解除する。
    /// 主選択（<see cref="SelectedEntity"/>）は、追加時は対象へ更新し、除外時は残る選択のいずれか
    /// （無ければ null）へ付け替える。
    /// </remarks>
    public void ToggleEntitySelection(EntityViewModel entity)
    {
        // エンティティ選択とリレーション選択は排他のため、リレーション側の選択を解除する
        foreach (var relationship in Relationships)
        {
            relationship.IsSelected = false;
        }

        SelectedRelationship = null;

        entity.IsSelected = !entity.IsSelected;

        if (entity.IsSelected)
        {
            // 追加したエンティティを主選択にする
            SelectedEntity = entity;
        }
        else if (SelectedEntity == entity)
        {
            // 主選択が外れた場合は残る選択のいずれかへ主選択を付け替える（無ければ null）
            SelectedEntity = Entities.FirstOrDefault(e => e.IsSelected);
        }

        // ToggleEntitySelection では主選択が変わらないケース（既存選択への追加・除外）も
        // あるため、SelectedEntity 変更通知に頼らず明示的にハイライトを再計算する
        UpdateRelatedHighlights();
    }

    /// <summary>全エンティティを選択する（Ctrl+A 用）</summary>
    [RelayCommand]
    private void SelectAllEntities()
    {
        foreach (var relationship in Relationships)
        {
            relationship.IsSelected = false;
        }

        SelectedRelationship = null;

        foreach (var entity in Entities)
        {
            entity.IsSelected = true;
        }

        // 主選択は末尾（最後に操作したとみなせる 1 個）へ寄せる
        SelectedEntity = Entities.LastOrDefault();

        UpdateRelatedHighlights();
    }

    /// <summary>全エンティティ選択を解除する（Esc・空白クリック用）</summary>
    /// <remarks>
    /// エンティティ選択のみを解除し、リレーション選択・保留リレーションモードには触れない。
    /// 空白クリックの全解除（<see cref="OnCanvasClicked"/>）とは異なり、リレーション作成中の
    /// 始点確定などを Esc で巻き戻さないための限定的な解除。
    /// </remarks>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var entity in Entities)
        {
            entity.IsSelected = false;
        }

        SelectedEntity = null;

        UpdateRelatedHighlights();
    }

    /// <summary>ラバーバンド矩形に触れた（intersect した）エンティティを選択する</summary>
    /// <param name="area">キャンバス座標系の選択矩形</param>
    /// <param name="additive"><c>true</c> なら既存選択へ追加、<c>false</c> なら置換する</param>
    /// <remarks>
    /// 判定は各エンティティの外接矩形（X / Y / Width / DisplayHeight）との交差＝「触れたら選択」。
    /// 完全包含は要求しない。リレーション選択とは排他とし、リレーション側の選択は解除する。
    /// </remarks>
    public void ApplyRubberBandSelection(Rect area, bool additive)
    {
        // エンティティ選択とリレーション選択は排他
        foreach (var relationship in Relationships)
        {
            relationship.IsSelected = false;
        }

        SelectedRelationship = null;

        EntityViewModel? lastHit = null;

        foreach (var entity in Entities)
        {
            var bounds = new Rect(entity.X, entity.Y, entity.Width, entity.DisplayHeight);
            var hit = area.IntersectsWith(bounds);

            if (hit)
            {
                entity.IsSelected = true;
                lastHit = entity;
            }
            else if (!additive)
            {
                // 非追加モードでは矩形外のエンティティの選択を落とす
                entity.IsSelected = false;
            }
        }

        // 主選択は今回触れたもののうち末尾を優先し、無ければ既存選択の何れかを維持する
        if (lastHit is not null)
        {
            SelectedEntity = lastHit;
        }
        else if (SelectedEntity is null || !SelectedEntity.IsSelected)
        {
            SelectedEntity = Entities.FirstOrDefault(e => e.IsSelected);
        }

        UpdateRelatedHighlights();
    }

    /// <summary>グループ移動のデルタをグループ剛体としてクランプする（純関数）</summary>
    /// <param name="minX">選択メンバーの最小 X 座標</param>
    /// <param name="minY">選択メンバーの最小 Y 座標</param>
    /// <param name="deltaX">適用したい X デルタ</param>
    /// <param name="deltaY">適用したい Y デルタ</param>
    /// <returns>いずれかのメンバーが 0 を割らないようクランプ済みのデルタ</returns>
    /// <remarks>
    /// グループを剛体として扱い、相対配置を保ったまま左端・上端が 0 に当たったら
    /// デルタごと止める（メンバー個別に 0 で丸めると相対配置が崩れるため）。
    /// </remarks>
    public static (double DeltaX, double DeltaY) ClampGroupDelta(
        double minX,
        double minY,
        double deltaX,
        double deltaY
    )
    {
        // 最小座標 + デルタが負にならないよう、デルタの下限を -最小座標へ引き上げる
        var clampedX = Math.Max(deltaX, -minX);
        var clampedY = Math.Max(deltaY, -minY);
        return (clampedX, clampedY);
    }
}
