using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.ViewModels;

/// <summary>テーブルに定義された一意制約（<c>UNIQUE</c>）1 件をプロパティパネルへ仲介する ViewModel</summary>
/// <remarks>
/// <para>
/// 構成列の正本は宣言順の <see cref="ColumnIds"/>（カラム Guid の並び）で、編集 UI の
/// <see cref="Members"/>（列選択コンボボックス 1 行 ＝ 構成列 1 つ）は常にそこから導出する。
/// 行の選択変更・削除は <see cref="MainViewModel"/> のコマンド経由で Undo 可能な差し替えとして適用される。
/// </para>
/// <para>
/// 行リストは末尾に「まだ列を選んでいない空スロット」を 1 つだけ持てる（＋ボタンで追加するビュー状態で、
/// モデルには反映しない）。ユーザーが列を選んだ時点で正本へ確定し、正本が外から変わったときは破棄する。
/// </para>
/// <para>
/// 表示名（<see cref="ResolvedName"/>）は DDL 生成と同じ合成規則
/// （<see cref="UniqueConstraintNaming.Resolve"/>）を共有する。ただし識別子の安全化は方言ごとに
/// 異なるためプレビューでは行わない（実際に出力される名前と記号の置換だけが食い違いうる）。
/// </para>
/// </remarks>
public partial class UniqueConstraintViewModel : ObservableObject
{
    /// <summary>モデルと同一の識別子</summary>
    public Guid Id { get; }

    /// <summary>この制約を保持するエンティティ（構成列候補・合成名の解決に用いる）</summary>
    private readonly EntityViewModel _owner;

    /// <summary>構成列の Guid（宣言順。表示・DDL 出力の列並びを表す）</summary>
    private readonly List<Guid> _columnIds;

    /// <summary>末尾に未選択の空スロット行を出しているかどうか（ビュー状態・モデル未反映）</summary>
    private bool _hasPendingSlot;

    /// <summary>制約名（空文字＝未設定＝DDL 生成時に列構成から合成する）</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>構成列の編集行（宣言順。末尾に空スロットを 1 行だけ持てる）</summary>
    public ObservableCollection<UniqueConstraintMemberViewModel> Members { get; } = new();

    /// <summary>構成列の Guid 一覧（宣言順）</summary>
    public IReadOnlyList<Guid> ColumnIds => _columnIds;

    /// <summary>構成列が変化したときに所有エンティティへ通知するイベント</summary>
    internal event EventHandler? ColumnIdsChanged;

    /// <summary>行の列選択がユーザー操作で変わったときに発火する（履歴化は購読側の責務）</summary>
    /// <remarks>
    /// 未購読・未処理のまま戻ってきた場合は、正本と表示が食い違わないよう自前で確定させる
    /// （<see cref="MainViewModel"/> を伴わない VM 単体利用の経路。履歴には残らない）
    /// </remarks>
    internal event EventHandler<UniqueConstraintMemberViewModel>? MemberSelectionEdited;

    /// <summary>意味モデルから ViewModel を生成する</summary>
    /// <param name="owner">この制約を保持するエンティティ</param>
    /// <param name="model">コピー元の <see cref="UniqueConstraint"/> モデル</param>
    public UniqueConstraintViewModel(EntityViewModel owner, UniqueConstraint model)
    {
        _owner = owner;
        Id = model.Id;
        _name = model.Name ?? string.Empty;
        _columnIds = new List<Guid>(model.ColumnIds);

        SyncMembers();
    }

