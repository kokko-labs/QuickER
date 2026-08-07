using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>外部キーを構成する列ペア（宣言順）の正本</summary>
    /// <remarks>
    /// モデル（<see cref="Relationship.ColumnPairs"/>）と同じ表現をそのまま持ち、編集 UI の
    /// <see cref="ColumnPairRows"/>（親列＋子列のコンボボックス 1 行 ＝ 列ペア 1 組）は常にここから導出する
    /// </remarks>
    private readonly List<RelationshipColumnPair> _columnPairs;

    /// <summary>末尾に未確定の空スロット行を出しているかどうか（ビュー状態・モデル未反映）</summary>
    private bool _hasPendingSlot;

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

    /// <summary>参照先列として選択可能な起点側カラム一覧（主キー列 ∪ 一意制約の構成列）</summary>
    /// <remarks>
    /// 外部キーが参照できるのは親側の候補キー（PK または UNIQUE 制約）だけなので、その両方を候補とする
    /// （<see cref="ColumnViewModel.IsUniqueConstraintMember"/> は所有エンティティが制約の増減へ追従して立てる）
    /// </remarks>
    public IReadOnlyList<ColumnViewModel> AvailableSourceColumns =>
        Source.Columns.Where(c => c.IsPrimaryKey || c.IsUniqueConstraintMember).ToList();

    /// <summary>外部キー列として選択可能な終点側カラム一覧</summary>
    public IReadOnlyList<ColumnViewModel> AvailableTargetColumns => Target.Columns.ToList();

    /// <summary>外部キーを構成する列ペア（宣言順。複合外部キーは 2 組以上になる）</summary>
    /// <remarks>
    /// <b>この一覧の変更通知（<c>PropertyChanged</c>）は発行しない</b>。UI は行コレクション
    /// <see cref="ColumnPairRows"/> の増減で更新されるうえ、履歴化は専用コマンド
    /// （<c>ChangeRelationshipColumnPairsCommand</c>）に一本化しているため
    /// </remarks>
    public IReadOnlyList<RelationshipColumnPair> ColumnPairs => _columnPairs;

    /// <summary>列ペアの編集行（宣言順。末尾に空スロットを 1 行だけ持てる）</summary>
    public ObservableCollection<RelationshipColumnPairViewModel> ColumnPairRows { get; } = new();

    /// <summary>列ペアを 1 行追加できるかどうか（両側に未使用の候補列があり、空スロットが出ていない）</summary>
    public bool CanAddColumnPair =>
        !_hasPendingSlot
        && CanSelectForeignKeyColumns
        && AvailableSourceColumns.Count
            > ColumnPairRows.Count(row => row.SelectedSourceColumn is not null)
        && AvailableTargetColumns.Count
            > ColumnPairRows.Count(row => row.SelectedTargetColumn is not null);

    /// <summary>列ペア行の選択がユーザー操作で変わったときに発火する（履歴化は購読側の責務）</summary>
    /// <remarks>
    /// 未購読・未処理のまま戻ってきた場合は、正本と表示が食い違わないよう自前で確定させる
    /// （<see cref="MainViewModel"/> を伴わない VM 単体利用の経路。履歴には残らない）
    /// </remarks>
    internal event EventHandler<RelationshipColumnPairViewModel>? ColumnPairSelectionEdited;

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

        // 列ペアはモデルの正本をそのまま引き継ぐ（複製して外部の List と実体を共有しない）
        _columnPairs = model.ColumnPairs.Select(pair => pair.Clone()).ToList();
        _constraintName = model.ConstraintName;
        _onDelete = model.OnDelete;
        _onUpdate = model.OnUpdate;
        Source = source;
        Target = target;

        // 購読の確立は Attach() に一本化する（幾何の初期計算も Attach() が行う）
        Attach();

        EnsureColumnSelectionConsistency();
        EnsureReferentialActionConsistency();
        SyncColumnPairRows();
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

    /// <summary>主キー化・一意制約入り・カラム名の変更時に、選択候補と選択整合性を再評価する</summary>
    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(ColumnViewModel.IsPrimaryKey)
                or nameof(ColumnViewModel.IsUniqueConstraintMember)
                or nameof(ColumnViewModel.Name)
        )
        {
            NotifyColumnCandidatesChanged();
            EnsureColumnSelectionConsistency();
        }
    }

    /// <summary>選択候補プロパティの変更を通知し、各編集行の候補を絞り直す</summary>
    private void NotifyColumnCandidatesChanged()
    {
        OnPropertyChanged(nameof(AvailableSourceColumns));
        OnPropertyChanged(nameof(AvailableTargetColumns));

        RefreshColumnPairCandidates();
        OnPropertyChanged(nameof(CanAddColumnPair));
    }

    /// <summary>スナップショット適用中など列整合チェックを一時的に抑止するためのフラグ</summary>
    internal bool SuppressColumnSelectionConsistency { get; set; }

    /// <summary>現在の種別・両端カラムに矛盾する列ペアを解消する（多対多では全ペアをクリアする）</summary>
    /// <remarks>
    /// 落とす対象は「両端のどちらかが実在しない列を指すペア」だけに絞る。候補キー（PK / UNIQUE）から
    /// 外れただけのペアは残す＝列が生きている限りユーザーの指定を勝手に捨てない
    /// </remarks>
    private void EnsureColumnSelectionConsistency()
    {
        if (SuppressColumnSelectionConsistency)
        {
            return;
        }

        if (!CanSelectForeignKeyColumns)
        {
            if (_columnPairs.Count > 0)
            {
                SetColumnPairs([]);
            }

            return;
        }

        var survivors = _columnPairs
            .Where(pair =>
                Source.Columns.Any(column => column.Id == pair.SourceColumnId)
                && Target.Columns.Any(column => column.Id == pair.TargetColumnId)
            )
            .ToList();

        if (survivors.Count != _columnPairs.Count)
        {
            SetColumnPairs(survivors);
        }
    }

    /// <summary>列ペア一覧を丸ごと差し替える（Undo コマンド・整合処理からの唯一の適用点）</summary>
    /// <param name="columnPairs">新しい列ペア（宣言順）</param>
    internal void SetColumnPairs(IEnumerable<RelationshipColumnPair> columnPairs)
    {
        _columnPairs.Clear();
        _columnPairs.AddRange(columnPairs.Select(pair => pair.Clone()));

        // 正本が変わった時点で編集途中の空スロットは意味を失うため捨てる（Undo・AI ツール由来の変更も同じ扱い）
        _hasPendingSlot = false;
        SyncColumnPairRows();
    }

    /// <summary>現在の列ペアを複製して返す（履歴コマンドのスナップショット用）</summary>
    internal IReadOnlyList<RelationshipColumnPair> SnapshotColumnPairs() =>
        _columnPairs.Select(pair => pair.Clone()).ToList();

    /// <summary>2 つの列ペア一覧が並び順まで含めて等しいかどうかを判定する</summary>
    /// <remarks>履歴を汚さないための「実質的な変化なし」判定に用いる</remarks>
    internal static bool SameColumnPairs(
        IReadOnlyList<RelationshipColumnPair> left,
        IReadOnlyList<RelationshipColumnPair> right
    ) =>
        left.Count == right.Count
        && left.Zip(right)
            .All(entry =>
                entry.First.SourceColumnId == entry.Second.SourceColumnId
                && entry.First.TargetColumnId == entry.Second.TargetColumnId
            );

    /// <summary>末尾へ未確定の空スロット行を 1 つ追加する（ビュー状態のみ・履歴に残さない）</summary>
    internal void AddPendingColumnPairSlot()
    {
        if (!CanAddColumnPair)
        {
            return;
        }

        _hasPendingSlot = true;
        SyncColumnPairRows();
    }

    /// <summary>未確定の空スロット行を破棄する（モデル未反映のためビュー状態の取り消しで足りる）</summary>
    internal void CancelPendingColumnPairSlot()
    {
        if (!_hasPendingSlot)
        {
            return;
        }

        _hasPendingSlot = false;
        SyncColumnPairRows();
    }

    /// <summary>現在の編集行の選択から列ペア一覧（宣言順）を組み立てる</summary>
    /// <param name="excluded">除外する行（行削除の適用先を組み立てる場合に指定する）</param>
    /// <remarks>
    /// <b>両側が選ばれている行だけ</b>を採る。片側だけ選んだ行は外部キーとして意味を成さないため、
    /// もう片方が選ばれるまでビュー状態に留める（＝モデル・履歴へ反映しない）
    /// </remarks>
    internal IReadOnlyList<RelationshipColumnPair> BuildColumnPairsFromRows(
        RelationshipColumnPairViewModel? excluded = null
    ) =>
        ColumnPairRows
            .Where(row => !ReferenceEquals(row, excluded))
            .Where(row =>
                row.SelectedSourceColumn is not null && row.SelectedTargetColumn is not null
            )
            .Select(row => new RelationshipColumnPair(
                row.SelectedSourceColumn!.Id,
                row.SelectedTargetColumn!.Id
            ))
            .ToList();

    /// <summary>行の列選択がユーザー操作で変わったことを購読側（履歴化する側）へ伝える</summary>
    internal void NotifyColumnPairSelectionEdited(RelationshipColumnPairViewModel row)
    {
        // 選択が動いた時点で他行の候補（重複除外）が変わるため、まず候補だけ整える
        RefreshColumnPairCandidates();
        OnPropertyChanged(nameof(CanAddColumnPair));

        ColumnPairSelectionEdited?.Invoke(this, row);

        // 誰も履歴化しなかった場合でも、正本と表示の食い違いは残さない
        var derived = BuildColumnPairsFromRows();

        if (!SameColumnPairs(_columnPairs, derived))
        {
            SetColumnPairs(derived);
        }
    }

    /// <summary>正本と空スロットの有無から編集行を作り直す</summary>
    /// <remarks>
    /// 行の増減は末尾でのみ吸収し、既存の行インスタンスは使い回す。ItemsControl のコンテナ再生成を避けて、
    /// コンボボックスの選択操作の途中で行の実体が差し替わらないようにするため
    /// </remarks>
    private void SyncColumnPairRows()
    {
        // 解決できない Guid（想定外の壊れた参照）は行に出さない＝DDL 生成のスキップと同じ扱い
        var resolved = _columnPairs
            .Select(pair =>
                (
                    Source: Source.Columns.FirstOrDefault(column =>
                        column.Id == pair.SourceColumnId
                    ),
                    Target: Target.Columns.FirstOrDefault(column =>
                        column.Id == pair.TargetColumnId
                    )
                )
            )
            .Where(pair => pair.Source is not null && pair.Target is not null)
            .ToList();

        var desired = resolved.Count + (_hasPendingSlot ? 1 : 0);

        while (ColumnPairRows.Count > desired)
        {
            var removed = ColumnPairRows[^1];
            ColumnPairRows.RemoveAt(ColumnPairRows.Count - 1);

            // 外れた行のコンボボックスが後片付けの過程で選択を落としても、正本を触らせない
            removed.Detach();
        }

        while (ColumnPairRows.Count < desired)
        {
            ColumnPairRows.Add(new RelationshipColumnPairViewModel(this));
        }

        for (var i = 0; i < ColumnPairRows.Count; i++)
        {
            ColumnPairRows[i]
                .ApplySelection(
                    i < resolved.Count ? resolved[i].Source : null,
                    i < resolved.Count ? resolved[i].Target : null,
                    isPendingSlot: i >= resolved.Count
                );
        }

        RefreshColumnPairCandidates();
        OnPropertyChanged(nameof(CanAddColumnPair));
    }

    /// <summary>各行の選択候補を「他行が使っていない列＋自行の現在選択」へ絞り込む</summary>
    private void RefreshColumnPairCandidates()
    {
        var usedSources = ColumnPairRows
            .Select(row => row.SelectedSourceColumn)
            .Where(column => column is not null)
            .Select(column => column!)
            .ToHashSet();
        var usedTargets = ColumnPairRows
            .Select(row => row.SelectedTargetColumn)
            .Where(column => column is not null)
            .Select(column => column!)
            .ToHashSet();
        var sourceCandidates = AvailableSourceColumns;
        var targetCandidates = AvailableTargetColumns;

        foreach (var row in ColumnPairRows)
        {
            row.SyncAvailableSourceColumns(
                sourceCandidates.Where(column =>
                    !usedSources.Contains(column)
                    || ReferenceEquals(column, row.SelectedSourceColumn)
                )
            );
            row.SyncAvailableTargetColumns(
                targetCandidates.Where(column =>
                    !usedTargets.Contains(column)
                    || ReferenceEquals(column, row.SelectedTargetColumn)
                )
            );
        }
    }

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
        OnPropertyChanged(nameof(CanAddColumnPair));

        EnsureColumnSelectionConsistency();
        EnsureReferentialActionConsistency();

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
    /// <remarks>
    /// 列ペアは正本をそのまま複製して載せる（空リスト＝外部キー句を作らないリレーション）。
    /// 複合外部キーは 2 組以上のペアがそのまま往復する。
    /// </remarks>
    public Relationship ToModel() =>
        new()
        {
            Id = Id,
            SourceEntityId = Source.Id,
            TargetEntityId = Target.Id,
            Type = Type,
            ColumnPairs = _columnPairs.Select(pair => pair.Clone()).ToList(),
            ConstraintName = ConstraintName,
            OnDelete = OnDelete,
            OnUpdate = OnUpdate,
        };

    /// <summary>両端エンティティ・カラムへの購読状態（<see cref="Attach"/> / <see cref="Detach"/> の多重実行ガード）</summary>
    /// <remarks>
    /// 生成直後は購読済み（コンストラクタが <see cref="Attach"/> を呼ぶ）
    /// 図への追加通知（<c>CollectionChanged</c>）でも <see cref="Attach"/> が呼ばれるため、
    /// このフラグがないと「新規生成 → コレクションへ追加」の経路で購読が二重になり通知が 2 回走る
    /// </remarks>
    private bool _isAttached;

    /// <summary>両端エンティティ・カラムへの購読を確立する（図へ復帰したリレーションの端点追従を戻す）</summary>
    /// <remarks>
    /// <see cref="Detach"/> と対になる購読処理の単一正本で、購読済みなら何もしない（二重購読ガード）
    /// 購読が切れている間に端点が移動している可能性があるため、幾何を計算し直して通知する
    /// 列選択の整合化（<see cref="EnsureColumnSelectionConsistency"/>）はここでは行わない
    /// ＝ Undo による復元時に既存の列選択を消さないため（生成時のみコンストラクタが実行する）
    /// </remarks>
    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;

        Source.PropertyChanged += OnEndpointChanged;
        Source.Columns.CollectionChanged += OnColumnsCollectionChanged;

        foreach (var column in Source.Columns)
        {
            column.PropertyChanged += OnColumnPropertyChanged;
        }

        // 自己参照（Source と Target が同一インスタンス）では Target 側は同じ発生源のため購読しない
        // （張ると同一ハンドラが 2 本になり、端点移動 1 回で通知・幾何再計算が 2 回走る）
        if (!ReferenceEquals(Source, Target))
        {
            Target.PropertyChanged += OnEndpointChanged;
            Target.Columns.CollectionChanged += OnColumnsCollectionChanged;

            foreach (var column in Target.Columns)
            {
                column.PropertyChanged += OnColumnPropertyChanged;
            }
        }

        UpdateGeometry();
        NotifyGeometryChanged();
    }

    /// <summary>両端エンティティ・カラムへの購読をすべて解除する（画面リセットや破棄時のリーク防止）</summary>
    /// <remarks>未購読なら何もしない（<see cref="Attach"/> と対称の多重実行ガード）</remarks>
    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _isAttached = false;

        Source.PropertyChanged -= OnEndpointChanged;
        Source.Columns.CollectionChanged -= OnColumnsCollectionChanged;

        foreach (var column in Source.Columns)
        {
            column.PropertyChanged -= OnColumnPropertyChanged;
        }

        // 自己参照では Target 側を購読していないため、解除も Attach() と同じ条件で括る
        if (!ReferenceEquals(Source, Target))
        {
            Target.PropertyChanged -= OnEndpointChanged;
            Target.Columns.CollectionChanged -= OnColumnsCollectionChanged;

            foreach (var column in Target.Columns)
            {
                column.PropertyChanged -= OnColumnPropertyChanged;
            }
        }
    }
}

