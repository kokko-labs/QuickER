using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;

namespace QuickER.ViewModels;

/// <summary>テーブルに定義された一意制約（<c>UNIQUE</c>）1 件をプロパティパネルへ仲介する ViewModel</summary>
/// <remarks>
/// <para>
/// 構成列の正本は宣言順の <see cref="ColumnIds"/>（カラム Guid の並び）で、チェックの ON/OFF は
/// <see cref="ColumnCandidates"/> の各項目から <see cref="MainViewModel"/> のコマンド経由で
/// Undo 可能な差し替えとして適用する（項目側は参加状態を持たず、常に正本から導出する）。
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

    /// <summary>制約名（空文字＝未設定＝DDL 生成時に列構成から合成する）</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>構成列の候補（所有エンティティの全カラム。チェックの ON/OFF で構成列を編集する）</summary>
    public ObservableCollection<UniqueConstraintColumnViewModel> ColumnCandidates { get; } = new();

    /// <summary>構成列の Guid 一覧（宣言順）</summary>
    public IReadOnlyList<Guid> ColumnIds => _columnIds;

    /// <summary>構成列が変化したときに所有エンティティへ通知するイベント</summary>
    internal event EventHandler? ColumnIdsChanged;

    /// <summary>意味モデルから ViewModel を生成する</summary>
    /// <param name="owner">この制約を保持するエンティティ</param>
    /// <param name="model">コピー元の <see cref="UniqueConstraint"/> モデル</param>
    public UniqueConstraintViewModel(EntityViewModel owner, UniqueConstraint model)
    {
        _owner = owner;
        Id = model.Id;
        _name = model.Name ?? string.Empty;
        _columnIds = new List<Guid>(model.ColumnIds);

        SyncColumnCandidates();
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

    /// <summary>構成列の一覧表示（宣言順のカンマ区切り。未選択時は案内文言）</summary>
    public string ColumnSummary
    {
        get
        {
            var names = MemberColumnNames;

            return names.Count == 0
                ? Strings.Property_UniqueConstraintNoColumns
                : string.Join(", ", names);
        }
    }

    /// <summary>指定カラムがこの制約の構成列かどうかを返す</summary>
    public bool ContainsColumn(Guid columnId) => _columnIds.Contains(columnId);

    /// <summary>構成列を丸ごと差し替える（Undo コマンドからの適用点）</summary>
    /// <param name="columnIds">新しい構成列の Guid（宣言順）</param>
    internal void SetColumnIds(IEnumerable<Guid> columnIds)
    {
        _columnIds.Clear();
        _columnIds.AddRange(columnIds);
        NotifyMembershipChanged();
    }

    /// <summary>所有エンティティのカラム増減・並び替えに合わせて構成列候補を作り直す</summary>
    /// <remarks>候補は参加状態を持たない（<see cref="ContainsColumn"/> から導出する）ため、単純な作り直しで足りる</remarks>
    internal void SyncColumnCandidates()
    {
        ColumnCandidates.Clear();

        foreach (var column in _owner.Columns)
        {
            ColumnCandidates.Add(new UniqueConstraintColumnViewModel(this, column));
        }

        NotifyMembershipChanged();
    }

    /// <summary>構成列に依存する表示（候補のチェック・一覧・合成名）を更新し、所有エンティティへ通知する</summary>
    private void NotifyMembershipChanged()
    {
        foreach (var candidate in ColumnCandidates)
        {
            candidate.NotifyMembershipChanged();
        }

        OnPropertyChanged(nameof(ColumnSummary));
        OnPropertyChanged(nameof(ResolvedName));
        ColumnIdsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>テーブル名・カラム名の変更を表示（構成列一覧・合成名）へ反映する</summary>
    internal void NotifyNamesChanged()
    {
        OnPropertyChanged(nameof(ColumnSummary));
        OnPropertyChanged(nameof(ResolvedName));
    }

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

/// <summary>一意制約の構成列候補 1 件（カラム 1 つと、その制約への参加状態）</summary>
/// <remarks>参加状態は制約側の構成列一覧から導出する読み取り専用（切替は Undo 可能なコマンド経由）</remarks>
public sealed class UniqueConstraintColumnViewModel : ObservableObject
{
    /// <summary>この候補が属する一意制約</summary>
    public UniqueConstraintViewModel Constraint { get; }

    /// <summary>候補となるカラム（名前の表示はこのカラムを直接束縛するためリネームに追従する）</summary>
    public ColumnViewModel Column { get; }

    /// <summary><see cref="UniqueConstraintColumnViewModel"/> を生成する</summary>
    public UniqueConstraintColumnViewModel(
        UniqueConstraintViewModel constraint,
        ColumnViewModel column
    )
    {
        Constraint = constraint;
        Column = column;
    }

    /// <summary>このカラムが制約の構成列かどうか</summary>
    public bool IsMember => Constraint.ContainsColumn(Column.Id);

    /// <summary>参加状態の変更を通知する（制約側の構成列変更に追従する）</summary>
    internal void NotifyMembershipChanged() => OnPropertyChanged(nameof(IsMember));
}
