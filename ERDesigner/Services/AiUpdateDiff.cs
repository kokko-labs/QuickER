using System.Collections.Generic;
using System.Linq;

namespace ERDesigner.Services;

/// <summary>
/// AI 更新差分のカテゴリです。
/// </summary>
public enum AiUpdateDiffCategory
{
    /// <summary>テーブル差分です。</summary>
    Table,

    /// <summary>カラム差分です。</summary>
    Column,

    /// <summary>リレーション差分です。</summary>
    Relationship,
}

/// <summary>
/// AI 更新差分の変更種別です。
/// </summary>
public enum AiUpdateDiffChangeType
{
    /// <summary>追加です。</summary>
    Add,

    /// <summary>変更です。</summary>
    Modify,

    /// <summary>削除です。</summary>
    Remove,
}

/// <summary>
/// Before / After の比較表示に使う 1 行です。
/// </summary>
public sealed class AiUpdateDiffDetailRow
{
    /// <summary>項目名です。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>変更前の値です。</summary>
    public string Before { get; init; } = string.Empty;

    /// <summary>変更後の値です。</summary>
    public string After { get; init; } = string.Empty;
}

/// <summary>
/// 差分一覧に表示する 1 件の変更です。
/// </summary>
public sealed class AiUpdateDiffItem
{
    /// <summary>カテゴリです。</summary>
    public AiUpdateDiffCategory Category { get; init; }

    /// <summary>変更種別です。</summary>
    public AiUpdateDiffChangeType ChangeType { get; init; }

    /// <summary>一覧表示用のラベルです。</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>右ペイン見出し用のタイトルです。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>差分詳細です。</summary>
    public IReadOnlyList<AiUpdateDiffDetailRow> Details { get; init; } = [];

    /// <summary>変更種別の表示名です。</summary>
    public string ChangeTypeLabel =>
        ChangeType switch
        {
            AiUpdateDiffChangeType.Add => "追加",
            AiUpdateDiffChangeType.Remove => "削除",
            _ => "変更",
        };

    /// <summary>カテゴリの表示名です。</summary>
    public string CategoryLabel =>
        Category switch
        {
            AiUpdateDiffCategory.Table => "テーブル",
            AiUpdateDiffCategory.Column => "カラム",
            _ => "リレーション",
        };
}

/// <summary>
/// TreeView 用の差分グループです。
/// </summary>
public sealed class AiUpdateDiffGroup
{
    /// <summary>グループ名です。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>グループ内の差分一覧です。</summary>
    public List<AiUpdateDiffItem> Items { get; } = new();

    /// <summary>表示用の件数付きタイトルです。</summary>
    public string DisplayTitle => $"{Title} ({Items.Count})";
}

/// <summary>
/// AI 更新差分の計算結果です。
/// </summary>
public sealed class AiUpdateDiffResult
{
    /// <summary>TreeView 表示用のグループ一覧です。</summary>
    public List<AiUpdateDiffGroup> Groups { get; } = new();

    /// <summary>差分件数です。</summary>
    public int TotalChanges => Groups.Sum(group => group.Items.Count);

    /// <summary>差分があるかどうかです。</summary>
    public bool HasChanges => TotalChanges > 0;
}
