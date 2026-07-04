using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Model;

namespace QuickER.ViewModels;

/// <summary>2 つのエンティティを接続するリレーションの ViewModel</summary>
/// <remarks>
/// 両端エンティティの位置変更を購読して線の端点を自動追従させる
/// 線本体 (X1,Y1)-(X2,Y2) と両端マーカー描画用の補助プロパティ
/// (<see cref="SourceMarker"/> / <see cref="TargetMarker"/>) を提供する
/// マーカー描画領域は端点（エンティティ境界）から内側へ <see cref="MarkerSize"/> 分の範囲とし、
/// 領域の外端（ローカル X = MarkerSize）が境界に一致する 鳥の足の先端をエンティティ枠へ接地させる IE 記法準拠の配置
/// </remarks>
public partial class RelationshipViewModel : ObservableObject
{
    /// <summary>端点マーカーの描画サイズ (px)</summary>
    private const double MarkerSize = 24;

    /// <summary>自己参照ループの描画サイズ (px)</summary>
    private const double SelfLoopSize = 56;

    /// <summary>自己参照リレーションのラベルを右へ寄せる補正量 (px)</summary>
    private const double SelfLoopLabelOffsetX = 10;

    /// <summary>マーカー中心を端点から内側へ置く距離 (px) 描画領域の外端が境界に接する</summary>
    private const double MarkerOffset = MarkerSize / 2;

    /// <summary>モデルと同一の識別子</summary>
    public Guid Id { get; }

    /// <summary>リレーション種別</summary>
    [ObservableProperty]
    private RelationshipType _type;

    /// <summary>起点エンティティ側の参照先カラム ID</summary>
    [ObservableProperty]
    private Guid? _sourceColumnId;

    /// <summary>終点エンティティ側の外部キーカラム ID</summary>
    [ObservableProperty]
    private Guid? _targetColumnId;

    /// <summary>外部キー制約名</summary>
    [ObservableProperty]
    private string? _constraintName;

    /// <summary>親行削除時の参照アクション</summary>
    [ObservableProperty]
    private ForeignKeyReferentialAction _onDelete;

    /// <summary>親キー更新時の参照アクション</summary>
    [ObservableProperty]
    private ForeignKeyReferentialAction _onUpdate;

    /// <summary>選択中かどうか</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>関連ハイライトで減光表示するかどうか（選択状態から導出する純粋な表示状態）</summary>
    /// <remarks>
    /// 他要素の選択によって関連から外れた際に不透明度を下げるためのフラグ
    /// 選択状態から <see cref="MainViewModel"/> が再計算して設定するため、Undo 履歴・保存対象には含めない
    /// </remarks>
    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>関連ハイライトで線を強調表示するかどうか（選択エンティティに接続された線に付与する）</summary>
    /// <remarks>
    /// 選択状態から <see cref="MainViewModel"/> が再計算して設定するため、Undo 履歴・保存対象には含めない
    /// 選択リレーション本体の青強調（IsSelected）より弱い強調を表す
    /// </remarks>
    [ObservableProperty]
    private bool _isEmphasized;

    /// <summary>リレーションの起点エンティティ（参照される PK 側）</summary>
    public EntityViewModel Source { get; }

    /// <summary>リレーションの終点エンティティ（外部キーを持つ側）</summary>
    public EntityViewModel Target { get; }

    /// <summary>参照先列として選択可能な起点側カラム一覧（主キー列のみ）</summary>
    public IReadOnlyList<ColumnViewModel> AvailableSourceColumns =>
        Source.Columns.Where(c => c.IsPrimaryKey).ToList();

    /// <summary>外部キー列として選択可能な終点側カラム一覧</summary>
    public IReadOnlyList<ColumnViewModel> AvailableTargetColumns => Target.Columns.ToList();

    /// <summary>UI 表示用の参照アクション候補一覧</summary>
    public IReadOnlyList<ForeignKeyReferentialAction> ReferentialActions { get; } =
    [
        ForeignKeyReferentialAction.NoAction,
        ForeignKeyReferentialAction.Cascade,
        ForeignKeyReferentialAction.SetNull,
        ForeignKeyReferentialAction.SetDefault,
    ];

    /// <summary>外部キー列の選択が有効なリレーション種別かどうか（多対多以外で有効）</summary>
    public bool CanSelectForeignKeyColumns => Type != RelationshipType.ManyToMany;