    /// <summary>制約名の変更時に、DDL 上の名前プレビューを更新する</summary>
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(ResolvedName));

    /// <summary>DDL へ出力される制約名（未設定なら列構成から合成した名前）</summary>
    public string ResolvedName =>
        UniqueConstraintNaming.Resolve(
            string.IsNullOrWhiteSpace(Name) ? null : Name,
            _owner.TableName,
            MemberColumnNames,
            identifier => identifier
        );

    /// <summary>構成列の名前（宣言順）。解決できない Guid は読み飛ばす</summary>
    private IReadOnlyList<string> MemberColumnNames =>
        _columnIds
            .Select(columnId => _owner.Columns.FirstOrDefault(column => column.Id == columnId))
            .Where(column => column is not null)
            .Select(column => column!.Name)
            .ToList();

    /// <summary>構成列を 1 行追加できるかどうか（未使用の列があり、空スロットが出ていない）</summary>
    public bool CanAddMember =>
        !_hasPendingSlot
        && _owner.Columns.Count > Members.Count(member => member.SelectedColumn is not null);

    /// <summary>指定カラムがこの制約の構成列かどうかを返す</summary>
    public bool ContainsColumn(Guid columnId) => _columnIds.Contains(columnId);

    /// <summary>構成列を丸ごと差し替える（Undo コマンドからの適用点）</summary>
    /// <param name="columnIds">新しい構成列の Guid（宣言順）</param>
    internal void SetColumnIds(IEnumerable<Guid> columnIds)
    {
        _columnIds.Clear();
        _columnIds.AddRange(columnIds);

        // 正本が変わった時点で編集途中の空スロットは意味を失うため捨てる（Undo・MCP 由来の変更も同じ扱い）
        _hasPendingSlot = false;
        NotifyMembershipChanged();
    }

    /// <summary>所有エンティティのカラム増減・並び替えに合わせて編集行と候補を作り直す</summary>
    /// <remarks>行は正本（<see cref="ColumnIds"/>）からの導出のため、単純な作り直しで足りる</remarks>
    internal void SyncColumns()
    {
        NotifyMembershipChanged();
    }

    /// <summary>末尾へ未選択の空スロット行を 1 つ追加する（ビュー状態のみ・履歴に残さない）</summary>
    internal void AddPendingSlot()
    {
        if (!CanAddMember)
        {
            return;
        }

        _hasPendingSlot = true;
        SyncMembers();
    }

    /// <summary>未選択の空スロット行を破棄する（モデル未反映のためビュー状態の取り消しで足りる）</summary>
    internal void CancelPendingSlot()
    {
        if (!_hasPendingSlot)
        {
            return;
        }

        _hasPendingSlot = false;
        SyncMembers();
    }

    /// <summary>現在の編集行の選択から構成列 Guid 一覧（宣言順）を組み立てる</summary>
    /// <param name="excluded">除外する行（行削除の適用先を組み立てる場合に指定する）</param>
    internal IReadOnlyList<Guid> BuildColumnIdsFromMembers(
        UniqueConstraintMemberViewModel? excluded = null
    ) =>
        Members
            .Where(member => !ReferenceEquals(member, excluded))
            .Select(member => member.SelectedColumn)
            .Where(column => column is not null)
            .Select(column => column!.Id)
            .ToList();

    /// <summary>行の列選択がユーザー操作で変わったことを購読側（履歴化する側）へ伝える</summary>
    internal void NotifyMemberSelectionEdited(UniqueConstraintMemberViewModel member)
    {
        // 選択が動いた時点で他行の候補（重複除外）が変わるため、まず候補だけ整える
        RefreshMemberCandidates();
        OnPropertyChanged(nameof(CanAddMember));

        MemberSelectionEdited?.Invoke(this, member);

        // 誰も履歴化しなかった場合でも、正本と表示の食い違いは残さない
        var derived = BuildColumnIdsFromMembers();

        if (!_columnIds.SequenceEqual(derived))
        {
            SetColumnIds(derived);
        }
    }

    /// <summary>構成列に依存する表示（編集行・合成名）を更新し、所有エンティティへ通知する</summary>
    private void NotifyMembershipChanged()
    {
        SyncMembers();

        OnPropertyChanged(nameof(ResolvedName));
        ColumnIdsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>正本と空スロットの有無から編集行を作り直す</summary>
    /// <remarks>
    /// 行の増減は末尾でのみ吸収し、既存の行インスタンスは使い回す。ItemsControl のコンテナ再生成を避けて、
    /// コンボボックスの選択操作の途中で行の実体が差し替わらないようにするため
    /// </remarks>
    private void SyncMembers()
    {
        // 解決できない Guid（想定外の壊れた参照）は行に出さない＝DDL 生成のスキップと同じ扱い
        var columns = _columnIds
            .Select(columnId => _owner.Columns.FirstOrDefault(column => column.Id == columnId))
            .Where(column => column is not null)
            .Select(column => column!)
            .ToList();

        var desired = columns.Count + (_hasPendingSlot ? 1 : 0);

        while (Members.Count > desired)
        {
            var removed = Members[^1];
            Members.RemoveAt(Members.Count - 1);

            // 外れた行のコンボボックスが後片付けの過程で選択を落としても、正本を触らせない
            removed.Detach();
        }

        while (Members.Count < desired)
        {
            Members.Add(new UniqueConstraintMemberViewModel(this));
        }

        for (var i = 0; i < Members.Count; i++)
        {
            Members[i].ApplySelection(i < columns.Count ? columns[i] : null);
        }

        RefreshMemberCandidates();
        OnPropertyChanged(nameof(CanAddMember));
    }

    /// <summary>各行の選択候補を「同じ制約の他行が使っていない列＋自行の現在選択」へ絞り込む</summary>
    private void RefreshMemberCandidates()
    {
        var used = Members
            .Select(member => member.SelectedColumn)
            .Where(column => column is not null)
            .Select(column => column!)
            .ToHashSet();

        foreach (var member in Members)
        {
            member.SyncAvailableColumns(
                _owner.Columns.Where(column =>
                    !used.Contains(column) || ReferenceEquals(column, member.SelectedColumn)
                )
            );
        }
    }

    /// <summary>テーブル名・カラム名の変更を表示（合成名）へ反映する</summary>
    /// <remarks>行のコンボボックスは <see cref="ColumnViewModel"/> を直接項目にしているためリネームへ自動追従する</remarks>
    internal void NotifyNamesChanged() => OnPropertyChanged(nameof(ResolvedName));

    /// <summary>現在の状態を意味モデルへコピーして返す</summary>
    /// <remarks>制約名の空文字は「未設定」を意味するため <c>null</c> へ戻す（モデルの表現に合わせる）</remarks>
    public UniqueConstraint ToModel() =>
        new()
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? null : Name,
            ColumnIds = new List<Guid>(_columnIds),
        };
}