/// <summary>外部キーを構成する列ペア 1 組を表す編集行（親列＋子列のコンボボックス 1 行分）</summary>
/// <remarks>
/// 正本はリレーション側の列ペア一覧で、この行は導出表示にすぎない。
/// <see cref="SelectedSourceColumn"/> / <see cref="SelectedTargetColumn"/> のユーザー操作による変更だけを
/// リレーションへ通知し、正本からの反映（<see cref="ApplySelection"/>）では通知しない。
/// UNIQUE 制約の構成列行と違い <b>1 行に 2 つの選択が要る</b>ため、片側だけ選ばれた行は
/// 未確定（<see cref="IsPendingSlot"/>）のままモデルへ載らない。
/// </remarks>
public sealed class RelationshipColumnPairViewModel : ObservableObject
{
    /// <summary>この行が属するリレーション</summary>
    public RelationshipViewModel Relationship { get; }

    /// <summary>親列の選択候補（他行が使っていない候補キー列＋自行の現在選択）</summary>
    public ObservableCollection<ColumnViewModel> AvailableSourceColumns { get; } = new();

    /// <summary>子列の選択候補（他行が使っていない列＋自行の現在選択）</summary>
    public ObservableCollection<ColumnViewModel> AvailableTargetColumns { get; } = new();