    /// <summary>参照アクションの設定が有効なリレーション種別かどうか（多対多以外で有効）</summary>
    public bool CanConfigureReferentialActions => Type != RelationshipType.ManyToMany;

    /// <summary>モデルと両端エンティティから ViewModel を生成し、端点追従と整合性確保を初期化する</summary>
    public RelationshipViewModel(Relationship model, EntityViewModel source, EntityViewModel target)
    {
        Id = model.Id;
        _type = model.Type;
        _sourceColumnId = model.SourceColumnId;
        _targetColumnId = model.TargetColumnId;
        _constraintName = model.ConstraintName;
        _onDelete = model.OnDelete;
        _onUpdate = model.OnUpdate;
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

        UpdateGeometry();
        EnsureColumnSelectionConsistency();
        EnsureReferentialActionConsistency();
    }

    /// <summary>両端カラムの増減に追従し、購読の着脱と候補・整合性の再評価を行う</summary>
    private void OnColumnsCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e
    )
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

    /// <summary>多対多では参照アクションを既定値へ戻し、設定不可状態を保つ</summary>
    private void EnsureReferentialActionConsistency()
    {
        if (!CanConfigureReferentialActions)
        {
            OnDelete = ForeignKeyReferentialAction.NoAction;
            OnUpdate = ForeignKeyReferentialAction.NoAction;
        }
    }

    /// <summary>主キー化やカラム名の変更時に、選択候補と選択整合性を再評価する</summary>
    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnViewModel.IsPrimaryKey) or nameof(ColumnViewModel.Name))
        {
            NotifyColumnCandidatesChanged();
            EnsureColumnSelectionConsistency();
        }
    }

    /// <summary>選択候補プロパティの変更を通知する</summary>
    private void NotifyColumnCandidatesChanged()
    {
        OnPropertyChanged(nameof(AvailableSourceColumns));
        OnPropertyChanged(nameof(AvailableTargetColumns));
    }

    /// <summary>スナップショット適用中など列整合チェックを一時的に抑止するためのフラグ</summary>
    internal bool SuppressColumnSelectionConsistency { get; set; }

    /// <summary>種別変更に伴う列選択の連動更新中かどうか</summary>
    internal bool IsUpdatingType { get; private set; }

    /// <summary>現在の種別・候補に矛盾する列選択を解消する（多対多では選択をクリアする）</summary>
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

    /// <summary>参照先カラム ID 変更時に再通知する（バインディング更新を確実にする）</summary>
    partial void OnSourceColumnIdChanged(Guid? value) => OnPropertyChanged(nameof(SourceColumnId));

    /// <summary>外部キーカラム ID 変更時に再通知する（バインディング更新を確実にする）</summary>
    partial void OnTargetColumnIdChanged(Guid? value) => OnPropertyChanged(nameof(TargetColumnId));

    /// <summary>両端の位置・サイズ変更時に幾何情報を再計算して通知する</summary>
    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(EntityViewModel.X)
                or nameof(EntityViewModel.Y)
                or nameof(EntityViewModel.Width)
                or nameof(EntityViewModel.DisplayHeight)
        )
        {
            UpdateGeometry();
            NotifyGeometryChanged();
        }
    }

    /// <summary>変化しうる幾何プロパティをまとめて再通知する</summary>
    /// <remarks>
    /// Source / Target は readonly で不変のため、それらにのみ依存する不変プロパティ
    /// (IsSelfRelationship / ShowSelfLoop / ShowEndpointMarkers / SelfLoopWidth / SelfLoopHeight) は
    /// 通知対象から除外し、無駄なバインディング再評価を避ける
    /// </remarks>
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
        OnPropertyChanged(nameof(SelfLoopLeft));
        OnPropertyChanged(nameof(SelfLoopTop));
    }

    /// <summary>種別変更直前に発火する（変更前の列選択スナップショット取得用フック）</summary>
    internal event EventHandler? TypeChanging;

    /// <summary>列整合処理を含む連動変更の完了後に発火する（Undo 記録制御に用いる）</summary>
    internal event EventHandler? TypeChangeCompleted;

    /// <summary>種別変更開始を記録し、変更直前フックを発火する</summary>
    partial void OnTypeChanging(RelationshipType value)
    {
        IsUpdatingType = true;
        TypeChanging?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>種別変更後にラベル・マーカー・選択可否を再通知し、列と参照アクションの整合性を確保する</summary>
    partial void OnTypeChanged(RelationshipType value)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(SourceMarker));
        OnPropertyChanged(nameof(TargetMarker));
        OnPropertyChanged(nameof(CanSelectForeignKeyColumns));
        OnPropertyChanged(nameof(CanConfigureReferentialActions));

        EnsureColumnSelectionConsistency();
        EnsureReferentialActionConsistency();
        IsUpdatingType = false;

        // 全連動変更完了後に通知する
        TypeChangeCompleted?.Invoke(this, EventArgs.Empty);
    }

    // ===== 端点座標 =====
    // 端点ごとに DisplayHeight 取得や三角関数を毎回呼ぶとドラッグ中のコストが大きいため、
    // 端点変更時に UpdateGeometry() で一括計算した値をフィールドへ保持し、getter はキャッシュを返すだけとする

    /// <summary>計算済みの端点・マーカー・ラベル座標および角度のキャッシュフィールド群</summary>
    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;
    private double _sourceMarkerX;
    private double _sourceMarkerY;
    private double _targetMarkerX;
    private double _targetMarkerY;
    private double _sourceMarkerAngle;
    private double _targetMarkerAngle;
    private double _labelX;
    private double _labelY;
    private double _selfLoopLeft;
    private double _selfLoopTop;

    /// <summary>起点エンティティ境界上の接続 X 座標</summary>
    public double X1 => _x1;

    /// <summary>起点エンティティ境界上の接続 Y 座標</summary>
    public double Y1 => _y1;

    /// <summary>終点エンティティ境界上の接続 X 座標</summary>
    public double X2 => _x2;

    /// <summary>終点エンティティ境界上の接続 Y 座標</summary>
    public double Y2 => _y2;

    /// <summary>端点座標・マーカー位置・角度・ラベル位置を一括で再計算しキャッシュへ格納する</summary>
    private void UpdateGeometry()
    {
        (_x1, _y1) = GetBoundaryPoint(Source, Target);
        (_x2, _y2) = GetBoundaryPoint(Target, Source);

        // 線方向の単位ベクトル（マーカー中心を端点から線の内側へ MarkerOffset 移動させるのに使用）
        double ux;
        double uy;

        if (IsSelfRelationship)
        {
            (ux, uy) = (1, 0);
        }
        else
        {
            var dx = _x2 - _x1;
            var dy = _y2 - _y1;
            var len = Math.Sqrt(dx * dx + dy * dy);

            if (len < 0.0001)
            {
                len = 1;
            }

            (ux, uy) = (dx / len, dy / len);
        }

        _sourceMarkerX = _x1 + ux * MarkerOffset;
        _sourceMarkerY = _y1 + uy * MarkerOffset;
        _targetMarkerX = _x2 - ux * MarkerOffset;
        _targetMarkerY = _y2 - uy * MarkerOffset;
        _sourceMarkerAngle = Math.Atan2(uy, ux) * 180.0 / Math.PI + 180;
        _targetMarkerAngle = Math.Atan2(uy, ux) * 180.0 / Math.PI;
        _selfLoopLeft = Source.X + Source.Width - 20;
        _selfLoopTop = Source.Y - SelfLoopSize / 2;
        _labelX = IsSelfRelationship
            ? _selfLoopLeft + SelfLoopWidth / 2 + SelfLoopLabelOffsetX
            : (_x1 + _x2) / 2;
        _labelY = IsSelfRelationship ? _selfLoopTop + SelfLoopHeight / 2 : (_y1 + _y2) / 2;
    }

    /// <summary>エンティティ中心から相手方向へ伸ばした線と矩形境界の交点を返す</summary>
    /// <remarks>自己参照時は右辺中央を返す</remarks>
    private static (double x, double y) GetBoundaryPoint(
        EntityViewModel source,
        EntityViewModel target
    )
    {
        if (ReferenceEquals(source, target))
        {
            return (source.X + source.Width, source.Y + source.DisplayHeight / 2);
        }

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

        var scaleX =
            Math.Abs(dx) < 0.0001 ? double.PositiveInfinity : (source.Width / 2) / Math.Abs(dx);
        var scaleY =
            Math.Abs(dy) < 0.0001
                ? double.PositiveInfinity
                : (source.DisplayHeight / 2) / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);

        return (sourceCenterX + dx * scale, sourceCenterY + dy * scale);
    }

    /// <summary>起点側マーカーの中心 X</summary>
    public double SourceMarkerX => _sourceMarkerX;

    /// <summary>起点側マーカー描画領域の左上 X</summary>
    public double SourceMarkerLeft => SourceMarkerX - MarkerSize / 2;

    /// <summary>起点側マーカーの中心 Y</summary>
    public double SourceMarkerY => _sourceMarkerY;

    /// <summary>起点側マーカー描画領域の左上 Y</summary>
    public double SourceMarkerTop => SourceMarkerY - MarkerSize / 2;

    /// <summary>終点側マーカーの中心 X</summary>
    public double TargetMarkerX => _targetMarkerX;

    /// <summary>終点側マーカー描画領域の左上 X</summary>
    public double TargetMarkerLeft => TargetMarkerX - MarkerSize / 2;

    /// <summary>終点側マーカーの中心 Y</summary>
    public double TargetMarkerY => _targetMarkerY;

    /// <summary>終点側マーカー描画領域の左上 Y</summary>
    public double TargetMarkerTop => TargetMarkerY - MarkerSize / 2;

    /// <summary>起点マーカーをエンティティ側へ向けて回転させる角度（度）</summary>
    public double SourceMarkerAngle => _sourceMarkerAngle;

    /// <summary>終点マーカーをエンティティ側へ向けて回転させる角度（度）</summary>
    public double TargetMarkerAngle => _targetMarkerAngle;

    /// <summary>線の中点 X（ラベル表示用）</summary>
    public double LabelX => _labelX;

    /// <summary>線の中点 Y（ラベル表示用）</summary>
    public double LabelY => _labelY;

    /// <summary>自己参照リレーションかどうか</summary>
    public bool IsSelfRelationship => Source.Id == Target.Id;

    /// <summary>自己参照リレーション描画用のループを表示するかどうか</summary>
    public bool ShowSelfLoop => IsSelfRelationship;

    /// <summary>端点マーカーを表示するかどうか</summary>
    public bool ShowEndpointMarkers => !IsSelfRelationship;

    /// <summary>自己参照ループの左上 X 座標</summary>
    public double SelfLoopLeft => _selfLoopLeft;

    /// <summary>自己参照ループの左上 Y 座標</summary>
    public double SelfLoopTop => _selfLoopTop;

    /// <summary>自己参照ループの幅</summary>
    public double SelfLoopWidth => SelfLoopSize;

    /// <summary>自己参照ループの高さ</summary>
    public double SelfLoopHeight => SelfLoopSize;

    // ===== 種別ごとのマーカー種類 =====

    /// <summary>端点マーカーの種類</summary>
    public enum MarkerKind
    {
        /// <summary>「1」を表す（短い縦棒）</summary>
        One,

        /// <summary>「多」を表す（鳥の足 crow's foot）</summary>
        Many,
    }

    /// <summary>起点側マーカーの種類</summary>
    public MarkerKind SourceMarker =>
        Type switch
        {
            RelationshipType.OneToOne => MarkerKind.One,
            RelationshipType.OneToMany => MarkerKind.One,
            RelationshipType.ManyToMany => MarkerKind.Many,
            _ => MarkerKind.One,
        };

    /// <summary>終点側マーカーの種類</summary>
    public MarkerKind TargetMarker =>
        Type switch
        {
            RelationshipType.OneToOne => MarkerKind.One,
            RelationshipType.OneToMany => MarkerKind.Many,
            RelationshipType.ManyToMany => MarkerKind.Many,
            _ => MarkerKind.One,
        };

    /// <summary>線上に表示するラベルテキスト（種別に応じた多重度表記）</summary>
    public string Label =>
        Type switch
        {
            RelationshipType.OneToOne => "1―1",
            RelationshipType.OneToMany => "1―N",
            RelationshipType.ManyToMany => "N―N",
            _ => string.Empty,
        };

    /// <summary>現在の状態をモデルへコピーして返す</summary>
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
            OnDelete = OnDelete,
            OnUpdate = OnUpdate,
        };

    /// <summary>両端エンティティ・カラムへの購読をすべて解除する（画面リセットや破棄時のリーク防止）</summary>
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
