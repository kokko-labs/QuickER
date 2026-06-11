using System.Collections.Generic;
using System.Linq;

namespace ERDesigner.Services;

/// <summary>AI 更新差分の対象カテゴリ</summary>
public enum AiUpdateDiffCategory
{
    /// <summary>テーブル差分</summary>
    Table,

    /// <summary>カラム差分</summary>
    Column,

    /// <summary>リレーション差分</summary>
    Relationship,
}

/// <summary>AI 更新差分の変更種別</summary>
public enum AiUpdateDiffChangeType
{
    /// <summary>追加</summary>
    Add,

    /// <summary>変更</summary>
    Modify,

    /// <summary>削除</summary>
    Remove,
}

/// <summary>Before / After の比較表示に使う 1 行</summary>
public sealed class AiUpdateDiffDetailRow
{
    /// <summary>項目名</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>変更前の値</summary>
    public string Before { get; init; } = string.Empty;

    /// <summary>変更後の値</summary>
    public string After { get; init; } = string.Empty;
}

/// <summary>差分一覧に表示する 1 件の変更</summary>
public sealed class AiUpdateDiffItem
{
    /// <summary>対象カテゴリ</summary>
    public AiUpdateDiffCategory Category { get; init; }

    /// <summary>変更種別</summary>
    public AiUpdateDiffChangeType ChangeType { get; init; }

    /// <summary>一覧表示用のラベル</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>右ペイン見出し用のタイトル</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>変更前後を対比する差分詳細</summary>
    public IReadOnlyList<AiUpdateDiffDetailRow> Details { get; init; } = [];

    /// <summary>変更種別の表示名</summary>
    public string ChangeTypeLabel =>
        ChangeType switch
        {
            AiUpdateDiffChangeType.Add => "追加",
            AiUpdateDiffChangeType.Remove => "削除",
            _ => "変更",
        };

    /// <summary>カテゴリの表示名</summary>
    public string CategoryLabel =>
        Category switch
        {
            AiUpdateDiffCategory.Table => "テーブル",
            AiUpdateDiffCategory.Column => "カラム",
            _ => "リレーション",
        };
}

/// <summary>TreeView 表示用の差分グループ</summary>
public sealed class AiUpdateDiffGroup
{
    /// <summary>グループ名</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>グループ内の差分一覧</summary>
    public List<AiUpdateDiffItem> Items { get; } = new();

    /// <summary>件数を付与した表示用タイトル</summary>
    public string DisplayTitle => $"{Title} ({Items.Count})";
}

/// <summary>AI 更新差分の計算結果</summary>
public sealed class AiUpdateDiffResult
{
    /// <summary>TreeView 表示用のグループ一覧</summary>
    public List<AiUpdateDiffGroup> Groups { get; } = new();

    /// <summary>全グループ合計の差分件数</summary>
    public int TotalChanges => Groups.Sum(group => group.Items.Count);

    /// <summary>差分が 1 件以上あるかどうか</summary>
    public bool HasChanges => TotalChanges > 0;
}
