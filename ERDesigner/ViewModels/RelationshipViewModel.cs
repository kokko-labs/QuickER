using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ERDesigner.Models;

namespace ERDesigner.ViewModels;

/// <summary>
/// 2 つのエンティティを接続するリレーションの ViewModel です。
/// エンティティの位置変更を購読し、線の端点を自動追従させます。
/// </summary>
/// <remarks>
/// 線本体（X1,Y1）-(X2,Y2) と、両端のマーカーを描くための補助プロパティ
/// (<see cref="SourceMarker"/> / <see cref="TargetMarker"/>) を提供します。
/// マーカーの位置は <see cref="MarkerOffset"/> 分だけ端点から内側にずらした座標です。
/// </remarks>
public partial class RelationshipViewModel : ObservableObject
{
    /// <summary>端点マーカーの描画サイズ (px)。</summary>
    private const double MarkerSize = 20;

    /// <summary>端点マーカーがエンティティ外側へ離れる余白 (px)。</summary>
    private const double MarkerGap = 4;

    /// <summary>マーカー中心が端点から外側へ置かれる距離 (px)。</summary>
    private const double MarkerOffset = MarkerSize / 2 + MarkerGap;

    /// <summary>モデルと同じ ID。</summary>
    public Guid Id { get; }

    /// <summary>関連の種類。</summary>
    [ObservableProperty]
    private RelationshipType _type;

    /// <summary>起点エンティティ側の参照先カラム ID。</summary>
    [ObservableProperty]
    private Guid? _sourceColumnId;

    /// <summary>終点エンティティ側の外部キーカラム ID。</summary>
    [ObservableProperty]
    private Guid? _targetColumnId;

    /// <summary>外部キー制約名。</summary>
    [ObservableProperty]
    private string? _constraintName;

    /// <summary>選択中かどうか。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>関連の起点となるエンティティ。</summary>
    public EntityViewModel Source { get; }

    /// <summary>関連の終点となるエンティティ。</summary>
    public EntityViewModel Target { get; }

    /// <summary>参照先列として選択可能な起点側カラム一覧です。</summary>
    public IReadOnlyList<ColumnViewModel> AvailableSourceColumns => Source.Columns.Where(c => c.IsPrimaryKey).ToList();

    /// <summary>外部キー列として選択可能な終点側カラム一覧です。</summary>
    public IReadOnlyList<ColumnViewModel> AvailableTargetColumns => Target.Columns.ToList();

    /// <summary>列選択が有効なリレーション種別かどうかです。</summary>
    public bool CanSelectForeignKeyColumns => Type != RelationshipType.ManyToMany;

    /// <summary>モデルと両端のエンティティから ViewModel を生成します。</summary>
    public RelationshipViewModel(Relationship model, EntityViewModel source, EntityViewModel target)
    {
        Id = model.Id;
        _type = model.Type;
        _sourceColumnId = model.SourceColumnId;
        _targetColumnId = model.TargetColumnId;
        _constraintName = model.ConstraintName;
        Source = source;
        Target = target;

        Source.PropertyChanged += OnEndpointChanged;
        Target.PropertyChanged += OnEndpointChanged;

        Source.Columns.CollectionChanged += OnColumnsCollectionChanged;
        Target.Columns.CollectionChanged += OnColumnsCollectionChanged;

        foreach (var column in Source.Columns)
        {
            column.PropertyChanged += OnColumnPropertyChanged;
        }

        foreach (var column in Target.Columns)
        {
            column.PropertyChanged += OnColumnPropertyChanged;
        }

        EnsureColumnSelectionConsistency();
    }