    /// <summary>選択中の親（被参照）カラム</summary>
    private ColumnViewModel? _selectedSourceColumn;

    /// <summary>選択中の子（外部キー）カラム</summary>
    private ColumnViewModel? _selectedTargetColumn;

    /// <summary>正本からの反映中かどうか（この間の変更はリレーションへ通知しない）</summary>
    private bool _isApplyingModelState;

    /// <summary>行リストから外された後かどうか（外れた行の後片付けで正本を触らないためのガード）</summary>
    private bool _isDetached;

    /// <summary>まだ正本へ確定していない行かどうか</summary>
    private bool _isPendingSlot;

    /// <summary><see cref="RelationshipColumnPairViewModel"/> を生成する</summary>
    public RelationshipColumnPairViewModel(RelationshipViewModel relationship)
    {
        Relationship = relationship;
    }

    /// <summary>この行が指す親（被参照）カラム</summary>
    public ColumnViewModel? SelectedSourceColumn
    {
        get => _selectedSourceColumn;
        set
        {
            if (ReferenceEquals(_selectedSourceColumn, value))
            {
                return;
            }

            _selectedSourceColumn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSourceColumnUnselected));
            NotifySelectionEdited();
        }
    }

    /// <summary>この行が指す子（外部キー）カラム</summary>
    public ColumnViewModel? SelectedTargetColumn
    {
        get => _selectedTargetColumn;
        set
        {
            if (ReferenceEquals(_selectedTargetColumn, value))
            {
                return;
            }

            _selectedTargetColumn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTargetColumnUnselected));
            NotifySelectionEdited();
        }
    }

    /// <summary>まだ正本へ確定していない行かどうか（＋で足した空スロット。× は履歴を残さず破棄する）</summary>
    public bool IsPendingSlot => _isPendingSlot;

    /// <summary>親列が未選択かどうか（プレースホルダー表示の判定）</summary>
    public bool IsSourceColumnUnselected => _selectedSourceColumn is null;

    /// <summary>子列が未選択かどうか（プレースホルダー表示の判定）</summary>
    public bool IsTargetColumnUnselected => _selectedTargetColumn is null;

    /// <summary>ユーザー操作による選択変更をリレーションへ通知する（正本反映中・切り離し後は何もしない）</summary>
    private void NotifySelectionEdited()
    {
        if (_isApplyingModelState || _isDetached)
        {
            return;
        }

        Relationship.NotifyColumnPairSelectionEdited(this);
    }

    /// <summary>正本の列ペアをこの行へ反映する（リレーションへの通知は行わない）</summary>
    internal void ApplySelection(
        ColumnViewModel? sourceColumn,
        ColumnViewModel? targetColumn,
        bool isPendingSlot
    )
    {
        _isApplyingModelState = true;

        try
        {
            SelectedSourceColumn = sourceColumn;
            SelectedTargetColumn = targetColumn;
        }
        finally
        {
            _isApplyingModelState = false;
        }

        if (_isPendingSlot != isPendingSlot)
        {
            _isPendingSlot = isPendingSlot;
            OnPropertyChanged(nameof(IsPendingSlot));
        }
    }

    /// <summary>この行を行リストから外れたものとして無効化する</summary>
    internal void Detach() => _isDetached = true;

    /// <summary>親列の選択候補を差分だけ入れ替える</summary>
    internal void SyncAvailableSourceColumns(IEnumerable<ColumnViewModel> columns) =>
        SyncCandidates(AvailableSourceColumns, columns);

    /// <summary>子列の選択候補を差分だけ入れ替える</summary>
    internal void SyncAvailableTargetColumns(IEnumerable<ColumnViewModel> columns) =>
        SyncCandidates(AvailableTargetColumns, columns);

    /// <summary>選択候補コレクションを差分だけ入れ替える</summary>
    /// <remarks>
    /// ItemsSource を丸ごと差し替えるとコンボボックスが選択を落とすため、実際に増減した項目だけを反映する
    /// （並びは所有エンティティのカラム順）
    /// </remarks>
    private static void SyncCandidates(
        ObservableCollection<ColumnViewModel> candidates,
        IEnumerable<ColumnViewModel> columns
    )
    {
        var desired = columns.ToList();

        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(candidates[i]))
            {
                candidates.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i >= candidates.Count)
            {
                candidates.Add(desired[i]);
            }
            else if (!ReferenceEquals(candidates[i], desired[i]))
            {
                candidates.Insert(i, desired[i]);
            }
        }
    }
}