/// <summary>一意制約の構成列 1 つを表す編集行（列選択コンボボックス 1 行分）</summary>
/// <remarks>
/// 正本は制約側の構成列一覧で、この行は導出表示にすぎない。<see cref="SelectedColumn"/> のユーザー操作による
/// 変更だけを制約へ通知し、正本からの反映（<see cref="ApplySelection"/>）では通知しない
/// </remarks>
public sealed class UniqueConstraintMemberViewModel : ObservableObject
{
    /// <summary>この行が属する一意制約</summary>
    public UniqueConstraintViewModel Constraint { get; }

    /// <summary>選択候補（他行が使っていない列＋自行の現在選択。制約側が絞り込んで供給する）</summary>
    public ObservableCollection<ColumnViewModel> AvailableColumns { get; } = new();

    /// <summary>選択中のカラム（<c>null</c>＝まだ列を選んでいない空スロット）</summary>
    private ColumnViewModel? _selectedColumn;

    /// <summary>正本からの反映中かどうか（この間の変更は制約へ通知しない）</summary>
    private bool _isApplyingModelState;

    /// <summary>行リストから外された後かどうか（外れた行の後片付けで正本を触らないためのガード）</summary>
    private bool _isDetached;

    /// <summary><see cref="UniqueConstraintMemberViewModel"/> を生成する</summary>
    public UniqueConstraintMemberViewModel(UniqueConstraintViewModel constraint)
    {
        Constraint = constraint;
    }

    /// <summary>この行が指す構成列（コンボボックスの選択項目）</summary>
    public ColumnViewModel? SelectedColumn
    {
        get => _selectedColumn;
        set
        {
            if (ReferenceEquals(_selectedColumn, value))
            {
                return;
            }

            _selectedColumn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPendingSlot));

            if (_isApplyingModelState || _isDetached)
            {
                return;
            }

            Constraint.NotifyMemberSelectionEdited(this);
        }
    }

    /// <summary>まだ列を選んでいない空スロット行かどうか（プレースホルダー表示の判定）</summary>
    public bool IsPendingSlot => _selectedColumn is null;

    /// <summary>正本の構成列をこの行へ反映する（制約への通知は行わない）</summary>
    internal void ApplySelection(ColumnViewModel? column)
    {
        _isApplyingModelState = true;

        try
        {
            SelectedColumn = column;
        }
        finally
        {
            _isApplyingModelState = false;
        }
    }

    /// <summary>この行を行リストから外れたものとして無効化する</summary>
    internal void Detach() => _isDetached = true;

    /// <summary>選択候補を差分だけ入れ替える</summary>
    /// <remarks>
    /// ItemsSource を丸ごと差し替えるとコンボボックスが選択を落とすため、実際に増減した項目だけを反映する
    /// （並びは所有エンティティのカラム順）
    /// </remarks>
    internal void SyncAvailableColumns(IEnumerable<ColumnViewModel> columns)
    {
        var desired = columns.ToList();

        for (var i = AvailableColumns.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(AvailableColumns[i]))
            {
                AvailableColumns.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i >= AvailableColumns.Count)
            {
                AvailableColumns.Add(desired[i]);
            }
            else if (!ReferenceEquals(AvailableColumns[i], desired[i]))
            {
                AvailableColumns.Insert(i, desired[i]);
            }
        }
    }
}