    private void OnColumnsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ColumnViewModel column in e.OldItems)
            {
                column.PropertyChanged -= OnColumnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ColumnViewModel column in e.NewItems)
            {
                column.PropertyChanged += OnColumnPropertyChanged;
            }
        }

        NotifyColumnCandidatesChanged();
        EnsureColumnSelectionConsistency();
    }

    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnViewModel.IsPrimaryKey) or nameof(ColumnViewModel.Name))
        {
            NotifyColumnCandidatesChanged();
            EnsureColumnSelectionConsistency();
        }
    }

    private void NotifyColumnCandidatesChanged()
    {
        OnPropertyChanged(nameof(AvailableSourceColumns));
        OnPropertyChanged(nameof(AvailableTargetColumns));
    }

    /// <summary>スナップショット適用中など、列整合チェックを一時的にスキップするためのフラグです。</summary>
    internal bool SuppressColumnSelectionConsistency { get; set; }

    private void EnsureColumnSelectionConsistency()
    {
        if (SuppressColumnSelectionConsistency)
        {
            return;
        }

        if (!CanSelectForeignKeyColumns)
        {
            SourceColumnId = null;
            TargetColumnId = null;
            return;
        }

        if (SourceColumnId is not null && AvailableSourceColumns.All(c => c.Id != SourceColumnId))
        {
            SourceColumnId = null;
        }

        if (TargetColumnId is not null && AvailableTargetColumns.All(c => c.Id != TargetColumnId))
        {
            TargetColumnId = null;
        }
    }

    partial void OnSourceColumnIdChanged(Guid? value) => OnPropertyChanged(nameof(SourceColumnId));

    partial void OnTargetColumnIdChanged(Guid? value) => OnPropertyChanged(nameof(TargetColumnId));

    /// <summary>両端の位置・幅が変わったら端点プロパティを再通知します。</summary>
    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntityViewModel.X) or nameof(EntityViewModel.Y) or nameof(EntityViewModel.Width) or nameof(EntityViewModel.DisplayHeight))
        {
            NotifyGeometryChanged();
        }
    }

    /// <summary>幾何プロパティをまとめて再通知します。</summary>
    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(SourceMarkerLeft));
        OnPropertyChanged(nameof(SourceMarkerTop));
        OnPropertyChanged(nameof(SourceMarkerX));
        OnPropertyChanged(nameof(SourceMarkerY));
        OnPropertyChanged(nameof(TargetMarkerLeft));
        OnPropertyChanged(nameof(TargetMarkerTop));
        OnPropertyChanged(nameof(TargetMarkerX));
        OnPropertyChanged(nameof(TargetMarkerY));
        OnPropertyChanged(nameof(SourceMarkerAngle));
        OnPropertyChanged(nameof(TargetMarkerAngle));
        OnPropertyChanged(nameof(LabelX));
        OnPropertyChanged(nameof(LabelY));
    }

    /// <summary>種別変更前の SourceColumnId/TargetColumnId を MainViewModel 側でキャプチャできるよう、変更直前に通知します。</summary>
    internal event EventHandler? TypeChanging;

    /// <summary>EnsureColumnSelectionConsistency を含む全連動変更完了後に発火します。MainViewModel の記録制御に使います。</summary>
    internal event EventHandler? TypeChangeCompleted;

    /// <summary>種別が変わったらラベルとマーカー種別を再通知します。</summary>
    partial void OnTypeChanging(RelationshipType value)
    {
        TypeChanging?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>種別が変わったらラベルとマーカー種別を再通知します。</summary>
    partial void OnTypeChanged(RelationshipType value)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(SourceMarker));
        OnPropertyChanged(nameof(TargetMarker));
        OnPropertyChanged(nameof(CanSelectForeignKeyColumns));

        EnsureColumnSelectionConsistency();

        // 全連動変更完了後に通知する
        TypeChangeCompleted?.Invoke(this, EventArgs.Empty);
    }

    // ===== 端点座標 =====

    /// <summary>起点エンティティ境界上の接続 X 座標。</summary>
    public double X1 => GetBoundaryPoint(Source, Target).x;

    /// <summary>起点エンティティ境界上の接続 Y 座標。</summary>
    public double Y1 => GetBoundaryPoint(Source, Target).y;

    /// <summary>終点エンティティ境界上の接続 X 座標。</summary>
    public double X2 => GetBoundaryPoint(Target, Source).x;

    /// <summary>終点エンティティ境界上の接続 Y 座標。</summary>
    public double Y2 => GetBoundaryPoint(Target, Source).y;

    // ===== マーカー位置（端点からエンティティ外側へ MarkerOffset 移動した点） =====

    private (double dx, double dy, double len) Direction()
    {
        var dx = X2 - X1;
        var dy = Y2 - Y1;
        var len = Math.Sqrt(dx * dx + dy * dy);

        if (len < 0.0001)
        {
            len = 1;
        }

        return (dx / len, dy / len, len);
    }

    /// <summary>エンティティ中心から相手方向へ伸ばした線と境界の交点を返します。</summary>
    private static (double x, double y) GetBoundaryPoint(EntityViewModel source, EntityViewModel target)
    {
        var sourceCenterX = source.X + source.Width / 2;
        var sourceCenterY = source.Y + source.DisplayHeight / 2;
        var targetCenterX = target.X + target.Width / 2;
        var targetCenterY = target.Y + target.DisplayHeight / 2;

        var dx = targetCenterX - sourceCenterX;
        var dy = targetCenterY - sourceCenterY;

        if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001)
        {
            return (sourceCenterX, sourceCenterY);
        }

        var scaleX = Math.Abs(dx) < 0.0001 ? double.PositiveInfinity : (source.Width / 2) / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.0001 ? double.PositiveInfinity : (source.DisplayHeight / 2) / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);

        return (sourceCenterX + dx * scale, sourceCenterY + dy * scale);
    }

    /// <summary>起点側マーカーの中心 X。</summary>
    public double SourceMarkerX
    {
        get
        {
            var (ux, _, _) = Direction();
            return X1 + ux * MarkerOffset;
        }
    }

    /// <summary>起点側マーカー描画領域の左上 X。</summary>
    public double SourceMarkerLeft => SourceMarkerX - MarkerSize / 2;

    /// <summary>起点側マーカーの中心 Y。</summary>
    public double SourceMarkerY
    {
        get
        {
            var (_, uy, _) = Direction();
            return Y1 + uy * MarkerOffset;
        }
    }

    /// <summary>起点側マーカー描画領域の左上 Y。</summary>
    public double SourceMarkerTop => SourceMarkerY - MarkerSize / 2;

    /// <summary>終点側マーカーの中心 X。</summary>
    public double TargetMarkerX
    {
        get
        {
            var (ux, _, _) = Direction();
            return X2 - ux * MarkerOffset;
        }
    }

    /// <summary>終点側マーカー描画領域の左上 X。</summary>
    public double TargetMarkerLeft => TargetMarkerX - MarkerSize / 2;

    /// <summary>終点側マーカーの中心 Y。</summary>
    public double TargetMarkerY
    {
        get
        {
            var (_, uy, _) = Direction();
            return Y2 - uy * MarkerOffset;
        }
    }

    /// <summary>終点側マーカー描画領域の左上 Y。</summary>
    public double TargetMarkerTop => TargetMarkerY - MarkerSize / 2;

    /// <summary>起点マーカーをエンティティ側へ向けて回転させる角度（度）。</summary>
    public double SourceMarkerAngle
    {
        get
        {
            var (ux, uy, _) = Direction();
            return Math.Atan2(uy, ux) * 180.0 / Math.PI + 180;
        }
    }

    /// <summary>終点マーカーをエンティティ側へ向けて回転させる角度（度）。</summary>
    public double TargetMarkerAngle
    {
        get
        {
            var (ux, uy, _) = Direction();
            return Math.Atan2(uy, ux) * 180.0 / Math.PI;
        }
    }

    /// <summary>線の中点 X（ラベル表示用）。</summary>
    public double LabelX => (X1 + X2) / 2;

    /// <summary>線の中点 Y（ラベル表示用）。</summary>
    public double LabelY => (Y1 + Y2) / 2;

    // ===== 種別ごとのマーカー種類 =====

    /// <summary>
    /// 端点マーカーの種類。
    /// </summary>
    public enum MarkerKind
    {
        /// <summary>「1」を表す（短い縦棒）。</summary>
        One,

        /// <summary>「多」を表す（鳥の足: crow's foot）。</summary>
        Many,
    }

    /// <summary>起点側マーカーの種類。</summary>
    public MarkerKind SourceMarker =>
        Type switch
        {
            RelationshipType.OneToOne => MarkerKind.One,
            RelationshipType.OneToMany => MarkerKind.One,
            RelationshipType.ManyToMany => MarkerKind.Many,
            _ => MarkerKind.One,
        };

    /// <summary>終点側マーカーの種類。</summary>
    public MarkerKind TargetMarker =>
        Type switch
        {
            RelationshipType.OneToOne => MarkerKind.One,
            RelationshipType.OneToMany => MarkerKind.Many,
            RelationshipType.ManyToMany => MarkerKind.Many,
            _ => MarkerKind.One,
        };

    /// <summary>線上に表示するラベルテキスト。</summary>
    public string Label =>
        Type switch
        {
            RelationshipType.OneToOne => "1―1",
            RelationshipType.OneToMany => "1―N",
            RelationshipType.ManyToMany => "N―N",
            _ => string.Empty,
        };

    /// <summary>現在の状態をモデルにコピーして返します。</summary>
    public Relationship ToModel() =>
        new()
        {
            Id = Id,
            SourceEntityId = Source.Id,
            TargetEntityId = Target.Id,
            Type = Type,
            SourceColumnId = SourceColumnId,
            TargetColumnId = TargetColumnId,
            ConstraintName = ConstraintName,
        };

    /// <summary>両端の PropertyChanged 購読を解除します（画面リセット時など）。</summary>
    public void Detach()
    {
        Source.PropertyChanged -= OnEndpointChanged;
        Target.PropertyChanged -= OnEndpointChanged;
        Source.Columns.CollectionChanged -= OnColumnsCollectionChanged;
        Target.Columns.CollectionChanged -= OnColumnsCollectionChanged;

        foreach (var column in Source.Columns)
        {
            column.PropertyChanged -= OnColumnPropertyChanged;
        }

        foreach (var column in Target.Columns)
        {
            column.PropertyChanged -= OnColumnPropertyChanged;
        }
    }
}
